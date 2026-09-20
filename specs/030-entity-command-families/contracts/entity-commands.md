# Contract: Entity Commands

Public surface added or changed by this feature. Signatures are illustrative F#.
The change is to **what a command carries**: one create and one delete per kind,
holding that kind's own record, replacing a shared base entity plus an asset
binding. A waypoint's link becomes a field of the waypoint, and the zone's link
table is removed.

## Entity — `world/Entity.fs`

```fsharp
// `Waypoint` gains one field; it now carries its own link.
[<Struct>]
type Waypoint =
    { Base : Entity
      Next : EntityRef option }   // the waypoint this one continues at; None ends a chain
```

`Actor`, `Prop`, `Trigger`, `Entity`, and `AssetBinding` are unchanged, so every
caller that reads an entity's fields — `Viewport`, `Inspector`, `Systems`,
`GameplayScreen` — is untouched.

## Zone — `world/Zone.fs`

```fsharp
// Per kind: read the record as a command records it (derived resources and runtime
// flags cleared, via a record-update so a newly added field is carried). Raises for a
// stale or wrong-kind ref; every caller resolves the ref first.
val captureActor / captureProp / capturePart / captureWaypoint / captureTrigger : zone:Zone -> ref:EntityRef -> 'Record

// Per kind: restore a recorded record at its ref, rebuilding derived resources. Raises
// when the recorded slot is not free. A part restore resolves its environment by epoch.
val occupyActor / occupyProp / occupyPart : zone:Zone -> world:Physics.World -> assets:Assets.AssetManager -> ref:EntityRef -> 'Record -> unit
val occupyWaypoint / occupyTrigger : zone:Zone -> ref:EntityRef -> 'Record -> unit

// Per kind: free the slot and release what the kind owns. No-op for a stale ref.
val removeActor / removeProp / removePart : zone:Zone -> world:Physics.World -> ref:EntityRef -> unit
val removeWaypoint / removeTrigger : zone:Zone -> ref:EntityRef -> unit

// Toggle an actor's collider overlay. A direct write rather than a command: the overlay is
// a visualisation preference, not a scene edit, so it is not an undo step. Both the
// Inspector and a duplicate go through it, so the write has one home.
val setShowCollider : zone:Zone -> ref:EntityRef -> show:bool -> unit

// Unchanged signatures, new storage: the link is the waypoint's `Next`.
val waypointNext    : zone:Zone -> ref:EntityRef -> EntityRef option
val setWaypointNext : zone:Zone -> ref:EntityRef -> next:EntityRef option -> unit
```

- **Removed**: `Links`, `setActorWithAsset`, `removeEntity`, and `occupyTrigger`'s old
  role as the shared restore's trigger branch. `bindingAt` and `bindingNone` are
  retired with the shared payload: with each kind's record captured whole, nothing
  reconstructs the asset half.
- **Unchanged**: `spawnActor`/`spawnProp`/`spawnPropBinding`/`spawnPartBinding`/
  `spawnWaypoint`/`spawnTrigger` (the add-and-return-a-ref path), the generic readers
  `isAlive`/`tryEntityAt`/`setTransform`/`setName`, `tryTriggerAt`, `setTrigger`,
  `defaultTrigger`, `setEnvironment`/`loadEnvironment`, and everything about the
  environment's parts.
- Every `EntityKind` match on a create, delete, or restore path is written out over
  all six kinds: there is no `| _ -> ()` arm, so a kind with no path is an unhandled
  case the compiler reports. F# warns on a non-exhaustive match rather than erroring,
  and the project does not promote that warning: naming the case is the signal FR-006
  asks for, without a project-wide flag.
- Each `occupy*` constructs its record field by field rather than updating the payload,
  so a field added to a kind fails to build until it is classified as authored,
  derived, or runtime (FR-006, SC-004).

## Command — `state/Command.fs`

