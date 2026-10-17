module Spawning

// The single place the editor creates entities. Every `Zone.spawn*` must be paired
// with the command that reverses it, immediately: recorded refs rely on no untracked
// `Add` reusing a slot they name, or undo/redo hits `SlotMap.Occupy` on a taken slot.
// `AssetBrowser` and `Selection` must not call `Zone.spawn*` directly.
//
// Each command carries the new entity's record as `Zone.capture*` takes it, so a copy
// carries every authored property of its original and neither carries runtime state.

open System
open System.Numerics
open Entity
open Command

// The entity a spawned asset becomes: the whole of what the editor authors for one, before the
// kind's own properties are added. Both panels that spawn by asset build theirs here, so the two
// paths cannot drift apart — a second copy of this literal is the drift (spec 033 FR-007). The
// literal itself moved to `Entity.baseEntity` (spec 064), shared with the cue's actor ops.
let baseEntity (name: string) : Entity =
    Entity.baseEntity name transformIdentity

// The editor-only anchor palette: where a new entity's position comes from, and the prefix of its automatic
// name. Waypoint and trigger creation both iterate `spawnAnchors`, so a new anchor is added once and both
// menus gain the row.
type SpawnAnchor =
    | Origin
    | Camera
    | Player

let spawnAnchors : SpawnAnchor list = [ Origin; Camera; Player ]

let anchorName (anchor: SpawnAnchor) : string =
    match anchor with
    | Origin -> "Origin"
    | Camera -> "Camera"
    | Player -> "Player"

let anchorAvailable (zone: Zone.Zone) (anchor: SpawnAnchor) : bool =
    match anchor with
    | Player -> Systems.isPlayerLive zone
    | Origin | Camera -> true

// The numeric suffix of a name matching `<prefix> <n>`, or None. Prefix matching is exact, so `Camera 7`
// reserves 7 and a name like `Camera wall` reserves nothing.
let private nameSuffix (prefix: string) (name: string) : int option =
    let lead = prefix + " "
    if not (name.StartsWith(lead, StringComparison.Ordinal)) then None else

    match Int32.TryParse(name.Substring(lead.Length)) with
    | true, n when n > 0 -> Some n
    | _ -> None

// The next automatic name for a prefix over a live slot map: the prefix plus the lowest positive number
// no live entity with that prefix already uses. Names are labels, so a rename or delete frees the number.
let private nextAutomaticName (prefix: string) (slots: Span<SlotMap.Slot<'T>>) (nameOf: 'T -> string) : string =
    let used = System.Collections.Generic.HashSet<int>()
    for i in 0 .. slots.Length - 1 do
        let slot = &slots.[i]
        if slot.Alive then
            match nameSuffix prefix (nameOf slot.Value) with
            | Some n -> used.Add n |> ignore
            | None -> ()

    let mutable n = 1
    while used.Contains n do n <- n + 1
    sprintf "%s %d" prefix n

let private nextWaypointName (zone: Zone.Zone) (anchor: SpawnAnchor) : string =
    nextAutomaticName (anchorName anchor) zone.Waypoints.Slots (fun (waypoint: Waypoint) -> waypoint.Base.Name)

let private nextTriggerName (zone: Zone.Zone) (anchor: SpawnAnchor) : string =
    nextAutomaticName (anchorName anchor) zone.Triggers.Slots (fun (trigger: Trigger) -> trigger.State.Base.Name)

// The position and facing an anchor supplies, or None when it has nothing to stamp (no live player).
let private anchorTransform (zone: Zone.Zone) (camera: Camera.Camera) (anchor: SpawnAnchor) : (Vector3 * Quaternion) option =
    match anchor with
    | Origin -> Some (Vector3.Zero, Quaternion.Identity)
    | Camera ->
        let rotation = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(camera.Position, Camera.forward camera, camera.Up))
        Some (camera.Position, rotation)
    | Player ->
        if not (anchorAvailable zone anchor) then None else

        let transform = (zone.Actors.Get zone.Player.Value).State.Base.Transform
        Some (transform.Position, transform.Rotation)

// A waypoint at an anchor, named automatically, or None when the anchor has nothing to stamp. Every anchor
// uses unit scale: a waypoint is a point, not a copy of an actor or camera.
let waypointAt (zone: Zone.Zone) (camera: Camera.Camera) (anchor: SpawnAnchor) : Entity option =
    match anchorTransform zone camera anchor with
    | Some (position, rotation) ->
        Some (Entity.baseEntity (nextWaypointName zone anchor) { transformIdentity with Position = position; Rotation = rotation })
    | None -> None

// The player trigger base sits just under the feet, so the player is inside at the moment of creation even
// with small ground unevenness. A fixed drop keeps the anchor independent of the trigger's authored height.
let private playerTriggerDrop = 0.1f

