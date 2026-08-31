# Implementation Plan: Command / Undo

**Branch**: `008-command-undo` | **Date**: 2026-08-30 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/008-command-undo/spec.md`

## Summary

Add the command/undo core (phase 2d) to the editor: a value-storing `Command` type
with `apply`/`undo`, an undo/redo stack, `Ctrl+Z`/`Ctrl+Y` entry points, and an
Edit menu.
Spawn, delete, duplicate, and transform become undoable/redoable, and a continuous
gizmo drag or held nudge coalesces into a single undo step. The zone is mutated in
place (not copied) — this requires making `Zone` a mutable reference type first.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`)

**Primary Dependencies**: `System.Numerics` (`Vector3`/`Quaternion`), SDL3-CS (input),
the existing `ImGui`/`Input`/`SlotMap` bindings. No new packages.

**Storage**: in-memory only — the single active `Zone` held by `GameplayState`,
mutated in place.

**Testing**: xUnit for the pure command/undo/coalescing logic (`test/CommandTests.fs`);
the UI interaction is validated manually via `quickstart.md`.

**Target Platform**: Linux x64 desktop (native).

**Project Type**: native desktop game.

**Performance Goals**: 60 fps; the per-frame drag/nudge path writes the transform in
place and pushes nothing; commands are allocated only on discrete edits (cold path).

**Constraints**: `GameplayState` is a `[<Struct>]` holding a `Zone` reference; the
editor mutates the zone in place and threads the struct only for its own transient
state. Undo/redo yields to an in-progress drag and to ImGui text input.

**Scale/Scope**: one active zone, one selection, one tool; four command cases.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: four command cases, one stack, one merge rule. The
  generic `Field` command and Inspector field editing are deferred until a phase
  that edits properties.
- **II. Fail Loudly** — PASS: undo/redo rely on LIFO symmetry, so `removeEntity`
  always targets a live slot and `setEntity` a live-or-free slot; absent-entity
  transform no-ops are the expected (guarded) path.
- **III. Spec-First** — PASS: derives from `spec.md`.
- **IV. Data-Oriented** — PASS: in-place mutation of the zone's inline slot-map
  arrays; `Command`/`EntityRef` are value types; `UndoStack` is a small mutable
  record.
- **V. Minimise GC Pressure** — PASS: commands allocate only on discrete edits, not
  per frame; the drag/nudge path mutates the transform in place without pushing.

## Project Structure

### Documentation (this feature)

```text
specs/008-command-undo/
├── plan.md              # This file
├── research.md          # Phase 0 decisions
├── data-model.md        # Phase 1 data/behaviour model
├── quickstart.md        # Phase 1 validation guide
├── contracts/
│   └── command-undo.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
core/Zone.fs            # Zone becomes mutable: in-place setEntity/removeEntity/
                        # spawnActor/spawnProp (drop the "TEMPORARY" seam comment)
editor/Command.fs       # NEW: Command DU + apply/undo + UndoStack
editor/EditorState.fs   # + Undo : Command.UndoStack; + NudgeBefore : Transform option
editor/Selection.fs     # delete/duplicate build + push commands
editor/AssetBrowser.fs  # spawn builds + pushes a Spawn command
editor/Editor.fs        # finishDrag/updateNudge push Transform; Ctrl+Z/Ctrl+Y; Edit menu
screen/GameplayScreen.fs# (unchanged by this feature; Editor.update state frame already threaded)
fsharp-game.fsproj      # insert editor/Command.fs before editor/EditorState.fs
test/CommandTests.fs    # NEW: command/undo/coalescing tests
```

**Structure Decision**: entity CRUD (`entityAt`/`setEntity`/`removeEntity`/
`spawnActor`/`spawnProp`) stays in `core/Zone.fs` but becomes in-place; `Command.fs`
depends on `Zone` + `Entity` only (never `GameplayState`), so there is no module
cycle. `Command.fs` is inserted after `screen/SharedState.fs` and before
`editor/EditorState.fs` (which now references `Command.UndoStack`).

## Complexity Tracking

No constitution violations to justify.
