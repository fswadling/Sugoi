module GameplayScreen

// Gameplay screen: hosts the editor workspace and the 3D scene (grid). Input is
// gathered once by the engine and delivered via `Frame`; this screen just
// consumes it (no per-screen input system).

open System
open SDL3
open Screen

let private updateAspectRatio (width: float32, height: float32) (state: GameplayState.GameplayState): GameplayState.GameplayState =
    let aspect = if height > 0.0f then width / height else state.Camera.Aspect
    { state with Camera = { state.Camera with Aspect = aspect } }

// Re-sync the camera aspect on switch-in so the grid and picking agree
// immediately. Input edge detection is engine-owned now, so there's no
// per-screen input state left to re-sync.
let activate (_shared: SharedState.SharedState) (state: GameplayState.GameplayState) : GameplayState.GameplayState =
    let size = ImGui.displaySize()
    updateAspectRatio (size.x, size.y) state

// Advance non-render logic: refresh the camera aspect from the frame's window
// size, then hand the frame's input to the editor, which owns the edit/play
// toggle, shortcuts, nudge, interaction, and camera.
let update (_shared: SharedState.SharedState) (frame: Frame) (state: GameplayState.GameplayState) : struct (GameplayState.GameplayState * ScreenTransition) =
    state
    |> updateAspectRatio (frame.WindowW, frame.WindowH)
    |> Editor.update frame

// Draw the editor workspace (when editing) and always emit the reference grid
// so the scene stays visible in both edit and play modes.
let draw (shared: SharedState.SharedState) (state: GameplayState.GameplayState) : struct (GameplayState.GameplayState * ScreenTransition) =
    let struct (state, transition) = Editor.draw shared state
    let state = Viewport.drawGrid state
    let state = if state.Editing then Viewport.buildLines state else state

    struct (state, transition)

// Render the 3D scene: upload the grid (copy pass), then begin the scene
// render pass (which clears the swapchain), draw the grid, and end the pass.
let renderScene (state: GameplayState.GameplayState) (pass: ScenePass) : unit =
    do DebugDraw.upload state.DebugDraw pass.Cmd
    let scenePass = SDL.BeginGPURenderPass(pass.Cmd, ReadOnlySpan<SDL.GPUColorTargetInfo>(pass.ColorTargets), 1u, IntPtr.Zero)
    do DebugDraw.draw state.DebugDraw (Camera.viewProjection state.Camera) pass.Cmd scenePass
    do SDL.EndGPURenderPass(scenePass)
