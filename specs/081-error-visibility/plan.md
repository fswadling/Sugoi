# Implementation Plan: Error Visibility

**Branch**: `081-error-visibility` | **Date**: 2026-10-12 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/081-error-visibility/spec.md`

## Summary

Give `080`'s console-only reports a visible surface: a small process-global **tally** records every non-fatal
report, de-duplicated by message with a running total; the application's existing **Debug window** shows the
total and the de-duplicated detail, and the existing **menu bar** carries an always-visible indicator when the
total is non-zero. Past a threshold the application focuses the Debug window once. No new window, dialog or
toast — the console line is unchanged.

## Technical Context

**Language/Version**: F# 8 / .NET `net10.0`

**Primary Dependencies**: unchanged. Uses the existing ImGui binding (`render/ImGui.fs`) and adds **two**
P/Invokes to functions already present in the vendored native `libcimgui.so`; no rebuild of the native library.

**Storage**: In-memory only, for the lifetime of the process. Nothing persisted. The console remains the
durable record.

**Testing**: xUnit (`test/fsharp-game.Tests.fsproj`), headless and device-free. The tally is pure and testable
on its own; the display surfaces are not unit-tested (they need a window) and are covered by the quickstart.

**Target Platform**: Desktop (Linux dev platform); single-process app.

**Project Type**: Desktop application — a data-oriented game engine plus an ImGui editor.

**Performance Goals**: No change to the frame budget. A frame with no reports allocates nothing (SC-003); a
report allocates only on the event.

**Constraints**: Reuse the application's existing surfaces (FR-008). Do not change the console output
(FR-006). Keep the dependency direction one-way (`screen → editor → state → world → core`): the tally lives in
`core` so the deep layers can report without depending on the display.

**Scale/Scope**: One new ~80-line module, one new test file, two binding declarations, and a mechanical edit
at ~20 existing report sites.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. One small module plus two binding lines. Reuses existing surfaces; no new window
  type, no notification framework. The ~20 call-site edits are a mechanical rename (`eprintfn` → the tally's
  `reportf`).
- **II. Fail Loudly** — PASS. Behaviour is unchanged: the same console line is still written for every
  report (FR-006). The feature only *adds* a visible read-out; it neither swallows nor downgrades a report.
- **III. Spec-First** — PASS. This plan precedes implementation.
- **IV. Data-Oriented Architecture** — PASS. The tally is a process-global counter, mirroring the existing
  `Gpu.UploadStats`/`Texture.loadStats` pattern; nothing hot-path changes shape.
- **V. Minimise GC Pressure** — PASS. Nothing is read or allocated per frame when there are no reports; the
  display reads two scalars and a short list only when the total is non-zero.

No violations — **Complexity Tracking is empty**.

## Project Structure

### Documentation (this feature)

```text
specs/081-error-visibility/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── error-visibility.md   # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

Single project, existing clusters. One new module (`core/Diagnostics.fs`) inserted into the compile list
after `core/SlotMap.fs` so every consumer (which sits higher) can see it.

```text
fsharp-game/
├── core/
│   └── Diagnostics.fs          # NEW: the process-global tally (report/reportf, count, recent, isLoud)
├── render/
│   └── ImGui.fs                # +2 bindings: pushStyleColorU32, popStyleColor
├── screen/
│   └── DebugOverlay.fs         # show the count + de-duplicated detail; focus once when loud
├── editor/
│   └── Editor.fs               # menu-bar indicator when the total is non-zero
├── world/                      # report sites re-wired to the tally (no behaviour change):
│   ├── CueEngine.fs            #   noOp / noOpTrigger / noOpPlace
│   ├── Assets.fs               #   scan / pump / eager load
│   ├── ActorRender.fs          #   missing-clip fallback
│   └── ScenarioFork.fs
├── state/
│   └── History.fs              # skip-and-report
├── editor/
│   └── Spawning.fs             # capacity refusal
├── render/
│   └── Ui.fs                   # font-disabled note
├── Engine.fs / Program.fs      # startup + frame-loop boundary
└── test/
    ├── DiagnosticsTests.fs     # NEW: count, de-dup, ordering, formatting, threshold
    └── fsharp-game.Tests.fsproj # register the new test file
```

**Structure Decision**: the tally goes in `core/` (dependency-free, below every reporter). `Diagnostics.fs`
is listed immediately after `core/SlotMap.fs` in `fsharp-game.fsproj`.

## Complexity Tracking

> No Constitution Check violations — this section is intentionally empty.
