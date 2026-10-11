---

description: "Task list for feature 079 — scenario-owned zones"
---

# Tasks: Scenario-Owned Zones

**Input**: Design documents from `specs/079-scenario-owned-zones/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/scenario-owned-zones.md`, `quickstart.md`

**Tests**: Included — the spec requests them (`FR-014`): the staging round-trip, ensure/restage into a present zone,
non-idempotence, the capacity refusal (entities **and** props), the cue op as a runtime effect, the editor fork as one
undoable step, and the retired surfaces — all headless.

**Organization**: This feature is a **model migration** (a zone id becomes a `string`; staging moves from the registry
into the scenario). The migration is necessarily **Foundational** — the tree does not compile until the id change and
the staging move land together, because the fork needs a staging source the moment the registry goes. Each user-story
phase then **closes its slice** with acceptance tests (and any gap those reveal). The build is restored at the end of
Phase 2 and stays green thereafter.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: `US1` / `US2` / `US3` (maps to `spec.md`)
- Exact file paths are given for every task.

## Path Conventions

Single desktop-game project (`fsharp-game/`). Sources in `core/`, `world/`, `state/`, `editor/`, `screen/`; tests in
`test/`; docs in `docs/`. `.fsproj` compile order is significant: a file may only reference earlier files.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the scope of the migration before touching code.

- [X] T001 Run `dotnet test test/fsharp-game.Tests.fsproj` to record the green baseline (the count), and list the files that mention the `Place` **type** (not the words "in place"): `state/Command.fs`, `state/GameplayState.fs`, `state/History.fs`, `world/Cue.fs`, `world/CueEngine.fs`, `world/ScenarioFork.fs`, `world/ScenarioRegistry.fs`, `world/Place.fs`, `world/ZoneRegistry.fs`, `editor/{Outliner,ZoneRegistryPanel,CueTree,CueEditor}.fs`, and the test files.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The model migration — a zone id is a `string`, and a scenario owns its zones' staging. **No story can be
tested until this phase is done**; the build is green again at its end.

**⚠️ CRITICAL**: Complete this phase before the story phases.

