module GameplayScreen

// Gameplay screen: hosts the editor workspace and the 3D scene (grid + actor
// meshes). Input is gathered once by the engine and delivered via `Frame`; this
// screen consumes it (no per-screen input system).

open System
open System.Numerics
open SDL3
open Screen
open Entity

let private updateAspectRatio (width: float32, height: float32) (state: byref<GameplayState.GameplayState>) : unit =
    let aspect = if height > 0.0f then width / height else state.Camera.Aspect
    state.Camera.Aspect <- aspect

// Re-sync the camera aspect on switch-in so the grid and picking agree
// immediately. Input edge detection is engine-owned now, so there's no
// per-screen input state left to re-sync.
let activate (_shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    let size = ImGui.displaySize()
    do updateAspectRatio (size.x, size.y) &state

// Advance each actor's own animation clock. Mutates each slot value in place
// through a byref so only the frame float is written, rather than rebuilding the
// Actor/ActorRender records via copy-and-update.
let private advanceActorAnimations (dt: float32) (zone: Zone.Zone) : unit =
    let slots = zone.Actors.Slots
    for i in 0 .. slots.Length - 1 do
        let mutable slot = &slots.[i]
        if slot.Alive then
            do ActorRender.advanceInPlace &slot.Value.Renderer dt

// Advance non-render logic: refresh the camera aspect from the frame's window
// size, advance the actor animations, then hand the frame's input to the editor,
// which owns the edit/play toggle, shortcuts, nudge, interaction, and camera.
let update (shared: SharedState.SharedState) (frame: Frame) (state: byref<GameplayState.GameplayState>) : ScreenTransition =
    do updateAspectRatio (frame.WindowW, frame.WindowH) &state
    do advanceActorAnimations frame.Dt state.Zone
    Editor.update shared.Assets frame &state

// Draw the editor workspace (when editing) and always emit the reference grid
// so the scene stays visible in both edit and play modes.
let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : ScreenTransition =
    let transition = Editor.draw shared.Assets &state
    do Viewport.drawGrid &state
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
let renderScene (state: inref<GameplayState.GameplayState>) (pass: ScenePass) : unit =
    do DebugDraw.upload state.DebugDraw pass.Cmd
    do DebugDraw.upload state.GizmoDraw pass.Cmd

    // Upload each actor's bone palette (copy pass) before the render pass.
    SlotMap.iteriAlive state.Zone.Actors (fun _ (a: Actor) ->
        ActorRender.upload a.Renderer pass.Cmd)

    let mutable depthTarget = pass.DepthTarget
    let scenePass = SDL.BeginGPURenderPass(pass.Cmd, ReadOnlySpan<SDL.GPUColorTargetInfo>(pass.ColorTargets), 1u, &depthTarget)

    let viewProjection = Camera.viewProjection state.Camera
    do DebugDraw.draw state.DebugDraw viewProjection pass.Cmd scenePass

    let lightDir = Vector3.Normalize(Vector3(0.4f, 0.8f, 0.4f))

    // Props (placed assets and imported environment parts), then the animated
    // actors. The environment entity itself draws nothing — its parts are props.
    let staticPipeline = state.StaticPipeline
    SlotMap.iteriAlive state.Zone.Environment.Props (fun _ (p: Prop) ->
        let model = Entity.modelMatrix p.Base.Transform
        do EnvironmentRender.draw p.Renderer scenePass pass.Cmd staticPipeline viewProjection model lightDir)

    let baseColor = Vector4(0.85f, 0.75f, 0.65f, 1.0f)
    let meshPipeline = state.MeshPipeline
    SlotMap.iteriAlive state.Zone.Actors (fun _ (a: Actor) ->
        let model = Entity.modelMatrix a.Base.Transform
        do ActorRender.draw a.Renderer scenePass pass.Cmd meshPipeline viewProjection model lightDir baseColor)

    // Editor markers + gizmos, depth-tested against the meshes drawn above.
    do DebugDraw.draw state.GizmoDraw viewProjection pass.Cmd scenePass

    do SDL.EndGPURenderPass(scenePass)
