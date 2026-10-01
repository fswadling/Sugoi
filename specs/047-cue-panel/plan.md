# Implementation Plan: Cue Panel

**Branch**: `047-cue-panel` | **Date**: 2026-09-30 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/047-cue-panel/spec.md`

## Summary

The editor half of phase 9: the cue becomes **visible, and editable**, in the editor. Two new editor modules.
`editor/CueTree.fs` is the one shared control — a **pure** derivation of the cue's rows (labels, depth, path,
and which nodes are executing) plus the thin ImGui drawing over it, parameterised only by the cue, a caller id
prefix, the caller's selection and a commit callback. `editor/CueEditor.fs` is the panel: it reads
`state.Cue`, draws it through the control, and writes an accepted edit back with its own undo history.

Nothing in the run changes. The cue is still one immutable value that shrinks to `Fin`; the panel is a read of it
plus a write of a new value, and there is no `live`/`current` derivation, no copy kept in step, no new op and no
change to `world/Cue.fs`, `world/CueEngine.fs` or the play chain. What is new is small and local: two modules,
one history (two lists of cue values, because a cue is immutable), five fields on `EditorState`, one line in
`Editor.fs`, one line of native dock layout, and one test file.

Three decisions shape it, all settled in the spec's clarifications:

- **What is executing is not retuned, replaced or removed — and an addition is always taken.** A node is
  *executing* exactly when it is an ancestor-or-self of an in-flight leaf (`WaitState`, `MoveCameraState`). The
  rule is a function of the cue alone, so it is asserted headlessly. A fresh authored cue has no in-flight leaf and
  is therefore wholly editable; a cue part-way through a sequence has its spine protected, and the ops queued
  behind the running one are editable. A running `Sequence` still takes a child, though — an addition lands on its
  queue, whose tail is ahead of the running op — which is the one edit the running path accepts.
- **A row per container**, indented — the D-013 recursive tree list, and the reference's shape. A `Sequence`,
  `Parallel` and `Fork` each get their own row; `WaitState` is drawn as the `Wait` it came from.
- **The history is per editing session** — dropped the moment Play begins, so a step can never refer to a cue
  that has since advanced. Undo and redo are the panel's own controls, because Ctrl+Z is the zone stack's.

The panel draws only while editing, when the play chain is stopped, so **the 60 fps path pays nothing** for the
feature: the cue tree's rows and strings are ordinary editor work, in the cold path the constitution exempts.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`), `TreatWarningsAsErrors`-clean build

**Primary Dependencies**: Dear ImGui through the existing binding (`render/ImGui.fs`) and its vendored native
`libcimgui.so`; Prime for `FDeque` (**already** referenced — the cue needs it); SDL3. **No new package.** The
native library is rebuilt because the default dock layout lives in it (below)

**Storage**: none — the cue's history is in memory for the editing session and dropped on Play; nothing about a
cue or its history is persisted (phase 15)

**Testing**: xUnit via `dotnet test` (headless). The control's pure half is a function of the cue alone —
`rows` / the lock rule / the path helpers — so every acceptance scenario that is not a drawing is asserted
without a device. The ImGui drawing and the dock layout are the walkthrough, the same split the house already
uses (`DialogueRender`'s pure layout, `PromptRender`'s pure layout, `DialoguePanel`'s drawing)

**Target Platform**: Linux desktop (SDL_gpu/Vulkan)

**Project Type**: desktop game with an in-engine editor (single project plus its test project)

**Performance Goals**: **the game loop is unchanged, so the target is trivially met**: `CueEditor.draw` is
reached only from `Editor.draw`, which returns at once unless `state.Editing`, and the play chain does not run
while editing — so the cue tree's rows, labels and ImGui calls are wholly outside the 60 fps path. Nothing on
the play path is touched: no change to `CueEngine`, no new per-frame work, no new field read by the tick

**Constraints**: the constitution's five principles; **the control depends on the cue and the ImGui binding
only** — never on `GameplayState`, `Command`, `EditorState`, a zone or the asset manager, so a trigger's cue
(phase 10) and a scenario's cue (phase 11) can reuse it; the panel writes `state.Cue` directly (a direct write,
D-011) with its own history (D-010), never a `Command`; the cue is frozen while editing (spec 046), so the panel
shows an unchanging value and its selection can only be invalidated by the author's own edit; the default dock
layout is **native** (`native/cimgui_sdl3.cpp`), so one line there and a `native/build.sh` rebuild ride with the
feature

