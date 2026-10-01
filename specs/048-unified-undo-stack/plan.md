# Implementation Plan: One undo history on the gameplay state

**Branch**: `048-unified-undo-stack` | **Date**: 2026-10-01 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/048-unified-undo-stack/spec.md`

## Summary

The editor keeps **one** history of scene edits instead of one per zone. `GameplayState.UndoByZone` — the side map
of parked stacks `specs/024` introduced — is deleted, and `EditorState.Undo` becomes the session's timeline. Three
new `Command` cases make that possible: `ZoneMove`, `ZoneAdd` and `ZoneDelete`.

The design's load-bearing idea is that **recording the moves is what removes the need to record a zone on a
command**. If every change of the active zone is a step, last-in-first-out guarantees that when undo reaches a
step, the active zone is the zone that step was recorded in — so the existing per-kind command family is untouched,
`apply`/`undo` keep acting on the active zone, and correctness is structural rather than a rule every future
command must remember. This deliberately reverses `specs/024` Decision 5's rejected alternative ("commands
carrying their zone"); the objection it named — undo teleporting the view — is answered by the move being a
*recorded step* the developer made rather than an implicit jump.

`ZoneDelete` carries a `ZoneContents` — the zone's authored state only (entities by ref, environment, light,
player), rebuilt through the same `Zone.occupy*` calls an entity create already uses — which reverses
`specs/024` Decision 11's "deletion is not an undoable edit".

Only the command *runners* move behind the state: `state/History.fs` (new, compiled after `state/GameplayState.fs`)
holds `apply`/`undo`/`commit` and the **state-level** steppers `undoHistory`/`redoHistory`, because a zone step
creates, removes and switches a zone. The `Command` **type** does not move — moving it would be cyclic — so
`state/Command.fs` keeps the type, the per-command `apply`/`undo`, and all of `UndoStack` (`create`, `push`, and the
zone-level `undo`/`redo`, which need no state and which the zone-level suite drives).

The cue is explicitly out of scope (FR-014): it keeps its own history, and moving it onto this one is the next
feature. The per-frame hot path is untouched — undo/redo is a discrete editor action, not a frame cost — and no
new dependency, module in `world/` or `editor/` panel is added.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`), `TreatWarningsAsErrors`-clean build

**Primary Dependencies**: none added. Prime, SDL3, Dear ImGui and Jolt are untouched; the feature uses `Zone`'s
existing `capture*`/`occupy*` readers and writers, `EntityRef`, and the existing command vocabulary

**Storage**: none — the history is in-memory and is not persisted (unchanged)

**Testing**: xUnit via `dotnet test` (headless). The suite builds a `GameplayState` the way
`test/GameplayStateTests.fs` already does (`Unchecked.defaultof` for the GPU pieces, a device-free asset manager),
so every scenario — including a zone delete's round trip and the refs it preserves — is assertable without a
device

**Target Platform**: Linux desktop (SDL_gpu/Vulkan)

**Project Type**: desktop game with an in-engine editor (single project; one `.fsproj` per the app and its tests)

**Performance Goals**: **no cost on any frame path.** Undo/redo fires only on a key press or a menu click and only
while editing, and the feature adds nothing to the play chain, the editor's per-frame update or any draw. Undoing a
zone delete is proportional to that zone's entity count — the same `Zone.occupy*` calls a spawn already makes —
and does not grow with the depth of the history, because a step applies one command. The suite asserts this as a
bound rather than pinning a byte figure, the way `specs/046`'s per-op cost is asserted.

**Constraints**: the constitution's five principles; commands store **values**, never closures, and a payload
cannot express a body, a renderer or a runtime flag; the `Command` type must stay above `state/GameplayState.fs`
(moving it is cyclic through `EditorState` → `UndoStack` → `Command`), so only the runners move; the zone guards
(non-empty, unused name, not the last zone) stay enforced where they are; the living docs move with the feature,
and the decisions it supersedes are recorded in its own artifacts rather than patched into the specs that wrote
them

