# Data Model: Cue Panel

Nothing here is a change to the cue. The cue is spec 046's one type, unchanged; what this feature adds is a view
of it, the paths that address it, and the history that takes an edit back. So the model below is the *editor's*:
what a row is, what a path is, where the selection and the history live, and the label vocabulary.

## `editor/CueTree.fs` — new

### The rows (pure)

```fsharp
/// One row of the panel: where the node is, how far it is indented, what it says, and whether it may be
/// changed (FR-011). A struct because a panel draws rows in a row; the `Path` is a list because a cue value
/// carries no ids and position is the only identity it has (research D3).
[<Struct>]
type Row =
    { Path : int list
      Depth : int
      Label : string
      Locked : bool }

/// Every row of the cue as the panel shows it, in display order: a row per container with its children
/// indented beneath it, the queue behind a running op listed in its own order, and `Fin` its own row.
/// The one thing the author is *told*, so it is a function of the cue alone and is asserted headlessly
/// (FR-002, FR-007).
val rows : Cue -> Row list

/// Whether this node is executing — an ancestor-or-self of a leaf that is in flight — which is when the editor
/// will not retune, replace or remove it (FR-011). Read off the value: `CueEngine` gives a run state to
/// `WaitState` and `MoveCameraState` only, so everything else is either structure or an op that has not begun.
val executing : Cue -> bool

/// Whether the editor offers an addition at a path. True for any `Sequence`, running or not: an addition lands
/// on a queue, and a queue's tail is ahead of the running op — the one operation the running path does not refuse
/// (FR-011). The pure half of that rule, so it is asserted headlessly rather than read off the drawing.
val canAppend : path: int list -> cue: Cue -> bool

/// The children of a node, in the index order a `Path` addresses them by. `[]` for a leaf or `Fin`.
val children : Cue -> Cue list

/// The ImGui id of a row: the caller's prefix and the node's path, so two cues drawn in one window
/// cannot collide and the same node keeps its id from frame to frame (FR-006). Pure, so the one
/// property of the sharing that is otherwise only inspectable is asserted headlessly too.
val rowId : idPrefix: string -> path: int list -> string
```

**The label vocabulary** — what each row says. It names the op, never an internal state, so a leaf in flight
reads as the op it came from (FR-004):

| Node | Label |
|---|---|
| `Fin` | `Fin` |
| `Sequence (current, rest)` | `Sequence` |
| `Parallel (left, right)` | `Parallel` |
| `Fork inner` | `Fork` |
| `Wait seconds` / `WaitState (seconds, _)` | `Wait 2` — the seconds, `%g`-formatted, so the in-flight form reads as its authored form |
| `Exposit text` | `Exposit: hello` |
| `SwitchZone zone` | `Zone: classroom` |
| `MoveCamera (target, duration)` / `MoveCameraState (…)` | `Move camera` |

**The child order a `Path` addresses**, one rule per constructor:

| Case | Children, in index order |
|---|---|
| `Sequence (current, rest)` | `0` = `current`, then `1 … n` = the queue, front first |
| `Parallel (left, right)` | `0` = `left`, `1` = `right` |
| `Fork inner` | `0` = `inner` |
| `Fin`, and every leaf | none |

### Navigation and the write path (pure)

```fsharp
/// The node a path names, or `None` when it names nothing (a stale selection, or a bug).
val tryAt : path: int list -> cue: Cue -> Cue option

/// The cue with the node at `path` replaced by `replacement`. Rebuilds only the spine from the root to
/// it. Raises when the path names no node: a write to a node that is not there is a bug, not a state
/// (Principle II).
val withAt : path: int list -> replacement: Cue -> cue: Cue -> Cue

/// The cue with the node at `path` removed. `[]` (the root) has no parent to remove it from: raises.
val withoutAt : path: int list -> cue: Cue -> Cue

/// The node at `path` with `child` appended to its queue, and the path of the child just added — so an
/// "Add Child" hands the caller a selection that follows the new node rather than being cleared, and the
/// two cannot disagree (research D3). Raises when `path` names no `Sequence`.
val withAppendedChild : path: int list -> child: Cue -> cue: Cue -> Cue * int list
```

