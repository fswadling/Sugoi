# Implementation Plan: Remove Editor Scroll Zoom

**Branch**: `065-remove-scroll-zoom` | **Date**: 2026-10-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/065-remove-scroll-zoom/spec.md`

## Summary

Remove the editor camera's scroll zoom — `0.9^wheel`, applied in `Editor.updateCamera` — which fires on every
wheel turn, including while scrolling an editor panel, and the wheel input plumbing that existed only to feed
it: the engine's per-frame wheel accumulation, `Input.Wheel` (and `gather`'s wheel parameter). Also remove
`Camera.dolly`, dead once the call is gone, with its two unit tests. Panel scrolling is ImGui's own (the events
the engine forwards via `processEvent`) and is untouched; orbit and fly are unchanged. This is a removal: no new
control replaces the zoom.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: `core/Input.fs` (the per-frame `Input` and `gather`), `Engine.fs` (event poll →
`gather`), `editor/Editor.fs` (`updateCamera`), `render/Camera.fs` (`orbit`/`dolly`/`pan`), `state/Screen.fs`
(`Frame`) — all present

**Storage**: N/A

**Testing**: xUnit, headless (`dotnet test`); no device

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: strictly less per-frame work (the wheel is no longer accumulated); adds nothing

**Constraints**: the build is warning-free; the suite is green; no device opened

**Scale/Scope**: 4 source files (`core/Input.fs`, `Engine.fs`, `editor/Editor.fs`, `render/Camera.fs`), 3 test
files (`test/InputTests.fs`, `test/PlayerTests.fs`, `test/CameraTests.fs`), 1 doc (`docs/EDITOR_DESIGN.md`); no
`.fsproj` change; no new module, no contracts (no external interface)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. The change removes a control and the dead path it fed; FR-005 removes `Camera.dolly`
  rather than leaving an unused primitive, so no dead code remains.
- **II. Fail Loudly** — PASS. No error paths are involved; nothing new can fail.
- **III. Spec-First** — PASS. This feature is `specs/065-remove-scroll-zoom`.
- **IV. Data-Oriented Architecture** — PASS. No data structure changes; the `Input` struct simply loses an
  unused field.
- **V. Minimise GC Pressure** — PASS. Strictly less per-frame work (no wheel accumulation, no `Pow`).

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/065-remove-scroll-zoom/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── tasks.md            # /speckit-tasks
```

(No `contracts/`: the feature exposes no interface to any other system.)

### Source Code (repository root)

```text
core/Input.fs          # drop `Wheel` from `Input`; drop `gather`'s wheel parameter and assignment
Engine.fs              # stop accumulating MouseWheel events; `Input.gather prevInput`
editor/Editor.fs       # drop the wheel dolly from `updateCamera`; orbit/fly only
render/Camera.fs       # drop `dolly`
test/InputTests.fs     # drop `Wheel` from the three input literals
test/PlayerTests.fs    # drop `Wheel` from its input literal
test/CameraTests.fs    # drop the two `dolly` tests
docs/EDITOR_DESIGN.md  # the interaction model: no scroll zoom (fly only)
```

**Structure Decision**: No module moves and no compile-order change. The removals are in place: the camera op
leaves `render/Camera.fs`; the editor stops calling it; the input field leaves `core/Input.fs` and the engine
stops producing it. `Camera.dolly` had exactly one production caller (`Editor.updateCamera`) and two tests, so
removing all three leaves no dangling reference.

## Complexity Tracking

> No Constitution Check violations.
