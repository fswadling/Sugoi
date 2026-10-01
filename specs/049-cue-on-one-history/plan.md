# Implementation Plan: The cue's edits ride the one undo history

**Branch**: `049-cue-on-one-history` | **Date**: 2026-10-01 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/049-cue-on-one-history/spec.md`

## Summary

The cue's edits stop being a history of their own and become steps on the **one** history spec 048 put on the
gameplay state. One new `Command` case does it — `SetCue of Previous : Cue.Cue * Next : Cue.Cue` — and the cue's
own history goes: `EditorState.CueHistory`, its type and module, its field and seed, and the four functions and
two `smallButton`s that drove it in `editor/CueEditor.fs`.

The design's load-bearing idea is that **a cue is a value, so a cue step is a snapshot**. A cue is immutable
(`specs/046`), so "before" and "after" are two references and an undo is a write of the earlier one — which is
exactly what `CueHistory` stored, and the same shape `SetLight`/`SetPlayer` already use. One case covers every
edit the panel can make (a kind change, a text change, an addition, a removal), so nothing has to know how a cue
was edited. The reason a second history once existed — a cue spans zones while the stacks were per zone
(`specs/047` D-010) — is gone, because the stacks are now one and the moves between zones are steps on it
(`specs/048`).

Two seams are worth naming. `CueEditor.commit` writes `state.Cue` and pushes the step **itself**, not through
`History.commit`, so the panel keeps taking only the gameplay state (research D3); only `History.apply` and
`History.undo` need the `SetCue` arms, because redo and undo reach them. And when Play begins the cue's **steps** are pruned from both sides of
the history, not the history cleared (research D4) — the cue advances while playing, so its steps could no longer
be applied cleanly, while a zone step is unaffected and must stay undoable. That is the property US3 exists to
protect; clearing the history would silently forfeit what `specs/048` bought.

Only the *runners* learn the new step: `state/History.fs` gains one arm on `apply` and one on `undo`, and a
`pruneCueSteps` helper. `Command.apply`/`undo` **refuse** it, exactly as they refuse a zone step, because a cue is
not a zone's. No type moves, no module is added, the cue itself and its engine are untouched, and nothing is
added to a frame path.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`), `TreatWarningsAsErrors`-clean build

**Primary Dependencies**: none added. Prime, SDL3, Dear ImGui and Jolt are untouched; the feature uses the
existing `Cue` value, the command vocabulary and the one `UndoStack`

**Storage**: none — the history is in-memory and is not persisted (unchanged)

**Testing**: xUnit via `dotnet test` (headless). The suite builds a `GameplayState` the way
`test/CuePanelTests.fs` and `test/GameplayStateTests.fs` already do (`Unchecked.defaultof` for the GPU pieces, a
device-free asset manager), so every scenario — a cue edit and a `Transform` undone in order, a prune, a whole
history — is assertable with no device

**Target Platform**: Linux desktop (SDL_gpu/Vulkan)

**Project Type**: desktop game with an in-engine editor (single project; one `.fsproj` per the app and its tests)

**Performance Goals**: **no cost on any frame path.** The step is pushed only when the panel accepts an edit and
is undone only on a key press or a menu click; `pruneCueSteps` runs once, on the edit/play transition, over a
history bounded by the author's edits, not per frame. A cue step allocates nothing beyond the `SetCue` node and
the list cell `UndoStack.push` already makes — the cue payload is two references to immutable values, not a copy
of a run — and adds nothing to the play chain, the editor's per-frame update or any draw. The suite asserts the
payload's shape (a value snapshot), the way `specs/046`'s per-op cost and `specs/048`'s step cost are asserted.

**Constraints**: the constitution's five principles; commands store **values**, never closures, and a payload
cannot express a body, a renderer or a runtime flag; the `Command` type must stay above `state/GameplayState.fs`
(moving it is cyclic through `EditorState` → `UndoStack` → `Command`), so only the runners move; a cue step is
carried by `History` and refused by `Command`, so the zone-level runner keeps acting on names-no-zone commands
only; the living docs move with the feature, and the decisions it supersedes are recorded in its own artifacts
rather than patched into the specs that wrote them

