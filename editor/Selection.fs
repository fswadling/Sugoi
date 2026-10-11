module Selection

// Selection edits shared by the Outliner (inline Del/Dup per row, plus the
// selection-based buttons) and the editor keyboard shortcuts (Del / Ctrl+D).
// The `*Ref` variants act on a specific entity, for the inline row buttons.

open Entity
open Command

// Delete a specific entity, recording it by kind so undo restores it in full. Clears the
// selection only when it was the deleted entity; otherwise leaves selection/drag untouched.
let deleteRef (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (ref: EntityRef) : unit =
    let zone = GameplayState.activeZone state
    let entity = Zone.tryEntityAt zone ref
    match entity with
    | None -> ()
    | Some _ ->

    // Every kind records its own record; there is no kind-agnostic payload left to fall
    // back to, so an unhandled kind cannot slip through quietly.
    let cmd =
        match ref.Kind with
        | EntityKind.Actor -> ActorDelete (state.ActiveZone, ref, Zone.captureActor zone ref)
        | EntityKind.Prop -> PropDelete (state.ActiveZone, ref, Zone.captureProp zone ref)
        | EntityKind.Part -> PartDelete (state.ActiveZone, ref, Zone.capturePart zone ref)
        | EntityKind.Waypoint -> WaypointDelete (state.ActiveZone, ref, Zone.captureWaypoint zone ref)
        | EntityKind.Trigger -> TriggerDelete (state.ActiveZone, ref, Zone.captureTrigger zone ref)
        | EntityKind.Environment -> invalidOp "deleteRef: the environment is not an addressable entity"
    // Through the state-level runner so undo/redo reverse it in one place.
    do History.commit shared &state cmd
    if state.Editor.Selection = Some ref then
        state.Editor <- { state.Editor with Selection = None; Drag = None }

// Delete the selected entity.
let delete (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    match state.Editor.Selection with
    | None -> ()
    | Some ref ->
    do deleteRef shared &state ref

// Duplicate a specific entity and select the copy.
let duplicateRef (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (ref: EntityRef) : unit =
    let entity = Zone.tryEntityAt (GameplayState.activeZone state) ref
    match entity with
    | None -> ()
    | Some e ->
    do Spawning.duplicate shared &state ref e

// Duplicate the selected entity.
let duplicate (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    match state.Editor.Selection with
    | None -> ()
    | Some ref -> duplicateRef shared &state ref

// Which entity a finished text-field edit should rename, and to what. `owner` is the
// entity the edited text was typed for — not necessarily the current selection, which
// can change underneath an in-progress edit — and `finished` is the field's commit
// signal (submitted, or left after an edit). None means "commit nothing": nothing was
// being edited, the edit is not finished, or the text is empty. Whether the value
// actually changes is left to the caller. The Inspector uses this for both its text
// fields, the entity name and a trigger's prompt.
let renameTarget (owner: EntityRef option) (finished: bool) (text: string) : (EntityRef * string) option =
    match owner with
    | Some ref when finished ->
        let name = text.Trim()
        if name = "" then None else Some (ref, name)
    | _ -> None

// Rename a specific entity as one undoable step. No-op when the ref no longer resolves
// or the name is unchanged; an empty or whitespace-only name is refused (the inspector
// never commits one). Selection and drag are left alone: a rename is a label change,
// not a change of what is being edited.
let renameRef (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (ref: EntityRef) (name: string) : unit =
    let name = name.Trim()
    if name = "" then invalidOp "renameRef: the name is empty" else

    let zone = GameplayState.activeZone state
    let entity = Zone.tryEntityAt zone ref
    match entity with
    | None -> ()
    | Some e ->
    if name = e.Name then () else

    let cmd = Rename (state.ActiveZone, ref, e.Name, name)
    apply zone state.Physics shared.Assets cmd
    let undo = UndoStack.push state.Editor.Undo cmd
    state.Editor <- { state.Editor with Undo = undo }

// Point a waypoint at another (`Some`), or end its chain (`None`), as one undoable
// step. No-op when it already points there. Selection and drag are left alone: the
// chain changes, not what is being edited.
let linkRef (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (ref: EntityRef) (next: EntityRef option) : unit =
    let zone = GameplayState.activeZone state
    let before = Zone.waypointNext zone ref
    if before = next then () else

    let cmd = Link (state.ActiveZone, ref, before, next)
    apply zone state.Physics shared.Assets cmd
    let undo = UndoStack.push state.Editor.Undo cmd
    state.Editor <- { state.Editor with Undo = undo }

// Apply `change` to a trigger's authored value as one undoable step, recorded against the
// ref the edit was made for (which is not always the selected one: a text field can outlive
// a selection change). No-op when the ref no longer resolves or the change is a no-op, so
// leaving a widget without editing it records nothing.
let updateTriggerRef (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (ref: EntityRef) (change: TriggerState -> TriggerState) : unit =
    let zone = GameplayState.activeZone state
    match Zone.tryTriggerAt zone ref with
    | ValueNone -> ()
    | ValueSome trigger ->
    let before = trigger.State
    let after = change before
    if after = before then () else

    let cmd = TriggerSet (state.ActiveZone, ref, before, after)
    apply zone state.Physics shared.Assets cmd
    let undo = UndoStack.push state.Editor.Undo cmd
    state.Editor <- { state.Editor with Undo = undo }

// Apply `change` to the active zone's light as one undoable step (spec 043, FR-006). `before` is
// the zone's light as it still stands — a control holds its edit in a local until the commit — so
// one drag is one step; a commit that changes nothing records nothing, exactly as `updateTriggerRef`
// above.
let setLight (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (change: Texture.LightSettings -> Texture.LightSettings) : unit =
    let zone = GameplayState.activeZone state
    let before = zone.Light
    let after = change before
    if after = before then () else

    let cmd = SetLight (state.ActiveZone, before, after)
    apply zone state.Physics shared.Assets cmd
    let undo = UndoStack.push state.Editor.Undo cmd
    state.Editor <- { state.Editor with Undo = undo }

// Bind `ref`'s actor as the active zone's player as one undoable step (spec 045). The ref is checked
// before any command exists — `playerHandleOf` fails loudly for a non-actor — and binding the actor who
// already is the player records nothing, so the control can be pressed twice without an empty step.
let setPlayer (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (ref: EntityRef) : unit =
    let zone = GameplayState.activeZone state
    let before = zone.Player
    let after = ValueSome (Systems.playerHandleOf ref)
    if after = before then () else

    let cmd = SetPlayer (state.ActiveZone, before, after)
    apply zone state.Physics shared.Assets cmd
    let undo = UndoStack.push state.Editor.Undo cmd
    state.Editor <- { state.Editor with Undo = undo }

// Drop a Part selection left dangling by an environment swap: its epoch no longer
// resolves, so the inspector would silently show nothing. Actor/Prop selections are
// left alone — a stale one can resolve again when undo/redo restores its slot.
let dropStalePartSelection (state: byref<GameplayState.GameplayState>) : unit =
    match state.Editor.Selection with
    | Some ref when ref.Kind = EntityKind.Part && not (Zone.isAlive (GameplayState.activeZone state) ref) ->
        state.Editor <- { state.Editor with Selection = None; Drag = None }
    | _ -> ()
