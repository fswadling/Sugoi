module Selection

// Selection edits shared by the Outliner (inline Del/Dup per row, plus the
// selection-based buttons) and the editor keyboard shortcuts (Del / Ctrl+D).
// The `*Ref` variants act on a specific entity, for the inline row buttons.

open Entity
open Command

// Delete a specific entity. Clears the selection only when it was the deleted
// entity; otherwise leaves selection/drag untouched.
let deleteRef (assets: Assets.AssetManager) (state: byref<GameplayState.GameplayState>) (ref: EntityRef) : unit =
    let entity = Zone.entityAt state.Zone ref
    match entity with
    | None -> ()
    | Some e ->

    let assetId = Zone.assetIdAt state.Zone ref
    let cmd = Delete (ref, e, assetId)
    apply state.Zone assets cmd
    let undo = UndoStack.push state.Editor.Undo cmd
    let editor =
        if state.Editor.Selection = Some ref then
            { state.Editor with Selection = None; Drag = None }
        else
            state.Editor
    state.Editor <- { editor with Undo = undo }

// Delete the selected entity.
let delete (assets: Assets.AssetManager) (state: byref<GameplayState.GameplayState>) : unit =
    match state.Editor.Selection with
    | None -> ()
    | Some ref -> deleteRef assets &state ref

// Duplicate a specific entity and select the copy.
let duplicateRef (assets: Assets.AssetManager) (state: byref<GameplayState.GameplayState>) (ref: EntityRef) : unit =
    let entity = Zone.entityAt state.Zone ref
    match entity with
    | None -> ()
    | Some e ->
    match ref.Kind with
    | EntityKind.Actor ->
        let assetId = Zone.assetIdAt state.Zone ref
        let newRef = Zone.spawnActor state.Zone assets e assetId
        let undo = UndoStack.push state.Editor.Undo (Duplicate (newRef, e, assetId))
        state.Editor <- { state.Editor with Selection = Some newRef; Undo = undo }
    | EntityKind.Prop ->
        let newRef = Zone.spawnProp state.Zone e
        let undo = UndoStack.push state.Editor.Undo (Duplicate (newRef, e, ""))
        state.Editor <- { state.Editor with Selection = Some newRef; Undo = undo }
    | _ -> ()

// Duplicate the selected entity.
let duplicate (assets: Assets.AssetManager) (state: byref<GameplayState.GameplayState>) : unit =
    match state.Editor.Selection with
    | None -> ()
    | Some ref -> duplicateRef assets &state ref