- **Every edit is `withAt`.** Replacing a node's value, appending a child (which is `withAt` on the
  `Sequence`, with `rest` one longer) and removing a node (the *parent* is written, one child shorter) all go
  through the same two primitives, so there is one place a path can be wrong and one place it raises.
  `withAppendedChild` returns the rebuilt node **and** the added child's path, so the caller never has to work
  the new index out for itself — the two cannot disagree about where the child landed.
- **`withoutAt` on the root raises** rather than doing something arbitrary: the root is `state.Cue`, and
  "remove the cue" is not an edit the panel offers — a root the author wants gone is emptied by its children or
  replaced by its kind dropdown.
- **`withAt` raises when the path names nothing**, and `tryAt` is how a caller asks the harmless question
  first. That split is the reference's (`Cue.get_at` beside its commit callback) and it is what makes FR-005's
  stale-selection case a cleared selection rather than a wrong write.

### The control's two drawing halves (ImGui only)

```fsharp
/// Draw the cue as selectable rows, indented by `Depth`, and return the path of the row clicked this
/// frame, if any. `idPrefix` disambiguates two cues drawn in one window: the row's label carries it after
/// a `##`, so ImGui keys the widgets apart without a shared id stack (FR-006).
val drawTree : idPrefix: string -> selection: int list option -> cue: Cue -> int list option

/// Draw the property editor for the node at `selection`, committing each accepted change through `commit`,
/// which is handed the **next cue** outright — the control owns the rebuild (replacing a node, appending
/// to a queue, dropping one), because removing a node changes its *parent* rather than the node itself.
///
/// Returns the selection the caller should hold afterwards: `selection` itself when nothing structural
/// happened, the added child's path after an "Add Child", and `None` when the selected node is gone. A node on
/// the running path is offered less rather than guarded: no kind, no numbers and no removal, and a running node
/// that is not a `Sequence` says why in one line (FR-011).
val drawEditor :
    idPrefix: string
    -> cue: Cue
    -> selection: int list
    -> commit: (Cue -> unit)
    -> int list option

/// The node's editable text, or `ValueNone` for every kind that carries none, and the node with its text
/// replaced. The field that edits it belongs to the caller — a buffer is a widget's scaffolding, not the
/// cue's — so the control names no editor state in either direction. The text-bearing kinds (`Exposit`,
/// `SwitchZone`) are never executing, so a caller drawing the field needs no lock check of its own.
val textOf : Cue -> string voption
val withText : cue: Cue -> text: string -> Cue
```

- **The control names no `GameplayState`, `EditorState`, `Command`, zone or asset** — it takes a cue, an id
  prefix, the caller's selection and its commit callback, and nothing else (FR-006). That is what lets a
  trigger's cue (phase 10) and a scenario's cue (phase 11) draw it without a panel.
- **A locked node is selectable and readable** — it is drawn as an ordinary row, so the author can click it and
  read it — and what its *editor* withholds is the change, not the reading: no kind, no numbers, no removal. A
  running node that is not a `Sequence` says so in one line rather than drawing nothing. That is FR-011 drawn
  rather than enforced by disabling rows, which would take the reading away too.

## `editor/CueEditor.fs` — new

```fsharp
/// The Cue panel: the screen's cue, drawn through the control, with its own undo history.
/// Takes only the state — the control is the cue's, and a zone name is typed rather than chosen
/// (research D7), so there is nothing in `SharedState` it needs.
val draw : state: byref<GameplayState.GameplayState> -> unit

/// The panel's one write path: the **next cue** written in place, one step on the cue's own history, and the
/// redo side cleared. The control only hands this a cue it has already decided is a change (research D4).
val commit : state: byref<GameplayState.GameplayState> -> next: Cue -> unit

