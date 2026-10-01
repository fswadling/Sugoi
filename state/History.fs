module History

// The one undo history (spec 048). A scene edit, a zone move, a zone add and a zone delete are all steps on
// `EditorState.Undo`; there is no history per zone. The reason is order: last-in-first-out means that when undo
// reaches a step, the active zone is the zone that step was recorded in, so no command has to name a zone.
//
// Compiled *after* `state/GameplayState.fs` — the project's second deliberate ordering inversion, after
// `world/CueEngine.fs` — because a zone step adds, removes and switches a zone. The `Command` *type* stays above
// the state: moving it would be cyclic, since `EditorState` names `UndoStack`, which names `Command`.

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
    | _ -> Command.undo (GameplayState.activeZone state) shared.Physics shared.Assets cmd

/// Apply a command and push it onto the one history, clearing the redo side. The editor's write path for a zone
/// step. (An entity edit's existing call sites apply and push themselves, onto the same field; routing them
/// through here too would be churn with no behaviour change.)
let commit (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (cmd: Command) : unit =
    do apply shared &state cmd
    state.Editor <- { state.Editor with Undo = UndoStack.push state.Editor.Undo cmd }

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
