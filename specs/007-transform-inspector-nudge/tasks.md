# Tasks: Transform Inspector + Keyboard Nudge

**Input**: Design documents from `/specs/007-transform-inspector-nudge/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/transform-inspector-nudge.md, quickstart.md

**Tests**: `test/EntityTests.fs` covers the pure Euler conversion; UI interaction is
validated manually via `quickstart.md`.

## Phase 1: Setup

**Purpose**: Confirm the baseline.

- [X] T001 Verify baseline: `dotnet build` and `dotnet test` succeed.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The Euler conversion both stories build on.

- [X] T002 Add `eulerDegrees : Quaternion -> Vector3` (ZYX Euler, degrees) to `core/Entity.fs`.
- [X] T003 [P] Add a test for `eulerDegrees` (identity → zero; a known quarter-turn → 90°) to `test/EntityTests.fs`.

**Checkpoint**: compiles; the Euler conversion is unit-tested.

---

## Phase 3: User Story 1 - See the full transform in the Inspector (Priority: P1) 🎯 MVP

**Goal**: the Inspector shows rotation (degrees) and scale.

**Independent Test**: select an entity; the Inspector shows name/position/rotation/scale.

### Implementation for User Story 1

- [X] T004 [US1] In `editor/Inspector.fs`, display rotation via `eulerDegrees` (degrees) and scale alongside name/position (read-only).

**Checkpoint**: rotate/scale gizmos produce a visible Inspector change.

---

## Phase 4: User Story 2 - Nudge the selection with Shift+WASD/QE (Priority: P2)

**Goal**: Shift+WASD/QE nudges the selection without flying the camera or toggling snap.

**Independent Test**: hold Shift and tap WASD/QE in each tool; the Inspector changes; camera does not fly; snap does not toggle.

### Implementation for User Story 2

- [X] T005 [US2] Add `nudgeDirOfKey`, `nudgeTransform`, and the step constants (move 0.05/0.5, rotate 0.02/π/12, scale 0.01/0.25) to `editor/Editor.fs`.
- [X] T006 [US2] Add `Editor.updateNudge` (Shift + not text input + no drag) and call it from `screen/GameplayScreen.fs` when editing.
- [X] T007 [US2] Gate the `Q` snap toggle on `not (Input.isShift input)` in `Editor.handleShortcuts`.
- [X] T008 [US2] Gate the camera fly path on `not (Input.isShift input)` in `screen/GameplayScreen.fs` `applyCamera`.

**Checkpoint**: nudge works in all three tools; no key collision.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [ ] T009 Verify gizmo drag end-to-end (`dotnet run`): click-select and move/rotate/scale drags update the Inspector; fix any mouse-coordinate or mouse-capture bug found.
- [ ] T010 Run `quickstart.md` end-to-end; confirm `dotnet build` is clean (2 expected native-pointer warnings) and `dotnet test` passes.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; blocks both stories.
- **US1 (Phase 3)**: depends on Foundational (`eulerDegrees`).
- **US2 (Phase 4)**: depends on Foundational; independent of US1 (both read the selection).
- **Polish (Phase 5)**: depends on US1 + US2.

### Parallel Opportunities

- T002/T003 (Phase 2) touch different files and can run in parallel.
- US1 (T004) and US2 (T005–T008) are independent once Foundational lands.

## Implementation Strategy

### MVP First (User Story 1)

1. Foundational (T002–T003) → Euler conversion compiles and tests.
2. US1 (T004) → Inspector shows rotation/scale (first visible result: gizmos "work").

Then US2 (T005–T008) → keyboard nudge, then Polish (T009–T010) → end-to-end
verification.
