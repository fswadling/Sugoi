# Tasks: Entity Store

**Input**: Design documents from `/specs/002-entity-store/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/slotmap.md, quickstart.md

**Tests**: Requested — the feature is verified by unit tests (`test/SlotMapTests.fs`, `test/EntityTests.fs`).

## Phase 1: Setup

**Purpose**: Confirm the baseline.

- [X] T001 Verify baseline: `dotnet build` succeeds and `dotnet run` still launches into the main menu.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The test project all stories rely on.

- [X] T002 Create `fsharp-game.Tests.fsproj` (xUnit, `<ProjectReference>` to `fsharp-game.fsproj`) and the `test/` directory; confirm an empty `dotnet test` runs.

**Checkpoint**: `dotnet test` runs (no tests yet, but the harness exists).

---

## Phase 3: User Story 1 - Slot map core (Priority: P1) 🎯 MVP

**Goal**: O(1) add/get/free with free-list reuse.

**Independent Test**: `test/SlotMapTests.fs` proves add/get/free and freed-slot reuse.

### Implementation for User Story 1

- [X] T003 [US1] Create `core/SlotMap.fs`: `Slot<'T>`, `Handle`, and `SlotMap<'T when 'T : struct>` with `add`/`get`/`free`/`count` and a free-list; insert at the top of `fsharp-game.fsproj`.
- [X] T004 [US1] Create `test/SlotMapTests.fs` proving add/get/free, count correctness, freed-slot reuse, and the fail-loudly paths (full-store `add` raises; `get`/`free` on an invalid handle raise).

**Checkpoint**: slot map adds/gets/frees and reuses freed slots (US1 complete).

---

## Phase 4: User Story 2 - Stale-handle detection (Priority: P2)

**Goal**: reject handles whose slot has been freed and reused.

**Independent Test**: a unit test frees an entity, reuses its slot, and asserts the old handle is rejected.

### Implementation for User Story 2

- [X] T005 [US2] Add generation tracking to `core/SlotMap.fs`: bump `Generation` on `free`, and validate `Index` + `Generation` in `get`/`free`.
- [X] T006 [US2] Add stale-handle rejection tests to `test/SlotMapTests.fs`.

**Checkpoint**: stale handles are rejected (US2 complete).

---

## Phase 5: User Story 3 - Entity model (Priority: P3)

**Goal**: entity kinds, references, and a shared base composed by specialised entities.

**Independent Test**: `test/EntityTests.fs` constructs each entity kind and asserts the shared base fields.

### Implementation for User Story 3

- [X] T007 [US3] Create `core/Entity.fs`: `Transform` (using `System.Numerics.Vector3`/`Quaternion`), `EntityKind`, `EntityRef` (+ `NO_ENTITY`), base `Entity`, and `Actor`/`Environment`/`Prop` (embed `Base : Entity`) plus standalone `Waypoint`/`Trigger`; insert after `core/SlotMap.fs` in `fsharp-game.fsproj`.
- [X] T008 [US3] Create `test/EntityTests.fs` asserting each kind composes the shared base fields and `NO_ENTITY` is distinct.

**Checkpoint**: entity model compiles and is tested (US3 complete).

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T009 Run `quickstart.md` validation: `dotnet build` clean + `dotnet test` green + `dotnet run` still launches.
- [X] T010 Confirm `dotnet build` shows only the 2 expected native-pointer warnings (in `Engine.fs`).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; blocks all stories (tests need the project).
- **US1 (Phase 3)**: depends on Foundational.
- **US2 (Phase 4)**: depends on US1 (generation is added to the slot map).
- **US3 (Phase 5)**: depends on US1 (entity references slot indices) but not US2.
- **Polish (Phase 6)**: depends on all stories.

### Parallel Opportunities

- T007/T008 (entity model) are independent of T005/T006 (stale detection) once
  US1 lands, so US2 and US3 can proceed in parallel.

## Implementation Strategy

### MVP First (User Story 1)

1. T001 → T002 → T003 → T004 (slot map core) → validate `dotnet test`.

Then US2 and US3 incrementally, each independently testable.