**Scale/Scope**: one `Command` case, two arms and one helper in `state/History.fs`, one type/module/field
deleted from `state/EditorState.fs`, one function body changed and four functions deleted in `editor/CueEditor.fs`,
one line changed in `editor/Editor.fs`, one test file adapted and one extended, and the doc/header amendments the
contract lists. No change to `world/`, `render/`, `state/GameplayState.fs` or the `.fsproj`

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — the feature is a **removal** with a small addition: a whole history type, its module, a field and four functions go, and in their place stand one command case, two arms and one filter. It adds no dependency, no module, no panel and no new type. The cue is what is migrated, not a speculative framework for migrating; triggers and the other direct-write surfaces are deliberately left alone (spec Assumptions). |
| II. Fail Loudly | Pass — `Command.apply`/`undo` raise on `SetCue`, as they do on a zone step, so the zone-level runner can never silently ignore a cue step; the prune is an explicit filter over a known case, not a catch-all. No fallback and no silent default is added. |
| III. Spec-First | Pass — `specs/049-cue-on-one-history/spec.md` is written, clarified (one question, `## Clarifications`) and drives this plan. |
| IV. Data-Oriented Architecture | Pass — the new payload is two immutable cue **values** referenced, not a copied run; no closure, no runtime flag, no inheritance is introduced. The cue's run stays a value that shrinks to `Fin` (`specs/046`), and the command union stays a discriminated union of values. |
| V. Minimise GC Pressure | Pass — nothing is added to a frame path. A step's only allocations are the `SetCue` node and the list cell `UndoStack.push` already makes, taken when the edit is accepted; the payload is two references, so no per-frame or per-op copy of a cue is made. |
| N-V. Testing is the Authoring Tool | Not engaged as written (the narrative subsystem is untouched), but its requirement is adopted: the whole feature — a cue step undone beside a scene step, and the Play prune — is exercisable with no graphical interface and is asserted headlessly. |

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

None. The design removes a mechanism rather than adding one — a second history, its four functions and its two
controls are the only things that ever needed it, and the one case added reuses the shape `SetLight`/`SetPlayer`
established. The single deviation from the layering convention is already the project's documented one: a
state-level command is carried out by `state/History.fs`, compiled after `state/GameplayState.fs` (as
`world/CueEngine.fs` is), and this feature adds no new inversion — it extends the existing one.

## Project Structure

### Documentation (this feature)

```text
specs/049-cue-on-one-history/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output — nine decisions
├── data-model.md        # Phase 1 output — the case, the arms, the prune, the deleted history
├── quickstart.md        # Phase 1 output — validation
├── contracts/
│   └── cue-history.md   # Phase 1 output — the surfaces that change
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
state/
├── Command.fs           # CHANGED: `SetCue of Previous : Cue.Cue * Next : Cue.Cue` joins the union, and
│                        #   `apply`/`undo` group it with the zone steps in the one `invalidOp` (a cue is not a
│                        #   zone's, so the zone-level runner refuses it). Every other case, `Batch` and
│                        #   `UndoStack.create`/`push` are unchanged.
├── History.fs           # CHANGED: `apply` and `undo` each gain a `SetCue` arm (write `state.Cue`); NEW
│                        #   `pruneCueSteps`, which filters `SetCue` from both sides on the way into play.
├── EditorState.fs       # CHANGED: `CueHistory` — the type, its module, the field and its seed — is deleted.
│                        #   `CueSelection`/`CueTextBuffer`/`CueTextPath`/`CueTextEditing` stay.
└── GameplayState.fs     # UNCHANGED: the cue already lives here.
editor/
├── CueEditor.fs         # CHANGED: `commit` writes the cue and pushes `SetCue` onto `state.Editor.Undo` itself;
│                        #   `canUndo`/`canRedo`/`undo`/`redo` and the two buttons in `draw` are deleted; the
│                        #   header comment is rewritten.
└── Editor.fs            # CHANGED: the play toggle calls `History.pruneCueSteps` instead of resetting the cue's
                         #   history. The keys, the menu and the `idle` gate are unchanged.
test/
├── CuePanelTests.fs     # CHANGED: the `CueHistory` and panel-round-trip tests become one-history equivalents;
│                        #   SC-001, SC-002, SC-004 and FR-006 join them.
└── GameplayStateTests.fs # CHANGED: SC-003 and FR-010 — a prune leaves no cue step and a zone step undoable.
docs/
├── EDITOR_DESIGN.md     # CHANGED: the workspace table's Cue row, D-010, D-011, D-013, the module map row.
├── PROJECT_STRUCTURE.md # CHANGED: the `CueEditor.fs` line.
└── ROADMAP.md           # CHANGED: phase 9's cue-editor bullet and the `specs/047` line.
world/                   # UNCHANGED: `Cue.fs` and `CueEngine.fs` — the cue and its run.
fsharp-game.fsproj       # UNCHANGED: no `Compile` entry moves or is added.
specs/047-cue-panel/     # UNCHANGED: a historical artefact. The statements this feature supersedes are listed
specs/048-…/             #   in this feature's research.md, Decision 8.
```

