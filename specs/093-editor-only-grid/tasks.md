---

description: "Task list for editor-only reference grid"
---

# Tasks: Editor-Only Reference Grid

**Input**: Design documents from `/specs/093-editor-only-grid/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [quickstart.md](./quickstart.md)

**Tests**: Requested — the spec requires the visibility rule to be asserted headlessly (FR-007,
SC-005), so each user story carries its own device-free test task.

**Organization**: Two user stories. US1 (P1) is the gate and is the MVP; US2 (P2) is its other half —
returning to the editor restores the grid.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)
- Exact file paths are given in every description

## Path Conventions

Single project. Sources live in their domain cluster at the repository root (`editor/`, `screen/`);
tests live in `test/`. No new source module is added, so `fsharp-game.fsproj` is **not** touched.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm a clean baseline before editing.

- [x] T001 At the repository root, run `dotnet build` (expect no warnings) and `dotnet test` (expect green) to record the pre-change baseline.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: none — this feature is a single gate (`Viewport.drawGrid`) with no shared prerequisite
infrastructure. It is delivered whole in User Story 1; there is nothing to complete first.

**Checkpoint**: proceed straight to User Story 1.

---

## Phase 3: User Story 1 - The grid is visible only while editing (Priority: P1) 🎯 MVP

**Goal**: While playing, the viewport draws no reference grid, and entering play leaves none of the
previous editing frame's grid lines in the frame.

**Independent Test**: On a device-free fixture, run `Viewport.drawGrid` once with `Editing = true` (the accumulator fills), then set `Editing = false` and call it again — the accumulator is emptied (`Count = 0`). Against the current (ungated) code the second call leaves `Count > 0`, so the test fails before the gate is added.

### Tests for User Story 1 ⚠️

> **NOTE: Write these tests FIRST; confirm T004 fails against the ungated `drawGrid` before T005.**

- [x] T002 [P] [US1] Create `test/ViewportTests.fs`: `open Xunit`, `open Viewport`, `open GameplayState`; add a `newState ()` helper using `GameplayState.create (Unchecked.defaultof<DebugDraw.DebugDraw>) …` (the `test/FadeTests.fs` fixture), and a device-free `DebugDraw` fixture whose CPU `Data` array is present while its GPU-handle fields are `Unchecked.defaultof` (the `test/TestSupport.fs` "device-free fixture" precedent). Allocate `Data = Array.zeroCreate 65536` — generously larger than the grid's ~5,600 floats — with a comment; do **not** mirror the accumulator's private floats-per-vertex stride.
- [x] T003 [P] [US1] Add `test/ViewportTests.fs` to the compile list in `test/fsharp-game.Tests.fsproj`, immediately after the `FadeTests.fs` entry, with a one-line comment naming spec 093.
- [x] T004 [US1] In `test/ViewportTests.fs`, add a test for the **play transition** (FR-002, SC-002): on the device-free fixture, first run `Viewport.drawGrid &state` with `state.Editing <- true` so the accumulator is populated (`Count > 0`), then set `state.Editing <- false` and assert a second `Viewport.drawGrid &state` leaves `state.DebugDraw.Count = 0`. Run `dotnet test` and confirm it FAILS before the gate exists (the ungated grid leaves `Count > 0` after the switch).

### Implementation for User Story 1

- [x] T005 [US1] Gate `Viewport.drawGrid` in `editor/Viewport.fs` on `state.Editing`: rebuild (`DebugDraw.clear state.DebugDraw |> DebugDraw.drawGrid`) while editing, and **empty** (`DebugDraw.clear state.DebugDraw`) while playing; keep the signature `(state: byref<GameplayState.GameplayState>) : unit` and the call site unchanged. Update the module header and the `// ── Reference grid … ──` section banner to say editor-only. Run `dotnet test` and confirm T004 now PASSES.

**Checkpoint**: User Story 1 is complete — play-mode frames contain no grid, and the headless test proves it.

---

## Phase 4: User Story 2 - Returning to the editor restores the grid (Priority: P2)

**Goal**: Re-entering the editor draws the grid again, with the same appearance as before this feature.

**Independent Test**: With `Editing = true` (on a device-free fixture), `Viewport.drawGrid` yields an
accumulator equal to a freshly built reference grid — the same `Count` and an identical `Data` buffer —
the same grid the play/editing toggle must restore.

### Tests for User Story 2 ⚠️

- [x] T006 [P] [US2] In `test/ViewportTests.fs`, add a test that with `Editing = true` on the device-free fixture, `Viewport.drawGrid` leaves the accumulator equal to a freshly built reference grid (FR-004, SC-004): the same `Count` **and** an identical `Data` buffer, where the reference is `DebugDraw.clear dd |> DebugDraw.drawGrid` on a second zeroed fixture. Compare the **whole** `Data` arrays (both fixtures start zeroed and only the grid writes into them, so no vertex stride is assumed). Assert this for both a fresh state and a state just returned from play — proving the grid is restored and byte-for-byte unchanged.

### Implementation for User Story 2

- [x] T007 [P] [US2] Update the editor-only wording in `screen/GameplayScreen.fs`: the `draw` overlay comment (note that the grid is emptied by `Viewport.drawGrid` while playing, alongside the gizmo clear) and the `renderScene` "grid + gizmo line vertices" comment, so no comment implies an always-visible grid.

**Checkpoint**: Both stories pass — the grid draws while editing, hides while playing, and its editor appearance is unchanged.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Final verification across both stories.

- [x] T008 Run `dotnet build` (expect warning-free) and `dotnet test` (expect green), then work through the manual toggle validation in [quickstart.md](./quickstart.md) (backtick to play, confirm no grid; backtick to edit, confirm it returns).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: empty.
- **User Story 1 (Phase 3)**: after Setup; delivers the gate.
- **User Story 2 (Phase 4)**: after US1 (its test asserts the same function's editing branch; its comment task is independent).
- **Polish (Phase 5)**: after both stories.

### Within User Story 1

- T002 and T003 (`[P]`) can go together (different files).
- T004 (the test expectation) MUST be written and FAIL before T005 (the gate).
- T005 depends on T002 and T004.

### Parallel Opportunities

- T002 ∥ T003 (test file vs. project file).
- T006 ∥ T007 (test file vs. `screen/GameplayScreen.fs`).

---

## Parallel Example: User Story 1

```bash
# Test file and its registration can be prepared together:
Task: "Create test/ViewportTests.fs with the fixture and newState helper"
Task: "Register test/ViewportTests.fs in test/fsharp-game.Tests.fsproj"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (baseline) and Phase 3 (US1).
2. **STOP and VALIDATE**: `dotnet test` proves play-mode frames draw no grid.
3. This is the whole user-visible fix; US2 only guards the return path.

### Incremental Delivery

1. US1 → grid is gone in play (the fix). Validate independently.
2. US2 → the grid provably returns, unchanged, when the editor is re-entered.
3. Polish → full build/test and the manual toggle walkthrough.

---

## Notes

- `[P]` tasks touch different files and have no unfinished dependencies.
- T005 is the only production-code change; the rest is a test file, a project-file entry and comments.
- `fsharp-game.fsproj` is intentionally unchanged (no new module, no new shader).
- The whole suite runs headlessly; no window is opened by any task.
- **Constraint coverage**: FR-005 (view rule only — no zone/scenario/command/persistence change) and FR-006 (markers, colliders, chain links and the gizmo keep their editor-only behaviour) are satisfied **by scope** — no authored-data code and no `Viewport.buildLines`/`GizmoDraw` code is touched — so they carry no task by design.
