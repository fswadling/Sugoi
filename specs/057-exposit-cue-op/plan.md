# Implementation Plan: Exposit Cue Op

**Branch**: `057-exposit-cue-op` | **Date**: 2026-10-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/057-exposit-cue-op/spec.md`

## Summary

Complete the cue's `Exposit` op so it authors a line's **presentation**, not only its text: extend the case from
`Exposit of text: string` to `Exposit of text: string * kind: Dialogue.Kind`, where the kind is the closed union
`PortraitDialogue of Roster.Character * Emotion | Dialogue | Thought` that `specs/038` already renders. The engine
writes the authored text and kind to `GameplayState.Dialogue` where the walk reaches the op; the view
(`world/DialogueRender.fs`) resolves and draws the portrait as it already does. The panel (`editor/CueTree.fs`) gains a
presentation chooser and, for a portrait line, a speaker chooser over the closed roster and an expression chooser over
the closed emotion set.

The work is confined to the cue's own modules, one pure dependency, the panel, and the tests:

- `world/Cue.fs` — change the `Exposit` case to carry `Dialogue.Kind`.
- `world/CueEngine.fs` — write the authored kind (and text) where the walk reaches the op.
- `editor/CueTree.fs` — `label`/`defaultOfKind`/`textOf`/`withText` updated for the new field; a `withExpositKind`
  write helper; the presentation/speaker/expression choosers in `drawEditor`.
- `world/Dialogue.fs` — correct the stale `[<SymbolicExpansion>]` note (its trigger is persistence, not this op).
- `test/CueTests.fs`, `test/CuePanelTests.fs` — the scenarios and the panel tables.

No new module, no new field, no fsproj change (`world/Dialogue.fs` is already compiled before `world/Cue.fs`), no new
package. The op still holds no run state and costs no time, so the idle path is unchanged.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`), warnings-as-errors-clean build

**Primary Dependencies**: `Dialogue` (existing, pure — the presentation vocabulary, which names `Roster`), `Roster`
(existing, pure), `Advent` (existing, pure), `Prime` (for `FDeque`), `System.Numerics` — all already present

**Storage**: none — the dialogue and the cue are runtime state, not persisted (phase 15)

**Testing**: xUnit via `dotnet test` (headless). The suite builds a `GameplayState` the way `test/CueTests.fs` and
`test/CuePanelTests.fs` already do; the dialogue is plain data, so the write is asserted with no device (`specs/046`,
FR-016)

**Target Platform**: Linux desktop (SDL_gpu/Vulkan); the panel's drawing needs a device, its pure half does not

**Project Type**: desktop game with an in-engine editor (single project; one `.fsproj` per the app and its tests)

**Performance Goals**: 60 fps. `Exposit` costs **no time** and adds **no run state**, so the existing idle-tick
allocation property is untouched (`specs/046` FR-012; Constitution V)

**Constraints**: the constitution's five principles; a cue is a value; `Cue.fs` names no zone/screen/asset/device type
(it now also names `Dialogue`, which is pure); resolution of a portrait stays the view's, at draw time, and a miss is
non-fatal (`specs/038`)

