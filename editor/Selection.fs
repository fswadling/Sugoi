module Selection

// Selection edits shared by the Outliner (inline Del/Dup per row, plus the
// selection-based buttons) and the editor keyboard shortcuts (Del / Ctrl+D).
// The `*Ref` variants act on a specific entity, for the inline row buttons.

open Entity
open Command

// Delete a specific entity. Clears the selection only when it was the deleted
// entity; otherwise leaves selection/drag untouched.
let deleteRef (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (ref: EntityRef) : unit =
    let entity = Zone.tryEntityAt state.Zone ref
    match entity with
    | None -> ()
    | Some e ->

    let binding = Zone.bindingAt state.Zone ref
    let cmd = Delete (ref, e, binding)
    apply state.Zone shared.Physics shared.Assets cmd
    let undo = UndoStack.push state.Editor.Undo cmd
    let editor =
        if state.Editor.Selection = Some ref then
            { state.Editor with Selection = None; Drag = None }
        else
            state.Editor
    state.Editor <- { editor with Undo = undo }

// Delete the selected entity.
let delete (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    match state.Editor.Selection with
    | None -> ()
    | Some ref ->
    do deleteRef shared &state ref

// Duplicate a specific entity and select the copy.
let duplicateRef (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (ref: EntityRef) : unit =
    let entity = Zone.tryEntityAt state.Zone ref
    match entity with
    | None -> ()
    | Some e ->
    do Spawning.duplicate shared &state ref e

// Duplicate the selected entity.
let duplicate (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    match state.Editor.Selection with
    | None -> ()
    | Some ref -> duplicateRef shared &state ref

// Drop a Part selection left dangling by an environment swap: its epoch no longer
// resolves, so the inspector would silently show nothing. Actor/Prop selections are
// left alone — a stale one can resolve again when undo/redo restores its slot.
let dropStalePartSelection (state: byref<GameplayState.GameplayState>) : unit =
    match state.Editor.Selection with
    | Some ref when ref.Kind = EntityKind.Part && not (Zone.isAlive state.Zone ref) ->
        state.Editor <- { state.Editor with Selection = None; Drag = None }
    | _ -> ()
