# Implementation Plan: Commands Carry the Zone They Act On

**Branch**: `077-commands-carry-zone` | **Date**: 2026-10-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/077-commands-carry-zone/spec.md`

## Summary

Give every command that changes a zone's contents the **place** it acts on, so a step is self-contained: apply and
undo resolve that place and act on its zone, without switching the active place. This removes the reason the "no
command carries a zone" rule existed (`docs/EDITOR_DESIGN.md` D-011; `specs/048`): a step no longer relies on the
active place being the one it was recorded in. A `Batch` may then span places — the capability the scenario fork
needs — as one undo step. Two consequences are pulled in with it: the transform-coalescing key becomes **(place, ref)**
(so transforms in two places never merge), and a deleted zone's restore rebuilds into the **inactive** zone and
detaches its bodies instead of switching to it and back (FR-014).

This is a **model refactor**, behaviour-preserving except where stated. It changes no command's effect — only which
zone it acts on and how a step finds it.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: `state/Command.fs` (the command type and the zone-level runner), `state/History.fs` (the
one history and the state-level runner), `world/Place.fs` (the place a command now carries), `world/Zone.fs`
(`occupyZone`/`deactivatePhysics`, for the delete-restore path), and every editor construction site:
`editor/Selection.fs`, `editor/Spawning.fs`, `editor/Editor.fs`, `editor/Inspector.fs`, `editor/CueEditor.fs`,
`editor/AssetBrowser.fs`, `editor/ZoneRegistryPanel.fs`. All present; no new dependency.

**Storage**: N/A — commands are not persisted; nothing on disk holds a command's shape.

**Testing**: xUnit, headless (`dotnet test`). The command/history tests are already headless (`GameplayStateTests`,
`SelectionTests`, `SpawningTests`, `CuePanelTests`, `AdventTests`), so the new guarantees are asserted with no device.

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: editor-only, cold path — a command is built on a click and undone on Ctrl+Z; the play loop is
untouched (it runs no commands). No per-frame cost.

**Constraints**: the `Command` type stays above `GameplayState` (it is compiled before it) and cannot name it;
resolution lives in the state-level runner. Behaviour for a single-place session is unchanged. Build warning-free;
suite green; no device opened.

**Scale/Scope**: 0 new source files; 9 source files touched (`state/Command.fs`, `state/History.fs`, and the 7 editor
sites above); 2 docs touched (`docs/EDITOR_DESIGN.md`, `docs/ARCHITECTURE.md`); tests across ~6 files updated.
`world/Zone.fs` is touched only if the delete-restore path needs a small addition (expected: none — it reuses
`deactivatePhysics`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. No new abstraction: a `Place` field is added to commands that already act on a zone, and a
  small `placeOf` helper. It **removes** a special rule (commands carry no zone) and its scaffolding (`ZoneMove`'s
  load-bearing role), and lets a future fork reuse the ordinary `Batch` rather than adding a bespoke command.
- **II. Fail Loudly** — PASS. A content command whose named place is absent when it is reached is a broken invariant
  and fails loudly (the spec's edge case); existing raises (part epoch mismatch, `Zone.occupy*` slot checks) are
  unchanged.
- **III. Spec-First** — PASS. This feature is `specs/077-commands-carry-zone`.
- **IV. Data-Oriented Architecture** — PASS. `Place` is a closed-union value; it is carried by value in the command
  record. No object graph is introduced.
- **V. Minimise GC Pressure** — PASS. The command layer is a cold, editor-only path; the play loop runs no commands.
  A `Place` is a small value with no allocation of its own.

The **Narrative Subsystem** principles do not apply (command/history layer, not `narrative/`).

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/077-commands-carry-zone/
├── spec.md
├── plan.md                    # this file
├── research.md                # Phase 0 output
├── data-model.md              # Phase 1 output
├── quickstart.md              # Phase 1 output
├── contracts/
│   └── command-resolution.md
├── checklists/
│   └── requirements.md
└── tasks.md                   # /speckit-tasks
```

### Source Code (repository root)

```text
state/Command.fs            # CHANGED: every zone-content case carries `Place` as its first field; add
                            #   `placeOf : Command -> Place option`. The state-level invalidOp group is unchanged.
state/History.fs            # CHANGED: apply/undo resolve each content command's place to its zone (via `placeOf`)
                            #   instead of the active zone; `Batch` still recurses per sub-command. `ZoneDelete`'s
                            #   undo rebuilds into the inactive zone and calls `Zone.deactivatePhysics` (no switch).
editor/Selection.fs         # CHANGED: each command built names `state.ActiveZone`; the direct `apply zone …` path
                            #   resolves to that same zone.
editor/Spawning.fs          # CHANGED: `*Create`/`Link*` commands (and `instantiateZone`'s batch) name the active
                            #   place.
editor/Editor.fs            # CHANGED: `Transform` commands (drag, nudge) name the active place.
editor/Inspector.fs         # CHANGED: commands it builds (via `Selection`) name the active place.
editor/CueEditor.fs         # CHANGED: any command it builds names the active place (`SetCue` is screen-level and
                            #   unchanged).
editor/AssetBrowser.fs      # CHANGED: the `SetEnvironment` step names the active place.
editor/ZoneRegistryPanel.fs # CHANGED: the instantiate batch names the active place.

docs/EDITOR_DESIGN.md       # D-011 reworded: a command carries the place it acts on
docs/ARCHITECTURE.md        # the command/history description updated

test/*.fs                   # CHANGED: every command construction gains a place; new tests for US1/US2/US3
```

**Structure Decision**: No module moves and no new files. `Command` and `History` keep their positions (`Command`
before `EditorState`/`GameplayState`; `History` after them), which is what keeps resolution out of the command type:
`Command` cannot name `GameplayState`, so it exposes `placeOf` and the state-level runner resolves place → zone from
`state.Zones`. The editor sites already hold `state`, so naming `state.ActiveZone` is mechanical. `world/Zone.fs` is
expected untouched: the delete-restore path reuses `Zone.deactivatePhysics`, the same build-then-detach
`GameplayState.populateZone` already uses for a zone that is not active.

## Complexity Tracking

> No Constitution Check violations.
