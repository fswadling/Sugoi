module Command

// Undoable editor commands. Each content command names the **place** it acts on and stores the data
// needed to apply and undo a mutation there, plus the affected `EntityRef` (kind, index, generation, and
// — for a part — its environment epoch). The generation and epoch are what let a restored entity be told
// apart from one that reused its slot; the place is what lets a step act on its own zone rather than
// whichever place is active, so a `Batch` may span places (spec 077).
//
// A create and a delete exist per entity kind, each carrying that kind's own authored
// value — what `Zone.capture*` reads off a live entity. There is no kind-agnostic payload:
// the base entity every kind shares could not carry a kind's own authored state, which is
// what this shape exists to fix. A `*Create` serves both spawning a new entity and
// duplicating an existing one, since the two were always the same operation and differ
// only in where the payload came from.
//
// A payload is the authored record itself, so it cannot express a physics attachment, a
// renderer, or a runtime flag: those are different types, held by the entity beside its
// `State` and rebuilt by `Zone.occupy*`.
//
// A command may also be several mutations that were one gesture (`Batch`), which keeps the
// rule that one command is one undo step true for a gesture that has to replace a zone's
// environment and every prop in it at once.

open Entity
open Zone
open Texture
open SlotMap
open ZoneTemplate

type Command =
    // An edit of the properties every kind shares. The `Place` names the zone the command acts on.
    | Transform of ZoneId : string * Ref : EntityRef * Before : Transform * After : Transform
    | Rename of ZoneId : string * Ref : EntityRef * Before : string * After : string
    // One create and one delete per kind, carrying the kind's own authored value.
    | ActorCreate of ZoneId : string * Ref : EntityRef * Actor : ActorState
    | ActorDelete of ZoneId : string * Ref : EntityRef * Actor : ActorState
    | PropCreate of ZoneId : string * Ref : EntityRef * Prop : PropState
    | PropDelete of ZoneId : string * Ref : EntityRef * Prop : PropState
    | PartCreate of ZoneId : string * Ref : EntityRef * Part : PropState
    | PartDelete of ZoneId : string * Ref : EntityRef * Part : PropState
    | WaypointCreate of ZoneId : string * Ref : EntityRef * Waypoint : Waypoint
    | WaypointDelete of ZoneId : string * Ref : EntityRef * Waypoint : Waypoint
    | TriggerCreate of ZoneId : string * Ref : EntityRef * Trigger : TriggerState
    | TriggerDelete of ZoneId : string * Ref : EntityRef * Trigger : TriggerState
    // A property edit that belongs to one kind: a waypoint's link, a trigger's shape.
    | Link of ZoneId : string * Ref : EntityRef * Before : EntityRef option * After : EntityRef option
    | TriggerSet of ZoneId : string * Ref : EntityRef * Before : TriggerState * After : TriggerState
    // The extend gesture as one step: create `Ref` from `Waypoint` and point `From` at it.
    | LinkSpawn of ZoneId : string * From : EntityRef * Before : EntityRef option * Ref : EntityRef * Waypoint : Waypoint
    // Zone-level rather than an entity: swapping the loaded environment.
    | SetEnvironment of ZoneId : string * Previous : Environment option * Next : Environment option
    // Zone-level rather than an entity: the authored light (spec 043). Pushed by the Inspector's
    // committed edits and carried by an instantiate, so a light change is reversible like any other.
    | SetLight of ZoneId : string * Previous : LightSettings * Next : LightSettings
    // Zone-level rather than an entity: the player designation (spec 045) — which of the zone's actors is
    // the avatar. The payload is the value `Player` already holds, so no fourth spelling of it is
    // introduced, and `Previous` is what makes undoing a re-designation restore the *previous actor*
    // rather than no player at all.
    | SetPlayer of ZoneId : string * Previous : Handle voption * Next : Handle voption
    // Several mutations that were one gesture: applied forwards, reversed backwards, so a create and
    // the delete that preceded it cannot come apart. One entry on the stack, like any other command,
    // and a step that puts a zone back exactly is what needs it (spec 035 FR-005). Not itself a place's:
    // each sub-command names its own, so a batch may span places.
    | Batch of Commands : Command list
    // The active place moved (spec 048, spec 077). A content command now names its own place, so this is no
    // longer load-bearing for undo's correctness; it stays because switching the active place is itself an
    // edit the author can undo.
    | ZoneMove of Previous : string * Next : string
    // A place was made present, and made active (spec 048, 061). `Previous` is the place active before the
    // add, which its undo restores: an add and the active change it causes are **one** step, so one click is
    // one undo.
    | ZoneAdd of Previous : string * Name : string
    // A place was made absent (spec 048, 061), carrying everything needed to put its zone back. The removed
    // place is never the active one (spec 061 FR-011), so `Previous` is the active place and is unchanged by
    // the delete; its undo rebuilds the zone in place without switching (spec 077 FR-014).
    | ZoneDelete of Previous : string * Name : string * Contents : ZoneContents
    // A place was made present **without** entering it (spec 078); its staging rides the command (spec 079) so
    // redo needs no store. `ZoneAdd` makes a place present *and active*, but a fork must not switch the layout. It
    // carries no `Previous` — it never changes the active place. Apply makes the place present and stages it from
    // `Staging`; undo removes the place.
    | ZonePresent of Name : string * Staging : ZoneStaging
    // A present zone's staging was replaced (spec 079): a fork into a zone the scene already holds. State-level
    // like `ZonePresent`, because only the state-level runner knows the active place and can detach an inactive
    // zone's built bodies. Apply stages to `Next`; undo stages back to `Previous`.
    | ZoneStage of Name : string * Previous : ZoneStaging * Next : ZoneStaging
    // Every zone was retired and the world returned to its start (spec 084). The payload is the whole previous
    // world — each present zone's name and authored contents, and which was active — so undo rebuilds it in place.
    // State-level like `ZoneDelete`: only the state knows the physics world, the GPU device and the active place,
    // and a `Batch` cannot express it (a delete may not name the active zone, and the step must also install the
    // fresh start). The running cue is reset by a sibling `SetCue` in the same batch, so this case stays zones-only.
    | ZonesClear of Previous : (string * ZoneContents) list * PreviousActive : string
    // The screen's cue was edited (spec 049). It is not a zone's — a cue spans zones (spec 046 FR-013) — so, like
    // a zone step, it is carried out against the gameplay state and not by the zone-level runner. The payload is
    // the two cue values: a cue is immutable, so a snapshot is a reference, and that is exactly what the cue's
    // own history stored before this feature. `Cue.Cue` is module-qualified because the module and the type share
    // the name; `state/EditorState.fs` writes it the same way.
    | SetCue of Previous : Cue.Cue * Next : Cue.Cue
    // The named adventure flags were edited (spec 055; a string set since spec 082). Like `SetCue`, it is not a
    // zone's — the set is project state on `SharedState` (`specs/055` FR-002) — so it is a state-level step carried
    // by `History` and never by the zone-level runner. The payload is the two set values: the set is immutable, so a
    // snapshot is the set itself, and a before/after pair makes undo total (an add and a remove are the same shape).
    | Advent of Previous : Advent.Set * Next : Advent.Set
    // The scenario library was edited (spec 070; string-keyed since spec 083): an add, delete, clear or register,
    // as a before/after pair of the whole library. Like `SetCue` and `Advent`, it is not a zone's — a scenario spans
    // zones — so it is a state-level step carried by `History` and never by the zone-level runner. The library is a
    // persistent ordered map, so the pair is a reference snapshot, and one shape covers every kind of edit.
    | SetScenario of Previous : ScenarioRegistry.Registry * Next : ScenarioRegistry.Registry

