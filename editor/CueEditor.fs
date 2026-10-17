module CueEditor

// The Cue panel (spec 047) and the Inspector's inline trigger-cue editor (spec 058). Both draw a cue through
// the one shared control (`CueTree`) and write each accepted edit back as a single step on the one undo history:
// the screen's cue is one `SetCue` step (`specs/049`), and a trigger's authored cue is one `TriggerSet` step —
// the command already carries the trigger's whole state, so the cue needs no command of its own (`specs/058`).
//
// The two surfaces share `drawCue`: only the id prefix, the cue's location and the write-back differ, so the
// tree/editor/text orchestration cannot drift between them.
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
open Prime
open Entity
open Cue
open Command
open SlotMap

// ── the names an actor's asset defines ──

/// The animation-clip and morph names an actor asset defines, for the panel's clip/morph choosers (spec 073
/// FR-006). Pure: an id that names no loaded asset yields nothing, and the field falls back to typing.
let private assetNamesOfAsset (assets: Assets.AssetManager) (assetId: string) : string list * string list =
    match assets.Actors.TryGetValue assetId with
    | false, _ -> [], []
    | true, a ->
    let clips = a.Model.Model.Anims |> Array.map (fun anim -> anim.Name) |> Array.toList
    let morphs = match a.Morphs with Some m -> Array.toList m.Names | None -> []
    clips, morphs

// ── the write path (the screen's cue) ──

/// Apply an accepted edit to the screen's cue: the next cue written in place, and one `SetCue` step pushed onto
/// the one history (spec 049). The control decides whether anything changed, so this always records a step — there
/// is no second opinion here about whether an edit "counted". It takes only the state: routing through
/// `Undo.commit` would make the panel take `SharedState`, which it does not need.
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
            do ImGui.seedText buffer (EditorState.cueTextBufferSize - 1) current
            Some nodePath

    let submitted =
        ImGui.inputText("Text" + idSuffix + "##" + CueTree.rowId idPrefix nodePath, buffer, unativeint buffer.Length, ImGui.enterReturnsTrue ||| ImGui.autoSelectAll, System.IntPtr.Zero, System.IntPtr.Zero)
    let finished = submitted || ImGui.isItemDeactivatedAfterEdit()
    let text = ImGui.bufferText buffer

    // Committed only for the node the buffer belongs to. The selection can change while the field is live
    // (the author clicks another row), and a deactivation in that same frame must not write this text there.
    if finished && path = Some nodePath && text <> current then
        onCommit (CueTree.withAt nodePath (write node text) cue)

    path, ImGui.isItemActive()

// ── the named things a node refers to ──

/// The names held by a live slot map, in slot order. One reader for the waypoint, trigger and actor choosers.
let private slotNames (slots: SlotMap<'T>) (nameOf: 'T -> string) : string list =
    let names = ResizeArray<string>()
    let span = slots.Slots
    for i in 0 .. span.Length - 1 do
        let slot = span.[i]
        if slot.Alive then names.Add(nameOf slot.Value)
    List.ofSeq names

/// One name chooser: a filtered combo over a live place's names. The caller reads the place — the control names
/// no zone (`FR-006`) — and `write` replaces the op's name (`CueTree.withWaypoint`/`withTrigger`/`withActor`).
let private drawNameCombo
    (names: string list)
    (label: string)
    (buffer: byte[])
    (idPrefix: string)
    (nodePath: int list)
    (node: Cue)
    (cue: Cue)
    (current: string)
    (write: string -> Cue -> Cue)
    (onCommit: Cue -> unit)
    : unit =

    let comboId = CueTree.rowId idPrefix nodePath
    let preview = if current = "" then "(none)" else current
    CueTree.filteredCombo buffer (label + "##" + comboId) preview names id current (fun name ->
        onCommit (CueTree.withAt nodePath (write name node) cue))

