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
        if slot.Alive then
            let actor = &zone.Actors.Get { Index = uint32 i; Generation = slot.Generation }
            do ActorRender.advanceInPlace &actor.Renderer dt

// Advance non-render logic: refresh the camera aspect from the frame's window
// size, advance the actor animations, then hand the frame's input to the editor
// (which owns the edit/play toggle, shortcuts, nudge, interaction, and camera).
// The play chain and the physics step run only while playing; each player system
// no-ops without a live bound player, so a play session with no player still advances
// the world but drives no character. The editor does not simulate, so it stays passive.
let update (shared: SharedState.SharedState) (frame: Frame) (state: byref<GameplayState.GameplayState>) : ScreenTransition =
    do updateAspectRatio (frame.WindowW, frame.WindowH) &state
    do advanceActorAnimations frame.Dt state.Zone
    let transition = Editor.update shared frame &state

    // The world simulates for the whole of play mode, player or not: the physics step and
    // the body→entity sync run regardless, while the player systems no-op without a live
    // player. The editor does not simulate, so it stays passive.
    if not state.Editing then
        do Systems.playerInput shared.Physics state.Zone state.Player frame.Input frame.Dt
        do Physics.step shared.Physics frame.Dt
        do Systems.syncActors shared.Physics state.Zone
        do Systems.cameraFollow state.Zone state.Player &state.Follow &state.Camera
        do Systems.animate state.Zone state.Player frame.Input

    transition

// Draw the editor workspace (when editing) and always emit the reference grid
// so the scene stays visible in both edit and play modes.
let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : ScreenTransition =
    let transition = Editor.draw shared &state
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
    state.Zone.Actors.IteriAlive(fun _ _ (a: Actor) ->
        ActorRender.upload a.Renderer pass.Cmd)

    let mutable depthTarget = pass.DepthTarget
    let scenePass = SDL.BeginGPURenderPass(pass.Cmd, ReadOnlySpan<SDL.GPUColorTargetInfo>(pass.ColorTargets), 1u, &depthTarget)

    let viewProjection = Camera.viewProjection state.Camera
    do DebugDraw.draw state.DebugDraw viewProjection pass.Cmd scenePass

    let light : Light =
        { Direction = Vector3.Normalize(Vector3(0.4f, 0.8f, 0.4f))
          Color = Vector3(1.0f, 1.0f, 1.0f)
          Ambient = Vector3(0.15f, 0.15f, 0.15f) }
    let cameraPos = state.Camera.Position

    // Props (placed assets and imported environment parts), then the animated
    // actors. The environment entity itself draws nothing — its parts are props.
    let staticPipeline = state.StaticPipeline
    do state.Zone.Props.IteriAlive(fun _ _ (p: Prop) ->
           let model = Entity.modelMatrix p.Base.Transform
           do EnvironmentRender.draw p.Renderer scenePass pass.Cmd staticPipeline viewProjection model light cameraPos)

    do state.Zone.Environment |> Option.iter (fun environment ->
        environment.Parts.IteriAlive(fun _ _ (p: Prop) ->
            let model = Entity.modelMatrix p.Base.Transform
            do EnvironmentRender.draw p.Renderer scenePass pass.Cmd staticPipeline viewProjection model light cameraPos))

    let meshPipeline = state.MeshPipeline
    do state.Zone.Actors.IteriAlive(fun _ _ (a: Actor) ->
           let model = Entity.modelMatrix a.Base.Transform
           do ActorRender.draw a.Renderer scenePass pass.Cmd meshPipeline viewProjection model light cameraPos)

    // Editor markers + gizmos, depth-tested against the meshes drawn above.
    do DebugDraw.draw state.GizmoDraw viewProjection pass.Cmd scenePass

    do SDL.EndGPURenderPass(scenePass)
