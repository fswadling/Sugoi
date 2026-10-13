# Implementation Plan: Advents as an Ordered String Set

**Branch**: `082-advent-strings` | **Date**: 2026-10-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/082-advent-strings/spec.md`

## Summary

Replace the hard-coded `Advent` discriminated union with a plain **string** milestone and the flag set with an
**ordered set of strings** — Prime's `OSet<string>`, which iterates in the order names were added and
de-duplicates. `world/Advent.fs` stops being a union with a reflection enumeration and becomes a thin home for the
set type and its four operations; `SharedState.Advents` changes from `Set<Advent>` to that type; `Command.Advent`
carries the before/after set as before (an `OSet` is persistent, so a reference snapshot is complete). The four cue
ops that named a milestone (`AddAdvent`, `RemoveAdvent`, `Await`, `If`) carry a **string** instead, and
`world/Cue.fs` no longer names `Advent` at all. `world/CueEngine.fs` adds/removes/tests membership through the set's
operations; the per-frame `Await` check stays allocation-free (verified: `OSet.contains` allocates **0 bytes** over
100k calls).

The **Advent panel** is rewritten: a single-line **text field** and an **Add** control record a typed name, and a
**list** of the current advents offers a **Remove** control per row — replacing the one-checkbox-per-case checklist,
since there are no fixed cases to list. A name is trimmed of surrounding whitespace and matched **exactly
(ordinal, case-sensitive)** (`specs/074`'s rule). Each add and remove is one `Advent` step on the one history,
unchanged. The cue panel's milestone chooser offers the **current** advent names and accepts a **typed** name that
matches none (`editor/CueTree.fs`'s existing `filteredNameCombo`, the `specs/074` speaker chooser's rule); typing a
new name does **not** add it to the set. There is no vocabulary and no cross-reference check — spelling correctness
is the author's responsibility, the deliberate trade.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: Prime (`OSet`, already referenced — no new package), `world/Advent.fs` (the set's home),
`state/SharedState.fs`, `state/Command.fs`, `state/History.fs`, `state/EditorState.fs` (a new text buffer),
`world/Cue.fs` + `world/CueEngine.fs` (the four ops and their run), `editor/AdventPanel.fs` (rewritten),
`editor/CueTree.fs` (labels, defaults, chooser), `editor/CueEditor.fs` (feeding the chooser).

**Storage**: N/A (nothing persisted — `specs/055` FR-010; persistence is phase 15)

**Testing**: xUnit, headless (`dotnet test`). `test/AdventTests.fs` rewritten for strings and the ordered set;
`test/CueTests.fs` and `test/CuePanelTests.fs` migrated to string milestones; every `SharedState` literal in tests
loses `Set.empty` for the advent field. The existing `an Await in flight allocates nothing per step` guard is
retained and now proves `OSet.contains` is allocation-free. No device.

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor, narrative engine in-process)

**Performance Goals**: the per-frame `Await`/`If` membership check MUST stay allocation-free (project Principle V);
met by `OSet.contains` (measured 0 bytes over 100,000 calls on a small set). `Advent.names` (an `OSet.toSeq`) is
editor-only and off the play path. The cue panel's chooser recomputes the names each frame in the editor only.

**Constraints**: warning-free build; green headless suite; no device opened; `world/Advent.fs` keeps its filename
and compile position (no `.fsproj` change); the panel's window stays named `"Advent"`, so the native dock layout
(`native/cimgui_sdl3.cpp`) is untouched.

**Scale/Scope**: game source — 1 module rewritten (`world/Advent.fs`), 1 panel rewritten (`editor/AdventPanel.fs`),
~9 edited (`state/SharedState.fs`, `state/Command.fs`, `state/History.fs`, `state/EditorState.fs`, `world/Cue.fs`,
`world/CueEngine.fs`, `editor/CueTree.fs`, `editor/CueEditor.fs`, `world/Scenario.fs` — comment only); tests —
1 rewritten, ~8 edited; docs — `ARCHITECTURE.md`, `EDITOR_DESIGN.md`, `PROJECT_STRUCTURE.md`, `ROADMAP.md`,
`CUE_MODEL_DECISION.md`. No files added or deleted; no `.fsproj` change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. It removes the union, its reflection enumeration (`all`) and its display name (`name`),
  and replaces them with a set type and four one-line operations. The panel is rewritten, not enlarged. The ordered
  set comes from an existing dependency (Prime), so nothing is added to the project.
- **II. Fail Loudly** — PASS. Nothing unexpected is swallowed: a blank add is refused as a value, not a silent
  default, and no name-miss raises (a miss is an expected authoring reality, `specs/074`'s stance). The deliberate
  removal of compile-time checking is the feature's premise — there is no vocabulary to validate against, and the
  author owns spelling — rather than an unrecoverable state left silent.
- **III. Spec-First** — PASS. This feature is `specs/082-advent-strings`.
- **IV. Data-Oriented Architecture** — PASS. A milestone is a plain string in an immutable persistent set; no
  object graph, no inheritance, no wrapper type beyond the set.
- **V. Minimise GC Pressure** — PASS. `OSet.contains` is allocation-free (measured), so the per-frame `Await`
  stays at 0 bytes per step; the set operations are the only hot-path change. `Advent.names` allocates, and is
  editor-only.
- **Narrative subsystem (N-I…N-V)** — N/A. The narrative engine names no milestone; the change does not reach it.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/082-advent-strings/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── advent-set.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
fsharp-game.fsproj         # unchanged (world/Advent.fs keeps its filename and compile slot); its header comment
                           #   for the file says "closed union" and is corrected
world/Advent.fs            # REWRITTEN: no union, no `all`, no `name`. Becomes `type Set = OSet<string>` plus
                           #   `empty`, `contains`, `add`, `remove`, `names`, `normalise` (trim). `open Prime`.
state/SharedState.fs       # `mutable Advents : Advent.Set`; `create` starts it `Advent.empty`
state/Command.fs           # `Advent of Previous: Advent.Set * Next: Advent.Set` (same step shape)
state/History.fs           # no logic change (assigns `next` / `previous`); comment reworded
state/EditorState.fs       # NEW field `AdventNameBuffer : byte[]` for the panel's text field
world/Cue.fs               # the four ops carry `string`: `AddAdvent/RemoveAdvent/Await/AwaitState of advent: string`,
                           #   `If of advent: string * …`; drop `open Advent`; header comment corrected
world/CueEngine.fs         # `Advent.add`/`remove`/`contains` in place of `Set.*`; drop the `Set`/`Advent` opens
editor/AdventPanel.fs      # REWRITTEN: text field + Add, and a list with Remove per row; `commit` unchanged shape
editor/CueTree.fs          # `withAdvent` takes a string; labels use the raw name (omitted when empty);
                           #   `defaultOfKind` defaults an advent op to `""`; `drawEditor` gains `advents: string list`
                           #   and its milestone combo becomes `filteredNameCombo` with a typed fallback
editor/CueEditor.fs        # pass `Advent.names shared.Advents` into `CueTree.drawEditor` (both call sites)
world/Scenario.fs          # comment only: "matched over the cases as `Advent.name` is" is corrected

test/AdventTests.fs        # REWRITTEN: strings, ordered set (order/dedup/trim/blank), one-step undo, zone survival
test/CueTests.fs           # milestone strings; `Set.contains` -> `Advent.contains`; the await/allocation guards kept
test/CuePanelTests.fs      # milestone strings; the advent-op defaults now `""`; labels use the raw name
test/GameplayStateTests.fs # `SharedState` literal: `Advents = Advent.empty`
test/SpawningTests.fs      # "
test/ScenarioTests.fs      # "
test/SelectionTests.fs     # "
test/ScenarioForkTests.fs  # "
test/ActorNameTests.fs     # "
docs/ARCHITECTURE.md       # the `Advent_Set` node and the milestone passages
docs/EDITOR_DESIGN.md      # D-011/D-013 and the Advent panel description (checklist -> text field + list)
docs/PROJECT_STRUCTURE.md  # the `Advent.fs` line and its header
docs/ROADMAP.md            # the phase-10 "adventure flags" passage
docs/CUE_MODEL_DECISION.md # the advents-off-the-one-history passages
```

**Structure Decision**: single project, F#'s file-order model. `world/Advent.fs` stays exactly where it is in the
`.fsproj` list (before `state/`), so nothing reorders; it merely changes from a union to a set module, and
`world/Cue.fs` — compiled just below it — loses its dependency on it entirely (the ops now carry strings). Every
consumer that held a `Set<Advent>` holds an `Advent.Set` instead, which is the same persistent shape, so the undo
step, the state field, and the panel's write path keep their structure. The only new state is one text buffer on the
editor's transient state, beside the zone-name field it mirrors.

## Complexity Tracking

> No Constitution Check violations.
