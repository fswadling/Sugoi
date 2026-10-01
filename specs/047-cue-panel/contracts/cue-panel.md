# Contract: Cue Panel

The surfaces this feature adds or changes, and what explicitly does not change.

## `editor/CueTree.fs` — NEW

```fsharp
[<Struct>]
type Row = { Path : int list; Depth : int; Label : string; Locked : bool }

// ── pure: what the cue looks like, and what it lets you change ──
val rows                 : Cue -> Row list
val executing            : Cue -> bool
val canAppend            : path: int list -> cue: Cue -> bool
val children             : Cue -> Cue list
val rowId                : idPrefix: string -> path: int list -> string
val tryAt                : path: int list -> cue: Cue -> Cue option
val withAt               : path: int list -> replacement: Cue -> cue: Cue -> Cue
val withoutAt            : path: int list -> cue: Cue -> Cue
val withAppendedChild    : path: int list -> child: Cue -> cue: Cue -> Cue * int list
val textOf               : Cue -> string voption
val withText             : cue: Cue -> text: string -> Cue

// ── ImGui: the thin drawing ──
val drawTree   : idPrefix: string -> selection: int list option -> cue: Cue -> int list option
val drawEditor : idPrefix: string -> cue: Cue -> selection: int list
                 -> commit: (Cue -> unit) -> int list option
```

**Contract**: the one shared control (FR-006). It takes **the cue, a caller-supplied id prefix, the caller's
selection and its commit callback, and nothing else** — it names no `GameplayState`, `EditorState`, `Command`, a
zone or an asset, so a trigger's cue (phase 10) and a scenario's cue (phase 11) can draw it. Its labels, its
child structure and **which nodes are read-only** (FR-011's sense: not retuned, replaced or removed) come from the pure half and are asserted headlessly (FR-007,
SC-004); only the widgets need a device. A row per container, indented, with a leaf in flight labelled as the op
it came from (FR-002, FR-004). A long cue scrolls in the window rather than being cut (FR-009): the rows go straight into the window, with no `beginChild` and no cap. Everything a path addresses (`withAt` / `withoutAt` / `withAppendedChild`)
**raises** when the path names no node; `tryAt` is the harmless question (Principle II). `drawEditor` hands its
`commit` the **next cue** outright — the control owns the rebuild, because removing a node changes its parent —
and `textOf` / `withText` are the two halves of the text field whose *buffer* belongs to the caller, so no
editor state is named here either.

## `editor/CueEditor.fs` — NEW

```fsharp
val draw     : state: byref<GameplayState.GameplayState> -> unit
val commit   : state: byref<GameplayState.GameplayState> -> next: Cue -> unit
val undo     : state: byref<GameplayState.GameplayState> -> unit
val redo     : state: byref<GameplayState.GameplayState> -> unit
val canUndo  : state: GameplayState.GameplayState -> bool
val canRedo  : state: GameplayState.GameplayState -> bool
```

**Contract**: the panel docks as a tab in the right column beside the Inspector (FR-001) through the native
layout (below), reads
**the screen's own cue** — the value that is executing, not a copy (FR-003) — and writes an accepted edit back
to `state.Cue` **directly**, with one step on the cue's own history, never through a `Command` (D-011): a cue
spans zones, so it cannot ride a zone's stack, and it is immutable, so a history step is one value in a list,
not a copy (research D5). It is shown while editing and not while playing (FR-008), so what it shows is the cue frozen at
the moment the editor was opened (spec 046). It takes only `state`: nothing it needs is in `SharedState` — the
zone name it edits is typed, not chosen, because a dropdown would make the control read the zone registry
(research D7). Its Undo/Redo controls are its own, because Ctrl+Z is the zone stack's.

## `state/EditorState.fs` — CHANGED

```fsharp
type CueHistory = { Applied : Cue.Cue list; Redo : Cue.Cue list }   // NEW, with create/push/undo/redo

// five NEW fields on EditorState
CueHistory     : CueHistory
CueSelection   : int list option
CueTextBuffer  : byte[]
CueTextPath    : int list option
CueTextEditing : bool
```

