---

description: "Task list for Zone Cue Ops"
---

# Tasks: Zone Cue Ops

**Input**: Design documents from `/specs/062-zone-cue-ops/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/zone-cue-ops.md, quickstart.md

**Tests**: Included — the project's convention (and SC-006) require the suite green, and each op is a
behaviour the suite can assert headlessly with no device.

**Organization**: Tasks are grouped by user story (US1 P1, US2 P2, US3 P3).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 (setup, foundational, polish carry none)

## Path Conventions

- Single F# project at the repository root; sources in `world/`, `state/`, `editor/`, tests in `test/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The shared cue vocabulary the whole feature hangs on.

- [x] T001 Add the three leaf cases `AddZone of place: Place`, `RemoveZone of place: Place`,
  `PreloadZone of place: Place` to `world/Cue.fs`, beside `SetZone`, and add them to the module's opening
  note that lists the no-progress ops (`Exposit`, `SetZone`, `SetCamera`, `AddAdvent`, `RemoveAdvent`).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The primitives and the compile-required match updates; the project builds only once these land
together, because adding union cases forces every exhaustive match to account for them.

**⚠️ CRITICAL**: No user story can be implemented until this phase is complete.

- [x] T002 [P] `world/Zone.fs`: add `loadTemplate (zone) (world) (assets) (template)` — populate a fresh zone
  from a registered template: `loadEnvironmentFrom`, then `zone.Light <- template.Light`, then a
  `spawnPropBinding` per `template.Props` entry (the content half of `editor/Spawning.instantiateZone`, without
  its replace/undo machinery).
- [x] T003 `state/GameplayState.fs`: add
  `presentZone (shared) (state) (name: Place) (template: ZoneTemplate option)` — raise if `name` is already
  present; create the zone; populate it via `Zone.loadTemplate` when a template is given; then
  `Zone.deactivatePhysics` so the inactive zone's bodies do not simulate. Leave `ActiveZone`, the camera and
  the selection untouched. (depends on T002)
- [x] T004 `world/CueEngine.fs`: add the `AddZone`/`RemoveZone`/`PreloadZone` arms to `advance` with their
  guards, and change `SetZone` to perform nothing when the place is not present (was: `switchZone`, which
  raises). Reaches `ZoneRegistry.tryTemplate` and `Assets.requestEnvironment` fully qualified.
  (depends on T001, T003)
- [x] T005 [P] `editor/CueTree.fs`: extend `kinds`, `kindName` (`"Add zone"`, `"Remove zone"`,
  `"Preload zone"`), `label`, `defaultOfKind` (each → `Place.start`), `withPlace` and the `drawEditor`
  place-chooser arm to the three new ops, and add them to the not-executing leaves in `executing`.
- [x] T006 [P] `test/CueTests.fs` and `test/CuePanelTests.fs`: extend the local `describe` helpers (and the
  `CuePanelTests` label expectations) so the test project compiles with the new cases.
- [x] T007 Build checkpoint: `dotnet build fsharp-game.fsproj` and `dotnet build test/fsharp-game.Tests.fsproj`
  compile with the new cases and matches.

**Checkpoint**: the ops exist, compile and are authorable; the user stories can proceed.

---

## Phase 3: User Story 1 - A cue adds and removes zones by place (Priority: P1) 🎯 MVP

**Goal**: A cue makes a declared place present (streaming its registered template) and makes a present place
absent (releasing it and unloading its environment), naming only a `Place`.

**Independent Test**: run a cue carrying an add op and assert the place becomes present with its template
loaded and the active place unchanged; run one carrying a remove op and assert it becomes absent and its
environment unloads; assert a place cannot be named unless the game declares it (it is a `Place`).

- [x] T008 [P] [US1] `test/GameplayStateTests.fs`: `presentZone` makes an absent place present and populated
  from a template (its environment and a placed prop), leaves `ActiveZone`/camera/selection unchanged, and
  leaves the new zone's bodies detached (not added to the physics world).
- [x] T009 [P] [US1] `test/CueTests.fs`: through `CueEngine.tick`, an `AddZone` op presents an absent place
  (with its registered template when the registry has one) and does not change the active place.
- [x] T010 [US1] `test/CueTests.fs`: a `RemoveZone` op makes a present, inactive place absent and unloads its
  environment unless another present place shares it; removing the active place or an absent place performs
  nothing.
