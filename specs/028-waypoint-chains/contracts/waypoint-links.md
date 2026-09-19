# Contract: Waypoint Links

Public surface added or changed by this feature. Signatures are illustrative F#.
A chain is a zone-level map from a waypoint's reference to its next, so the
waypoint record and the shared spawn/delete/duplicate commands are untouched.

## Zone — `world/Zone.fs`

```fsharp
// Added field, initialised in Zone.create:
//   Links : Dictionary<EntityRef, EntityRef>   // source waypoint -> its next

// The waypoint a waypoint's chain continues at, if that link still resolves. Pure:
// requires the source to be a live waypoint, the entry to exist, and the target to
// resolve as a live waypoint; otherwise None. An unresolvable link is left in place.
val waypointNext : zone:Zone -> ref:EntityRef -> EntityRef option

// Point a waypoint at another (Some target) or end its chain (None). Raises on a
// self-link and on a source or target that is not a waypoint; does not require the
// target to be alive (a restored link may be momentarily inert).
val setWaypointNext : zone:Zone -> ref:EntityRef -> next:EntityRef option -> unit
```

- Nothing else in `Zone` changes: `spawnWaypoint`, `removeEntity`, `setTransform`,
  `setName`, `tryEntityAt`, and `bindingAt` behave exactly as before. In
  particular a spawned or duplicated waypoint has no link, and a delete leaves the
  map alone.
- Because the map is a field of the zone, chains are zone-scoped automatically and
  a link cannot cross zones.

## Commands — `state/Command.fs`

```fsharp
// A link edit: the next before and after, either of which may be absent.
| Link of Ref : EntityRef * Before : EntityRef option * After : EntityRef option

// The extend gesture as one step: create `Ref` from `Entity` and point `From` at it.
| LinkSpawn of From : EntityRef * Before : EntityRef option * Ref : EntityRef * Entity : Entity
```

- `apply`/`undo` for `Link` write `After`/`Before` through `Zone.setWaypointNext`.
- `apply` for `LinkSpawn` creates the waypoint through the existing waypoint branch
  of `setActorWithAsset` and links it; `undo` restores `Before` and removes it.
- Neither merges with anything: `UndoStack.push` merges only consecutive
  `Transform` commands, so one link edit is exactly one undo step.

## Spawning — `editor/Spawning.fs`

```fsharp
// Create a waypoint in the active zone and link `from` to it, pushing the single
// command that reverses both and selecting the new waypoint. No shared state is
// needed: waypoints have no body and no asset.
val spawnLinkedWaypoint : state:byref<GameplayState.GameplayState> -> from:EntityRef -> entity:Entity -> unit
```

- Follows the module's invariant: the spawn and the command that reverses it are
  recorded together, and `Zone.spawnWaypoint` is still called only from here (and
  in tests).

## Selection — `editor/Selection.fs`

```fsharp
// Point `ref` at `next` (Some), or break its link (None), as one undoable step.
// No-op when it already points there; leaves selection and drag untouched.
val linkRef : shared:SharedState.SharedState -> state:byref<GameplayState.GameplayState> -> ref:EntityRef -> next:EntityRef option -> unit
```

## Outliner — `editor/Outliner.fs`

The Waypoints section gains:

- **Add Next at Camera** — `Spawning.spawnLinkedWaypoint` with a waypoint built at
  `state.Camera.Position` and the camera's view-facing rotation (the same rotation
  the existing *Add at Camera* builds). Disabled unless the selection is a waypoint.
- **Unlink** — `Selection.linkRef … None`. Disabled unless the selected waypoint has
  a resolvable next.
- a per-row **Link** button, drawn after the shared *Del*/*Dup* buttons by
  continuing the same line the row left open. Disabled unless a different waypoint
  is selected. It links the *selected* waypoint to that row's waypoint.

All three are disabled rather than hidden, and none is offered in play mode (the
section is drawn only while editing).

## Viewport — `editor/Viewport.fs`

- `buildLines` draws, for every live waypoint whose next resolves: a line from the
  waypoint to the midpoint of the segment toward its next lifted `0.08` in Y, and a
  two-wing arrow head at that midpoint (wings `0.07` either side of the direction's
  perpendicular, from a base `0.15` back), all in a dedicated link colour
  (200, 122, 255).
- Nothing is drawn when the two waypoints coincide (within `0.001`), since there is
  no direction to show and normalising a zero delta would emit invalid vertices.
- The connector is appended to the same gizmo accumulator as the waypoint markers,
  so it depth-tests with the scene and is drawn only while editing.
