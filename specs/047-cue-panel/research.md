# Phase 0 Research: Cue Panel

The model is already settled (spec 046: the cue *is* its run, shrinking to `Fin`), and the panel adds no model.
So this phase is not inventing: it is where the *editor's* concerns meet the port's existing shape — the panel
protocol, the ImGui binding's actual widget list, the seed/commit discipline the Inspector established, and the
fact that the default dock layout is native. Several of the nine decisions below exist only because of one of
those.

## Decision 1 — The control lives in `editor/`, and its readable half is a pure function of the cue

- **Decision**: `editor/CueTree.fs` holds the whole shared control — a pure derivation (`rows`, the executing
  rule it rests on, and the path helpers) followed by the thin ImGui drawing over it (`drawTree`, `drawEditor`).
  `editor/CueEditor.fs` is the panel. Both names are already reserved by `docs/PROJECT_STRUCTURE.md`, and
  neither module names `GameplayState`, `EditorState`, `Command`, a zone or an asset.
- **Rationale**: this is the house split applied to the one place it was not yet applied. Every other view in
  this project separates what is *computed* from what is *drawn* — `DialogueRender` and `PromptRender` each put a
  pure layout function beside the drawing — and that split is the whole reason FR-007 and SC-004 are reachable:
  the panel's labels, its child structure and its lock state are a function of the cue alone, so a headless
  suite can assert what the author sees without a device, exactly as it asserts a cue's walk today.
- **Why not `world/`, where the other two views live**: `DialogueRender` and `PromptRender` are in `world/`
  because they draw into the **game's own 2D layer** — they are part of what the game shows a player. This
  control draws ImGui widgets in the editor workspace, which is a different audience and a different binding. A
  `world/` module that names ImGui would put the editor's widget toolkit in the game's vocabulary.
- **Why not a third module**: the spec names `editor/CueTree.fs` and `editor/CueEditor.fs` and
  `docs/PROJECT_STRUCTURE.md` reserves the same two; a `world/CueRender.fs` for the pure half would be a third
  place to look for the same feature. The pure half sits at the top of the module that owns the drawing.
- **Alternatives considered**: **the pure derivation in `world/Cue.fs`** (the model module) — rejected: a
  *view*'s row vocabulary is not the vocabulary, and spec 046's contract for that module is "the type", so this
  feature would widen a completed feature's module for the editor's convenience. **Paths and navigation in
  `world/Cue.fs`** (which the reference does — `Cue.get_at` / `Cue.remove_child`) — rejected for the same
  reason; the reference's `Cue` module was also its editor's, so it had nowhere else to put them.

## Decision 2 — The rows, and the rule that decides what is protected

- **Decision**: one pure function derives every row of the panel from the cue:

  ```fsharp
  [<Struct>]
  type Row =
      { Path : int list     // the node's position: [] is the root, otherwise child indices
        Depth : int         // indentation, 0 at the root
        Label : string      // what the row says (the vocabulary below)
        Locked : bool }     // on the running path: not retuned, replaced or removed (FR-011)
  ```

  and the flag it carries is decided by one rule:

  ```fsharp
  /// Whether this node is *executing* — an ancestor-or-self of a leaf that is in flight.
  let rec executing (cue: Cue) : bool =
      match cue with
      | WaitState _ | MoveCameraState _ -> true             // in flight, by definition
      | Sequence (current, _) -> executing current          // the front runs; the queue has not started
      | Parallel (l, r) -> executing l || executing r       // both sides run at once
      | Fork inner -> executing inner                       // its branch runs beside the remainder
      | Fin | Wait _ | Exposit _ | SwitchZone _ | MoveCamera _ -> false  // not begun
  ```

  A node is **locked** exactly when `executing` holds for it, and a locked node is one the editor will not
  retune, replace or remove. A `Sequence` gets a row of its own and its
  children indented beneath it; so do `Parallel` and `Fork`; every leaf gets a row.
