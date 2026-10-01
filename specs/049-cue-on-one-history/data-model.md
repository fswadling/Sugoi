# Data Model: The cue's edits ride the one undo history

Three changes to the model: one new `Command` case, one arm and one helper on the state-level history
runner, and the deletion of the cue's own history — a type, its module, a field, and the panel functions
behind its two controls. The command family, `UndoStack`, the cue itself and the panel's editing rules are
unchanged.

## `state/Command.fs` — the type gains one case

```fsharp
type Command =
    | …                                        // UNCHANGED: every existing case, including `Batch`
    // The screen's cue was edited (spec 049). It is not a zone's — a cue spans zones (spec 046 FR-013) —
    // so, like a zone step, it is carried out against the gameplay state and not by the zone-level runner.
    // The payload is the two cue values: a cue is immutable, so a snapshot is a reference, and that is
    // exactly what the cue's own `CueHistory` stored before this feature (research D2).
    | SetCue of Previous : Cue.Cue * Next : Cue.Cue
```

- **Why a case and not a second history.** `CueHistory` existed only because the undo stacks were per zone
  and a cue spans zones (`specs/047` D-010). `specs/048` removed the premise — one history on the state —
  so the cue can ride it, and a second history is now only a second timeline to keep in step (research D1).
- **Why two cue values and not a command per edit kind.** A removal, an addition, a kind change and a text
  change are all "the cue was this and is now that", so one case covers every edit the panel can make and
  nothing has to know how a cue was edited. It is the shape `SetLight`/`SetPlayer` already use for a
  zone-level value (research D2).
- **Why `Cue.Cue` is written module-qualified.** `world/Cue.fs` is compiled before `state/Command.fs`, so
  the case can name it; `state/EditorState.fs` already writes it the same way, and no new `open` is needed.

## `state/Command.fs` — the zone-level runner refuses the new case

`Command.apply`/`Command.undo` match every case explicitly. The three zone steps are grouped into one
`invalidOp`; `SetCue` joins that group, because a cue is not a zone's and the zone-level runner cannot carry
it out either:

```fsharp
    | ZoneMove _
    | ZoneAdd _
    | ZoneDelete _
    | SetCue _ ->
        invalidOp "Command.apply: a state-level step is on the gameplay state's history, not a zone edit"
```

- The `invalidOp` message gains a word: the group is no longer only zone steps. Reaching either function
  with a state-level step is a broken invariant, not a value to ignore (principle II).
- `UndoStack.undo`/`redo` (the zone-level steppers) call these two, so they raise on a `SetCue` too — but a
  `SetCue` is only ever pushed onto `state.Editor.Undo` and only ever stepped by `History`, so the path is
  unreachable. `UndoStack.create`/`push` are untouched: `push`'s merge only fires for two `Transform`s of
  the same ref, so a cue edit clears the redo side exactly as the cue's own `push` did, and two cue edits
  never coalesce.

## `state/History.fs` — one arm on each runner, and a prune for Play

```fsharp
// `apply` and `undo` each gain one arm, before the fall-through to `Command.apply`/`Command.undo`:
//   apply: | SetCue (_, next)      -> state.Cue <- next
//   undo:  | SetCue (previous, _)  -> state.Cue <- previous

/// Drop every cue step from both sides of the one history, leaving every other step exactly as it was.
/// Called when Play begins (FR-005): the cue advances while playing, so a recorded cue could no longer be
/// applied cleanly, while a zone step is unaffected and must stay undoable.
val pruneCueSteps : byref<GameplayState.GameplayState> -> unit
```

```fsharp
let pruneCueSteps (state: byref<GameplayState.GameplayState>) : unit =
    let keep cmd =
        match cmd with
        | SetCue _ -> false
        | _ -> true
    state.Editor <-
        { state.Editor with
            Undo =
                { Applied = List.filter keep state.Editor.Undo.Applied
                  Redo = List.filter keep state.Editor.Undo.Redo } }
```

- **`SetCue` is a state-level step, so it is carried out here.** `History` is the module that already
  applies a zone move, an add and a delete against the state; a cue edit belongs beside them (research D3).
  `CueEditor.commit` writes and pushes itself and does **not** route through `History.commit` — that would
  make the panel take `SharedState`, which it does not need — but redo still reaches `apply`, so the arm is
  required.
- **The prune lives here, not in `Editor.fs`.** It is a history operation, and keeping the `SetCue` match in
  `History` means `editor/Editor.fs` never learns the step's shape. It takes no `SharedState`, because a cue
  step touches no zone and no resource.
- **It filters, it does not clear.** Only `SetCue` steps go, from **both** sides; a zone step stays undoable
  across a play (FR-005/FR-010, research D4). Filtering is sound because the steps are independent — no cue
  step depends on a zone step or on another cue step — so the remaining timeline's order and meaning are
  intact.

