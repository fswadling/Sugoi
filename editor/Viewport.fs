module Viewport

// The 3D viewport is the docking workspace's central (passthru) node, not an
// ImGui window. It emits the reference grid into the DebugDraw accumulator each
// frame (drawn to the swapchain behind ImGui by Engine.fs).

// Emit the reference grid for this frame.
let drawGrid (state: GameplayState.GameplayState) : GameplayState.GameplayState =
    let dd = DebugDraw.clear state.DebugDraw |> DebugDraw.drawGrid
    { state with DebugDraw = dd }