/// The actor asset `AddActor` builds from, chosen from the discovered actor assets (`spec 073` FR-008). The
/// caller reads the asset manager; the write is `CueTree.withAsset`.
let private drawAssetCombo
    (assets: Assets.AssetManager)
    (buffer: byte[])
    (idPrefix: string)
    (nodePath: int list)
    (node: Cue)
    (cue: Cue)
    (current: string)
    (onCommit: Cue -> unit)
    : unit =

    let comboId = CueTree.rowId idPrefix nodePath
    let ids = assets.Actors.Values |> Seq.sortBy (fun a -> a.Id) |> Seq.map (fun a -> a.Id) |> List.ofSeq
    let preview = if current = "" then "(none)" else current
    CueTree.filteredCombo buffer ("Asset##" + comboId) preview ids id current (fun id ->
        onCommit (CueTree.withAt nodePath (CueTree.withAsset id node) cue))

/// A named op's pick-only chooser: choose one name from `names` (a discovered song's id, or a sound's). The op
/// names a discovered asset — an op naming an undiscovered one could only no-op — so there is no typed fallback,
/// the `drawAssetCombo` pattern. `label` titles the combo; `suffix` keeps a two-field op's fields off each
/// other's ImGui ids; `write` is the field's writer (`CueTree.withSong` or `withSong2`).
let private drawPickCombo
    (names: string list)
    (label: string)
    (suffix: string)
    (buffer: byte[])
    (idPrefix: string)
    (nodePath: int list)
    (node: Cue)
    (cue: Cue)
    (write: string -> Cue -> Cue)
    (current: string)
    (onCommit: Cue -> unit)
    : unit =

    let comboId = CueTree.rowId idPrefix nodePath + suffix
    let preview = if current = "" then "(none)" else current
    CueTree.filteredCombo buffer (label + "##" + comboId) preview names id current (fun picked ->
        onCommit (CueTree.withAt nodePath (write picked node) cue))

// ── the selected node's text or waypoint, whichever it carries ──

