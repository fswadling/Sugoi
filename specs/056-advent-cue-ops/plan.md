# Implementation Plan: Advent Cue Ops

**Branch**: `056-advent-cue-ops` | **Date**: 2026-10-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/056-advent-cue-ops/spec.md`

## Summary

Add the four cue ops that make the adventure flags drive a cue: `AddAdvent of Advent` and `RemoveAdvent of Advent`
(no progress of their own — they write `SharedState.Advents` where the walk reaches them), `Await of Advent` (blocks
until the flag is set) with its `AwaitState` begun form, and `If of Advent * thenCue * elseCue` (runs the chosen
branch in its own place). The events carry the **closed `Advent` union**, not a string, for `specs/055`'s reason.

The work is confined to the cue's own modules plus the panel:

- `world/Cue.fs` — add the five cases; `open Advent` (a pure, earlier module), widening its dependency note by one
  pure module.
- `world/CueEngine.fs` — handle the ops where the walk reaches them, reading/writing `shared.Advents` (which it
  already carries); `open Advent`. `Await` is a condition re-checked each step, `If` resolves to its branch.
- `editor/CueTree.fs` — the four kinds, the milestone chooser over the closed list (`Advent.all`), an `If`'s two
  child rows, labels and the lock rule; `open Advent`.
- `test/CueTests.fs`, `test/CuePanelTests.fs` — the scenarios and the panel tables.
- `docs/ROADMAP.md` and the two module headers — corrected to name the advent ops.

No new module, no new field, no fsproj change (Advent is already compiled before `Cue.fs`), no new dependency. A
waiting `Await` writes nothing and reports `ValueNone`, so it allocates nothing per step.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`), `TreatWarningsAsErrors`-clean build

**Primary Dependencies**: `Advent` (existing, pure), `Prime` (for `FDeque`), `System.Numerics` — all already present

**Storage**: none — the flag set is runtime state, not persisted (phase 15)

**Testing**: xUnit via `dotnet test` (headless). The suite builds a `GameplayState`/`SharedState` the way
`test/CueTests.fs` and `test/AdventTests.fs` already do; the flag set is plain data, so both effects are asserted with
no device (`specs/046`, FR-016)

**Target Platform**: Linux desktop (SDL_gpu/Vulkan)

**Project Type**: desktop game with an in-engine editor (single project; one `.fsproj` per the app and its tests)

**Performance Goals**: 60 fps. A waiting `Await` must cost the loop **0 bytes per step** (`specs/046`, FR-012;
Constitution V)

**Constraints**: the constitution's five principles; a cue is a value and a step returns the next value without
mutating it; `Cue.fs` names no zone/screen/asset/device type (it now also names `Advent`, which is pure); the flags
are `SharedState`'s, so the effects land there; a running cue's flag writes are not undoable (existing invariant); the
ops must not change the shape of the run or the walk (`specs/046`, FR-017)

**Scale/Scope**: two `world/` modules and one `editor/` control changed, two test files extended, `docs/ROADMAP.md`
and two module headers corrected. No new module, no new field, no fsproj change

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — four ops (five cases) on the existing vocabulary, and **no new module, field or mechanism**: the effects reuse `SharedState.Advents` exactly as the `Advent` undo step does, and `Await` is a condition the walk re-checks rather than a machine. The closed union carries the milestone. |
| II. Fail Loudly | Pass — a milestone cannot be unknown (the union is closed, so a misspelling is a compile error); `Add`-already-set and `Remove`-not-set are documented **idempotent no-ops**, not errors (an expected state, not a bug). |
| III. Spec-First | Pass — `specs/056-advent-cue-ops/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — still one discriminated union; the flag set is F#'s `Set<Advent>` (immutable, ordered, replaced on change), the shape spec 055 chose. No object graph, no registry. |
| V. Minimise GC Pressure | Pass, and tested: a waiting `Await` reports `ValueNone` (a struct) and writes nothing, so it allocates nothing per step; `Add`/`Remove` replace a `Set`, an op-begin cost like any completed op, not a per-tick one. |
| N-V. Testing is the Authoring Tool | Adopted in substance — the flag set is plain data, so the ops are asserted headlessly against a `SharedState`. |

## Project Structure

### Documentation (this feature)

```text
specs/056-advent-cue-ops/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── advent-ops.md    # Phase 1 output — the surfaces that change
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
world/
├── Cue.fs           # CHANGED: add `AddAdvent of Advent`, `RemoveAdvent of Advent`, `Await of Advent`,
│                    #   `AwaitState of Advent` and `If of Advent * thenCue: Cue * elseCue: Cue`; `open Advent`.
│                    #   Still names no zone, screen, asset or device.
├── CueEngine.fs     # CHANGED: handle the ops where the walk reaches them — `Add`/`Remove` write
│                    #   `shared.Advents`; `Await` completes when the flag is set and otherwise becomes
│                    #   `AwaitState`; `If` runs the branch its flag selects. `open Advent`.
├── Advent.fs        # UNCHANGED: the closed union and its names.
└── Systems.fs       # UNCHANGED.
editor/
└── CueTree.fs       # CHANGED: the four kinds, an `Advent.all` chooser, an `If`'s two child rows, labels
                     #   and the lock rule for a running await; `open Advent`.
state/
├── SharedState.fs   # UNCHANGED: `Advents` is already there and mutable.
└── Command.fs       # UNCHANGED: a running cue's flag writes are not authored edits, so no command.
test/
├── CueTests.fs      # CHANGED: the four ops' scenarios and the 0-byte waiting step.
└── CuePanelTests.fs # CHANGED: the four kinds in the panel's tables.
docs/
└── ROADMAP.md       # CHANGED: phase 10's deliverable corrected to name the advent ops.
fsharp-game.fsproj   # UNCHANGED: `world/Advent.fs` is already compiled before `world/Cue.fs`.
```

**Structure Decision**: the ops stay inside the cue's own modules. `world/Cue.fs` gains one pure dependency
(`world/Advent.fs`, compiled earlier and naming nothing) so the events carry the closed union rather than a string;
`world/CueEngine.fs` performs the flag reads/writes on `SharedState.Advents` where the walk reaches them, exactly as
`SwitchZone` reuses `GameplayState.switchZone`. The panel's chooser needs only `Advent.all` (a closed list), so it
lives in `editor/CueTree.fs` without reading a zone — unlike the waypoint chooser, which the panel supplies because it
needs the active zone. There is no new module and no fsproj change.

## Phase 0: Research

See [research.md](./research.md). It settles carrying the closed union rather than a string (D1), how the write ops
effect the set (D2), why `Await` needs a begun form and what it means for `specs/046`'s convention (D3), how `If`
resolves to a branch (D4), and the editor's chooser and child rows (D5).

## Phase 1: Design

- [data-model.md](./data-model.md) — the added cases, the engine's handling, the panel tables, the state transitions of
  an await, and validation rules.
- [contracts/advent-ops.md](./contracts/advent-ops.md) — every surface that changes and what explicitly does not.
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite and a scratch tick that sets, awaits and
  branches on a milestone.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — four ops, no new module/field/mechanism; the effects reuse the existing flag set. |
| II. Fail Loudly | Pass — the union makes an unknown milestone unrepresentable, and the two idempotent cases are documented expected states. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — one DU unchanged in kind; F#'s `Set<Advent>` is the flag set. |
| V. Minimise GC Pressure | Pass, and tested: the waiting step's 0 bytes are asserted. |
| N-V. Testing is the Authoring Tool | Adopted in substance — asserted headlessly against the flag set. |
