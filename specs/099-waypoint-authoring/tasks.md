---
description: "Task list for Waypoint Authoring UX"
---

# Tasks: Waypoint Authoring UX

**Input**: Design documents from `specs/099-waypoint-authoring/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`

**Tests**: Automated tasks are included for the spawn, naming, insertion, and
undo/redo paths because the plan requires them and the project validates every
feature through its xUnit suite. The editor menus themselves have no headless
harness; they are validated with `quickstart.md`.

**Organization**: Tasks are grouped by user story. The shared anchor palette and
spawn/undo behaviour are foundational because all three stories use them.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Which user story this task belongs to (`US1`, `US2`, `US3`)
- Every task includes an exact file path

## Path Conventions

Single project at the repository root:

- Editor modules: `editor/`
- Render-layer bindings: `render/`
- Transient editor state: `state/`
- Headless tests: `test/`
- Documentation: `docs/`

---

## Phase 1: Setup

**Purpose**: Confirm the baseline and add the one new UI primitive.

- [x] T001 Confirm the baseline builds and tests from `Sugoi.fsproj` and `test/Sugoi.Tests.fsproj` (`dotnet build`, `dotnet test`).
- [x] T002 [P] Add `openPopup`, `beginPopup`, and `endPopup` P/Invoke bindings in `render/ImGui.fs` (the native library already exports `igOpenPopup_Str`, `igBeginPopup`, `igEndPopup`).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared anchor, naming, insertion, and spawn behaviour used by all three
user stories.

**⚠️ CRITICAL**: No user story is complete until this phase is done.

- [x] T003 Add the editor-only `WaypointAnchor` union (`Origin`, `Camera`, `Player`), the ordered palette, anchor labels, and `anchorAvailable` (Player requires `Systems.isPlayerLive`) in `editor/Spawning.fs`.
- [x] T004 Add the automatic-name helper to `editor/Spawning.fs`: `<Prefix> <n>`, where `n` is the lowest positive integer not used by a live waypoint with that prefix (parse the numeric suffix).
- [x] T005 Add anchor entity constructors in `editor/Spawning.fs`: `Origin` (world origin, identity facing), `Camera` (camera position and view direction), and `Player` (actor's authored position and rotation). All use unit scale and the T004 name.
- [x] T006 Change `spawnLinkedWaypoint` in `editor/Spawning.fs` to insert: set the new waypoint's `Next` to the predecessor's previous next, then point the predecessor at the new waypoint; capture the linked waypoint in the existing `LinkSpawn` payload.
- [x] T007 Add `spawnWaypointAt` and `spawnNextWaypointAt` dispatchers in `editor/Spawning.fs` over the shared palette; both select the new waypoint, refuse at capacity through `hasRoom`, and create nothing when the anchor is unavailable.

**Checkpoint**: Anchor construction, naming, insertion, and one-step spawns are ready.
No user-facing menu is required yet.

---

## Phase 3: User Story 1 - Stamp a waypoint where the player is standing (Priority: P1) 🎯

**Goal**: A player-anchored waypoint uses the player's current authored position and
facing, with unit scale, and is unavailable without a live designation.

**Independent Test**: Place the player at a distinctive transform; the headless test
proves the anchor's construction and the availability gate. End-to-end menu
validation follows once US2's creation menu exists.

**Note**: US1's construction and dispatch are delivered by T003-T007 (the palette is
shared). This phase proves the player-specific behaviour and its gate.

### Tests for User Story 1

- [x] T008 [P] [US1] Add tests in `test/SpawningTests.fs`: a player-anchored waypoint copies the player's position and full rotation with unit scale; returns `None` with no player or a stale designation; `anchorAvailable` is false for Player in those cases; and `spawnWaypointAt Player` creates and selects the waypoint and records exactly one `WaypointCreate`.

**Checkpoint**: The player anchor's data and availability are proven without UI.

---

## Phase 4: User Story 2 - One creation menu instead of a row of anchor buttons (Priority: P1)

**Goal**: One creation control opens a menu of all anchors; adding an anchor extends
the palette rather than the panel, and automatic names make the resulting waypoints
distinguishable.

**Independent Test**: Open the waypoint section, confirm one creation control, create
from every anchor, and confirm the automatic names and selection.

### Tests for User Story 2

- [x] T009 [US2] Add tests in `test/SpawningTests.fs`: creating at Camera twice yields `Camera 1` and `Camera 2`; creating at Origin yields `Origin 1`; a renamed or deleted automatic name is reused by the next creation; every creation selects the new waypoint and records exactly one step; a full waypoint map refuses without recording a step.

### Implementation for User Story 2

- [x] T010 [P] [US2] Replace the four-button waypoint toolbar in `editor/Outliner.fs` with one `Add Waypoint` button that opens a popup of the shared anchor palette, disabling `At Player` when unavailable and creation when the zone is at capacity.
- [x] T011 [US2] Simplify `editor/Outliner.fs`: remove the per-row `Link` control and the `selected`/`linkable` parameters from `slotRows`, updating every call site.

**Checkpoint**: Creation at every anchor works from one menu, with readable names and
one-step undo.

---

## Phase 5: User Story 3 - Author a chain as a property of the selected waypoint (Priority: P2)

**Goal**: The selected waypoint exposes its `Next` and an `Add Next` menu; linking,
retargeting, ending, and insertion are each one undoable step, and insertion
preserves the chain tail.