// The base position a trigger anchor supplies, or None when it has nothing to stamp (no live player).
let private anchorPosition (zone: Zone.Zone) (camera: Camera.Camera) (anchor: SpawnAnchor) : Vector3 option =
    match anchor with
    | Origin -> Some Vector3.Zero
    | Camera -> Some camera.Position
    | Player ->
        if not (anchorAvailable zone anchor) then None else

        let feet = (zone.Actors.Get zone.Player.Value).State.Base.Transform.Position
        Some (feet - Vector3(0.0f, playerTriggerDrop, 0.0f))

// A trigger at an anchor, named automatically, or None when the anchor has nothing to stamp. The anchor
// supplies position only: rotation is identity and scale is unit, and `spawnTrigger` supplies the default
// radius and height.
let triggerAt (zone: Zone.Zone) (camera: Camera.Camera) (anchor: SpawnAnchor) : Entity option =
    match anchorPosition zone camera anchor with
    | Some position ->
        Some (Entity.baseEntity (nextTriggerName zone anchor) { transformIdentity with Position = position })
    | None -> None

// Push the command that reverses a freshly-created entity, and select it.
let private commit (state: byref<GameplayState.GameplayState>) (cmd: Command) (ref: EntityRef) : unit =
    let undo = UndoStack.push state.Editor.Undo cmd
    state.Editor <- { state.Editor with Selection = Some ref; Undo = undo }

