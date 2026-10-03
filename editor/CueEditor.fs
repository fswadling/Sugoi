module CueEditor

// The Cue panel (spec 047) and the Inspector's inline trigger-cue editor (spec 058). Both draw a cue through
// the one shared control (`CueTree`) and write each accepted edit back as a single step on the one undo history:
// the screen's cue is one `SetCue` step (`specs/049`), and a trigger's authored cue is one `TriggerSet` step —
// the command already carries the trigger's whole state, so the cue needs no command of its own (`specs/058`).
//
// The panel takes only the gameplay state. Nothing it needs is in `SharedState`: the control is the cue's, and a
// zone name is typed rather than chosen, because a dropdown would have the control read the zone registry
// (`FR-006`). A camera op's waypoint *is* chosen from a combo — but from the active zone's waypoints, read
// here in the panel, so the control still names no zone. The inline trigger editor additionally takes `SharedState`,
// because it commits a zone edit through `Selection.updateTriggerRef`, which needs it.
//
// The panel reads `state.Cue` each frame rather than keeping a copy, which is what makes "the screen's own
// cue, not one kept in step with it" true by construction (`FR-003`), and why an op that has run needs no
// filtering: it is not in the value any more. The inline editor likewise reads the selected trigger's cue each
// frame. Each keeps its own node selection and text field, so the two editors cannot collide.

open System.Text
open Entity
open Cue
open Command

// ── the text field's NUL-terminated buffer ──

let private seedText (buffer: byte[]) (text: string) : unit =
    let charCount = min text.Length (EditorState.cueTextBufferSize - 1)
    let byteCount = Encoding.UTF8.GetBytes(text, 0, charCount, buffer, 0)
    buffer.[byteCount] <- 0uy

let private bufferText (buffer: byte[]) : string =
    let zero = System.Array.IndexOf(buffer, 0uy)
    let length = if zero < 0 then buffer.Length else zero
    if length = 0 then "" else Encoding.UTF8.GetString(buffer, 0, length)

// ── the write path (the screen's cue) ──

/// Apply an accepted edit to the screen's cue: the next cue written in place, and one `SetCue` step pushed onto
/// the one history (spec 049). The control decides whether anything changed, so this always records a step — there
/// is no second opinion here about whether an edit "counted". It takes only the state: routing through
/// `History.commit` would make the panel take `SharedState`, which it does not need.
let commit (state: byref<GameplayState.GameplayState>) (next: Cue) : unit =
    let before = state.Cue
    state.Cue <- next
    state.Editor <- { state.Editor with Undo = UndoStack.push state.Editor.Undo (SetCue (before, next)) }

// ── the selected node's text ──

/// The selected node's text field, drawn for a caller's own transient state and committed through `onCommit`.
/// The buffer and the node `path` belongs to are the caller's (a control that named `EditorState` could not be
/// reused where the cue is stored elsewhere, or by a second editor), and it follows the Inspector's `NameBuffer`
/// rule: re-seeded — and re-pointed at the node it belongs to — only while the field is *not* live, and committed
/// once, so an edit in progress is never overwritten and its text is never applied to a different node. Returns
/// the field's path and liveness for the caller to store.
let private drawText
    (buffer: byte[])
    (path: int list option)
    (editing: bool)
    (idPrefix: string)
    (nodePath: int list)
    (node: Cue)
    (cue: Cue)
    (current: string)
    (onCommit: Cue -> unit)
    : int list option * bool =

    let path =
        if editing then path
        else
            do seedText buffer current
            Some nodePath

    let submitted =
        ImGui.inputText("Text##" + CueTree.rowId idPrefix nodePath, buffer, unativeint buffer.Length, ImGui.enterReturnsTrue ||| ImGui.autoSelectAll, System.IntPtr.Zero, System.IntPtr.Zero)
    let finished = submitted || ImGui.isItemDeactivatedAfterEdit()
    let text = bufferText buffer

    // Committed only for the node the buffer belongs to. The selection can change while the field is live
    // (the author clicks another row), and a deactivation in that same frame must not write this text there.
    if finished && path = Some nodePath && text <> current then
        onCommit (CueTree.withAt nodePath (CueTree.withText node text) cue)

    path, ImGui.isItemActive()

// ── the selected camera op's waypoint ──

/// The selected camera op's waypoint, chosen from the active zone's live waypoints rather than typed, so the
/// name the op names always resolves (`Zone.tryWaypointByName`, the resolution the engine uses). The caller
/// reads the zone — the control names none (`FR-006`) — and the write is the control's own `withText`, which
/// for a camera op replaces the waypoint and keeps a `FlyCamera`'s speed. A value that names no live waypoint
/// (the waypoint was deleted, or the op belongs to another zone) still shows; the combo only ever writes a
/// name it offers.
let private drawWaypointCombo
    (zone: Zone.Zone)
    (idPrefix: string)
    (nodePath: int list)
    (node: Cue)
    (cue: Cue)
    (current: string)
    (onCommit: Cue -> unit)
    : unit =

    let id = CueTree.rowId idPrefix nodePath
    let preview = if current = "" then "(none)" else current
    if ImGui.beginCombo("Waypoint##" + id, preview, 0) then
        let slots = zone.Waypoints.Slots
        for i in 0 .. slots.Length - 1 do
            let slot = slots.[i]
            if slot.Alive then
                let name = slot.Value.Base.Name
                // The slot index keeps two waypoints sharing a name apart in ImGui; the *name* is what the op
                // resolves by, so choosing either writes the same thing, as at run time.
                if ImGui.selectableBool(name + "##wp" + id + string i, name = current, 0, ImGui.ImVec2(0.0f, 0.0f)) && name <> current then
                    onCommit (CueTree.withAt nodePath (CueTree.withText node name) cue)
        do ImGui.endCombo()