- **Rationale**: the rule is a function of the cue **alone**, which is what makes FR-007's "which nodes are
  read-only" assertable headlessly rather than a property of the drawing. And it lands exactly where the
  engine's own state does: `world/CueEngine.fs` gives only two cues any run state of their own — `WaitState`,
  whose deadline is in its pocket, and `MoveCameraState` — and every other case is either structure
  (`Sequence`/`Parallel`/`Fork`) or an op that has not begun. So "read-only" is not a policy the editor
  invents; it is *executing*, read off the value.
- **The one edit the running path accepts, and why.** `executing` decides what may be *changed*; it does not
  decide what may be *added*. An addition never lands on the running op — it lands on a `Sequence`'s queue, and a
  queue's tail is ahead of the front op — so `canAppend` (a pure predicate beside `executing`) is true for any
  `Sequence`, running or not, and `drawEditor` withholds the kind, the numbers and removal from everything
  `executing` and offers the addition to everything `canAppend`. The first cut locked the whole running path,
  which made a running sequence unappendable; the author revised that after seeing it. The rule is therefore one
  line of policy rather than a second derivation: *what is executing is not retuned, replaced or removed; what is
  ahead of it is editable; and an addition is always ahead.*
- **A `Parallel`'s branches are both locked**: a parallel runs its sides at once, so neither is "behind" the
  other and neither is ahead of the running op. A `Fork`'s inner branch is locked too — its branch runs beside
  the remainder — while the remainder's queue behind it is editable, as the remainder is a `Sequence`.
- **Alternatives considered**: **locking only the in-flight leaf**, with the running sequence's kind and removal
  free too — rejected: replacing or removing the sequence that holds the running op discards it, which is a
  different act from appending ahead of it. **The whole running path locked**, which was the first cut — rejected
  by the author after implementation as over-restrictive: it left the panel unable to extend the very sequence the
  author was watching.
  **Marking a locked row visually** (a suffix, a different colour) — deferred: the panel reports the reason in
  words when the author selects a locked node, which says more than a glyph would, and the default font is
  ASCII so a lock symbol is not free.

## Decision 3 — A node's identity is a path, because a cue value carries no ids

- **Decision**: a node is addressed by a `int list` path of child indices, `[]` for the root, with one
  well-defined child order per constructor:

  | Case | Children, in index order |
  |---|---|
  | `Sequence (current, rest)` | `0` = `current`, then `1 … n` = the queue, front first |
  | `Parallel (left, right)` | `0` = `left`, `1` = `right` |
  | `Fork inner` | `0` = `inner` |
  | every leaf, and `Fin` | none |

  Navigation is three small pure functions over a cue — `tryAt path cue`, `withAt path replacement cue`,
  `withoutAt path cue` — which rebuild only the spine from the root to the node they touched.
- **Rationale**: the alternative is to put ids into the cue, and a cue is *authored* data — spec 046's model
  exists so that a cue value is the thing the author writes and the thing that runs, with nothing beside it. An
  editor-bookkeeping id inside it would be exactly the template/runtime split that model collapsed, in
  miniature. A path is derivable (the row derivation already walks in child order), costs nothing in the model,
  and is the reference's own choice for the same reason.
- **A stale selection, and the rule for it.** A path is *positional*, so a structural edit can move a sibling
  onto it and leave it naming a different node. FR-005's "the selection does not silently point at a different
  node" is therefore enforced by where the selection is put, not by hoping:
  - a **value** edit (kind, or the numbers a kind carries) never moves a sibling, so the selection stands;
  - **adding** a child selects the child that was added — the one path the panel knows exactly;
  - **removing** a node clears the selection;
  - and `tryAt` returning `None` (the node is gone) clears it too, which is the edge case's "renders without
    error".
- **Alternatives considered**: **a stable id per node**, generated when the cue is authored — a change to
  spec 046's type, and a field no author would write; and it would only help until the cue is rebuilt by the
  engine, which rebuilds the spine around the running op every time an op begins. **An `obj` identity** — cue
  values are immutable and *rebuilt* (a step returns a fresh spine), so object identity is not stable across a
  tick either.

## Decision 4 — One gesture is one undo step, decided by comparing the widget's value with the node's

