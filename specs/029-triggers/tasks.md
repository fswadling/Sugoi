---

description: "Task list for the Triggers feature"
---

# Tasks: Triggers

**Input**: Design documents from `/specs/029-triggers/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — the plan (`Testing`) and quickstart specify xUnit coverage in `test/ZoneTests.fs`, `test/SpawningTests.fs`, and a new `test/SystemsTests.fs` for the containment rule, which needs neither a physics world nor a GPU. The Inspector widgets, the wireframe, and the play overlay are covered by the quickstart's interactive scenarios.

**Organization**: Tasks are grouped by user story. The payload, its storage, and its detection land first (they are shared by every story); authoring is US1, detection is US2, and interactable tracking plus the overlay is US3.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- F# single project at the repository root; modules live in domain clusters (`world/`, `state/`, `render/`, `editor/`, `screen/`).
- Compile order is defined by `fsharp-game.fsproj`; no source file is added, so the app's compile list is unchanged. The **test** project gains one file.
- Tests live in `test/` (one `<Module>Tests.fs` per module).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Register the new test module.

- [X] T001 Create `test/SystemsTests.fs` (empty `module SystemsTests` skeleton) and add `<Compile Include="SystemsTests.fs" />` to `test/fsharp-game.Tests.fsproj` after `PlayerTests.fs` and before `GameplayStateTests.fs`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The payload, its zone storage, its edit command, and the detection function. Every user story reads these.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Replace the `Trigger` placeholder with the composed record in `world/Entity.fs` (`{ Base : Entity; Radius : float32; Height : float32; Interactable : bool; Prompt : string; mutable Triggered : bool }`) and add `triggerRef`, with the comment stating that the base position is the base of the cylinder, that `Base.Enabled` is the disabled flag, and that `Triggered` is runtime state.
- [X] T003 [P] Add `Triggers : SlotMap<Trigger>` (capacity 32) to the `Zone` record and `Zone.create` in `world/Zone.fs`, and mention triggers in the module's header comment.
- [X] T004 Add the trigger case to `Zone.isAlive`, `tryEntityAt`, `setTransform`, `removeEntity`, and `bindingAt` (answering `bindingNone`) in `world/Zone.fs`, mirroring the existing waypoint cases, and add the trigger case to `setActorWithAsset` so the shared restore path can re-occupy a trigger slot (depends on T002, T003).
- [X] T005 Add `Zone.setTrigger` to `world/Zone.fs`: raise for a ref of another kind, no-op for a stale or absent one, otherwise reassign the slot payload (depends on T004).
- [X] T006 Add the `TriggerSet of Ref : EntityRef * Before : Trigger * After : Trigger` case to `Command` in `state/Command.fs`, with `apply` writing `After` and `undo` writing `Before` through `Zone.setTrigger` (depends on T005).
- [X] T007 Add `Systems.updateTriggers` to `world/Systems.fs`: derive the player position from the designated actor's entity position, then per trigger write `Triggered` in place using the inclusive cylinder rule (disabled triggers and no-player both clear), and answer the nearest enabled interactable trigger the player is inside by squared distance to the volume's centre; return `EntityRef voption` (depends on T003).
- [X] T008 Add the containment cases to `test/SystemsTests.fs`: inside on the radius and at the base and top boundaries; outside just beyond the radius, above the top, and below the base; a player held inside across repeated updates stays inside; a disabled trigger reports outside and clears; no designation (and a designation whose actor is gone) clears every trigger; two overlapping triggers both report inside; the nearest of two interactable triggers is answered and switches as the player moves; a non-interactable or disabled trigger is never answered (depends on T007).

**Checkpoint**: Triggers can be created, stored, edited, and detected headlessly; nothing is visible in the editor yet.

---

## Phase 3: User Story 1 - Author trigger volumes in a zone (Priority: P1) 🎯 MVP

**Goal**: A trigger can be added, placed, sized, renamed, duplicated, and deleted, drawn as a cylinder wireframe matching its size.

**Independent Test**: add a trigger, move it, set radius and height and confirm the wireframe matches, rename it, duplicate and delete it, and undo each step.

### Implementation for User Story 1

- [X] T009 [US1] Add `Spawning.spawnTrigger` to `editor/Spawning.fs` (spawn through `Zone.spawnTrigger`-equivalent creation, push the reversing `Spawn` command with `Zone.bindingNone`, select it) and a `Trigger` case to `duplicate`; add `Zone.spawnTrigger` to `world/Zone.fs` if the spawn helper is not already covered by the shared path (depends on T004).
- [X] T010 [US1] Replace the Triggers placeholder in `editor/Outliner.fs` with an *Add Trigger* button and a row per live trigger (the shared `entityRow`, so select/*Del*/*Dup*/rename all work) (depends on T009).
- [X] T011 [P] [US1] Add `DebugDraw.cylinder` to `render/DebugDraw.fs`: two 16-segment rings at the ends plus a vertical line at each segment angle, taking a centre, a half-height, and a radius, matching the family's form (depends on nothing).
- [X] T012 [US1] Draw a cylinder wireframe per trigger in the active zone in `editor/Viewport.fs` (`buildLines`), at the volume's centre, in the selection colour when selected and a dedicated trigger colour otherwise (depends on T011, T003).
- [X] T013 [US1] Add the trigger block to `editor/Inspector.fs`: drag floats for radius and height, a checkbox for interactable, a text field for the prompt, and a checkbox for enabled (writing `Base.Enabled`), each committed edit building the payload from the live one and pushing one `TriggerSet`; add the `igDragFloat` P/Invoke to `render/ImGui.fs` and commit a drag on `isItemDeactivatedAfterEdit` (depends on T006).
- [X] T014 [US1] Add the trigger cases to `test/ZoneTests.fs` and `test/SpawningTests.fs`: a spawned trigger resolves and is alive; `setName`/`setTransform` work and keep the ref valid; removal makes the ref stale and a reused slot is not aliased; `bindingAt` answers `bindingNone`; duplication copies the payload; the "kind with no entity" list loses `Trigger` (depends on T004, T009).
- [X] T015 [P] [US1] Update `test/EntityTests.fs`: `Trigger` composes the shared `Entity` base (name and flags) rather than being standalone (depends on T002).

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - Detect the player inside a trigger (Priority: P2)

**Goal**: While playing, each trigger reports whether the player is inside it, and the play-mode overlay shows it.

**Independent Test**: bind a player, place a trigger, play, and walk the player in and out of the volume — including along the radius boundary and over the top — confirming the overlay reports inside exactly where the rule says.

### Implementation for User Story 2

- [X] T016 [US2] Add `mutable InteractableTrigger : EntityRef voption` to `GameplayState` in `state/GameplayState.fs` and set it in `GameplayState.create` (depends on nothing).
- [X] T017 [US2] Call `Systems.updateTriggers` in the play-mode block of `screen/GameplayScreen.fs` (after the body→entity sync, so the player's position is this frame's) and store its answer in `InteractableTrigger`; leave the editing path untouched (depends on T007, T016).
- [X] T018 [US2] Draw the play-mode overlay in `screen/GameplayScreen.fs`: the names of the triggers the player is inside (from their updated flags), so detection is observable without a cue (depends on T017).

**Checkpoint**: Detection is visible while playing, and the containment rule is already covered headlessly (T008).

---

## Phase 5: User Story 3 - Interactable triggers and their prompt (Priority: P3)

**Goal**: The nearest interactable trigger the player is inside has its prompt shown while they are in it.

**Independent Test**: two interactable triggers with prompts, walk from one to the other, and confirm the prompt switches and disappears on leaving.

### Implementation for User Story 3

- [X] T019 [US3] Draw the tracked interactable trigger's prompt on the play-mode overlay in `screen/GameplayScreen.fs`, resolving the tracked ref and skipping an empty prompt (depends on T017).

**Checkpoint**: All three stories work together; the overlay reports detection and the interaction prompt.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Whole-feature validation.

- [X] T020 Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj` and confirm the whole suite passes with the trigger changes.
- [X] T021 Confirm the features the spec defers are absent: no cue property on `Trigger`, no rising-edge output from `updateTriggers`, no interact-key handling — so the cue slice adds the action a trigger performs without unpicking detection (FR-010).
- [ ] T022 Walk the interactive scenarios 10–17 in `specs/029-triggers/quickstart.md` in the editor (with a display): placing and sizing a volume, undo per edit, detection in and around the volume while playing, the disabled and no-player cases, the interactable prompt switching, walking through a volume, and zone scoping. **Not run if the agent environment has no display.**

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks all user stories**.
- **User Stories (Phase 3–5)**: all depend on Phase 2.
  - US1 needs the storage and the command; its wireframe and Inspector block are its own.
  - US2 needs only `updateTriggers` (T007) and the gameplay state field.
  - US3 needs US2's overlay, and nothing else.
