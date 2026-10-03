# Implementation Plan: Fade Cue Ops

**Branch**: `060-fade-cue-ops` | **Date**: 2026-10-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/060-fade-cue-ops/spec.md`

## Summary

Add the two fade ops the reference has — `FadeOut` and `FadeIn`, each carrying a `duration` — to the cue,
and drive `GameplayState.Fade` (the level `specs/059` renders) from wherever it stands to 1.0 (out) or 0.0
(in) over that duration, landing exactly on the endpoint. A duration of zero or less is over before it
begins. The cue panel lists both kinds, shows an in-flight fade as the op it came from, and retunes its
duration as one undo step.

The work is confined to the cue's own modules and the tests, and needs no fsproj change:

- `world/Cue.fs` — `FadeOut of duration`, `FadeIn of duration`, and their `…State` cases (the starting
  level, the duration, the deadline), plus the header note that names the timed leaves.
- `world/CueEngine.fs` — one private `stepFade` helper and the four `advance` arms that write
  `state.Fade`.
- `editor/CueTree.fs` — `kinds`, `kindName`, `defaultOfKind`, `label`, `executing`, `children` and
  `drawEditor` for the two kinds.
- `test/CueTests.fs`, `test/CuePanelTests.fs` — the scenarios, the two `describe` helpers, and the panel
  tables.

No new module, no new field, no package, no native change. An in-flight fade holds no `FDeque` and rebuilds
nothing, so the idle path is unchanged.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`), warnings-as-errors-clean build

**Primary Dependencies**: `Prime` (for `FDeque`, unchanged), `System.Numerics`, `Advent`, `Dialogue` — all
already present. The engine names `GameplayState` (it already does) for the level the ops write.

**Storage**: none — a cue is runtime data and nothing serializes it (persistence is a later phase)

**Testing**: xUnit via `dotnet test` (headless). The effect is a `float32` on `GameplayState`, so every
scenario is stepped against a headless state and asserted; no window, GPU or device (`specs/046` FR-016)

**Target Platform**: Linux desktop (SDL_gpu/Vulkan); the engine's writes need no device, and the panel's
drawing is the walkthrough's business

**Project Type**: desktop game with an in-engine editor (single project; one `.fsproj` per the app and its
tests)

**Performance Goals**: 60 fps. An in-flight fade writes one `float32` and rebuilds nothing, so it allocates
nothing per step; a zero-duration fade costs no time (Constitution V)

**Constraints**: the constitution's five principles; a cue is a value; `world/Cue.fs` still names no zone,
screen, asset or device — the fade ops carry only a duration, exactly as the reference's do, and the engine
that reaches the screen is already below the state (`specs/046` D3)

**Scale/Scope**: two `world/` modules, one `editor/` control and two test files. No new module, field,
fsproj entry or package

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — two ops of one `float` each, four cases total, one shared `stepFade`; the level, the overlay and the panel control all already exist. No easing, no colour, no new mechanism. |
| II. Fail Loudly | Pass — there is no invalid fade (the duration's sign has meaning, not error); a duration of zero or less is a defined immediate fade, matching `Wait`. Nothing is silently defaulted. |
| III. Spec-First | Pass — `specs/060-fade-cue-ops/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — still one discriminated union; each fade carries a scalar and its run state three scalars, no object graph. |
| V. Minimise GC Pressure | Pass — an in-flight fade rebuilds nothing and writes one `float32`; the begin step allocates only the state case the walk already carries, not per-tick. |
| N-V. Testing is the Authoring Tool | Not this subsystem (the narrative engine's); adopted in substance — the level over a timeline is asserted headlessly. |

## Project Structure

### Documentation (this feature)

```text
specs/060-fade-cue-ops/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── fade-cue-ops.md  # Phase 1 output — the surfaces that change
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
world/
├── Cue.fs           # CHANGED: `FadeOut`/`FadeIn of duration` and their `…State` cases; the header note.
└── CueEngine.fs     # CHANGED: `stepFade` and the four `advance` arms that write `GameplayState.Fade`.
editor/
└── CueTree.fs       # CHANGED: the two kinds in `kinds`/`kindName`/`defaultOfKind`/`label`/`executing`/
                     #   `children`/`drawEditor`.
state/
└── GameplayState.fs # UNCHANGED: `Fade` is already there (spec 059).
screen/
└── GameplayScreen.fs# UNCHANGED: the overlay already reads `Fade`; the ops only move the value.
test/
├── CueTests.fs      # CHANGED: the fade timeline, the part-way start, the endpoint, the immediate case,
│                    #   the allocation property, and `describe`.
└── CuePanelTests.fs # CHANGED: the kinds/labels, the in-flight lock, the retune-one-step, and `describe`.
fsharp-game.fsproj   # UNCHANGED: no new module.
```

**Structure Decision**: the ops belong in the cue's own modules, exactly as `Wait` and `FlyCamera` do. The
authored cases carry only a `duration` — never the level — so `world/Cue.fs` stays free of
`GameplayState` and keeps its "names no zone, screen, asset or device" property; the level is captured and
written in `world/CueEngine.fs`, which is compiled after the state for precisely this reason. The panel
needs no zone, registry or asset for a duration, so the whole authoring surface stays in the shared control
(`editor/CueTree.fs`) with the other numbers.

## Phase 0: Research

See [research.md](./research.md). It settles two ops with four cases over one shared state case (D1), a
linear tween that lands exactly on the endpoint (D2), an immediate rule for a non-positive duration (D3),
one shared `stepFade` helper (D4), the panel treating fades as `Wait` is treated (D5), capturing the level
at begin (D6), and allowing two fades at once rather than forbidding them (D7).

## Phase 1: Design

- [data-model.md](./data-model.md) — the cue cases, the engine's handlers, the panel surfaces, validation.
- [contracts/fade-cue-ops.md](./contracts/fade-cue-ops.md) — every surface that changes, and what does not.
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite and the interactive walkthrough.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — two scalar ops, one helper, reused panel and overlay; no new mechanism. |
| II. Fail Loudly | Pass — a non-positive duration is a defined immediate fade, not an error. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — one DU with scalar-carrying cases. |
| V. Minimise GC Pressure | Pass — in-flight rebuilds nothing; one float write per step. |
| N-V. Testing is the Authoring Tool | Adopted in substance — the timeline is asserted headlessly. |
