# Contract: The cue's edits ride the one undo history

The surfaces this feature adds or changes, and what explicitly does not change.

## `state/Command.fs` — CHANGED

**Contract**: one new case joins the union — `SetCue of Previous : Cue.Cue * Next : Cue.Cue`. `Command.apply`
and `Command.undo` group it with the three zone steps into the one `invalidOp`, because a cue is not a zone's
and the zone-level runner cannot carry it out; the message gains a word ("a state-level step … not a zone
edit"). Every existing case, the `Batch` gesture, the payload rule and `UndoStack.create`/`push` (with the
transform merge) are unchanged. `UndoStack.undo`/`redo` — the **zone-level** steppers the zone suite drives —
are unchanged and stay in this module; they now raise on a `SetCue`, which a zone stack never holds.

## `state/History.fs` — CHANGED

```fsharp
// `apply`/`undo` each gain one arm, before the fall-through to `Command.apply`/`Command.undo`:
//   apply: | SetCue (_, next)     -> state.Cue <- next
//   undo:  | SetCue (previous, _) -> state.Cue <- previous

val pruneCueSteps : byref<GameplayState.GameplayState> -> unit   // NEW
```

**Contract**: the state-level runner learns the cue step — it writes `state.Cue` and nothing else — and gains
`pruneCueSteps`, which filters every `SetCue` step out of **both** `Applied` and `Redo` and leaves every other
step exactly as it was. The module's position (compiled after `state/GameplayState.fs`), its signatures for the
existing runners and steppers, and its file header are otherwise unchanged.

## `state/EditorState.fs` — CHANGED

```fsharp
type CueHistory = { … }         // REMOVED — the type and its create/push/undo/redo module
CueHistory : CueHistory         // REMOVED — the field and its `create ()` seed
CueSelection   : int list option // UNCHANGED
CueTextBuffer  : byte[]          // UNCHANGED
CueTextPath    : int list option // UNCHANGED
CueTextEditing : bool            // UNCHANGED
```

**Contract**: the cue's own history is gone; the panel's selection and its three text-field fields stay,
because they are interface scaffolding rather than a history. `Undo : UndoStack` is untouched — it is already
the one history (spec 048), and this feature is what makes a cue edit reach it. No other field is added or
removed.

## `editor/CueEditor.fs` — CHANGED

```fsharp
val commit  : byref<GameplayState.GameplayState> -> Cue -> unit   // UNCHANGED SIGNATURE, new body
// REMOVED: val canUndo, val canRedo, val undo, val redo
```

**Contract**: `commit` writes `state.Cue` and pushes `SetCue (before, next)` onto `state.Editor.Undo` itself —
**not** through `History.commit`, so the panel keeps taking only the gameplay state (research D3). `canUndo`,
`canRedo`, `undo` and `redo` are deleted, and so is the block of `draw` that drew their two `smallButton`s and
its comment. The tree, the selected node's editor, the text field and the lock rules are unchanged; the file's
header comment is rewritten, because it described an own history the panel no longer has.

## `editor/Editor.fs` — CHANGED

**Contract**: on the way **into** play (the backtick toggle, `state.Editing` going false) the line that reset
the cue's history is replaced by `History.pruneCueSteps &state`, so the cue's *steps* go while every zone step
stays. `undoStep`/`redoStep`, the `Ctrl+Z`/`Ctrl+Y` bindings, the Edit menu and the `idle` gate are unchanged.

## `test/CuePanelTests.fs` — CHANGED, plus new coverage

**Contract**: the `CueHistory` tests and the `CueEditor.commit`/`undo`/`redo`/`canUndo`/`canRedo` round-trip are
replaced by one-history equivalents, and the panel's suite gains a small `shared` fixture (mirroring
`GameplayStateTests`'s) so the history can be driven. What is asserted, headlessly and with no device:

- the panel's write path: `CueEditor.commit` changes `state.Cue` once and pushes exactly one `SetCue` onto
  `state.Editor.Undo`; `History.undoHistory` restores the earlier cue, `History.redoHistory` re-applies it, and
  an undo with nothing applied is a no-op;
- **SC-001**: a cue edit followed by an actor transform are undone in the reverse of the order made — the
  transform first, the cue second — and redone in the order made;
- **SC-002**: the panel's compiled surface exposes no `undo`, `redo`, `canUndo` or `canRedo`;
- **SC-004**: undo through the whole history returns the project to its starting state, cue included;
- **FR-006**: undoing a cue step changes no active zone, no camera and no selection.

In `test/GameplayStateTests.fs`, beside the one-history tests, **SC-003** and FR-010: `History.pruneCueSteps`
removes the cue's steps from **both** sides and leaves a zone step still undoable.

## `docs/` and file headers — CHANGED, and the prior specs — NOT CHANGED

The living design documents move with the feature:

| File | Change |
|---|---|
| `docs/EDITOR_DESIGN.md` | the workspace table's Cue row (line ~63); D-010 (the cue's undo); D-011 (the command/undo model — the cue joins the command family, and the "non-entity surfaces" clause's cue example is corrected); D-013 (the cue editor's interaction keeps its tree list but not its own history); the module map's `CueEditor`/`CueTree` row |
| `docs/PROJECT_STRUCTURE.md` | the `CueEditor.fs` line (`spec 047; direct write, its own history` → the cue panel, whose edits are steps on the one history) |
| `docs/ROADMAP.md` | phase 9's cue-editor deliverable ("with the cue's own undo history (D-010)" → edits on the one history) and the "Next step" summary, which calls the panel "the Cue panel with the cue's own undo history (specs/047)" |
| `editor/CueEditor.fs` | its header comment, which describes a direct write with an own history |

**Deliberately not moved**: `docs/CUE_MODEL_DECISION.md` — it records the earlier model (it is marked
"superseded as the design; kept for the measurements and the route"), and its cue-history clause is part of
what it recorded, not a description of current behaviour.

**The prior specs are not edited.** `specs/047-cue-panel` and `specs/048-unified-undo-stack` are historical
artefacts — each records the design at the moment it was written, and editing one afterwards is how the record
stops being trustworthy. The statements this feature supersedes are recorded in [research.md](./research.md)
**Decision 8**, in a table of *what was written → what this feature does instead*, together with the list of
what is **not** superseded.

## Explicitly unchanged

- **The cue itself** (`world/Cue.fs`) and its engine (`world/CueEngine.fs`) — one type that is the vocabulary
  and its run, and the pure step functions over it. No new type, no state array, no handle.
- **The panel's editing rules** (`specs/047` FR-011/FR-012, D-013) — a node that is executing cannot be
  retuned, replaced or removed; a running `Sequence` still takes an addition; a row per container; the
  node-text field commits once.
- **The rest of the command family and `UndoStack`** — payloads, `apply`/`undo` bodies, `Batch`, `create`,
  `push`. The transform merge is untouched, and a cue edit simply never matches it.
- **`state/GameplayState.fs`, `state/SharedState.fs`** — untouched. The cue lives on the gameplay state
  already; nothing its neighbours own changes.
- **`fsharp-game.fsproj`** — no `Compile` entry changes: `SetCue` names an existing type, `History.pruneCueSteps`
  an existing module, and `CueEditor` an existing file.
- **`editor/Spawning.fs`, `editor/Selection.fs`, `editor/Inspector.fs`, the other panels** — untouched. They
  apply a command and push it onto the now-one history, as they already did; this feature adds a step kind
  beside them rather than changing how they record.
- **The render path, the physics world, `world/Systems.fs`, and the cue's runtime execution** — untouched.
- **Triggers wired to cues** — out of scope (roadmap phase 10); when they arrive they use the same step.