- **Polish (Phase 6)**: depends on the desired stories being complete.

### Within the Foundational phase

- T002 ∥ T003 (different files), then T004, then T005 ∥ T006 (same file, in order), with T007 after T003 and T008 after T007.

### Within the User Story phases

- T009 → T010, and T011 ∥ T012's other dependency, then T012 and T013 in either order.
- T016 → T017 → T018 → T019.

### Parallel Opportunities

- T002 ∥ T003.
- T011 (`render/DebugDraw.fs`) ∥ T013 (`render/ImGui.fs` binding) ∥ T015 (`test/EntityTests.fs`) — three different files, none blocking the others.

```bash
# After T005, these are on different files:
Task: "Add DebugDraw.cylinder to render/DebugDraw.fs"
Task: "Update test/EntityTests.fs for the composed Trigger base"
Task: "Add the TriggerSet case to state/Command.fs"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational) — the payload, its storage, the edit command, and detection.
2. Complete Phase 3 (US1) — add, place, size, rename, duplicate, delete, with the wireframe.
3. **Stop and validate**: run `test/ZoneTests.fs`, `test/SpawningTests.fs`, `test/SystemsTests.fs`, then place and size a trigger in the editor and undo a step.

### Incremental Delivery

1. Foundational → triggers are zone entities, detectable headlessly.
2. US1 → triggers can be authored and seen (MVP).
3. US2 → detection is visible while playing.
4. US3 → interactable triggers track the nearest and show their prompt.

---

## Notes

- [P] tasks touch different files and have no incomplete dependencies.
- No new source module: the trigger is a payload type, its storage and writes are cases in modules that already dispatch by kind, its detection is a function on the existing `Systems`, and its UI is a section, a block, and a wireframe in existing panels.
- `Trigger` is deliberately not shown with a runtime flag in the Inspector: the panels are hidden while playing, so it could only be shown stale (research Decision 2).
- The cue is not modelled, not stubbed, and not referenced: FR-010 pins that so the deferral cannot silently grow.
- Verify `dotnet test` passes after each phase; commit per task or logical group.