/// The cue's own history, taken back and put forward. Both are no-ops with nothing on their side.
val undo : state: byref<GameplayState.GameplayState> -> unit
val redo : state: byref<GameplayState.GameplayState> -> unit
val canUndo : state: GameplayState.GameplayState -> bool
val canRedo : state: GameplayState.GameplayState -> bool
```

- **The panel writes `state.Cue` directly**, not through a `Command`: the cue is neither zone-scoped nor an
  entity, so it cannot ride a zone's stack (D-011), and its own history is two lists on `EditorState`
  (research D5). This is the same direct-write shape the cast binding, the zone registry and the Dialogue panel
  already have.
- **One gesture, one step**: `commit` pushes the value the cue had *before* the write. Whether an edit
  *counted* is the control's question, decided by comparing the widget's value with the node's (research D4), so
  this never has a second opinion and never records a step the author would not recognise.
- **The panel draws the tree first, then the editor**, so an edit accepted this frame is visible in the tree
  next frame — the same one-frame lag `DialoguePanel`'s write has, and invisible in practice because the tree
  above the click does not move.
- **The panel stages the write.** F# forbids capturing a byref in a closure, and the control draws through a
  caller-supplied callback, so `CueEditor.draw` collects the next cue into a mutable local and applies it once
  the control has returned — one place, and one step.

## `state/EditorState.fs` — changed

The cue's history, beside the `UndoStack` the zone edits use:

```fsharp
/// The cue's own undo history (spec 047, D-010). Snapshots of the cue value *before* each accepted edit —
/// a cue is immutable, so a snapshot is just the value, not a copy — dropped when play begins, because the cue
/// a step refers to is about to advance. The editor's, not a zone's: a cue spans zones.
type CueHistory =
    { Applied : Cue.Cue list
      Redo : Cue.Cue list }

module CueHistory =
    val create : unit -> CueHistory
    val push : before: Cue.Cue -> history: CueHistory -> CueHistory
    val undo : current: Cue.Cue -> history: CueHistory -> Cue.Cue * CueHistory
    val redo : current: Cue.Cue -> history: CueHistory -> Cue.Cue * CueHistory
```

and five fields on `EditorState`, following the shape its text-field fields already have:

```fsharp
CueHistory : CueHistory            // the cue's own undo history (dropped when play begins)
CueSelection : int list option     // which node is highlighted — a path, and the panel's own, never the cue's
CueTextBuffer : byte[]             // the selected node's text: an `Exposit`'s line, a `SwitchZone`'s zone
CueTextPath : int list option      // the node that text belongs to — not always the selected one
CueTextEditing : bool              // whether the field was live as of the last draw
```

- **`CueTextPath` exists for the reason `NameEditRef` and `PromptEditRef` do**: the selection can change while
  a field is live, and the commit must go to the node the text was typed for, never to whatever is selected
  now. The buffer is re-seeded — and the path re-pointed — only while the field is not live, so an edit in
  progress is never overwritten.
- **A buffer is reused** (`byte[]`, seeded in place) rather than a fresh string per frame, the same reason the
  Inspector's fields are: typing allocates nothing.
- **The selection is the panel's own state and is never written into the cue** (spec's Key Entities): nothing
  about which row is highlighted travels with a cue, so a cue handed to a trigger's cue or a scenario's cue
  carries no view state with it.
- **`CueHistory` is dropped when play begins**, which is the edit/play toggle in `editor/Editor.fs` — the one
  place the transition is known.

## `editor/Editor.fs` — changed

```fsharp
let private panelNames = [| "Outliner"; "Inspector"; "Asset Browser"; "Cast"; "Zone Registry"; "Dialogue"; "Cue" |]
```

- the panel joins the Ctrl+Tab cycle (FR-001);
- `draw` calls `CueEditor.draw &state` with the other panels, so the workspace draws it (FR-008);
- the backtick toggle drops the cue's history on the way **into** play:

```fsharp
if Input.isPressed Input.Key.Backtick frame.Input && not (ImGui.wantTextInput()) then
    state.Editing <- not state.Editing
    // Play begins: the cue's own history goes with it (FR-013) — the cue a step refers to is about to
    // advance, so a step could no longer be applied cleanly.
    if not state.Editing then
        state.Editor <- { state.Editor with CueHistory = CueHistory.create () }