/// Draw the editor for `path`'s node after `drawEditor`: the waypoint/path ops choose from the active zone's
/// waypoints, the clip/morph fields choose from the character's asset's names (a combo) or are typed when the
/// asset does not resolve, and the two-name ops (a cross-fade, a move) draw a second field. `onCommit` is handed
/// the next cue. A node whose text this draws is never executing (`ExpositWaitState` is excluded from
/// `textOf`), so this needs no lock check. Returns the two fields' `(path, editing)` pairs for the caller to
/// store.
let private drawNodeText
    (assets: Assets.AssetManager)
    (activeZone: Zone.Zone)
    (zoneOfPlace: string -> Zone.Zone option)
    (namesOf: Zone.Zone -> string -> string list * string list)
    (scenarios: string list)
    (filter: byte[])
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

    // The zone a node's choosers read: the op's own place's zone, or the active zone for the place-less motion
    // ops (spec 075 FR-006). `None` when the op's place is not present — the chooser is then not drawn.
    let zoneFor (node: Cue) : Zone.Zone option =
        match CueTree.placeOf node with
        | ValueSome place -> zoneOfPlace place
        | ValueNone -> Some activeZone

    // The clip/morph names an op's choosers offer, read from the actor of that name in the op's place.
    let namesFor (actor: string) : string list * string list =
        match zoneFor node with
        | Some zone -> namesOf zone actor
        | None -> [], []

    // One name field: a combo over `names` when the asset resolves any, else a typed field. `suffix` keeps a
    // two-name op's fields — and their fallbacks — off each other's ImGui ids.
    let drawName (suffix: string) (field: byte[]) (fieldPath: int list option) (fieldEditing: bool) (names: string list) (ofCue: Cue -> string voption) (write: Cue -> string -> Cue) : int list option * bool =
        match ofCue node with
        | ValueSome current when not names.IsEmpty ->
            let comboId = CueTree.rowId idPrefix path + "/" + suffix
            let preview = if current = "" then "(none)" else current
            CueTree.filteredCombo filter ("Name##" + comboId) preview names id current (fun name ->
                onCommit (CueTree.withAt path (write node name) cue))
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

    // An actor chooser: a combo over the op's place's live actors; nothing when the place is absent.
    let drawActorField (actor: string) : unit =
        match zoneFor node with
        | Some zone -> do drawNameCombo (slotNames zone.Actors (fun (a: Actor) -> a.State.Base.Name)) "Actor" filter idPrefix path node cue actor CueTree.withActor onCommit
        | None -> ()

    // A waypoint chooser: a combo over the op's place's live waypoints; nothing when the place is absent.
    let drawWaypointField (waypoint: string) : unit =
        match zoneFor node with
        | Some zone -> do drawNameCombo (slotNames zone.Waypoints (fun (w: Waypoint) -> w.Base.Name)) "Waypoint" filter idPrefix path node cue waypoint CueTree.withWaypoint onCommit
        | None -> ()

    // A trigger chooser: a combo over the op's place's live triggers; nothing when the place is absent.
    let drawTriggerField (trigger: string) : unit =
        match zoneFor node with
        | Some zone -> do drawNameCombo (slotNames zone.Triggers (fun (t: Trigger) -> t.State.Base.Name)) "Trigger" filter idPrefix path node cue trigger CueTree.withTrigger onCommit
        | None -> ()

    match node with
    | SetCamera (_, waypoint) | FlyCamera (_, waypoint, _) ->
        do drawWaypointField waypoint
        (textPath, editing), (textPath2, editing2)
    | RotateActor (actor, waypoint, _) ->
        // the actor and the waypoint are chosen in the op's place (motion is active-zone only, so it is the
        // active zone)
        do drawActorField actor
        do drawWaypointField waypoint
        (textPath, editing), (textPath2, editing2)
    | MoveActor (actor, path, _, _, _) ->
        // the actor and the path are chosen; the moving and idle clips are chosen from her asset's clips
        do drawActorField actor
        do drawWaypointField path
        let clips, _ = namesFor actor
        drawName "clip0" buffer textPath editing clips CueTree.textOf CueTree.withText,
        drawName "clip1" buffer2 textPath2 editing2 clips CueTree.textOf2 CueTree.withText2
    | Animate (_, actor, _, _, _) | Morph (_, actor, _, _, _) ->
        do drawActorField actor
        let clips, morphs = namesFor actor
        let names = match node with Animate _ -> clips | _ -> morphs
        drawName "clip0" buffer textPath editing names CueTree.textOf CueTree.withText, (textPath2, editing2)
    | CrossFadeAnimation (_, actor, _, _, _, _) | CrossFadeMorph (_, actor, _, _, _, _) ->
        do drawActorField actor
        let clips, morphs = namesFor actor
        let names = match node with CrossFadeAnimation _ -> clips | _ -> morphs
        drawName "clip0" buffer textPath editing names CueTree.textOf CueTree.withText,
        drawName "clip1" buffer2 textPath2 editing2 names CueTree.textOf2 CueTree.withText2
    | AddActor (place, actor, asset, waypoint) ->
        // The asset is chosen from the discovered actor assets; the name is typed (there is no actor yet); the
        // waypoint is chosen from the op's **own** place's zone, which may not be the active one.
        do drawAssetCombo assets filter idPrefix path node cue asset onCommit
        let nameField = drawText buffer textPath editing "" idPrefix path node cue actor CueTree.withText onCommit
        match zoneOfPlace place with
        | Some placeZone ->
            do drawNameCombo (slotNames placeZone.Waypoints (fun (w: Waypoint) -> w.Base.Name)) "Waypoint" filter idPrefix path node cue waypoint CueTree.withWaypoint onCommit
            nameField, (textPath2, editing2)
        | None ->
            nameField, drawText buffer2 textPath2 editing2 "2" idPrefix path node cue waypoint CueTree.withText2 onCommit
    | RemoveActor (_, actor) ->
        do drawActorField actor
        (textPath, editing), (textPath2, editing2)
    | EnableTrigger (place, current) | DisableTrigger (place, current) ->
        // the trigger is chosen in the place the op names (the cross-zone convention); an absent place falls
        // back to the typed field, as the set op's waypoint does
        match zoneOfPlace place with
        | Some placeZone ->
            do drawNameCombo (slotNames placeZone.Triggers (fun (t: Trigger) -> t.State.Base.Name)) "Trigger" filter idPrefix path node cue current CueTree.withTrigger onCommit
            (textPath, editing), (textPath2, editing2)
        | None ->
            drawText buffer textPath editing "" idPrefix path node cue current (fun n name -> CueTree.withTrigger name n) onCommit, (textPath2, editing2)
    | EnablePlayer (_, actor) | DisablePlayer (_, actor) ->
        do drawActorField actor
        (textPath, editing), (textPath2, editing2)
    | ForkScenario scenario ->
        // the scenario is chosen from the current library or typed — a name may be authored before it is added, so
        // the combo commits a typed fallback and adds nothing to the library itself (`specs/074`'s speaker rule,
        // `specs/083` FR-009). A scenario needs no zone read, so it is offered where a zone name could not be.
        CueTree.filteredNameCombo filter ("Scenario##" + CueTree.rowId idPrefix path) scenario scenarios scenario (fun candidate ->
            onCommit (CueTree.withAt path (CueTree.withScenario (ScenarioRegistry.normalise candidate) node) cue))
        (textPath, editing), (textPath2, editing2)
    | AddMusic song | RemoveMusic song | PlayMusic song | StopMusic song | SetMusicLevel (song, _, _) ->
        // the song is chosen from the discovered music assets (pick-only — a music op is meaningless until the
        // asset exists, so there is no typed fallback; `spec 088` research D6). Music is global, so no zone read.
        do drawPickCombo (Assets.musicNames assets) "Song" "" filter idPrefix path node cue CueTree.withSong song onCommit
        (textPath, editing), (textPath2, editing2)
    | PlaySound sound ->
        // the sound is chosen from the discovered sound assets (pick-only, as a song is). A sound is global, so
        // no zone read; the op names the asset id the voice pool plays (`spec 090`).
        do drawPickCombo (Assets.soundAssets assets |> List.map (fun a -> a.Id)) "Sound" "" filter idPrefix path node cue CueTree.withSong sound onCommit
        (textPath, editing), (textPath2, editing2)
    | CrossFadeMusic (fromSong, toSong, _) ->
        // two choosers — the outgoing song and the incoming one (the `CrossFadeAnimation` two-field pattern)
        do drawPickCombo (Assets.musicNames assets) "Song" "" filter idPrefix path node cue CueTree.withSong fromSong onCommit
        do drawPickCombo (Assets.musicNames assets) "Song" "/to" filter idPrefix path node cue CueTree.withSong2 toSong onCommit
        (textPath, editing), (textPath2, editing2)
    | _ -> drawGenericFields ()

