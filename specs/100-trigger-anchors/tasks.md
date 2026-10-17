---
description: "Task list for Trigger Anchors"
---

# Tasks: Trigger Anchors

**Input**: Design documents from `specs/100-trigger-anchors/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`

**Tests**: Automated tasks are included for placement, naming, capacity, selection,
independence, and undo/redo because the plan requires them and the project validates
every feature through its xUnit suite. The editor menu is validated with
`quickstart.md`.

**Organization**: Tasks are grouped by user story. The shared anchor palette and trigger
creation adapters are foundational because both P1 stories use them.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Which user story this task belongs to (`US1`, `US2`, `US3`)
- Every task includes an exact file path

## Path Conventions

Single project at the repository root:

- Editor modules: `editor/`
- Headless tests: `test/`
- Documentation: `docs/`

---

## Phase 1: Setup

**Purpose**: Confirm the baseline and the UI primitive this feature reuses.

- [x] T001 Confirm the baseline builds and tests from `Sugoi.fsproj` and `test/Sugoi.Tests.fsproj` (`dotnet build`, `dotnet test`).
- [x] T002 Verify the `openPopup`, `beginPopup`, and `endPopup` bindings added by feature 099 are present in `render/ImGui.fs`; no render-layer change is expected.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared anchor palette and trigger creation adapters used by every
user story.

**⚠️ CRITICAL**: No user story is complete until this phase is done.

- [x] T003 Rename `WaypointAnchor` to `SpawnAnchor` and `waypointAnchors` to `spawnAnchors` in `editor/Spawning.fs`; update the references in `editor/Outliner.fs` and `editor/Inspector.fs`.
- [x] T004 Extract the waypoint naming loop into a private generic automatic-name helper and add the trigger name function (same `<Anchor> <lowest free n>` rule) in `editor/Spawning.fs`.
- [x] T005 Add `anchorPosition` and `triggerAt` in `editor/Spawning.fs`: `Origin` is zero, `Camera` is the camera's exact position, `Player` is the actor's feet minus `Vector3(0, 0.1, 0)` and is `None` without a live player. Build the trigger entity with identity rotation, unit scale, and the T004 name.
- [x] T006 Add `spawnTriggerAt` in `editor/Spawning.fs`: check `hasRoom`, build through `triggerAt`, and delegate to the existing `spawnTrigger` so the new trigger is selected and one `TriggerCreate` step is recorded.

**Checkpoint**: The shared palette, trigger positioning, naming, and one-step creation
are ready.

---

## Phase 3: User Story 1 - Stamp a trigger at the player or camera (Priority: P1) 🎯

**Goal**: A player-anchored trigger uses the player's feet minus 0.1 m; a
camera-anchored trigger uses the camera's exact position. Both use the default volume,
are selected, and are one undo step.

**Independent Test**: Place the player at a distinctive transform and create a trigger
at the player; confirm its base is 0.1 m below her feet, with radius 1.0 and height 2.0.
Move the camera and create one at the camera; confirm its base matches the camera
position. Confirm selection, undo/redo, and independence.

**Note**: US1's construction and dispatch are delivered by T003-T006 (the anchor
palette is shared with waypoints). This phase proves the trigger-specific behaviour.

### Tests for User Story 1

- [x] T007 [US1] Add tests in `test/SpawningTests.fs`: the player anchor uses feet minus 0.1 m with the default radius/height; the camera anchor uses the exact camera position; no/stale player yields `None`; `spawnTriggerAt` selects the trigger and records exactly one `TriggerCreate`; undo/redo restore it; moving the player afterwards leaves the trigger in place; a full trigger map refuses without recording a step.

**Checkpoint**: Trigger placement is proven without the UI.

---

## Phase 4: User Story 2 - One creation menu instead of a trigger button per anchor (Priority: P1)

**Goal**: One creation control opens a menu of the shared anchors; adding an anchor
extends the palette rather than the panel.

**Independent Test**: Open the trigger section, confirm one creation control, create
from each enabled anchor, and confirm the results and the disabled states.

### Implementation for User Story 2

- [x] T008 [P] [US2] Replace the trigger section's `Add Trigger` button in `editor/Outliner.fs` with one button that opens a popup over `spawnAnchors`, disabling creation at capacity and `Player` when `anchorAvailable` is false.

