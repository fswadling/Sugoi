# Tasks: Editor Workspace Shell

**Input**: Design documents from `/specs/003-editor-workspace/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/editor-workspace.md, quickstart.md

**Tests**: Not requested — validation is manual (visual UI) via `quickstart.md`.

## Phase 1: Setup

**Purpose**: Confirm the baseline.

- [X] T001 Verify baseline: `dotnet build` succeeds and `dotnet run` launches into the main menu.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The binding + data layers all stories build on.

- [X] T002 Add ImGui docking/widget bindings to `render/ImGui.fs`: `DockSpace`, `MainMenuBar`, `Menu`, `MenuItem`, `TreeNode`, `CollapsingHeader`, `SameLine`, `Separator`, `BeginChild`.
- [X] T003 Create `editor/EditorState.fs` (`Selection : EntityRef option`) and grow `screen/GameplayState.fs` to hold `Camera`, `Editor`, and `Editing : bool`.
- [X] T004 Adjust `fsharp-game.fsproj` compile order for the screen↔editor split (core → render → screen/data → editor → screen/behaviour → Engine).

**Checkpoint**: the shell compiles with the new binding + data layers.

---

## Phase 3: User Story 1 - Open the editor workspace (Priority: P1) 🎯 MVP

**Goal**: Play opens a dockable workspace (menu bar + four panels).

**Independent Test**: launch → menu → Play shows the dockable workspace.

### Implementation for User Story 1

- [X] T005 [US1] Create `editor/Editor.fs` (dock space + menu bar) and the four panel shells: `editor/Viewport.fs`, `editor/Outliner.fs`, `editor/Inspector.fs`, `editor/AssetBrowser.fs` (each an empty/titled window).
- [X] T006 [US1] Wire `screen/GameplayScreen.fs` to call `Editor.draw` instead of the placeholder.

**Checkpoint**: Play opens the dockable workspace (menu bar + four empty panels).

---

## Phase 4: User Story 2 - Navigate the 3D viewport (Priority: P2)

**Goal**: the viewport renders a grid and orbits/pans/zooms.

**Independent Test**: drag/scroll in the viewport and see the grid move.

### Implementation for User Story 2

- [X] T007 [US2] Create `render/Camera.fs` (look-at orbit camera: `Position`/`Target`/`Up` + `orbit`/`dolly`/`pan`, → view-projection via `System.Numerics`).
- [X] T008a [US2] Write the line shader (`shaders/line.vert` + `shaders/line.frag`) and compile to SPIR-V with `glslc`.
- [X] T008b [US2] Create `render/DebugDraw.fs` GPU resources: line pipeline (`GPUPrimitiveType.LineList`), vertex/staging/uniform buffers.
- [X] T008c [US2] Implement the `DebugDraw` CPU accumulator: `clear`/`line`/`drawGrid` (pre-allocated buffer, no per-frame allocation).
- [X] T008d [US2] Implement `DebugDraw.upload` (copy pass) + `draw` (bind pipeline, push view-projection, draw).
- [X] T009a [US2] Gather SDL input in `Engine.fs` (mouse pos/buttons/wheel + held keys) and forward it to `GameplayScreen.update`.
- [X] T009b [US2] Fill `editor/Viewport.fs`: emit the grid each frame and apply orbit/dolly/fly to the camera.
- [X] T009c [US2] Render the grid to the swapchain before `ImGui.renderDrawData` in `Engine.fs`.
- [X] T009d [US2] Load the line shader SPIR-V from disk at startup and add the `shaders/*.spv` copy step to `fsharp-game.fsproj`.

**Checkpoint**: viewport shows the grid and responds to pitch/yaw/pan/zoom.

---

## Phase 5: User Story 3 - Browse discovered assets (Priority: P3)

**Goal**: the Asset Browser lists discovered assets.

**Independent Test**: with/without assets on disk, the browser lists them (or is empty).

### Implementation for User Story 3

- [X] T010 [US3] Create `core/Assets.fs` (scan `actors/`/`environments/`/`props/` + JSON manifests).
- [X] T011 [US3] Fill `editor/AssetBrowser.fs`: list assets by name/kind.

**Checkpoint**: asset browser lists discovered assets.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T012 Run `quickstart.md` validation end-to-end (launch → Play → navigate → assets → quit).
- [X] T013 Confirm `dotnet build` is clean (2 expected native-pointer warnings).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; blocks all stories.
- **US1 (Phase 3)**: depends on Foundational.
- **US2 (Phase 4)**: depends on US1 (viewport panel exists) + Foundational.
- **US3 (Phase 5)**: depends on US1 (browser panel exists) + Foundational.
- **Polish (Phase 6)**: depends on all stories.

### Parallel Opportunities

- US2 (camera + grid) and US3 (assets + browser) are independent once US1 lands
  and can proceed in parallel.

## Implementation Strategy

### MVP First (User Story 1)

1. T001 → T002–T004 (foundation) → T005–T006 (US1) → validate launch-to-workspace.

Then US2 and US3 incrementally, each independently testable.

---

## Phase 7: Convergence

- [X] T014 Eliminate per-frame allocations in the render/draw hot path — `DebugDraw.writeMatrix` (16-float array), `DebugDraw.draw` (`[| binding |]`), and `Engine.fs` (`[| ti |]` × 2) allocate each frame, contradicting `plan.md` "no per-frame allocation" and Constitution V (contradicts)
- [ ] T015 Capture the backtick play/edit toggle in `spec.md` (or remove it) — `GameplayScreen` toggles `Editing` and shows the grid in play mode, but the spec's Assumptions and `data-model.md` state the editor is always in edit mode this milestone (unrequested)
