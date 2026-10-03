# Phase 0 Research: Camera Cue Ops by Waypoint

The model is settled (`specs/046-cue-core`); this feature corrects one leaf and corrects it *toward the reference*.
Most of the work is deciding where a name resolves, what a flight pins, and how little geometry the port needs.

## Decision 1 — Replace `MoveCamera` with `SetCamera` and `FlyCamera`, both addressed by waypoint

- **Decision**: remove `MoveCamera`/`MoveCameraState` and the `CameraPose` type; add `SetCamera of waypoint: string`
  (a leaf with no progress of its own) and `FlyCamera of path: string * speed: float` with its `FlyCameraState`.
- **Rationale**: `MoveCamera` carried an authored pose the author dragged — a free-floating position and look. This
  game places cameras by **waypoints** (`specs/026`, `specs/028`), and the editor can type a waypoint *name* without
  the control reading the zone registry (`specs/047`, FR-006), which a pose it cannot express. The reference
  (`odin-game`) drives the camera exactly this way: a snap to a named waypoint and a flight along a named chain. The
  fourth shape FR-017 asked for ("progress is more than a deadline") is not lost — `FlyCamera` still proves it, now
  with a `…State` case that carries the resolved path.
- **Alternatives considered**: **keep `MoveCamera` and add the two** — rejected: two ways to move the camera, one of
  which the editor cannot author by name and the game never uses. **Port `SetCamera` only** — rejected: the timed
  camera op (and the shape it proves) would be lost. **Keep `CameraPose` for a future raw-pose op** — rejected: no
  op uses it after `MoveCamera` leaves, and `CameraPose` itself is a struct this repo values; it returns with
  whichever op needs it (research D8 of `046`, and FR-001).

## Decision 2 — A name resolves in the active zone, where the walk reaches the op

- **Decision**: at the step an op is reached, resolve its name against `GameplayState.activeZone`, not the zone that
  was active when the cue was authored, and not any other zone. Add
  `Zone.tryWaypointByName : Zone -> string -> EntityRef option` (first live waypoint of that name, in slot order) and
  `Zone.waypointChain : Zone -> EntityRef -> EntityRef list` (walk `Next`, bounded so a cycle terminates). An
  unresolved name raises.
- **Rationale**: the cue is the screen's and outlives a zone switch (`specs/046`, FR-003), so a name must be read
  against *where the cue is now*. Resolution at reach-time also means a waypoint added after a cue was authored still
  resolves. The helpers belong in `world/Zone.fs` beside `waypointNext`, because a chain is the zone's data; nothing
  in the app walked a chain before this feature (`test/ZoneTests.fs` says so).
- **Alternatives considered**: **resolve with `zone.Waypoints` inline in the engine** — rejected: the two helpers
  are the zone's own vocabulary, and a name lookup and a chain walk are the kind of thing `waypointNext` already is.
  **Require unique names** — rejected: the editor does not enforce it, and `setName` allows duplicates; first-in-slot
  -order is deterministic and matches the reference. **A zone-scoped waypoint index built once** — rejected: a new
  structure for a lookup that happens once per op, not per tick.
- **Cycle bound**: the chain walk stops after the zone's live waypoint count, so an author's cyclic links terminate
  rather than loop forever (FR-016). The bound is the live count, because a simple chain cannot exceed it.

## Decision 3 — A flight's in-flight value carries its resolved path, and is pinned to it

- **Decision**: `FlyCameraState` carries `path: string`, `points: Vector3[]`, `rotations: Quaternion[]`,
  `duration: float`, `endsAt: float`. The engine resolves the chain into `points`/`rotations` **once**, when the op
  begins, and never reads the zone again for that flight.
- **Rationale**: a run is pinned to the values it started from (`docs/CUE_MODEL_DECISION.md`, invariant 5), so a
  waypoint edited, deleted, or left behind by a zone switch while the camera flies must not corrupt it. Arrays (not
  `FDeque`/`list`) give O(1) indexed evaluation, which the per-tick spline needs, and are the constitution's
  preferred contiguous hot-path shape (Principle IV). `path` is carried so the panel can label the running row as
  the op it came from (FR-015) — the same reason `MoveActorState` in the reference carries the actor's name.