// ── the shared cue-editor pipeline ──

[<Struct>]
type private CueDraw =
    { Selection : int list option
      Structural : bool
      EditorNext : Cue option
      TextNext : Cue option
      TextPath : int list option
      TextEditing : bool
      TextPath2 : int list option
      TextEditing2 : bool }

/// Draw the tree and the selected node's property/text editors for one cue. Identity and write-back are the
/// caller's: the id prefix keeps two co-visible cues apart, and the two pending cues are returned so the caller
/// can apply them to its own store (the screen's cue or a trigger's authored cue). The property editor's change
/// is folded into the cue the text fields read, so a field edit in the same frame cannot overwrite a structural
/// one.
let private drawCue
    (shared: SharedState.SharedState)
    (state: byref<GameplayState.GameplayState>)
    (idPrefix: string)
    (cue: Cue)
    (selection: int list option)
    (focusPath: int list option)
    (textBuffer: byte[])
    (textPath: int list option)
    (textEditing: bool)
    (textBuffer2: byte[])
    (textPath2: int list option)
    (textEditing2: bool)
    : CueDraw =

    let activeZone = GameplayState.activeZone state
    let zones = state.Zones
    let zoneNames = zones |> Seq.map (fun kv -> kv.Key) |> Seq.sort |> List.ofSeq
    let zoneOfPlace (place: string) : Zone.Zone option =
        match zones.TryGetValue place with
        | true, z -> Some z
        | false, _ -> None
    let scenarioNames = ScenarioRegistry.names state.Scenarios
    let assetNamesOf (zone: Zone.Zone) (actor: string) : string list * string list =
        match Zone.tryActorByName zone actor with
        | Some ref when zone.Actors.IsAlive(handleOf ref) ->
            let a = &zone.Actors.Get(handleOf ref)
            assetNamesOfAsset shared.Assets a.State.AssetId
        | _ -> [], []

    let mutable current = selection
    match CueTree.drawTree idPrefix current focusPath cue with
    | Some clicked -> current <- Some clicked
    | None -> ()

    let mutable editorNext : Cue option = None
    let mutable textNext : Cue option = None
    let mutable structural = false
    let mutable path = textPath
    let mutable editing = textEditing
    let mutable path2 = textPath2
    let mutable editing2 = textEditing2

    match current with
    | Some nodePath ->
        current <- CueTree.drawEditor idPrefix state.ActiveZone zoneNames state.Editor.ComboFilter (Assets.actorNames shared.Assets) (OSet.toSeq shared.Advents |> List.ofSeq) cue nodePath (fun next -> editorNext <- Some next)
        structural <- current <> Some nodePath

        // The text fields read the cue the property editor just produced, so one frame's edits compose.
        let workingCue =
            match editorNext with
            | Some next -> next
            | None -> cue

        match current with
        | Some nodePath' ->
            match CueTree.tryAt nodePath' workingCue with
            | Some node ->
                let (path', editing'), (path2', editing2') =
                    drawNodeText shared.Assets activeZone zoneOfPlace assetNamesOf scenarioNames state.Editor.ComboFilter textBuffer textPath textEditing textBuffer2 textPath2 textEditing2 idPrefix nodePath' node workingCue (fun next -> textNext <- Some next)
                path <- path'
                editing <- editing'
                path2 <- path2'
                editing2 <- editing2'
            | None -> ()
        | None -> ()
    | None -> ()

    { Selection = current
      Structural = structural
      EditorNext = editorNext
      TextNext = textNext
      TextPath = path
      TextEditing = editing
      TextPath2 = path2
      TextEditing2 = editing2 }