- [X] T002 Migrate zone identity to `string` in `state/Command.fs` and `state/GameplayState.fs`: every content command's `Place` field becomes `string`; `Zones : Dictionary<string, Zone>`; `ActiveZone : string`; add `let startZone : string = "start"` (replacing `Place.start`); `presentZone`/`populateZone`/`addZone`/`deleteZone`/`switchZone`/`selectZone` take `string`; drop `open Place`; **remove the `ZoneRegistry` field** and its `ZoneRegistry.create ()` in `GameplayState.create` (the registry retires in T010, so the struct must stop referencing it).
- [X] T003 Migrate `state/History.fs`, `world/Cue.fs`, `world/CueEngine.fs`, `world/ScenarioRegistry.fs` and `world/ScenarioFork.fs` to `string` zone ids (in `ScenarioFork` touch only the id types — the `materialise` rewrite is T008); delete `world/Place.fs` and remove it from `fsharp-game.fsproj`; update `world/Scenario.fs`'s doc comments (they cite `Place`/`ZoneRegistry`).
- [X] T004 `world/ScenarioZone.fs`: add `Staging : ZoneTemplate option` to `ScenarioZone`; `capture` gathers `Zone.captureTemplate zone` alongside the narrative entities. `world/ScenarioRegistry.fs`: `ScenarioContents.Zones : Map<string, ScenarioZone>`; `capture` takes `Dictionary<string, Zone>`.
- [X] T005 `state/Command.fs`: change `ZonePresent of Name : string * Staging : ZoneTemplate option`; add `ZoneStage of Name : string * Previous : ZoneTemplate option * Next : ZoneTemplate option`; put both in `placeOf`'s `None` group and in the `invalidOp` group of `Command.apply`/`Command.undo`.
- [X] T006 `state/History.fs`: `apply`/`undo` arms — `ZonePresent` → `presentZone … Staging` / `deleteZone`; `ZoneStage` → `stageZone … Next` / `stageZone … Previous`.
- [X] T007 `state/GameplayState.fs`: add `stageZone (shared) (&state) (name) (staging : ZoneTemplate option)` — replace the zone's environment/light/props, detach physics when `name <> ActiveZone`, leave `Zone.Player` alone. Delete `Spawning.instantiateZone` (`editor/Spawning.fs`) and its now-unused imports.
- [X] T008 `world/ScenarioFork.fs`: rework `materialise` to **ensure** each zone by name, **restage** a present zone (skip when `Zone.captureTemplate zone = sz.Staging`), **append** entities, and **fork** the cue; extend the capacity pre-check to include the scenario's props (all-or-nothing, `None` on shortfall).
- [X] T009 Retire the staging-dependent cue ops: remove `AddZone`/`PreloadZone` from `world/Cue.fs` and their `advance` arms from `world/CueEngine.fs`; keep `SetZone`/`RemoveZone` (now `string`); update `editor/CueTree.fs` (`kinds`, `kindName`, `defaultOfKind`, `label`, `children`, `placeOf`, `withPlace`) and `editor/CueEditor.fs`.
- [X] T010 Retire the Zone Registry and the closed vocabulary's editor surface: delete `world/ZoneRegistry.fs` and `editor/ZoneRegistryPanel.fs`, remove both from `fsharp-game.fsproj`, and stop drawing the panel in `editor/Editor.fs`. `editor/Outliner.fs`: list the present zones (`state.Zones` keys) with their residency marker, and make "Add place" a typed **name** committing `ZoneAdd (active, name)` (guarded: non-empty, not already present). `editor/CueTree.fs`: the zone chooser becomes a typed name field (drop `Place.all`).
- [X] T011 Migrate the test suite for the new model: `Place` → `string` across `test/*.fs`; retire `test/ZoneRegistryTests.fs` (its subject is gone) and remove it from `test/fsharp-game.Tests.fsproj`; move `SpawningTests`' `instantiateZone` cases to `GameplayState.stageZone` / a scenario fork; fix up `CueTests`/`CuePanelTests` for the retired cue ops and `describe` arms. **Build and suite green here.**

**Checkpoint**: A zone is a named thing, a scenario carries its zones' staging, and the registry is gone — the model is
in place.

---

## Phase 3: User Story 1 - A scenario is the unit of authoring; forking loads the whole scenario (Priority: P1) 🎯 MVP

**Goal**: Registering captures a scene's zones (with their staging) and forking brings them up fully.

**Independent Test**: register a scenario whose zones carry distinct environments and lights; fork it into a world
without them; assert each zone is present with its environment, light, placed props and entities, and its cue runs in
parallel — with no Zone Registry interaction.

### Tests for User Story 1

- [X] T012 [US1] Add US1's coverage to `test/ScenarioForkTests.fs`: `ScenarioZone.capture` carries the staging (environment asset + light + props) and the entities; register-then-fork restores a zone's environment and light in a world without it; a multi-zone scenario brings every zone up with its own staging; the `ForkScenario` cue op (via `CueEngine.tick`) and `ScenarioPanel.fork` both load the whole scenario (`FR-001/002/004`, `SC-001`).

**Checkpoint**: User Story 1 is functional and independently testable (MVP).

---

## Phase 4: User Story 2 - Forking into an existing world restages the zones it names (Priority: P2)

**Goal**: A fork that names a **present** zone reuses it (never duplicates) and replaces its staging with the
scenario's.

**Independent Test**: present a zone with one environment; fork a scenario naming it with a different environment and
entities; assert the zone count did not grow, the zone carries the scenario's environment/light/props, and the
entities were appended.

### Tests for User Story 2

- [X] T013 [US2] Add US2's coverage to `test/ScenarioForkTests.fs`: a fork naming a present zone **reuses** it (`Zones.Count` unchanged) and **replaces** its environment/light/props with the scenario's; appends its entities; the restage is **skipped** when the staging already matches (no environment release/reload); a fork of the **active** zone leaves the active place, camera and selection unchanged; forking twice duplicates entities but not zones; and the editor fork's one `Batch` **round-trips a restage** — undo restores the previous environment/light/props, redo re-applies (`FR-003/004/005/007/008/013`, `SC-002/004`).

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Zones are addressed by name; the closed vocabulary and the registry retire (Priority: P3)

