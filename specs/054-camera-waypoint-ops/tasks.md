# Tasks: Camera Cue Ops by Waypoint

**Input**: Design documents from `/specs/054-camera-waypoint-ops/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md,
contracts/camera-ops.md, quickstart.md

**Tests**: Not TDD. Both ops are plain data on `state.Camera`, so they are exercised **headlessly** by the existing
suite — the acceptance tests are part of each story's implementation, as every cue spec's tests are (spec FR-014,
FR-017; `specs/046`). No device, window or GPU is opened.

**Organization**: Tasks are grouped by user story. The vocabulary change and the resolution/geometry helpers are
shared foundation (Phases 1–2) and block every story. **US1** (P1) is the snap from a named waypoint; **US2** (P2) is
the flight along a named chain; **US3** (P3) is the panel that authors both by name. The build is expected to be red
between T001 and T008 (the removed cases still have call sites); each checkpoint says when it comes back green.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US3)
- Include exact file paths in descriptions

## Path Conventions

Single-project F# repository. Paths are relative to the repository root (`fsharp-game/`):

- `core/`, `render/`, `world/` (game-domain, incl. the cue), `state/`, `editor/`, `screen/`, `test/`, `specs/`,
  `docs/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the vocabulary change both stories and the panel rest on.

- [X] T001 In `world/Cue.fs`, remove the `MoveCamera` and `MoveCameraState` cases and the `[<Struct>] CameraPose`
  type; add `SetCamera of waypoint: string`, `FlyCamera of path: string * speed: float`, and
  `FlyCameraState of path: string * points: Vector3[] * rotations: Quaternion[] * duration: float * endsAt: float`;
  correct the module header, which names `MoveCameraState` as the example `…State` case. (plan.md Project Structure;
  data-model.md; FR-001, FR-002, FR-004, FR-011.)

**Checkpoint**: the type compiles on its own; every call site of the removed cases is now an error to fix below.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: the name/chain helpers and the geometry must exist before either story can be implemented or
verified.

- [X] T002 In `world/Zone.fs`, add `tryWaypointByName : Zone -> string -> EntityRef option` (the first live waypoint
  whose `Base.Name` equals the name, in slot order) and `waypointChain : Zone -> EntityRef -> EntityRef list` (walk
  `Next` to the end, bounded by `int zone.Waypoints.Count` so a cycle terminates), beside `waypointNext`. (data-model.md;
  research D2; FR-005, FR-016.)
- [X] T003 In `world/CueEngine.fs`, remove the `MoveCamera`/`MoveCameraState` arms and the `applyPose`/`lerpPose`
  helpers; add the private `cameraLookDistance = 3.5f`, a `facing` helper (the waypoint's arrow:
  `Vector3.Transform(-Vector3.UnitZ, rotation)`, matching `editor/Viewport.fs`), and the private geometry:
  `catmullRom` (position), `evalSpline` (endpoint-duplicated, clamped), `evalRotation` (per-segment `Quaternion.Slerp`),
  and `pathLength` (10 samples per segment). Correct the module header, which names `MoveCamera` as the camera op.
  (data-model.md; research D4; FR-003, FR-006, FR-007.)

**Checkpoint**: the helpers are in place and the engine no longer names the removed cases (it still does not handle
the new ones).

---

## Phase 3: User Story 1 - A cue points the camera from a named waypoint (Priority: P1) 🎯 MVP

**Goal**: a `SetCamera` op puts the camera at a named waypoint's position and facing, at no cost in time.

**Independent Test**: place a waypoint of known transform in a zone, reach `SetCamera` naming it, and assert
`state.Camera` (quickstart, "snap").

### Implementation for User Story 1

- [X] T004 [US1] In `world/CueEngine.fs`, handle `SetCamera waypoint` where the walk reaches it: resolve
  `Zone.tryWaypointByName` against `GameplayState.activeZone`, raise `invalidOp` naming the waypoint and the zone when
  it does not resolve (an unknown name and an empty name are the same case), write the camera through the T003
  helper, and report `ValueSome Fin`. (research D2/D6; FR-002, FR-003, FR-008, FR-009.)
- [X] T005 [US1] In `test/CueTests.fs`, add the `SetCamera` scenarios: the camera's position and look direction match
  a waypoint placed in the active zone (0 error); the op costs no time (the cue is at the next op in the same step);
  an unknown name, an empty name, and a waypoint that exists only in a non-active zone each raise; a snap leaves
  `state.Follow` unchanged (record before/after). Add `SetCamera`/`FlyCamera`/`FlyCameraState` to `describe` and drop
  `MoveCamera`. (FR-003, FR-009, FR-014, FR-018; SC-001, SC-004.)

