# Implementation Plan: Editor-Only Reference Grid

**Branch**: `093-editor-only-grid` | **Date**: 2026-10-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/093-editor-only-grid/spec.md`

## Summary

The viewport's **reference grid** is currently drawn every frame in both modes. This feature makes it
an **editor-only aid**: drawn while editing, gone while playing. The grid lives in its own `DebugDraw`
accumulator (`GameplayState.DebugDraw`), separate from the markers/gizmo accumulator
(`GameplayState.GizmoDraw`, already editor-only), and is uploaded/drawn unconditionally in
`GameplayScreen.renderScene`. The change is therefore a single gate — `Viewport.drawGrid` rebuilds the
grid while `state.Editing`, and **empties** its accumulator while playing — plus the matching comments
and a headless test. No authored data, no render-pass change, no new module.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: SDL3 + SDL_gpu (`SDL3-CS`), Dear ImGui (`ImGui.fs`/`ImGuiSdl3.fs`). The grid
is CPU geometry accumulated into `render/DebugDraw.fs`'s line buffer. No new dependency.

**Storage**: N/A — a view rule only; the grid is never authored and never persisted (FR-005).

**Testing**: `dotnet test` (xUnit, one `<Module>Tests.fs` per module, headless).

**Target Platform**: Linux desktop (SDL3 + SDL_gpu / Vulkan).

**Project Type**: desktop game (single project, `fsharp-game.fsproj`).

**Performance Goals**: no change; a play-mode frame uploads an empty line buffer (one fewer draw than
today), an editing frame is unchanged.

**Constraints**: `state.DebugDraw` holds **only** the grid (`Viewport.drawGrid` is its sole writer), so
both hiding and clearing are the same act — emptying the accumulator. `renderScene` uploads and draws it
unconditionally, so no render-pass change is needed and no stale lines can survive the switch.

**Scale/Scope**: one gate in `editor/Viewport.fs` (`drawGrid` reads `state.Editing`), comment updates in
`editor/Viewport.fs` and `screen/GameplayScreen.fs`, one new test file. `fsharp-game.fsproj` is
unchanged (no new source module, no new shader).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. One changed function body and its call-site comments; no new function, no
  new module, no new setting. The grid stays one gated unit (axis lines included, per the Clarifications),
  so nothing is split speculatively.
- **II. Fail Loudly** — PASS. No error path is introduced or swallowed; the gate is a two-way branch on
  an existing flag.
- **III. Spec-First** — PASS. This plan follows `specs/093-editor-only-grid/spec.md`.
- **IV. Data-Oriented Architecture** — PASS. No new data; the existing `DebugDraw` accumulator record is
  reused (`DebugDraw.clear` is a struct copy).
- **V. Minimise GC Pressure** — PASS. `DebugDraw.clear` is `{ dd with Count = 0 }` — a struct copy that
  allocates nothing; play mode allocates strictly less than today.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/093-editor-only-grid/
├── plan.md              # this file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

No `contracts/`: the feature exposes no external interface (no shader, no file format, no public API);
it is an internal view rule.

### Source Code (repository root)

```text
editor/
└── Viewport.fs          # drawGrid gains the `Editing` gate (rebuild while editing, clear while playing);
                         #   module header + section banner name the grid editor-only
screen/
└── GameplayScreen.fs    # call-site comment: the grid is now editor-only (the call itself is unchanged)
test/
├── ViewportTests.fs     # NEW: headless — the grid accumulator is non-empty while editing, empty while
│                        #   playing, and empty on the play transition frame
└── fsharp-game.Tests.fsproj  # add the new test file to the compile list (near FadeTests.fs)
fsharp-game.fsproj       # UNCHANGED (no new source module, no new shader)
```

**Structure Decision**: single project; no file is added to the game project, so `fsharp-game.fsproj`'s
compile order is untouched. The gate sits in the existing `editor/Viewport.fs` (the module that draws
the grid), and the new test file is inserted into `test/fsharp-game.Tests.fsproj` beside `FadeTests.fs`
— the precedent for a pure, device-free view rule (`fadeOverlay`).

## Complexity Tracking

No constitution violations; nothing to justify.
