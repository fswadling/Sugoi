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

// The per-frame actor step: blend each actor's pose, and while playing advance its clocks and its sway.
// The alive scan reads the map's slot span, then each payload is written through, so nothing is rebuilt.
//
// It runs in *both* modes — an actor must be posed while it is being placed in the editor — and it runs
// last in `update`, after every system and cue op that sets a clip weight or moves an actor, so the
// blend sees this frame's weights and the sway this frame's positions (spec 091, research D1).
let private simulateActors (dt: float32) (playing: bool) (zone: Zone.Zone) : unit =
    let slots = zone.Actors.Slots
    for i in 0 .. slots.Length - 1 do
        let slot = &slots.[i]
        if not slot.Alive then () else
        let actor = &slot.Value
        let transform = actor.State.Base.Transform
        do ActorRender.simulate &actor.Renderer dt playing (Entity.modelMatrix transform) transform.Rotation

// The active environment whose assets are pinned. Cached so an unchanged environment performs no pin-set
// clear, refill or lock (FR-013): only an actual change reaches `Assets.setPinned`.
let mutable private lastPinned : string voption = ValueNone

// Advance non-render logic: refresh the camera aspect from the frame's window size, then hand the
// frame's input to the editor (which owns the edit/play toggle, shortcuts, nudge, interaction, and
// camera). The editor does not simulate, so it stays passive: the animation clock, the dialogue
// tick and the player systems run only while playing. The play chain and the physics step
// therefore also run only while playing; each player system no-ops without a live bound player, so a
// play session with no player still advances the world but drives no character. The one step that runs
// in both modes is the actor step (spec 091), and it runs last — see its own comment.
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
        | Zone.Active environment | Zone.Awaiting environment -> ValueSome environment.AssetId
        | Zone.NoEnvironment -> ValueNone
    if pinned <> lastPinned then
        match pinned with
        | ValueSome assetId -> do Assets.setPinned shared.Assets [ assetId ]
        | ValueNone -> do Assets.setPinned shared.Assets []
        lastPinned <- pinned
    do Assets.pump shared.Assets Assets.UploadBudgetMs
    do Zone.reconcileEnvironment (GameplayState.activeZone state) state.Physics shared.Assets

    if not state.Editing then
        let playTime = state.PlayTime + float frame.Dt
        // The dialogue tick: while playing only, so the editor's SPACE is its own. Gated on a text
        // field having focus too, because the editor's fields take the same keys (FR-003). The same
        // play time feeds the cue below, so a timeout and a cue wait share one clock.
        do Systems.updateDialogue (Input.isPressed Input.Key.Space frame.Input) (ImGui.wantTextInput()) playTime &state.Dialogue
        do Systems.playerInput state.Physics zone frame.Input frame.Dt
        do Physics.step state.Physics frame.Dt
        do Systems.syncActors state.Physics zone
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
        state.PlayTime <- playTime
        CueEngine.tick shared &state playTime

    // The actor step last, on the *current* active zone (a cue may have switched place this frame): the
    // blend then sees the weights the systems and the cue set, and the sway the positions
    // `Systems.syncActors` wrote. It sits outside the play gate so an actor is also posed while editing
    // (spec 091, research D1).
    do simulateActors frame.Dt (not state.Editing) (GameplayState.activeZone state)

    // Persist the scenario library if it changed (spec 095 FR-005): one reference comparison, so a write happens
    // only on an author action — an add, clear, register or delete, or the undo/redo of one. A failed write is
    // retried at a bounded interval rather than once per frame (spec 098 FR-003).
    do Project.persistFrame (float frame.Dt) state.Scenarios

    // Pick up an external edit to the project file (spec 096): only while editing, and only when live reload is
    // on, and at most once per interval — so actual play performs no file I/O.
    if Project.pollEnabled state.Editing state.Editor.LiveReload then
        let reloaded = Project.pollExternal (float frame.Dt)
        match reloaded with
        | Some registry -> state.Scenarios <- registry
        // `open Screen` brings `ScreenTransition.None` into scope, so the option's empty case is matched
        // with a wildcard rather than the ambiguous `None`.
        | _ -> ()

    transition

