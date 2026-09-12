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
    let ref = Zone.spawnActor state.Zone shared.Physics shared.Assets entity assetId
    let binding = { AssetId = Some assetId; SubmeshIndex = 0; SubmeshCount = 0 }
    do commit &state (Spawn (ref, entity, binding)) ref

let spawnProp (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (entity: Entity) (assetId: string) : unit =
    let ref = Zone.spawnProp state.Zone shared.Physics shared.Assets entity assetId
    let binding = { AssetId = Some assetId; SubmeshIndex = 0; SubmeshCount = 0 }
    do commit &state (Spawn (ref, entity, binding)) ref

// Copy an existing entity into a new slot, recording the binding it was built from.
// Dispatching on kind first makes an unsupported kind fail loudly.
let duplicate (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (source: EntityRef) (entity: Entity) : unit =
    match source.Kind with
    | EntityKind.Actor ->
        let binding = Zone.bindingAt state.Zone source
        match binding.AssetId with
        | None -> failwith "actor without an asset id"
        | Some assetId ->
        let ref = Zone.spawnActor state.Zone shared.Physics shared.Assets entity assetId
        do commit &state (Duplicate (ref, entity, binding)) ref
    | EntityKind.Prop ->
        let binding = Zone.bindingAt state.Zone source
        let ref = Zone.spawnPropBinding state.Zone shared.Physics shared.Assets entity binding
        do commit &state (Duplicate (ref, entity, binding)) ref
    | EntityKind.Part ->
        let binding = Zone.bindingAt state.Zone source
        let ref = Zone.spawnPartBinding state.Zone shared.Physics shared.Assets entity binding
        do commit &state (Duplicate (ref, entity, binding)) ref
    | kind -> failwithf "duplicate: kind %A has no entity to duplicate" kind
