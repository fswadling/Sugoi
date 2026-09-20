module Command

// Undoable editor commands. Each stores the data needed to apply and undo a
// mutation over the active zone, plus the affected `EntityRef` (kind, index,
// generation, and — for a part — its environment epoch). The generation and epoch
// are what let a restored entity be told apart from one that reused its slot.

open Entity
open Zone

type Command =
    | Spawn of Ref : EntityRef * Entity : Entity * Binding : AssetBinding
    | Delete of Ref : EntityRef * Entity : Entity * Binding : AssetBinding
    | Duplicate of Ref : EntityRef * Entity : Entity * Binding : AssetBinding
    | Transform of Ref : EntityRef * Before : Transform * After : Transform
    | Rename of Ref : EntityRef * Before : string * After : string
    | Link of Ref : EntityRef * Before : EntityRef option * After : EntityRef option
    // The extend gesture as one step: create `Ref` from `Entity` and point `From` at it.
    | LinkSpawn of From : EntityRef * Before : EntityRef option * Ref : EntityRef * Entity : Entity
    // A trigger's payload is richer than the base entity the shared cases carry, so the
    // kinds that destroy or copy one record it here.
    | TriggerSet of Ref : EntityRef * Before : Trigger * After : Trigger
    | TriggerDelete of Ref : EntityRef * Trigger : Trigger
    | TriggerDuplicate of Ref : EntityRef * Trigger : Trigger
    | SetEnvironment of Previous : Environment option * Next : Environment option

// ── apply / undo a single command ──

let apply (zone: Zone) (physics: Physics.World) (assets: Assets.AssetManager) (cmd: Command) : unit =
    match cmd with
    | Spawn (r, entity, binding)
    | Duplicate (r, entity, binding) ->
        setActorWithAsset zone physics assets r entity binding
    | Delete (r, _, _) ->
        removeEntity zone physics r
    | Transform (r, _, after) ->
        setTransform zone physics r after
    | Rename (r, _, after) ->
        setName zone r after
    | Link (r, _, after) ->
        setWaypointNext zone r after
    | LinkSpawn (from, _, r, entity) ->
        setActorWithAsset zone physics assets r entity bindingNone
        setWaypointNext zone from (Some r)
    | TriggerSet (r, _, after) ->
        setTrigger zone r after
    | TriggerDelete (r, _) ->
        removeEntity zone physics r
    | TriggerDuplicate (r, trigger) ->
        occupyTrigger zone r trigger
    | SetEnvironment (_, next) ->
        setEnvironment zone physics assets next

let undo (zone: Zone) (physics: Physics.World) (assets: Assets.AssetManager) (cmd: Command) : unit =
    match cmd with
    | Spawn (r, _, _)
    | Duplicate (r, _, _) ->
        removeEntity zone physics r
    | Delete (r, entity, binding) ->
        setActorWithAsset zone physics assets r entity binding
    | Transform (r, before, _) ->
        setTransform zone physics r before
    | Rename (r, before, _) ->
        setName zone r before
    | Link (r, before, _) ->
        setWaypointNext zone r before
    | LinkSpawn (from, before, r, _) ->
        setWaypointNext zone from before
        removeEntity zone physics r
    | TriggerSet (r, before, _) ->
        setTrigger zone r before
    | TriggerDelete (r, trigger) ->
        occupyTrigger zone r trigger
    | TriggerDuplicate (r, _) ->
        removeEntity zone physics r
    | SetEnvironment (previous, _) ->
        setEnvironment zone physics assets previous

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
