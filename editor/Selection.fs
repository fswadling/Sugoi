module Selection

// Selection edits shared by the Outliner (inline Del/Dup per row, plus the
// selection-based buttons) and the editor keyboard shortcuts (Del / Ctrl+D).
// The `*Ref` variants act on a specific entity, for the inline row buttons.

open Entity

// Delete a specific entity. Clears the selection only when it was the deleted
// entity; otherwise leaves selection/drag untouched.
let deleteRef (state: GameplayState.GameplayState) (ref: EntityRef) : GameplayState.GameplayState =
    let entity = Zone.entityAt state.Zone ref
    match entity with
    | None -> state
    | Some _ ->

    let editor =
        if state.Editor.Selection = Some ref then
            { state.Editor with Selection = None; Drag = None }
        else
            state.Editor
    { state with Zone = Zone.removeEntity state.Zone ref; Editor = editor }

// Delete the selected entity.
let delete (state: GameplayState.GameplayState) : GameplayState.GameplayState =
    match state.Editor.Selection with
    | None -> state
    | Some ref -> deleteRef state ref

// Duplicate a specific entity and select the copy.
let duplicateRef (state: GameplayState.GameplayState) (ref: EntityRef) : GameplayState.GameplayState =
    let entity = Zone.entityAt state.Zone ref
    match entity with
    | None -> state
    | Some e ->
    match ref.Kind with
    | EntityKind.Actor ->
        let zone, newRef = Zone.spawnActor state.Zone e
        { state with Zone = zone; Editor = { state.Editor with Selection = Some newRef } }
    | EntityKind.Prop ->
        let zone, newRef = Zone.spawnProp state.Zone e
        { state with Zone = zone; Editor = { state.Editor with Selection = Some newRef } }
    | _ -> state

// Duplicate the selected entity.
let duplicate (state: GameplayState.GameplayState) : GameplayState.GameplayState =
    match state.Editor.Selection with
    | None -> state
    | Some ref -> duplicateRef state ref
