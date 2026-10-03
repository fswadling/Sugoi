# Tasks: Fade Cue Ops

**Input**: Design documents from `/specs/060-fade-cue-ops/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md,
contracts/fade-cue-ops.md, quickstart.md

**Tests**: Not TDD. The fade is a `float32` on `GameplayState`, so every scenario is exercised **headlessly**
and the acceptance tests are part of each story (spec FR-006). No device, window or GPU is opened.

**Organization**: Tasks are grouped by user story. **US1** (P1) is the ops and the tween; **US2** (P2) is the
panel. Adding the cases (Phase 1) breaks four exhaustive matches, fixed in Phase 2; the build is red from
T001 until T004 lands.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US2)

## Path Conventions

Single-project F# repository. Paths are relative to the repository root (`fsharp-game/`): `world/`, `state/`,
`screen/`, `editor/`, `test/`, `specs/`, `docs/`.

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 In `world/Cue.fs`, add `FadeOut of duration: float`, `FadeIn of duration: float`,
  `FadeOutState of initial: float * duration: float * endsAt: float` and
  `FadeInState of initial: float * duration: float * endsAt: float`; widen the header note that names the
  timed leaves (`WaitState`, `FlyCameraState`) to include the two fade states. (data-model.md; FR-001.)

**Checkpoint**: the type carries the ops; every exhaustive `match` on `Cue` is now an error to fix.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: fix the four exhaustive matches so the app and tests build again. US1's behaviour lands here
too, because `advance` cannot be made exhaustive without writing the tween.

- [X] T002 In `world/CueEngine.fs`, add the private `stepFade` helper (deadline branch sets the endpoint
  exactly and answers `true`; otherwise the linear tween and `false`) and the four `advance` arms for
  `FadeOut`/`FadeOutState` (target `1.0f`) and `FadeIn`/`FadeInState` (target `0.0f`), capturing
  `state.Fade` at begin. (data-model.md, contracts; FR-002, FR-003, FR-004, FR-005, FR-006.)
- [X] T003 In `editor/CueTree.fs`, handle the four cases in `executing`, `label`, `children`, `kindName`,
  `kinds`, `defaultOfKind` and `drawEditor` (the duration via `dragNumber`, committing `FadeOut`/`FadeIn`),
  as `Wait` is handled. (data-model.md; FR-007, FR-008.)
- [X] T004 In `test/CueTests.fs` and `test/CuePanelTests.fs`, extend each `describe` helper with the four
  cases (`FadeOut d | FadeOutState (d, _, _)` and the in one) so both compile.

**Checkpoint**: `dotnet build` and `dotnet test` are green; the fades exist but are unasserted.

---

## Phase 3: User Story 1 - A fade op drives the screen level over its duration (Priority: P1) 🎯 MVP

**Goal**: a fade out darkens to exactly 1.0 and a fade in clears to exactly 0.0 over its duration, from
wherever the level stands.

**Independent Test**: step a cue containing a fade at a few times and assert `state.Fade` at each, including
the exact endpoint and the immediate case (quickstart, "the fade timeline").

### Tests for User Story 1

- [X] T005 [US1] In `test/CueTests.fs`, add: a `FadeOut 2.0` from 0 gives 0/0.5/1.0 at 0/1/2 and then `Fin`;
  a `FadeIn 2.0` from 1 gives 1/0.5/0.0; a part-way start (0.4) fades out to 1.0 and in to 0.0; a zero and a
  negative duration set the endpoint and are `Fin` in one step; the endpoint is exactly `1.0f`/`0.0f`; and an
  in-flight fade allocates 0 bytes per step. (FR-002, FR-003, FR-004, FR-005, FR-006; SC-001, SC-002, SC-003.)

**Checkpoint**: the whole tween is asserted with no device.

---

## Phase 4: User Story 2 - The fade ops are authored in the cue panel (Priority: P2)

**Goal**: the panel lists both kinds, shows a fade in flight as the op it came from, retunes the duration as
one undo step, and locks a running fade.

**Independent Test**: assert the labels (authored and in-flight), the in-flight lock, and a duration edit as
one `SetCue` step that undo restores (quickstart, "the panel authors it").

### Tests for User Story 2

- [X] T006 [US2] In `test/CuePanelTests.fs`, add to the "every kind" test the labels `Fade out 2` / `Fade in
  2` and their in-flight forms; add an in-flight fade to the read-only cases (locked, reads as its op); and
  assert a duration edit is one `SetCue` step undone and redone. (FR-007, FR-008; SC-004.)

**Checkpoint**: the panel half is asserted; the drawing is the walkthrough.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [X] T007 Build `fsharp-game.fsproj` and `test/fsharp-game.Tests.fsproj` with **0 warnings** and run the
  full suite green; walk `quickstart.md` (the timeline, the part-way start, the endpoint, the immediate
  case, the panel rows). (SC-005; FR-002–FR-008.)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 is the root.
- **Foundational (Phase 2)**: T002, T003 and T004 all depend on T001; T002 (CueEngine) is where US1's
  behaviour lands and blocks T005.
- **US1 (Phase 3)**: T005 depends on T002 (and T004 for the test file to compile).
- **US2 (Phase 4)**: T006 depends on T003.
- **Polish (Phase 5)**: depends on all of the above.

### Same-file constraint (sequential)

- `world/Cue.fs`: T001 only.
- `world/CueEngine.fs`: T002 only.
- `editor/CueTree.fs`: T003 only.
- `test/CueTests.fs`: T004 → T005.
- `test/CuePanelTests.fs`: T004 → T006.

### Parallel Opportunities

- T003 (CueTree) and T002 (CueEngine) touch different files and can run together once T001 lands; T004
  touches only the tests and can also run alongside them.
- T005 and T006 are one file each, so they can run together once T004 lands.

---

## Implementation Strategy

### MVP First (User Story 1)

1. T001 (the cases), then Phase 2 (T002–T004) to write the tween and return the build to green.
2. US1 (T005) is the MVP: a cue fades the view to black and back, asserted headlessly.
3. Then US2 (the panel), then polish.

---

## Notes

- No fsproj change: the ops live in `world/Cue.fs`/`world/CueEngine.fs`, the panel surface in
  `editor/CueTree.fs`, and the tests in the existing files.
- No `Command`/`History` change: a fade edit is still one `SetCue` step (spec 049).
- The overlay and the level are spec 059's; this feature only moves the level.
- `stepFade` is shared by the two ops so the out and in tweens cannot drift (research D4).

### Implementation status (2026-10-03)

- **T001–T007: done.** Both projects build with **0 warnings** and `dotnet test` passes (470 tests, up from
  464 at `specs/059`; the six new cases are the four fade scenarios in `CueTests` and the lock/retune pair in
  `CuePanelTests`). The two ops, their run states and the shared `stepFade` are in place; the panel offers
  both kinds and locks a running fade; no `Command`/`History`/fsproj change (FR-007).
- One type correction during implementation: the run states' `initial` is `float32` (the level's type), not
  `float`; the data-model and contract snippets were updated to match.
- The **interactive walkthrough is manual** (the quickstart's scratch tick runs headlessly and is covered by
  the suite; the editor rows need a window).