// ── the place a command acts on ──

/// The place a content command acts on, or `None` for a state-level step and for a `Batch` (whose sub-commands
/// each name their own place). The state-level runner resolves `Some place` to its zone before handing the
/// command to the zone-level runner, so a step acts on its own place, not the active one. Total: a case either
/// names its place or is one of the state-level steps listed below.
let placeOf (cmd: Command) : string option =
    match cmd with
    | Transform (place, _, _, _)
    | Rename (place, _, _, _)
    | ActorCreate (place, _, _)
    | ActorDelete (place, _, _)
    | PropCreate (place, _, _)
    | PropDelete (place, _, _)
    | PartCreate (place, _, _)
    | PartDelete (place, _, _)
    | WaypointCreate (place, _, _)
    | WaypointDelete (place, _, _)
    | TriggerCreate (place, _, _)
    | TriggerDelete (place, _, _)
    | Link (place, _, _, _)
    | TriggerSet (place, _, _, _)
    | LinkSpawn (place, _, _, _, _)
    | SetEnvironment (place, _, _)
    | SetLight (place, _, _)
    | SetPlayer (place, _, _) ->
        Some place
    | Batch _
    | ZoneMove _
    | ZoneAdd _
    | ZoneDelete _
    | ZonePresent _
    | ZoneStage _
    | ZonesClear _
    | SetCue _
    | Advent _
    | SetScenario _ ->
        None

