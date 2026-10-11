module History

// The one undo history (spec 048). A scene edit, a zone move, a zone add, a zone delete, a cue edit and an
// adventure-flag edit are all steps on `EditorState.Undo`; there is no history per zone, and none of its own for the
// cue (spec 049) or the flags (spec 055).
//
// A content command names the **place** it acts on (spec 077), so the state-level runner resolves that place and the
// step acts on its own zone rather than whichever place is active — a `Batch` may span places, and undoing a step
// never changes the active place. `ZoneMove` stays a step because switching the active place is itself an edit the
// author can undo, not because undo depends on it.
//
// Compiled *after* `state/GameplayState.fs` — the project's second deliberate ordering inversion, after
// `world/CueEngine.fs` — because a state-level step adds, removes or switches a zone, or writes the screen's
// cue. The `Command` *type* stays above the state: moving it would be cyclic, since `EditorState` names
// `UndoStack`, which names `Command`.

open Command

// ── The runner — a command in, carried out on the state ──

/// Apply one command to the gameplay state. A content command names its place (spec 077), so it is carried out on
/// **that place's zone**, not the active one — a `Batch` may therefore span places. A state-level step (a zone
/// move/add/delete, the cue, the flags, the scenario library) is carried out here.
let rec apply (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (cmd: Command) : unit =
    match cmd with
    | ZoneMove (_, next) -> GameplayState.switchZone shared &state next
    | ZoneAdd (_, name) -> GameplayState.addZone shared &state name
    | ZoneDelete (_, name, _) -> GameplayState.deleteZone shared &state name
    // Make an absent place present **without** entering it (spec 078): the registry's template streams in when one
    // is registered, exactly as the cue's `AddZone` does. The active place is unchanged — the command only ever
    // names an absent place.
    | ZonePresent name ->
        GameplayState.presentZone shared &state name (ZoneRegistry.tryTemplate name state.ZoneRegistry)
    // A batch may mix state-level and zone-level steps (spec 061: Instantiate is a presence/active change
    // and a template load as one step), so it is dispatched here, through the state-level runner, rather
    // than delegated whole to `Command.apply` (which refuses state-level steps).
    | Batch commands -> for cmd in commands do apply shared &state cmd
    // The cue is not a zone's, so a cue step is a write of the value and nothing else — no zone, no camera, no
    // selection (research D5). The cue is the screen's own value, so it needs neither `state.Physics` nor
    // `shared.Assets`.
    | SetCue (_, next) -> state.Cue <- next
    // The adventure flags are not a zone's either: the step writes `SharedState.Advents`, so the state-level
    // runner carries it as it carries `SetCue` (spec 055 FR-007).
    | Advent (_, next) -> shared.Advents <- next
    // The scenario library is not a zone's either: the step files or removes one entry (spec 070), so the
    // state-level runner carries it as it carries the adventure flags. The library lives on the gameplay state,
    // beside the zones.
    | SetScenario (name, _, next) -> ScenarioRegistry.setContents name next state.Scenarios
    // A content command names its own place: resolve it to its zone so the step acts on its own place rather
    // than the active one (spec 077). A step that names no place cannot reach here; an absent place is a broken
    // invariant, because LIFO reaches a step while its place is still present.
    | _ ->
        let place =
            match Command.placeOf cmd with
            | Some place -> place
            | None -> failwith "History.apply: a step that names no place reached the zone-level runner"
        match state.Zones.TryGetValue place with
        | true, zone -> Command.apply zone state.Physics shared.Assets cmd
        | false, _ -> failwithf "History.apply: the step names an absent place '%s'" (Place.placeName place)

/// The inverse of `apply`.
let rec undo (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (cmd: Command) : unit =
    match cmd with
    | ZoneMove (previous, _) -> GameplayState.switchZone shared &state previous
    | ZoneAdd (previous, name) ->
        // Leave the added zone first (it is the active one, and only an inactive zone may be released), then take
        // it away. It is empty by now: every edit made to it came later on the history and has been undone first.
        GameplayState.switchZone shared &state previous
        GameplayState.deleteZone shared &state name
    | ZoneDelete (_, name, contents) ->
        // Rebuild the zone **in place**, without switching (spec 077 FR-014). `Zone.occupy*` builds bodies that
        // join the simulation as they are created, so a zone that is not active has them detached right after —
        // the same build-then-detach `populateZone` uses for a place that is not active. The active place, the
        // camera and the selection are undisturbed.
        state.Zones.[name] <- Zone.create ()
        Zone.occupyZone state.Zones.[name] state.Physics shared.Assets contents
        Zone.deactivatePhysics state.Zones.[name] state.Physics
    // Undo a presence step by removing the place (spec 078). It is not the active one: a fork's created place is
    // never active, and a later switch onto it was undone first (LIFO) — the same invariant `ZoneDelete` relies on.
    | ZonePresent name -> GameplayState.deleteZone shared &state name
    | SetCue (previous, _) -> state.Cue <- previous
    | Advent (previous, _) -> shared.Advents <- previous
    | SetScenario (name, previous, _) -> ScenarioRegistry.setContents name previous state.Scenarios
    | Batch commands ->
        // Backwards, through the state-level runner, so a mixed batch reverses in dependency order.
        for cmd in List.rev commands do undo shared &state cmd
    // A content command names its own place: resolve it to its zone (spec 077), exactly as `apply` does.
    | _ ->
        let place =
            match Command.placeOf cmd with
            | Some place -> place
            | None -> failwith "History.undo: a step that names no place reached the zone-level runner"
        match state.Zones.TryGetValue place with
        | true, zone -> Command.undo zone state.Physics shared.Assets cmd
        | false, _ -> failwithf "History.undo: the step names an absent place '%s'" (Place.placeName place)

/// Apply a command and push it onto the one history, clearing the redo side. The editor's write path for a zone
/// step and for an entity **delete** — a delete routes through the state-level runner where undo/redo do, so the
/// reversal lives in one place. The other entity edits still apply and push themselves onto the same field;
/// routing them through here too would be churn with no behaviour change.
let commit (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (cmd: Command) : unit =
    do apply shared &state cmd
    state.Editor <- { state.Editor with Undo = UndoStack.push state.Editor.Undo cmd }

// ── Pruning the cue's steps ──

/// Drop every cue step from both sides of the one history, leaving every other step exactly as it was
/// (spec 049 FR-005). Called when Play begins: the cue advances while playing, so a recorded cue could no longer
/// be applied cleanly, while a zone step is unaffected and must stay undoable. It takes no `SharedState`,
/// because a cue step touches no zone and no resource.
///
/// A scenario fork is one `Batch` holding a `SetCue` beside the entity commands (spec 078), so the prune reaches
/// **into** a batch: it drops the nested `SetCue` and keeps the rest, leaving the fork's entities undoable on the
/// way into play. A batch emptied by the drop is itself dropped.
let pruneCueSteps (state: byref<GameplayState.GameplayState>) : unit =
    let rec keep (cmd: Command) : Command option =
        match cmd with
        | SetCue _ -> None
        | Batch commands ->
            match List.choose keep commands with
            | [] -> None
            | survivors -> Some (Batch survivors)
        | _ -> Some cmd

    state.Editor <-
        { state.Editor with
            Undo =
                { Applied = List.choose keep state.Editor.Undo.Applied
                  Redo = List.choose keep state.Editor.Undo.Redo } }

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
