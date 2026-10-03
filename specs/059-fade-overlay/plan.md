# Implementation Plan: Screen Fade

**Branch**: `059-fade-overlay` | **Date**: 2026-10-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/059-fade-overlay/spec.md`

## Summary

Give the gameplay screen a **fade level** — a `float32` in 0..1 on `GameplayState`, defaulting to 0 — and
draw a full-viewport black overlay at that opacity over the rendered game view, last in the in-game 2D
layer's batch so it covers both the scene and the dialogue/prompt layer but stays under the editor's ImGui
panels. The level is tuned from a new **Miscellaneous** panel docked in the editor's right column beside the
Inspector/Cue/Advent, by a slider bound to 0..1.

This is the value and the overlay the reference (`odin-game`'s `screen.fade`, drawn as
`rl.Fade(rl.BLACK, fade)`) gives the cue ops `FadeOut`/`FadeIn` to tween. Those ops are a later feature;
this one delivers the field, the overlay and the authoring surface.

The work is:

- `state/GameplayState.fs` — one field, `Fade: float32`, defaulting to 0.
- `screen/GameplayScreen.fs` — a pure `fadeOverlay` rule (no-op at 0, else a full-view black quad at alpha
  `level`) and the one `Ui.rect` commit in `draw`.
- `render/ImGui.fs` — the one missing binding, `sliderFloat`.
- `editor/MiscPanel.fs` (new) — the right-docked panel; writes `Fade` in place (no undo step).
- `editor/Editor.fs` — the panel in the cycle list and the draw order.
- `native/cimgui_sdl3.cpp` — dock `"Miscellaneous"` into the right node; `libcimgui.so` rebuilt.
- `test/FadeTests.fs` (new) — the default and the pure overlay rule, headless.
- `fsharp-game.fsproj`, `test/fsharp-game.Tests.fsproj` — the two new files.

No new package, no new pipeline, no device needed for the tests.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`), warnings-as-errors-clean build

**Primary Dependencies**: SDL3-CS, the custom cimgui binding (`render/ImGui.fs` + native `libcimgui.so`) —
all already present; the overlay reuses the in-game 2D layer (`render/Ui.fs`), which is already compiled
before the gameplay screen

**Storage**: none — the fade level is runtime screen state, never authored and never persisted (persistence
is a later phase)

**Testing**: xUnit via `dotnet test` (headless). The overlay rule is pure (`fadeOverlay`: sizes and a level
in, a struct overlay out), so the no-op, the coverage and the alpha are asserted with no device. The
drawing and the panel are the interactive walkthrough

**Target Platform**: Linux desktop (SDL_gpu/Vulkan). The header and the native ImGui library already build
with `g++`, which is present

**Project Type**: desktop game with an in-engine editor (single project; one `.fsproj` per the app and its
tests, plus the native `libcimgui.so`)

**Performance Goals**: 60 fps. At level 0 no quad is emitted, so the idle path is byte-for-byte the
pre-feature frame; otherwise one more quad joins the existing batch — no new draw call, no per-frame
allocation (Constitution V)

**Constraints**: the constitution's five principles; the fade is a scalar on struct state; the overlay
reuses the 2D layer's pipeline and white texture rather than adding a pass; the editor panels stay above it

**Scale/Scope**: one state field, one pure rule, one panel, one ImGui binding, one native dock line, one
test file. No new module beyond the panel and the tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one field, one pure rule, one small panel and one binding; the overlay reuses the existing 2D layer and its batch, and the panel reuses the existing docking workspace. No new pass, pipeline or asset. |
| II. Fail Loudly | Pass — the level's domain is 0..1 and the slider cannot leave it; a raised level is a real value, not a fallback. At 0 the overlay is skipped by an explicit rule, not a silent default. |
| III. Spec-First | Pass — `specs/059-fade-overlay/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — a `float32` field on the gameplay struct; no object graph, no new entity. |
| V. Minimise GC Pressure | Pass — `fadeOverlay` returns a struct (`voption` of a struct tuple), and the commit is one quad into the reused batch; level 0 adds nothing. |
| N-V. Testing is the Authoring Tool | Not this subsystem (the narrative engine's); adopted in substance — the overlay rule is asserted headlessly. |

## Project Structure

### Documentation (this feature)

```text
specs/059-fade-overlay/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── fade-overlay.md  # Phase 1 output — the surfaces that change
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
state/
└── GameplayState.fs     # CHANGED: one field, `mutable Fade: float32`, defaulting to 0.
screen/
└── GameplayScreen.fs    # CHANGED: the pure `fadeOverlay` rule and the one `Ui.rect` commit in `draw`.
render/
└── ImGui.fs             # CHANGED: add the `sliderFloat` binding (igSliderFloat).
editor/
├── MiscPanel.fs         # NEW: the right-docked Miscellaneous panel; writes `Fade` in place.
└── Editor.fs            # CHANGED: "Miscellaneous" in `panelNames` and `MiscPanel.draw` in `draw`.
native/
├── cimgui_sdl3.cpp      # CHANGED: dock "Miscellaneous" into the right node of the default layout.
└── libcimgui.so         # REBUILT by `native/build.sh`.
test/
├── FadeTests.fs         # NEW: the default and the pure overlay rule, headless.
fsharp-game.fsproj       # CHANGED: compile `editor/MiscPanel.fs` (after AdventPanel, before Editor).
test/fsharp-game.Tests.fsproj # CHANGED: compile `FadeTests.fs`.
```

**Structure Decision**: the value lives on `GameplayState`, where the reference has it and where
`Editing`/`Follow`/`PlayTime` already sit. The overlay is not a new rendering path: the in-game 2D layer is
already a blended, depth-less, last-drawn batch, so the fade is one more quad in it — and being the last
thing in that batch is what puts it under the editor's ImGui panels, which render after the scene pass. The
panel is a new `editor/` module because a panel is not a selection and so cannot live in the Inspector, the
same reason `AdventPanel` and `DialoguePanel` are modules of their own; it docks through the same native
layout the other right-column panels use.

## Phase 0: Research

See [research.md](./research.md). It settles a live, screen-scoped, non-undoable scalar (D1), the overlay as
a quad in the existing 2D layer rather than a new pass (D2), a pure level→overlay rule so the no-op is
assertable without a device (D3), a new right-docked panel written in place (D4), the slider binding (D5),
and the deliberate divergence that the editor panels stay above the overlay (D6).

## Phase 1: Design

- [data-model.md](./data-model.md) — the field, the pure overlay rule, the panel surfaces, and validation.
- [contracts/fade-overlay.md](./contracts/fade-overlay.md) — every surface that changes, and what does not.
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite and the interactive walkthrough.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one field, one rule, one panel, one binding; existing batch and dock layout reused. |
| II. Fail Loudly | Pass — domain held by the control; the 0 case is an explicit rule, not a silent default. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — a scalar on struct state. |
| V. Minimise GC Pressure | Pass — struct overlay, one reused-batch quad, nothing at 0. |
| N-V. Testing is the Authoring Tool | Adopted in substance — the overlay rule is asserted headlessly. |
