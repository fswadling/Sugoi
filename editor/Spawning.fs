module Spawning

// The single place the editor creates entities. Every `Zone.spawn*` must be paired
// with the command that reverses it, immediately: recorded refs rely on no untracked
// `Add` reusing a slot they name, or undo/redo hits `SlotMap.Occupy` on a taken slot.
// `AssetBrowser` and `Selection` must not call `Zone.spawn*` directly.
//
// Each command carries the new entity's record as `Zone.capture*` takes it, so a copy
// carries every authored property of its original and neither carries runtime state.

open Entity
open Command

// Push the command that reverses a freshly-created entity, and select it.
let private commit (state: byref<GameplayState.GameplayState>) (cmd: Command) (ref: EntityRef) : unit =
    let undo = UndoStack.push state.Editor.Undo cmd
    state.Editor <- { state.Editor with Selection = Some ref; Undo = undo }

// Spawn a whole actor or prop asset into the active zone.
let spawnActor (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (entity: Entity) (assetId: string) : unit =
    let zone = GameplayState.activeZone state
    let ref = Zone.spawnActor zone shared.Physics shared.Assets entity assetId
    do commit &state (ActorCreate (ref, Zone.captureActor zone ref)) ref

let spawnProp (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (entity: Entity) (assetId: string) : unit =
    let zone = GameplayState.activeZone state
    let ref = Zone.spawnProp zone shared.Physics shared.Assets entity assetId
    do commit &state (PropCreate (ref, Zone.captureProp zone ref)) ref

// Spawn a waypoint into the active zone. It carries no asset, so no shared state is
// needed.
let spawnWaypoint (state: byref<GameplayState.GameplayState>) (entity: Entity) : unit =
    let zone = GameplayState.activeZone state
    let ref = Zone.spawnWaypoint zone entity
    do commit &state (WaypointCreate (ref, Zone.captureWaypoint zone ref)) ref

// Spawn a trigger into the active zone. It carries no asset and no body, so no shared
// state is needed.
let spawnTrigger (state: byref<GameplayState.GameplayState>) (entity: Entity) : unit =
    let zone = GameplayState.activeZone state
    let ref = Zone.spawnTrigger zone entity
    do commit &state (TriggerCreate (ref, Zone.captureTrigger zone ref)) ref

// Create a waypoint in the active zone, link `from` to it, and select the new one. The
// spawn and the link are one command (`LinkSpawn`), so one undo removes the waypoint and
// restores the chain end it extended.
let spawnLinkedWaypoint (state: byref<GameplayState.GameplayState>) (from: EntityRef) (entity: Entity) : unit =
    let zone = GameplayState.activeZone state
    let before = Zone.waypointNext zone from
    let ref = Zone.spawnWaypoint zone entity
    do Zone.setWaypointNext zone from (Some ref)
    do commit &state (LinkSpawn (from, before, ref, Zone.captureWaypoint zone ref)) ref

// Copy an existing entity into a new slot, recording the create that reverses it. The
// copy carries every authored property of the original — a waypoint's link included —
// because the payload is the captured record; it carries no runtime state.
// Dispatching on kind first makes an unsupported kind fail loudly.
let duplicate (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (source: EntityRef) (entity: Entity) : unit =
    let zone = GameplayState.activeZone state
    match source.Kind with
    | EntityKind.Actor ->
        let record = Zone.captureActor zone source
        let ref = Zone.spawnActor zone shared.Physics shared.Assets entity record.AssetId
        // `spawnActor` opens with the overlay off; the copy takes the original's value, so
        // the live copy matches the command that reverses it.
        do Zone.setShowCollider zone ref record.ShowCollider
        do commit &state (ActorCreate (ref, record)) ref
    | EntityKind.Prop ->
        let record = Zone.captureProp zone source
        let binding = { AssetId = record.AssetId; SubmeshIndex = record.SubmeshIndex; SubmeshCount = record.SubmeshCount }
        let ref = Zone.spawnPropBinding zone shared.Physics shared.Assets entity binding
        do commit &state (PropCreate (ref, record)) ref
    | EntityKind.Part ->
        let record = Zone.capturePart zone source
        let binding = { AssetId = record.AssetId; SubmeshIndex = record.SubmeshIndex; SubmeshCount = record.SubmeshCount }
        let ref = Zone.spawnPartBinding zone shared.Physics shared.Assets entity binding
        do commit &state (PartCreate (ref, record)) ref
    | EntityKind.Waypoint ->
        // The captured record carries the original's link, so the copy links to the same
        // successor (FR-013). `spawnWaypoint` starts a chain of its own, so the link is
        // written after it, keeping the live copy in step with the command.
        let record = Zone.captureWaypoint zone source
        let ref = Zone.spawnWaypoint zone entity
        do Zone.setWaypointNext zone ref record.Next
        do commit &state (WaypointCreate (ref, record)) ref
    | EntityKind.Trigger ->
        // The copy carries the authored shape but not the runtime flag, so write the
        // captured record over the shape the spawn created.
        let record = Zone.captureTrigger zone source
        let ref = Zone.spawnTrigger zone entity
        do Zone.setTrigger zone ref record
        do commit &state (TriggerCreate (ref, record)) ref
    | EntityKind.Environment -> invalidOp "duplicate: the environment is not an addressable entity"
