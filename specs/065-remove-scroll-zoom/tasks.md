---

description: "Task list for Remove Editor Scroll Zoom"
---

# Tasks: Remove Editor Scroll Zoom

**Input**: Design documents from `/specs/065-remove-scroll-zoom/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Included — the project's convention (and SC-005) require the suite green. This is a removal, so most
of the "test" work is dropping the `dolly` tests and keeping the rest compiling; there is no new headless
assertion (there is no wheel value left to drive).

**Organization**: Tasks are grouped by user story (US1 P1, US2 P2).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 (foundational and polish carry none)

## Path Conventions

- Single F# project at the repository root; sources in `core/`, `editor/`, `render/`, `Engine.fs`, tests in
  `test/`.

---

## Phase 1: Foundational (Blocking Prerequisites)

**Purpose**: The atomic removal. Removing `Wheel` from `Input` forces every construction site (the engine and
two test files) to change, and removing `Camera.dolly` forces its caller and its tests to change, so the
project only builds once these land together.

**⚠️ CRITICAL**: No user story can be validated until this phase is complete.

- [X] T001 [P] `core/Input.fs`: remove `Wheel : float32` from the `Input` record; remove `gather`'s
  `(wheel: float32)` parameter and the `Wheel = wheel` assignment; update `gather`'s doc comment (no "plus the
  accumulated wheel").
- [X] T002 [P] `render/Camera.fs`: remove `dolly` (and its comment); `orbit` and `pan` stay.
- [X] T003 `Engine.fs`: remove the `let mutable wheel = 0.0f` accumulator and the `MouseWheel` branch that fed
  it (every event is still forwarded to `ImGuiSdl3.processEvent`); call `Input.gather prevInput`; update the
  poll comment. (depends on T001)
- [X] T004 `editor/Editor.fs`: remove the `if input.Wheel <> 0.0f then Camera.dolly …` block from
  `updateCamera`; update its comment (orbit and fly only; no wheel zoom). (depends on T002)
- [X] T005 [P] `test/InputTests.fs` and `test/PlayerTests.fs`: drop `Wheel = 0.0f` from the `Input` literals
  (three and one respectively). (depends on T001)
- [X] T006 [P] `test/CameraTests.fs`: remove the two `dolly` tests (`dolly scales the distance`, `dolly clamps
  to a minimum distance`); keep the `orbit` and `pan` tests. (depends on T002)
- [X] T007 Build checkpoint: `dotnet build fsharp-game.fsproj` and `dotnet build test/fsharp-game.Tests.fsproj`
  compile warning-free, proving nothing still references `Input.Wheel` or `Camera.dolly`.

**Checkpoint**: the wheel path and the camera dolly are gone; the user stories can be validated.

---

## Phase 2: User Story 1 - Scrolling a panel scrolls the panel only (Priority: P1) 🎯 MVP

**Goal**: no wheel turn moves the camera, while panels still scroll.

**Independent Test**: a search shows no wheel value is read and no `dolly` is called anywhere; and by hand,
scrolling a panel scrolls it without moving the view, and scrolling the viewport does nothing.

- [X] T008 [US1] Verify the collision path is gone: search `core/`, `editor/`, and `Engine.fs` for `Wheel` and
  `dolly` — neither appears in code (only in `specs/` and `docs/`). `updateCamera` applies orbit and fly only,
  so no input path moves the camera.
- [ ] T009 [US1] Run `quickstart.md`'s manual steps 1–2: scroll over a panel (it scrolls; the camera does not
  move), and over the viewport (nothing happens).

**Checkpoint**: US1 works and is validated.

---

## Phase 3: User Story 2 - Orbit and fly are unaffected (Priority: P2)

**Goal**: navigation is unchanged, and no dead path remains.

**Independent Test**: the `orbit`/`pan` tests pass and `updateCamera`'s orbit/fly arms are untouched; the build
proves the removed pieces have no remaining reference.

- [X] T010 [US2] Confirm `updateCamera`'s orbit and fly arms are unchanged (right-drag orbits; right-drag +
  `WASD`/`Q`/`E` flies) and that `test/CameraTests.fs`'s `orbit` and `pan` tests still pass — the only removed
  tests are the two `dolly` ones.

**Checkpoint**: all stories function.

---

## Phase 4: Polish & Cross-Cutting Concerns

- [X] T011 [P] Docs: `docs/EDITOR_DESIGN.md` — the interaction model drops "scroll zooms" and notes the camera
  is fly-only.
- [X] T012 `dotnet build` warning-free and `dotnet test` green (SC-005); run the automated section of
  `quickstart.md`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Foundational (Phase 1)**: no dependencies; **blocks** the user stories.
- **US1–US2 (Phases 2–3)**: depend on Foundational.
- **Polish (Phase 4)**: depends on the stories.

### Within and between stories

- T001 → T003/T005 and T002 → T004/T006 are the two removal chains (the input field, the camera op).
- T008/T009/T010 are verification; they need Phase 1 done first.

### Parallel Opportunities

- T001, T002 (distinct files) and then T003, T004, T005, T006 (distinct files) can proceed together.
- T011 (docs) is independent of the code.

---

## Implementation Strategy

### MVP First (User Story 1)

1. Foundational (the removal; it builds warning-free).
2. US1: the collision is gone (search + by-hand scroll).
3. US2: orbit/fly unchanged.

### Incremental notes

- The Foundational phase is one landing: the field and the op cannot half-remove, or the build breaks.
- This is a deletion; there is deliberately no new behaviour and no new test beyond keeping the rest green.

## Notes

- `[P]` = different files, no dependencies.
- The wheel is removed because the camera was its only reader; ImGui's own panel scrolling is unaffected.
- `Camera.dolly` is removed rather than left unused (research D3); a future zoom would be a new control.
- No `.fsproj` change: no module is added or moved.