**Structure Decision**: the new case goes in **`state/Command.fs`**, with the rest of the command vocabulary, and
its runner in **`state/History.fs`**, which already carries the state-level steps and is compiled after
`state/GameplayState.fs` — this feature adds no new inversion, it extends the existing one. The prune goes beside
the steppers in `History.fs` rather than inline in `editor/Editor.fs`, so the step's shape is known in exactly one
place. `Command.apply`/`undo` **refuse** the case, keeping the zone-level runner's contract that every command it
carries acts on the active zone. Nothing is added to `world/`, `render/`, the physics world or the `.fsproj`, and
the cue panel's editing rules, its tree control (`editor/CueTree.fs`) and every other panel are untouched.

## Phase 0: Research

See [research.md](./research.md). It settles that a cue edit is a command and not a second history (D1), why the
payload is two cue values (D2), why the panel commits inline and keeps taking only the state (D3), that Play
prunes the cue's steps from both sides and nothing else (D4), what a cue step does and does not touch (D5), that
the panel's controls and their functions are deleted (D6), what the suite asserts (D7), the superseded statements
and the wording (D8), and the documents that move (D9).

## Phase 1: Design

- [data-model.md](./data-model.md) — the `SetCue` case and why it is a value snapshot, the `invalidOp` the
  zone-level runner keeps and extends, the two `History` arms and `pruneCueSteps`, the `CueHistory` removal, the
  panel's new write path and the four functions it loses, the Play-toggle change, a command-semantics table and
  the validation rules.
- [contracts/cue-history.md](./contracts/cue-history.md) — every surface that changes (`state/Command.fs`,
  `state/History.fs`, `state/EditorState.fs`, `editor/CueEditor.fs`, `editor/Editor.fs`, the two test files, and
  the living docs) and what explicitly does not (the cue and its engine, the panel's editing rules, the rest of
  the command family, the `.fsproj`, the other editor files, the render and physics paths, and the prior specs,
  which are historical artefacts and are not edited).
- [quickstart.md](./quickstart.md) — runnable validation: the headless cases, and an editor walkthrough of one
  undo across the panel and the scene, the missing controls, Play keeping the zone edits, and a cue step touching
  nothing else.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one case, two arms and one filter in; a history type, a module, a field and four functions out. No new dependency, module or panel. |
| II. Fail Loudly | Pass — the zone-level runner raises on `SetCue`; the prune is an explicit, typed filter rather than a catch-all. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — a DU of values; the payload is two immutable cue references, not a copied run, and no closure or flag is introduced. |
| V. Minimise GC Pressure | Pass — nothing on a frame path; a step's allocations are its case node and one list cell, taken when the edit is accepted. |
| N-V. Testing is the Authoring Tool | Adopted in substance — D7 makes every scenario, including the prune, assertable headlessly with no device. |