**Scale/Scope**: two new editor modules (~490 lines between them — the `MoveCamera` fields are the bulk of it),
one small type and five fields on
`EditorState`, **three `.fsproj` entries** (two in the app, one in the test project), **one line of native dock
layout plus a rebuild**, one new test file (21 tests), and
three doc amendments. No change to `world/Cue.fs`, `world/CueEngine.fs`, `screen/GameplayScreen.fs`, the play
chain, `state/Command.fs`, the undo stacks, or any zone/entity/asset module

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — two modules, both **already reserved** by `docs/PROJECT_STRUCTURE.md` and both named by the spec; no new package, no new command type, no new op, no new module in `world/` or `state/` (the history is *two lists* on `EditorState`). The tempting larger readings are each rejected with a reason: the history could be a `Command` family (it is not a zone edit, and a cue is immutable, so a snapshot costs one reference); the editor could own a reorder/add-family surface (D-013's *reorder* is explicitly out of this cut, in the spec's Assumptions); the control could take its zone names as a combo (that would make it depend on a zone, which FR-006 forbids). |
| II. Fail Loudly | Pass — the lock is enforced by construction: `CueTree` withholds the editing controls for a locked node, so there is no widget that *could* write one, and the panel reports the reason in words rather than drawing inert widgets with no explanation. A path that names no node — a stale selection, or a bug — **raises** (`CueTree`'s navigation, in the reference's own idiom) rather than silently editing the nearest thing. An unknown zone name typed into a `SwitchZone` still raises where it always did, in `GameplayState.switchZone`. |
| III. Spec-First | Pass — `specs/047-cue-panel/spec.md` is written, clarified (five questions recorded) and drives this plan. |
| IV. Data-Oriented Architecture | Pass — no inheritance, no object graph, no component registry. The cue stays one discriminated union and the run is still never mutated; the view is a `Row list` derived from the cue by a pure function, and the only mutation is the panel writing a new cue value where it belongs. A node's identity is a **path** of child indices because a cue value carries no ids — the alternative (threading ids through the model) would put editor bookkeeping inside the vocabulary. |
| V. Minimise GC Pressure | Pass, and the argument is structural rather than a measurement to be re-taken: the panel is reached only while `state.Editing`, when the play chain is stopped, so the rows, labels and ImGui strings it builds are **not** on the 60 fps path — the same cold path every other panel already allocates in. The play path is untouched, so a cue in flight still costs 0 bytes per idle tick (spec 046's measured property), and this feature cannot regress it. The row derivation is kept a plain list rather than contorted into a reused array for that reason (Principle V's "clarity first, then measure"). |
| N-V. Testing is the Authoring Tool | Not engaged as written (the narrative subsystem is untouched), but its requirement is adopted rather than dodged: the control's labels, child structure and lock state are a *pure* function of the cue (FR-007), so the cue becomes something a headless suite can read and author — which is the only way this project can assert a cue at all, and it is what the panel is drawn *from*. |

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

None. The feature adds no mechanism: it reads a value and writes a value, and everything it needs that is not
new already exists (the binding's widgets, the panel protocol, the seed/commit/`isItemDeactivatedAfterEdit`
discipline the Inspector established). The one piece of *new* machinery is the cue's own history, and it is two
`Cue list`s because a cue is immutable — smaller than the `Command` family it deliberately is not.

## Project Structure

### Documentation (this feature)

```text
specs/047-cue-panel/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── cue-panel.md     # Phase 1 output — the surfaces that change
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
editor/
├── CueTree.fs           # NEW: the one shared cue control. The pure half first — `rows` (labels, depth,
│                        #   path, locked), the executing rule it rests on, `canAppend` beside it, the path
│                        #   helpers (`tryAt`, `withAt`, `withoutAt`, `withAppendedChild`), `rowId` and the
│                        #   text pair (`textOf`/`withText`) — then the thin drawing over it (`drawTree`,
│                        #   `drawEditor`).
│                        #   Parameterised by the cue, an id prefix, the caller's selection and a commit
│                        #   callback, and by nothing else: it names no `GameplayState`, `EditorState`, zone
│                        #   or asset, so a trigger's and a scenario's cue can reuse it.
├── CueEditor.fs         # NEW: the panel. Opens the "Cue" window, reads `state.Cue`, draws it through
│                        #   `CueTree`, applies an accepted edit with the one write path (`state.Cue <- …`
│                        #   plus one step on the cue's own history), and offers Undo/Redo for that history.
├── Editor.fs            # CHANGED: `panelNames` gains "Cue" (so Ctrl+Tab reaches it); `draw` calls
│                        #   `CueEditor.draw`; the edit/play toggle drops the cue's history when Play begins
│                        #   (FR-013).
state/
└── EditorState.fs       # CHANGED: `CueHistory` (a type and its create/push/undo/redo, beside `UndoStack`'s
                         #   shape in `Command.fs`) and the panel's five fields — the history, the selection
                         #   path, the selected node's text buffer and the two flags that guard it.
test/
├── CuePanelTests.fs     # NEW: the control's pure half headlessly — the rows, the lock rule over hand-built
│                        #   cues, the path round trips, and the history's own steps.
└── fsharp-game.Tests.fsproj  # CHANGED: one Compile entry for it, after `CueTests.fs`.
native/
└── cimgui_sdl3.cpp      # CHANGED: one line — `DockBuilderDockWindow("Cue", right)` — a tab beside the
                         #   Inspector, the only sub-editor that does not dock in the left column, with the
                         #   comment the others carry. `libcimgui.so` is git-ignored build output, rebuilt with
                         #   `bash native/build.sh`.
docs/
├── ROADMAP.md           # CHANGED: phase 9's editor deliverable marked complete, its "done when" met, and the
│                        #   next-step line advanced to phase 10.
├── PROJECT_STRUCTURE.md # CHANGED: `editor/CueEditor.fs` and `editor/CueTree.fs` are no longer "(planned)".
└── EDITOR_DESIGN.md     # CHANGED: D-010 (the history is per editing session, dropped on Play), D-013 (the
                         #   row-per-container shape, the running path that takes an addition but not a
                         #   replacement, reorder out of this cut), and
                         #   D-011's "cue root" note, which said a cue stack would land when the cue root did.
fsharp-game.fsproj        # CHANGED: `editor/CueTree.fs` then `editor/CueEditor.fs`, both before
                          #   `editor/Editor.fs` (which calls the panel) and after `state/` and
                          #   `world/CueEngine.fs`.
```

