---
description: "Task list for Actor and Prop Spawn Anchors"
---

# Tasks: Actor and Prop Spawn Anchors

**Input**: Design documents from `specs/101-actor-prop-anchors/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`

**Tests**: Automated tasks are included for actor/prop placement, orientation,
availability, capacity, selection, and undo/redo because the plan requires them and the
project validates every feature through its xUnit suite. The Asset Browser selector and
buttons are validated with `quickstart.md`.

**Organization**: Tasks are grouped by user story. The shared anchor vocabulary, asset
anchor transform, and spawn adapters are foundational because both P1 stories use them.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Which user story this task belongs to (`US1`, `US2`, `US3`)
- Every task includes an exact file path

## Path Conventions

Single project at the repository root:

- Transient editor state: `state/`
- Editor modules: `editor/`
- Headless tests: `test/`
- Documentation: `docs/`

---

## Phase 1: Setup

**Purpose**: Confirm the baseline and the existing UI primitives.

- [x] T001 Confirm the baseline builds and tests from `Sugoi.fsproj` and `test/Sugoi.Tests.fsproj` (`dotnet build`, `dotnet test`).
- [x] T002 Verify `beginCombo`, `selectable`, and `beginDisabled` are available in `render/ImGui.fs` for the shared selector; no render-layer change is expected.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared anchor state and asset placement adapters used by every user
story.

**⚠️ CRITICAL**: No user story is complete until this phase is done.

- [x] T003 Move `SpawnAnchor`, `spawnAnchors`, and `anchorName` from `editor/Spawning.fs` into `state/EditorState.fs`; add `SpawnAt : SpawnAnchor` defaulting to `Origin`, and update the references in `editor/Spawning.fs`, `editor/Outliner.fs`, `editor/Inspector.fs`, and `test/SpawningTests.fs`.
- [x] T004 Add `assetAnchorTransform` in `editor/Spawning.fs`: `Origin` is zero/identity; `Camera` is the camera's exact position with yaw `atan2(forward.X, forward.Z)` about +Y (identity yaw when the horizontal view is zero); `Player` is the live player's authored feet position and rotation, or `None` without a live player.
- [x] T005 Add `spawnActorAt` and `spawnPropAt` in `editor/Spawning.fs`: apply the T004 transform to the entity built from the asset name, then delegate to the existing `spawnActor`/`spawnProp` so selection, capacity, asset binding, and one `ActorCreate`/`PropCreate` step are preserved.

**Checkpoint**: Anchor transforms and anchor-aware spawn functions are ready.

---

## Phase 3: User Story 1 - Spawn an actor at the player or camera (Priority: P1) 🎯

**Goal**: An actor can be spawned at Origin, Camera, or Player with the expected
position, facing, selection, asset, and undo step.

**Independent Test**: Call `spawnActorAt` for each anchor and confirm position, rotation,
unit scale, selected actor, asset name/binding, one `ActorCreate`, undo/redo, no-player
refusal, and capacity refusal.

### Tests for User Story 1

- [x] T006 [US1] Add tests in `test/SpawningTests.fs` (with a headless asset manager that includes an actor asset): actor at Player uses the player's feet and rotation; actor at Camera uses the camera's position and horizontal yaw (upright, including a vertical-view camera); actor at Origin uses zero/identity; every anchor keeps unit scale and the asset name; moving the player or camera afterwards leaves the actor in place; the actor is selected and records one `ActorCreate`; undo/redo restore it; no live player and a full actor map create nothing and record nothing.

**Checkpoint**: Actor anchor placement is proven without the UI.

---

## Phase 4: User Story 2 - Spawn a prop at the player or camera (Priority: P1)

**Goal**: A prop can be spawned at Origin, Camera, or Player with the expected position,
facing, selection, asset binding, and undo step.

**Independent Test**: Call `spawnPropAt` for each anchor and confirm position, rotation,
unit scale, selected prop, asset binding/collider, one `PropCreate`, undo/redo,
no-player refusal, and capacity refusal.

### Tests for User Story 2

- [x] T007 [US2] Add tests in `test/SpawningTests.fs`: prop at Player uses the player's feet and rotation; prop at Camera uses the camera's position and horizontal yaw (upright, including a vertical-view camera); prop at Origin uses zero/identity; every anchor keeps unit scale, the asset name, and the asset binding; moving the player or camera afterwards leaves the prop in place; the prop is selected and records one `PropCreate`; undo/redo restore it; no live player and a full prop map create nothing and record nothing.

**Checkpoint**: Prop anchor placement is proven without the UI.

---

## Phase 5: User Story 3 - One shared anchor selector for both asset lists (Priority: P2)

