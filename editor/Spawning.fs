module Spawning

// The single place the editor creates entities. Every `Zone.spawn*` must be paired
// with the command that reverses it, immediately: recorded refs rely on no untracked
// `Add` reusing a slot they name, or undo/redo hits `SlotMap.Occupy` on a taken slot.
// `AssetBrowser` and `Selection` must not call `Zone.spawn*` directly.

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
    let binding = { AssetId = Some assetId; SubmeshIndex = 0; SubmeshCount = 0 }
    do commit &state (Spawn (ref, entity, binding)) ref

let spawnProp (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (entity: Entity) (assetId: string) : unit =
    let zone = GameplayState.activeZone state
    let ref = Zone.spawnProp zone shared.Physics shared.Assets entity assetId
    let binding = { AssetId = Some assetId; SubmeshIndex = 0; SubmeshCount = 0 }
    do commit &state (Spawn (ref, entity, binding)) ref

// Spawn a waypoint into the active zone. It carries no asset, so no shared state is
// needed; the `Spawn` command's binding is `bindingNone`, which the restore ignores.
let spawnWaypoint (state: byref<GameplayState.GameplayState>) (entity: Entity) : unit =
    let zone = GameplayState.activeZone state
    let ref = Zone.spawnWaypoint zone entity
    do commit &state (Spawn (ref, entity, Zone.bindingNone)) ref

// Create a waypoint in the active zone, link `from` to it, and select the new one. The
// spawn and the link are one command (`LinkSpawn`), so one undo removes the waypoint and
// restores the chain end it extended.
let spawnLinkedWaypoint (state: byref<GameplayState.GameplayState>) (from: EntityRef) (entity: Entity) : unit =
    let zone = GameplayState.activeZone state
    let before = Zone.waypointNext zone from
    let ref = Zone.spawnWaypoint zone entity
    do Zone.setWaypointNext zone from (Some ref)
    do commit &state (LinkSpawn (from, before, ref, entity)) ref

// Copy an existing entity into a new slot, recording the binding it was built from.
// Dispatching on kind first makes an unsupported kind fail loudly.
let duplicate (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (source: EntityRef) (entity: Entity) : unit =
    let zone = GameplayState.activeZone state
    match source.Kind with
    | EntityKind.Actor ->
        let binding = Zone.bindingAt zone source
        match binding.AssetId with
        | None -> failwith "actor without an asset id"
        | Some assetId ->
        let ref = Zone.spawnActor zone shared.Physics shared.Assets entity assetId
        do commit &state (Duplicate (ref, entity, binding)) ref
    | EntityKind.Prop ->
        let binding = Zone.bindingAt zone source
        let ref = Zone.spawnPropBinding zone shared.Physics shared.Assets entity binding
        do commit &state (Duplicate (ref, entity, binding)) ref
    | EntityKind.Part ->
        let binding = Zone.bindingAt zone source
        let ref = Zone.spawnPartBinding zone shared.Physics shared.Assets entity binding
        do commit &state (Duplicate (ref, entity, binding)) ref
    | EntityKind.Waypoint ->
        let ref = Zone.spawnWaypoint zone entity
        do commit &state (Duplicate (ref, entity, Zone.bindingNone)) ref
    | kind -> failwithf "duplicate: kind %A has no entity to duplicate" kind
