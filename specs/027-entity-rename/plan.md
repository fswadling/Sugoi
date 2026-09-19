# Implementation Plan: Entity Rename

**Branch**: `027-entity-rename` | **Date**: 2026-09-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/027-entity-rename/spec.md`

## Summary

Make the Inspector's name row an editable field for every entity kind it can show
(actors, props, environment parts, waypoints). The commit path is small and rides
the existing command machinery: a new `Rename` undo command that carries the name
before and after, a `Zone.setName` that writes the name into the addressed
entity's base across the four kinds (mirroring `setTransform`'s dispatch, with no
physics work), and a `Selection.renameRef` that applies and pushes the command,
mirroring `deleteRef`. The Inspector keeps one reused text buffer, re-seeded from
the selected entity each frame, and commits when the field is submitted (Enter) or
left after an edit (click away) — never mid-typing. One new P/Invoke
(`igIsItemDeactivatedAfterEdit`, already exported by the vendored library) and two
input-text flag constants provide the commit signal; names are deliberately not
unique, because refs, not names, address entities.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`)

**Primary Dependencies**: custom cimgui bindings (`render/ImGui.fs` —
`igInputText` already bound; `igIsItemDeactivatedAfterEdit` and
`ImGuiInputTextFlags_EnterReturnsTrue`/`AutoSelectAll` are already exported by the
vendored `native/libcimgui.so`, verified with `nm`, so no native rebuild). Nothing
else: the change touches no rendering, physics, or assets code.

**Storage**: N/A — in-memory scene data; persistence is a later phase

**Testing**: xUnit via `dotnet test test/fsharp-game.Tests.fsproj`. The rename's
data path is headless-testable through `Zone.setName`, `Selection.renameRef`,
`Command`, and `UndoStack`; the field's focus/commit interaction is not (it needs
an ImGui frame), and is covered by the quickstart's interactive walk.

**Target Platform**: Linux desktop, SDL3 + SDL_gpu, software 60 FPS cap

**Project Type**: single-project desktop game with a docked editor

**Performance Goals**: hold 60 FPS; the field reuses one buffer and seeding it
allocates nothing, so no new per-frame allocation is introduced into the panel

**Constraints**: one reused buffer serves every selected entity, so a pending edit
must be attributable to the entity it was typed for; the field must not let typing
reach the editor's keyboard shortcuts; `Entity.Name` stays immutable (the payload
is reassigned, as `setTransform` already does)

**Scale/Scope**: one field in one panel; tens of renames per session

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one undo command case, one zone operation, one selection operation, one text field reusing the existing binding, one new P/Invoke over an already-exported symbol. No new module, file, type, or dependency, and no dialog. |
| II. Fail Loudly | Pass — an empty name raises in the renaming operation as a precondition (the UI never calls it with one, matching how the zones feature refuses an empty zone name), while a rename addressed to a dead ref is a documented no-op like `setTransform`. |
| III. Spec-First | Pass — `specs/027-entity-rename/spec.md` is approved and drives this plan. |
| IV. Data-Oriented Architecture | Pass — a name stays a `string` field on the shared `Entity` struct; the command carries two strings on the cold undo path; no new entity type or allocation pattern is introduced. |
| V. Minimise GC Pressure | Pass — the Inspector is a cold path and reuses one buffer; the seed encodes straight into it with no intermediate array, and a rename allocates only the two strings the command holds. |

No violations; the Complexity Tracking table is empty.

## Project Structure

### Documentation (this feature)

```text
specs/027-entity-rename/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── entity-rename.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
state/
├── Command.fs           # + Rename command (before/after names); apply/undo dispatch
├── EditorState.fs       # + the reused name buffer, its owner ref, and the buffer-size literals
└── (unchanged: GameplayState.fs)

world/
└── Zone.fs              # + setName (ref dispatch over Actor/Prop/Part/Waypoint); no physics

editor/
├── Selection.fs         # + renameTarget (pure commit decision) and renameRef (apply + push)
├── Inspector.fs         # the name row becomes a text field; seed, commit, gate, attribute
└── Editor.fs            # Inspector.draw now takes shared (one call site)

render/
└── ImGui.fs             # + isItemDeactivatedAfterEdit P/Invoke; + EnterReturnsTrue / AutoSelectAll flags

test/
├── ZoneTests.fs         # setName across kinds; stale ref is a no-op; nothing else changes
└── SelectionTests.fs    # NEW: renameRef pushes one Rename step, undo/redo, empty raises, no-op cases
```

**Structure Decision**: single project, existing domain clusters, and one new
**test** file (`test/SelectionTests.fs`) — registered in
`test/fsharp-game.Tests.fsproj` — because `Selection` has no test module yet and
this feature adds a third entry point to it (alongside delete and duplicate).
No source file is added. The rename is split exactly as the existing edits are:
the *data* change is a `Zone` operation, the *undo* is a `Command`, and the
*interaction* is a `Selection` entry point, so the Inspector stays a UI surface
that reads a field and calls one operation, as the Outliner does for
delete/duplicate.

## Phase 0: Research

See [research.md](./research.md). It decides the command shape, where the name is
written and why `Entity.Name` stays immutable, the empty-name policy and where it
is enforced, the shared-buffer interaction and how a pending edit stays
attributed to its own entity (with the ImGui invariant that makes re-seeding every
frame safe), the commit signals, and why names stay non-unique.

## Phase 1: Design

- [data-model.md](./data-model.md) — the `Rename` command, `Zone.setName`, the
  `EditorState` field state, the Inspector's commit rule, the state transitions,
  and the validation rules.
- [contracts/entity-rename.md](./contracts/entity-rename.md) — the public surfaces
  added or changed (`Command`, `Zone`, `Selection`, `EditorState`, `ImGui`, the
  Inspector).
- [quickstart.md](./quickstart.md) — runnable validation scenarios.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — the interaction is one field and one commit rule; no dialog, no modal, no new file in the app. |
| II. Fail Loudly | Pass — empty raises as a precondition; a dead ref is a documented no-op consistent with the other `Zone` writes. |
| III. Spec-First | Pass. |
| IV. Data-Oriented | Pass — unchanged entity shape; command holds two strings. |
| V. Minimise GC | Pass — one reused buffer, encoded into directly; no per-frame allocation added. |