// ── the selected node's text or waypoint, whichever it carries ──

/// Draw the editor for `path`'s node after `drawEditor`: a camera op chooses its waypoint, every other
/// text-bearing node is typed into. `onCommit` is handed the next cue. A node that carries text is never
/// executing — only the two in-flight leaves are — so this needs no lock check (`CueTree.textOf` says so).
let private drawNodeText
    (zone: Zone.Zone)
    (buffer: byte[])
    (textPath: int list option)
    (editing: bool)
    (idPrefix: string)
    (path: int list)
    (node: Cue)
    (cue: Cue)
    (onCommit: Cue -> unit)
    : int list option * bool =

    match node with
    | SetCamera current | FlyCamera (current, _) ->
        do drawWaypointCombo zone idPrefix path node cue current onCommit
        textPath, editing
    | _ ->
        match CueTree.textOf node with
        | ValueSome current -> drawText buffer textPath editing idPrefix path node cue current onCommit
        | ValueNone -> textPath, editing

// ── the Cue panel ──

let draw (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Cue", System.IntPtr.Zero, 0) |> ignore

    // The cue is the screen's own value, read where it lives: what it shows shrinks as the cue runs, so an
    // op that has already run needs no row of its own (FR-002, FR-003).
    let cue = state.Cue
    let idPrefix = "cue"
    let zone = GameplayState.activeZone state

    // ── the tree, then the selected node's editor ──
    // An edit accepted here shows in the tree next frame; the tree above the click does not move, so the lag
    // is invisible.
    let mutable selection = state.Editor.CueSelection
    match CueTree.drawTree idPrefix selection cue with
    | Some clicked -> selection <- Some clicked
    | None -> ()

    match selection with
    | Some path ->

        // The control owns the rebuild and hands back the next cue rather than writing it: a byref cannot
        // be captured by the callback it draws through, so the write is staged here and applied in one
        // place, as one step.
        let mutable pending : Cue option = None
        selection <- CueTree.drawEditor idPrefix cue path (fun next -> pending <- Some next)
        match pending with
        | Some next -> do commit &state next
        | None -> ()

        match selection with
        | Some path' ->
            match CueTree.tryAt path' state.Cue with
            | Some node ->
                let mutable textPending : Cue option = None
                let (path'', editing) =
                    drawNodeText zone state.Editor.CueTextBuffer state.Editor.CueTextPath state.Editor.CueTextEditing idPrefix path' node state.Cue (fun next -> textPending <- Some next)
                state.Editor <- { state.Editor with CueTextPath = path''; CueTextEditing = editing }
                match textPending with
                | Some next -> do commit &state next
                | None -> ()
            | None -> ()
        | None -> ()

    | None -> ()

    state.Editor <- { state.Editor with CueSelection = selection }
    do ImGui.endWindow()

// ── the Inspector's inline trigger-cue editor ──

/// The inline editor's transient fields, re-pointed at `ref` — and reset — when the selected trigger changes, so
/// a node selection or text typed for one trigger is never applied to another. Pure (no drawing), so the reset
/// rule is assertable headlessly.
let resetTriggerEditor (state: byref<GameplayState.GameplayState>) (ref: EntityRef) : unit =
    if state.Editor.TriggerCueRef <> Some ref then
        state.Editor <-
            { state.Editor with
                TriggerCueRef = Some ref
                TriggerCueSelection = None
                TriggerCueTextPath = None
                TriggerCueEditing = false }

/// Draw the selected trigger's authored cue inline, and commit each accepted edit as one `TriggerSet` step
/// recorded against that trigger (spec 058). The trigger's transient editor state is its own: when
/// `TriggerCueRef` names a different trigger the fields reset, so a node selection or text typed for one trigger
/// is never applied to another. An authored cue is never executing, so the control offers every edit.
let drawTrigger (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (ref: EntityRef) : unit =
    let zone = GameplayState.activeZone state
    match Zone.tryTriggerAt zone ref with
    | ValueNone -> ()
    | ValueSome trigger ->

    do resetTriggerEditor &state ref

    let idPrefix = "triggercue"
    let cue = trigger.State.Cue

    let mutable selection = state.Editor.TriggerCueSelection
    match CueTree.drawTree idPrefix selection cue with
    | Some clicked -> selection <- Some clicked
    | None -> ()

    match selection with
    | Some path ->

        let mutable pending : Cue option = None
        selection <- CueTree.drawEditor idPrefix cue path (fun next -> pending <- Some next)
        match pending with
        | Some next -> do Selection.updateTriggerRef shared &state ref (fun t -> { t with Cue = next })
        | None -> ()

        match selection with
        | Some path' ->
            match CueTree.tryAt path' cue with
            | Some node ->
                let mutable textPending : Cue option = None
                let (path'', editing) =
                    drawNodeText zone state.Editor.TriggerCueTextBuffer state.Editor.TriggerCueTextPath state.Editor.TriggerCueEditing idPrefix path' node cue (fun next -> textPending <- Some next)
                state.Editor <- { state.Editor with TriggerCueTextPath = path''; TriggerCueEditing = editing }
                match textPending with
                | Some next -> do Selection.updateTriggerRef shared &state ref (fun t -> { t with Cue = next })
                | None -> ()
            | None -> ()
        | None -> ()

    | None -> ()

    state.Editor <- { state.Editor with TriggerCueSelection = selection }
