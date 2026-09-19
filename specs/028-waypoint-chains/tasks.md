---

description: "Task list for the Waypoint Chains feature"
---

# Tasks: Waypoint Chains

**Input**: Design documents from `/specs/028-waypoint-chains/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — the plan (`Testing`) and quickstart specify xUnit coverage for the model and command paths, extending `test/ZoneTests.fs`, `test/SpawningTests.fs`, and `test/SelectionTests.fs`. The section's button gating and the connectors need an ImGui frame and are covered by the quickstart's interactive scenarios.

**Organization**: Tasks are grouped by user story. The link storage and the two commands are shared by every story and land first; the extend gesture is US1, linking and unlinking existing waypoints is US2, and the connectors are US3.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- F# single project at the repository root; modules live in domain clusters (`world/`, `state/`, `render/`, `editor/`).
- Compile order is defined by `fsharp-game.fsproj`; no new source file is added, so both the app's and the test project's compile lists are unchanged.
- Tests live in `test/` (one `<Module>Tests.fs` per module).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the feature needs no build change.

- [X] T001 Verify that the feature extends existing modules only — `world/Zone.fs`, `state/Command.fs`, `editor/Spawning.fs`, `editor/Selection.fs`, `editor/Outliner.fs`, `editor/Viewport.fs` — so `fsharp-game.fsproj` and `test/fsharp-game.Tests.fsproj` need no new `<Compile>` entry, and no new test module is needed (the three existing test modules are extended).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The link as zone data, and the two commands that make it undoable. Every user story reads this.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Add `Links : Dictionary<EntityRef, EntityRef>` to the `Zone` record and initialise it in `Zone.create` in `world/Zone.fs`, with the comment stating that it is keyed by the source waypoint (so a waypoint has at most one next), that a missing entry ends a chain, and that entries for deleted waypoints are left inert rather than pruned (research Decisions 2 and 4).
- [X] T003 [P] Add `Zone.waypointNext` to `world/Zone.fs`: return `Some target` only when the source is a live waypoint, the map has an entry for it, and the target resolves as a live waypoint; otherwise `None`. Pure — never clear the entry (depends on T002).
- [X] T004 Add `Zone.setWaypointNext` to `world/Zone.fs`: raise (`invalidOp`) on a source or target that is not a waypoint kind and on a self-link; otherwise write (`Some target`) or remove (`None`) the entry. Do not require the target to be alive (depends on T002).
- [X] T005 Add the `Link of Ref : EntityRef * Before : EntityRef option * After : EntityRef option` case to `Command` in `state/Command.fs`, with `apply` writing `After` and `undo` writing `Before` through `Zone.setWaypointNext` (depends on T004).
- [X] T006 Add the `LinkSpawn of From : EntityRef * Before : EntityRef option * Ref : EntityRef * Entity : Entity` case to `Command` in `state/Command.fs`: `apply` creates the waypoint through `setActorWithAsset` (with `bindingNone`) and then points `From` at `Ref`; `undo` restores `From`'s `Before` and then removes `Ref` (depends on T004).
- [X] T007 Add a `// ── Chain links ──` section to `test/ZoneTests.fs`: a link resolves and clears; a chain reads in order (A, B, C by following the links); a self-link raises and leaves the link unchanged; a link to a deleted waypoint does not resolve; deleting the middle of a chain stops the chain at the link that pointed to it; a waypoint spawned into the deleted one's slot is not aliased by either stale link; a link made in one zone does not resolve in another (depends on T002, T003, T004).

**Checkpoint**: Links can be made, resolved, and broken headlessly; nothing is visible in the editor yet.

---

## Phase 3: User Story 1 - Build a chain while placing waypoints (Priority: P1) 🎯 MVP

**Goal**: *Add Next at Camera* creates a waypoint at the camera and links the selected waypoint to it, as one undo step.

**Independent Test**: select a waypoint, add three next-waypoints at three camera positions, confirm the chain follows the placement order and each faces the camera's direction; undo once and confirm the last waypoint is gone and the previous end is the chain's end again.

### Implementation for User Story 1

- [X] T008 [US1] Add `Spawning.spawnLinkedWaypoint` to `editor/Spawning.fs`: read the predecessor's current next (the `Before`), spawn the waypoint through `Zone.spawnWaypoint`, point the predecessor at it with `Zone.setWaypointNext`, and commit the `LinkSpawn` command (which selects the new waypoint) (depends on T006).
- [X] T009 [US1] Add an *Add Next at Camera* button to the Waypoints section of `editor/Outliner.fs`, disabled unless the selection is a waypoint: build the new waypoint exactly as *Add at Camera* does (camera position, and the rotation taking −Z onto the camera forward) and call `Spawning.spawnLinkedWaypoint` (depends on T008).
- [X] T010 [US1] Add the extend-gesture cases to `test/SpawningTests.fs`: the gesture creates a waypoint, links the predecessor to it, selects it, and pushes exactly one `LinkSpawn`; undo removes the waypoint *and* restores the predecessor's previous link; redo reapplies both; extending a waypoint that already had a next replaces it (depends on T008).

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - Link and unlink waypoints that already exist (Priority: P2)