**Scale/Scope**: one new module (~80 lines), three `Command` cases and one small record, one side map removed,
two editor files changed (one of them by three call sites), one new `.fsproj` entry, one test file extended, and
the doc/spec amendments the contract lists. No change to `world/Zone.fs`, the render path or the physics world

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — the feature is a **removal** with a small addition: the per-zone side map and its swap go, and in their place stand three command cases, one record and one short runner module. It adds no dependency, no new panel, no new `world/` module, and no per-kind command. Two histories become one for scene edits, so `specs/032`'s, `specs/034`'s and `specs/047`'s exceptions lose their cause. The cue is deliberately **not** migrated, so nothing it needs is built speculatively. |
| II. Fail Loudly | Pass — the zone guards are unchanged and still raise (`switchZone` on an unknown name, `addZone` on an empty or duplicate one, `deleteZone` on the last zone), and the undo path cannot reach them badly because last-in-first-out means a step never names a zone that a later step removed. No fallback, no silent default, no catch-all is added. |
| III. Spec-First | Pass — `specs/048-unified-undo-stack/spec.md` is written, clarified (five questions, `## Clarifications`) and drives this plan. |
| IV. Data-Oriented Architecture | Pass — commands stay a discriminated union of values with no closures; the state **loses** the per-zone dictionary and gains nothing; the history is two `Command list`s as it already was. `EntityRef` identity survives a zone round trip, which is the same discipline `specs/015` established. |
| V. Minimise GC Pressure | Pass — nothing is added to a frame path. The allocations a step makes are its payloads, made when the step is taken: `captureZone` allocates on a delete (which is a discrete action), and an undo allocates through the `occupy*` path a spawn already uses. No allocation is added to `update`, `draw` or the play chain. |
| N-V. Testing is the Authoring Tool | Not engaged as written (the narrative subsystem is untouched), but its requirement is adopted: the whole feature is exercisable with no graphical interface and is asserted headlessly, which is the only way this project can demonstrate an undo that crosses zones. |

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

None. The design removes a mechanism rather than adding one — the side map, the swap, and the two Undo/Redo
controls the cue still needs are the only things that ever needed it. The one place it follows a deviation from
the layering convention is the second use of the project's existing, documented inversion (a module compiled after
`state/GameplayState.fs`, as `world/CueEngine.fs` is), taken because a zone step is a `GameplayState` operation.

## Project Structure

### Documentation (this feature)

```text
specs/048-unified-undo-stack/
├── plan.md              # This file
├── research.md          # Phase 0 output — ten decisions
├── data-model.md        # Phase 1 output — the three cases, the record, the runner
├── quickstart.md        # Phase 1 output — validation
├── contracts/
│   └── undo-history.md  # Phase 1 output — the surfaces that change
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
state/
├── Command.fs           # CHANGED: `ZoneMove`/`ZoneAdd`/`ZoneDelete` join the union, and `ZoneDelete` carries
│                        #   `Zone.ZoneContents`. The per-command `apply`/`undo` and `UndoStack` (`create`,
│                        #   `push`, and the zone-level `undo`/`redo` the zone suite drives) stay as they are.
├── History.fs           # NEW — and listed *after* `state/GameplayState.fs`, because a zone step creates,
│                        #   removes and switches a zone: `apply`/`undo` (the state-level runner), `commit`
│                        #   (apply then push) and `undoHistory`/`redoHistory` (the state-level steppers).
├── GameplayState.fs     # CHANGED: `UndoByZone` removed and the stack swap gone from `enterZone`.
│                        #   `switchZone`/`selectZone`/`addZone`/`deleteZone` keep their signatures and guards;
│                        #   `deleteZone` is what a `ZoneDelete` applies and what a `ZoneAdd`'s undo calls.
└── EditorState.fs       # CHANGED in meaning only: `Undo` is now the one history, not the active zone's.
world/
└── Zone.fs              # CHANGED: the `ZoneContents` record and `captureZone`/`occupyZone`, beside the
                         #   `capture*`/`occupy*` family they are built from. No zone gains a field.
```
editor/
├── Editor.fs            # CHANGED: `undoStep`/`redoStep` call `History.undoHistory`/`redoHistory`; the keys,
│                        #   the menu and the `idle` gate are unchanged.
├── Outliner.fs          # CHANGED: the Zones section's row click / *Del* / *Add Zone* commit a `ZoneMove` /
│                        #   `ZoneDelete` / `ZoneAdd`, so the step is recorded; the disabled gates are unchanged.
├── CastPanel.fs         # CHANGED: header only — the per-zone reason for the direct write no longer applies.
└── ZoneRegistryPanel.fs # CHANGED: header only — the same correction.
test/
└── GameplayStateTests.fs # CHANGED: the `UndoByZone` assertion goes; new cross-zone, add, delete and
                          #   whole-history cases join it.