**Goal**: One `Spawn at` control governs both the Actors and Props sections; asset
buttons spawn at the selected anchor and are disabled when it is unavailable or the kind
is full.

**Independent Test**: Open the Asset Browser, confirm one selector shared by both
sections, spawn an actor and a prop from the same anchor, and verify the unavailable and
capacity states.

### Implementation for User Story 3

- [x] T008 [P] [US3] Add the shared `Spawn at` combo in `editor/AssetBrowser.fs`, reading/writing `state.Editor.SpawnAt`; route actor and prop asset buttons through `spawnActorAt`/`spawnPropAt`; disable `Player` when `anchorAvailable` is false and disable spawn buttons when the selected anchor is unavailable or the kind is at capacity; leave environments unchanged.

**Checkpoint**: The selector and asset buttons expose the anchor placement in the
editor.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and end-to-end validation.

- [x] T009 [P] Record actor/prop spawn anchors (shared `Spawn at` selector, exact camera position and horizontal yaw, player feet/rotation, asset names unchanged) in `docs/EDITOR_DESIGN.md`.
- [ ] T010 Run `dotnet build Sugoi.fsproj`, the full `dotnet test test/Sugoi.Tests.fsproj`, and the manual scenarios in `specs/101-actor-prop-anchors/quickstart.md`; confirm the gameplay loop still holds the project's target frame rate and fix any regressions.
  - Automated portion complete: build succeeds and the full suite passes (688 tests). The manual `quickstart.md` editor scenarios remain to be run in a graphics session.
  - Environment non-interference, selector transience (not undoable or persisted), and the no-schema-change rule are verified by the existing suite and the manual environment step rather than by dedicated code paths.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: starts immediately.
- **Foundational (Phase 2)**: depends on Setup; blocks all user stories.
- **US1 (Phase 3)**: depends on Foundational.
- **US2 (Phase 4)**: depends on Foundational; it does not depend on US1.
- **US3 (Phase 5)**: depends on Foundational; the UI wiring is independent of the
  headless tests.
- **Polish (Phase 6)**: depends on all desired stories.

### Task Dependencies

- T003 → T004 → T005 (same module, increasing information).
- T006 and T007 depend on T004 and T005.
- T008 depends on T003 and T005.
- T009 and T010 depend on all stories.

### User Story Dependencies

- **US1** is independently proven by T006.
- **US2** is independently proven by T007.
- **US3** is independently testable in the editor after T005; it is the user-facing
  shell, so the first usable increment includes it even though it is P2.

### Within Each User Story

- Write or update the headless tests, then the implementation.
- Keep spawning on the existing `spawnActor`/`spawnProp` and their commands; do not add
  a command family.
- Verify the story's checkpoint before moving on.

### Parallel Opportunities

- **T008** (`editor/AssetBrowser.fs`) can run beside T006/T007
  (`test/SpawningTests.fs`) once Foundational is complete.
- **T009** (`docs/EDITOR_DESIGN.md`) is independent of the test work.
- T006 and T007 share `test/SpawningTests.fs`, so they should not be edited
  concurrently.

## Parallel Example: After Foundational

```text
Developer A: T008 — shared selector and anchor-aware asset buttons in editor/AssetBrowser.fs
Developer B: T006 + T007 — actor and prop placement tests in test/SpawningTests.fs
```

## Implementation Strategy

### MVP First (US1 + US2 + US3)

The placement capability (US1/US2) is only reachable through the selector (US3), even
though US3 is P2. Complete Foundational, then the tests and the Asset Browser wiring;
validate the combined flow.

### Incremental Delivery

1. Setup + Foundational → shared anchor state and asset adapters.
2. US1 → actor placement tests.
3. US2 → prop placement tests.
4. US3 → selector and asset-button wiring; validate in the editor.
5. Polish → docs and the full suite.

### Parallel Team Strategy

1. One owner completes T003-T005 (shared state and Spawning work).
2. Then split by file:
   - Owner A: T008 (`editor/AssetBrowser.fs`).
   - One owner for T006/T007, because both share `test/SpawningTests.fs`.
3. Finish with T009-T010.

## Notes

- [P] markers indicate different files and no dependency on an incomplete task.
- `SpawnAnchor` moves to `EditorState` because `EditorState` is compiled before
  `Spawning`; `anchorAvailable` stays in `Spawning`.
- Actor/prop names remain the asset names; no automatic naming.
- Camera placement uses horizontal yaw only and may float; player placement overlaps her.
- Capacity refusal reuses `hasRoom` and records nothing.
- Validate with `quickstart.md` after the automated suite passes.