**Independent Test**: Select a waypoint, link it to another, end the chain, then
insert a new waypoint in the middle; confirm the order and verify undo/redo restores
each state exactly.

### Tests for User Story 3

- [x] T012 [US3] Update/add tests in `test/SpawningTests.fs`: extending a middle waypoint sets `new.Next` to the old next and preserves the tail; undo restores the original link and removes the new waypoint; redo restores the insertion; extending a chain end still links with `Next = None`.

### Implementation for User Story 3

- [x] T013 [P] [US3] Add the waypoint chain section to `editor/Inspector.fs`: a filtered `Next` combo over `(end)` plus every other live waypoint, committing through `Selection.linkRef`, and an `Add Next` popup over the shared palette using `spawnNextWaypointAt`; gate `At Player` and capacity, and report the pick through a local mutable because `state` is a `byref`.

**Checkpoint**: Chains are authored entirely from the selected waypoint; the Outliner
no longer needs chain controls.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, shared comments, and end-to-end validation.

- [x] T014 [P] Record the waypoint authoring interaction (one creation menu, Inspector chain controls, player anchor, automatic names, insertion) in `docs/EDITOR_DESIGN.md`.
- [x] T015 [P] Update the `ComboFilter` comment in `state/EditorState.fs` to note that the waypoint next-chooser shares the reused buffer.
- [ ] T016 Run `dotnet build Sugoi.fsproj`, the full `dotnet test test/Sugoi.Tests.fsproj`, and the manual scenarios in `specs/099-waypoint-authoring/quickstart.md`; confirm the gameplay loop still holds the project's target frame rate (the feature adds no gameplay work), and fix any regressions.
  - Automated portion complete: build succeeds and the full suite passes (674 tests). The manual `quickstart.md` editor scenarios remain to be run in a graphics session.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: starts immediately.
- **Foundational (Phase 2)**: depends on Setup; blocks all user stories.
- **US1 (Phase 3)**: depends on Foundational (its behaviour is constructed there).
- **US2 (Phase 4)**: depends on Foundational and T002 (popup binding).
- **US3 (Phase 5)**: depends on Foundational and T002 (popup binding); it does not
  depend on US2.
- **Polish (Phase 6)**: depends on all desired stories.

### Task Dependencies

- T002 has no dependency on the Spawning work and can run alongside T003-T007.
- T003 → T004 → T005 → T007 (same file, increasing information).
- T006 is independent of T004/T005 but same file as T003-T005, so run it in the
  file's edit sequence.
- T008 depends on T003-T007.
- T009 depends on T003-T007.
- T010 depends on T002 and T007.
- T011 depends on T010 (same file).
- T012 depends on T006.
- T013 depends on T002 and T007.
- T014-T016 depend on all stories.

### User Story Dependencies

- **US1** is independently proven by T008; its menu exposure comes with US2.
- **US2** is independently testable after Foundational; it does not require US1 or
  US3.
- **US3** is independently testable after Foundational; it does not require US1 or
  US2.

### Within Each User Story

- Write or update the headless tests, then the implementation.
- Keep every spawn/link effect on the existing `WaypointCreate`/`LinkSpawn`/`Link`
  commands; do not add a command family.
- Verify the story's checkpoint before moving on.

### Parallel Opportunities

- **T002** (`render/ImGui.fs`) can run beside the `editor/Spawning.fs` work.
- After Foundational, **T010** (`editor/Outliner.fs`) and **T013**
  (`editor/Inspector.fs`) can run in parallel; they touch different files.
- **T008** (tests) can run alongside T010/T013 once Foundational is complete.
- **T014** and **T015** (docs and a comment) can run in parallel.
- Test tasks T008, T009, and T012 share `test/SpawningTests.fs`, so they should not
  be edited concurrently.

## Parallel Example: After Foundational

```text
Developer A: T010 + T011 — one creation menu in editor/Outliner.fs
Developer B: T013 — chain controls in editor/Inspector.fs
Developer C: T008 — player-anchor tests in test/SpawningTests.fs
```

## Implementation Strategy

### MVP First (US2 + US1)

The requested capability needs both P1 stories: US2 provides the creation menu and
US1 provides the player anchor it exposes. Complete Setup + Foundational, then US2,
then US1, and stop to validate the player-anchored creation flow with the automated
tests and the relevant `quickstart.md` steps.

### Incremental Delivery

1. Setup + Foundational → shared anchor/naming/insertion behaviour.
2. US2 → one creation menu for Origin/Camera/Player; validate creation and names.
3. US1 → player-anchor tests and availability; validate the player workflow.
4. US3 → Inspector chain controls and insertion; validate chains and undo/redo.
5. Polish → docs and the full suite.

### Parallel Team Strategy

1. One owner completes T002-T007 (bindings and shared Spawning behaviour).
2. Then split by file:
   - Owner A: T010 + T011 (`editor/Outliner.fs`).
   - Owner B: T013 (`editor/Inspector.fs`).
   - One owner for T008/T009/T012, because all three share `test/SpawningTests.fs`.
3. Finish with T014-T016.

## Notes

- [P] markers indicate different files and no dependency on an incomplete task.
- The existing `WaypointCreate`, `LinkSpawn`, and `Link` commands already carry every
  field this feature writes; no command or undo-runner change is expected.
- `At Player` is an expected `None` when no live player exists; this is not an error.
- Capacity refusal reuses `Spawning.hasRoom` and records nothing.
- Validate with `quickstart.md` after the automated suite passes.
