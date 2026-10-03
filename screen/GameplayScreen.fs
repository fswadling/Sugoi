module GameplayScreen

// Gameplay screen: hosts the editor workspace and the 3D scene (grid + actor
// meshes). Input is gathered once by the engine and delivered via `Frame`; this
// screen consumes it (no per-screen input system).

open System
open System.Numerics
open SDL3
open Screen
open Entity
open Texture

let private updateAspectRatio (width: float32, height: float32) (state: byref<GameplayState.GameplayState>) : unit =
    let aspect = if height > 0.0f then width / height else state.Camera.Aspect
    state.Camera.Aspect <- aspect

// Re-sync the camera aspect on switch-in so the grid and picking agree
// immediately. Input edge detection is engine-owned now, so there's no
// per-screen input state left to re-sync.
let activate (_shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    let size = ImGui.displaySize()
    do updateAspectRatio (size.x, size.y) &state

// Advance each actor's own animation clock in place: the alive scan reads the map's
// slot span, then each payload is resolved through `Get` so only the frame float is written.
let private advanceActorAnimations (dt: float32) (zone: Zone.Zone) : unit =
    let slots = zone.Actors.Slots
    for i in 0 .. slots.Length - 1 do
        let slot = &slots.[i]
        if not slot.Alive then () else
        let actor = &slot.Value
        do ActorRender.advanceInPlace &actor.Renderer dt

// Advance non-render logic: refresh the camera aspect from the frame's window size, then hand the
// frame's input to the editor (which owns the edit/play toggle, shortcuts, nudge, interaction, and
// camera). The editor does not simulate, so it stays passive: the animation clock, the dialogue
// dismiss and the player systems run only while playing. The play chain and the physics step
// therefore also run only while playing; each player system no-ops without a live bound player, so a
// play session with no player still advances the world but drives no character.
let update (shared: SharedState.SharedState) (frame: Frame) (state: byref<GameplayState.GameplayState>) : ScreenTransition =
    do updateAspectRatio (frame.WindowW, frame.WindowH) &state
    let zone = GameplayState.activeZone state
    let transition = Editor.update shared frame &state

    // Async environment loading (spec 053): on the render thread, refresh the pin set to the active
    // zone's environment (never dropped, never evicted), drain decoded payloads and advance the
    // budgeted GPU build, then finish an environment that landed for the active zone. Runs every
    // frame, in edit and play alike.
    let pinned =
        match (GameplayState.activeZone state).EnvironmentState with
        | Zone.Active environment | Zone.Awaiting environment -> [ environment.AssetId ]
        | Zone.NoEnvironment -> []
    do Assets.setPinned shared.Assets pinned
    do Assets.pump shared.Assets Assets.UploadBudgetMs
    do Zone.reconcileEnvironment (GameplayState.activeZone state) shared.Physics shared.Assets

    if not state.Editing then
        // The animation clock advances only while playing, so an actor stands still while it is being
        // placed and edited.
        do advanceActorAnimations frame.Dt zone

        // The dialogue dismiss: while playing only, so the editor's SPACE is its own. Gated on a text
        // field having focus too, because the editor's fields take the same keys (FR-008).
        do Systems.dismissDialogue (Input.isPressed Input.Key.Space frame.Input) (ImGui.wantTextInput()) &state.Dialogue
        do Systems.playerInput shared.Physics zone frame.Input frame.Dt
        do Physics.step shared.Physics frame.Dt
        do Systems.syncActors shared.Physics zone
        do Systems.updateTriggers zone &state.InteractableTrigger &state.Cue
        // Interaction (spec 058): the tracked interactable trigger's authored cue runs on the press edge of the
        // interact key — not on entry, and not once per frame while held.
        if Input.isPressed Input.Key.E frame.Input then
            do Systems.forkTriggerCue zone state.InteractableTrigger &state.Cue
        do Systems.cameraFollow zone &state.Follow &state.Camera
        do Systems.animate zone frame.Input
        // The cue advances last (research D5), on play time: an op's effect is what the next frame's
        // systems see, and the clock is frozen while the editor is open, so Play resumes a cue rather
        // than leaping it forward (FR-002). There is no start — the first play tick begins whatever
        // cue is set.
        state.PlayTime <- state.PlayTime + float frame.Dt
        CueEngine.tick shared &state state.PlayTime

    transition

// The prompt of the nearest interactable trigger the player is inside, or "" for nothing to show.
// `Systems.updateTriggers` (spec 029) already chose that trigger; this reads its authored text. An
// explicit match, not `ValueOption.bind`, so nothing closes over `zone` and nothing allocates
// (FR-010); the text is used as authored, not trimmed (FR-002). A dialogue line takes the screen
// (FR-012), and the prompt is a play-mode read-out, so it is silent while editing (FR-001).
let private promptText (state: GameplayState.GameplayState) (zone: Zone.Zone) : string =
    if state.Editing || state.Dialogue.Shown then ""
    else
        match state.InteractableTrigger with
        | ValueSome triggerRef ->
            match Zone.tryTriggerAt zone triggerRef with
            | ValueSome trigger -> trigger.State.Prompt
            | ValueNone -> ""
        | ValueNone -> ""

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : ScreenTransition =
    let transition = Editor.draw shared &state
    do Viewport.drawGrid &state
    // The game's 2D layer's batch is built here, where the screen state is mutable, and
    // uploaded/drawn in `renderScene` (the `DebugDraw` pattern). Its two consumers compose: the
    // dialogue panel, then the interact prompt below it. With nothing shown the batch stays empty,
    // so the frame is the pre-feature frame (FR-003, FR-009).
    let viewport = ImGui.displaySize()
    do Ui.beginBatch &state.Ui viewport.x viewport.y
    do DialogueRender.draw shared.Assets shared.Cast viewport.x viewport.y &state.Ui state.Dialogue
    do PromptRender.draw (promptText state (GameplayState.activeZone state)) viewport.x viewport.y &state.Ui

    if state.Editing then
        do Viewport.buildLines &state
    else
        // Markers/gizmos only accumulate while editing; clear them so a
        // switch to play mode doesn't linger the previous frame's overlay.
        state.GizmoDraw <- DebugDraw.clear state.GizmoDraw

    transition

// Render the 3D scene: upload the grid + gizmo line vertices (copy passes),
// then begin the scene render pass (clearing color + depth), draw the grid, the
// props (placed assets + imported environment parts), the actors, and finally
// the gizmo/marker lines (depth-tested so they are occluded by geometry in
// front of them).
let renderScene (shared: SharedState.SharedState) (state: inref<GameplayState.GameplayState>) (pass: ScenePass) : unit =
    // ── per-frame buffers ──
    // Gather the static instances (CPU), then upload every per-frame buffer in copy
    // passes before the render pass.
    let zone = GameplayState.activeZone state
    do Zone.gatherStatic shared.Gpu.Device zone
    do EnvironmentBatch.upload shared.Gpu.Device zone.Batch pass.Cmd
    do DebugDraw.upload state.DebugDraw pass.Cmd
    do DebugDraw.upload state.GizmoDraw pass.Cmd
    do Ui.upload state.Ui pass.Cmd

    // Upload each actor's bone palette (copy pass) before the render pass.
    zone.Actors.IteriAlive(fun _ _ (a: Actor) ->
        ActorRender.upload a.Renderer pass.Cmd)

    // ── scene pass ──
    let mutable depthTarget = pass.DepthTarget
    let scenePass = SDL.BeginGPURenderPass(pass.Cmd, ReadOnlySpan<SDL.GPUColorTargetInfo>(pass.ColorTargets), 1u, &depthTarget)

    let viewProjection = Camera.viewProjection state.Camera
    do DebugDraw.draw state.DebugDraw viewProjection pass.Cmd scenePass

    // The scene's one sun, resolved from the active zone's authored settings (spec 043) instead of
    // a constant, so a zone lights its own scene.
    let light = Texture.toLight zone.Light
    let cameraPos = state.Camera.Position

    // Static scene: props (placed assets + imported environment parts) batched into
    // one instanced draw per distinct submesh, then the animated actors.
    do EnvironmentBatch.render zone.Batch scenePass pass.Cmd shared.Gpu.StaticPipeline viewProjection light cameraPos

    let celPipeline = shared.Gpu.CelPipeline
    let outlinePipeline = shared.Gpu.OutlinePipeline
    do zone.Actors.IteriAlive(fun _ _ (a: Actor) ->
           let model = Entity.modelMatrix a.State.Base.Transform
           do ActorRender.draw a.Renderer scenePass pass.Cmd celPipeline outlinePipeline viewProjection model light)

    // Editor markers + gizmos, depth-tested against the meshes drawn above.
    do DebugDraw.draw state.GizmoDraw viewProjection pass.Cmd scenePass

    // The game's 2D layer last: blended, depth-less, so it sits on top of the scene and is
    // never occluded (FR-011). No-op when nothing is shown.
    do Ui.draw state.Ui pass.Cmd scenePass

    do SDL.EndGPURenderPass(scenePass)
