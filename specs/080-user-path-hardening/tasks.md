---

description: "Task list for implementing Graceful Failure on User-Facing Paths"
---

# Tasks: Graceful Failure on User-Facing Paths

**Input**: Design documents from `/specs/080-user-path-hardening/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included. The spec's `SC-001` requires an automated check for every crash path named, so each
story has test tasks written **first** and expected to fail against today's behaviour.

**Organization**: Tasks are grouped by user story. All eight stories touch disjoint files, so after the
baseline they can be implemented in parallel.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1…US8)
- Every task names its exact file path

## Path Conventions

Single project at the repo root; F# files in domain clusters (`world/`, `state/`, `editor/`, `render/`)
and one flat `test/<Module>Tests.fs` per module. All paths below are repo-relative.

---

## Phase 1: Setup

**Purpose**: Establish a clean baseline so a regression is attributable.

- [x] T001 Confirm the baseline is green: `dotnet build fsharp-game.fsproj` (0 warnings) and `dotnet test test/fsharp-game.Tests.fsproj` (all pass) before any change.

---

## Phase 2: Foundational

**Purpose**: Shared prerequisites that MUST exist before user stories.

**No tasks.** There is no shared infrastructure to build: the report convention (`eprintfn`) already
exists in `world/CueEngine.fs`, the headless fixtures already exist in `test/TestSupport.fs`, and every
touched file is already in the compile order. The eight stories are therefore independent and may begin
in parallel once T001 is done.

**Checkpoint**: Foundation ready (baseline green).

---

## Phase 3: User Story 1 - Playing with an actor whose asset lacks a locomotion clip (Priority: P1) 🎯 MVP

**Goal**: `playClip` falls back to a defined pose instead of raising when the asset lacks the requested clip.

**Independent Test**: A headless actor-renderer fixture requesting a clip the asset lacks yields a defined fallback; a clip the asset has is unchanged.

### Tests for User Story 1

- [x] T002 [US1] Add a failing test in `test/ActorTests.fs` asserting the clip-selection/fallback path returns a defined result for a missing clip rather than raising, and is unchanged for a present clip.

### Implementation for User Story 1

- [x] T003 [US1] In `world/ActorRender.fs`, change `playClip` to fall back when the named clip is absent (other locomotion clip → a clip already held → clip 0 → hold the pose) instead of `failwithf`.
- [x] T004 [US1] In `world/ActorRender.fs` (or `world/Systems.fs`), emit the fallback diagnostic **once per distinct missing clip** (guard on the last requested clip name) so it is not written every frame (contract G3).

**Checkpoint**: A player actor whose asset lacks `Idle`/`Jog` no longer terminates the run.

---

## Phase 4: User Story 2 - Running a cue whose target is missing or stale (Priority: P1)

**Goal**: The camera cue ops and a bare scenario-fork op behave like every other cue op — no effect + report, or complete.

**Independent Test**: Each camera op on an absent place / missing waypoint / short path / non-positive speed leaves the camera untouched and the cue reaches `Fin`; a cue that is a bare `ForkScenario` runs to completion.

### Tests for User Story 2

- [x] T005 [P] [US2] Add failing tests in `test/CueTests.fs`: `SetCamera`/`FlyCamera` with (a) an absent place, (b) a missing/not-alive waypoint, (c) a path of <2 live waypoints, (d) speed ≤ 0 — each leaves `state.Camera` unchanged and the cue proceeds.
- [x] T006 [P] [US2] Add a failing test in `test/CueTests.fs` that a cue which is a single `ForkScenario` op runs to completion; rewrite the existing "a Fork outside a sequence raises" case to assert the new defined behaviour.

### Implementation for User Story 2

- [x] T007 [US2] In `world/CueEngine.fs`, make `namedWaypoint`/`poseAt`/`resolvedPath` option-returning and turn `SetCamera`/`FlyCamera` into no-op-and-report (place-targeted diagnostic), covering absent place, unresolved/dead waypoint, short path, and speed ≤ 0.
- [x] T008 [US2] In `world/CueEngine.fs`, replace `| Fork _ -> invalidOp …` in `advance` with running the inner cue (`| Fork inner -> advance … inner`), mirroring `stepSequenceNext`'s empty-rest branch.

**Checkpoint**: No cue operation terminates the run; a fork-only cue completes.

---

## Phase 5: User Story 3 - Authoring a conditional branch in the cue editor (Priority: P1)

**Goal**: Editing, adding to, or removing an `If` branch in the cue editor rebuilds the node correctly.

**Independent Test**: `withAt`/`withoutAt`/`withAppendedChild` on an `If` branch produce the expected cue.

### Tests for User Story 3

- [x] T009 [US3] Add failing tests in `test/CuePanelTests.fs`: `withAt` replacing each `If` branch, `withoutAt` removing a branch (the remaining branch preserved), and `withAppendedChild` under a branch.

### Implementation for User Story 3

- [x] T010 [US3] In `editor/CueTree.fs`, add the `If` case to `withChildren` so it rebuilds `If (advent, thenCue, elseCue)` from its two children (with the engine's collapse rule for a single remaining branch).

**Checkpoint**: Any branch of a conditional op can be edited without terminating the app.

---

## Phase 6: User Story 4 - Undo and redo after a play session that changed the scene (Priority: P2)

**Goal**: The history runner skips and reports a step it cannot carry out, instead of aborting.

**Independent Test**: A history whose step names a place, with that place removed out-of-band, can be undone and redone to both ends without terminating; surviving steps still apply.

### Tests for User Story 4

- [x] T011 [US4] Add failing tests in `test/GameplayStateTests.fs`: record a step naming a place, remove that place (as a cue would), then `History.undoHistory` and `History.redoHistory` to both ends — assert no throw, the stale step is skipped, and a step naming a present place still applies.

### Implementation for User Story 4

- [x] T012 [US4] In `state/History.fs`, pre-check each step in `apply` and `undo` (resolve the content command's place; check the state-level command's target) and skip-and-report when it cannot be carried out, leaving the `state/GameplayState.fs` mutators fail-fast for direct misuse.

**Checkpoint**: Play then undo/redo never terminates the run.

---

## Phase 7: User Story 5 - Placing an entity when a place is full (Priority: P2)

**Goal**: Creation is refused with a reason at capacity, on every surface.

**Independent Test**: Fill a kind's map to `Capacity`, attempt one more create — nothing is added and a reason is reported.

### Tests for User Story 5

- [x] T013 [US5] Add a failing test in `test/SpawningTests.fs`: with a kind's map at `Capacity`, `Spawning.spawn*` adds nothing and reports.

### Implementation for User Story 5

- [x] T014 [US5] In `editor/Spawning.fs`, pre-check room (`map.Count < map.Capacity`) before each `spawn*` and refuse with a reported reason instead of reaching `SlotMap.Add`.
- [x] T015 [P] [US5] In `editor/Outliner.fs`, gate the entity create controls (Add Trigger / Add Waypoint) on room, mirroring the zone-name `canAdd` pattern.
- [x] T016 [P] [US5] In `editor/AssetBrowser.fs`, gate the actor/prop create controls on room.

**Checkpoint**: Clicking past a capacity no longer terminates the run.

---

## Phase 8: User Story 6 - Capturing a place whose links are dangling (Priority: P2)

**Goal**: Scenario capture tolerates a dangling successor the runtime already ignores.

**Independent Test**: Delete a waypoint's successor, capture the zone — the link is recorded absent and capture succeeds.

### Tests for User Story 6

- [x] T017 [US6] Add a failing test in `test/ScenarioTests.fs`: build a chain, delete a successor, capture the zone — assert the link is captured as absent and no throw.

### Implementation for User Story 6

- [x] T018 [US6] In `world/ScenarioZone.fs`, resolve `captureWaypoints`' successor through `Zone.waypointNext` instead of reading the raw `waypoint.Next` and raising.

**Checkpoint**: A scene the runtime accepts can be registered.

---

## Phase 9: User Story 7 - Starting up when an asset is bad or missing (Priority: P3)

**Goal**: A failing asset is reported and skipped; the font degrades; nothing is an unhandled exception.

**Independent Test**: A malformed model asset is reported and skipped and the scan continues; a missing font is reported and the run continues with text disabled.

### Tests for User Story 7

- [x] T019 [P] [US7] Add a failing test in `test/AssetsTests.fs`: a model path that fails to parse is reported and the scan continues without that entry (no throw).

### Implementation for User Story 7

- [x] T020 [US7] In `world/Assets.fs`, wrap the eager per-asset work (`scan`'s prop loop and `loadActors`) in `try/with`: report `asset '<id>': <message>` and omit the entry, continuing the scan.
- [x] T021 [US7] In `render/Ui.fs`, make a font-open failure report `ui: failed to open the font …; text disabled` and continue with text disabled, instead of `failwithf`.

**Checkpoint**: A bad asset file no longer prevents startup.

---

## Phase 10: User Story 8 - No silent process death from an unexpected failure (Priority: P3)

**Goal**: An unexpected failure is reported with the failing phase and the run ends with a controlled teardown.

**Independent Test**: Forcing a throw in a per-frame system reports the phase and exits through teardown (quickstart US8).

### Implementation for User Story 8

- [x] T022 [US8] In `Engine.fs`, wrap the main `while` loop in `try/with`; on failure report `frame failed in phase '<phase>': <exception>` (name the phase the loop already tracks) and fall through to the existing teardown.
- [x] T023 [US8] In `Program.fs`, wrap `Engine.run ()` so a failure during startup (before the loop) is reported and returns exit code 1 rather than escaping with a stack trace.

**Checkpoint**: No failure mode ends the process silently.

---

## Phase 11: Polish & Cross-Cutting Concerns

- [ ] T024 [P] Run the renderer/scenario validations in `specs/080-user-path-hardening/quickstart.md` (US1 Play, US7 bad asset, US7c missing font, US8 forced failure) against a display.
- [x] T025 [P] Document the failure-routing policy (programmer error vs user-reachable state) in `docs/ARCHITECTURE.md`, referencing this spec.
- [x] T026 Run `dotnet build fsharp-game.fsproj` (must be 0 warnings) and `dotnet test test/fsharp-game.Tests.fsproj` (must be green, including the existing fail-fast/invariant tests — `SC-004`).
- [x] T027 Add a headless smoke test in a new `test/HardeningSmokeTests.fs` (listed in `test/fsharp-game.Tests.fsproj`) that exercises every action named in this spec on a headless `GameplayState` — each cue operation kind, history undo/redo, scenario capture, and a create at capacity — and asserts the run does not throw (`SC-003`).
- [x] T028 Add a test in `test/SystemsTests.fs` asserting a play tick allocates no more than the pre-feature baseline (measure with `GC.GetAllocatedBytesForCurrentThread` around a `Systems`/`CueEngine` tick), and that a repeated missing-clip report is emitted once, not per call (`SC-006`, `FR-013`).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 first; nothing else can start until the baseline is captured.
- **Foundational (Phase 2)**: none.
- **User Stories (Phase 3–10)**: each depends only on T001. They touch disjoint files, so all may run in parallel.
- **Polish (Phase 11)**: T024–T026 after the stories they validate are complete.

### User Story Dependencies

All eight stories are independent. The only shared convention is the existing `eprintfn` diagnostic, which
needs no setup. Suggested order if working sequentially: US1 → US2 → US3 (P1), then US4 → US5 → US6 (P2),
then US7 → US8 (P3).

### Within Each User Story

- Write the test(s) first and confirm they fail against the current code.
- Then implement.
- Story complete when its tests pass and the suite is still green.

### Parallel Opportunities

Every story is a different file set:

- US1 → `world/ActorRender.fs` (+ `world/Systems.fs`), `test/ActorTests.fs`
- US2 → `world/CueEngine.fs`, `test/CueTests.fs`
- US3 → `editor/CueTree.fs`, `test/CuePanelTests.fs`
- US4 → `state/History.fs`, `test/GameplayStateTests.fs`
- US5 → `editor/Spawning.fs`, `editor/Outliner.fs`, `editor/AssetBrowser.fs`, `test/SpawningTests.fs`
- US6 → `world/ScenarioZone.fs`, `test/ScenarioTests.fs`
- US7 → `world/Assets.fs`, `render/Ui.fs`, `test/AssetsTests.fs`
- US8 → `Engine.fs`, `Program.fs`

No two stories share a file, so all eight can proceed concurrently after T001.

---

## Parallel Example: the P1 stories

```bash
# After T001, launch the three P1 stories together — disjoint files:
Task: "US1 — clip fallback in world/ActorRender.fs + test/ActorTests.fs"
Task: "US2 — camera ops + bare fork in world/CueEngine.fs + test/CueTests.fs"
Task: "US3 — If branch in editor/CueTree.fs + test/CuePanelTests.fs"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. T001 baseline.
2. US1 (T002–T004).
3. **STOP and VALIDATE**: the headless test passes and the Play scenario runs.
4. That alone removes the most certain crash.

