---

description: "Task list for feature 078 — fork a scenario into the scene"
---

# Tasks: Fork a Scenario into the Scene

**Input**: Design documents from `specs/078-fork-scenario/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/scenario-fork.md`, `quickstart.md`

**Tests**: Included — the spec requests them explicitly (`FR-014`): the cue op, the editor step and its undo/redo,
the footprint rules, non-idempotence, the capacity no-op and the unregistered no-op, all headless.

**Organization**: Tasks are grouped by user story. The **shared materialiser** (`world/ScenarioFork.materialise`),
the presence step, the slot-map capacity member and the history changes are Phase 2 — every story rests on them.
Each story then adds its own entry point (or its edge rules) and its own tests, and is independently verifiable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: `US1` / `US2` / `US3` (maps to `spec.md`)
- Exact file paths are given for every task.

## Path Conventions

This is a single desktop-game project (`fsharp-game/`). Sources live in `core/`, `world/`, `state/`, `editor/`;
tests in `test/`; docs in `docs/`. `.fsproj` compile order is significant: a file may only reference earlier files.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The two new files, wired into their compile lists at the correct position.

- [X] T001 [P] Create `world/ScenarioFork.fs` (module header + intention-block comment, no code yet) and add `<Compile Include="world/ScenarioFork.fs" />` to `fsharp-game.fsproj` **after `state/GameplayState.fs`, before `world/CueEngine.fs`** (the project's deliberate ordering inversion point).
- [X] T002 [P] Create `test/ScenarioForkTests.fs` (module header + the shared headless fixtures it will reuse: a `GameplayState.create` state, a `TestSupport.manager` shared state with a fabricated actor asset, and a `register` helper calling `ScenarioPanel.commit`) and add `<Compile Include="test/ScenarioForkTests.fs" />` to `test/fsharp-game.Tests.fsproj` after `ScenarioTests.fs`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The one command case, the slot-map member, the history arms, and the shared materialiser. **No story can
be implemented without these.**

**⚠️ CRITICAL**: Complete this phase before any user story.

- [X] T003 [P] Add `member _.Capacity : uint32 = uint32 slotCount` to `SlotMap` in `core/SlotMap.fs` (the fixed size the map was built with; free room = `Capacity - Count`), and assert it in `test/SlotMapTests.fs`.
- [X] T004 [P] Add the case `| ZonePresent of Name : Place` to `Command` in `state/Command.fs`, to the state-level group of `placeOf` (answer `None`), and to the `invalidOp` group in `Command.apply`/`Command.undo` (it is state-level and must never reach the zone-level runner).
- [X] T005 Add the state-level arms to `state/History.fs`: `apply (ZonePresent (_, name))` → `GameplayState.presentZone shared &state name (ZoneRegistry.tryTemplate name state.ZoneRegistry)`; `undo (ZonePresent (_, name))` → `GameplayState.deleteZone shared &state name` (depends on T004).
- [X] T006 In `state/History.fs`, teach `pruneCueSteps` to **recurse into `Batch`**: drop a nested `SetCue` and keep the rest (dropping a batch emptied by it). A non-`Batch`, non-`SetCue` step stays exactly as before (`FR-012`).
- [X] T007 Implement `ScenarioFork.materialise` in `world/ScenarioFork.fs` (depends on T003, T004): signature `shared -> byref<state> -> ScenarioContents -> Command list option`, returning `Some commands` here. It (1) creates each absent place via `GameplayState.presentZone` with `ZoneRegistry.tryTemplate`, recording `ZonePresent`; (2) per place appends actors (`Zone.spawnActor` + `Zone.setShowCollider` + detach the body when `place <> active` guarded by `Physics.isAdded`, record `ActorCreate`), then waypoints in two passes (`Zone.spawnWaypoint` all, then `Zone.setWaypointNext` from the scenario's `Next : int option` mapped through the new refs, record one `WaypointCreate` each carrying the resolved link), then triggers (`Zone.spawnTrigger` + `Zone.setTrigger`, record `TriggerCreate`); (3) returns the commands in apply order. Never calls `switchZone`; never writes the cue, camera or selection. Bind `let zone = state.Zones.[place]` / `let active = state.ActiveZone` as locals before list constructs (FS0406). **Note**: this returns `Some` unconditionally; the `None` refusal (the capacity pre-check) is added by T014 below, so the interim state is incomplete-but-buildable rather than a contract breach.

**Checkpoint**: Foundation ready — every story below can now proceed.

---

## Phase 3: User Story 1 - A cue forks a scenario into the running scene (Priority: P1) 🎯 MVP

**Goal**: The `ForkScenario` cue op materialises a registered scenario and runs its cue in parallel to whatever is
already running.

**Independent Test**: Register a scenario (actors, a chained waypoint pair, triggers); drive `CueEngine.tick` over a
sequence that reaches `ForkScenario`; assert the entities appear in their places and the scenario's cue runs beside
the rest. Unregistered → a diagnostic no-op; `Fin` scenario cue → entities added, running cue unchanged; the op adds
no step to the history.

### Implementation for User Story 1

- [X] T008 [US1] Add `| ForkScenario of scenario: Scenario` to `Cue` in `world/Cue.fs` and every arm the compiler reports: `world/CueEngine.fs` `advance` (registered → `materialise` then `ValueSome (Fork contents.Cue)`; `tryContents = None` → `eprintfn` diagnostic (the `noOp` convention) and `ValueSome Fin`; `materialise = None` → `ValueSome Fin`); and `editor/CueTree.fs` (executing → `false`, `label` → `"Fork scenario: " + Scenario.scenarioName`, `children` → `[]`, `kindName` → `"Fork scenario"`, `placeOf` → `ValueNone`). Build must pass (depends on T007).
- [X] T009 [US1] Add the op's authoring controls: a `"Scenarios"` group `[ "Fork scenario" ]` in `CueTree.kinds`, `defaultOfKind "Fork scenario" -> ForkScenario (List.head Scenario.all)`, a `withScenario : Scenario -> Cue -> Cue` writer (raising on any other node), and a `CueEditor` filtered combo over `Scenario.all` by `Scenario.scenarioName` committing `CueTree.withAt path (CueTree.withScenario s node) cue` (`FR-011`).
- [X] T010 [US1] Add US1's tests to `test/ScenarioForkTests.fs`: op reached in a sequence → entities added + cue forked beside the rest; absent place created with the active place unchanged; unregistered → nothing added and the rest still runs; `Fin` scenario cue → entities added, running cue unchanged; the op leaves `state.Editor.Undo.Applied` unchanged (`FR-001/002/003/006/009`, `SC-001`).

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - The editor forks a scenario as one undoable step (Priority: P2)

**Goal**: A **Fork** control on each Scenario row that adds the entities and forks the cue as exactly one step on the
one history.

**Independent Test**: Register a scenario; call `ScenarioPanel.fork`; assert the entities appear, the screen's cue
became the fork, and exactly one step is on the history; undo → the entities and any created places are gone and the
cue is exactly what it was; redo → all returns. Unregistered → the write path does nothing.

### Implementation for User Story 2

- [X] T011 [US2] Add `ScenarioPanel.fork : shared -> byref<state> -> Scenario -> unit` in `editor/ScenarioPanel.fs` (public, headlessly drivable): look up `tryContents`; `materialise`; when the scenario's cue is not `Fin`, `state.Cue <- Cue.forkInto contents.Cue before` and add `SetCue (before, next)` to the step; push the whole as **one** `Batch` via `UndoStack.push` **without** re-applying (the `Spawning.instantiateZone` pattern). Add a per-row `smallButton "Fork"`, `sameLine`d and `beginDisabled (Option.isNone registered)` (`FR-007/008`).
- [X] T012 [US2] Add US2's tests to `test/ScenarioForkTests.fs`: fork is one step; undo/redo round-trips the entities and the cue exactly; a scenario naming an absent place leaves the active place, camera and selection unchanged; `fork` on an unregistered scenario does nothing (`FR-004/007/008`, `SC-004`).
- [X] T013 [US2] Add the fork step's history-interaction tests to `test/ScenarioForkTests.fs`: `History.apply`/`undo` of a `ZonePresent` round-trips a place's presence (present-from-template and present-empty); the zone-level runner (`Command.apply`/`Command.undo`) `invalidOp`s a `ZonePresent`; `pruneCueSteps` drops the `SetCue` nested in a fork's `Batch` while keeping its `ZonePresent`/`*Create` commands (`FR-012`).

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - A fork reproduces the scenario's footprint, and clashes are handled (Priority: P3)

**Goal**: A fork brings a scenario's **places** into the scene (create-absent from template or empty, append-present),
allows duplicates, and refuses cleanly when a target zone lacks room.

**Independent Test**: Fork a scenario naming an absent place and a present one; assert create-absent (from its
registry template, or empty) and append-present; fork twice → a second copy; force a capacity overflow in one target
zone → a logged no-op that adds nothing anywhere.

### Implementation for User Story 3

- [X] T014 [US3] Add the **capacity pre-check** at the top of `ScenarioFork.materialise` (`world/ScenarioFork.fs`, depends on T007): per target place, free room where the place is present is `SlotMap.Capacity - Count`, else full `Zone.create` capacity; sum the actors/waypoints/triggers to add and, if any target lacks room for any kind, `eprintfn` the reason and return `None` **before** mutating anything (no place created, no entity added) — all-or-nothing (`FR-010`).
- [X] T015 [US3] Add US3's tests to `test/ScenarioForkTests.fs`: create-absent from a registered `ZoneRegistry` template (environment + light) and empty when none; append-present leaves the present zone's environment/light/player untouched; fork twice → two copies of every entity; a chained waypoint pair forks to two waypoints whose `Next` resolves to the fork's own added waypoint; a full target zone → `materialise` returns `None` and nothing changed anywhere; `SlotMap.Capacity`/free-slot arithmetic (`FR-002/005/010`, `SC-002/003/005`).

**Checkpoint**: All three user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: The guiding documents and the end-to-end validation (`FR-015`, `SC-006`).

- [X] T016 [P] Update `docs/ARCHITECTURE.md` ("Scenario & Cue": running a scenario is forking it into the scene), `docs/EDITOR_DESIGN.md` (the Workspace's Scenario row gains **Fork**; the `Fork scenario` cue op is listed), and `docs/ROADMAP.md` (phase 11's remainder lands).
- [X] T017 Run `dotnet build fsharp-game.fsproj` (must be **warning-free**) and `dotnet test test/fsharp-game.Tests.fsproj` (all green: the 531 existing + the new cases), then walk `quickstart.md`'s headless checks.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies — start immediately.
- **Foundational (Phase 2)**: depends on Setup — **blocks all stories**.
- **User Stories (Phase 3–5)**: all depend on Foundational. US1 and US2 can run in parallel (disjoint files except
  `test/ScenarioForkTests.fs`); US3 depends on T007 (its pre-check lives in the materialiser).
- **Polish (Phase 6)**: depends on the stories being complete.

### Within Each Story

- Implementation before that story's tests.
- `ForkScenario` (T008) must land with all its compiler-required arms at once, or the tree will not build — the
  project's intended way to add vocabulary.

### Task-level dependencies

- T005 → T004; T007 → T003, T004; T008 → T007; T011 → T007; T014 → T007.
- T009 → T008; T010 → T008, T009; T012 → T011; T013 → T006, T011; T015 → T014.
- T001 and T002 are independent skeletons — run in parallel (`[P]`).

### Parallel Opportunities

- T001, T002 (Setup) and T003, T004 (Foundational) are disjoint files: `[P]`.
- Once Foundational is done, **US1 (T008–T010)** and **US2 (T011–T013)** can be worked in parallel; only
  `test/ScenarioForkTests.fs` is shared, so append to it in one stream or accept sequential edits.
- T016 (docs) is disjoint from all code: `[P]` with any code task.

---

## Parallel Example: after Foundational

```bash
# Story 1 — the cue op (world/Cue.fs, world/CueEngine.fs, editor/CueTree.fs, editor/CueEditor.fs):
Task: "T008 [US1] Add ForkScenario to Cue and its compiler-required arms"
Task: "T009 [US1] Add the op's CueTree/CueEditor controls"

# Story 2 — the editor step (editor/ScenarioPanel.fs):
Task: "T011 [US2] Add ScenarioPanel.fork and the per-row Fork button"

# Polish — docs (docs/*.md):
Task: "T016 [P] Update ARCHITECTURE / EDITOR_DESIGN / ROADMAP"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 (T001–T002) → Phase 2 (T003–T007).
2. Phase 3 (T008–T010): the cue op, its controls, its tests.
3. **STOP and VALIDATE**: `dotnet test` green; US1's independent test passes with no window.
4. That is the feature's reason to exist — a scenario runs at run time.

### Incremental Delivery

1. Setup + Foundational → the shared materialiser is ready.
2. US1 → run a scenario from a cue (MVP).
3. US2 → fork from the editor as one undoable step.
4. US3 → the footprint edges and the capacity refusal.
5. Polish → docs + the end-to-end run.

---

## Notes

- `[P]` = different files, no incomplete dependency.
- A fork is **not idempotent** by design (`FR-005`) — do not add de-duplication.
- A refused fork **logs and does nothing at all** — never raises mid-play (`FR-010`, the plan's one justified
  deviation from Fail Loudly).
- Build warning-free, suite green, no device opened (`SC-006`).
- Commit as `Implement 078: fork a scenario into the scene`.

---

## Phase 7: Convergence

- [X] T018 Extend the cue-vocabulary coverage in `test/CuePanelTests.fs` for `ForkScenario` — add its row label (`"Fork scenario: Intro"`) to ``every kind's row says what it is…`` and assert the authoring round-trip (`kindName (defaultOfKind Place.start "Fork scenario")` is `"Fork scenario"`, `children` is empty, `placeOf` is `ValueNone`, and `withScenario` replaces a `ForkScenario` node and raises on any other) per plan "test/*.fs: CueTree/CueEngine op coverage" and quickstart "Supporting-file coverage" (partial)
