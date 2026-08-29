module Viewport

// 3D viewport panel. Empty this step; the grid + orbit camera land in the
// DebugDraw render step.

let draw (_state: GameplayState.GameplayState) =
    do ImGui.beginWindow("Viewport", System.IntPtr.Zero, 0) |> ignore
    do ImGui.text("3D viewport (grid next)")
    do ImGui.endWindow()
