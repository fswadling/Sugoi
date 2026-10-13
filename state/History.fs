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

// A step the history cannot carry out — its place or target is not in the state it names — is skipped and
// reported, not fatal (spec 080, US4): a runtime cue op mutates the scene without a history step, so a
// recorded step can outlive the place it names. The state mutators stay fail-fast for direct misuse.
let private skip (phase: string) (cmd: Command) (reason: string) : unit =
    do Diagnostics.reportf "History.%s: skipping %A: %s" phase cmd reason

/// Apply one command to the gameplay state. A content command names its place (spec 077), so it is carried out on
/// **that place's zone**, not the active one — a `Batch` may therefore span places. A state-level step (a zone
/// move/add/delete, the cue, the flags, the scenario library) is carried out here.
let rec apply (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (cmd: Command) : unit =
    match cmd with
    | ZoneMove (_, next) ->
        if state.Zones.ContainsKey next then
            GameplayState.switchZone shared &state next
        else
            skip "apply" cmd (sprintf "the place '%s' is not present" next)
    | ZoneAdd (_, name) ->
        if state.Zones.ContainsKey name then
            skip "apply" cmd (sprintf "the place '%s' is already present" name)
        else
            GameplayState.addZone shared &state name
    | ZoneDelete (_, name, _) ->
        if not (state.Zones.ContainsKey name) then
            skip "apply" cmd (sprintf "the place '%s' is not present" name)
        elif name = state.ActiveZone then
            skip "apply" cmd (sprintf "the place '%s' is the active place" name)
        else
            GameplayState.deleteZone shared &state name
    // Make a zone present **without** entering it, staged from the command's `Staging` (spec 079). The active zone
    // is unchanged — the command only ever names an absent zone.
    | ZonePresent (name, staging) ->
        if state.Zones.ContainsKey name then
            skip "apply" cmd (sprintf "the place '%s' is already present" name)
        else
            GameplayState.presentZone shared &state name staging
    // Replace a present zone's staging (spec 079); the state-level runner knows the active zone, so an inactive
    // zone's built bodies are detached inside `stageZone`.
    | ZoneStage (name, _, next) ->
        if state.Zones.ContainsKey name then
            GameplayState.stageZone shared &state name next
        else
            skip "apply" cmd (sprintf "the place '%s' is not present" name)
    // Retire every zone and install the fresh start (spec 084). A whole-world step: it needs the physics world, the GPU
    // device and the active place, so only this runner can carry it. The target is fixed, so the payload is only for
    // undo; the running cue is reset by a sibling `SetCue` in the same batch.
    | ZonesClear _ ->
        GameplayState.replaceZones shared &state [ (GameplayState.startZone, Zone.emptyContents) ] GameplayState.startZone
    // A batch may mix state-level and zone-level steps (spec 061: Instantiate is a presence/active change
    // and a template load as one step), so it is dispatched here, through the state-level runner, rather
    // than delegated whole to `Command.apply` (which refuses state-level steps).
    | Batch commands -> for cmd in commands do apply shared &state cmd
    // The cue is not a zone's, so a cue step is a write of the value and nothing else — no zone, no camera, no
    // selection (research D5). The cue is the screen's own value, so it needs neither `state.Physics` nor
    // `shared.Assets`.
    | SetCue (_, next) -> state.Cue <- next
    // The adventure flags are not a zone's either: the step writes `SharedState.Advents`, so the state-level
    // runner carries it as it carries `SetCue` (spec 055 FR-007; the set is a set of names since spec 082).
    | Advent (_, next) -> shared.Advents <- next
    // The scenario library is not a zone's either: the step writes the whole library (spec 070; string-keyed
    // since spec 083), so the state-level runner carries it as it carries the adventure flags. The library lives on
    // the gameplay state, beside the zones.
    | SetScenario (_, next) -> state.Scenarios <- next
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
        | false, _ -> skip "apply" cmd (sprintf "the place '%s' is not present" place)

/// The inverse of `apply`.
let rec undo (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (cmd: Command) : unit =
    match cmd with
    | ZoneMove (previous, _) ->
        if state.Zones.ContainsKey previous then
            GameplayState.switchZone shared &state previous
        else
            skip "undo" cmd (sprintf "the place '%s' is not present" previous)
    | ZoneAdd (previous, name) ->
        // Leave the added zone first (it is the active one, and only an inactive zone may be released), then take
        // it away. It is empty by now: every edit made to it came later on the history and has been undone first.
        // A place a runtime cue has since removed makes the step un-carryable: skip it rather than abort (spec 080).
        if state.Zones.ContainsKey previous && state.Zones.ContainsKey name then
            GameplayState.switchZone shared &state previous
            GameplayState.deleteZone shared &state name
        else
            skip "undo" cmd (sprintf "the place '%s' is not present" (if state.Zones.ContainsKey previous then name else previous))
    | ZoneDelete (_, name, contents) ->
        // Rebuild the zone **in place**, without switching (spec 077 FR-014). `Zone.occupy*` builds bodies that
        // join the simulation as they are created, so a zone that is not active has them detached right after —
        // the same build-then-detach `populateZone` uses for a place that is not active. The active place, the
        // camera and the selection are undisturbed.
        if state.Zones.ContainsKey name then
            skip "undo" cmd (sprintf "the place '%s' is already present" name)
        else

        state.Zones.[name] <- Zone.create ()
        Zone.occupyZone state.Zones.[name] state.Physics shared.Assets contents
        Zone.deactivatePhysics state.Zones.[name] state.Physics
    // Undo a presence step by removing the zone (spec 078). It is not the active one: a fork's created zone is
    // never active, and a later switch onto it was undone first (LIFO) — the same invariant `ZoneDelete` relies on.
    | ZonePresent (name, _) ->
        if not (state.Zones.ContainsKey name) then
            skip "undo" cmd (sprintf "the place '%s' is not present" name)
        elif name = state.ActiveZone then
            skip "undo" cmd (sprintf "the place '%s' is the active place" name)
        else
            GameplayState.deleteZone shared &state name
    // Undo a restage by staging back to the zone's previous staging (spec 079).
    | ZoneStage (name, previous, _) ->
        if state.Zones.ContainsKey name then
            GameplayState.stageZone shared &state name previous
        else
            skip "undo" cmd (sprintf "the place '%s' is not present" name)
    | ZonesClear (zones, active) ->
        GameplayState.replaceZones shared &state zones active
    | SetCue (previous, _) -> state.Cue <- previous
    | Advent (previous, _) -> shared.Advents <- previous
    | SetScenario (previous, _) -> state.Scenarios <- previous
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
        | false, _ -> skip "undo" cmd (sprintf "the place '%s' is not present" place)

/// Apply a command and push it onto the one history, clearing the redo side. The editor's write path for a zone
/// step and for an entity **delete** — a delete routes through the state-level runner where undo/redo do, so the
/// reversal lives in one place. The other entity edits still apply and push themselves onto the same field;
/// routing them through here too would be churn with no behaviour change.
let commit (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (cmd: Command) : unit =
    do apply shared &state cmd
    state.Editor <- { state.Editor with Undo = UndoStack.push state.Editor.Undo cmd }

// ── Clearing the world (spec 084) ──

/// Retire every present zone and reset the running cue, as one step on the one history (spec 084). The Edit-menu item
/// and its shortcut both call this, so the zones and the cue are captured once, here. The cue reset rides the existing
/// `SetCue` step in the same `Batch` (skipped when the cue is already empty), so `ZonesClear` stays zones-only and one
/// clear is one step; `pruneCueSteps` already reaches into a batch on Play and keeps the zone step undoable.
let clearZones (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    let zones = state.Zones
    let previous = [ for KeyValue (name, zone) in zones -> name, Zone.captureZone zone ]
    let cueSteps = if state.Cue.IsFin then [] else [ SetCue (state.Cue, Cue.Fin) ]
    do commit shared &state (Batch (ZonesClear (previous, state.ActiveZone) :: cueSteps))

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