- **Decision**: every editing control compares what it is showing against what the node holds, and commits only
  when they differ. A drag commits on `ImGui.isItemDeactivatedAfterEdit()` (the Inspector's own discipline, so
  a drag is one step, not one step per frame); a text field commits on the same or on submit (the Inspector's
  `NameBuffer` pattern, including re-seeding only while the field is not live); the kind dropdown, Add and
  Remove commit on the click, because a click is already one discrete act.
- **Rationale**: the alternative — the house's usual `if after = before then () else` — would need **structural
  equality over a whole cue**, which for a deep cue is an O(n) walk *and* drags in `FDeque` equality. Comparing
  the value the widget was seeded with against the value it now shows is O(1), needs nothing new from `Prime`,
  and is exactly the question that matters ("did this gesture change anything?"). It also keeps the cheap case
  cheap: clicking into a field and clicking out without typing commits nothing, so the history does not collect
  empty steps.
- **Alternatives considered**: **`=` on the cue** — rejected above (cost, and `FDeque` equality); **push
  unconditionally on deactivate** — rejected because it makes a no-op gesture an undo step, which is the kind
  of small wrongness that makes an author distrust undo; **push while dragging** — the binding's `dragFloat`
  returns true every frame of a drag, so this would be one step per frame.

## Decision 5 — The cue's own history is two lists of values, on `EditorState`, dropped when Play begins

- **Decision**: `state/EditorState.fs` gains

  ```fsharp
  type CueHistory = { Applied : Cue.Cue list; Redo : Cue.Cue list }
  ```

  holding the cue **as it was before** each accepted edit (and, on the redo side, the value to put back), with
  `create` / `push` / `undo` / `redo` beside it — the shape `state/Command.fs` already gives `UndoStack`. The
  panel owns the writes: an accepted edit is `state.Cue <- after` plus one `push before`. The history is
  **cleared when Play begins**, in `editor/Editor.fs`'s edit/play toggle.
- **Rationale**: a cue is immutable and shared, so "the value before the edit" is just another value in a
  list, not a copy — the cheapest history this project could have, and the reason none of the `Command` machinery
  is needed. It is not
  a zone edit and cannot ride a zone's stack (a cue spans zones), which D-010 and D-011 already say; this is
  the cue's own stack those notes call for, landing where they said it would. Dropping it on Play is what keeps
  every step applicable by construction: while editing, the cue is frozen (spec 046), so a step is always
  applicable; the moment the cue starts advancing, an older step would refer to ops that have already run, so
  the history goes rather than resurrecting them.
- **Why `EditorState` and not a new `state/` module**: it is transient editor state, it must be named before
  `EditorState` can hold it, and `EditorState` already names `Command` for the same reason (`UndoStack`). A
  module for two lists would be the Minimalism principle's own counter-example.
- **Invocation: Undo/Redo controls in the panel.** `Ctrl+Z` is already the zone stack's
  (`Editor.handleShortcuts` → `undoStep`), and making it mean "the cue's history" while the Cue panel has focus
  and "the zone's" otherwise would make one key mean two things depending on which panel was last clicked —
  the sort of ambiguity that is worse than a second control. The panel therefore offers its own Undo/Redo,
  disabled when there is nothing to take back, which is also the honest shape: the cue's history is the
  panel's.
- **Alternatives considered**: **a `CueEdit` command family** (`SetNode of path * before * after`, `AddChild`,
  `RemoveChild`) on a project-scoped `UndoStack` — rejected: a command exists to carry a *reversible* pair for
  a mutation of mutable state, and here the whole "before" state is one reference, so the family would be more
  types to express less. **Reusing `Command.UndoStack`** — impossible: its `Command`s are applied to a `Zone`.
  **A project-scoped stack on `GameplayState`** — rejected: it is not the cue's, it is the editor's, and the
  panel is its only writer.

## Decision 6 — The dock line is native, and the native library is rebuilt

- **Decision**: `native/cimgui_sdl3.cpp`'s `cimgui_setup_dock_layout` gains
  `ImGui::DockBuilderDockWindow("Cue", right);` — a tab beside the Inspector, and the one sub-editor that does
  not dock in the left column — with the comment the others carry. `libcimgui.so` is rebuilt with
  `bash native/build.sh`.
