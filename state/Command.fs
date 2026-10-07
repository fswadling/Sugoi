module Command

// Undoable editor commands. Each stores the data needed to apply and undo a
// mutation over the active zone, plus the affected `EntityRef` (kind, index,
// generation, and — for a part — its environment epoch). The generation and epoch
// are what let a restored entity be told apart from one that reused its slot.
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
open Advent
open Casting
open Place
open Scenario

type Command =
    // An edit of the properties every kind shares.
    | Transform of Ref : EntityRef * Before : Transform * After : Transform
    | Rename of Ref : EntityRef * Before : string * After : string
    // One create and one delete per kind, carrying the kind's own authored value.
    | ActorCreate of Ref : EntityRef * Actor : ActorState
    | ActorDelete of Ref : EntityRef * Actor : ActorState
    | PropCreate of Ref : EntityRef * Prop : PropState
    | PropDelete of Ref : EntityRef * Prop : PropState
    | PartCreate of Ref : EntityRef * Part : PropState
    | PartDelete of Ref : EntityRef * Part : PropState
    | WaypointCreate of Ref : EntityRef * Waypoint : Waypoint
    | WaypointDelete of Ref : EntityRef * Waypoint : Waypoint
    | TriggerCreate of Ref : EntityRef * Trigger : TriggerState
    | TriggerDelete of Ref : EntityRef * Trigger : TriggerState
    // A property edit that belongs to one kind: a waypoint's link, a trigger's shape.
    | Link of Ref : EntityRef * Before : EntityRef option * After : EntityRef option
    | TriggerSet of Ref : EntityRef * Before : TriggerState * After : TriggerState
    // The extend gesture as one step: create `Ref` from `Waypoint` and point `From` at it.
    | LinkSpawn of From : EntityRef * Before : EntityRef option * Ref : EntityRef * Waypoint : Waypoint
    // Zone-level rather than an entity: swapping the loaded environment.
    | SetEnvironment of Previous : Environment option * Next : Environment option
    // Zone-level rather than an entity: the authored light (spec 043). Pushed by the Inspector's
    // committed edits and carried by an instantiate, so a light change is reversible like any other.
    | SetLight of Previous : LightSettings * Next : LightSettings
    // Zone-level rather than an entity: the player designation (spec 045) — which of the zone's actors is
    // the avatar. The payload is the value `Player` already holds, so no fourth spelling of it is
    // introduced, and `Previous` is what makes undoing a re-designation restore the *previous actor*
    // rather than no player at all.
    | SetPlayer of Previous : Handle voption * Next : Handle voption
    // Several mutations that were one gesture: applied forwards, reversed backwards, so a create and
    // the delete that preceded it cannot come apart. One entry on the stack, like any other command,
    // and a step that puts a zone back exactly is what needs it (spec 035 FR-005).
    | Batch of Commands : Command list
    // The active place moved (spec 048). The one case that is not a zone edit: recording the moves is what
    // keeps the active place the one the step being undone belongs to, so every other command may go on naming
    // no zone and acting on whatever place is active, and the history may be one timeline rather than one per
    // zone.
    | ZoneMove of Previous : Place * Next : Place
    // A place was made present, and made active (spec 048, 061). `Previous` is the place active before the
    // add, which its undo restores: an add and the active change it causes are **one** step, so one click is
    // one undo.
    | ZoneAdd of Previous : Place * Name : Place
    // A place was made absent (spec 048, 061), carrying everything needed to put its zone back. The removed
    // place is never the active one (spec 061 FR-011), so `Previous` is the active place and is unchanged by
    // the delete; it is kept so the undo's temporary re-activation can be reversed.
    | ZoneDelete of Previous : Place * Name : Place * Contents : ZoneContents
    // The screen's cue was edited (spec 049). It is not a zone's — a cue spans zones (spec 046 FR-013) — so, like
    // a zone step, it is carried out against the gameplay state and not by the zone-level runner. The payload is
    // the two cue values: a cue is immutable, so a snapshot is a reference, and that is exactly what the cue's
    // own history stored before this feature. `Cue.Cue` is module-qualified because the module and the type share
    // the name; `state/EditorState.fs` writes it the same way.
    | SetCue of Previous : Cue.Cue * Next : Cue.Cue
    // The named adventure flags were edited (spec 055). Like `SetCue`, it is not a zone's — the set is project state on
    // `SharedState` (`specs/055` FR-002) — so it is a state-level step carried by `History` and never by the zone-level
    // runner. The payload is the two set values: the set is immutable, so a snapshot is the set itself, and a before/after
    // pair makes undo total (an add and a remove are the same shape).
    | Advent of Previous : Set<Advent> * Next : Set<Advent>
    // The scenario library was edited (spec 070): a registration, a re-registration or a clear, as a before/after
    // pair of the entry (`ScenarioContents option`). Like `SetCue` and `Advent`, it is not a zone's — a scenario
    // spans zones — so it is a state-level step carried by `History` and never by the zone-level runner. The payload
    // is a `ScenarioContents`, whose zones are an immutable `Map`, so a snapshot is a value the step can hold.
    | SetScenario of Name : Scenario * Previous : ScenarioRegistry.ScenarioContents option * Next : ScenarioRegistry.ScenarioContents option

