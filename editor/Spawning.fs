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

// The entity a spawned asset becomes: the whole of what the editor authors for one, before the
// kind's own properties are added. Both panels that spawn by asset build theirs here, so the two
// paths cannot drift apart — a second copy of this literal is the drift (spec 033 FR-007).
let baseEntity (name: string) : Entity =
    { Name = name
      Transform = transformIdentity
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

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
// because the captured value is written into it; it carries no runtime state. What the
// command records is the copy's own authored value, so redo restores the copy rather than
// a second original.
// Dispatching on kind first makes an unsupported kind fail loudly.
let duplicate (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (source: EntityRef) (entity: Entity) : unit =
    let zone = GameplayState.activeZone state
    match source.Kind with
    | EntityKind.Actor ->
        let original = Zone.captureActor zone source
        let ref = Zone.spawnActor zone shared.Physics shared.Assets entity original.AssetId
        // `spawnActor` opens with the overlay off; the copy takes the original's value.
        do Zone.setShowCollider zone ref original.ShowCollider
        do commit &state (ActorCreate (ref, Zone.captureActor zone ref)) ref
    | EntityKind.Prop ->
        let original = Zone.captureProp zone source
        let binding = { AssetId = original.AssetId; SubmeshIndex = original.SubmeshIndex; SubmeshCount = original.SubmeshCount }
        let ref = Zone.spawnPropBinding zone shared.Physics shared.Assets entity binding
        do commit &state (PropCreate (ref, Zone.captureProp zone ref)) ref
    | EntityKind.Part ->
        let original = Zone.capturePart zone source
        let binding = { AssetId = original.AssetId; SubmeshIndex = original.SubmeshIndex; SubmeshCount = original.SubmeshCount }
        let ref = Zone.spawnPartBinding zone shared.Physics shared.Assets entity binding
        do commit &state (PartCreate (ref, Zone.capturePart zone ref)) ref
    | EntityKind.Waypoint ->
        // The captured value carries the original's link, so the copy links to the same
        // successor (FR-013). `spawnWaypoint` starts a chain of its own, so the link is
        // written after it.
        let original = Zone.captureWaypoint zone source
        let ref = Zone.spawnWaypoint zone entity
        do Zone.setWaypointNext zone ref original.Next
        do commit &state (WaypointCreate (ref, Zone.captureWaypoint zone ref)) ref
    | EntityKind.Trigger ->
        // The copy carries the authored shape but not the runtime flag, so write the
        // original's value over the shape the spawn created.
        let original = Zone.captureTrigger zone source
        let ref = Zone.spawnTrigger zone entity
        do Zone.setTrigger zone ref original
        do commit &state (TriggerCreate (ref, Zone.captureTrigger zone ref)) ref
    | EntityKind.Environment -> invalidOp "duplicate: the environment is not an addressable entity"
