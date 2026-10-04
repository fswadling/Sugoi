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

open System.Collections.Generic
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

// ── the names a character's asset defines ──

/// The animation-clip and morph names a character's asset defines, for the panel's clip/morph choosers. Read
/// from the actor in the scene when she is placed, else from any cast binding for her (the cue resolves the art
/// at run time, so the binding is the authoring-time stand-in). Empty when neither resolves — the field then
/// falls back to typing the name.
let private assetNames
    (assets: Assets.AssetManager)
    (cast: Dictionary<Casting.Slot, string>)
    (zones: Dictionary<Place.Place, Zone.Zone>)
    (runtimeCast: Dictionary<Roster.Character, Place.Place * EntityRef>)
    (character: Roster.Character) : string list * string list =
    let fromScene =
        match runtimeCast.TryGetValue character with
        | true, (place, ref) ->
            match zones.TryGetValue place with
            | true, zone when zone.Actors.IsAlive(handleOf ref) ->
                let actor = &zone.Actors.Get(handleOf ref)
                Some actor.Renderer.Asset
            | _ -> None
        | false, _ -> None
    let asset =
        match fromScene with
        | Some a -> Some a
        | None ->
        // else the binding's asset for any look, since the op names the character, not a look
        let mutable found = None
        for pair in cast do
            if found.IsNone && pair.Key.Character = character then
                match assets.Actors.TryGetValue pair.Value with
                | true, a -> found <- Some a
                | false, _ -> ()
        found
    match asset with
    | None -> [], []
    | Some a ->
        let clips = a.Model.Model.Anims |> Array.map (fun anim -> anim.Name) |> Array.toList
        let morphs = match a.Morphs with Some m -> Array.toList m.Names | None -> []
        clips, morphs

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
    (idSuffix: string)
    (idPrefix: string)
    (nodePath: int list)
    (node: Cue)
    (cue: Cue)
    (current: string)
    (write: Cue -> string -> Cue)
    (onCommit: Cue -> unit)
    : int list option * bool =

    let path =
        if editing then path
        else
            do seedText buffer current
            Some nodePath

    let submitted =
        ImGui.inputText("Text" + idSuffix + "##" + CueTree.rowId idPrefix nodePath, buffer, unativeint buffer.Length, ImGui.enterReturnsTrue ||| ImGui.autoSelectAll, System.IntPtr.Zero, System.IntPtr.Zero)
    let finished = submitted || ImGui.isItemDeactivatedAfterEdit()
    let text = bufferText buffer

    // Committed only for the node the buffer belongs to. The selection can change while the field is live
    // (the author clicks another row), and a deactivation in that same frame must not write this text there.
    if finished && path = Some nodePath && text <> current then
        onCommit (CueTree.withAt nodePath (write node text) cue)

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
                    onCommit (CueTree.withAt nodePath (CueTree.withWaypoint name node) cue)
        do ImGui.endCombo()

/// The trigger toggles' trigger name, chosen from the named place's live triggers, so the name the op names
/// resolves (`Zone.tryTriggerByName`, the resolution the engine uses). The caller reads the zone — the control
/// names none — and the write is `CueTree.withTrigger`.
let private drawTriggerCombo
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
    if ImGui.beginCombo("Trigger##" + id, preview, 0) then
        let slots = zone.Triggers.Slots
        for i in 0 .. slots.Length - 1 do
            let slot = slots.[i]
            if slot.Alive then
                let name = slot.Value.State.Base.Name
                // The slot index keeps two triggers sharing a name apart in ImGui; the *name* is what the op
                // resolves by, so choosing either writes the same thing, as at run time.
                if ImGui.selectableBool(name + "##trig" + id + string i, name = current, 0, ImGui.ImVec2(0.0f, 0.0f)) && name <> current then
                    onCommit (CueTree.withAt nodePath (CueTree.withTrigger name node) cue)
        do ImGui.endCombo()

// ── the selected node's text or waypoint, whichever it carries ──

