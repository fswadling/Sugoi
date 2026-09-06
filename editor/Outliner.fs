module Outliner

// Left tree panel: lists the active zone's entities in collapsible sections —
// actors, props, triggers, and waypoints. Each row selects on click and carries
// inline Del/Dup buttons. Triggers/waypoints have no zone storage yet.

open Entity

let private entityRow (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (name: string) (id: string) (ref: EntityRef) : unit =
    if ImGui.button(sprintf "%s##%s" name id, ImGui.ImVec2(0.0f, 0.0f)) then
        state.Editor <- { state.Editor with Selection = Some ref }

    do ImGui.sameLine(0.0f, -1.0f)

    if ImGui.smallButton(sprintf "Del##del_%s" id) then
        Selection.deleteRef shared &state ref

    do ImGui.sameLine(0.0f, -1.0f)

    if ImGui.smallButton(sprintf "Dup##dup_%s" id) then
        Selection.duplicateRef shared &state ref

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Outliner", System.IntPtr.Zero, 0) |> ignore

    let zone = state.Zone

    // The currently loaded environment (a zone has at most one).
    let environmentName =
        match zone.Environment with
        | None -> "(none)"
        | Some environment -> environment.Base.Name
    do ImGui.text(sprintf "Environment: %s" environmentName)
    do ImGui.text("")

    if ImGui.collapsingHeader("Actors", 0) then
        if SlotMap.count zone.Actors = 0u then
            do ImGui.text("(none)")
        else
            let slots = zone.Actors.Slots
            for i in 0 .. slots.Length - 1 do
                let slot = slots.[i]
                if slot.Alive then
                    entityRow shared &state slot.Value.Base.Name (sprintf "actor%d" i) { Kind = EntityKind.Actor; Index = uint32 i }

    if ImGui.collapsingHeader("Props", 0) then
        if SlotMap.count zone.Props = 0u then
            do ImGui.text("(none)")
        else
            let slots = zone.Props.Slots
            for i in 0 .. slots.Length - 1 do
                let slot = slots.[i]
                if slot.Alive then
                    entityRow shared &state slot.Value.Base.Name (sprintf "prop%d" i) { Kind = EntityKind.Prop; Index = uint32 i }

    if ImGui.collapsingHeader("Triggers", 0) then
        do ImGui.text("(none)")

    if ImGui.collapsingHeader("Waypoints", 0) then
        do ImGui.text("(none)")

    do ImGui.endWindow()