- **Alternatives considered**: **re-resolve each tick** — rejected: it would read mutable zone data every frame, break
  the pin, and allocate on the hot path. **Store waypoint refs and look each up per tick** — rejected: same, and it
  would have to handle a vanished waypoint every frame. **Store the path as `EntityRef list`** — rejected: the refs
  are not needed once the transforms are read, and a value pinned to refs still breaks when the refs go stale.

## Decision 4 — A flight is a Catmull-Rom position and a slerp facing; the camera looks along the waypoint's facing

- **Decision**: position follows a Catmull-Rom spline through the waypoint positions (endpoints duplicated), facing
  interpolates through the waypoint rotations (per-segment `Quaternion.Slerp`), duration is the sampled arc length
  divided by the speed, and the camera's look target is `position + facing * cameraLookDistance`, where
  `cameraLookDistance = 3.5f` is a named constant. All of this is private to `world/CueEngine.fs`.
- **Rationale**: it is the reference's behaviour (its `eval_spline` is Catmull-Rom, its look target is
  `pos + quat_forward(rot) * 3.5`), and the clarification's answers fixed facing and smoothing (spec
  Clarifications, 2026-10-03). A named constant for the look distance keeps the magic number out of the handler
  (FR-003). `Vector3.Transform(-Vector3.UnitZ, rotation)` is the waypoint convention the viewport's arrow draws
  (`editor/Viewport.fs`) and `newWaypointAtCamera` authors (`editor/Outliner.fs`, via `CreateWorld`). The actor
  convention (`world/Systems.fs` `forwardOf`) uses `+UnitZ`; a waypoint is judged by its arrow, so the camera follows
  the arrow — the fix made after the first cut looked the opposite way (2026-10-03).