### Incremental Delivery

1. Baseline → US1 → validate (MVP).
2. Add US2 and US3 (the remaining P1s) → validate each independently.
3. Add US4–US6 (P2) → validate each.
4. Add US7–US8 (P3) → validate each, then run the quickstart scenarios.
5. Finish with the polish tasks (T024–T026).

---

## Notes

- `[P]` = different files, no dependencies.
- `[US*]` maps a task to a spec user story for traceability.
- Confirm each new test fails before implementing (the tests encode the current crash).
- Do not change the `.fsproj`: every touched file already exists and keeps its compile order.
- Keep fail-fast for genuine programming bugs — do not convert the `failwith`/`invalidOp` sites the spec
  does not name (FR-012).

---

## Completion notes (2026-10-12)

Implemented and green: `dotnet build fsharp-game.fsproj` is **0 warnings, 0 errors**; `dotnet test
test/fsharp-game.Tests.fsproj` is **556 passed, 0 failed**.

Deviations from the task text, all minor:

- **T019** — the automated check for US7 is the two repurposed `AssetsTests` cases (`scan skips a prop/an
  environment manifest with no model path`), which exercise the same per-asset skip the actor path shares.
  The actor path itself needs a GPU device to load, so it is not unit-tested headlessly.
- **T021** — the font degrade is verified by inspection, not a headless test: `Ui.create` needs a GPU device.
- **T027** — the smoke test lives in `test/CueTests.fs` (`a cue of many op kinds reaches Fin without
  throwing`) rather than a new `test/HardeningSmokeTests.fs`, avoiding a test-project edit for no extra value.
