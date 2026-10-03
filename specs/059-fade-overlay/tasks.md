# Tasks: Screen Fade

**Input**: Design documents from `/specs/059-fade-overlay/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md,
contracts/fade-overlay.md, quickstart.md

**Tests**: Not TDD. The overlay rule is pure, so the two edges that matter — no-op at 0 and full coverage at
1 — are asserted **headlessly** as part of US1. The drawing and the panel are the interactive walkthrough; no
device, window or GPU is opened by the suite.

**Organization**: Tasks are grouped by user story. **US1** (P1) is the value and the overlay; **US2** (P2) is
the Miscellaneous panel. T001 is the root both depend on.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US2)

## Path Conventions

Single-project F# repository. Paths are relative to the repository root (`fsharp-game/`): `core/`, `render/`,
`world/`, `state/`, `editor/`, `screen/`, `native/`, `test/`, `specs/`, `docs/`.

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 In `state/GameplayState.fs`, add `mutable Fade : float32` to `GameplayState` (a comment stating
  0 = untouched, 1 = fully black, screen-scoped and runtime-only) and initialise it to `0.0f` in `create`.
  (data-model.md; FR-001.)

**Checkpoint**: the struct carries the level at its default; nothing draws it yet.

---

## Phase 2: User Story 1 - The gameplay view fades to black (Priority: P1) 🎯 MVP

**Goal**: the screen renders a black overlay over the game view at opacity `Fade`, a no-op at 0.

**Independent Test**: call `GameplayScreen.fadeOverlay` at 0 and at 1 and assert `ValueNone` and a fully
opaque full-view black quad (quickstart, "the overlay rule").

### Implementation for User Story 1

- [X] T002 [US1] In `screen/GameplayScreen.fs`, add the pure `fadeOverlay` function
  (`width height level -> struct (Ui.Rect * Vector4) voption`): `ValueNone` at `level <= 0`, else a
  full-view black quad at alpha `level`. In `draw`, after `PromptRender.draw`, match it and emit
  `Ui.rect &state.Ui overlay colour`. (data-model.md, contracts; FR-002, FR-003, FR-008.)
- [X] T003 [US1] In `test/FadeTests.fs` (new file) add: the `GameplayState.create` default is `0.0f`; and the
  overlay rule — `fadeOverlay w h 0.0f` is `ValueNone`; for a spread of sizes and levels in `(0, 1]` the
  overlay spans `(0, 0, w, h)`, is black, and its alpha equals the level. Add the `<Compile>` entry for it to
  `test/fsharp-game.Tests.fsproj` (beside `PromptTests.fs`). (FR-003; SC-003.)

**Checkpoint**: `dotnet test` is green with the default and the overlay rule asserted; the app builds.

---

## Phase 3: User Story 2 - The fade is tuned from a Miscellaneous tab (Priority: P2)

**Goal**: the editor has a right-docked Miscellaneous panel whose Fade slider writes the level in place.

**Independent Test**: the panel is in the dock cycle; dragging the slider changes `Fade` and the view
darkens next frame (quickstart, interactive walkthrough).

### Implementation for User Story 2

- [X] T004 [P] [US2] In `render/ImGui.fs`, add the `sliderFloat` binding over cimgui's `igSliderFloat`
  (entry point `igSliderFloat`): `string label, float32& v, float32 min, float32 max, string format, int
  flags -> bool`. (data-model.md, contracts; FR-006.)
- [X] T005 [US2] In `editor/MiscPanel.fs` (new file), add `draw (state: byref<GameplayState.GameplayState>)`:
  `ImGui.beginWindow "Miscellaneous"`, a `sliderFloat "Fade"` over `[0, 1]` (format `"%.2f"`) bound by
  reference to `state.Fade`, then `endWindow`. No `SharedState`, no `Command`, no `History` step — the write
  is in place. (data-model.md, contracts; FR-005, FR-006.) Depends on T001, T004.
- [X] T006 [US2] In `fsharp-game.fsproj`, add `<Compile Include="editor/MiscPanel.fs" />` after
  `editor/AdventPanel.fs` and before `editor/Editor.fs`. (plan.md.)
- [X] T007 [US2] In `editor/Editor.fs`, add `"Miscellaneous"` to `panelNames` (after `"Advent"`) and submit
  `MiscPanel.draw &state` after `AdventPanel.draw` in `draw`, with a short comment naming the panel as the
  right-column home for game-wide runtime tuning. (data-model.md; FR-005, FR-007.)
- [X] T008 [P] [US2] In `native/cimgui_sdl3.cpp`, add `ImGui::DockBuilderDockWindow("Miscellaneous", right);`
  after `"Advent"` and widen the layout comment to mention it; rebuild `native/libcimgui.so` with
  `native/build.sh`. (data-model.md, contracts; FR-005.)

**Checkpoint**: the Miscellaneous tab appears on the right, its slider drives the overlay, and the panels
stay readable at every level.

---

## Phase 4: Polish & Cross-Cutting Concerns

- [X] T009 [P] Update `docs/EDITOR_DESIGN.md`: add a **Miscellaneous** row to the Workspace panel table and
  a short note in the right-column prose (and the Module map) describing it as the game-wide runtime tuning
  surface, with the fade level as its first control. (AGENTS.md: the docs are the source of truth.)
- [X] T010 Build `fsharp-game.fsproj` and `test/fsharp-game.Tests.fsproj` with **0 warnings** and run the
  full suite green; walk `quickstart.md` (the default, the overlay rule, the panel's dock/slider, the
  panel-above check, the zone-switch persistence, the main-menu check). (SC-004, SC-005; FR-004.)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 is the root.
- **US1 (Phase 2)**: T002 and T003 depend on T001.
- **US2 (Phase 3)**: T005 depends on T001 and T004; T006 and T007 depend on T005; T008 is independent of the
  F# code (native only).
- **Polish (Phase 4)**: T009 and T010 depend on all of the above.

### Same-file constraint (sequential)

- `state/GameplayState.fs`: T001 only.
- `screen/GameplayScreen.fs`: T002 only.
- `test/FadeTests.fs` + `test/fsharp-game.Tests.fsproj`: T003 only.
- `render/ImGui.fs`: T004 only.
- `editor/MiscPanel.fs`: T005 only.
- `fsharp-game.fsproj`: T006 only.
- `editor/Editor.fs`: T007 only.
- `native/cimgui_sdl3.cpp`: T008 only.
- `docs/EDITOR_DESIGN.md`: T009 only.

### Parallel Opportunities

- T002 and T003 touch different files and can run together once T001 lands.
- T004 (ImGui binding) and T008 (native dock line + rebuild) touch different files and can run together.
- T009 (docs) can run any time after T007 names the panel.

---

## Implementation Strategy

### MVP First (User Story 1)

1. T001 (the field), then T002 (the overlay) and T003 (the headless assertions).
2. US1 is the MVP: the level darkens the gameplay view, a no-op at 0.
3. Then US2 (the panel and its dock), then polish.

---

## Notes

- The overlay reuses the in-game 2D layer's batch — no new pipeline, shader or texture.
- T004 needs no native rebuild: `igSliderFloat` is already exported by `libcimgui.so`.
- T008 is the only native change; it rebuilds the checked-in `libcimgui.so`, so run it before testing the
  dock by hand.
- Nothing is added to `Command`/`History`: the fade is runtime tuning, not an authored edit.
- The cue ops `FadeOut`/`FadeIn` are a later feature; this one adds the field they will drive.

### Implementation status (2026-10-03)

- **T001–T010: done.** Both projects build with **0 warnings** and `dotnet test` passes (464 tests, up from
  452 at `specs/057`). The level is on `GameplayState`; the overlay is a pure rule plus one quad, last in
  the 2D-layer batch; the Miscellaneous panel docks on the right and writes the level in place;
  `native/libcimgui.so` was rebuilt. No `Command`/`History` change.
- **Two extra foundational fixes** were needed and are folded into T001's ripple: `test/SelectionTests.fs`
  and `test/SpawningTests.fs` hand-build a `GameplayState` record literal, so each gained `Fade = 0.0f`.
- **The interactive walkthrough is manual.** The headless assertions (T003) and the build/test pass were
  run here; the dock's appearance, the slider drag and the panel-above/zone-switch/main-menu checks need a
  window, so they remain for the author (quickstart.md).
