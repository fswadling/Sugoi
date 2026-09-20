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
    // Several mutations that were one gesture: applied forwards, reversed backwards, so a create and
    // the delete that preceded it cannot come apart. One entry on the stack, like any other command,
    // and a step that puts a zone back exactly is what needs it (spec 035 FR-005).
    | Batch of Commands : Command list

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