// ── apply / undo a single command ──

let rec apply (zone: Zone) (physics: Physics.World) (assets: Assets.AssetManager) (cmd: Command) : unit =
    match cmd with
    | Transform (_, r, _, after) ->
        setTransform zone physics r after
    | Rename (_, r, _, after) ->
        setName zone r after
    | ActorCreate (_, r, actor) ->
        occupyActor zone physics assets r actor
    | ActorDelete (_, r, _) ->
        removeActor zone physics r
    | PropCreate (_, r, prop) ->
        occupyProp zone physics assets r prop
    | PropDelete (_, r, _) ->
        removeProp zone physics r
    | PartCreate (_, r, part) ->
        occupyPart zone physics assets r part
    | PartDelete (_, r, _) ->
        removePart zone physics r
    | WaypointCreate (_, r, waypoint) ->
        occupyWaypoint zone r waypoint
    | WaypointDelete (_, r, _) ->
        removeWaypoint zone r
    | TriggerCreate (_, r, trigger) ->
        occupyTrigger zone r trigger
    | TriggerDelete (_, r, _) ->
        removeTrigger zone r
    | Link (_, r, _, after) ->
        setWaypointNext zone r after
    | TriggerSet (_, r, _, after) ->
        setTrigger zone r after
    | LinkSpawn (_, from, _, r, waypoint) ->
        occupyWaypoint zone r waypoint
        setWaypointNext zone from (Some r)
    | SetEnvironment (_, _, next) ->
        setEnvironment zone physics assets next
    | SetLight (_, _, next) ->
        zone.Light <- next
    | SetPlayer (_, _, next) ->
        zone.Player <- next
    // A zone move is not an edit to one zone, so the zone-level runner cannot carry it out: `History.apply`
    // does, against the state (spec 048). The cue is the same kind of step (spec 049) — it is not a zone's
    // either. Reaching one of these here is a broken invariant, not a value to ignore.
    | ZoneMove _
    | ZoneAdd _
    | ZoneDelete _
    | ZonePresent _
    | ZoneStage _
    | ZonesClear _
    | SetCue _
    | Advent _
    | SetScenario _ ->
        invalidOp "Command.apply: a state-level step is on the gameplay state's history, not a zone edit"
    | Batch commands ->
        for cmd in commands do apply zone physics assets cmd

