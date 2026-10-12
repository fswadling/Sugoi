---

description: "Task list for implementing Error Visibility"
---

# Tasks: Error Visibility

**Input**: Design documents from `/specs/081-error-visibility/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included. The spec's `SC-001`/`SC-002` require an automated record of the tally's behaviour, so US1
and US2 each have a test task written first and expected to fail before the implementation; T013 adds the
`SC-003` (no per-frame cost) assertion.

**Organization**: Tasks are grouped by user story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)
- Every task names its exact file path

## Path Conventions

Single project at the repo root; F# files in domain clusters (`core/`, `render/`, `screen/`, `editor/`,
`world/`, `state/`) and one flat `test/<Module>Tests.fs` per module. Paths are repo-relative.

---

## Phase 1: Setup

**Purpose**: Establish a clean baseline so a regression is attributable.

- [x] T001 Confirm the baseline is green: `dotnet build fsharp-game.fsproj` (0 warnings) and `dotnet test test/fsharp-game.Tests.fsproj` (all pass) before any change.

---

## Phase 2: Foundational

**Purpose**: The pieces both user stories read.

**⚠️ CRITICAL**: US1 and US2 both read the tally, so this phase blocks them.

- [x] T002 [P] Add `core/Diagnostics.fs` — the process-global tally (`report`/`reportf`, `errorCount`, `lastMessage`, `recent`, `isLoud`, `clear`, `loudThreshold`, the `Report` type) per `plan.md`/`data-model.md`, and list it in `fsharp-game.fsproj` immediately after `core/SlotMap.fs`.
- [x] T003 [P] Add two bindings to `render/ImGui.fs`: `pushStyleColorU32(int idx, uint32 col)` (`igPushStyleColor_U32`) and `popStyleColor(int count)` (`igPopStyleColor`).

**Checkpoint**: `core/Diagnostics.fs` compiles and is visible to every higher cluster; the two colour bindings resolve.

---

## Phase 3: User Story 1 - Seeing that the application is producing errors (Priority: P1) 🎯 MVP

**Goal**: Reports are counted and a non-zero total is visible in the app, de-duplicated, and prominent past a threshold.

**Independent Test**: Cause a report; assert the total is visible in the app's own interface with no terminal. Repeat it many times; assert the count rises while the detail stays one entry.

### Tests for User Story 1

- [x] T004 [US1] Add `test/DiagnosticsTests.fs` (registered in `test/fsharp-game.Tests.fsproj`) asserting: a fresh tally reports nothing; a report raises `errorCount` and becomes `lastMessage`; `n` repeats of the same text give **one** entry with `Count = n`; `reportf` formats as `eprintfn` would; `isLoud()` is false below `loudThreshold` and true at it (`FR-001/002/005/006`, `SC-002`).

### Implementation for User Story 1

- [x] T005 [US1] Route every report site through the tally — replace `eprintfn` with `Diagnostics.reportf` in `world/CueEngine.fs`, `world/Assets.fs`, `world/ActorRender.fs`, `world/ScenarioFork.fs`, `state/History.fs`, `editor/Spawning.fs`, `render/Ui.fs`, `Engine.fs`, `Program.fs` (console output unchanged, `FR-006`).
- [x] T006 [US1] In `screen/DebugOverlay.fs`, show the total and the latest message in the Debug window body (red via `pushStyleColorU32`/`popStyleColor`) when non-zero, nothing when zero; focus the Debug window once when `isLoud()` (`FR-003`, `FR-005`, `SC-005`).
- [x] T007 [US1] In `editor/Editor.fs`, add an always-visible menu-bar item `(!) N error(s)` when the total is non-zero, so the indicator shows even when the Debug window is collapsed or closed (`FR-003`).

**Checkpoint**: A handled failure is visibly counted in the app, and a clean run shows nothing.

---

## Phase 4: User Story 2 - Inspecting what was reported (Priority: P2)

**Goal**: The distinct reports are readable, each with its repeat count, and reachable from the indicator.

**Independent Test**: With several distinct reports made, the detail lists each one with its count, most recent first, and the indicator brings the detail forward.

### Tests for User Story 2

- [x] T008 [P] [US2] Extend `test/DiagnosticsTests.fs` asserting distinct reports appear most-recent-first in `recent()` and each carries its own count (`FR-004`).

### Implementation for User Story 2

- [x] T009 [P] [US2] In `screen/DebugOverlay.fs`, list the de-duplicated `recent()` entries (`xN  <message>`) beneath the summary line (`FR-004`).
- [x] T010 [P] [US2] In `editor/Editor.fs`, make the menu-bar indicator focus the Debug window on click (`setWindowFocus "Debug"`), so the detail is brought forward (`FR-004`, US2/AC2).

**Checkpoint**: The detail is inspectable and reachable from both the panel and the menu bar.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [x] T011 Run `dotnet build fsharp-game.fsproj` (must be 0 warnings) and `dotnet test test/fsharp-game.Tests.fsproj` (must be green, including the existing suite — the ~20 rewired report sites behave identically on the console).
- [ ] T012 Run the display scenarios in `specs/081-error-visibility/quickstart.md` (clean run shows nothing; a report is counted; collapse the Debug window and pass the threshold; console line unchanged) against a display.
- [x] T013 Assert the zero-report read path adds no per-frame cost: extend `test/DiagnosticsTests.fs` with a `GC.GetAllocatedBytesForCurrentThread` check over a loop of `errorCount()`/`isLoud()`/`lastMessage()` on a cleared tally, asserting zero bytes allocated (`SC-003`, `FR-007`).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 first.
- **Foundational (Phase 2)**: T002 and T003 block the stories. They touch different files, so they can run together.
- **User Stories (Phase 3–4)**: both depend on Phase 2. US1 is the MVP; US2 builds on the same display surface (T009 extends what T006 wrote), so US2 follows US1 in practice even though its tests are independent.
- **Polish (Phase 5)**: after the stories.

### Within Each User Story

- Write the test(s) first and confirm they fail against the current code, then implement.

### Parallel Opportunities

- T002 and T003 are different files (parallel).
- T004 (a new file) is independent of T005–T007.
- Within US2, T008 (`test/DiagnosticsTests.fs`) and T010 (`editor/Editor.fs`) are different files; T009 touches `screen/DebugOverlay.fs`, which T006 also edits, so T009 follows T006.

---

## Parallel Example: the Foundational phase

```bash
# Two different files, no dependency between them:
Task: "T002 add core/Diagnostics.fs + register in fsharp-game.fsproj"
Task: "T003 add pushStyleColorU32/popStyleColor bindings to render/ImGui.fs"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. T001 baseline.
2. Phase 2 (T002, T003).
3. US1 (T004–T007).
4. **STOP and VALIDATE**: a report is visibly counted in the app; a clean run shows nothing.

