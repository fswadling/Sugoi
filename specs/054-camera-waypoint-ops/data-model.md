# Data Model: Camera Cue Ops by Waypoint

## `world/Cue.fs` — changed

### The camera leaves, before and after

```fsharp
// REMOVED
| MoveCamera of target: CameraPose * duration: float
| MoveCameraState of from: CameraPose * target: CameraPose * duration: float * endsAt: float
[<Struct>] type CameraPose = { Position: Vector3; Target: Vector3 }

// ADDED, in their place
| SetCamera of waypoint: string       // no progress of its own — performed where reached, no state record
| FlyCamera of path: string * speed: float
| FlyCameraState of path: string * points: Vector3[] * rotations: Quaternion[] * duration: float * endsAt: float
```

- **`SetCamera` is the no-progress leaf**: like `Exposit` and `SwitchZone`, it holds nothing in flight, so it has no
  `…State` sibling (`specs/046`, FR-007/FR-017).
- **`FlyCamera`/`FlyCameraState` is the timed leaf** whose progress is more than a deadline: the authored form names
  a path and a speed; the in-flight form holds what the run needs — the **resolved** positions and facings (one entry
  per waypoint in the chain), the duration, and the deadline. This is the `…State` rule (`specs/046`, FR-017).
- **`points`/`rotations` are arrays, not a deque or list**, because the per-tick spline indexes into them; arrays are
  contiguous and O(1)-indexed, and the value is built once and never mutated (Constitution IV).
- **`path` is carried in the state** so the panel can label the running row as the op it came from (FR-015) without
  reading the zone.
- **`CameraPose` is deleted** — no op uses it once `MoveCamera` leaves (FR-001).
- **Invariants carried over**: the value is immutable — a step returns a new value and mutates nothing; a flight's
  in-flight value is a *snapshot* of the chain at the step it began and never reads the zone again (FR-012).

## `world/Zone.fs` — changed

```fsharp
// The first live waypoint named `name`, in slot order; None if there is none. Names need not be unique.
val tryWaypointByName : zone: Zone -> name: string -> EntityRef option

// The chain starting at `start`, following `Next` links to the end; bounded by the zone's live waypoint
// count, so a cycle terminates. `start` first.
val waypointChain : zone: Zone -> start: EntityRef -> EntityRef list
```

- **`tryWaypointByName`** scans `zone.Waypoints.Slots` for the first live waypoint whose `Base.Name` equals `name`,
  and returns a `waypointRef`. It is `option`, not `voption`: resolution happens once per op, off the per-tick path,
  so the option's allocation does not matter (`waypointNext` already returns `option`).
- **`waypointChain`** is the walk `test/ZoneTests.fs` says "nothing in the app" did before this feature; it now lives
  beside `waypointNext` and `setWaypointNext`. It stops at `None` or after the live count, so a cyclic chain
  terminates (FR-016). A chain that starts at a non-waypoint or a dead ref is not reachable through
  `tryWaypointByName`, which returns live waypoints only.

## `world/CueEngine.fs` — changed

```fsharp
// The two ops' effects, performed where the walk reaches them (the module is compiled after GameplayState,
// so it reads the active zone and writes the camera):

| SetCamera waypoint ->
    // resolve in the active zone; snap the camera to position + facing
| FlyCamera (path, speed) ->
    // resolve the chain once; cut to the first waypoint; hold the deadline
| FlyCameraState (path, points, rotations, duration, endsAt) ->
    // in flight: interpolate, write the camera, report ValueNone; at the deadline, land and report Fin
```

Private helpers that replace the `CameraPose` pair:

```fsharp
val private cameraLookDistance : float32 = 3.5f
val private facing : rotation: Quaternion -> Vector3          // the waypoint's arrow: -UnitZ rotated by rotation
val private setCamera  : state: byref<GameplayState> -> position: Vector3 -> rotation: Quaternion -> unit
```

- **`facing`** is the waypoint's arrow direction (`-UnitZ` rotated by the rotation, the convention
  `editor/Viewport.fs` draws); **`setCamera`** writes `state.Camera.Position` and the derived
  `Target = position + facing * cameraLookDistance` in one place, so the snap and every flight step look the way the
  arrow points.