/// Draw the editor for `path`'s node after `drawEditor`: the waypoint/path ops choose from the active zone's
/// waypoints, the clip/morph fields choose from the character's asset's names (a combo) or are typed when the
/// asset does not resolve, and the two-name ops (a cross-fade, a move) draw a second field. `onCommit` is handed
/// the next cue. A node that carries a name is never executing, so this needs no lock check. Returns the two
/// fields' `(path, editing)` pairs for the caller to store.
let private drawNodeText
    (zone: Zone.Zone)
    (zoneOfPlace: Place.Place -> Zone.Zone option)
    (namesOf: Roster.Character -> string list * string list)
    (buffer: byte[])
    (textPath: int list option)
    (editing: bool)
    (buffer2: byte[])
    (textPath2: int list option)
    (editing2: bool)
    (idPrefix: string)
    (path: int list)
    (node: Cue)
    (cue: Cue)
    (onCommit: Cue -> unit)
    : (int list option * bool) * (int list option * bool) =

    // One name field: a combo over `names` when the asset resolves any, else a typed field. `suffix` keeps a
    // two-name op's fields — and their fallbacks — off each other's ImGui ids.
    let drawName (suffix: string) (field: byte[]) (fieldPath: int list option) (fieldEditing: bool) (names: string list) (ofCue: Cue -> string voption) (write: Cue -> string -> Cue) : int list option * bool =
        match ofCue node with
        | ValueSome current when not names.IsEmpty ->
            let id = CueTree.rowId idPrefix path + "/" + suffix
            let preview = if current = "" then "(none)" else current
            if ImGui.beginCombo("Name##" + id, preview, 0) then
                for name in names do
                    if ImGui.selectableBool(name + "##" + id + name, name = current, 0, ImGui.ImVec2(0.0f, 0.0f)) && name <> current then
                        onCommit (CueTree.withAt path (write node name) cue)
                do ImGui.endCombo()
            fieldPath, fieldEditing
        | ValueSome current -> drawText field fieldPath fieldEditing suffix idPrefix path node cue current write onCommit
        | ValueNone -> fieldPath, fieldEditing

    let drawGenericFields () =
        let field0 =
            match CueTree.textOf node with
            | ValueSome current -> drawText buffer textPath editing "" idPrefix path node cue current CueTree.withText onCommit
            | ValueNone -> textPath, editing
        let field1 =
            match CueTree.textOf2 node with
            | ValueSome current -> drawText buffer2 textPath2 editing2 "2" idPrefix path node cue current CueTree.withText2 onCommit
            | ValueNone -> textPath2, editing2
        field0, field1

    match node with
    | SetCamera current | FlyCamera (current, _) ->
        do drawWaypointCombo zone idPrefix path node cue current onCommit
        (textPath, editing), (textPath2, editing2)
    | RotateActor (_, current, _) ->
        // the turn names a waypoint in the active zone (motion is active-zone only), chosen here
        do drawWaypointCombo zone idPrefix path node cue current onCommit
        (textPath, editing), (textPath2, editing2)
    | MoveActor (character, current, _, _, _) ->
        // the path is chosen; the moving and idle clips are chosen from her asset's clips
        do drawWaypointCombo zone idPrefix path node cue current onCommit
        let clips, _ = namesOf character
        drawName "clip0" buffer textPath editing clips CueTree.textOf CueTree.withText,
        drawName "clip1" buffer2 textPath2 editing2 clips CueTree.textOf2 CueTree.withText2
    | Animate (character, _, _, _) | Morph (character, _, _, _) ->
        let clips, morphs = namesOf character
        let names = match node with Animate _ -> clips | _ -> morphs
        drawName "clip0" buffer textPath editing names CueTree.textOf CueTree.withText, (textPath2, editing2)
    | CrossFade (character, _, _, _, _) | CrossMorph (character, _, _, _, _) ->
        let clips, morphs = namesOf character
        let names = match node with CrossFade _ -> clips | _ -> morphs
        drawName "clip0" buffer textPath editing names CueTree.textOf CueTree.withText,
        drawName "clip1" buffer2 textPath2 editing2 names CueTree.textOf2 CueTree.withText2
    | SetActor (place, _, _, current) ->
        // The set op names a waypoint in its **own** place, which may not be the active zone, so the combo is
        // offered from that place's zone when it is present; an absent place falls back to the typed field.
        match zoneOfPlace place with
        | Some placeZone ->
            do drawWaypointCombo placeZone idPrefix path node cue current onCommit
            (textPath, editing), (textPath2, editing2)
        | None ->
            drawText buffer textPath editing "" idPrefix path node cue current CueTree.withText onCommit, (textPath2, editing2)
    | RemoveActor _ -> (textPath, editing), (textPath2, editing2)
    | EnableTrigger (place, current) | DisableTrigger (place, current) ->
        // the trigger is chosen in the place the op names (the cross-zone convention); an absent place falls
        // back to the typed field, as the set op's waypoint does
        match zoneOfPlace place with
        | Some placeZone ->
            do drawTriggerCombo placeZone idPrefix path node cue current onCommit
            (textPath, editing), (textPath2, editing2)
        | None ->
            drawText buffer textPath editing "" idPrefix path node cue current (fun n name -> CueTree.withTrigger name n) onCommit, (textPath2, editing2)
    | _ -> drawGenericFields ()