// ── apply / undo a single command ──

let rec apply (zone: Zone) (physics: Physics.World) (assets: Assets.AssetManager) (cmd: Command) : unit =
    match cmd with
    | Transform (r, _, after) ->
        setTransform zone physics r after
    | Rename (r, _, after) ->
        setName zone r after
    | ActorCreate (r, actor) ->
        occupyActor zone physics assets r actor
    | ActorDelete (r, _) ->
        removeActor zone physics r
    | PropCreate (r, prop) ->
        occupyProp zone physics assets r prop
    | PropDelete (r, _) ->
        removeProp zone physics r
    | PartCreate (r, part) ->
        occupyPart zone physics assets r part
    | PartDelete (r, _) ->
        removePart zone physics r
    | WaypointCreate (r, waypoint) ->
        occupyWaypoint zone r waypoint
    | WaypointDelete (r, _) ->
        removeWaypoint zone r
    | TriggerCreate (r, trigger) ->
        occupyTrigger zone r trigger
    | TriggerDelete (r, _) ->
        removeTrigger zone r
    | Link (r, _, after) ->
        setWaypointNext zone r after
    | TriggerSet (r, _, after) ->
        setTrigger zone r after
    | LinkSpawn (from, _, r, waypoint) ->
        occupyWaypoint zone r waypoint
        setWaypointNext zone from (Some r)
    | SetEnvironment (_, next) ->
        setEnvironment zone physics assets next
    | SetLight (_, next) ->
        zone.Light <- next
    | SetPlayer (_, next) ->
        zone.Player <- next
    // A zone move is not an edit to one zone, so the zone-level runner cannot carry it out: `History.apply`
    // does, against the state (spec 048). The cue is the same kind of step (spec 049) — it is not a zone's
    // either. Reaching one of these here is a broken invariant, not a value to ignore.
    | ZoneMove _
    | ZoneAdd _
    | ZoneDelete _
    | SetCue _
    | Advent _
    | SetScenario _ ->
        invalidOp "Command.apply: a state-level step is on the gameplay state's history, not a zone edit"
    | Batch commands ->
        for cmd in commands do apply zone physics assets cmd

let rec undo (zone: Zone) (physics: Physics.World) (assets: Assets.AssetManager) (cmd: Command) : unit =
    match cmd with
    | Transform (r, before, _) ->
        setTransform zone physics r before
    | Rename (r, before, _) ->
        setName zone r before
    | ActorCreate (r, _) ->
        removeActor zone physics r
    | ActorDelete (r, actor) ->
        occupyActor zone physics assets r actor
    | PropCreate (r, _) ->
        removeProp zone physics r
    | PropDelete (r, prop) ->
        occupyProp zone physics assets r prop
    | PartCreate (r, _) ->
        removePart zone physics r
    | PartDelete (r, part) ->
        occupyPart zone physics assets r part
    | WaypointCreate (r, _) ->
        removeWaypoint zone r
    | WaypointDelete (r, waypoint) ->
        occupyWaypoint zone r waypoint
    | TriggerCreate (r, _) ->
        removeTrigger zone r
    | TriggerDelete (r, trigger) ->
        occupyTrigger zone r trigger
    | Link (r, before, _) ->
        setWaypointNext zone r before
    | TriggerSet (r, before, _) ->
        setTrigger zone r before
    | LinkSpawn (from, before, r, _) ->
        setWaypointNext zone from before
        removeWaypoint zone r
    | SetEnvironment (previous, _) ->
        setEnvironment zone physics assets previous
    | SetLight (previous, _) ->
        zone.Light <- previous
    | SetPlayer (previous, _) ->
        zone.Player <- previous
    | ZoneMove _
    | ZoneAdd _
    | ZoneDelete _
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

        // Coalesce consecutive transforms to the same ref into one step so a
        // continuous gizmo drag / held nudge undoes as a single action. Only a
        // non-merge push clears the redo stack (a merged step extends the same
        // in-progress gesture).
        let merge (prev: Command) (newCmd: Command) : Command option =
            match prev, newCmd with
            | Transform (a, before, _), Transform (b, _, after) ->
                if a = b then
                    Some (Transform (a, before, after))
                else None
            | _ -> None

        match merge prev cmd with
        | Some merged -> { t with Applied = merged :: rest }
        | None -> { Applied = cmd :: t.Applied; Redo = [] }

    // Stepping a stack against **one** zone. The editor does not use these: a step on the gameplay state's one
    // history may be a zone move, an add or a delete, which only `History` can carry out (spec 048). These stay
    // because they need no gameplay state at all, which is what the zone-level suite drives them with.
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
