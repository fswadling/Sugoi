module Outliner

// Left tree panel: a **Zones** section (the project's zones, add/select/delete) above
// the active zone's entities in collapsible sections — actors, props, waypoints, and
// triggers. Each entity row selects on click and carries inline Del/Dup buttons.

open System.Numerics
open Entity
open Command

// A waypoint at the camera, facing the way the camera looks: `CreateWorld` yields the
// rotation taking -Z (the project's camera-forward convention) onto the view direction.
let private newWaypointAtCamera (camera: Camera.Camera) : Entity =
    let rotation = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(camera.Position, Camera.forward camera, camera.Up))
    Entity.baseEntity "Waypoint" { transformIdentity with Position = camera.Position; Rotation = rotation }

let private entityRow (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (name: string) (id: string) (ref: EntityRef) : unit =
    if ImGui.button(sprintf "%s##%s" name id, ImGui.ImVec2(0.0f, 0.0f)) then
        state.Editor <- { state.Editor with Selection = Some ref }

    do ImGui.sameLine(0.0f, -1.0f)

    if ImGui.smallButton(sprintf "Del##del_%s" id) then
        Selection.deleteRef shared &state ref

    do ImGui.sameLine(0.0f, -1.0f)

    if ImGui.smallButton(sprintf "Dup##dup_%s" id) then
        Selection.duplicateRef shared &state ref

// The cast members the runtime cast index places in `place` (spec 063), as a short tag list, or "" when none.
// Read from the index, so a zone row answers "who is here" at a glance without opening the Cast panel.
let private castTags (state: GameplayState.GameplayState) (place: Place.Place) : string =
    let members =
        state.Cast
        |> Seq.filter (fun pair -> fst pair.Value = place)
        |> Seq.map (fun pair -> Roster.characterName pair.Key)
        |> Seq.sort
        |> Seq.toArray
    if members.Length = 0 then "" else "[" + System.String.Join(", ", members) + "]"

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Outliner", System.IntPtr.Zero, 0) |> ignore

    // ── Zones ──
    // The game's places, the present ones listed in declaration order with the active one marked. Adding
    // offers the absent places (a place already present cannot be added twice, spec 061 FR-003); removing is
    // refused for the active place, which the author must switch away from first (FR-011).
    if ImGui.collapsingHeader("Zones", 0) then
        for place in Place.all do
            if state.Zones.ContainsKey place then
                let name = Place.placeName place
                let marker = if place = state.ActiveZone then "* " else "  "
                // Environment residency (spec 050; loading/failed states spec 053): `[-]` no environment,
                // `[ ]` not loaded, `[~]` loading, `[+]` resident, `[x]` failed.
                let residency =
                    match state.Zones.[place].Environment with
                    | None -> "[-]"
                    | Some environment when Assets.isEnvironmentResident shared.Assets environment.AssetId -> "[+]"
                    | Some environment when Assets.hasFailed shared.Assets environment.AssetId -> "[x]"
                    | Some environment when Assets.isLoading shared.Assets environment.AssetId -> "[~]"
                    | Some _ -> "[ ]"
                // A click always makes the place the Inspector's subject, whether it switches or is already
                // active; a click that *changes* the place is a step on the one history (spec 048).
                if ImGui.button(sprintf "%s%s%s##zone_%s" marker residency name name, ImGui.ImVec2(0.0f, 0.0f)) then
                    if place = state.ActiveZone then GameplayState.selectZone shared &state place
                    else History.commit shared &state (ZoneMove (state.ActiveZone, place))
                do ImGui.sameLine(0.0f, -1.0f)
                // The active place cannot be made absent (FR-011), so the control is refused rather than hidden.
                do ImGui.beginDisabled(place = state.ActiveZone)
                if ImGui.smallButton(sprintf "Del##delzone_%s" name) then
                    History.commit shared &state (ZoneDelete (state.ActiveZone, place, Zone.captureZone state.Zones.[place]))
                do ImGui.endDisabled()

                // The cast members standing in this zone, as tags beside its row.
                let tags = castTags state place
                if tags <> "" then
                    do ImGui.sameLine(0.0f, 8.0f)
                    do ImGui.text(tags)

        do ImGui.text("")
        // Adding offers the declared places that are not present — a place already present is not offered.
        // The zone map is read into a local so the filter closes over the dictionary, not the `byref` state.
        let zones = state.Zones
        let absent = Place.all |> List.filter (fun place -> not (zones.ContainsKey place))
        do ImGui.beginDisabled(absent.IsEmpty)
        if ImGui.beginCombo("Add place##addzone", "(choose a place)", 0) then
            for place in absent do
                if ImGui.selectableBool(Place.placeName place, false, 0, ImGui.ImVec2(0.0f, 0.0f)) then
                    History.commit shared &state (ZoneAdd (state.ActiveZone, place))
            do ImGui.endCombo()
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