**Contract**: transient editor state, beside the `UndoStack` the zone edits use — which is why the type is
here, before `EditorState` can name it, as `UndoStack` is in `state/Command.fs`. The history holds the cue value
*before* each accepted edit; it is dropped when play begins (FR-013). The selection is the panel's own and is
never written into a cue (spec's Key Entities). The text buffer belongs to the node `CueTextPath` names — not
always the selected one — and is re-seeded only while the field is not live, the Inspector's rule.

## `editor/Editor.fs` — CHANGED

```fsharp
let private panelNames = [| "Outliner"; "Inspector"; "Asset Browser"; "Cast"; "Zone Registry"; "Dialogue"; "Cue" |]
```

plus `do CueEditor.draw &state` in `draw`, and the cue's history dropped on the edit → play toggle.

**Contract**: the panel is reachable by the panel-cycling shortcut (FR-001), is drawn with the rest of the
workspace and so appears again on return to editing (FR-008), and the only moment the cue's history is dropped
is the one transition into play (FR-013).

## `native/cimgui_sdl3.cpp` — CHANGED

One line, in `cimgui_setup_dock_layout`:

```cpp
// The cue panel (spec 047) is the one sub-editor that does not belong here: it docks beside the
// Inspector on the right, because what it edits is a cue rather than a place in the scene.
ImGui::DockBuilderDockWindow("Cue", right);
```

**Contract**: the default dock layout is built in native code, so FR-001's "docked in the right column as a tab
beside the Inspector" is a one-line native change and a `bash native/build.sh` rebuild. `libcimgui.so` is
git-ignored build output (`native/.gitignore`), so no binary is committed; the build needs `g++` and the
vendored cimgui source already in the tree, and neither network nor `cmake`.

## `fsharp-game.fsproj` — CHANGED

Two `Compile` entries: `editor/CueTree.fs` then `editor/CueEditor.fs`, both **before** `editor/Editor.fs` (which
calls the panel) and after `state/` and `world/CueEngine.fs`.

**Contract**: F# compiles in list order. `CueTree` precedes the panel that calls it; `CueEditor` precedes the
workspace that draws it; and both follow `state/EditorState.fs` (which names `Cue.Cue` for the history) — which
`world/Cue.fs` already precedes.

## `test/CuePanelTests.fs` — NEW

```fsharp
// added to test/fsharp-game.Tests.fsproj, after CueTests.fs
```

**Contract**: the control's pure half and the panel's rules, asserted with no device — the rows (container rows,
indentation — including a `Sequence` inside a `Sequence` and a `Parallel`'s siblings — the queue's order, and the
whole label vocabulary, with a `WaitState` labelled as its `Wait`), the executing rule over hand-built cues
(nothing locked in a fresh authored cue; the running leaf and the containers above it not retuned, replaced or
removed, and the queue behind editable; both `Parallel` branches locked), the addition rule beside it (a running
`Sequence` takes a child, and a leaf, a `Parallel` and a path that names nothing do not), the path helpers' round
trips and their raises, the history's own steps, and **the panel's write path where the pieces meet** —
`CueEditor.commit` writing the screen's cue once and `undo`/`redo` walking that step against a headless
`GameplayState` (SC-006, SC-007, SC-008).

## `docs/` — CHANGED

| File | Change |
|---|---|
| `ROADMAP.md` | phase 9's editor deliverable marked complete and its "done when" met; the next-step line advances to phase 10 |
| `PROJECT_STRUCTURE.md` | `editor/CueEditor.fs` and `editor/CueTree.fs` lose their "(planned)" and say what they are |
| `EDITOR_DESIGN.md` | D-010: the cue's history is per editing session, dropped when play begins; D-013: the row-per-container shape, the running path that takes an addition but not a replacement, and reorder out of this cut; D-011's "cue root" note brought up to date |

## Explicitly unchanged

- **`world/Cue.fs`** — the cue is not widened: no new case, no field, no id. The panel reads and writes cue
  *values*.
- **`world/CueEngine.fs`** — `advance` / `tick` / `forkInto` and the play chain as they are. The panel writes
  `state.Cue` directly and the engine is untouched; an edit takes effect on the next play tick, exactly as
  setting a cue does.
- **`screen/GameplayScreen.fs`, `state/GameplayState.fs`** — no new field and no new line. `Cue` is already
  there and already the run.
- **`state/Command.fs`, `UndoStack`, the zone stacks** — the cue's history is not a command and does not touch
  them; Ctrl+Z keeps meaning the zone's.
- **`render/ImGui.fs`** — no new binding. The control uses what is there: `selectableBool`, `beginCombo` /
  `selectableBool` / `endCombo`, `dragFloat`, `inputText`, `isItemActive` / `isItemDeactivatedAfterEdit`,
  `button`, `beginDisabled`, `text`, `sameLine` (the reference's own row vocabulary).
- **The other panels** (`Outliner`, `Inspector`, `AssetBrowser`, `CastPanel`, `ZoneRegistryPanel`,
  `DialoguePanel`) — untouched, save for the one line `Editor.fs` adds to each panel's list membership.
- **The render path, physics, `SharedState`, assets, zones, actors** — untouched. The cue panel is drawn only
  while editing, when the play chain is stopped, so nothing on the 60 fps path changes and spec 046's measured
  0-byte idle tick cannot be affected.
- **The rest of the op family and the other cue surfaces** — no new op (research D7); trigger → cue wiring is
  phase 10, and the scenario's cue is phase 11, both of which draw this control rather than a new one.