- **Rationale**: FR-001 requires the panel to be a docked tab, and ImGui's `DockBuilder` is not
  exposed through this project's binding — the *default* layout is built in native code, and that file already
  documents each panel's dock by hand ("Cast is a tab beside the Outliner, per docs/EDITOR_DESIGN.md's
  Workspace table"). So the line belongs there, and the build is a step this feature owns rather than a
  surprise. It goes to the **right** rather than the left, where the other sub-editors are, because what it
  edits is a cue rather than a place in the scene: the right column is the editing column, and it is where a
  trigger's cue will be drawn when phase 10 edits one inline in the Inspector. `libcimgui.so` is
  **git-ignored build output** (`native/.gitignore`), so the rebuild touches no
  committed binary; `native/build.sh` compiles the vendored cimgui source with `g++` and needs no network once
  the source is present. Verified in this environment: the script builds clean.
- **Alternatives considered**: **expose `igDockBuilderDockWindow` / `igSetNextWindowDockID` and dock from F#**
  — more binding surface, more F# code, and a dock decision moved away from the place the other five docks are
  written down; rejected as more machinery for the same result. **Leaving the panel undocked** (it would open
  as a floating window the author must place) — rejected: FR-001 is a docked tab, and the workspace table
  already says so.

## Decision 7 — What is edited per kind, and what this cut deliberately leaves out

- **Decision**: the property editor offers, for the **selected, unlocked** node:

  | Node | The controls |
  |---|---|
  | any | a **Kind** dropdown over the ten cases; choosing one replaces the node with that kind's default |
  | `Fin` | no fields |
  | `Wait` (or `WaitState`, shown as `Wait n`) | **Seconds** — a drag |
  | `Exposit` | **Text** — a field over the panel's reused buffer |
  | `SwitchZone` | **Zone** — a field, free text (see below) |
  | `MoveCamera` (or `MoveCameraState`, shown as the move) | **Target** (x, y, z) and **Seconds** — drags |
  | `Sequence` | **Add Child**, which appends a default op to the queue — offered whether or not the sequence is
    running, since an addition lands ahead of the running op |
  | `Parallel`, `Fork` | nothing but **Kind** and **Remove**; their children are edited by selecting them |
  | any node but the root, and nothing that is executing | **Remove** |

  A node on the running path is offered no kind and no numbers, and a running node that is not a `Sequence` says
  why in one line — "this op is running and cannot be changed" — which is FR-011 said in the author's terms rather
  than a panel that looks broken.
- **Rationale**: the surface is the reference's, minus what this cut drops. The kind dropdown plus per-kind
  fields is the one shape that keeps the *set of ops* editable without an editor per op, and it is what makes
  the panel worth having: an author can turn a queued `Exposit` into a `Wait`, or retune the line it shows,
  while the cue runs around it. `MoveCamera` carries no `from` field to edit because the authored form does not
  have one — the engine captures where the camera was when the op *begins* (`world/CueEngine.fs`), so there is
  nothing to present and nothing to lose.
