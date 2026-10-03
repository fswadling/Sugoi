module Outliner

// Left tree panel: a **Zones** section (the project's zones, add/select/delete) above
// the active zone's entities in collapsible sections — actors, props, waypoints, and
// triggers. Each entity row selects on click and carries inline Del/Dup buttons.

open System.Numerics
open System.Text
open Entity
open Command

// The pending zone name in the text buffer, up to the first NUL byte; "" when empty.
let private zoneName (buf: byte[]) : string =
    let zero = System.Array.IndexOf(buf, 0uy)
    let length = if zero < 0 then buf.Length else zero
    if length = 0 then "" else Encoding.UTF8.GetString(buf, 0, length)

let private clearName (buf: byte[]) : unit =
    Array.fill buf 0 buf.Length 0uy

// The base entity a freshly-added waypoint or trigger is created from: the given name
// and transform, with the default editor flags. The name is what the Outliner shows and
// what the Inspector edits.
let private newEntity (name: string) (transform: Transform) : Entity =
    { Name = name
      Transform = transform
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

// A waypoint at the camera, facing the way the camera looks: `CreateWorld` yields the
// rotation taking -Z (the project's camera-forward convention) onto the view direction.
let private newWaypointAtCamera (camera: Camera.Camera) : Entity =
    let rotation = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(camera.Position, Camera.forward camera, camera.Up))
    newEntity "Waypoint" { transformIdentity with Position = camera.Position; Rotation = rotation }

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
    // The project's zones, active one marked. Adding is offered only for a
    // non-empty, unused name; deleting is offered only while another zone remains.
    if ImGui.collapsingHeader("Zones", 0) then
        let names = state.Zones.Keys |> Seq.sort |> Seq.toArray
        for name in names do
            if state.Zones.ContainsKey name then
                let marker = if name = state.ActiveZone then "* " else "  "
                // Residency (spec 050): whether the zone's environment is on the GPU right now.
                // `[-]` no environment, `[+]` resident, `[ ]` not resident; no binding, no panel.
                let residency =
                    match state.Zones.[name].Environment with
                    | None -> "[-]"
                    | Some environment when Assets.isEnvironmentResident shared.Assets environment.AssetId -> "[+]"
                    | Some _ -> "[ ]"
                // A click always makes the zone the Inspector's subject, whether it switches or is
                // the zone already in use (spec 043, FR-003). A click that *changes* the zone is a step on the
                // one history — recording the moves is what lets an edit be undone after moving away, because
                // it keeps the active zone the one the step being undone belongs to (spec 048). Clicking the
                // zone already active records nothing.
                if ImGui.button(sprintf "%s%s%s##zone_%s" marker residency name name, ImGui.ImVec2(0.0f, 0.0f)) then
                    if name = state.ActiveZone then GameplayState.selectZone shared &state name
                    else History.commit shared &state (ZoneMove (state.ActiveZone, name))
                if state.Zones.Count > 1 then
                    do ImGui.sameLine(0.0f, -1.0f)
                    if ImGui.smallButton(sprintf "Del##delzone_%s" name) then
                        // One step, carrying the zone's contents so the delete can be taken back (spec 048). The
                        // contents are read *before* the commit removes the zone, and `previous` is the zone
                        // active right now — the deleted zone itself when it is the active one, so an undo puts
                        // the developer back where they were.
                        History.commit shared &state (ZoneDelete (state.ActiveZone, name, Zone.captureZone state.Zones.[name]))

        do ImGui.text("")
        do ImGui.inputText("##newzone", state.Editor.NewZoneName, unativeint state.Editor.NewZoneName.Length, 0, System.IntPtr.Zero, System.IntPtr.Zero) |> ignore
        let pending = zoneName state.Editor.NewZoneName
        let canAdd = pending <> "" && not (state.Zones.ContainsKey pending)
        do ImGui.beginDisabled(not canAdd)
        if ImGui.button("Add Zone", ImGui.ImVec2(0.0f, 0.0f)) then
            // One step: the add and the zone change it causes (the new zone becomes active) are one undo.
            History.commit shared &state (ZoneAdd (state.ActiveZone, pending))
            clearName state.Editor.NewZoneName
        do ImGui.endDisabled()
        do ImGui.text("")

    let zone = GameplayState.activeZone state

    // The currently loaded environment (a zone has at most one).
    let environmentName =
        match zone.Environment with
        | None -> "(none)"
        | Some environment -> environment.Base.Name
    do ImGui.text(sprintf "Environment: %s" environmentName)
    do ImGui.text("")

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
            Spawning.spawnTrigger &state (newEntity "Trigger" transformIdentity)

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
            Spawning.spawnWaypoint &state (newEntity "Waypoint" transformIdentity)

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