// ── the Cue panel ──

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Cue", System.IntPtr.Zero, 0) |> ignore

    // The cue is the screen's own value, read where it lives: what it shows shrinks as the cue runs, so an
    // op that has already run needs no row of its own (FR-002, FR-003).
    let cue = state.Cue
    let idPrefix = "cue"
    let zone = GameplayState.activeZone state
    // The set op reads its *own* place's zone for the waypoint chooser; the dict is captured, not the byref.
    let zones = state.Zones
    let zoneOfPlace (place: Place.Place) : Zone.Zone option =
        match zones.TryGetValue place with
        | true, z -> Some z
        | false, _ -> None
    // The clip/morph choosers read the character's asset's names; the dicts are captured, not the byref.
    let runtimeCast = state.Cast
    let assetNamesOf (character: Roster.Character) : string list * string list =
        assetNames shared.Assets shared.Cast.Bindings zones runtimeCast character

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
                let (path'', editing), (path2', editing2) =
                    drawNodeText zone zoneOfPlace assetNamesOf state.Editor.CueTextBuffer state.Editor.CueTextPath state.Editor.CueTextEditing state.Editor.CueTextBuffer2 state.Editor.CueTextPath2 state.Editor.CueTextEditing2 idPrefix path' node state.Cue (fun next -> textPending <- Some next)
                state.Editor <- { state.Editor with CueTextPath = path''; CueTextEditing = editing; CueTextPath2 = path2'; CueTextEditing2 = editing2 }
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
                TriggerCueEditing = false
                TriggerCueTextPath2 = None
                TriggerCueTextEditing2 = false }

/// Draw the selected trigger's authored cue inline, and commit each accepted edit as one `TriggerSet` step
/// recorded against that trigger (spec 058). The trigger's transient editor state is its own: when
/// `TriggerCueRef` names a different trigger the fields reset, so a node selection or text typed for one trigger
/// is never applied to another. An authored cue is never executing, so the control offers every edit.
let drawTrigger (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (ref: EntityRef) : unit =
    let zone = GameplayState.activeZone state
    let zones = state.Zones
    let zoneOfPlace (place: Place.Place) : Zone.Zone option =
        match zones.TryGetValue place with
        | true, z -> Some z
        | false, _ -> None
    let runtimeCast = state.Cast
    let assetNamesOf (character: Roster.Character) : string list * string list =
        assetNames shared.Assets shared.Cast.Bindings zones runtimeCast character
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
                let (path'', editing), (path2', editing2) =
                    drawNodeText zone zoneOfPlace assetNamesOf state.Editor.TriggerCueTextBuffer state.Editor.TriggerCueTextPath state.Editor.TriggerCueEditing state.Editor.TriggerCueTextBuffer2 state.Editor.TriggerCueTextPath2 state.Editor.TriggerCueTextEditing2 idPrefix path' node cue (fun next -> textPending <- Some next)
                state.Editor <- { state.Editor with TriggerCueTextPath = path''; TriggerCueEditing = editing; TriggerCueTextPath2 = path2'; TriggerCueTextEditing2 = editing2 }
                match textPending with
                | Some next -> do Selection.updateTriggerRef shared &state ref (fun t -> { t with Cue = next })
                | None -> ()
            | None -> ()
        | None -> ()

    | None -> ()

    state.Editor <- { state.Editor with TriggerCueSelection = selection }
