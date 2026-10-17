module Outliner

// Left tree panel: a **Zones** section (the project's zones, add/select/delete) above
// the active zone's entities in collapsible sections — actors, props, waypoints, and
// triggers. Each entity row selects on click and carries inline Del/Dup buttons.

open System
open System.Collections.Generic
open Entity
open Command

let private entityRow (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (name: string) (ref: EntityRef) : unit =
    if ImGui.button(name, ImGui.ImVec2(0.0f, 0.0f)) then
        state.Editor <- { state.Editor with Selection = Some ref }

    do ImGui.sameLine(0.0f, -1.0f)

    if ImGui.smallButton "Del" then
        Selection.deleteRef shared &state ref

    do ImGui.sameLine(0.0f, -1.0f)

    if ImGui.smallButton "Dup" then
        Selection.duplicateRef shared &state ref

/// One slot-map section: a row per live slot under its own pushed id, so the static row labels
/// (`Del`, `Dup`) stay unique without formatting an identifier per row.
let private slotRows
    (shared: SharedState.SharedState)
    (state: byref<GameplayState.GameplayState>)
    (prefix: string)
    (slots: Span<SlotMap.Slot<'T>>)
    (nameOf: 'T -> string)
    (refOf: uint32 -> uint32 -> EntityRef) : unit =
    for i in 0 .. slots.Length - 1 do
        let slot = &slots.[i]
        if slot.Alive then
            let ref = refOf (uint32 i) slot.Generation
            do ImGui.pushId (prefix + string i)
            entityRow shared &state (nameOf slot.Value) ref
            do ImGui.popId()

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    let visible = ImGui.beginWindow("Outliner", System.IntPtr.Zero, 0)
    if visible then

        // ── Zones ──
        // The present zones, listed with the active one marked. A zone is named (spec 079), so adding is by typed
        // name — there is no closed vocabulary to choose from; a name already present is refused, because a zone's
        // name is its identity. Removing is refused for the active zone, which the author must switch away from first.
        if ImGui.collapsingHeader("Zones", 0) then
            for KeyValue (name, zone) in state.Zones do
                let marker = if name = state.ActiveZone then "* " else "  "
                // Environment residency (spec 050; loading/failed states spec 053): `[-]` no environment,
                // `[ ]` not loaded, `[~]` loading, `[+]` resident, `[x]` failed.
                let residency =
                    match zone.Environment with
                    | None -> "[-]"
                    | Some environment when Assets.isEnvironmentResident shared.Assets environment.AssetId -> "[+]"
                    | Some environment when Assets.hasFailed shared.Assets environment.AssetId -> "[x]"
                    | Some environment when Assets.isLoading shared.Assets environment.AssetId -> "[~]"
                    | Some _ -> "[ ]"
                // A click always makes the zone the Inspector's subject, whether it switches or is already active; a
                // click that *changes* the zone is a step on the one history (spec 048).
                if ImGui.button(sprintf "%s%s%s##zone_%s" marker residency name name, ImGui.ImVec2(0.0f, 0.0f)) then
                    if name = state.ActiveZone then GameplayState.selectZone shared &state name
                    else Undo.commit shared &state (ZoneMove (state.ActiveZone, name))
                do ImGui.sameLine(0.0f, -1.0f)
                // The active zone cannot be made absent, so the control is refused rather than hidden.
                do ImGui.beginDisabled(name = state.ActiveZone)
                if ImGui.smallButton(sprintf "Del##delzone_%s" name) then
                    Undo.commit shared &state (ZoneDelete (state.ActiveZone, name, Zone.captureZone zone))
                do ImGui.endDisabled()

            do ImGui.text("")
            // Add a zone by name: a name that is not present is brought into existence by naming it (spec 079).
            let buffer = state.Editor.ZoneNameBuffer
            do ImGui.inputText("Name##addzone", buffer, unativeint buffer.Length, 0, System.IntPtr.Zero, System.IntPtr.Zero) |> ignore
            do ImGui.sameLine(0.0f, -1.0f)
            let newName = ImGui.bufferText buffer
            let canAdd = newName <> "" && not (state.Zones.ContainsKey newName)
            do ImGui.beginDisabled(not canAdd)
            if ImGui.smallButton("Add##addzone_btn") && canAdd then
                Undo.commit shared &state (ZoneAdd (state.ActiveZone, newName))
            do ImGui.endDisabled()
            do ImGui.text("")

        let zone = GameplayState.activeZone state

        if ImGui.collapsingHeader("Actors", 0) then
            if zone.Actors.Count = 0u then
                do ImGui.text("(none)")
            else
                slotRows shared &state "actor" zone.Actors.Slots (fun (a: Actor) -> a.State.Base.Name) actorRef

        if ImGui.collapsingHeader("Props", 0) then
            if zone.Props.Count = 0u then
                do ImGui.text("(none)")
            else
                slotRows shared &state "prop" zone.Props.Slots (fun (p: Prop) -> p.State.Base.Name) propRef

        if ImGui.collapsingHeader("Parts", 0) then
            match zone.Environment with
            | None -> do ImGui.text("(none)")
            | Some environment ->

            if environment.Parts.Count = 0u then
                do ImGui.text("(none)")
            else
                slotRows shared &state "part" environment.Parts.Slots (fun (p: Prop) -> p.State.Base.Name) (partRef environment.Epoch)

        if ImGui.collapsingHeader("Triggers", 0) then
            let triggerRoom = zone.Triggers.Count < zone.Triggers.Capacity
            do ImGui.beginDisabled(not triggerRoom)
            let addTrigger = ImGui.button("Add Trigger", ImGui.ImVec2(0.0f, 0.0f))
            do ImGui.endDisabled()
            if addTrigger then
                Spawning.spawnTrigger &state (Entity.baseEntity "Trigger" transformIdentity)

            if zone.Triggers.Count = 0u then
                do ImGui.text("(none)")
            else
                slotRows shared &state "trigger" zone.Triggers.Slots (fun (t: Trigger) -> t.State.Base.Name) triggerRef

        if ImGui.collapsingHeader("Waypoints", 0) then
            let waypointRoom = zone.Waypoints.Count < zone.Waypoints.Capacity

            // One creation menu: every anchor is a row, so adding an anchor adds a row rather than a
            // button. Chain editing belongs to the selected waypoint and lives in the Inspector.
            do ImGui.beginDisabled(not waypointRoom)
            if ImGui.button("Add Waypoint", ImGui.ImVec2(0.0f, 0.0f)) then
                do ImGui.openPopup("##waypoint_add", 0)
            do ImGui.endDisabled()

            if ImGui.beginPopup("##waypoint_add", 0) then
                do ImGui.beginDisabled(not waypointRoom)
                for anchor in Spawning.waypointAnchors do
                    if ImGui.menuItem(Spawning.anchorName anchor, null, false, Spawning.anchorAvailable zone anchor) then
                        do Spawning.spawnWaypointAt &state anchor
                        do ImGui.closeCurrentPopup()
                do ImGui.endDisabled()
                do ImGui.endPopup()

            if zone.Waypoints.Count = 0u then
                do ImGui.text("(none)")
            else
                slotRows shared &state "waypoint" zone.Waypoints.Slots (fun (w: Waypoint) -> w.Base.Name) waypointRef

    do ImGui.endWindow()