- [x] T011 [P] [US1] `test/CuePanelTests.fs`: the `"Add zone"` / `"Remove zone"` kinds and labels render, and
  `withPlace` writes a place on both new ops (as it does for `SetZone`).

**Checkpoint**: US1 works and is testable independently.

---

## Phase 4: User Story 2 - A cue preloads the next zone's environment, protecting the active one (Priority: P2)

**Goal**: A cue starts a present zone's environment loading asynchronously, so a later switch shows no pop-in,
without ever unloading the active zone's environment.

**Independent Test**: preload an inactive present zone's environment through the cue engine and assert its
residency becomes loading (de-duplicated), and that the active zone's environment is untouched.

- [x] T012 [P] [US2] `test/CueTests.fs`: a `PreloadZone` op requests the present zone's environment (its
  residency becomes loading) and performs nothing for an absent place or a place with no environment.
- [x] T013 [P] [US2] `test/GameplayStateTests.fs`: a preload leaves the active zone's environment
  residency untouched (it is not released to make room).

**Checkpoint**: US1 and US2 both work and are independently testable.

---

## Phase 5: User Story 3 - The zone ops compose and are safe to re-run (Priority: P3)

**Goal**: A redundant or contradictory zone op performs nothing, so a cue is safe to run twice, and the ops
compose into a prepared transition.

**Independent Test**: run a cue that adds an already-present place, removes an already-absent place, removes
the active place, preloads an environment-less place and sets an absent place — each completes without raising;
then assert a preload → set → remove composition leaves the expected state.

- [x] T014 [US3] `test/CueTests.fs`: every redundant/contradictory zone op performs nothing and the cue still
  reaches `Fin` (the re-run safety property of FR-007/FR-008).
- [x] T015 [US3] `test/CueTests.fs`: the composition `PreloadZone B → SetZone B → RemoveZone A` leaves `B`
  active and `A` absent.

**Checkpoint**: all three stories function.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [x] T016 [P] Docs: mark the deferred `PreloadZone` op delivered in `docs/ENVIRONMENT_LRU_PLAN.md`; note the
  zone cue ops under `docs/ROADMAP.md` (phase 12 / stage primitives). Update `docs/ARCHITECTURE.md` only if it
  should describe cue-driven zone changes.
- [x] T017 `dotnet build` warning-free and `dotnet test` green (SC-006).
- [x] T018 Run the automated section of `quickstart.md` and confirm the headless outcomes.

---

## Follow-up

- [x] T019 `world/CueEngine.fs` + `state/GameplayState.fs`: a `PreloadZone` on a present place with no
  environment resolves the place's registered template, streams it into the zone
  (`GameplayState.populateZone`), and loads that environment — closing the "present but empty, registered"
  gap; covered by a test in `test/CueTests.fs`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks** every user story.
- **US1–US3 (Phases 3–5)**: all depend on Foundational; run in priority order (or in parallel if staffed).
- **Polish (Phase 6)**: depends on the stories.

### Within and between stories

- T001 is one union change; T004/T005/T006 are what makes it compile.
- T002 → T003 → T004 is a chain (the template helper, the presence primitive that uses it, the cue arm that
  calls it).
- Test tasks follow the code they cover; US1–US3's tests touch the same few files, so they are not all
  parallelisable.

### Parallel Opportunities

- T002 and T005 touch distinct files once T001 lands.
- T008 (GameplayStateTests), T009 (CueTests) and T011 (CuePanelTests) are different files.
- T012 and T013 touch distinct files; T016 is independent of the code.

---

## Implementation Strategy

### MVP First (User Story 1)

1. Setup + Foundational (the ops exist, compile and are authorable).
2. US1 (add/remove by cue, streaming).
3. **STOP and VALIDATE**: a cue can present and remove a place; the active place is unchanged by an add.
4. US2 (preload), then US3 (no-op safety and composition), then polish.

### Incremental notes

- The Foundational phase is deliberately one landing: the union gains three cases and every exhaustive match
  must account for them, so it does not half-compile.
- The ops' behaviour is small; the bulk of the work is the tests that pin the no-op rule and the streaming.

## Notes

- `[P]` = different files, no dependencies.
- Cue-driven zone changes are runtime effects, never steps on the undo history (FR-010).
- A redundant zone op is the ordinary case, not a bug — the cue path is forgiving; the editor's functions
  are not (research D2).
- "Present/absent" is the zone-map level; "environment residency" is the GPU level — do not conflate them.
- No `.fsproj` change: no module is added or moved.
