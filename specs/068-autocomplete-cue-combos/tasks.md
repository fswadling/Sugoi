---
description: "Task list for Autocomplete Cue Combos"
---

# Tasks: Autocomplete Cue Combos

**Input**: Design documents from `/specs/068-autocomplete-cue-combos/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/filtered-combo.md, quickstart.md

**Tests**: Included — the match rule is pure and device-free, so it is asserted headlessly (SC-005). The combos
themselves are device-drawn and are exercised by the walkthrough, as the panel's drawing already is.

**Organization**: Tasks are grouped by user story (US1 P1 the name combos, US2 P2 the kind combo).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 (foundational and polish carry none)

## Path Conventions

- Single F# project at the repository root; sources in `render/`, `state/`, `editor/`, tests in `test/`.

---

## Phase 1: Foundational (Blocking Prerequisites)

**Purpose**: The three bindings, the filter buffer, and the two control functions.

**⚠️ CRITICAL**: No user story can be implemented until this phase is complete.

- [X] T001 [P] `render/ImGui.fs`: add `isWindowAppearing`, `setKeyboardFocusHere` and `closeCurrentPopup`
  (`igIsWindowAppearing`, `igSetKeyboardFocusHere`, `igCloseCurrentPopup` — already exported by `libcimgui.so`).
- [X] T002 [P] `state/EditorState.fs`: add `ComboFilter : byte[]` and allocate it in `create`.
- [X] T003 [P] `editor/CueTree.fs`: add the pure `matches (filter) (name) : bool` (case-insensitive substring; an
  empty filter matches all) and the `filteredCombo` control (filter `inputText` + matching `selectableBool` rows;
  focus on `isWindowAppearing`; Enter takes the top match and `closeCurrentPopup`). (depends on T001)
- [X] T004 Build checkpoint: `dotnet build fsharp-game.fsproj` and `dotnet build test/fsharp-game.Tests.fsproj`
  compile with the helper.

**Checkpoint**: the control exists; the stories can proceed.

---

## Phase 2: User Story 1 - Type to filter a name combo (Priority: P1) 🎯 MVP

**Goal**: every cue-panel name combo (character, place, advent, presentation, look, waypoint, trigger,
clip/morph) is filterable by typing; Enter picks the top match.

**Independent Test**: assert `matches` directly; the combos are driven through a node with a name field.

- [X] T005 [US1] `editor/CueTree.fs`: give `drawEditor` a `filter: byte[]` parameter and convert its character,
  place, advent, presentation (speaker/expression) and look combos to `filteredCombo` (the write each `onPick`
  performs is unchanged). (depends on T003)
- [X] T006 [US1] `editor/CueEditor.fs`: convert `drawName` (clip/morph), `drawWaypointCombo` and `drawTriggerCombo`
  to `CueTree.filteredCombo`, and pass `state.Editor.ComboFilter` into `CueTree.drawEditor` from both `draw` and
  `drawTrigger`. (depends on T005)
- [X] T007 [P] [US1] `test/CuePanelTests.fs`: assert `matches` — empty filter matches all; a substring matches
  case-insensitively (`jo` matches `Jog`); a non-matching filter yields an empty list.

**Checkpoint**: US1 works and is testable independently.

---

## Phase 3: User Story 2 - Type to filter the kind dropdown (Priority: P2)

**Goal**: the grouped kind dropdown flattens to a filtered list while a filter is entered, and restores its
groups when cleared.

**Independent Test**: opening with no filter shows the group separators; typing narrows to a flat list.

- [X] T008 [US2] `editor/CueTree.fs`: in the kind combo, draw the existing grouped list when the filter is empty
  and a flat `matches`-filtered list when it is entered (rows are the same `kindName` strings; a pick is the same
  `defaultOfKind` commit). (depends on T003)

**Checkpoint**: both stories function.

---

## Phase 4: Polish & Cross-Cutting Concerns

- [X] T009 [P] Docs: note the filterable cue combos in `docs/EDITOR_DESIGN.md`.
- [X] T010 `dotnet build` warning-free and `dotnet test` green (SC-005).
- [X] T011 Run the automated section of `quickstart.md` and confirm the headless outcomes.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Foundational (Phase 1)**: no dependencies; **blocks** the user stories.
- **US1–US2 (Phases 2–3)**: depend on Foundational; run in priority order.
- **Polish (Phase 4)**: depends on the stories.

### Within and between stories

- T003 → T005; T005 → T006; T003 → T008.
- The kind combo (T008) is independent of the name-combo wiring (T005/T006) once T003 lands.

### Parallel Opportunities

- T001, T002, T003 (distinct files) then T004.
- T007 (tests) runs beside T005/T006.

---

## Implementation Strategy

### MVP First (User Story 1)

1. Foundational (bindings, buffer, control).
2. US1 (the name combos).
3. **STOP and VALIDATE**: type into a clip combo and see it narrow.
4. US2 (the kind combo), then polish.

### Incremental notes

- The helper removes repeated `beginCombo` boilerplate, so each converted combo gets shorter, not longer.
- The kind combo is the only one with bespoke drawing (grouped vs flat).

## Notes

- `[P]` = different files, no dependencies.
- No cue value or write changes: a pick is the same edit as today, one undo step (FR-005).
- Scope is the cue panel; other panels' dropdowns are out of scope.
- No `.fsproj` change: no module is added or moved.