**Checkpoint**: the snap is demonstrable headlessly; the panel is not yet updated, so the app still does not build.

---

## Phase 4: User Story 2 - A cue flies the camera along a waypoint chain (Priority: P2)

**Goal**: a `FlyCamera` op flies the camera along a named chain at a speed and lands on the last waypoint.

**Independent Test**: build a chain of known length, fly at a known speed, and assert arrival and end pose
(quickstart, "fly").

### Implementation for User Story 2

- [X] T006 [US2] In `world/CueEngine.fs`, handle `FlyCamera (path, speed)` and `FlyCameraState`: resolve the chain
  with `Zone.waypointChain` and raise on fewer than two waypoints or a non-positive speed (FR-010); convert the chain
  to `points`/`rotations`; cut the camera to the first waypoint; when the sampled `pathLength` makes the duration
  zero, land on the last pose and report `ValueSome Fin`; otherwise return
  `FlyCameraState(path, points, rotations, duration, now + duration)`. In flight, write the camera at the spline's
  `t` and report `ValueNone`; at `endsAt`, land on the last waypoint's pose and report `ValueSome Fin`. (research
  D3/D5; FR-004, FR-005, FR-006, FR-007, FR-012.)
- [X] T007 [US2] In `test/CueTests.fs`, add the `FlyCamera` scenarios: the camera cuts to the first waypoint on the
  begin step; it is on the path between the endpoints while in flight (progress never backwards); the cue is `Fin` at
  `pathLength/speed` and the camera is exactly on the last waypoint's pose; the flight is pinned — it still lands
  after its waypoints are deleted and after a zone switch; an unknown name, a one-waypoint chain, and a zero/negative
  speed each raise; a cyclic chain (`a`→`b`→`a`) still terminates, landing on its last waypoint rather than looping
  (FR-016); a flight in progress leaves `state.Follow` unchanged (FR-018); and 2000 in-flight ticks allocate 0 bytes
  (`GC.GetAllocatedBytesForCurrentThread`). (FR-005, FR-006, FR-010, FR-012, FR-013, FR-016, FR-018; SC-002,
  SC-003, SC-004.)

**Checkpoint**: both camera ops are demonstrable headlessly; the app still does not build (the panel).

---

## Phase 5: User Story 3 - The panel authors both camera ops by name (Priority: P3)