**Goal**: The selected waypoint can be pointed at an existing waypoint, or have its link broken, each as one undo step.

**Independent Test**: place three waypoints, link the first to the second from the list, confirm the chain reads first-then-second, break the link, and confirm the first has no next; undo both and confirm the links return.

### Implementation for User Story 2

- [X] T011 [US2] Add `Selection.linkRef` to `editor/Selection.fs`, mirroring `renameRef`: resolve the current next with `Zone.waypointNext`, return without a command when it already points where asked, otherwise build `Link (ref, before, next)`, `Command.apply` it, and `UndoStack.push` it, leaving selection/drag/nudge untouched (depends on T005).
- [X] T012 [US2] Add the link actions to the Waypoints section of `editor/Outliner.fs`: an *Unlink* button (disabled unless the selected waypoint has a resolvable next) calling `Selection.linkRef … None`, and a per-row *Link* button appended after the shared row actions (disabled unless a different waypoint is selected) calling `Selection.linkRef … (Some rowRef)` (depends on T011).
- [X] T013 [US2] Add the link-edit cases to `test/SelectionTests.fs`: linking pushes exactly one `Link` carrying both sides and sets the next; undo restores the previous next exactly, including when it was a different waypoint; unlinking is one step; linking to the current next pushes nothing; deleting a mid-chain waypoint and undoing restores the whole chain (depends on T011).

**Checkpoint**: US1's gesture plus linking and unlinking existing waypoints both work, with the chain intact across deletes and undos.

---

## Phase 5: User Story 3 - See the chain in the viewport (Priority: P3)

**Goal**: Every link is drawn as a connector with a direction arrow, and nothing is drawn for coincident waypoints.

**Independent Test**: link three waypoints and confirm three connectors appear showing which way the chain runs; move the middle waypoint and confirm its connectors follow; place two waypoints at the same position and confirm no connector is drawn between them.

### Implementation for User Story 3

- [X] T014 [US3] Add the connector to `editor/Viewport.fs`: a `linkColor` (200, 122, 255) beside the other marker colours, a private helper drawing a line from a waypoint to the segment midpoint lifted `0.08` in Y plus a two-wing arrow head (wings `0.07` either side of the direction's perpendicular, base `0.15` back), called from the existing per-waypoint iteration in `buildLines` through `Zone.waypointNext` and `Zone.tryEntityAt`; skip entirely when the two positions are within `0.001` (depends on T003).

**Checkpoint**: Chains are readable in the viewport and follow their waypoints.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Whole-feature validation.

- [X] T015 Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj` and confirm the whole suite passes with the chain changes.
- [ ] T016 Walk the interactive scenarios 10–17 in `specs/028-waypoint-chains/quickstart.md` in the editor (with a display): the extend gesture, the disabled gating, linking and unlinking from the list, connectors following their waypoints, coincident waypoints drawing nothing, delete/undo mid-chain, zone switching, and no change to anything else. **Not run if the agent environment has no display.**

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks all user stories**.
- **User Stories (Phase 3–5)**: all depend on Phase 2.
  - US1 depends on Phase 2's `LinkSpawn` (T006).
  - US2 depends on Phase 2's `Link` (T005).
  - US3 depends only on Phase 2's `waypointNext` (T003).
- **Polish (Phase 6)**: depends on the desired stories being complete.

### Within the Foundational phase

- T002 ∥ T003 (`Zone`), then T004, then T005 ∥ T006 (the same file, so in order), then T007.

### Within the User Story phases

- T008 → T009 → T010.
- T011 → T012; T013 depends on T011.

### Parallel Opportunities

- T005 ∥ T006 are separate cases in one file and can be written together with T007's expectations in view.
- T014 (`editor/Viewport.fs`) is independent of T008–T013 (`editor/Spawning.fs`, `editor/Outliner.fs`, `editor/Selection.fs`), so US3 can proceed alongside US1/US2.

```bash
# After T004, these are on different files:
Task: "Add the connector drawing to editor/Viewport.fs"
Task: "Add Spawning.spawnLinkedWaypoint to editor/Spawning.fs"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational) — the link map and the two commands.
2. Complete Phase 3 (US1) — the extend gesture, which is the reference project's whole chain-authoring surface.
3. **Stop and validate**: run `test/ZoneTests.fs` and `test/SpawningTests.fs`, then build a three-waypoint chain in the editor and undo one step.

### Incremental Delivery

1. Foundational → links resolve, break, and stay inert when stale, headlessly.
2. US1 → chains can be walked out in front of the camera (MVP).
3. US2 → existing waypoints can be linked and unlinked.
4. US3 → the chain is visible in the viewport.

---

## Notes

- [P] tasks touch different files and have no incomplete dependencies.
- No new source module: the link is a map field on `Zone`, the edits are two `Command` cases, and the UI is added to the existing Outliner section.
- `Waypoint`, `spawnWaypoint`, `removeEntity`, `setTransform`, `setName`, and the shared `Spawn`/`Delete`/`Duplicate` commands are deliberately untouched (research Decision 10).
- No production code walks a chain, so nothing depends on a chain being acyclic; the tests walk links themselves to assert order.
- Verify `dotnet test` passes after each phase; commit per task or logical group.