```

## `native/cimgui_sdl3.cpp` — changed

One line in `cimgui_setup_dock_layout`, and the one dock of this feature — every other sub-editor is left-column:

```cpp
// The cue panel (spec 047) is the one sub-editor that does not belong here: it docks beside the
// Inspector on the right, because what it edits is a cue rather than a place in the scene.
ImGui::DockBuilderDockWindow("Cue", right);
```

## State transitions

| From | Event | To |
|---|---|---|
| the editor is entered | the workspace is drawn | the panel shows `state.Cue` — the cue frozen as of the entry (spec 046) |
| a node is selected | a row is clicked | the selection is that node's path; the cue is untouched (FR-005) |
| the selection names no node | the cue is read | the selection is cleared (the edge case) |
| an editable node accepts an edit | the panel commits | `state.Cue` is the cue with that node replaced; one step is pushed on the cue's history |
| a node is added | the panel commits | the cue with the child appended; **the new child is selected** |
| the selected node is removed | the panel commits | the cue without it; **the selection is cleared** |
| Undo | the panel's Undo | `state.Cue` is the value before the last accepted edit; that value is on the redo side |
| Redo | the panel's Redo | the edit is put back; the value is on the applied side again |
| Play begins (the backtick toggle) | `Editor.update` | the cue's history is **empty**; the cue itself is untouched (FR-013) |
| Play is left (the toggle again) | `Editor.update` | the panel shows the cue as it now stands (SC-002) |

## Validation rules

| Rule | Where it is enforced |
|---|---|
| A locked node cannot be retuned, replaced or removed | `rows` marks it; `drawEditor` withholds the kind, the numbers and removal from it (FR-011) — enforced by construction, not by a guard |
| A `Sequence` always takes an addition | `canAppend` is true for any `Sequence`, running or not: an addition lands on a queue, whose tail is ahead of the running op |
| A path that names no node is a bug | `withAt` / `withoutAt` / `withAppendedChild` **raise**; `tryAt` is the harmless question (Principle II) |
| The root cannot be removed | `withoutAt []` raises — the panel offers removal only for a node with a parent |
| A kind change to `Sequence` starts it as `Sequence(Fin, [])` | an authoring default rather than a walk product: nothing else is both inert and non-`Fin` (an `Exposit ""` or a zero-length move completes at once and would collapse the sequence the author just made), and the engine drops a finished front and pulls the queue's next in the same step — so the new sequence is inert until a child is added, and runs as soon as one is |
| An edit that changes nothing is not a step | the control compares the widget's value with the node's before committing (research D4) |
| The history is empty while playing | it is dropped on the edit→play toggle; the panel draws no Undo/Redo while playing, because it draws nothing while playing (FR-008, FR-013) |
| A `SwitchZone` naming an unknown zone | unchanged: `GameplayState.switchZone` already raises with the zone's name |
| Two cues in one window cannot collide | the caller's id prefix is carried in every row's and control's ImGui id (FR-006) |

## Deliberately not modelled

- **Any change to the cue** — `world/Cue.fs` is untouched. The panel is a reader and a writer of *values*, not
  a widening of the vocabulary.
- **An authored cue beside the run** — there is none to keep, and none is added: the panel edits
  `state.Cue`, which is the run (spec 046). Editing what has already been run is not offered and not needed.
- **Node ids, or a component per node** — identity is a path (research D3).
- **Reorder, dragging between parents, and a `Parallel`'s arity** — out of this cut (research D7).
- **A command for a cue edit, and a project-scoped stack** — the history is two lists on `EditorState`
  (research D5); D-011's "project-scoped stack" is what this *is*, in the smallest form it can take.
- **Persistence of the cue, the selection or the history** — nothing is written to disk (phase 15).
