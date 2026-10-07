---

description: "Task list for State Ownership Boundary"
---

# Tasks: State Ownership Boundary

**Input**: Design documents from `/specs/069-state-ownership-boundary/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/state-ownership.md

**Tests**: The suite must stay green (SC-003) and the test fixtures build `SharedState` by hand, so
fixture edits are part of the work. No new behavioural tests are warranted — behaviour is unchanged
(US1's membership rule is compile-enforced by removing the fields). One focused test task per move
verifies the readers.

**Organization**: By user story (US1 P1, US2 P1, US3 P2, US4 P3). US2 and US3 are independent,
self-contained landings: the registry can be moved (and the project built) before physics moves, and
vice versa.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 / US4 (setup and polish carry none)

## Path Conventions

- Single F# project at the repository root; sources in `state/`, `world/`, `screen/`, `editor/`;
  tests in `test/`; docs in `docs/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the baseline and the (unchanged) compile order.

- [x] T001 Confirm the baseline: `dotnet build fsharp-game.fsproj` is warning-free and `dotnet test` is green before any edit.
- [x] T002 Confirm no compile-order change is needed: `physics/Physics.fs` and `world/ZoneRegistry.fs` are listed before `state/GameplayState.fs` in `fsharp-game.fsproj`, so `GameplayState` may reference both. Make no `.fsproj` edit.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: None. The two moves are independent, self-contained landings; nothing must precede them. Proceed to US1.

---

## Phase 3: User Story 1 - Each container states and obeys one rule (Priority: P1) 🎯 MVP

**Goal**: Both state headers state the readership rule (engine/any-screen → `SharedState`;
gameplay-world → `GameplayState`), and the obsolete per-zone-undo (`spec 034`) rationale is gone.

**Independent Test**: Read `state/SharedState.fs` and `state/GameplayState.fs`; each states the rule, and
`SharedState` no longer cites per-zone undo stacks. (The "and obeys it" half is compile-enforced by US2/US3.)

- [x] T003 [P] [US1] `state/SharedState.fs`: rewrite the header to the readership rule — this holds state the engine itself, or more than one screen, may read (the GPU context and the asset manager; the cast binding and the advent flags because a journal/inventory may present them) — and delete the "undo stacks are per zone … outlive every zone (spec 034)" rationale.
- [x] T004 [P] [US1] `state/GameplayState.fs`: rewrite the header to say it owns the gameplay world and that world's configuration — the zones and, joining them, the zone registry and the physics world — beside the screen's own runtime state.

**Checkpoint**: the rule is stated in both modules.

---

## Phase 4: User Story 2 - The zone registry lives beside the zones it registers (Priority: P1)

**Goal**: Move `ZoneRegistry.Registry` from `SharedState` to `GameplayState`; every reader reads it from the state; behaviour unchanged.

**Independent Test**: from a fresh gameplay state, register the active zone against a place and drive a
cue that instantiates it — the registration is read and the place is brought in as before; the project
builds and the suite is green.

- [x] T005 [US2] `state/SharedState.fs`: remove the `ZoneRegistry : ZoneRegistry.Registry` field and its `ZoneRegistry = ZoneRegistry.create ()` line in `create`.
- [x] T006 [US2] `state/GameplayState.fs`: add `ZoneRegistry : ZoneRegistry.Registry` (non-mutable) and construct `ZoneRegistry.create ()` in `create`; no signature change.
- [x] T007 [US2] `editor/ZoneRegistryPanel.fs`: read/write the registry from the state — `ZoneRegistry.tryTemplate entry.Place state.ZoneRegistry` and `state.ZoneRegistry.Templates.[entry.Place] <- template`.
- [x] T008 [US2] `world/CueEngine.fs`: the `AddZone`/`PreloadZone` template read becomes `ZoneRegistry.tryTemplate name state.ZoneRegistry`.
- [x] T009 [US2] Test fixtures: drop `ZoneRegistry = …` from the hand-built `SharedState`s in `test/GameplayStateTests.fs`, `test/AdventTests.fs`, `test/CastIndexTests.fs`, `test/CuePanelTests.fs`, `test/CueTests.fs`; make `test/CueTests.fs`'s `registerTemplate` seed the gameplay state's registry.
- [x] T010 [US2] Build/test checkpoint: `dotnet build` warning-free and `dotnet test` green; a registration is read by the panel and by a cue's instantiate.

**Checkpoint**: the registry lives on the gameplay state; US1's rule holds for it.

---

## Phase 5: User Story 3 - Physics lives beside the zones it simulates (Priority: P2)

**Goal**: Move `Physics.World` from `SharedState` to `GameplayState`; every reader reads it from the state; one world, behaviour unchanged.

**Independent Test**: the play chain steps one world read from the gameplay state; switching zones
detaches the outgoing zone's bodies and attaches the incoming zone's; shutdown releases the gameplay
zones through the same world; the build is warning-free and the suite green.