```fsharp
type Command =
    | Transform of Ref : EntityRef * Before : Transform * After : Transform
    | Rename    of Ref : EntityRef * Before : string * After : string
    | ActorCreate    of Ref : EntityRef * Actor : Actor
    | ActorDelete    of Ref : EntityRef * Actor : Actor
    | PropCreate     of Ref : EntityRef * Prop : Prop
    | PropDelete     of Ref : EntityRef * Prop : Prop
    | PartCreate     of Ref : EntityRef * Part : Prop
    | PartDelete     of Ref : EntityRef * Part : Prop
    | WaypointCreate of Ref : EntityRef * Waypoint : Waypoint
    | WaypointDelete of Ref : EntityRef * Waypoint : Waypoint
    | TriggerCreate  of Ref : EntityRef * Trigger : Trigger
    | TriggerDelete  of Ref : EntityRef * Trigger : Trigger
    | Link       of Ref : EntityRef * Before : EntityRef option * After : EntityRef option
    | TriggerSet of Ref : EntityRef * Before : Trigger * After : Trigger
    | LinkSpawn  of From : EntityRef * Before : EntityRef option * Ref : EntityRef * Waypoint : Waypoint
    | SetEnvironment of Previous : Environment option * Next : Environment option
```

- `KCreate` serves both spawning and duplicating: `apply` occupies the ref with the
  record, `undo` removes it. `KDelete` is the mirror.
- `Spawn`, `Delete`, `Duplicate`, and `TriggerDuplicate` are gone, as is the shared
  `(Entity, AssetBinding)` payload.
- A payload never carries a live resource: a captured record has `Physics = None`,
  `Renderer = default`, and `Triggered = false`.
- A kind with no create, delete, or restore path is a **compiler-reported unhandled
  case**, while a field added to a kind **does** fail the build (through `occupy*`'s
  explicit construction, which record construction checks exactly).
- `UndoStack.push` still merges only consecutive `Transform`s, so every other
  command is exactly one undo step.

## Spawning — `editor/Spawning.fs`

```fsharp
// Unchanged shape: add the entity, push the create that reverses it, select it.
val spawnActor / spawnProp / spawnWaypoint / spawnTrigger : state:byref<GameplayState.GameplayState> -> entity:Entity -> ... -> unit

// The extend gesture, still one command.
val spawnLinkedWaypoint : state:byref<GameplayState.GameplayState> -> from:EntityRef -> entity:Entity -> unit

// A duplicate is a create whose record came from another entity, so the copy carries
// every property of the original — the waypoint's link included — and not its runtime
// state.
val duplicate : shared:SharedState.SharedState -> state:byref<GameplayState.GameplayState> -> source:EntityRef -> entity:Entity -> unit
```

- The module's invariant is unchanged: every `Zone.spawn*` is paired immediately with
  the command that reverses it, so a recorded ref can never name a slot an untracked
  add reused.

## Selection — `editor/Selection.fs`

```fsharp
// Delete, recording the entity by kind (capture + the kind's delete).
val deleteRef : shared:SharedState.SharedState -> state:byref<GameplayState.GameplayState> -> ref:EntityRef -> unit

// Point a waypoint at another, or end its link, as one `Link` step.
val linkRef : shared:SharedState.SharedState -> state:byref<GameplayState.GameplayState> -> ref:EntityRef -> next:EntityRef option -> unit
```

- `deleteRef` loses its trigger special case: every kind now decides its own command
  the same way.
- `renameRef` and `updateTriggerRef` are unchanged; `updateTriggerRef` still pushes a
  `TriggerSet` (FR-004), now simply as the trigger family's property edit.
- Nothing in `Selection` changes what is selected or dragged, and a link edit still
  leaves selection and drag alone.

## Inspector and Viewport

- **Viewport is unchanged**: `buildLines` still calls `Zone.waypointNext` and draws the
  connector, without knowing where the link is stored.
- **The Inspector's overlay toggle moves onto `Zone.setShowCollider`**, which is also
  what a duplicate uses to give a copy the original's overlay, so the write has one
  home. It stays a direct write and is **not** an undo step; the value is carried by the
  record a delete or a copy captures, so it round-trips (FR-003).
- Headlessly, only the *recording* half of FR-003 can be tested: building or releasing an
  actor's renderer needs a GPU device, so undoing an actor's deletion is covered by the
  quickstart's interactive walk.