**Scale/Scope**: two `world/` modules, one `editor/` control, one comment correction and two test files. No new
module, no new field, no fsproj change

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one existing case widened by one field, plus one write helper; the presentation vocabulary, the display and the panel choosers are all reused. No new module, field, op or mechanism. |
| II. Fail Loudly | Pass — a speaker or expression cannot be unknown (both unions are closed, so a misspelling is a compile error); an unresolvable *portrait* is the expected, documented miss `specs/038` already models, not an error (an expected artistic state). |
| III. Spec-First | Pass — `specs/057-exposit-cue-op/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — still one discriminated union; the op carries a nested closed union value, no string, no object graph. |
| V. Minimise GC Pressure | Pass — the op holds no state and its step is one field-wise write to a struct `Dialogue`; the constructor allocation is an op-begin cost like any completed op, not a per-tick one. |
| N-V. Testing is the Authoring Tool | Adopted in substance — the dialogue is plain data, so the op is asserted headlessly. |

## Project Structure

### Documentation (this feature)

```text
specs/057-exposit-cue-op/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── exposit-op.md    # Phase 1 output — the surfaces that change
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
world/
├── Cue.fs           # CHANGED: `Exposit of text: string * kind: Dialogue.Kind`. Still names no zone, screen,
│                    #   asset or device; its dependency note widens by the pure `Dialogue` (and its `Roster`).
├── CueEngine.fs     # CHANGED: `exposit` writes the authored kind as well as the text where the walk reaches it.
├── Dialogue.fs      # CHANGED (one comment): the `[<SymbolicExpansion>]` note is corrected to name persistence
│                    #   (phase 15), not this op — nothing serializes a cue yet.
└── DialogueRender.fs# UNCHANGED: already resolves and draws all three kinds (spec 038).
cast/
└── Roster.fs        # UNCHANGED: the closed cast the speaker chooser walks.
editor/
├── CueTree.fs       # CHANGED: `label`, `defaultOfKind`, `textOf`, `withText` for the new field; a public
│                    #   `withExpositKind` write; presentation/speaker/expression choosers in `drawEditor`.
└── CueEditor.fs     # UNCHANGED: the existing text field still edits the line's text.
state/
├── GameplayState.fs # UNCHANGED: `Dialogue` is already there and mutable.
└── Command.fs       # UNCHANGED: a cue edit is still one `SetCue` step (spec 049).
test/
├── CueTests.fs      # CHANGED: the three presentation kinds, the instant step, and the replace case.
└── CuePanelTests.fs # CHANGED: labels, the default, `textOf`/`withText` preserving the kind, and `withExpositKind`.
fsharp-game.fsproj   # UNCHANGED: `world/Dialogue.fs` is already compiled before `world/Cue.fs`.
```

**Structure Decision**: the op stays inside the cue's own modules. `world/Cue.fs` gains one pure dependency
(`world/Dialogue.fs`, compiled earlier and naming only `Roster`) so the op carries the closed presentation union rather
than strings, exactly as `specs/056` widened the same note by `world/Advent.fs`. `world/CueEngine.fs` writes the
dialogue where the walk reaches the op, as it already does for the text. The panel's choosers need only closed lists
(`Roster.characters`, `Dialogue.emotions`), so they live in `editor/CueTree.fs` without reading a zone — unlike the
waypoint chooser, which the panel supplies because it needs the active zone (`specs/047` FR-006). Portrait resolution
stays in the view, where `specs/038` put it, so the engine still needs no `AssetManager`. There is no new module and no
fsproj change.

## Phase 0: Research

See [research.md](./research.md). It settles carrying the closed `Dialogue.Kind` rather than reference-style fields
(D1), leaving resolution at draw time (D2), adding no `…State` case (D3), keeping the row label stable (D4), putting
the choosers in the control (D5), and deferring `[<SymbolicExpansion>]` (D6).

## Phase 1: Design

- [data-model.md](./data-model.md) — the `Exposit` case, the engine's write, the panel surfaces, and validation rules.
- [contracts/exposit-op.md](./contracts/exposit-op.md) — every surface that changes and what explicitly does not.
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite and a scratch tick that shows a portrait
  line and a thought.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one field, one helper, reused vocabulary/display/choosers; no new mechanism. |
| II. Fail Loudly | Pass — closed unions make a bad speaker/expression unrepresentable; an unbound portrait is the documented expected miss. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — one DU, nested closed union value, no string, no object graph. |
| V. Minimise GC Pressure | Pass — no state, one field-wise struct write; idle path unchanged. |
| N-V. Testing is the Authoring Tool | Adopted in substance — asserted headlessly against the dialogue. |
