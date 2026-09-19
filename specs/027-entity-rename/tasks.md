---

description: "Task list for the Entity Rename feature"
---

# Tasks: Entity Rename

**Input**: Design documents from `/specs/027-entity-rename/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — the plan (`Testing`) and quickstart specify xUnit coverage for the rename's data path (`Zone.setName`, `Selection.renameRef`, the `Rename` command through `UndoStack`) in `test/ZoneTests.fs` and a new `test/SelectionTests.fs`. The field's focus/commit interaction needs an ImGui frame, so it is covered by the quickstart's interactive scenarios instead.

**Organization**: Tasks are grouped by user story. The rename's data path (command, zone write, selection entry point) is shared by every story and lands first; the field itself is US1; the guard rails that keep a pending edit attributed to its own entity are US2; the "nothing else changes" boundary is US3.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- F# single project at the repository root; modules live in domain clusters (`world/`, `state/`, `render/`, `editor/`).
- Compile order is defined by `fsharp-game.fsproj`; no source file is added, so the app's compile list is unchanged. The **test** project gains one file.
- Tests live in `test/` (one `<Module>Tests.fs` per module).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Register the new test module.

- [X] T001 Create `test/SelectionTests.fs` (empty `module SelectionTests` skeleton) and add `<Compile Include="SelectionTests.fs" />` to `test/fsharp-game.Tests.fsproj` after `SpawningTests.fs` and before `PlayerTests.fs`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The rename as an undoable edit: the command, the zone write, and the selection entry point. Every user story reads this.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Add the `Rename of Ref : EntityRef * Before : string * After : string` case to `Command` in `state/Command.fs`, with `apply` writing `After` and `undo` writing `Before`, both through `Zone.setName`.
- [X] T003 [P] Add `Zone.setName` to `world/Zone.fs`: dispatch on the ref's kind over `Actor`/`Prop`/`Waypoint`/`Part`, reassigning the payload as `{ p with Base = { p.Base with Name = name } }` in place; no-op for a stale/absent ref or an unstored kind; no physics or binding work. Put it next to `setTransform` and mirror its shape.
- [X] T004 Add `Selection.renameRef` to `editor/Selection.fs` (depends on T002, T003): raise (`invalidOp`) on an empty or whitespace-only name; resolve the entity with `Zone.tryEntityAt` (no-op when it is gone); return without a command when the trimmed name equals the current one; otherwise build `Rename (ref, current, trimmed)`, `Command.apply` it, and `UndoStack.push` it, leaving selection/drag/nudge untouched.

**Checkpoint**: A rename can be applied and undone headlessly; nothing is visible in the editor yet.

---

## Phase 3: User Story 1 - Rename the selected entity in the Inspector (Priority: P1) 🎯 MVP

**Goal**: The Inspector's name row is a text field that commits on Enter or on leaving the field, as one undoable step, and the entity's row follows it in the Outliner.

**Independent Test**: select an entity, rename it, confirm the new name in the Inspector and its Outliner row, undo and redo, and repeat for an actor, a prop, a part, and a waypoint.

### Implementation for User Story 1

- [X] T005 [P] [US1] Add the `isItemDeactivatedAfterEdit` P/Invoke (entry point `igIsItemDeactivatedAfterEdit`, already exported by the vendored library) and the `enterReturnsTrue` / `autoSelectAll` input-text flag constants to `render/ImGui.fs`, beside the existing `inputText` binding and `passthruCentralNode` flag.
- [X] T006 [P] [US1] Add `entityNameBufferSize` (256) and `entityNameMaxChars` (63) literals, plus the `NameBuffer : byte[]` and `NameEditRef : EntityRef option` fields, to `state/EditorState.fs`, allocating the buffer and initialising the ref to `None` in `EditorState.create`.
- [X] T007 [US1] Turn the Inspector's name row into a text field in `editor/Inspector.fs` and change `Inspector.draw` to take `shared` (updating its single call site in `editor/Editor.fs`): seed `NameBuffer` from the selected entity's name each frame (encode at most `entityNameMaxChars` characters straight into the buffer and NUL-terminate it, recording `Some ref` in `NameEditRef`), draw it with `inputText("Name", …, enterReturnsTrue ||| autoSelectAll, …)`, and commit via `Selection.renameRef` when the call returns true (Enter) or `isItemDeactivatedAfterEdit()` is true (depends on T004, T005, T006).
- [X] T008 [US1] Add rename cases to `test/SelectionTests.fs`: `renameRef` changes the name; it pushes exactly one `Rename` command; undo restores the previous name and redo reapplies the new one; the command's `Before`/`After` match the entity's names (depends on T004).

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - Move between entities without the name field leaking text (Priority: P2)

**Goal**: An uncommitted edit never becomes anybody's name, an empty name is never stored, and text typed for one entity is applied to that entity even if the selection changed.

**Independent Test**: type without committing and switch entities; empty the field and click away; rename a sequence of entities and confirm each keeps exactly the name typed for it.

### Implementation for User Story 2

- [X] T009 [US2] Gate and attribute the commit in `editor/Inspector.fs`: commit nothing when the trimmed text is empty or equals the name of the entity in `NameEditRef` (no undo step for a no-op), and apply the text to `NameEditRef`'s entity rather than to the current selection, then re-point `NameEditRef` at the current selection, so a mid-edit selection change commits to the entity it was typed for (depends on T007).
- [X] T010 [US2] Add the guard cases to `test/SelectionTests.fs`: an empty and a whitespace-only name raise and leave the name unchanged; a rename to the identical name raises no command and adds no undo step; a rename addressed to a deleted (stale) ref changes nothing and creates nothing (depends on T004).

**Checkpoint**: US1's happy path plus the interaction's sharp edges are both covered.

---

## Phase 5: User Story 3 - A rename changes nothing else about the entity (Priority: P3)

**Goal**: The rename is a label change, duplicates are allowed, and nothing else about the entity moves.

**Independent Test**: rename each kind and confirm transform, collider, and asset are untouched; give two entities the same name and edit each independently.

### Implementation for User Story 3

- [X] T011 [US3] Add `Zone.setName` cases to `test/ZoneTests.fs` under a `// ── Names ──` banner: the name changes for an actor, a prop, a part, and a waypoint; the transform, physics attachment, and asset binding are unchanged by the rename; a stale ref and `Environment`/`Trigger` refs are no-ops; two entities can hold the same name and each still resolves by its own ref (depends on T003).

