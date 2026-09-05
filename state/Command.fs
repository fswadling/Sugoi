module Command

// Undoable editor commands. Each command stores the data needed to apply and
// undo a mutation over the active zone, plus the affected `EntityRef` (an index,
// not a pointer), so undo survives entity deletion and re-creation.

open Entity
open Zone

type Command =
    | Spawn of Ref : EntityRef * Entity : Entity * Binding : AssetBinding
    | Delete of Ref : EntityRef * Entity : Entity * Binding : AssetBinding
    | Duplicate of Ref : EntityRef * Entity : Entity * Binding : AssetBinding
    | Transform of Ref : EntityRef * Before : Transform * After : Transform
    | SetEnvironment of PreviousAssetId : string * NewAssetId : string

// ── apply / undo a single command ──

let apply (zone: Zone) (assets: Assets.AssetManager) (cmd: Command) : unit =
    match cmd with
    | Spawn (r, entity, binding)
    | Duplicate (r, entity, binding) ->
        setActorWithAsset zone assets r entity binding
    | Delete (r, _, _) ->
        removeEntity zone r
    | Transform (r, _, after) ->
        Option.iter (fun (e: Entity) -> setEntity zone r { e with Transform = after }) (entityAt zone r)
    | SetEnvironment (_, newAssetId) ->
        setEnvironment zone assets newAssetId

let undo (zone: Zone) (assets: Assets.AssetManager) (cmd: Command) : unit =
    match cmd with
    | Spawn (r, _, _)
    | Duplicate (r, _, _) ->
        removeEntity zone r
    | Delete (r, entity, binding) ->
        setActorWithAsset zone assets r entity binding
    | Transform (r, before, _) ->
        Option.iter (fun (e: Entity) -> setEntity zone r { e with Transform = before }) (entityAt zone r)
    | SetEnvironment (previousAssetId, _) ->
        setEnvironment zone assets previousAssetId

// ── undo stack ──

type UndoStack =
    { Applied : Command list
      Redo : Command list }

module UndoStack =

    // Re-bind the enclosing module's command-level apply/undo under unambiguous
    // names — `Command` is also the name of the DU type, so `Command.undo` here
    // would resolve to the type, not this module.
    let private undoCommand zone assets cmd = undo zone assets cmd
    let private applyCommand zone assets cmd = apply zone assets cmd

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
                if a.Kind = b.Kind && a.Index = b.Index then
                    Some (Transform (a, before, after))
                else None
            | _ -> None

        match merge prev cmd with
        | Some merged -> { t with Applied = merged :: rest }
        | None -> { Applied = cmd :: t.Applied; Redo = [] }

    let undo (t: UndoStack) (zone: Zone) (assets: Assets.AssetManager) : UndoStack =
        match t.Applied with
        | [] -> t
        | cmd :: rest ->

        undoCommand zone assets cmd
        { Applied = rest; Redo = cmd :: t.Redo }

    let redo (t: UndoStack) (zone: Zone) (assets: Assets.AssetManager) : UndoStack =
        match t.Redo with
        | [] -> t
        | cmd :: rest ->

        applyCommand zone assets cmd
        { Applied = cmd :: t.Applied; Redo = rest }
