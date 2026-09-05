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
    do if state.Editing then Viewport.buildLines &state

    transition

// Render the 3D scene: upload the grid (copy pass), then begin the scene render
// pass (clearing color + depth), draw the grid and the actor meshes, and end the
// pass.
let renderScene (state: inref<GameplayState.GameplayState>) (pass: ScenePass) : unit =
    do DebugDraw.upload state.DebugDraw pass.Cmd

    // Upload each actor's bone palette (copy pass) before the render pass.
    SlotMap.iteriAlive state.Zone.Actors (fun _ (a: Actor) ->
        ActorRender.upload a.Renderer pass.Cmd)

    let mutable depthTarget = pass.DepthTarget
    let scenePass = SDL.BeginGPURenderPass(pass.Cmd, ReadOnlySpan<SDL.GPUColorTargetInfo>(pass.ColorTargets), 1u, &depthTarget)

    let viewProjection = Camera.viewProjection state.Camera
    do DebugDraw.draw state.DebugDraw viewProjection pass.Cmd scenePass

    let baseColor = Vector4(0.85f, 0.75f, 0.65f, 1.0f)
    let lightDir = Vector3.Normalize(Vector3(0.4f, 0.8f, 0.4f))
    let meshPipeline = state.MeshPipeline
    SlotMap.iteriAlive state.Zone.Actors (fun _ (a: Actor) ->
        let model = Entity.modelMatrix a.Base.Transform
        do ActorRender.draw a.Renderer scenePass pass.Cmd meshPipeline viewProjection model lightDir baseColor)

    do SDL.EndGPURenderPass(scenePass)