docs/
├── EDITOR_DESIGN.md     # CHANGED: principle 4, D-010, D-011, D-022, D-023 restated for one history.
├── ARCHITECTURE.md      # CHANGED: "the undo history [is] held per zone" corrected; the `UndoByZone` node out.
├── PROJECT_STRUCTURE.md # CHANGED: `state/History.fs` listed for what it is.
└── ROADMAP.md           # CHANGED: only if it names the per-zone history.
specs/024-multiple-zones/ # UNCHANGED: a historical artefact. The decisions and requirements it records that this
                          #   feature supersedes are listed in this feature's research.md, Decision 10.
specs/{030,032,043,045}/  # UNCHANGED: likewise. Only the living docs move with the feature.
fsharp-game.fsproj        # CHANGED: `state/History.fs` after `state/GameplayState.fs`, before `editor/Editor.fs`.
```

**Structure Decision**: the new module goes in **`state/`**, beside the state and the editor state it works on,
because the history is the editor's behaviour over the gameplay state and neither a panel nor a system. It is
compiled *after* `state/GameplayState.fs` because a zone step adds, removes and switches a zone — the same
inversion `world/CueEngine.fs` already uses, and the `.fsproj` comment says so. **The `Command` type stays above
the state**, because `EditorState` must name `UndoStack`, which names `Command`, and `GameplayState` names
`EditorState`; only the runners move, which is what keeps the type-level cycle `specs/024` feared from existing.
`ZoneContents` and its `captureZone`/`occupyZone` go in **`world/Zone.fs`**, with the `capture*`/`occupy*` family
they are built from: a command's payload is defined where the thing it describes is (`ActorState`, `PropState`,
`Environment` in `world/Entity.fs`; `LightSettings` in `render/Texture.fs`), and `ZoneTemplate` — the same kind of
composite, recorded for a different purpose — is in `world/` too. `world/Zone.fs` is compiled before
`state/Command.fs`, so the `ZoneDelete` case can name the record. Nothing is added to `world/Systems.fs`, the
render path or the physics world, no zone gains a field, and no panel other than the Outliner's Zones section
changes where it writes.

## Phase 0: Research

See [research.md](./research.md). It settles one history at the state level and the removal of the side map (D1),
why recording the moves removes the need to tag a command with a zone (D2), why an add or a delete that changes
the active zone is one step (D3), what a deleted zone is retained as and rebuilt through (D4), where the command
runners live and why the type must not move (D5), what undoing a move does to the camera and the selection (D6),
that moves do not coalesce (D7), the play/edit boundary and the cue's seam (D8), what the headless suite asserts
(D9), and the terminology and the documents that move (D10).

## Phase 1: Design

- [data-model.md](./data-model.md) — the three new `Command` cases, the `ZoneContents` record and its
  `captureZone`/`occupyZone` (which live in `world/Zone.fs`, and why the contents are not `Zone` and not
  `ZoneTemplate`), `state/History.fs`'s signatures, the `UndoByZone` removal, the per-command `apply`/`undo`
  table, and the validation rules.
- [contracts/undo-history.md](./contracts/undo-history.md) — every surface that changes (`state/Command.fs`, the
  new `state/History.fs`, `state/GameplayState.fs`, `state/EditorState.fs`, `world/Zone.fs`, `editor/Editor.fs`,
  `editor/Outliner.fs`, the two panel headers, the `.fsproj`, the test file, and the living docs) and what
  explicitly does not (the per-kind command family, `world/Systems.fs`, the other editor files, the cue's history,
  the render and physics paths, and the prior specs, which are historical artefacts and are not edited).
- [quickstart.md](./quickstart.md) — runnable validation: the headless cases, and an editor walkthrough of
  cross-zone undo, an add, a delete and a bare move.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one short module, three cases, one record; a dictionary, a swap and two reasons-for-exception deleted. No new dependency, panel or `world/` module. |
| II. Fail Loudly | Pass — the guards are unchanged and still raise; the undo path cannot name a zone a later step removed, because that step is undone first. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — a DU of values, no closures, `EntityRef` identity preserved through a round trip. |
| V. Minimise GC Pressure | Pass — nothing on a frame path; a step's allocations are its payloads, taken when the step is taken. |
| N-V. Testing is the Authoring Tool | Adopted in substance — D9 makes the whole feature assertable headlessly, including the refs a delete restores. |