- **The spline geometry lives in `core/Maths.fs`** (below), not here: `world/CueEngine.fs` `open`s `Maths`.
  `pathLength` samples the spline at a fixed rate per segment (the reference's 10) and sums the segment distances; it
  is the numerator of the duration (FR-006).
- **`advance` keeps its signature and its `Cue voption` answer.** `SetCamera` reports `ValueSome Fin`; `FlyCamera`'s
  begin reports `ValueSome(FlyCameraState …)`; an in-flight step reports `ValueNone`; the landing step reports
  `ValueSome Fin`.
- **No new field on `GameplayState`.** The flight writes the existing `state.Camera`; the follow camera is untouched
  (FR-018).

## `core/Maths.fs` — new

```fsharp
val catmullRom   : p0:Vector3 -> p1:Vector3 -> p2:Vector3 -> p3:Vector3 -> t:float32 -> Vector3
val evalSpline   : points:Vector3[] -> t:float -> Vector3
val evalRotation : rotations:Quaternion[] -> t:float -> Quaternion
val pathLength   : points:Vector3[] -> samplesPerSegment:int -> float
```

- **The spline geometry, out of the cue.** A curve through points and a turn through rotations are neither the
  cue's nor the camera's, so they live in `core/` beside the other pure helpers; nothing here names a zone, screen,
  asset or device (FR-006, FR-007).

## `editor/CueEditor.fs` — changed

```fsharp
// The panel draws a camera op's waypoint as a combo over the active zone's live waypoints (FR-015, FR-019).
val private drawWaypointCombo :
    state: byref<GameplayState> -> idPrefix: string -> path: int list -> node: Cue -> current: string -> unit
```

- **The combo belongs to the panel**, because the panel already holds `GameplayState`: it reads
  `GameplayState.activeZone` and offers `zone.Waypoints.Slots`' live names, writing the choice through
  `CueTree.withAt`/`CueTree.withText`, so the shared control stays zone-agnostic (`specs/047`, FR-006) and the edit is
  the same one `drawText` would make — one `SetCue` step on the one history.
- **Only camera nodes use it.** `SetCamera` and `FlyCamera` route to the combo; `Exposit` and `SwitchZone` keep the
  text field. A name the combo does not offer (a deleted waypoint, or one in another zone) is still shown as the
  combo's preview, so the authored value is never hidden.
- **`withText` is the write path for all four** text-bearing kinds: for a camera op it replaces the waypoint and
  keeps a `FlyCamera`'s speed.

## State transitions (a flight)

| From | Event | To |
|---|---|---|
| `FlyCamera(path, speed)` | the op is reached | a flight in flight: the chain resolved, the camera cut to the first waypoint, `FlyCameraState(path, points, rotations, duration, endsAt)` |
| `FlyCamera(path, speed)` | the op is reached, path length 0 | the camera on the last waypoint's pose, `Fin` (over before it begins) |
| `FlyCameraState …` | a tick before `endsAt` | the same value; the camera at the spline's `t`; `ValueNone` |
| `FlyCameraState …` | a tick at/after `endsAt` | the camera on the last waypoint's pose, `Fin` |
| any | the waypoints edit, delete, or a zone switch | unchanged — the flight is pinned to its resolved path |

## Validation rules

| Rule | Where it is enforced |
|---|---|
| A `SetCamera`/`FlyCamera` name must resolve to a live waypoint in the active zone | `CueEngine`, where the walked op is reached — raises, naming the waypoint and zone (FR-009) |
| A `FlyCamera` path must have at least two waypoints | `CueEngine`, at the op — raises (FR-010) |
| A `FlyCamera` speed must be positive | `CueEngine`, at the op — raises (FR-010) |
| A waypoint chain must terminate | `Zone.waypointChain` — bounded by the live waypoint count (FR-016) |
| A flight's duration may be zero | not an error: it is over before it begins (Decision 5) |
| Duplicate waypoint names | not an error: the first live one in slot order resolves (Decision 2) |
| A camera op's waypoint is chosen from the active zone | `editor/CueEditor.fs` `drawWaypointCombo` offers only `activeZone`'s live waypoints (FR-019); the engine still raises if a name resolves to nothing (FR-009) |

## Deliberately not modelled

- **A raw-pose camera op** — `MoveCamera`'s arbitrary position/look is dropped (FR-001); it returns as its own op if
  ever needed.
- **Camera-follow toggling** — phase 10, not this feature (FR-018).
- **Waypoint authoring** — exists (`specs/026`/`028`); this feature names waypoints, it does not make them.
- **Persistence** — cues are not saved (phase 15); there is no `MoveCamera` to migrate.
- **The rest of the op family** — warps, fades, emotes, enable/disable sets, advent flags, scenario binds stay
  deferred (`specs/046`, research D1).