let rec undo (zone: Zone) (physics: Physics.World) (assets: Assets.AssetManager) (cmd: Command) : unit =
    match cmd with
    | Transform (_, r, before, _) ->
        setTransform zone physics r before
    | Rename (_, r, before, _) ->
        setName zone r before
    | ActorCreate (_, r, _) ->
        removeActor zone physics r
    | ActorDelete (_, r, actor) ->
        occupyActor zone physics assets r actor
    | PropCreate (_, r, _) ->
        removeProp zone physics r
    | PropDelete (_, r, prop) ->
        occupyProp zone physics assets r prop
    | PartCreate (_, r, _) ->
        removePart zone physics r
    | PartDelete (_, r, part) ->
        occupyPart zone physics assets r part
    | WaypointCreate (_, r, _) ->
        removeWaypoint zone r
    | WaypointDelete (_, r, waypoint) ->
        occupyWaypoint zone r waypoint
    | TriggerCreate (_, r, _) ->
        removeTrigger zone r
    | TriggerDelete (_, r, trigger) ->
        occupyTrigger zone r trigger
    | Link (_, r, before, _) ->
        setWaypointNext zone r before
    | TriggerSet (_, r, before, _) ->
        setTrigger zone r before
    | LinkSpawn (_, from, before, r, _) ->
        setWaypointNext zone from before
        removeWaypoint zone r
    | SetEnvironment (_, previous, _) ->
        setEnvironment zone physics assets previous
    | SetLight (_, previous, _) ->
        zone.Light <- previous
    | SetPlayer (_, previous, _) ->
        zone.Player <- previous
    | ZoneMove _
    | ZoneAdd _
    | ZoneDelete _
    | ZonePresent _
    | ZoneStage _
    | ZonesClear _
    | SetCue _
    | Advent _
    | SetScenario _ ->
        invalidOp "Command.undo: a state-level step is on the gameplay state's history, not a zone edit"
    | Batch commands ->
        // Backwards, so the reversal of a mutation happens before the reversal of what it depended on.
        for cmd in List.rev commands do undo zone physics assets cmd

// ── undo stack ──

type UndoStack =
    { Applied : Command list
      Redo : Command list }

module UndoStack =

    // Re-bind the enclosing module's command-level apply/undo under unambiguous
    // names — `Command` is also the name of the DU type, so `Command.undo` here
    // would resolve to the type, not this module.
    let private undoCommand zone physics assets cmd = undo zone physics assets cmd
    let private applyCommand zone physics assets cmd = apply zone physics assets cmd

    let create () : UndoStack =
        { Applied = []; Redo = [] }

    let push (t: UndoStack) (cmd: Command) : UndoStack =
        match t.Applied with
        | [] -> { Applied = [ cmd ]; Redo = [] }
        | prev :: rest ->

        // Coalesce consecutive transforms to the same entity into one step so a continuous gizmo drag / held
        // nudge undoes as a single action. `Place` is part of the key: an `EntityRef` index is per-zone, so two
        // places can hold the same index and must not merge (spec 077). Only a non-merge push clears the redo
        // stack (a merged step extends the same in-progress gesture).
        let merge (prev: Command) (newCmd: Command) : Command option =
            match prev, newCmd with
            | Transform (pa, a, before, _), Transform (pb, b, _, after) ->
                if a = b && pa = pb then
                    Some (Transform (pa, a, before, after))
                else None
            | _ -> None

        match merge prev cmd with
        | Some merged -> { t with Applied = merged :: rest }
        | None -> { Applied = cmd :: t.Applied; Redo = [] }

    // Stepping a stack against **one** zone. The editor does not use these: a step on the gameplay state's one
    // history may be a zone move, an add or a delete, which only `History` can carry out (spec 048). These stay
    // because they need no gameplay state at all, which is what the zone-level suite drives them with; a stack
    // whose commands name different places must be stepped through `History` instead (spec 077).
    let undo (t: UndoStack) (zone: Zone) (physics: Physics.World) (assets: Assets.AssetManager) : UndoStack =
        match t.Applied with
        | [] -> t
        | cmd :: rest ->

        undoCommand zone physics assets cmd
        { Applied = rest; Redo = cmd :: t.Redo }

    let redo (t: UndoStack) (zone: Zone) (physics: Physics.World) (assets: Assets.AssetManager) : UndoStack =
        match t.Redo with
        | [] -> t
        | cmd :: rest ->

        applyCommand zone physics assets cmd
        { Applied = cmd :: t.Applied; Redo = rest }
