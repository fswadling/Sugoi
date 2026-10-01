module History

// The one undo history (spec 048). A scene edit, a zone move, a zone add, a zone delete and a cue edit are all
// steps on `EditorState.Undo`; there is no history per zone, and none of its own for the cue (spec 049). The
// reason is order: last-in-first-out means that when undo reaches a step, the active zone is the zone that step
// was recorded in, so no command has to name a zone.
//
// Compiled *after* `state/GameplayState.fs` — the project's second deliberate ordering inversion, after
// `world/CueEngine.fs` — because a state-level step adds, removes or switches a zone, or writes the screen's
// cue. The `Command` *type* stays above the state: moving it would be cyclic, since `EditorState` names
// `UndoStack`, which names `Command`.

open Command

// ── The runner — a command in, carried out on the state ──

/// Apply one command to the gameplay state. An entity or zone-value command goes to **the active zone**: no
/// command carries a zone, because recording the moves is what keeps the active zone the one the step belongs
/// to (research D2).
let apply (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (cmd: Command) : unit =
    match cmd with
    | ZoneMove (_, next) -> GameplayState.switchZone shared &state next
    | ZoneAdd (_, name) -> GameplayState.addZone shared &state name
    | ZoneDelete (_, name, _) -> GameplayState.deleteZone shared &state name
    // The cue is not a zone's, so a cue step is a write of the value and nothing else — no zone, no camera, no
    // selection (research D5). The cue is the screen's own value, so it needs neither `shared.Physics` nor
    // `shared.Assets`.
    | SetCue (_, next) -> state.Cue <- next
    | _ -> Command.apply (GameplayState.activeZone state) shared.Physics shared.Assets cmd

/// The inverse of `apply`.
let undo (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (cmd: Command) : unit =
    match cmd with
    | ZoneMove (previous, _) -> GameplayState.switchZone shared &state previous
    | ZoneAdd (previous, name) ->
        // Leave the added zone first (it is the active one, and only an inactive zone may be released), then take
        // it away. It is empty by now: every edit made to it came later on the history and has been undone first.
        GameplayState.switchZone shared &state previous
        GameplayState.deleteZone shared &state name
    | ZoneDelete (previous, name, contents) ->
        // `Zone.occupy*` creates each body and the body joins the simulation as it is created, so only the active
        // zone's bodies may be built: the zone is brought back and made active, its contents put in, and it is
        // then switched away from if it should not stay (research D4).
        state.Zones.[name] <- Zone.create ()
        GameplayState.switchZone shared &state name
        Zone.occupyZone state.Zones.[name] shared.Physics shared.Assets contents
        if previous <> name then GameplayState.switchZone shared &state previous
    | SetCue (previous, _) -> state.Cue <- previous
    | _ -> Command.undo (GameplayState.activeZone state) shared.Physics shared.Assets cmd

/// Apply a command and push it onto the one history, clearing the redo side. The editor's write path for a zone
/// step. (An entity edit's existing call sites apply and push themselves, onto the same field; routing them
/// through here too would be churn with no behaviour change.)
let commit (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (cmd: Command) : unit =
    do apply shared &state cmd
    state.Editor <- { state.Editor with Undo = UndoStack.push state.Editor.Undo cmd }

// ── Pruning the cue's steps ──

/// Drop every cue step from both sides of the one history, leaving every other step exactly as it was
/// (spec 049 FR-005). Called when Play begins: the cue advances while playing, so a recorded cue could no longer
/// be applied cleanly, while a zone step is unaffected and must stay undoable. It takes no `SharedState`,
/// because a cue step touches no zone and no resource.
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

// ── Stepping the history ──

/// Step the one history back, reversing the command it reaches. A no-op with nothing applied.
let undoHistory (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    match state.Editor.Undo.Applied with
    | [] -> ()
    | cmd :: rest ->

    do undo shared &state cmd
    state.Editor <- { state.Editor with Undo = { Applied = rest; Redo = cmd :: state.Editor.Undo.Redo } }

/// Step the one history forward again, re-applying what was undone. A no-op with nothing undone.
let redoHistory (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    match state.Editor.Undo.Redo with
    | [] -> ()
    | cmd :: rest ->

    do apply shared &state cmd
    state.Editor <- { state.Editor with Undo = { Applied = cmd :: state.Editor.Undo.Applied; Redo = rest } }