**Goal**: the cue panel offers the two camera kinds, edits a name (and a flight's speed), and locks a running flight.

**Independent Test**: assert the panel's tables and lock behaviour (quickstart, "panel").

### Implementation for User Story 3

- [X] T008 [US3] In `editor/CueTree.fs`, replace "Move camera" in `kinds` with "Set camera" and "Fly camera"; update
  `kindName`, `defaultOfKind` (`SetCamera ""`, `FlyCamera("", 1.0)`), `label` (`Camera: …`, `Fly: …`), `children`,
  `executing` (`FlyCameraState` is in flight), `textOf`/`withText` (the waypoint and the path), and `drawEditor` (a
  `"Speed"` drag for `FlyCamera`); remove the `MoveCamera` block and correct the `executing` comment that names
  `MoveCameraState`. (research D8; FR-015.) This brings the app build back to green.
- [X] T009 [US3] In `test/CuePanelTests.fs`, update `describe` and the kind/label/default/text tables: the two camera
  kinds are offered and "Move camera" is not; each default; the row labels including a `FlyCameraState` labelled from
  its path; `textOf`/`withText` for both names; and that a `FlyCameraState` is locked. (FR-015; SC-007.)
- [X] T014 [US3] In `editor/CueEditor.fs`, draw a camera op's waypoint as a combo over the active zone's live
  waypoints: route `SetCamera`/`FlyCamera` to it, read `GameplayState.activeZone`, and write the choice through
  `CueTree.withAt`/`CueTree.withText`, so the shared control stays zone-agnostic and a chosen name always resolves
  (FR-015, FR-019; SC-008). Device-bound (ImGui), so validated by the quickstart walkthrough rather than the headless
  suite, like the rest of `CueEditor.draw`.

**Checkpoint**: all three stories work; `dotnet build` has no unresolved case and the app compiles.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: prove the removal and the properties, and record the outcome.

- [X] T010 [P] Grep the source for `MoveCamera`, `MoveCameraState` and `CameraPose` and remove any remaining
  reference (e.g. a stale comment), so SC-005 holds. (SC-005.)
- [X] T011 Build `fsharp-game.fsproj` and `test/fsharp-game.Tests.fsproj` with **0 warnings** and run the full suite
  green; walk `quickstart.md` end to end (the snap, the flight, the pinned flight, the raises, the 0-byte tick).
  (FR-017; SC-006.)
- [X] T013 Move the spline geometry (`catmullRom`, `evalSpline`, `evalRotation`, `pathLength`) out of
  `world/CueEngine.fs` into a new `core/Maths.fs` (added to `fsharp-game.fsproj` after `core/Input.fs`), and have the
  engine `open Maths`; the engine keeps only `cameraLookDistance`, `facing` and `setCamera`. (plan.md Project
  Structure; research D4.)
- [X] T015 Fix the camera facing sign: `world/CueEngine.fs` `facing` uses `-UnitZ` (the waypoint arrow,
  `editor/Viewport.fs`) instead of `UnitZ`, so a snap/flight looks the way the arrow points; `test/CueTests.fs`'s snap
  test computes its expected look from the arrow so the convention cannot regress silently. (FR-003; spec
  Clarifications 2026-10-03.)
- [ ] T012 Commit the change.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies; T001 changes the vocabulary and is the root of everything.
- **Foundational (Phase 2)**: depends on T001; blocks US1 and US2.
- **US1 (Phase 3)**: depends on T002, T003.
- **US2 (Phase 4)**: depends on T002, T003; independent of US1.
- **US3 (Phase 5)**: depends on T001 (the cases it names); T008 is what returns the build to green.
- **Polish (Phase 6)**: depends on all of the above.

### Same-file constraint (sequential)

- `world/Cue.fs`: T001 only.
- `world/CueEngine.fs`: T003 → T004 → T006 (one file, in that order).
- `test/CueTests.fs`: T005 → T007.
- `editor/CueTree.fs`: T008.
- `test/CuePanelTests.fs`: T009.
- `world/Zone.fs`: T002.

### Parallel Opportunities

- T002 (`world/Zone.fs`) and T003 (`world/CueEngine.fs`) can run in parallel once T001 lands.
- T008 and T009 are the same story but different files; T009 can be written against the T008 tables.
- T010 is independent of the code tasks once T001–T009 are done.

---

## Implementation Strategy

### MVP First (User Story 1)

1. T001 (vocabulary), T002/T003 (helpers).
2. T004/T005 (the snap) — verify the snap headlessly.
3. The MVP is a cue that frames the camera from a named waypoint.

### Incremental Delivery

1. T001–T003 → foundation.
2. US1 (T004–T005) → the snap.
3. US2 (T006–T007) → the flight, pinned and timed.
4. US3 (T008–T009, T014) → the panel; the build is green again.
5. Polish (T010–T012).

---

## Notes

### Implementation status (2026-10-03)

- **T001–T011, T013, T014: done.** Both projects build with **0 warnings** and `dotnet test` passes (428 tests).
  `MoveCamera`/`MoveCameraState`/`CameraPose` are gone; `SetCamera`, `FlyCamera`/`FlyCameraState` and the spline
  geometry in `core/Maths.fs` are in; the panel's waypoint combo (T014, FR-019) is the follow-up that supersedes the
  earlier "a name is typed" decision — see spec Clarifications, research D8, and the contract.
- **T015 (facing-sign fix): done.** The first cut looked along `+UnitZ`, the opposite of the waypoint arrow
  (`-UnitZ`, `editor/Viewport.fs`); `CueEngine.facing` and the snap test now follow the arrow (FR-003; spec
  Clarifications 2026-10-03). No artefact asserted the sign beyond the test's literal, which is now computed from the
  arrow so the convention cannot regress silently.
- **T012 (commit): still open.** The tree is uncommitted.

- Tests are part of each story, not a separate phase: both ops are assertable headlessly, so the acceptance
  scenarios live in `test/CueTests.fs`/`test/CuePanelTests.fs`.
- The build is red from T001 until T008 (the removed cases have call sites in `CueTree.fs` and the tests). This is
  expected; the checkpoints mark it.
- No `docs/*.md` names `MoveCamera`, so there is no doc task; only the code comments are corrected with the code
  (T001, T003, T008 and T014).
- **The panel combo (T014) is device-bound** — it draws ImGui against `GameplayState.activeZone` — so it has no
  headless test; the quickstart's interactive walkthrough is its validation, as for the rest of the panel's drawing.
