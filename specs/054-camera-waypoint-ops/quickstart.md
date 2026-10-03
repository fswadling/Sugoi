# Quickstart: Camera Cue Ops by Waypoint

## Prerequisites

- .NET 10 SDK (`dotnet --version` reports `10.x`).
- Nothing else: **no window, no GPU, no device**. The vocabulary (`world/Cue.fs`) depends on `Prime`
  (already referenced) and `System.Numerics` only; the engine (`world/CueEngine.fs`) also names the gameplay state,
  which the suite builds the way `test/GameplayStateTests.fs` does (`Unchecked.defaultof` for the GPU pieces) and
  populates with waypoints through `Zone.spawnWaypoint`.
- `dotnet build` clean (warnings are errors in this repo).

## Automated coverage (headless)

`dotnet test` runs the extended suites alongside the existing ones. What it asserts, by story:

| Story / requirement | Assertion |
|---|---|
| 1 — snap | A `SetCamera "wp"` with a waypoint at a known position/facing puts the camera exactly there (0 positional, 0 direction error against the waypoint's arrow — the camera looks the way the arrow points, not the opposite) and costs no time (the cue is at the next op in the same step); an unknown name, an empty name, and a name that exists only in a non-active zone each raise where reached. |
| 2 — fly | A `FlyCamera("a", speed)` over a chain a→b→c cuts to `a` on the begin step, is on the path between the endpoints while in flight, is `Fin` at `pathLength/speed`, and ends exactly on `c`'s pose; an unknown name, a chain of one waypoint, and a non-positive speed each raise; a flight survives a zone switch and a later delete of its waypoints (it is pinned); progress never goes backwards. |
| 3 — panel | `CueTree.kinds` offers "Set camera"/"Fly camera" and not "Move camera"; each kind's default; the row labels (`Camera: …`, `Fly: …`, including the in-flight row); `textOf`/`withText` for the two names; the speed drag; and that a `FlyCameraState` reads as its op and is locked. The panel's waypoint combo is device-bound (ImGui), so it is exercised by the interactive walkthrough below rather than the headless suite (FR-019). |
| FR-012 / FR-013 — allocation | 2000 in-flight flight ticks allocate **0 bytes** (measured with `GC.GetAllocatedBytesForCurrentThread`); resolving a path allocates once, bounded and independent of the cue's length. |
| FR-014 — fail loudly | Every malformed case above raises where the op is reached — 0 silent skips. |
| FR-011 — shape | The two ops change neither the shape of the run nor the walk: the existing engine, fork, bounded-shape and determinism tests stay green unchanged. |
| SC-005 — removal | `MoveCamera`, `MoveCameraState` and `CameraPose` appear nowhere in the source. |

## Interactive walkthrough

The engine loads with the game rather than on its own (it reads the active zone and writes the camera), so the
fastest way to *see* the ops is a scratch test in `test/CueTests.fs` using the same headless fixtures. Place
waypoints in the active zone and tick:

```fsharp
// two waypoints in the fresh state's "main" zone, forming a straight 3-unit path on +X
let zone = GameplayState.activeZone state
let a = Zone.spawnWaypoint zone { (entity "a") with Transform = { transformIdentity with Position = Vector3(0.0f, 0.0f, 0.0f) } }
let b = Zone.spawnWaypoint zone { (entity "b") with Transform = { transformIdentity with Position = Vector3(3.0f, 0.0f, 0.0f) } }
do Zone.setWaypointNext zone a (Some b)

// ── snap ──
state.Cue <- SetCamera "b"
CueEngine.tick shared &state 0.0
// state.Camera.Position = b's position; state.Cue = Fin

// ── fly ──
state.Cue <- FlyCamera("a", 1.5)     // 3 units at 1.5/s => duration 2
CueEngine.tick shared &state 0.0     // cuts to a
CueEngine.tick shared &state 1.0     // halfway (+X ~1.5)
CueEngine.tick shared &state 2.0     // lands on b; Fin
```

Then the properties that are about *shape* rather than time:

- **Pinned** — after the flight begins, `do Zone.removeWaypoint zone b` and keep ticking: the camera still lands on
  where `b` was, because the flight resolved its path when it began.
- **Loud** — `state.Cue <- SetCamera "nope"` and tick: it raises, naming the waypoint and the zone.
- **0 bytes** — tick an in-flight `FlyCameraState` 2000 times (its deadline far away) and measure
  `GC.GetAllocatedBytesForCurrentThread`: 0.
- **Combo** — select a `Set camera` node with waypoints in the active zone: the combo lists exactly those waypoint
  names; choosing one relabels the row `Camera: <name>` as one undo step, and a waypoint in another zone is not
  offered (FR-019).

## Expected outcome

- `dotnet test` is green, including the 0-byte flight assertion and the malformed-case raises.
- The scratch tick prints the camera snapping to a waypoint and flying a straight chain, arriving on time.
- In the editor, the cue panel's kind list offers "Set camera" and "Fly camera"; a waypoint is chosen from a combo
  of the active zone's waypoints (FR-019), a flight's speed is dragged, and a running flight's row reads `Fly:
  <path>` and is read-only. With a player live, the follow camera still runs each frame before the cue (FR-018): a
  snap holds for its step, and a flight holds while it runs.