## `state/EditorState.fs` — the cue's own history goes

```fsharp
// REMOVED: the type and its module
type CueHistory = { Applied : Cue.Cue list; Redo : Cue.Cue list }
module CueHistory =
    val create : unit -> CueHistory
    val push   : before: Cue.Cue -> history: CueHistory -> CueHistory
    val undo   : current: Cue.Cue -> history: CueHistory -> Cue.Cue * CueHistory
    val redo   : current: Cue.Cue -> history: CueHistory -> Cue.Cue * CueHistory

// REMOVED: the field
CueHistory : CueHistory

// UNCHANGED: the panel's interface scaffolding (which node is highlighted, the buffer being typed into)
CueSelection   : int list option
CueTextBuffer  : byte[]
CueTextPath    : int list option
CueTextEditing : bool
```

- The field group's comment loses its "the cue's own undo history" clause; the selection, the buffer and the
  two flags stay, and stay described, because they are UI scaffolding rather than a history (FR-008).
- `EditorState.create` drops the `CueHistory = CueHistory.create ()` line and nothing else.

## `editor/CueEditor.fs` — the write path commits inline, and the four functions go

```fsharp
/// Apply an accepted edit: the next cue written in place, and one `SetCue` step pushed onto the one history.
/// The control decides whether anything changed, so this always records a step.
let commit (state: byref<GameplayState.GameplayState>) (next: Cue) : unit =
    let before = state.Cue
    state.Cue <- next
    state.Editor <- { state.Editor with Undo = UndoStack.push state.Editor.Undo (SetCue (before, next)) }

// REMOVED: canUndo, canRedo, undo, redo — and the two `smallButton`s that called them in `draw`.
```

- **The panel still takes only the gameplay state** (spec 047's property, preserved deliberately): the write
  and the push are the same two lines every other editor call site already uses (research D3).
- **`commit`'s signature is unchanged**, so its two call sites in `draw` (the node editor's staged rebuild and
  the text field's submit) are unchanged.
- **The four functions are deleted, not left unused.** The only entry points are `Ctrl+Z`/`Ctrl+Y` and the
  Edit menu, whose enabled state already reads the one history and already gates on `Drag = None` and
  `not (ImGui.wantTextInput())` (research D6).
- A new `open Command` (or the qualified `Command.SetCue`/`Command.UndoStack.push`) supplies the case and the
  stack; `open Cue` stays for the cue vocabulary.

## `editor/Editor.fs` — the Play toggle prunes instead of clearing

```fsharp
// was: state.Editor <- { state.Editor with CueHistory = CueHistory.create () }
if not state.Editing then
    History.pruneCueSteps &state
```

- The comment changes with the line: the cue's *steps* go on the way into play, because the cue advances
  while playing and such a step could no longer be applied cleanly — while every zone step stays.
- `undoStep`/`redoStep` keep calling `History.undoHistory`/`redoHistory` and `Selection.dropStalePartSelection`;
  the shortcut keys, the Edit menu and the `idle` gate are unchanged. `dropStalePartSelection` only drops a
  Part selection whose environment epoch no longer resolves, and a cue step changes no environment, so it is
  a no-op on the cue path and needs no guard (FR-009's drag/text conditions are the menu's and the shortcut's).

## Command semantics

| Command | `apply` | `undo` | carried by |
|---|---|---|---|
| `SetCue (previous, next)` | `state.Cue <- next` | `state.Cue <- previous` | `History` (the state) |
| a zone step (`ZoneMove`/`ZoneAdd`/`ZoneDelete`) | unchanged | unchanged | `History` (the state) |
| every existing entity or zone-value case | unchanged — the active zone | unchanged — the active zone | `Command` (a zone) |

## Validation rules

| Rule | Where it is enforced |
|---|---|
| A cue step is not a zone edit | `Command.apply`/`undo` raise on `SetCue`; only `History` carries it out against the state |
| A cue step changes only `state.Cue` | `History.apply`/`undo` write that field and nothing else — no zone, camera or selection (research D5) |
| A cue edit is one step | `CueEditor.commit` pushes exactly once per accepted edit |
| A cue edit clears the redo side | `UndoStack.push`, unchanged — `SetCue` is not a mergeable `Transform` pair |
| A cue edit never coalesces with another | the same merge rule; only two `Transform`s of one ref collapse |
| A cue edit is only made while editing | Play freezes the cue (`specs/046`) and the panel is editor-only; there is no "edit the cue while it runs" case to decide (spec Assumptions) |
| Play drops the cue's steps and nothing else | `History.pruneCueSteps`, from both `Applied` and `Redo` (FR-005/D4) |
| A stale selection renders as no selection | `CueTree`'s existing rule — a step carries no selection (FR-006, clarified) |