- **A zone name is a text field, not a combo — deliberately.** A dropdown would have to enumerate the
  registered zones, which means the control reading the zone registry, which FR-006 forbids ("the caller
  supplies nothing about storage, selection ownership, or whether the cue is the screen's" — and a cue must be
  reusable where the zones are not, e.g. a scenario's). A typed name is the authored form's own shape, and a
  name that names no zone still raises loudly where it always did, in `GameplayState.switchZone`.
- **Out of this cut, with a reason each**: **reorder** (D-013 names it; the clarified editing set is change /
  add / remove, and the spec's Assumptions say so); **editing a locked node** (FR-011); **a
  `Parallel`'s arity** (a parallel is a pair, always — "add a third" is not a shape the type has);
  **dragging nodes between parents**; and **authoring a cue where there is none** beyond the kind dropdown and
  Add Child on a root the author can reach — the panel edits the cue the screen holds, and setting one is
  still code, or phase 10's triggers (the spec's Assumptions).
- **Alternatives considered**: **an inline editor per row** (each row its own widgets) — rejected: it makes a
  deep cue a wall of controls, and it puts the committed-edit bookkeeping in the tree walk rather than in one
  property block. **A modal per node** — rejected: heavier than the panel, and the reference's per-node
  properties block is already the shape D-013 records.

## Decision 8 — What the headless suite can assert, and what it cannot

- **Decision**: `test/CuePanelTests.fs` asserts, over cues built by hand and over the panel's own pure rules:
  - **the rows** — a leaf cue is one row at depth 0; a sequence is a row per container with children indented
    one deeper — including a `Sequence` nested in a `Sequence`, and a `Parallel`'s two sides as siblings; the
    queue behind a running op is listed in order; and **the whole label vocabulary**, with a `WaitState`
    labelled as the `Wait` it came from and a `MoveCameraState` as the move (FR-002, FR-004);
  - **the executing rule** — a fresh authored cue (no `…State`) has *nothing* locked and is wholly editable; a
    cue part-way through a wait locks the leaf **and the spine to the root** and leaves the queue behind it
    editable; a running `Parallel` locks both branches; a `Fork`'s inner branch is locked while the remainder's
    queue is not (FR-011, SC-006);
  - **the addition rule** — a running `Sequence` takes a child, while the op in flight, a queued leaf, a
    `Parallel` and a path that names nothing do not (FR-011, SC-006);
  - **navigation** — `tryAt` finds the node a path names and raises on a path that names nothing; `withAt`
    replaces exactly that node and leaves every other node the same value; `withoutAt` removes exactly it;
    and re-reading the cue after a round trip gives back what was put in (SC-007's "exactly once, and exactly");
  - **the history** — a push then an undo restores the cue exactly and a redo puts the edit back; a push clears
    the redo side; an undo with nothing applied is a no-op; and a `create`d history has nothing to take back
    (SC-008);
  - **the panel's write path**, where the pieces meet — `CueEditor.commit` writing the screen's cue exactly once
    and recording exactly one step, `undo` restoring it exactly and `redo` putting it back, with
    `canUndo`/`canRedo` tracking both, driven against a headless `GameplayState` (SC-007, SC-008).
- **What it cannot assert**: the ImGui widgets, the dock position and the panel's appearance. Those are the
  walkthrough — the same limit the house accepts for `DialoguePanel` and `PromptRender.draw`, and the reason
  FR-007 pushes the labels, the structure and the lock — and, beside it, the addition rule — into pure functions
  in the first place.
- **Alternatives considered**: driving ImGui headlessly to assert widget state — no: ImGui needs a context and
  a backend, and a test suite that stood one up would be testing the binding, not the panel. The pure split
  answers the same question more cheaply and is the house's own answer.

## Decision 9 — The documents that move with the feature

- **Decision**: three docs, and no fourth.
  - `docs/ROADMAP.md` — phase 9's editor deliverable (`editor/CueEditor.fs` + `editor/CueTree.fs`, recursive
    tree list, with the cue's own undo history) is **complete**, the phase's "done when" is met, and the
    next-step line advances to phase 10.
  - `docs/PROJECT_STRUCTURE.md` — the two `editor/` entries lose their "(planned)" and say what they are.
  - `docs/EDITOR_DESIGN.md` — D-010 gains that the cue's history is **per editing session** (dropped when Play
    begins) and that the panel is the editor's, shown while editing; D-013 gains the row-per-container shape,
    the running path that takes an addition but not a replacement, and the fact that **reorder is not in this cut**; and D-011's "cue root" note —
    which says the non-entity surfaces "need a project-scoped stack when it lands" — is brought up to date, as
    the cue root has now landed with its own history.
- **Rationale**: each of these already describes the cue editor, and each would otherwise be left saying
  something this feature makes untrue — the roadmap's "the editor follows as its own spec" line, the structure
  doc's "(planned)", and the design doc's reorder and its description of a stack that now exists. Bringing them
  together is what keeps the docs a description of the project rather than a plan it has passed.
- **Explicitly not moved**: `docs/ARCHITECTURE.md` (no state, system, chain or diagram changes — the panel is
  editor-only and the cue's place is unchanged) and `docs/CUE_MODEL_DECISION.md` (already marked historical, and
  this feature is what it predicted would follow).
