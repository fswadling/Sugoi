module Selection

// Selection edits shared by the Outliner buttons and the editor keyboard
// shortcuts (Del / Ctrl+D). These mutate the single editor selection.

open Entity

let delete (state: GameplayState.GameplayState) : GameplayState.GameplayState =
    match state.Editor.Selection with
    | None -> state
    | Some ref ->

    let entity = Zone.entityAt state.Zone ref
    match entity with
    | None -> state
    | Some _ ->

    { state with
        Zone = Zone.removeEntity state.Zone ref
        Editor = { state.Editor with Selection = None; Drag = None } }

let duplicate (state: GameplayState.GameplayState) : GameplayState.GameplayState =
    match state.Editor.Selection with
    | None -> state
    | Some ref ->

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
