# Tasks: Advent Cue Ops

**Input**: Design documents from `/specs/056-advent-cue-ops/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md,
contracts/advent-ops.md, quickstart.md

**Tests**: Not TDD. The flag set is plain data on `SharedState`, so every scenario is exercised **headlessly** and the
acceptance tests are part of each story (spec FR-009, FR-012). No device, window or GPU is opened.

**Organization**: Tasks are grouped by user story. The vocabulary change (Phase 1) blocks every story. **US1** (P1) is
the write half; **US2** (P1) is the await; **US3** (P2) is the branch; **US4** (P3) is the panel. The build stays
green throughout (adding cases to a DU is additive; the exhaustive matches are updated in the same tasks that add
them — but the panel's `match` in `CueTree` and the tests' `describe` go red until T008/T009, so the app builds fully
only after Phase 6).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)

## Path Conventions

Single-project F# repository. Paths are relative to the repository root (`fsharp-game/`): `core/`, `render/`, `world/`,
`state/`, `editor/`, `screen/`, `test/`, `specs/`, `docs/`.

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 In `world/Cue.fs`, add `AddAdvent of advent: Advent`, `RemoveAdvent of advent: Advent`,
  `Await of advent: Advent`, `AwaitState of advent: Advent` and `If of advent: Advent * thenCue: Cue * elseCue: Cue`;
  add `open Advent` and widen the module header's dependency note (one more pure module). (data-model.md; FR-001,
  FR-002, FR-006.)

**Checkpoint**: the type compiles; every downstream `match` on `Cue` is now an error to fix in the tasks below.

---

## Phase 2: User Story 1 - A cue sets or clears a milestone (Priority: P1) 🎯 MVP

**Goal**: `AddAdvent`/`RemoveAdvent` write `SharedState.Advents` where reached, at no cost in time.

**Independent Test**: run `AddAdvent MetAiko`, assert the set contains it and the cue is `Fin` in one step
(quickstart, "set").

### Implementation for User Story 1

- [X] T002 [US1] In `world/CueEngine.fs`, add `open Advent`; handle `AddAdvent advent` and `RemoveAdvent advent` where
  the walk reaches them: `shared.Advents <- Set.add/remove advent shared.Advents`, then `ValueSome Fin`. (data-model.md;
  research D1/D2; FR-003, FR-004.)
- [X] T003 [US1] In `test/CueTests.fs`, add the write scenarios: `AddAdvent m` sets the flag and costs no time;
  `RemoveAdvent m` clears it; adding a set flag and removing a clear one are no-ops (no raise). Add the new cases to
  `describe`. (FR-003, FR-004, FR-009; SC-001, SC-003.)

**Checkpoint**: the write half works headlessly; the panel and the other ops' matches are still red.

---

## Phase 3: User Story 2 - A cue waits for a milestone (Priority: P1)

**Goal**: `Await` blocks until the flag is set, then continues; it costs nothing while waiting.

**Independent Test**: `Await m` with `m` clear stays put; set `m` and it finishes (quickstart, "wait").

### Implementation for User Story 2

- [X] T004 [US2] In `world/CueEngine.fs`, handle `Await advent` and `AwaitState advent`: `Await` reports `ValueSome Fin`
  when `Set.contains advent shared.Advents` else `ValueSome (AwaitState advent)`; `AwaitState` reports `ValueSome Fin`
  when set else `ValueNone`. (data-model.md; research D3; FR-005, FR-006, FR-008.)
- [X] T005 [US2] In `test/CueTests.fs`, add the await scenarios: waiting holds the op and the queue; setting the flag
  finishes it; an already-set flag costs no time; an `AddAdvent` earlier in the same cue satisfies a later `Await` in
  the same step; clearing the flag while waiting leaves it waiting; 2000 waiting steps allocate 0 bytes. (FR-005,
  FR-008; SC-002, SC-003.)

**Checkpoint**: a cue can be gated on a milestone and resumes when it lands.

---

## Phase 4: User Story 3 - A cue branches on a milestone (Priority: P2)

**Goal**: `If` runs the branch its milestone selects, in its own place.

**Independent Test**: reach `If m` with `m` clear (else runs) and set (then runs) (quickstart, "branch").

### Implementation for User Story 3

- [X] T006 [US3] In `world/CueEngine.fs`, handle `If (advent, thenCue, elseCue)`: a tail `advance` of
  `if Set.contains advent shared.Advents then thenCue else elseCue`. (data-model.md; research D4; FR-007.)
- [X] T007 [US3] In `test/CueTests.fs`, add the branch scenarios: the selected branch runs and the other never does; a
  `Fin` branch costs no time; a timed branch begins in the `If`'s place; an `If` nests inside a `Sequence`. (FR-007;
  SC-004.)

**Checkpoint**: milestones drive choices.

---

## Phase 5: User Story 4 - The panel authors the four ops (Priority: P3)

**Goal**: the panel offers the four kinds and a closed-list milestone chooser; a running await is read-only.

**Independent Test**: assert the tables and the lock behaviour (quickstart, "panel").

### Implementation for User Story 4

- [X] T008 [US4] In `editor/CueTree.fs`, add `open Advent`; add the four kinds to `kinds`; update `kindName`,
  `defaultOfKind` (each with `List.head Advent.all`; `If` with two `Fin` branches), `label`, `children` (`If` → its two
  branches), `executing` (`AwaitState` in flight), and `drawEditor` (a milestone chooser over `Advent.all`, committed
  through `withAt`). `textOf`/`withText` are unchanged. (data-model.md; research D5; FR-010.) This is what returns the
  app build to green.
- [X] T009 [US4] In `test/CuePanelTests.fs`, update `describe` and the kind/label/children/lock tables for the four
  ops: the kinds are offered; each default; the `If`'s two children; a running await is locked and labelled `Await m`;
  `textOf` is `ValueNone` for all four. (FR-010; SC-006.)
- [X] T012 [US4] In `editor/CueTree.fs`, head each of an `If`'s branches with a `then`/`else` label row so the two are
  told apart: add `Row.Branch`, emit a label row above each branch from `rows` (indenting the branch beneath it), and
  draw a label row as plain text in `drawTree` so it is shown but not selectable. (FR-010; user request.)
  `test/CuePanelTests.fs` asserts the labels, branch markers and depths.

**Checkpoint**: all four stories work and the app compiles.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T010 [P] In `docs/ROADMAP.md`, correct phase 10's deliverable line to name the advent cue ops (with advents,
  `specs/055`); confirm the `world/Cue.fs` and `world/CueEngine.fs` headers name the advent ops. (spec Assumptions.)
- [X] T011 Build `fsharp-game.fsproj` and `test/fsharp-game.Tests.fsproj` with **0 warnings** and run the full suite
  green; walk `quickstart.md` (the set, the await, the branch, the 0-byte waiting step). (FR-012; SC-005.)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 is the root.
- **US1 (Phase 2)**: depends on T001.
- **US2 (Phase 3)**: depends on T001; independent of US1.
- **US3 (Phase 4)**: depends on T001; independent of US1/US2.
- **US4 (Phase 5)**: depends on T001; T008 returns the build to green.
- **Polish (Phase 6)**: depends on all of the above.

### Same-file constraint (sequential)

- `world/Cue.fs`: T001 only.
- `world/CueEngine.fs`: T002 → T004 → T006.
- `test/CueTests.fs`: T003 → T005 → T007.
- `editor/CueTree.fs`: T008 → T012. `test/CuePanelTests.fs`: T009 → T012.

### Parallel Opportunities

- T002, T004, T006 are one file, in order; but the *tests* T003/T005/T007 follow them on one file.
- T008 and T009 are different files once T001 lands; T009 can be written against the T008 tables.

---

## Implementation Strategy

### MVP First (User Story 1 + 2)

1. T001 (vocabulary).
2. US1 (T002–T003) and US2 (T004–T005) together are the story-gating MVP: a cue can set a milestone and wait for one.
3. Then US3 (branch), then US4 (panel), then polish.

---

## Notes

### Implementation status (2026-10-03)

- **T001–T012: done.** Both projects build with **0 warnings** and `dotnet test` passes (448 tests). The four ops, the
  `AwaitState` begun-marker, the closed-list chooser and the `If`'s `then`/`else` branch labels (T012) are in; no
  `Command`/`History` change (FR-011).

- Tests are part of each story; the flag set is plain data, so everything is assertable headlessly.
- The build is red from T001 until T008 (the `CueTree` match and the test `describe` functions); each checkpoint marks
  the state.
- No `Command`/`History` change: a running cue's flag writes are not authored edits.
