# Tasks: Selection + Transform Foundations

**Input**: Design documents from `/specs/005-selection-transform-foundations/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/entity-foundations.md, quickstart.md

**Tests**: Included — the storage lifecycle is verified by unit tests (the spec's
P1 story is only testable via tests). This is a retrospective task list: all tasks
are already complete (commit `ca73afd`) and marked `[X]`.

## Phase 1: Setup

**Purpose**: Confirm the baseline.

- [X] T001 Verify baseline: `dotnet build` and `dotnet test` succeed before changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The entity/reference and slot-map access changes every story builds on.

- [X] T002 Change `EntityRef.Index` from `int` to `uint32` and remove the `NO_ENTITY` sentinel in `core/Entity.fs`.
- [X] T003 Add `transformIdentity` to `core/Entity.fs`.
- [X] T004 Change `Environment` to own `Props : SlotMap<Prop>` in `core/Entity.fs` (declare `Prop` before `Environment`).
- [X] T005 [P] Add index-based `tryGetByIndex`/`setByIndex`/`removeByIndex` to `core/SlotMap.fs`.
- [X] T006 [P] Add slot-map index-accessor tests to `test/SlotMapTests.fs`.
- [X] T007 [P] Update `test/EntityTests.fs` for `Environment.Props` and remove the `NO_ENTITY` test.

**Checkpoint**: entity/ref model and slot-map accessors compile with tests.

---

## Phase 3: User Story 1 - A zone holds the scene's entities (Priority: P1) 🎯 MVP

**Goal**: a `Zone` container for actors + environment-owned props, on the gameplay state.

**Independent Test**: `dotnet test` proves the storage lifecycle via `SlotMapTests.fs`.

### Implementation for User Story 1

- [X] T008 [US1] Create `core/Zone.fs` (`Zone { Name; Actors; Environment }` + `create` with caps 64/512) and insert it in `fsharp-game.fsproj` after `core/Entity.fs`.
- [X] T009 [US1] Add `Zone : Zone.Zone` to `screen/GameplayState.fs` and initialize it in `create`.

**Checkpoint**: gameplay state owns a zone; storage lifecycle tests pass.

---

## Phase 4: User Story 2 - The editor remembers its transform tool and drag state (Priority: P2)

**Goal**: the editor tracks tool, snap, and in-progress drag state.

**Independent Test**: `EditorState.create ()` defaults to "move tool, no snap, no drag"; a `Drag` records its axis + pre-drag transform.

### Implementation for User Story 2

- [X] T010 [US2] Add `Tool`/`GizmoAxis`/`Drag` types and `Tool`/`Snap`/`Drag` fields to `editor/EditorState.fs`.

**Checkpoint**: editor state carries the transform-tool/drag state the gizmo phase needs.

---

## Phase 5: User Story 3 - Debug drawing renders wireframe boxes and spheres (Priority: P3)

**Goal**: box + sphere wireframe primitives for markers and gizmos.

**Independent Test**: the debug-draw accumulator emits a 12-edge box and a sphere outline.

### Implementation for User Story 3

- [X] T011 [US3] Add `wireframeBox` and `sphere` to `render/DebugDraw.fs` (reuse the preallocated accumulator).

**Checkpoint**: wireframe box + sphere primitives available on the line renderer.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T012 Run `dotnet build` (clean, 2 expected native-pointer warnings) and `dotnet test` (27 passing).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; blocks all user stories.
- **US1 (Phase 3)**: depends on Foundational (slot-map access + `Environment.Props`).
- **US2 (Phase 4)**: depends on Foundational (`Transform`/`EntityRef` types).
- **US3 (Phase 5)**: independent of US1/US2 (touches only `DebugDraw.fs`).
- **Polish (Phase 6)**: depends on all stories.

### Parallel Opportunities

- T005/T006/T007 (Phase 2) touch different files and could run in parallel.
- US2 and US3 are independent of each other and of US1's `Zone` wiring; all three
  could proceed in parallel once Foundational lands.

## Implementation Strategy

### MVP First (User Story 1)

1. Foundational (T002–T007) → entity model + slot-map access compile and test.
2. US1 (T008–T009) → a zone lives on the gameplay state, storage is test-proven.

Then US2 and US3 incrementally, each independently verifiable. (All phases were
landed together in this case; the ordering above is the logical dependency order.)