**Checkpoint**: The feature's boundary is pinned by tests, not by convention.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Whole-feature validation.

- [X] T012 Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj` and confirm the whole suite passes with the rename changes.
- [ ] T013 Walk the interactive scenarios 7–17 in `specs/027-entity-rename/quickstart.md` in the editor (with a display): the field appears for every kind, Enter and click-away both commit, undo/redo, an empty name is not stored, no leakage between entities, typing does not trigger shortcuts, and the renamed entity keeps colliding and rendering. **Not run if the agent environment has no display.**

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks all user stories**.
- **User Stories (Phase 3–5)**: all depend on Phase 2.
  - US1 adds the field itself and depends on Phase 2 only.
  - US2 refines US1's commit logic (T009 depends on T007), but its tests depend only on Phase 2.
  - US3 tests Phase 2's zone write only.
- **Polish (Phase 6)**: depends on the desired stories being complete.

### Within the Foundational phase

- T002 (`state/Command.fs`) ∥ T003 (`world/Zone.fs`).
- T004 depends on both.

### Within the User Story phases

- T005 ∥ T006 ∥ T003 (three different files).
- T007 depends on T004, T005, T006.
- T009 depends on T007.
- T008 ∥ T010 ∥ T011 (test cases in two files; T008 and T010 share `SelectionTests.fs`, so run them in order).

### Parallel Opportunities

- T002 ∥ T003.
- T005 ∥ T006 (binding and editor state are independent files).

```bash
# After T001, the foundational pieces are on different files:
Task: "Add the Rename case to state/Command.fs"
Task: "Add Zone.setName to world/Zone.fs"
Task: "Add the ImGui commit signal and flag constants to render/ImGui.fs"
Task: "Add the name buffer and owner ref to state/EditorState.fs"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational) — the undoable rename.
2. Complete Phase 3 (US1) — the Inspector field.
3. **Stop and validate**: run `test/SelectionTests.fs` and `test/ZoneTests.fs`, then rename an actor in the editor and undo it.

### Incremental Delivery

1. Foundational → renames apply and undo headlessly.
2. US1 → rename from the Inspector (MVP).
3. US2 → the guards that keep edits attributed and never store an empty name.
4. US3 → the boundary: a rename changes the label and nothing else.

---

## Notes

- [P] tasks touch different files and have no incomplete dependencies.
- No new source module: a name is a field on the existing shared `Entity`, the change is one new command case, and the field lives in the existing Inspector.
- `Entity.Name` stays immutable; the payload is reassigned in place, as `setTransform` already does.
- The name field reuses one buffer, re-seeded every frame; ImGui's in-widget text takes priority over it while the field is active, so re-seeding cannot disturb typing (research Decision 5).
- Verify `dotnet test` passes after each phase; commit per task or logical group.

---

## Phase 7: Convergence

- [X] T014 Reconcile FR-010's same-frame visibility with the panel draw order in `editor/Editor.fs` — the Inspector commits the rename after the Outliner has already drawn, so the entity's row shows the new name one frame later — by either drawing the Inspector before the Outliner or narrowing the requirement to "no later than the next frame" per FR-010 / SC-004 (partial). *Resolved by narrowing the requirement (the second branch): FR-010, SC-004, and US1/AC6 now say "by the next frame"; the panel draw order is unchanged, since one frame of label latency is imperceptible and reordering working panels would change focus/navigation order for no visible gain.*
- [X] T015 Extract the rename commit decision from `Inspector.draw` into a pure function (edited text, owner ref, submitted, deactivated, current name) and cover it in `test/SelectionTests.fs`, so the attribution rule behind "a rename never lands on an entity other than the one it was typed for" is guarded by a test rather than by reasoning about ImGui's internals per FR-005 / SC-003 (partial). *Done as `Selection.renameTarget (owner) (finished) (text)`: it takes the owner ref (never the selection), the field's commit signal, and the buffer text, and answers which entity to rename and to what. The Inspector now only draws the field and calls it.*
- [X] T016 Add a headless assertion in `test/ZoneTests.fs` that repeated renames of one entity leave its physics body, the zone's body map, and the kind's slot count unchanged, covering the frame-rate / no-growth criterion for renames per SC-006 (partial).
