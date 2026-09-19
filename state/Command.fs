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
