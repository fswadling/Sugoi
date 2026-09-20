# Contract: Authored State

Public surface added or changed by this feature. Signatures are illustrative F#. The
change is that an entity's authored state becomes a type of its own, so a command payload
cannot carry a body, a renderer, or a runtime flag.

## Entity — `world/Entity.fs`

```fsharp
// Added: the authored value of each kind that carries something derived or runtime.
[<Struct>] type ActorState   = { Base : Entity; AssetId : string; ShowCollider : bool }
[<Struct>] type PropState    = { Base : Entity; AssetId : string option; SubmeshIndex : int; SubmeshCount : int }
[<Struct>] type TriggerState = { Base : Entity; Radius : float32; Height : float32; Interactable : bool; Prompt : string }

// Changed: the entity holds its authored value and keeps its derived fields as siblings,
// and there are no accessors — an authored read is `x.State.…`.
[<Struct>]
type Actor =
    { State : ActorState
      mutable Physics : PhysicsAttachment<CapsuleCollider> option
      mutable Renderer : ActorRender.ActorRender }

[<Struct>]
type Prop =
    { State : PropState
      mutable Physics : PhysicsAttachment<MeshCollider> option
      mutable Renderer : EnvironmentRender.EnvironmentRender }

[<Struct>]
type Trigger =
    { State : TriggerState
      mutable Triggered : bool }        // runtime, recomputed in place
```

- **Unchanged**: `Entity`, `Waypoint`, `Environment`, `AssetBinding`, all renderer and
  attachment types. A waypoint is already pure authored state, so it is its own payload.
- **Reads** of authored properties are spelled through `State`
  (`a.State.Base.Transform.Position`, `t.State.Radius`); there are no read-through
  accessors. **Writes** are whole-record copy-and-updates on an authored path
  (`a <- { a with State.ShowCollider = show }`); an in-place write through `State` does not
  compile, so the compiler finds every write site.
- Construction sites change shape: `{ Base = …; AssetId = …; Physics = None; … }` becomes
  `{ State = { Base = …; AssetId = … }; Physics = None; … }`.

## Zone — `world/Zone.fs`

```fsharp
// Resolve a ref to the value a command carries. Raises for a stale or wrong-kind ref.
// Reads only — there is no clearing step (FR-005).
val captureActor / captureProp / capturePart / captureTrigger : zone:Zone -> ref:EntityRef -> 'State
val captureWaypoint : zone:Zone -> ref:EntityRef -> Waypoint          // unchanged

// Build a live entity from an authored value, producing every derived resource.
// Field by field, so a new derived field fails to build until it is produced.
val occupyActor / occupyProp / occupyPart : zone:Zone -> world:Physics.World -> assets:Assets.AssetManager -> ref:EntityRef -> 'State -> unit
val occupyWaypoint / occupyTrigger : zone:Zone -> ref:EntityRef -> 'State -> unit

// The spawn builders keep their present parameters and assemble the authored value
// inside, which is where a new authored field's initial value is decided — the site US2's
// experiment lands on. A caller that already holds an authored value occupies a slot with
// it rather than spawning.
val spawnActor / spawnProp : zone:Zone -> world:Physics.World -> assets:Assets.AssetManager -> entity:Entity -> assetId:string -> EntityRef
val spawnPropBinding / spawnPartBinding : zone:Zone -> world:Physics.World -> assets:Assets.AssetManager -> entity:Entity -> binding:AssetBinding -> EntityRef
val spawnWaypoint / spawnTrigger : zone:Zone -> entity:Entity -> EntityRef
```

- The `spawn*` builders are unchanged in signature: they keep the `Entity` (and the asset
  or binding) a caller passes and build the authored value inside.
- `setTrigger` takes a `TriggerState` and does not touch `Triggered`.
- `setShowCollider`, `setName`, `setTransform`, and `setWaypointNext` write the authored
  value; their signatures are unchanged.
- `removeActor`/`removeProp`/`removePart`/`removeWaypoint`/`removeTrigger` are unchanged.
- `deactivateEnvironment` and `attachPart` are behaviourally unchanged: a swap still
  clears a part's resource fields and rebuilds them, which is a lifecycle behaviour
  rather than a way to make a record safe (FR-011).

## Command — `state/Command.fs`

```fsharp
| ActorCreate    of Ref : EntityRef * Actor : ActorState
| ActorDelete    of Ref : EntityRef * Actor : ActorState
| PropCreate     of Ref : EntityRef * Prop : PropState
| PropDelete     of Ref : EntityRef * Prop : PropState
| PartCreate     of Ref : EntityRef * Part : PropState
| PartDelete     of Ref : EntityRef * Part : PropState
| TriggerCreate  of Ref : EntityRef * Trigger : TriggerState
| TriggerDelete  of Ref : EntityRef * Trigger : TriggerState
| TriggerSet     of Ref : EntityRef * Before : TriggerState * After : TriggerState
```

- `WaypointCreate`/`WaypointDelete` still carry a `Waypoint`; `Link`, `LinkSpawn`,
  `Transform`, `Rename`, and `SetEnvironment` are unchanged.
- A payload can no longer express a physics attachment, a renderer, or a runtime flag.
- `apply`/`undo` bodies are otherwise unchanged, and `UndoStack.push` still merges only
  consecutive transforms.

## Inspector — `editor/Inspector.fs`

```fsharp
// The trigger property edit is expressed over the authored value.
val updateTriggerRef : shared:SharedState.SharedState -> state:byref<GameplayState.GameplayState> -> ref:EntityRef -> change:(TriggerState -> TriggerState) -> unit
```

- The overlay checkbox keeps going through `Zone.setShowCollider` and stays off the undo
  stack; the trigger fields (radius, height, interactable, prompt, enabled) now edit the
  authored value, and each still records exactly one `TriggerSet`.
- `Selection` and `Spawning` change only in reading the authored value where they
  previously read the live record; their behaviour and their command pairing are unchanged.

## Not changed

- `Waypoint`, `Entity`, `Environment`, and every renderer and attachment type.
- The per-kind storage in a zone, the undo stack, and the undo granularity of every
  gesture.
- `render/`, `physics/`, and `core/` — none of them reads an entity's authored state, so
  this feature does not reach them; `screen/GameplayScreen.fs` changes only in that its
  three authored reads are spelled through `State`.