- [x] T011 [US3] `state/SharedState.fs`: remove the `Physics : Physics.World` field and its `Physics = Physics.create ()` line in `create`.
- [x] T012 [US3] `state/GameplayState.fs`: add `Physics : Physics.World` (non-mutable) and construct `Physics.create ()` in `create`; in `swapZone`, `populateZone` and `deleteZone` read `state.Physics` instead of `shared.Physics` (they keep their `shared` parameter for `Assets`/`Gpu.Device`).
- [x] T013 [P] [US3] `world/CueEngine.fs`: `shared.Physics` → `state.Physics` throughout (the actor ops and the motion ops).
- [x] T014 [P] [US3] `state/History.fs`: `shared.Physics` → `state.Physics` at every `Command.apply`/`undo` and `Zone.occupyZone` call.
- [x] T015 [P] [US3] `screen/GameplayScreen.fs`: `shared.Physics` → `state.Physics` (reconcile, player input, `Physics.step`, actor sync).
- [x] T016 [P] [US3] `editor/Editor.fs`, `editor/Selection.fs`, `editor/Spawning.fs`, `editor/AssetBrowser.fs`: `shared.Physics` → `state.Physics`.
- [x] T017 [US3] `Engine.fs`: the shutdown release becomes `Zone.release zone manager.Gameplay.Physics manager.Shared.Gpu.Device`.
- [x] T018 [US3] Test fixtures: drop `Physics = …` from the hand-built `SharedState`s (same files as T009); in `test/CueTests.fs`, `shared.Physics` → `state.Physics` for the body reads and `Physics.step`.
- [x] T019 [US3] Build/test checkpoint: `dotnet build` warning-free and `dotnet test` green; one world is stepped per play frame; a zone switch detaches/attaches the right bodies.

**Checkpoint**: physics lives on the gameplay state; US1's rule holds for it too.

---

## Phase 6: User Story 4 - Cast and advent flags stay shared (Priority: P3)

**Goal**: Bound the change: the cast binding and the advent flags stay on `SharedState`, for the stated reason.

**Independent Test**: `SharedState` still holds the cast binding and the advent flags, with a rationale naming a non-gameplay reader.

- [x] T020 [US4] `state/SharedState.fs`: comment the cast binding and the advent flags as shared because a screen other than the world (a journal/inventory) may read them — not because they outlive a zone; ensure no move touches them.

**Checkpoint**: the boundary is drawn once, and both "look-alike" members are deliberately kept.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Bring the docs into agreement with the code (FR-010) and validate.

- [x] T021 [P] `docs/ARCHITECTURE.md`: update §State management to the readership rule and the new membership (registry + physics on the gameplay state).
- [x] T022 [P] `docs/PROJECT_STRUCTURE.md`: update the `SharedState.fs` one-liner (no longer "GPU context, assets, physics") and the `GameplayState.fs` line (adds the registry and the world).
- [x] T023 [P] `docs/EDITOR_DESIGN.md`: correct the D-011 note that says `SharedState` hosts the zone registry (it hosts the cast, the scenario library and the advent flags); ensure no doc cites per-zone undo stacks as a placement reason.
- [x] T024 [P] `docs/ENVIRONMENT_LRU_PLAN.md`, `docs/ROADMAP.md`: check for `Shared.Physics`/registry container references and correct any that are now wrong.
- [x] T025 Final validation: `dotnet build` warning-free and `dotnet test` green with no device opened; walk `quickstart.md` (stale-reference search, membership check, register/instantiate, a cue's zone ops, play + switch, menu round-trip).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: none — no blocking prerequisite.
- **US1 (Phase 3)**: after Setup; independent (header text).
- **US2 (Phase 4)**: after Setup; independent — a complete, compiling landing on its own.
- **US3 (Phase 5)**: after Setup; independent — a complete, compiling landing on its own. US2 and US3 may be done in either order.
- **US4 (Phase 6)**: after US2 and US3 (it is the "nothing else moved" bound).
- **Polish (Phase 7)**: after all stories.

### Within and between stories

- T005–T006 are one coupled change (removing the field from one record and adding it to the other).
- T011–T012 are the same for physics.
- T009 and T018 touch the same test files (the fixtures drop both fields); if both stories are done together, edit the fixtures once.
- T020 (US4) must not conflict with the moves; it adds a comment only.

### Parallel Opportunities

- T003 and T004 (the two headers) are different files — parallel.
- T013–T016 (physics readers) are different files — parallel once T011/T012 land.
- T021–T024 (docs) are different files — parallel.

---

## Implementation Strategy

### MVP First

1. Setup (T001–T002).
2. US1 (T003–T004) — the rule is stated.
3. US2 (T005–T010) — the registry moves; this is the first real landing and, with US1, the MVP.
4. **STOP and VALIDATE**: the registry is read from the gameplay state, the build is clean and the suite green.

### Incremental Delivery

1. US1: the rule stated (headers).
2. US2: registry moved → build/test → deliver.
3. US3: physics moved → build/test → deliver.
4. US4: cast/advents confirmed staying.
5. Polish: docs corrected; quickstart walked.

### Notes

- `[P]` = different files, no dependencies.
- This is a **relocation, not a redesign**: one physics world, one registry, one history, unchanged
  behaviour. If a change alters behaviour, it is out of scope.
- The readership rule is the deliverable; the field moves are how it is made true.
- `Command.apply`/`undo` keep taking `(zone, physics, assets)` as values, so no `Command → GameplayState`
  cycle forms; `Zone.*` keeps taking the world as a parameter, so no zone owns it.
