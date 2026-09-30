# Implementation Plan: Undoable Player Designation

**Branch**: `045-undoable-player-designation` | **Date**: 2026-09-30 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/045-undoable-player-designation/spec.md`

## Summary

The Inspector's actor section writes the player designation straight into the live zone
(`zone.Player <- ValueSome …`), so it is the one *zone-owned* editor edit that is not reversible —
while the zone's light, which is exactly the same kind of state, has been a command since spec 043. The
fix is to give the designation the command the light already has: a zone-level `SetPlayer of Previous :
Handle voption * Next : Handle voption` in the existing `Command` union, applied and undone against
`Zone.Player`, pushed by a `Selection.setPlayer` that mirrors `Selection.setLight` (including its
no-op-if-unchanged rule) and called by the Inspector's existing button. The button then reads its own
state — disabled and relabelled when the selected actor already is the player — so that setting, undoing
and redoing the designation are observable in the editor, where today nothing shows it at all.

One new command variant, one recording helper, one Inspector control, one test. No new module, no new
field, no new resource, no shader, no dependency, no persistence, and no change to play mode.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`), `TreatWarningsAsErrors`-clean build

**Primary Dependencies**: SDL3 (via `SDL3-CS`), Dear ImGui (custom cimgui binding, `render/ImGui.fs`),
Jolt Physics (untouched by this feature)

**Storage**: none — the designation is session state; the project has no save/load

**Testing**: xUnit via `dotnet test` (headless). The test project cannot open a GPU device, so only the
pure half is asserted; the editor control is the run walkthrough

**Target Platform**: Linux desktop (SDL_gpu/Vulkan)

**Project Type**: desktop game with an in-engine editor (single project; one `.fsproj` per the app and
its tests)

**Performance Goals**: 60 fps. This feature adds nothing to a per-frame path: a designation is a click,
and the editor's button already existed

**Constraints**: the constitution's minimalism and fail-loudly; `[<Struct>]` records for hot-path data;
the undo stacks are **per zone** (`GameplayState.UndoByZone`, swapped on a zone switch); the command
payload may hold only authored state, never a renderer or a physics attachment

**Scale/Scope**: one `Command` variant, one `Selection` helper, one Inspector control, one
`docs/EDITOR_DESIGN.md` decision; ~4 source files plus docs and tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — the change reuses the command union, the per-zone undo stack and the recording-helper pattern spec 043 established. It adds **no** new module, field, payload type, resource, pipeline or dependency, and it removes a documented inconsistency rather than adding a mechanism. The only new behaviour (the control reading its state) exists to make an undo observable, which is required for the feature to be verifiable at all. |
| II. Fail Loudly | Pass — no new failure path and no fallback. `Systems.playerHandleOf` already fails loudly for a non-actor ref, and the command cannot produce an invalid designation: it stores the very value `zone.Player` can hold, before and after. |
| III. Spec-First | Pass — `specs/045-undoable-player-designation/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — one DU case carrying two plain values (`Handle voption`, a `[<Struct>]` and a value option), joining a union that already carries zone-level before/after pairs; mutation stays the `mutable` field `Zone` already exposes. No object graph, no inheritance. |
| V. Minimise GC Pressure | Pass — the per-frame path is untouched. A designation is a discrete editor act: no allocation per frame, and the command is built once per click. |
| N-V. Testing is the Authoring Tool | Not engaged — the narrative subsystem is untouched. |

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

None. `SetPlayer` is the feature's one new command, and it is needed because the designation is the last
zone-owned edit that cannot be taken back. Nothing simpler suffices: a direct write is what the feature
exists to remove, and a project-scoped stack would be the wrong home for state that lives on a zone.

## Project Structure

### Documentation (this feature)

```text
specs/045-undoable-player-designation/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── player-designation.md   # Phase 1 output — the surfaces that change
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
state/
└── Command.fs           # CHANGED: `SetPlayer of Previous : Handle voption * Next : Handle voption`
                         #   (zone-level, beside `SetEnvironment`/`SetLight`), with its `apply` and
                         #   `undo` arms writing `zone.Player`. No payload type is added: the value is
                         #   already `Zone.Player`'s own.

editor/
├── Selection.fs         # CHANGED: `setPlayer`, the recording helper, shaped like `setLight` — read
│                        #   the previous value, no-op when unchanged (FR-004), apply, push one step.
└── Inspector.fs         # CHANGED: the actor section's control calls `Selection.setPlayer` and reads
                         #   the designation, drawn disabled/relabelled when this actor already is the
                         #   player (FR-005, Story 3).

docs/
└── EDITOR_DESIGN.md     # CHANGED: principle 4 and D-022 drop the player designation from the
                         #   direct-write list (it is a zone's own state, not project-scoped), and a
                         #   new D-025 records why it is a command, citing D-024's reasoning.

test/
└── ZoneTests.fs         # CHANGED: the designation is set by the command and put back by its undo —
                         #   the first designation (undo → no player) and a re-designation (undo →
                         #   the previous actor) — mirroring the `SetLight` case.

world/
├── Zone.fs              # UNCHANGED: `mutable Player : Handle voption` already exists.
└── Systems.fs           # UNCHANGED: `isPlayerLive` already tolerates a stale designation.
```

**Structure Decision**: the command joins **`state/Command.fs`**'s existing union rather than gaining a
module — it is the same shape as `SetEnvironment` and `SetLight`, which are the union's other two
zone-level cases, and it rides machinery (per-zone `UndoStack`, `Command.apply`/`undo`) that already
exists. The recording helper goes in **`editor/Selection.fs`**, where `setLight` lives, because that
module is the editor's one place for "make this edit and record it". The affordance stays in the
**Inspector**'s actor section, which is the only place the action has ever been. Nothing is added to
`world/Zone.fs` or `world/Systems.fs`: the field and the stale-tolerance both already exist, and the
feature's whole point is that they were never the problem.

## Phase 0: Research

See [research.md](./research.md). It settles the command's shape and home (D1), the recording helper and
its no-op rule (D2), the payload type and why no new one is needed (D3), the control that makes the edit
observable (D4), the design-doc amendment (D5), and what is asserted headlessly (D6).

## Phase 1: Design

- [data-model.md](./data-model.md) — the `SetPlayer` case, its `apply`/`undo` arms, the
  `Selection.setPlayer` contract and its no-op rule, the `Zone.Player` value's two states, and the
  validation rules (a non-actor ref, an unchanged value, a stale handle).
- [contracts/player-designation.md](./contracts/player-designation.md) — the surfaces that change
  (`Command`, `Selection`, `Inspector`, `EDITOR_DESIGN`) and what explicitly does not (`Zone`,
  `Systems`, the actor/zone/part commands, the light's command, the deliberately-direct writes, the
  renderer, and play mode).
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite and the run walkthrough
  (set, undo, redo, the no-op, the previous value, zone independence, the control's state).

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one union case, one helper, one control; no new module, field, resource or dependency. |
| II. Fail Loudly | Pass — no new failure path; the payload is the value the field already holds. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — a DU case carrying a `voption` of a `[<Struct>]`, on an existing union. |
| V. Minimise GC Pressure | Pass — no per-frame work added; one command per click. |
| N-V. Testing is the Authoring Tool | Not engaged. |