// ── the Cue panel ──

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    let visible = ImGui.beginWindow("Cue", System.IntPtr.Zero, 0)
    if visible then

        // The cue is the screen's own value, read where it lives: what it shows shrinks as the cue runs, so an
        // op that has already run needs no row of its own (FR-002, FR-003).
        let refocus = state.Editor.CueRefocus
        if refocus then do ImGui.setNavCursorVisible(true)

        let result =
            drawCue shared &state "cue" state.Cue state.Editor.CueSelection (if refocus then state.Editor.CueSelection else None)
                state.Editor.CueTextBuffer state.Editor.CueTextPath state.Editor.CueTextEditing
                state.Editor.CueTextBuffer2 state.Editor.CueTextPath2 state.Editor.CueTextEditing2

        state.Editor <-
            { state.Editor with
                CueSelection = result.Selection
                CueRefocus = result.Structural
                CueTextPath = result.TextPath
                CueTextEditing = result.TextEditing
                CueTextPath2 = result.TextPath2
                CueTextEditing2 = result.TextEditing2 }

        match result.EditorNext with
        | Some next -> do commit &state next
        | None -> ()
        match result.TextNext with
        | Some next -> do commit &state next
        | None -> ()

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
    match Zone.tryTriggerAt (GameplayState.activeZone state) ref with
    | ValueNone -> ()
    | ValueSome trigger ->

    do resetTriggerEditor &state ref

    let result =
        drawCue shared &state "triggercue" trigger.State.Cue state.Editor.TriggerCueSelection None
            state.Editor.TriggerCueTextBuffer state.Editor.TriggerCueTextPath state.Editor.TriggerCueEditing
            state.Editor.TriggerCueTextBuffer2 state.Editor.TriggerCueTextPath2 state.Editor.TriggerCueTextEditing2

    state.Editor <-
        { state.Editor with
            TriggerCueSelection = result.Selection
            TriggerCueTextPath = result.TextPath
            TriggerCueEditing = result.TextEditing
            TriggerCueTextPath2 = result.TextPath2
            TriggerCueTextEditing2 = result.TextEditing2 }

    match result.EditorNext with
    | Some next -> do Selection.updateTriggerRef shared &state ref (fun t -> { t with Cue = next })
    | None -> ()
    match result.TextNext with
    | Some next -> do Selection.updateTriggerRef shared &state ref (fun t -> { t with Cue = next })
    | None -> ()
