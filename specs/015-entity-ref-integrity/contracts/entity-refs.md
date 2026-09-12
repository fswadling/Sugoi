# Contract: Entity Reference Integrity

Public surfaces touched by this feature. Signatures are illustrative F#, matching
the data model.

## Slot map API — `core/SlotMap.fs`

```fsharp
type SlotMap<'T when 'T : struct>(capacity: uint) =
    member Slots   : ReadOnlySpan<Slot<'T>>
    member Count   : uint32
    member Add     : value:'T -> Handle
    member Get     : handle:Handle -> byref<'T>
    member Get     : index:uint32 * generation:uint32 -> byref<'T>
    member Free    : handle:Handle -> unit
    member IsAlive    : index:uint32 * generation:uint32 -> bool
    member IsOccupied : index:uint32 -> bool
    member TryGet     : index:uint32 * generation:uint32 -> 'T option
    member Occupy     : index:uint32 * generation:uint32 * value:'T -> unit
    member IteriAlive : (uint32 -> uint32 -> 'T -> unit) -> unit
    member RemoveByIndex : index:uint32 * generation:uint32 * cleanup:('T -> unit) option -> unit
```

## Reference resolution — `world/Entity.fs`, `world/Zone.fs`

```fsharp
type EntityRef = { Kind : EntityKind; Index : uint32; Generation : uint32; Epoch : uint32 }
val actorRef : uint32 -> uint32 -> EntityRef
val propRef  : uint32 -> uint32 -> EntityRef
val partRef  : uint32 -> uint32 -> uint32 -> EntityRef

val partsFor    : Zone -> EntityRef -> SlotMap<Prop> option
val isAlive     : Zone -> EntityRef -> bool
val tryEntityAt : Zone -> EntityRef -> Entity option

val Zone.setTransform     : Zone -> Physics.World -> EntityRef -> Transform -> unit
val Zone.removeEntity     : Zone -> Physics.World -> EntityRef -> unit
val Zone.setActorWithAsset : Zone -> Physics.World -> Assets.AssetManager
                              -> EntityRef -> Entity -> AssetBinding -> unit
```

## Creation surface — `editor/Spawning.fs`

```fsharp
val spawnActor : SharedState -> byref<GameplayState> -> Entity -> string -> unit
val spawnProp  : SharedState -> byref<GameplayState> -> Entity -> string -> unit
val duplicate  : SharedState -> byref<GameplayState> -> EntityRef -> Entity -> unit
```

## Selection — `editor/Selection.fs`

```fsharp
val dropStalePartSelection : byref<GameplayState> -> unit
```

## Rules

- **Incarnation identity.** A reference names a specific slot incarnation via
  `Generation`, plus (for parts) the owning environment via `Epoch`. A reference is
  live only if its kind's store has that `(index, generation)` alive and, for a
  part, `Epoch` equals the loaded environment's.
- **Stale refs are inert, not aliasing.** `tryEntityAt` returns `None`,
  `isAlive` returns `false`, `setTransform`/`removeEntity` are no-ops, and
  `bindingAt` raises. No stale ref may read or mutate a same-index slot belonging
  to another entity or environment.
- **Parts are environment-scoped.** `partsFor` returns `Some` only when the ref's
  epoch matches the loaded environment. A part ref from a previous environment
  never resolves against the current environment, even at the same index and
  generation.
- **Environment swaps preserve placed content.** `loadEnvironment`/`setEnvironment`
  tear down and rebuild only the current environment's parts; `Zone.Props`
  (placed props) and `Zone.Actors` are untouched. A swap keeps the parts map, so
  its slots and generations survive and a later swap back revives the same refs.
- **Restore is strict.** `Occupy` raises if the target slot is already alive;
  `ensureSlotFree` checks before allocating renderer/physics resources. A restore
  onto a slot taken by an untracked `Add` reports an error rather than overwriting.
- **Creation is always undo-paired.** Only `editor/Spawning.fs` calls
  `Zone.spawn*`; each call immediately pushes the `Spawn`/`Duplicate` command that
  reverses it. `AssetBrowser`/`Selection` must not call `Zone.spawn*` directly.
- **Environment swaps are commands.** `Command.SetEnvironment` stores the previous
  and next `Environment` values, so undo/redo reactivate the exact environment.
  The undo stack is LIFO, so a swap is undone before any part commands recorded
  after it — which is why a part restore always finds its environment loaded.
- **Selection reconcile.** After an environment load and after every undo/redo
  step, a Part selection that no longer resolves is cleared (with any drag);
  Actor/Prop selections are left alone (they may resolve again after undo).
- **Transform coalescing is incarnation-aware.** Consecutive `Transform` commands
  merge only when their full `EntityRef` values are equal.
