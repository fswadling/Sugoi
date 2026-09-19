# Contract: Waypoints

Public surface added or changed by this feature. Signatures are illustrative F#.
A waypoint becomes a stored entity kind composing the shared `Entity` base; the
zone gains a waypoint slot map and the operations that address it; the editor
gains a list with two add actions and a viewport marker. Nothing else changes.

## Entity — `world/Entity.fs`

```fsharp
// A point of interest placed in a zone: where to stand and which way to face.
// Composes the shared base entity (name, transform, editor flags); carries no
// renderer, collider, or asset binding.
[<Struct>]
type Waypoint =
    { Base : Entity }

// A ref to a waypoint slot (kind = Waypoint, epoch = 0).
val waypointRef : index:uint32 -> generation:uint32 -> EntityRef
```

- `EntityKind.Waypoint` was already in the vocabulary; it now addresses stored
  data rather than being unconditionally absent.
- A waypoint's position and rotation are `Base.Transform.Position` and
  `Base.Transform.Rotation`; its facing direction is the rotation applied to
  −Z.
- `Trigger` is unchanged and still unstored.

## Zone — `world/Zone.fs`

```fsharp
// Added field (capacity 128), alongside Actors (64) and Props (32):
//   Waypoints : SlotMap<Waypoint>

// A binding with no asset, for kinds that carry none (a waypoint).
val bindingNone : AssetBinding        // { AssetId = None; SubmeshIndex = 0; SubmeshCount = 0 }

// Spawn a waypoint from its base entity and return its ref. No world or assets
// are needed: a waypoint has no renderer and no body.
val spawnWaypoint : zone:Zone -> entity:Entity -> EntityRef
```

Existing operations that gain a waypoint case (signatures unchanged):

| Operation | Added behaviour |
|---|---|
| `isAlive` | A live waypoint slot makes the ref alive (was: never alive). |
| `tryEntityAt` | Returns `Some waypoint.Base`. |
| `setTransform` | Writes the base transform in place; no body to recreate. |
| `removeEntity` | Frees the slot; nothing to destroy. |
| `setActorWithAsset` | Occupies the slot with `{ Base = entity }` (the undo-restore path), after `ensureSlotFree`. |
| `bindingAt` | Returns `bindingNone` for a waypoint; other kinds without a binding still raise. |

- A waypoint ref resolves only against `Zone.Waypoints`; a stale ref (wrong
  generation) resolves to `None`, so a deleted waypoint's ref cannot reach a
  waypoint that reused its slot.
- `setTransform`/`removeEntity` on a stale or absent waypoint ref are no-ops, as
  for actors, props, and parts.
- Occupying a non-free waypoint slot raises.
- A waypoint never appears in `BodyToEntity`: it is never added to the physics
  world.

## Spawning — `editor/Spawning.fs`

```fsharp
// Create a waypoint in the active zone, push the command that reverses it, and
// select it.
val spawnWaypoint : state:byref<GameplayState.GameplayState> -> entity:Entity -> unit
```

- `duplicate` gains a `Waypoint` case (`Zone.spawnWaypoint` + a `Duplicate`
  command with `bindingNone`). Duplicating `Environment` or `Trigger` still
  raises.
- The module's invariant holds for waypoints: `Zone.spawnWaypoint` is called only
  here (and in tests), so every spawn is immediately paired with its reversing
  command and a recorded ref can never name a slot that a later untracked spawn
  reused.

## Outliner — `editor/Outliner.fs`

- A **Waypoints** collapsing header at the end of the active zone's entity list,
  after **Triggers**, drawing:
  - one row per live waypoint, using the shared `entityRow` (click the name to
    select; inline *Del* and *Dup* buttons calling `Selection.deleteRef` /
    `Selection.duplicateRef`);
  - *Add Waypoint* → a waypoint at the origin with identity facing;
  - *Add at Camera* → a waypoint at the editor camera's position, rotated to face
    the camera's view direction
    (`Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(camera.Position,
    Camera.forward camera, camera.Up))`);
  - `(none)` when the zone has no waypoints.
- New waypoints are named `Waypoint`; the name is display-only (the Inspector
  shows it for the selection).

## Viewport — `editor/Viewport.fs`

- `buildLines` appends, per live waypoint in the active zone: a wireframe box at
  its position, a shaft along its facing of length `gizmoLen` (0.75), and a head
  of four barbs from the shaft tip to `basePoint ± right` and `basePoint ± up`,
  where `basePoint = tip − forward · headLen` (0.25), `right`/`up` are the
  rotation's local X/Y axes scaled by `headRadius` (0.12), and `forward` is the
  rotation applied to −Z.
- Colour is the selection colour when the waypoint is selected, white otherwise.
- The marker is appended to the gizmo accumulator, so it depth-tests against the
  scene and is drawn after the meshes.
- No `DebugDraw` primitive is added: the head is composed from `line` calls at
  its single call site.