**Structure Decision**: the two modules go in **`editor/`** for the reason the rule they follow was drawn for —
the cue's *other* view, `DialogueRender`, sits in `world/` because it draws into the game's own 2D layer, while
this one draws ImGui widgets and belongs with the panels. `docs/PROJECT_STRUCTURE.md` already reserves exactly
these two paths and `docs/EDITOR_DESIGN.md`'s workspace table already affords a "Cue" tab, so
neither module's *place* is a new decision. Its *dock* is the one thing this feature settles differently from that
first sketch: the panel goes beside the **Inspector** in the right column rather than into the left beside the
Outliner, because what it edits is a cue rather than a place in the scene — and the right column is where the
editing surfaces are, which is also the column the trigger's cue (phase 10) will be drawn in. The split inside
`CueTree.fs` — a pure derivation, then a thin
drawing over it — is the house split (`DialogueRender`, `PromptRender`) and it is what makes FR-007 and SC-004
reachable: everything a reader of the panel is *told* is a function of the cue, and only the widgets need a
device. The default dock layout is native (ImGui's `DockBuilder` is not exposed through the binding), so the
feature's one line there is a real dependency of the spec's FR-001, stated rather than discovered at
implementation time: `native/cimgui_sdl3.cpp` already documents each panel's dock by hand, and `libcimgui.so` is
git-ignored output rebuilt by `native/build.sh` (verified working in this environment, with `g++` and the
vendored cimgui source already present). Nothing is added to `world/`, `state/Command.fs` or the play chain.

## Phase 0: Research

See [research.md](./research.md). It settles where the control lives and what is pure (D1), the row derivation
and the executing rule the lock rests on (D2), the path as a node's identity and what that means for a stale
selection (D3), the commit discipline that makes one gesture one undo step (D4), the cue's own history and why
it is two lists of references (D5), the native dock line (D6), what is edited per kind and what this cut leaves
out (D7), what the headless suite can assert (D8), and the documents that move with the feature (D9).

## Phase 1: Design

- [data-model.md](./data-model.md) — the `Row` the control derives, the executing rule, the path helpers, the
  panel's write path, the `CueHistory` type and its steps, the five fields on `EditorState`, and the label
  vocabulary with its validation rules.
- [contracts/cue-panel.md](./contracts/cue-panel.md) — every surface that changes (`editor/CueTree.fs`,
  `editor/CueEditor.fs`, `editor/Editor.fs`, `state/EditorState.fs`, the `.fsproj`, the native dock line, the
  test file, three docs) and what explicitly does not (`world/Cue.fs`, `world/CueEngine.fs`, the play chain,
  `state/Command.fs` and the undo stacks, the other panels, the render path).
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite, and a walkthrough that sets a cue
  in code, plays it, opens the editor and reads the panel shrinking to `Fin`, then edits and undoes.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — two reserved modules; one small type and five fields on `EditorState`; one native line. The history is two lists of references; there is no command type, no new op, no new module outside `editor/`. |
| II. Fail Loudly | Pass — the lock withholds the widgets rather than guarding them, an unknown path raises, and an unknown zone still raises in `switchZone`; the panel says *why* a node cannot be changed instead of drawing a mute one. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — one union, one pure `Row list`, paths for identity; no inheritance and no editor bookkeeping written into the cue. |
| V. Minimise GC Pressure | Pass, structurally: the panel is outside the play path, so it neither adds nor can regress a per-frame cost; spec 046's 0-byte idle tick is untouched by this feature. |
| N-V. Testing is the Authoring Tool | Adopted in substance — D2 and D8 exist so that what the panel shows, and which node it will let you change, are asserted with no device. |