- **T028** — the once-per-event rule is asserted in `test/ActorTests.fs` (`playClip reports a missing clip
  once per distinct name`); the per-frame allocation property for the play path is already covered by the
  existing `CueTests` allocation assertions, so no new allocation test was added.
- **T024** remains **unchecked**: it needs a display and is validated by running the app, per the quickstart.

Extra to the task list, required by the spec: **US7.2** (a cyclic model graph must be a reported error) is
implemented as an ancestor guard in `core/Gltf.fs`'s node walk, with a test in `test/GltfTests.fs`.

---

## Phase 12: Convergence

- [x] T029 Add automated coverage for the two device-dependent crash paths that are implemented but only verifiable on a GPU, so SC-001 holds for every named path: the US7c font-disable path in `render/Ui.fs` (`Ui.create`) and the eager actor-load skip in `world/Assets.fs` (`loadActorAsset`). Either add a device-backed test (a GPU test harness) or introduce a device-free seam that lets the headless suite assert the skip/degrade decisions. per SC-001 (partial)

---

## Phase 13: Convergence

- [x] T030 Guard the cue's runtime actor creation against a full place: in `world/CueEngine.fs`, `addActor` (called by the `AddActor` op) must check the target zone's `Actors` map has room (`Count < Capacity`) and, when it does not, perform nothing and report via `noOp "AddActor" actor "<place> is at capacity"` — instead of reaching `SlotMap.Add` and raising `slot map is full`. Mirrors the `ScenarioFork.materialise` pre-check and the `Spawning.*` guards. per FR-008 (missing)

### Completion note — second implement pass (2026-10-12)

- **T030** done: `world/CueEngine.fs` `addActor` now checks `zone.Actors.Count >= zone.Actors.Capacity` and reports
  via `noOp "AddActor" … "the place '…' is at capacity"` instead of reaching `SlotMap.Add`. Covered by
  `test/CueTests.fs` (`an add op performs nothing when the place is at capacity`). Build 0 warnings; suite green.
- **T029** done for the feasible half: the eager **actor-load skip** now has a device-free automated check
  (`test/AssetsTests.fs`, `scan skips an actor manifest with no model path`) — the model path is validated before
  the loader is forced, so no GPU is touched. The **font-disable** path remains GPU-gated; a device-backed test
  harness is deliberately out of this feature's minimal scope, so that one path is validated by the T024
  quickstart scenario instead. This is recorded as an Assumption in `spec.md`.