// The prompt of the nearest interactable trigger the player is inside, or "" for nothing to show.
// `Systems.updateTriggers` (spec 029) already chose that trigger; this reads its authored text. An
// explicit match, not `ValueOption.bind`, so nothing closes over `zone` and nothing allocates
// (FR-010); the text is used as authored, not trimmed (FR-002). A dialogue line takes the screen
// (FR-012), and the prompt is a play-mode read-out, so it is silent while editing (FR-001).
let private promptText (state: GameplayState.GameplayState) (zone: Zone.Zone) : string =
    if state.Editing || state.Dialogue.Shown then "" else
    match state.InteractableTrigger with
    | ValueNone -> ""
    | ValueSome triggerRef ->
    match Zone.tryTriggerAt zone triggerRef with
    | ValueSome trigger -> trigger.State.Prompt
    | ValueNone -> ""


// The fade overlay for a `width` × `height` view at `level`: no overlay at 0, so the pre-feature frame is
// unchanged, otherwise a full-view black quad at alpha `level`. Pure, so the no-op and the full coverage are
// assertable headlessly.
let fadeOverlay (width: float32) (height: float32) (level: float32) : struct (Ui.Rect * Vector4) voption =
    if level <= 0.0f then ValueNone
    else ValueSome (struct ({ X = 0.0f; Y = 0.0f; W = width; H = height }, Vector4(0.0f, 0.0f, 0.0f, level)))

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : ScreenTransition =
    let transition = Editor.draw shared &state
    do Viewport.drawGrid &state
    // The game's 2D layer's batch is built here, where the screen state is mutable, and
    // uploaded/drawn in `renderScene` (the `DebugDraw` pattern). Its two consumers compose: the
    // dialogue panel, then the interact prompt below it. With nothing shown the batch stays empty,
    // so the frame is the pre-feature frame (FR-003, FR-009).
    let viewport = ImGui.displaySize()
    do Ui.beginBatch &state.Ui viewport.x viewport.y
    do DialogueRender.draw shared.Assets viewport.x viewport.y &state.Ui state.Dialogue
    do PromptRender.draw (promptText state (GameplayState.activeZone state)) viewport.x viewport.y &state.Ui

    // The fade (spec 059) is the last thing in the layer's batch, so it covers the dialogue and prompt
    // above, and — the layer being drawn inside the scene pass — it stays under the editor's ImGui panels
    // (FR-002, FR-008). At level 0 nothing is emitted, so the frame is unchanged (FR-003).
    match fadeOverlay viewport.x viewport.y state.Fade with
    | ValueSome (struct (overlay, colour)) -> Ui.rect &state.Ui overlay colour
    | ValueNone -> ()

    if state.Editing then
        do Viewport.buildLines &state
    else
        // Editor overlays only accumulate while editing; clear the markers/gizmo so a
        // switch to play mode doesn't linger them. (The grid's own accumulator is
        // emptied by `Viewport.drawGrid` on the same switch, spec 093.)
        state.GizmoDraw <- DebugDraw.clear state.GizmoDraw

    transition

// Render the 3D scene: upload the grid + gizmo line vertices (copy passes; the
// grid's accumulator is empty while playing, spec 093), then begin the scene render
// pass (clearing color + depth), draw the grid, the props (placed assets + imported
// environment parts), the actors, and finally the gizmo/marker lines (depth-tested so
// they are occluded by geometry in front of them).
let renderScene (shared: SharedState.SharedState) (state: inref<GameplayState.GameplayState>) (pass: ScenePass) : unit =
    // ── per-frame buffers ──
    // Gather the static instances (CPU), then upload every per-frame buffer in copy
    // passes before the render pass.
    let zone = GameplayState.activeZone state
    // Gather only when the static scene changed; upload only what was gathered (FR-009).
    let gathered = Zone.gatherStatic shared.Gpu.Device zone
    if gathered then do EnvironmentBatch.upload shared.Gpu.Device zone.Batch pass.Cmd
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

    // The scene's one sun, resolved from the active zone's authored settings (spec 043) instead of
    // a constant, so a zone lights its own scene.
    let light = Texture.toLight zone.Light

    // The zone's sky (spec 092): a camera-centred dome drawn first, depth-less, so the grid and geometry
    // drawn after it overwrite it. No-op when the sky is off, so an unedited zone's frame is unchanged.
    let sky = zone.Skydome
    if sky.Enabled then
        do Skydome.draw state.Skydome scenePass pass.Cmd shared.Gpu.SkydomePipeline state.Camera sky light

    do DebugDraw.draw state.DebugDraw viewProjection pass.Cmd scenePass

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