**Goal**: A zone is a name, not a declared case; the Zone Registry and the `Place` vocabulary are gone.

**Independent Test**: fork a scenario naming a zone id no code declares; assert it comes up; assert the registry, the
`Place` vocabulary and the `AddZone`/`PreloadZone` ops are gone.

### Tests for User Story 3

- [X] T014 [US3] Add US3's coverage to `test/ScenarioForkTests.fs` (and the supporting tests): a fork naming an **undeclared** zone id creates it with the scenario's staging; `ZonePresent`/`ZoneStage` apply/undo round-trip (including a restage on a present, **inactive** zone leaving its bodies detached) and the zone-level runner `invalidOp`s both; `SetZone`/`RemoveZone` remain and `AddZone`/`PreloadZone` are gone; the capacity refusal covers **props** as well as entities and changes nothing anywhere (`FR-009/010/011/012`, `SC-003/005`).

**Checkpoint**: All three user stories are functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: The guiding documents and the end-to-end validation (`FR-015`, `SC-006`).

- [X] T015 [P] Update `docs/ARCHITECTURE.md` (zones are named; a scenario owns its zones' staging; the registry is retired), `docs/EDITOR_DESIGN.md` (D-023's panel retired; the module map; the phasing), `docs/ZONE_MODEL_DECISION.md` (the `ZoneTemplate`↔scenario convergence is taken; `ZoneTemplate` is the staging), and `docs/ROADMAP.md` (phase 12 the registry retired; phase 11 folds staging into the scenario).
- [X] T016 Run `dotnet build fsharp-game.fsproj` (must be **warning-free**) and `dotnet test test/fsharp-game.Tests.fsproj` (all green), then walk `quickstart.md`'s headless checks.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup — **blocks all stories**; the build is restored at its end.
- **User Stories (Phase 3–5)**: all depend on Foundational; US2 builds on US1's staging capture, and US3 verifies the
  migration US1/US2 rest on. In practice they close in order P1 → P2 → P3.
- **Polish (Phase 6)**: depends on the stories.

### Task-level dependencies

- T003 → T002; T005 → T004; T006 → T005, T007; T007 → T002; T008 → T005, T007; T009 → T003; T010 → T003; T011 → T002–T010 (the migration must be complete).
- T012 → T008; T013 → T008; T014 → T006, T009, T010.
- T015, T016 depend on the stories.

### Parallel Opportunities

- T015 (docs) is disjoint from all code: `[P]`.
- Within Phase 2 the tasks touch overlapping files and are **not** independent (it is one migration); run them in order.
- The story phases are largely in the one test file (`test/ScenarioForkTests.fs`), so run them sequentially.

---

## Implementation Strategy

### MVP (User Story 1)

1. Phase 1 (T001) → Phase 2 (T002–T011).
2. Phase 3 (T012): a scenario carries its zone's staging and forking loads it.
3. **STOP and VALIDATE**: `dotnet test` green; a fork brings a zone up fully with no registry.

### Incremental Delivery

1. Setup + Foundational → the model is migrated, suite green.
2. US1 → fork loads the whole scenario (MVP).
3. US2 → fork into an existing world restages it.
4. US3 → named zones; the registry and the retired ops are gone.
5. Polish → docs + the end-to-end run.

---

## Notes

- `[P]` = different files, no incomplete dependency.
- **Zones are ensured by name; entities are appended.** A zone's name is its identity (`Zones` is keyed by it); an
  entity's name is a label (`FR-003` vs `FR-005`). This asymmetry is intended.
- A refused fork **logs and changes nothing at all** (entities **and** props) — never raises mid-play (`FR-009`).
- Retiring the registry removes no authoring affordance: the active zone's environment is set in the Asset Browser
  (`SetEnvironment`) and its light in the Inspector.
- Build warning-free, suite green, no device opened (`SC-006`).
- Commit as `Implement 079: scenarios own their zones`.