// Refuse a create that would exceed a kind's fixed capacity, reporting why (spec 080, US5). The creation
// surfaces gate on room first; `SlotMap.Add` would otherwise raise on a full map.
let private hasRoom (kind: string) (place: string) (map: SlotMap.SlotMap<'T>) : bool =
    if map.Count < map.Capacity then true
    else
    do Diagnostics.reportf "editor: cannot add %s to '%s': at capacity (%d)" kind place map.Capacity
    false

// Spawn a whole actor into the active zone. A name is a label, not an identity (spec 072), so this places an
// actor named by `entity` and records nothing else: a duplicate name is a legitimate scene, and the Asset
// Browser is the one surface that places an actor, so there is only this one rule.
let spawnActor (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (entity: Entity) (assetId: string) : unit =
    let zone = GameplayState.activeZone state
    if not (hasRoom "actor" state.ActiveZone zone.Actors) then () else

    let ref = Zone.spawnActor zone state.Physics shared.Assets entity assetId
    do commit &state (ActorCreate (state.ActiveZone, ref, Zone.captureActor zone ref)) ref

let spawnProp (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (entity: Entity) (assetId: string) : unit =
    let zone = GameplayState.activeZone state
    if not (hasRoom "prop" state.ActiveZone zone.Props) then () else

    let ref = Zone.spawnProp zone state.Physics shared.Assets entity assetId
    do commit &state (PropCreate (state.ActiveZone, ref, Zone.captureProp zone ref)) ref

// Spawn a waypoint into the active zone. It carries no asset, so no shared state is
// needed.
let spawnWaypoint (state: byref<GameplayState.GameplayState>) (entity: Entity) : unit =
    let zone = GameplayState.activeZone state
    if not (hasRoom "waypoint" state.ActiveZone zone.Waypoints) then () else

    let ref = Zone.spawnWaypoint zone entity
    do commit &state (WaypointCreate (state.ActiveZone, ref, Zone.captureWaypoint zone ref)) ref

// Spawn a trigger into the active zone. It carries no asset and no body, so no shared
// state is needed.
let spawnTrigger (state: byref<GameplayState.GameplayState>) (entity: Entity) : unit =
    let zone = GameplayState.activeZone state
    if not (hasRoom "trigger" state.ActiveZone zone.Triggers) then () else

    let ref = Zone.spawnTrigger zone entity
    do commit &state (TriggerCreate (state.ActiveZone, ref, Zone.captureTrigger zone ref)) ref

// Create a trigger at an anchor in the active zone and select it. The capacity check happens before the
// anchor is built, so a refusal has no effect. An unavailable anchor (no player) creates nothing.
let spawnTriggerAt (state: byref<GameplayState.GameplayState>) (anchor: SpawnAnchor) : unit =
    let zone = GameplayState.activeZone state
    if not (hasRoom "trigger" state.ActiveZone zone.Triggers) then () else

    match triggerAt zone state.Camera anchor with
    | Some entity -> do spawnTrigger &state entity
    | None -> ()

// Create a waypoint at an anchor in the active zone and select it. The capacity check happens before the
// anchor is built, so a refusal has no effect. An unavailable anchor (no player) creates nothing.
let spawnWaypointAt (state: byref<GameplayState.GameplayState>) (anchor: SpawnAnchor) : unit =
    let zone = GameplayState.activeZone state
    if not (hasRoom "waypoint" state.ActiveZone zone.Waypoints) then () else

    match waypointAt zone state.Camera anchor with
    | Some entity -> do spawnWaypoint &state entity
    | None -> ()

// Create a waypoint in the active zone, link `from` to it, and select the new one. When `from` already has
// a next, the new waypoint is inserted before it, so the rest of the chain is preserved. The spawn and both
// links are one command (`LinkSpawn`), so one undo removes the waypoint and restores the previous link.
let spawnLinkedWaypoint (state: byref<GameplayState.GameplayState>) (from: EntityRef) (entity: Entity) : unit =
    let zone = GameplayState.activeZone state
    if not (hasRoom "waypoint" state.ActiveZone zone.Waypoints) then () else

    let before = Zone.waypointNext zone from
    let ref = Zone.spawnWaypoint zone entity
    do Zone.setWaypointNext zone ref before
    do Zone.setWaypointNext zone from (Some ref)
    do commit &state (LinkSpawn (state.ActiveZone, from, before, ref, Zone.captureWaypoint zone ref)) ref

// Extend `from` with a waypoint at an anchor. An unavailable anchor (no player) creates nothing.
let spawnNextWaypointAt (state: byref<GameplayState.GameplayState>) (from: EntityRef) (anchor: SpawnAnchor) : unit =
    let zone = GameplayState.activeZone state
    if not (hasRoom "waypoint" state.ActiveZone zone.Waypoints) then () else

    match waypointAt zone state.Camera anchor with
    | Some entity -> do spawnLinkedWaypoint &state from entity
    | None -> ()

// Copy an existing entity into a new slot, recording the create that reverses it. The
// copy carries every authored property of the original — a waypoint's link included —
// because the captured value is written into it; it carries no runtime state. What the
// command records is the copy's own authored value, so redo restores the copy rather than
// a second original.
// Dispatching on kind first makes an unsupported kind fail loudly.
let duplicate (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (source: EntityRef) (entity: Entity) : unit =
    let zone = GameplayState.activeZone state
    let fits =
        match source.Kind with
        | EntityKind.Actor -> hasRoom "actor" state.ActiveZone zone.Actors
        | EntityKind.Prop -> hasRoom "prop" state.ActiveZone zone.Props
        | EntityKind.Part ->
            match zone.Environment with
            | Some environment -> hasRoom "part" state.ActiveZone environment.Parts
            | None -> false
        | EntityKind.Waypoint -> hasRoom "waypoint" state.ActiveZone zone.Waypoints
        | EntityKind.Trigger -> hasRoom "trigger" state.ActiveZone zone.Triggers
        | EntityKind.Environment -> true // not duplicable: falls to the `invalidOp` below
    if not fits then () else

    match source.Kind with
    | EntityKind.Actor ->
        let original = Zone.captureActor zone source
        let ref = Zone.spawnActor zone state.Physics shared.Assets entity original.AssetId
        // `spawnActor` opens with the overlay off; the copy takes the original's value.
        do Zone.setShowCollider zone ref original.ShowCollider
        do commit &state (ActorCreate (state.ActiveZone, ref, Zone.captureActor zone ref)) ref
    | EntityKind.Prop ->
        let original = Zone.captureProp zone source
        let binding = { AssetId = original.AssetId; SubmeshIndex = original.SubmeshIndex; SubmeshCount = original.SubmeshCount }
        let ref = Zone.spawnPropBinding zone state.Physics shared.Assets entity binding
        do commit &state (PropCreate (state.ActiveZone, ref, Zone.captureProp zone ref)) ref
    | EntityKind.Part ->
        let original = Zone.capturePart zone source
        let binding = { AssetId = original.AssetId; SubmeshIndex = original.SubmeshIndex; SubmeshCount = original.SubmeshCount }
        let ref = Zone.spawnPartBinding zone state.Physics shared.Assets entity binding
        do commit &state (PartCreate (state.ActiveZone, ref, Zone.capturePart zone ref)) ref
    | EntityKind.Waypoint ->
        // The captured value carries the original's link, so the copy links to the same
        // successor (FR-013). `spawnWaypoint` starts a chain of its own, so the link is
        // written after it.
        let original = Zone.captureWaypoint zone source
        let ref = Zone.spawnWaypoint zone entity
        do Zone.setWaypointNext zone ref original.Next
        do commit &state (WaypointCreate (state.ActiveZone, ref, Zone.captureWaypoint zone ref)) ref
    | EntityKind.Trigger ->
        // The copy carries the authored shape but not the runtime flag, so write the
        // original's value over the shape the spawn created.
        let original = Zone.captureTrigger zone source
        let ref = Zone.spawnTrigger zone entity
        do Zone.setTrigger zone ref original
        do commit &state (TriggerCreate (state.ActiveZone, ref, Zone.captureTrigger zone ref)) ref
    | EntityKind.Environment -> invalidOp "duplicate: the environment is not an addressable entity"
