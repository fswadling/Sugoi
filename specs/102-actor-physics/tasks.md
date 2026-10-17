---
description: "Task list for per-actor physics modes and properties"
---

# Tasks: Per-Actor Physics Modes and Properties

**Input**: Design documents from `/specs/102-actor-physics/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Included because the project's workflow requires headless coverage for physics and editor changes; `test/ActorPhysicsTests.fs` is the new test module.

**Organization**: Tasks are grouped by user story so each story can be implemented and validated independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)
- Every task names the exact file path it changes

## Path Conventions

Single F# project at the repository root; source clusters are `world/`, `physics/`, `state/`, and `editor/`. Tests live in `test/`.

---

## Phase 1: Setup

**Purpose**: Add the test module and confirm the build baseline.

- [x] T001 Create `test/ActorPhysicsTests.fs` with an empty module and add it to `test/Sugoi.Tests.fsproj` after `PhysicsTests.fs` and `ZoneTests.fs`
- [x] T002 Run the baseline build (`dotnet build Sugoi.fsproj`) and confirm it passes before feature work, so later failures are attributable

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Authored model, physics adapter, zone body lifecycle, and the shared command/undo path. No user story can land without these.

**⚠️ CRITICAL**: Complete this phase before starting US1–US4.

- [x] T003 Add `ActorMobility`, `ActorPhysics`, `dynamicPhysics`, `kinematicPhysics`, and the property-bound constants in `world/Entity.fs`
- [x] T004 Add `Physics : ActorPhysics` to `ActorState` in `world/Entity.fs`
- [x] T005 Update every `ActorState` literal in `test/TestSupport.fs`, `test/EntityTests.fs`, `test/CuePanelTests.fs`, `test/ProjectTests.fs`, `test/ScenarioForkTests.fs`, and `test/SystemsTests.fs` so the solution compiles with the new field
- [x] T006 Add `CapsuleSettings` and parameterise `Physics.createCapsuleBody` with rotation and settings in `physics/Physics.fs`; apply mass with `OverrideMassProperties` and `MassPropertiesOverride`, and update the call sites in `test/PlayerTests.fs` and `test/PhysicsTests.fs`
- [x] T007 Add a self-excluding body filter and a capsule shape-cast helper in `physics/Physics.fs`, returning the closest hit fraction along a direction
- [x] T008 Add the `ActorPhysics` → `CapsuleSettings` mapping, refactor `attachActorPhysics`, and add `Zone.setActorPhysics` (destroy old body, rebuild, preserve renderer) in `world/Zone.fs`
- [x] T009 Change `Zone.spawnActor` to consume a full `ActorState`; build `kinematicPhysics` in `editor/Spawning.fs` and `state/CueEngine.fs`, and pass the captured state in `state/ScenarioFork.fs`; update the test call sites in `test/CueTests.fs`, `test/ScenarioForkTests.fs`, and `test/ZoneTests.fs`
- [x] T010 Add `ActorPhysicsSet` to `state/Command.fs`, including `placeOf`, `apply`, and `undo`
- [x] T011 Add `Selection.updateActorPhysicsRef` in `editor/Selection.fs`, mirroring `updateTriggerRef` and recording no step for a no-op
- [x] T012 Run `dotnet build Sugoi.fsproj` and fix every compile error caused by the model and signature changes

**Checkpoint**: The model, body lifecycle, and undo path exist. User stories can begin.

---

## Phase 3: User Story 1 - Place an actor that stands its ground (Priority: P1) 🎯 MVP

**Goal**: A newly placed actor is kinematic, so she is solid but not pushed by the player. The author can switch her to `NoPhysics` and still select her in the viewport.

**Independent Test**: Place an actor, enter play mode, and walk into her. She does not move and blocks the player. Switch to `NoPhysics` and confirm the player passes through and the actor remains click-selectable.

### Tests for User Story 1

- [x] T013 [US1] Add tests for the default `Kinematic` mode, collision immobility, `NoPhysics` having no body and letting the player through, `Show Collider` for each mode, the upright lock, and viewport selection of a `NoPhysics` actor in `test/ActorPhysicsTests.fs`

### Implementation for User Story 1

- [x] T014 [P] [US1] Draw the `Physics` mode combo (`Dynamic`, `Kinematic`, `NoPhysics`) in the actor-only section of `editor/Inspector.fs`, committing each selection through `Selection.updateActorPhysicsRef`
- [x] T015 [P] [US1] Add an editor-only ray/capsule or ray/bounds helper to `editor/Raycast.fs` for actor picking
- [x] T016 [US1] Extend `Viewport.pick` in `editor/Viewport.fs` to compare the physics hit with the nearest actor-bounds hit, and pass `shared.Assets` from `editor/Editor.fs`
- [x] T017 [US1] Verify in `editor/Viewport.fs` that `Show Collider` draws no capsule for `NoPhysics` and that the actor marker still renders; no code change unless the overlay fails

**Checkpoint**: US1 is independently demonstrable: a placed NPC stands still, and all three modes can be selected and picked.

---

## Phase 4: User Story 2 - Tune how a dynamic actor feels (Priority: P1)

**Goal**: A dynamic actor exposes mass, friction, restitution, and gravity factor; edits preserve her transform and record one undo step each.

**Independent Test**: Place two dynamic actors, set different mass and restitution, collide with both, and confirm different responses. Edit a property and confirm the actor does not move on screen and undo restores the previous value.

### Tests for User Story 2

- [x] T018 [US2] Add tests for mass- and gravity-dependent displacement, friction, zero restitution, transform preservation across a property edit, one undo step per drag, and no step for a no-op in `test/ActorPhysicsTests.fs`

### Implementation for User Story 2

- [x] T019 [US2] Add bounded `Mass`, `Friction`, `Restitution`, and `Gravity Factor` drag controls to `editor/Inspector.fs`, committing on `isItemDeactivatedAfterEdit`
- [x] T020 [US2] Hide `Mass` and `Gravity Factor` for `Kinematic` and all four numeric controls for `NoPhysics`, while keeping `Friction` and `Restitution` visible for `Kinematic`, in `editor/Inspector.fs`
- [x] T021 [US2] Verify in `world/Zone.fs` that `setActorPhysics` preserves the existing renderer and body→entity mapping; add a renderer-identity assertion to T018's test

**Checkpoint**: US2 is independently testable; US1 continues to work.

---

## Phase 5: User Story 3 - Keep kinematic NPCs and the player playable (Priority: P2)

**Goal**: Kinematic actors remain cue-movable and stop at geometry; the player designation always implies `Dynamic`.

**Independent Test**: Run a move cue against a kinematic actor and confirm she stops at a wall. Designate a kinematic actor as the player and confirm she becomes movable in one undo step. Confirm `NoPhysics` motion cues are reported no-ops.

### Tests for User Story 3

- [x] T022 [US3] Add tests for kinematic move/rotate, kinematic movement stopping at a mesh wall, `NoPhysics` no-ops, player promotion by designation, zone activation, and undo/redo, `AddActor` defaulting to kinematic, and `EnablePlayer` promotion in `test/ActorPhysicsTests.fs`

### Implementation for User Story 3

- [x] T023 [P] [US3] Guard `Systems.playerInput` on `Mobility = Dynamic` in `world/Systems.fs`
- [x] T024 [P] [US3] Add `Zone.ensurePlayerDynamic` in `world/Zone.fs` and call it from `Zone.occupyZone`, `GameplayState.swapZone`, and the `SetPlayer` apply/undo path in `state/Command.fs`
- [x] T025 [US3] Batch `ActorPhysicsSet` + `SetPlayer` in `Selection.setPlayer` in `editor/Selection.fs` when the target is not already dynamic
- [x] T026 [US3] Implement the kinematic branch in `CueEngine.stepMove` in `state/CueEngine.fs` using the shape-cast helper; keep the dynamic and no-body paths, the facing slerp, and the clip-weight blend
- [x] T027 [US3] Promote the actor to dynamic in `CueEngine.EnablePlayer` and keep `CueEngine.addActor` on the kinematic default in `state/CueEngine.fs`
- [x] T028 [US3] Make the Inspector offer only `Dynamic` for the currently designated player in `editor/Inspector.fs`

**Checkpoint**: US3 works with US1/US2; cues and player input behave correctly.

---

## Phase 6: User Story 4 - Physics choices survive editing and saving (Priority: P2)

**Goal**: The physics setting round-trips through duplicate, scenario fork, save/reload, and undo, and old manifests load as dynamic.

**Independent Test**: Set distinct physics on several actors, duplicate one, fork a scenario, undo/redo, save and reload, then confirm every setting. Load a pre-feature manifest and confirm its actors are dynamic.

### Tests for User Story 4

- [x] T029 [US4] Add tests for duplicate, scenario fork, save/reload, legacy manifest decode, hand-edited bounds, an inactive zone retaining the setting, and delete/restore by undo in `test/ActorPhysicsTests.fs` and `test/ProjectTests.fs`

### Implementation for User Story 4

- [x] T030 [P] [US4] Add `ActorPhysicsDto` and `ActorDto.Physics : ActorPhysicsDto option` in `world/Project.fs`; encode `Some`, decode `None` as `dynamicPhysics`, and reject out-of-range values through the project error path
- [x] T031 [P] [US4] Make `Spawning.duplicate` in `editor/Spawning.fs` copy the full `ActorState` (physics and show-collider) instead of writing fields after spawn
- [x] T032 [P] [US4] Make `ScenarioFork.materialise` in `state/ScenarioFork.fs` spawn from the scenario's `ActorState` unchanged
- [x] T033 [US4] Update `test/ProjectTests.fs` fixtures to carry the physics setting and the legacy decode case

**Checkpoint**: All four user stories are independently functional and the setting survives every authored path.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Verification, documentation, and consistency.

- [x] T034 [P] Update `docs/ARCHITECTURE.md` and `docs/ROADMAP.md` with the three-mode actor physics model
- [x] T035 [P] Add a `test/ProjectTests.fs` case asserting an out-of-range physics value is reported through the project error path and no partial library is installed
- [x] T036 Run `dotnet build Sugoi.fsproj` and `dotnet test test/Sugoi.Tests.fsproj`; fix any failures
- [ ] T037 Run the manual scenarios in `specs/102-actor-physics/quickstart.md` and confirm the expected results
- [x] T038 Verify `project.manifest` written by the game contains the physics field and reloads identically
- [x] T039 [P] Add a headless `Systems.syncActors` allocation check across Dynamic, Kinematic, and NoPhysics, and record the manual frame-rate check from quickstart in `test/ActorPhysicsTests.fs`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup; blocks all user stories.
- **US1 (Phase 3)**: Depends on Foundational. This is the MVP.
- **US2 (Phase 4)**: Depends on Foundational and shares the Inspector file with US1, so complete US1's T014 before T019.
- **US3 (Phase 5)**: Depends on Foundational; shares `state/CueEngine.fs` and `editor/Inspector.fs` with earlier stories, so run after US1.
- **US4 (Phase 6)**: Depends on Foundational; the DTO and duplicate/fork changes are independent of US2/US3 and can run in parallel with them.
- **Polish (Phase 7)**: Depends on all desired stories.

### User Story Dependencies

- **US1**: No story dependency; delivers the MVP.
- **US2**: Builds on the same mode plumbing as US1 but is independently testable.
- **US3**: Uses the mode semantics from US1 and the body rebuild from Foundational.
- **US4**: Uses the authored type from Foundational; independently testable.

### Parallel Opportunities

- T014 (Inspector) and T015/T016 (picking) touch different files and can run in parallel after Foundational.
- T023 (`world/Systems.fs`) and T024 (`world/Zone.fs`, `state/GameplayState.fs`, `state/Command.fs`) are disjoint and can run in parallel.
- T030 (`world/Project.fs`), T031 (`editor/Spawning.fs`), and T032 (`state/ScenarioFork.fs`) touch different files and can run in parallel.
- Documentation task T034 and the T035/T039 test tasks can run in parallel with the final test pass.

## Parallel Example: User Story 1

```text
Task: "Draw the Physics mode combo in editor/Inspector.fs"
Task: "Add the editor-only ray/bounds helper to editor/Raycast.fs"
```

## Parallel Example: User Story 3

```text
Task: "Guard playerInput in world/Systems.fs"
Task: "Add ensurePlayerDynamic in world/Zone.fs, state/GameplayState.fs, and state/Command.fs"
```

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Validate with T013 and the quickstart's kinematic/no-physics scenarios.
4. Stop for review; the requested NPC behaviour is delivered.

### Incremental Delivery

1. Foundational → model, body lifecycle, undo path.
2. US1 → placed NPCs stand still; mode switching works.
3. US2 → dynamic actors can be tuned.
4. US3 → kinematic cues and player designation stay correct.
5. US4 → the setting survives duplicate, scenes, saves, and old projects.

### Parallel Team Strategy

After Foundational:
- Developer A: US1 (Inspector combo + picking).
- Developer B: US2 (property controls) once US1's Inspector combo lands.
- Developer C: US4 (persistence, duplicate, fork) in parallel.
- US3 follows US1 because it shares the Inspector and cue engine.

## Notes

- [P] tasks touch different files and have no incomplete dependency.
- Tests live in one new module, so test tasks within `test/ActorPhysicsTests.fs` are sequential by file.
- Keep Jolt types inside `physics/Physics.fs`; `world/` sees only the authored `ActorPhysics`.
- Rebuild bodies only on cold paths (edit, undo, activation promotion); never in the per-frame sync.
- T037 is the only open task: it requires an interactive display. The underlying scenarios are covered
  headlessly by `test/ActorPhysicsTests.fs` and `test/ProjectTests.fs`.
