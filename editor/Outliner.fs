module Outliner

// Left tree panel: lists the active zone's actors and props. Clicking a row
// selects it (delete/duplicate are keyboard shortcuts: Del / Ctrl+D).

open Entity

let draw (state: GameplayState.GameplayState) : GameplayState.GameplayState =
    do ImGui.beginWindow("Outliner", System.IntPtr.Zero, 0) |> ignore

    let mutable state = state
    let zone = state.Zone

    if SlotMap.count zone.Actors = 0u then
        do ImGui.text("(no actors)")
    else
        SlotMap.iteriAlive zone.Actors (fun i a ->
            if ImGui.button(sprintf "%s##actor%d" a.Base.Name (int i), ImGui.ImVec2(0.0f, 0.0f)) then
                state <- { state with Editor = { state.Editor with Selection = Some { Kind = EntityKind.Actor; Index = i } } })

    if SlotMap.count zone.Environment.Props = 0u then
        do ImGui.text("(no props)")
    else
        SlotMap.iteriAlive zone.Environment.Props (fun i p ->
            if ImGui.button(sprintf "%s##prop%d" p.Base.Name (int i), ImGui.ImVec2(0.0f, 0.0f)) then
                state <- { state with Editor = { state.Editor with Selection = Some { Kind = EntityKind.Prop; Index = i } } })

    match state.Editor.Selection with
    | Some _ ->
        if ImGui.button("Duplicate", ImGui.ImVec2(0.0f, 0.0f)) then state <- Selection.duplicate state
        if ImGui.button("Delete", ImGui.ImVec2(0.0f, 0.0f)) then state <- Selection.delete state
    | None -> ()

    do ImGui.endWindow()
    state