- **Alternatives considered**: **a straight polyline** — rejected by the clarification: the reference smooths, and a
  smooth curve is what a camera path wants. **Facing along the direction of travel** — rejected by the clarification:
  a waypoint already points where the designer aimed it. **SQUAD rotation (the reference's full form)** — deferred:
  per-segment `Quaternion.Slerp` (which already takes the shortest arc) gives a continuous facing through the chain
  and needs no log/exp/squad machinery; SQUAD is a refinement if a multi-waypoint turn ever reads unsatisfactorily.
  **Sample the spline for duration at a finer rate** — not needed: 10 samples per segment is the reference's own
  rate.

## Decision 5 — A flight cuts to its path start, and a degenerate flight is over before it begins

- **Decision**: at the step the flight is reached, place the camera at the first waypoint's pose and return
  `FlyCameraState`; each later tick interpolates and reports `ValueNone`; at `endsAt` place the camera on the last
  waypoint's pose and report `Fin`. A flight whose sampled length is zero (or a speed that makes the duration zero)
  applies the last pose and reports `Fin` in the same step.
- **Rationale**: the clarification fixed the cut-to-start (spec Clarifications). Reporting `ValueNone` while in
  flight is what keeps the tick at 0 bytes (`specs/046`, FR-012). The degenerate case is the same rule the other
  timed ops follow (`specs/046` edge cases; `Wait`/`MoveCamera` are "over before they begin" when their deadline has
  passed), so no new rule enters the walk.
- **Alternatives considered**: **ease from the camera's current pose** — rejected by the clarification. **Do not move
  until the second tick** (the `MoveCamera` pattern) — rejected: the clarification says the camera cuts to the start
  when the flight begins, and op graphs read better when the begin step has a visible result.

## Decision 6 — An unresolvable name, a short chain, or a bad speed raises where the op is reached

- **Decision**: `SetCamera` and `FlyCamera` raise `invalidOp` with the offending name/zone (and the offending value
  for a bad speed) at the step the op is reached, rather than no-op silently. A `FlyCamera` requires a chain of at
  least two waypoints and a positive speed.
- **Rationale**: `GameplayState.switchZone` already raises for an unknown zone, and Constitution II makes
  unexpected state a bug; the reference's silent `nil` would let a cutscene play wrong with no signal. This is the
  rule `specs/046` FR-014 states for a malformed cue.
- **Alternatives considered**: **silent no-op (the reference)** — rejected on the constitution. **A `Result`/`Option`
  failure path** — rejected: an unknown waypoint in a hand-authored cue is a bug, not an expected runtime failure
  (AGENTS.md; Constitution II).

## Decision 7 — A camera op does not touch the follow camera

- **Decision**: `SetCamera`/`FlyCamera` write `state.Camera` and nothing else. Enabling or disabling the player's
  follow camera is not part of this feature.
- **Rationale**: the play chain runs `Systems.cameraFollow` before the cue each frame (`screen/GameplayScreen.fs`),
  so a snap holds only for the step it performs and a flight holds while it rewrites the camera each step. That is
  accepted and recorded (FR-018): the reference toggles follow with its own ops, and toggling follow is phase 10's
  (`docs/ROADMAP.md`, phase 10). Building half of it here would be exactly the "second feature hiding inside this
  one" `specs/046` research D1 rejected for a fade.
- **Alternatives considered**: **suppress follow while a camera op drives** — rejected: it needs a screen flag and an
  enable op to restore follow, i.e. phase 10's mechanism. **Fail loudly when a player is live** — rejected: a camera
  op is legal in a scene that also has a player; it is the follow toggle, not this op, that must change.

## Decision 8 — The panel offers the two camera kinds and a waypoint combo; `CueTree` stays zone-agnostic

- **Decision**: in `editor/CueTree.fs`, the kind list becomes "Set camera" and "Fly camera" (in place of "Move
  camera"); `SetCamera` and `FlyCamera` carry text (their name) through `textOf`/`withText`; `FlyCamera` also has a
  speed `dragNumber`; `FlyCameraState` counts as executing (read-only) and is labelled from its carried `path`. In
  `editor/CueEditor.fs`, the panel draws the selected camera op's waypoint as a combo over the **active zone's live
  waypoints** (read from `GameplayState.activeZone` → `zone.Waypoints.Slots`), writing the chosen name through
  `CueTree.withAt`/`withText`. `CueTree` still names no zone.
- **Rationale**: `CueEditor.draw` already draws the selected node's text through `CueTree.textOf`/`withText`, and the
  numbers through `CueTree.drawEditor`, so the generic path covers both ops once the tables name them (`specs/047`).
  The one thing a typed name cannot give is a name that is guaranteed to resolve: the engine raises on an unknown
  waypoint (D6), so offering the zone's waypoints as a combo spares the author a reach-time error. The combo lives in
  the panel because the panel already holds `GameplayState`; putting it in `CueTree` would make the shared control
  read the zone, which `specs/047` FR-006 forbids and which phase 10's and 11's callers need it not to.
- **Alternatives considered**: **a waypoint dropdown in `CueTree`** — rejected: it would make the control read the
  zone registry (`specs/047`, FR-006). **Keep names typed** — superseded: a name that no waypoint matches is a
  reach-time failure (D6), and the panel can prevent it without costing the control its zone-agnosticism. **Leave
  "Move camera" in the list** — rejected: it must go with the case (FR-001, SC-007).

## Documentation that moves

No `docs/*.md` names `MoveCamera` (checked), so **no doc change**. The comments that do name it are corrected with
the code: the module headers of `world/Cue.fs` and `world/CueEngine.fs` (which name `MoveCameraState`/`MoveCamera` as
the example `…State`/camera op) and the `executing` comment in `editor/CueTree.fs`. The prior spec
(`specs/046-cue-core`) is left as history; this spec supersedes its camera leaf, and a note is not added to a
completed spec.
