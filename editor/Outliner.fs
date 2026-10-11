module Outliner

// Left tree panel: a **Zones** section (the project's zones, add/select/delete) above
// the active zone's entities in collapsible sections — actors, props, waypoints, and
// triggers. Each entity row selects on click and carries inline Del/Dup buttons.

open System.Numerics
open System.Collections.Generic
open Entity
open Command

// A waypoint at the camera, facing the way the camera looks: `CreateWorld` yields the
// rotation taking -Z (the project's camera-forward convention) onto the view direction.
let private newWaypointAtCamera (camera: Camera.Camera) : Entity =
    let rotation = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(camera.Position, Camera.forward camera, camera.Up))
    Entity.baseEntity "Waypoint" { transformIdentity with Position = camera.Position; Rotation = rotation }

// Read a NUL-terminated text buffer as a string (the Outliner's add-zone name field, spec 079).
let private bufferText (buffer: byte[]) : string =
    let zero = System.Array.IndexOf(buffer, 0uy)
    let length = if zero < 0 then buffer.Length else zero
    if length = 0 then "" else System.Text.Encoding.UTF8.GetString(buffer, 0, length)

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
                else History.commit shared &state (ZoneMove (state.ActiveZone, name))
            do ImGui.sameLine(0.0f, -1.0f)
            // The active zone cannot be made absent, so the control is refused rather than hidden.
            do ImGui.beginDisabled(name = state.ActiveZone)
            if ImGui.smallButton(sprintf "Del##delzone_%s" name) then
                History.commit shared &state (ZoneDelete (state.ActiveZone, name, Zone.captureZone zone))
            do ImGui.endDisabled()

        do ImGui.text("")
        // Add a zone by name: a name that is not present is brought into existence by naming it (spec 079).
        let buffer = state.Editor.ZoneNameBuffer
        do ImGui.inputText("Name##addzone", buffer, unativeint buffer.Length, 0, System.IntPtr.Zero, System.IntPtr.Zero) |> ignore
        do ImGui.sameLine(0.0f, -1.0f)
        let newName = bufferText buffer
        let canAdd = newName <> "" && not (state.Zones.ContainsKey newName)
        do ImGui.beginDisabled(not canAdd)
        if ImGui.smallButton("Add##addzone_btn") && canAdd then
            History.commit shared &state (ZoneAdd (state.ActiveZone, newName))
        do ImGui.endDisabled()
        do ImGui.text("")

    let zone = GameplayState.activeZone state

    if ImGui.collapsingHeader("Actors", 0) then
        if zone.Actors.Count = 0u then
            do ImGui.text("(none)")
        else
            let slots = zone.Actors.Slots
            for i in 0 .. slots.Length - 1 do
                let slot = slots.[i]
                if slot.Alive then
                    entityRow shared &state slot.Value.State.Base.Name (sprintf "actor%d" i) (actorRef (uint32 i) slot.Generation)

    if ImGui.collapsingHeader("Props", 0) then
        if zone.Props.Count = 0u then
            do ImGui.text("(none)")
        else
            let slots = zone.Props.Slots
            for i in 0 .. slots.Length - 1 do
                let slot = slots.[i]
                if slot.Alive then
                    entityRow shared &state slot.Value.State.Base.Name (sprintf "prop%d" i) (propRef (uint32 i) slot.Generation)

    if ImGui.collapsingHeader("Parts", 0) then
        match zone.Environment with
        | None -> do ImGui.text("(none)")
        | Some environment ->

        if environment.Parts.Count = 0u then
            do ImGui.text("(none)")
        else
            let slots = environment.Parts.Slots
            for i in 0 .. slots.Length - 1 do
                let slot = slots.[i]
                if slot.Alive then
                    entityRow shared &state slot.Value.State.Base.Name (sprintf "part%d" i) (partRef environment.Epoch (uint32 i) slot.Generation)

    if ImGui.collapsingHeader("Triggers", 0) then
        if ImGui.button("Add Trigger", ImGui.ImVec2(0.0f, 0.0f)) then
            Spawning.spawnTrigger &state (Entity.baseEntity "Trigger" transformIdentity)

        if zone.Triggers.Count = 0u then
            do ImGui.text("(none)")
        else
            let slots = zone.Triggers.Slots
            for i in 0 .. slots.Length - 1 do
                let slot = slots.[i]
                if slot.Alive then
                    entityRow shared &state slot.Value.State.Base.Name (sprintf "trigger%d" i) (triggerRef (uint32 i) slot.Generation)

    if ImGui.collapsingHeader("Waypoints", 0) then
        // The chain actions act on the selected waypoint, so they are gated on it — and
        // disabled rather than hidden, so a button never moves the ones beside it.
        let selected =
            match state.Editor.Selection with
            | Some ref when ref.Kind = EntityKind.Waypoint && Zone.isAlive zone ref -> Some ref
            | _ -> None

        if ImGui.button("Add Waypoint", ImGui.ImVec2(0.0f, 0.0f)) then
            Spawning.spawnWaypoint &state (Entity.baseEntity "Waypoint" transformIdentity)

        do ImGui.sameLine(0.0f, -1.0f)

        if ImGui.button("Add at Camera", ImGui.ImVec2(0.0f, 0.0f)) then
            Spawning.spawnWaypoint &state (newWaypointAtCamera state.Camera)

        do ImGui.sameLine(0.0f, -1.0f)

        // Extend the selected waypoint: a new waypoint at the camera, linked as its next,
        // which becomes the selection so a chain can be walked out one link at a time.
        do ImGui.beginDisabled(Option.isNone selected)
        let extend = ImGui.button("Add Next at Camera", ImGui.ImVec2(0.0f, 0.0f))
        do ImGui.endDisabled()
        if extend then
            match selected with
            | Some from -> Spawning.spawnLinkedWaypoint &state from (newWaypointAtCamera state.Camera)
            | None -> ()

        do ImGui.sameLine(0.0f, -1.0f)

        // End the selected waypoint's chain, when it has one.
        let hasNext = selected |> Option.bind (Zone.waypointNext zone) |> Option.isSome
        do ImGui.beginDisabled(not hasNext)
        let unlink = ImGui.button("Unlink", ImGui.ImVec2(0.0f, 0.0f))
        do ImGui.endDisabled()
        if unlink then
            match selected with
            | Some from -> Selection.linkRef shared &state from None
            | None -> ()

        if zone.Waypoints.Count = 0u then
            do ImGui.text("(none)")
        else
            let slots = zone.Waypoints.Slots
            for i in 0 .. slots.Length - 1 do
                let slot = slots.[i]
                if slot.Alive then
                    let id = sprintf "waypoint%d" i
                    let ref = waypointRef (uint32 i) slot.Generation
                    entityRow shared &state slot.Value.Base.Name id ref

                    // Continue the row's line with the link action: point the selected
                    // waypoint at this one.
                    do ImGui.sameLine(0.0f, -1.0f)
                    let canLink = selected |> Option.exists (fun from -> from <> ref)
                    do ImGui.beginDisabled(not canLink)
                    let link = ImGui.smallButton(sprintf "Link##link_%s" id)
                    do ImGui.endDisabled()
                    if link then
                        match selected with
                        | Some from -> Selection.linkRef shared &state from (Some ref)
                        | None -> ()

    do ImGui.endWindow()