### Incremental Delivery

1. Foundational → the tally exists and compiles.
2. US1 → the count and indicator are visible → validate (MVP).
3. US2 → the detail is inspectable and reachable → validate.
4. Finish with T011–T012.

---

## Notes

- `[P]` = different files, no dependencies. `[US*]` maps a task to a spec user story.
- Confirm each new test fails before implementing.
- Do not change the console output text (FR-006); the reward is a rename, not a rewrite.
- Keep the Debug window's name constant (`"Debug"`) — the count goes in the body, never the title
  (`research.md`, "do not rename the Debug window").

### Completion note (2026-10-12)

Implemented T001–T011 and T013. `dotnet build fsharp-game.fsproj` is **0 warnings, 0 errors**;
`dotnet test test/fsharp-game.Tests.fsproj` is **565 passed, 0 failed** (+7 `DiagnosticsTests`).

- T002 — `core/Diagnostics.fs` (the process-global tally), listed in `fsharp-game.fsproj` after `core/SlotMap.fs`.
- T003 — `pushStyleColorU32`/`popStyleColor` added to `render/ImGui.fs` (both already in `native/libcimgui.so`).
- T005 — 20 report sites moved from `eprintfn` to `Diagnostics.reportf` (console output unchanged); the only
  remaining `eprintfn` call is inside `Diagnostics`.
- T006/T009 — the Debug window body shows the count, last message and de-duplicated detail; focuses once when loud.
- T007/T010 — the menu bar shows `(!) N error(s)` when non-zero and focuses the Debug window on click.
- T013 — the zero-report read path is asserted allocation-free.

T012 remains **unchecked**: it needs a display and is run per `quickstart.md`.
