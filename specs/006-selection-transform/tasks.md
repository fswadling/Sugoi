# Tasks: Selection + Transform

**Input**: Design documents from `/specs/006-selection-transform/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/selection-transform.md, quickstart.md

**Tests**: `test/RaycastTests.fs` covers the pure ray math; UI interaction is
validated manually via `quickstart.md`.

## Phase 1: Setup

**Purpose**: Confirm the baseline.

- [X] T001 Verify baseline: `dotnet build` and `dotnet test` succeed.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Input plumbing + entity access + ray math every story builds on.

- [X] T002 Add the `cimgui_display_size()` and `cimgui_want_capture_mouse()` shims to `native/cimgui_sdl3.cpp` and rebuild `libcimgui.so` via `native/build.sh`.
- [X] T003 Bind `ImGui.displaySize` and `ImGui.wantCaptureMouse` in `render/ImGui.fs`.
- [X] T004 Add `MouseX`/`MouseY`/`LeftDown` to `Input` in `screen/Screen.fs`.
- [X] T005 Add `PrevLeftDown`/`WindowW`/`WindowH` to `screen/GameplayState.fs`; in `screen/GameplayScreen.fs`, gather mouse/size and set `Camera.Aspect = w/h` from `ImGui.displaySize`.
- [X] T006 [P] Add `entityAt`/`setEntity`/`removeEntity`/`spawnActor`/`spawnProp` to `core/Zone.fs` (marked temporary — migrate to `editor/Command.fs` in 2d).
- [X] T007 [P] Create `editor/Raycast.fs` (`Ray`, `rayFromScreen`, `intersectAabb`, `planeIntersect`, `raySegmentDistance`) and insert it in `fsharp-game.fsproj` before `editor/Viewport.fs`.
- [X] T008 [P] Add `test/RaycastTests.fs` (ray-through-centre, box hit/miss, plane hit/miss, segment distance) to `test/fsharp-game.Tests.fsproj`.

**Checkpoint**: compiles; ray math is unit-tested; zone entity access exists.

---

## Phase 3: User Story 1 - Spawn an entity from the asset browser (Priority: P1) 🎯 MVP

**Goal**: clicking an actor/prop asset adds it to the zone and selects it.

**Independent Test**: click an asset; a wireframe marker appears and is selected.

### Implementation for User Story 1

- [X] T009 [US1] Fill `editor/AssetBrowser.fs`: render actor/prop assets as buttons; on click, `spawnActor`/`spawnProp` with a base entity named after the asset, and set `Editor.Selection`.
- [X] T010 [US1] Add `Viewport.buildLines` in `editor/Viewport.fs`: draw a `wireframeBox` per actor/prop, orange when selected, and call it from `Viewport.drawGrid`/draw.

**Checkpoint**: spawn a marker and see it selected in the viewport.

---

## Phase 4: User Story 2 - Select, focus, delete, and duplicate (Priority: P2)

**Goal**: click-to-select plus `F`/`Del`/`Ctrl+D`.

**Independent Test**: click markers to move the highlight; `F`/`Del`/`Ctrl+D` act on it.

### Implementation for User Story 2

- [X] T011 [US2] Add `Viewport.ray`/`pick` (cast `rayFromScreen`, `intersectAabb` over the zone, nearest hit).
- [X] T012 [US2] In `editor/Editor.fs`, add `interact` — on left-press (and not `ImGui.wantCaptureMouse()`), `pick` and set the selection (`None` clears on empty click) — and call it from `GameplayScreen.update` when editing.
- [X] T013 [US2] Add `F` (focus), `Del` (delete via `removeEntity` + clear selection), `Ctrl+D` (duplicate via `spawn*` + select copy) to `Editor.handleShortcuts`.
- [X] T014 [US2] Fill `editor/Outliner.fs`: list actors/props, click to select, Delete/Duplicate buttons.

**Checkpoint**: select/focus/delete/duplicate all work from the viewport and outliner.

---

## Phase 5: User Story 3 - Transform an entity with gizmos (Priority: P3)

**Goal**: move/rotate/scale the selection via gizmos.

**Independent Test**: select an entity and drag a gizmo handle; `1`/`2`/`3`/`Q` change behaviour.

### Implementation for User Story 3

- [X] T015 [US3] Add `axisDir`/`basis` and `Viewport.drawGizmo` (move lines + tip spheres, scale lines + tip boxes, rotate rings) and draw it for the selection in `buildLines`.
- [X] T016 [US3] Add `Viewport.gizmoAxisAt` (segment hit for move/scale, ring hit for rotate).
- [X] T017 [US3] In `editor/Editor.fs`, add `1`/`2`/`3` (tool) and `Q` (snap) to `handleShortcuts`.
- [X] T018 [US3] Add the drag lifecycle: `interact` begins a `Drag` when `gizmoAxisAt` hits, `applyDrag` derives the transform (move/scale project onto the camera plane, rotate measures the angle), `finishDrag` clears it; call `applyDrag`/`finishDrag` from `GameplayScreen.update`.
- [X] T019 [US3] Fill `editor/Inspector.fs`: read-only name + transform for the selection.

**Checkpoint**: gizmos translate/rotate/scale the selection; drag ends at the final value.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T020 Run `quickstart.md` end-to-end; confirm no per-frame allocation in `buildLines`/gizmo path.
- [X] T021 Confirm `dotnet build` is clean (2 expected native-pointer warnings) and `dotnet test` passes.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; blocks all stories.
- **US1 (Phase 3)**: depends on Foundational (zone helpers + `buildLines`).
- **US2 (Phase 4)**: depends on Foundational (ray + input); benefits from US1 (has entities to select).
- **US3 (Phase 5)**: depends on US2 (needs a selection) + Foundational (gizmo axis math).
- **Polish (Phase 6)**: depends on all stories.

### Parallel Opportunities

- T006/T007/T008 (Phase 2) touch different files and can run in parallel.
- US1's outliner/asset-browser wiring is independent of US2's raycast; once
  Foundational lands, US1 and US2 can proceed in parallel.

## Implementation Strategy

### MVP First (User Story 1)

1. Foundational (T002–T008) → input + ray math + zone access compile and test.
2. US1 (T009–T010) → spawn a marker and see it selected (first visible result).

Then US2 (select/focus/delete/duplicate) and US3 (gizmos), each independently
verifiable, building toward the full spawn → select → transform loop.

---

## Phase 7: Convergence

- [X] T022 Add Delete/Duplicate buttons to `editor/Outliner.fs` for the current selection (the panel lists + click-selects only; the `Del`/`Ctrl+D` keyboard shortcuts already work) per T014 (partial)
- [X] T023 Reorganise `editor/Outliner.fs` into collapsible sections (Actors, Props, Triggers, Waypoints) with inline Del/Dup buttons per row (via `Selection.deleteRef`/`duplicateRef` and new `ImGui.collapsingHeader`/`smallButton`/`sameLine` bindings), replacing the bottom buttons; Triggers/Waypoints are empty `(none)` placeholders until the zone stores those kinds
