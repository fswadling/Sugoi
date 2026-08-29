module Inspector

// Right panel: the single editing surface for the selection. Empty (no
// selection) this milestone.

let draw (_state: GameplayState.GameplayState) =
    do ImGui.beginWindow("Inspector", System.IntPtr.Zero, 0) |> ignore
    do ImGui.text("(no selection)")
    do ImGui.endWindow()
