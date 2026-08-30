module Outliner

// Left tree panel: lists the active zone's entities in collapsible sections —
// actors, props, triggers, and waypoints. Each row selects on click and carries
// inline Del/Dup buttons. Triggers/waypoints have no zone storage yet.

open Entity

let private entityRow (state: GameplayState.GameplayState) (name: string) (id: string) (ref: EntityRef) : GameplayState.GameplayState =
    let mutable state = state

    if ImGui.button(sprintf "%s##%s" name id, ImGui.ImVec2(0.0f, 0.0f)) then
        state <- { state with Editor = { state.Editor with Selection = Some ref } }

    do ImGui.sameLine(0.0f, -1.0f)

    if ImGui.smallButton(sprintf "Del##del_%s" id) then
        state <- Selection.deleteRef state ref

    do ImGui.sameLine(0.0f, -1.0f)

    if ImGui.smallButton(sprintf "Dup##dup_%s" id) then
        state <- Selection.duplicateRef state ref

    state

let draw (state: GameplayState.GameplayState) : GameplayState.GameplayState =
    do ImGui.beginWindow("Outliner", System.IntPtr.Zero, 0) |> ignore

    let mutable state = state
    let zone = state.Zone

    if ImGui.collapsingHeader("Actors", 0) then
        if SlotMap.count zone.Actors = 0u then
            do ImGui.text("(none)")
        else
            SlotMap.iteriAlive zone.Actors (fun i a ->
                state <- entityRow state a.Base.Name (sprintf "actor%d" (int i)) { Kind = EntityKind.Actor; Index = i })

    if ImGui.collapsingHeader("Props", 0) then
        if SlotMap.count zone.Environment.Props = 0u then
            do ImGui.text("(none)")
        else
            SlotMap.iteriAlive zone.Environment.Props (fun i p ->
                state <- entityRow state p.Base.Name (sprintf "prop%d" (int i)) { Kind = EntityKind.Prop; Index = i })

    if ImGui.collapsingHeader("Triggers", 0) then
        do ImGui.text("(none)")

    if ImGui.collapsingHeader("Waypoints", 0) then
        do ImGui.text("(none)")

    do ImGui.endWindow()
    state
