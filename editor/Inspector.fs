module Inspector

// Right panel: the single editing surface for the selection. Read-only this
// phase (shows the selected entity's name + transform; field editing lands with
// the command/undo phase).

open Entity

let draw (state: inref<GameplayState.GameplayState>) =
    do ImGui.beginWindow("Inspector", System.IntPtr.Zero, 0) |> ignore

    match state.Editor.Selection with
    | Some ref ->
        match Zone.entityAt state.Zone ref with
        | Some e ->
            do ImGui.text(sprintf "Name: %s" e.Name)
            do ImGui.text(sprintf "Position: %A" e.Transform.Position)
            do ImGui.text(sprintf "Rotation: %A" (eulerDegrees e.Transform.Rotation))
            do ImGui.text(sprintf "Scale: %A" e.Transform.Scale)
        | None ->
            do ImGui.text("(no selection)")
    | None ->
        do ImGui.text("(no selection)")

    do ImGui.endWindow()
