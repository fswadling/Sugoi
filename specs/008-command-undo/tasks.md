# Tasks: Command / Undo

**Input**: Design documents from `/specs/008-command-undo/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/command-undo.md, quickstart.md

**Tests**: `test/CommandTests.fs` covers the pure command/undo/coalescing logic; UI
interaction is validated manually via `quickstart.md`.

## Phase 1: Setup

**Purpose**: Confirm the baseline.

- [X] T001 Verify baseline: `dotnet build` and `dotnet test` succeed.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The mutable zone and the command core both stories build on.

- [X] T002 Make `core/Zone.fs` mutable: add `mutable` to `Zone.Actors`/`Zone.Environment` and to `Environment.Props`; change `setEntity`/`removeEntity` to mutate in place and return `unit`; change `spawnActor`/`spawnProp` to mutate in place and return `EntityRef`; remove the "TEMPORARY — moves to editor/Command.fs" comment.
- [X] T003 Update the write call sites to the in-place API so the project compiles: `editor/Selection.fs` (`deleteRef`, `duplicateRef`), `editor/AssetBrowser.fs` (spawn), and `editor/Editor.fs` (`setSelectedTransform`). Read-only `Zone.entityAt` callers are unchanged.
- [X] T004 Add `editor/Command.fs` (the `Command` DU — `Spawn`/`Delete`/`Duplicate`/`Transform` — plus `apply`/`undo` and the `UndoStack` with `Transform` coalescing) and insert it in `fsharp-game.fsproj` between `screen/SharedState.fs` and `editor/EditorState.fs`.
- [X] T005 Add `Undo : Command.UndoStack` and `NudgeBefore : Transform option` to `editor/EditorState.fs`, initialised in `create`.
- [X] T006 [P] Add `test/CommandTests.fs` (spawn/delete/duplicate/transform `apply`/`undo` round-trips and `Transform` coalescing) and register it in `test/fsharp-game.Tests.fsproj`.

**Checkpoint**: compiles; the command core is unit-tested; no undo is wired into the editor yet.

---

## Phase 3: User Story 1 - Undo/redo spawn, delete, duplicate (Priority: P1) 🎯 MVP

**Goal**: structural edits are undoable/redoable via `Ctrl+Z`/`Ctrl+Shift+Z`.

**Independent Test**: spawn/delete/duplicate an entity, then undo and redo each.

### Implementation for User Story 1

- [X] T007 [US1] Wire delete to build + apply + push a `Delete` command in `editor/Selection.fs` (`deleteRef`/`delete`), clearing the selection when it was the deleted entity.
- [X] T008 [US1] Wire duplicate to allocate a copy and push a `Duplicate` command (selecting the copy) in `editor/Selection.fs` (`duplicateRef`/`duplicate`).
- [X] T009 [P] [US1] Wire spawn to allocate and push a `Spawn` command (selecting the new entity) in `editor/AssetBrowser.fs`.
- [X] T010 [P] [US1] Add `Ctrl+Z` (undo) and `Ctrl+Y` (redo) to `editor/Editor.fs` `handleShortcuts`, plus an Edit menu with Undo/Redo items in `editor/Editor.fs` `draw`; gated on editing, `Drag = None`, and no text input; both call `Command.UndoStack.undo`/`redo` against `state.Zone`.

**Checkpoint**: spawn/delete/duplicate are undoable and redoable from the keyboard.

---

## Phase 4: User Story 2 - Undo/redo transform as a single gesture (Priority: P2)

**Goal**: a gizmo drag or held nudge collapses into one undo step.

**Independent Test**: drag a gizmo (or hold a nudge), then a single undo reverts the whole gesture.

### Implementation for User Story 2

- [X] T011 [US2] In `editor/Editor.fs` `finishDrag`, push a `Transform` command using `Drag.Start` as `Before` and the current entity transform as `After`.
- [X] T012 [US2] In `editor/Editor.fs` `updateNudge`, capture `Editor.NudgeBefore` on the first nudge frame and push a `Transform` command on release (mirroring `nudge_before`).

**Checkpoint**: transform gestures are undoable as single steps (coalescing covered by T006).

---

## Phase 5: Polish & Cross-Cutting Concerns

- [ ] T013 Run `quickstart.md` end-to-end (`dotnet run`): confirm all scenarios, including undo blocked mid-drag and while typing; fix any shortcut/mouse bug found.
- [X] T014 Confirm `dotnet build` is clean (2 expected native-pointer warnings) and `dotnet test` passes.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup. T002 → T003; T002 → T004 → T005; T004 → T006.
- **US1 (Phase 3)**: depends on Foundational (T004/T005 for the command core, T002/T003 for in-place CRUD).
- **US2 (Phase 4)**: depends on Foundational + US1 (shortcuts); the transform wiring builds on the in-place CRUD and the stack.
- **Polish (Phase 5)**: depends on US1 + US2.

### Parallel Opportunities

- T006 is parallel with T005 (different files).
- Within US1: T009 (AssetBrowser) and T010 (Editor) are parallel with T007/T008; T007 and T008 both edit `Selection.fs`, so run sequentially.

## Implementation Strategy

### MVP First (User Story 1)

1. Foundational (T002–T006) → mutable zone + command core compile and test.
2. US1 (T007–T010) → spawn/delete/duplicate are undoable/redoable from the keyboard (first visible result).

Then US2 (T011–T012) → transform gestures coalesce into single steps, then Polish
(T013–T014) → end-to-end verification.
