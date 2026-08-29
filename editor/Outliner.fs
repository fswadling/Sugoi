module Outliner

// Left tree panel. Empty this milestone (no entities until 2c).

let draw (_state: GameplayState.GameplayState) =
    do ImGui.beginWindow("Outliner", System.IntPtr.Zero, 0) |> ignore
    do ImGui.text("(empty)")
    do ImGui.endWindow()