**Checkpoint**: Creation at every anchor works from one menu.

---

## Phase 5: User Story 3 - Readable automatic trigger names (Priority: P2)

**Goal**: New triggers receive anchor-informed names with per-anchor lowest-free
numbers; rename and delete free a number.

**Independent Test**: Create two triggers at the camera and one at the player; confirm
`Camera 1`, `Camera 2`, `Player 1`; rename one and create again and confirm the freed
number is reused.

### Tests for User Story 3

- [x] T009 [US3] Add tests in `test/SpawningTests.fs`: two camera creations yield `Camera 1` and `Camera 2`; a player creation yields `Player 1`; a renamed or deleted automatic name is reused by the next creation at that anchor; creation introduces no duplicate automatic name.

**Checkpoint**: Trigger names are readable in the list and the cue chooser.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and end-to-end validation.

- [x] T010 [P] Record trigger authoring (shared anchor menu, camera exact position, player feet minus 0.1 m, automatic names) in `docs/EDITOR_DESIGN.md`.
- [ ] T011 Run `dotnet build Sugoi.fsproj`, the full `dotnet test test/Sugoi.Tests.fsproj`, and the manual scenarios in `specs/100-trigger-anchors/quickstart.md`; confirm the gameplay loop still holds the project's target frame rate and fix any regressions.
  - Automated portion complete: build succeeds and the full suite passes (680 tests). The manual `quickstart.md` editor scenarios remain to be run in a graphics session.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: starts immediately.
- **Foundational (Phase 2)**: depends on Setup; blocks all user stories.
- **US1 (Phase 3)**: depends on Foundational.
- **US2 (Phase 4)**: depends on Foundational and T002 (popup binding, already
  present); it does not depend on US1's tests.
- **US3 (Phase 5)**: depends on Foundational; it does not depend on US1 or US2.
- **Polish (Phase 6)**: depends on all desired stories.

### Task Dependencies

- T003 → T004 → T005 → T006 (same file, increasing information).
- T007 depends on T005 and T006.
- T008 depends on T006 and T002.
- T009 depends on T004 and T005.
- T010 and T011 depend on all stories.

### User Story Dependencies

- **US1** is independently proven by T007.
- **US2** is independently testable after Foundational; its menu is a thin adapter
  over the tested spawn path.
- **US3** is independently proven by T009; the naming helper is foundational.

### Within Each User Story

- Write or update the headless tests, then the implementation.
- Keep creation on the existing `TriggerCreate` command; do not add a command family.
- Verify the story's checkpoint before moving on.

### Parallel Opportunities

- **T008** (`editor/Outliner.fs`) can run beside T007/T009 (`test/SpawningTests.fs`)
  once Foundational is complete.
- **T010** (`docs/EDITOR_DESIGN.md`) is independent of the test work.
- T007 and T009 share `test/SpawningTests.fs`, so they should not be edited
  concurrently.

## Parallel Example: After Foundational

```text
Developer A: T008 — trigger creation popup in editor/Outliner.fs
Developer B: T007 + T009 — placement and naming tests in test/SpawningTests.fs
```

## Implementation Strategy

### MVP First (US1 + US2)

Placement alone has no UI, so the MVP is both P1 stories: the shared anchor/trigger
construction (Foundational), the one-menu Outliner entry (US2), and the placement
tests (US1). US3's automatic names are the P2 polish.

### Incremental Delivery

1. Setup + Foundational → shared anchors and trigger creation adapters.
2. US1 → placement tests and validation.
3. US2 → one creation menu; validate the player/camera flow.
4. US3 → naming tests and reuse.
5. Polish → docs and the full suite.

### Parallel Team Strategy

1. One owner completes T003-T006 (shared Spawning work).
2. Then split by file:
   - Owner A: T008 (`editor/Outliner.fs`).
   - One owner for T007/T009, because both share `test/SpawningTests.fs`.
3. Finish with T010-T011.

## Notes

- [P] markers indicate different files and no dependency on an incomplete task.
- `TriggerCreate` already carries the complete authored trigger; no command or undo
  runner change is expected.
- `Player` is an expected `None` when no live player exists; this is not an error.
- The camera anchor performs no ground query; a floating volume is accepted.
- Capacity refusal reuses `Spawning.hasRoom` and records nothing.
- Validate with `quickstart.md` after the automated suite passes.
