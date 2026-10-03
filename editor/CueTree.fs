module CueTree

// The one shared cue control (spec 047): a cue drawn as a recursive tree list — a row per container, its
// children indented beneath it — with what is executing read-only (except that any `Sequence` takes an addition,
// whose tail is ahead of the running op) and what is still to run editable.
//
// It names `Cue`, `Prime` and `ImGui` and nothing else: no `GameplayState`, no `EditorState`, no `Command`,
// no zone and no asset. That is what lets a trigger's cue (phase 10) and a scenario's cue (phase 11) draw it
// (`FR-006`), and it is why a zone name is typed into a field rather than chosen from a list.
//
// The readable half is pure — `rows`, `executing` and the path helpers — so what the author is told, and
// which nodes the control will let them change, are asserted headlessly (`FR-007`, `SC-004`). The drawing
// over it is thin, and needs a device. The whole thing is editor-only work: the panel that calls it draws
// only while editing, when the play chain is stopped, so nothing here is on the game loop's path.

open System
open System.Numerics
open Prime
open Cue
open Advent

// ── what the cue looks like ──

/// One row: where the node is, how far it is indented, what it says, and whether it may be changed.
///
/// A path is a node's identity — a cue value carries no ids — so `[]` is the root and a child index follows
/// the constructor's own order (`children`). Positional identity is why an edit that moves a sibling is
/// followed by the panel moving its selection rather than trusting the path to still mean the same node.
[<Struct>]
type Row =
    { Path : int list
      Depth : int
      Label : string
      Locked : bool
      // The branch a label row names (`"then"` / `"else"`) and `""` for an ordinary node row. A label row is
      // shown above its branch to tell an `If`'s two branches apart; it carries no node, so it is never selectable
      // and `Path` is only its branch's own path.
      Branch : string }

/// Whether this node is *executing* — an ancestor-or-self of a leaf that is in flight — which is when it is
/// read-only (`FR-011`): a node on the running path is not retuned, replaced or removed. (An *addition* is
/// still offered to a `Sequence` on it — see `drawEditor` — because a queue's tail is ahead of the running op.)
///
/// Read off the value rather than decided by the editor: `world/CueEngine.fs` gives a run state of its own to
/// two cases only (`WaitState`'s deadline, `FlyCameraState`'s resolved path), so a `Sequence`'s front is executing
/// when what it holds is, a `Parallel`'s sides are executing together, and everything else is either
/// structure or an op that has not begun. A suite can therefore assert the lock with no device.
let rec executing (cue: Cue) : bool =
    match cue with
    | WaitState _ | FlyCameraState _ | AwaitState _ -> true
    | Sequence (current, _) -> executing current
    | Parallel (left, right) -> executing left || executing right
    | Fork inner -> executing inner
    | Fin | Wait _ | Exposit _ | SwitchZone _ | SetCamera _ | FlyCamera _ | AddAdvent _ | RemoveAdvent _ | Await _ | If _ -> false

/// What a row says. A leaf in flight is shown as the op it came from — a `WaitState` as its `Wait` — because
/// the view is what is left to run, never an internal state's name (`FR-004`).
let private label (cue: Cue) : string =
    match cue with
    | Fin -> "Fin"
    | Sequence _ -> "Sequence"
    | Parallel _ -> "Parallel"
    | Fork _ -> "Fork"
    | Wait seconds -> sprintf "Wait %g" seconds
    | WaitState (seconds, _) -> sprintf "Wait %g" seconds
    | Exposit (text, _) -> "Exposit: " + text
    | SwitchZone zone -> "Zone: " + zone
    | SetCamera waypoint -> "Camera: " + waypoint
    | FlyCamera (path, _) | FlyCameraState (path, _, _, _, _) -> "Fly: " + path
    | AddAdvent advent -> "Add: " + Advent.name advent
    | RemoveAdvent advent -> "Remove: " + Advent.name advent
    | Await advent | AwaitState advent -> "Await: " + Advent.name advent
    | If (advent, _, _) -> "If: " + Advent.name advent

/// A `Sequence`'s children in index order: the op in flight first, then the queue behind it.
let private sequenceChildren (current: Cue) (rest: Cue FDeque) : Cue list =
    current :: (FDeque.toSeq rest |> List.ofSeq)

/// The children of a node, in the index order a path addresses them by. `[]` for a leaf or `Fin`.
let children (cue: Cue) : Cue list =
    match cue with
    | Sequence (current, rest) -> sequenceChildren current rest
    | Parallel (left, right) -> [ left; right ]
    | Fork inner -> [ inner ]
    | If (_, thenCue, elseCue) -> [ thenCue; elseCue ]
    | Fin | Wait _ | WaitState _ | Exposit _ | SwitchZone _ | SetCamera _ | FlyCamera _ | FlyCameraState _
    | AddAdvent _ | RemoveAdvent _ | Await _ | AwaitState _ -> []

/// Every row of the cue as the panel shows it, in display order: a row per container with its children
/// indented beneath it, the queue behind a running op in its own order, and `Fin` its own row. An `If`'s two
/// branches each get a label row (`then` / `else`) above them, so the author can tell them apart; the label
/// carries no node of its own. The one thing the author is *told*, so it is a function of the cue alone
/// (`FR-002`, `FR-007`).
///
/// List-based and allocating on purpose: this runs while the editor is open and the play chain is stopped, so
/// it is a cold path, and the constitution's guidance ("clarity first, then measure") is about the frame
/// loop. Keeping it one function is also what stops the drawing and the assertion from drifting apart.
let rows (cue: Cue) : Row list =
    let nodeRow (path: int list) (depth: int) (node: Cue) : Row =
        { Path = path; Depth = depth; Label = label node; Locked = executing node; Branch = "" }
    let branchRow (path: int list) (depth: int) (name: string) : Row =
        { Path = path; Depth = depth; Label = name; Locked = false; Branch = name }
    let rec walk (path: int list) (depth: int) (node: Cue) : Row list =
        match node with
        // An `If`'s branches are told apart by a label row above each: the label one step deeper than the `If`,
        // the branch it names one deeper again, so the label reads as heading the branch.
        | If (_, thenCue, elseCue) ->
            let thenPath = path @ [ 0 ]
            let elsePath = path @ [ 1 ]
            [ nodeRow path depth node ]
            @ [ branchRow thenPath (depth + 1) "then" ]
            @ walk thenPath (depth + 2) thenCue
            @ [ branchRow elsePath (depth + 1) "else" ]
            @ walk elsePath (depth + 2) elseCue
        | _ ->
            nodeRow path depth node
            :: (children node |> List.mapi (fun i child -> walk (path @ [ i ]) (depth + 1) child) |> List.concat)
    walk [] 0 cue

/// The ImGui id of a row: the caller's prefix and the node's path, so two cues drawn in one window cannot
/// collide and the same node keeps its id from frame to frame (`FR-006`). Pure, so the one property of the
/// sharing that would otherwise only be inspectable is asserted headlessly too.
let rowId (idPrefix: string) (path: int list) : string =
    idPrefix + "/" + String.concat "." (List.map string path)

// ── where a node is, and how it is written back ──

/// The node a path names, or `None` when it names nothing — a stale selection, or a bug. The harmless
/// question: everything that *writes* through a path raises instead (`Principle II`).
let rec tryAt (path: int list) (cue: Cue) : Cue option =
    match path with
    | [] -> Some cue
    | index :: rest ->
        match List.tryItem index (children cue) with
        | Some child -> tryAt rest child
        | None -> None

/// A node rebuilt around a new child list, in its own constructor. The one place a child list becomes a node,
/// so replacing a child and removing one cannot disagree about what a shape means.
///
/// A child list that no longer fills the constructor is not an error: a `Sequence` with nothing left is
/// `Fin`, a `Parallel` or `Fork` with one child left collapses to it — which is the engine's own rule (`a
/// side that is Fin drops out`, `world/CueEngine.fs`), so removing a branch behaves like the run does.
let private withChildren (cue: Cue) (items: Cue list) : Cue =
    match cue, items with
    | Sequence _, current :: rest' -> Sequence(current, FDeque.ofList rest')
    | Sequence _, [] -> Fin
    | Parallel _, [ only ] -> only
    | Parallel _, [ left; right ] -> Parallel(left, right)
    | Parallel _, [] -> Fin
    | Fork _, [ inner ] -> Fork inner
    | Fork _, [] -> Fin
    | _ -> invalidOp (sprintf "CueTree: cannot rebuild %s around %d children" (label cue) (List.length items))

/// The cue with the node at `path` replaced by `replacement`, rebuilding only the spine from the root to it.
/// Raises when the path names no node: a write to a node that is not there is a bug, not a state
/// (`Principle II`).
let withAt (path: int list) (replacement: Cue) (cue: Cue) : Cue =
    let rec go (path: int list) (node: Cue) : Cue =
        match path with
        | [] -> replacement
        | index :: rest ->
            let items = children node
            if index < 0 || index >= List.length items then invalidOp (sprintf "CueTree: no node at %A" path)
            withChildren node (List.mapi (fun i child -> if i = index then go rest child else child) items)
    go path cue

/// The cue with the node at `path` removed. The root has no parent to remove it from: raises.
let withoutAt (path: int list) (cue: Cue) : Cue =
    let rec go (path: int list) (node: Cue) : Cue =
        match path with
        | [] -> invalidOp "CueTree: the root cannot be removed"
        | [ index ] ->
            let items = children node
            if index < 0 || index >= List.length items then invalidOp (sprintf "CueTree: no node at %A" path)
            let kept = items |> List.indexed |> List.choose (fun (i, child) -> if i = index then None else Some child)
            withChildren node kept
        | index :: rest ->
            let items = children node
            if index < 0 || index >= List.length items then invalidOp (sprintf "CueTree: no node at %A" path)
            withChildren node (List.mapi (fun i child -> if i = index then go rest child else child) items)
    go path cue

/// The node at `path` with `child` appended to its queue, and the path of the child just added — so an "Add
/// Child" can hand the panel a selection that follows the new node rather than being cleared, and the two
/// cannot disagree. Raises when `path` names no `Sequence`: the control is drawn only on one.
let withAppendedChild (path: int list) (child: Cue) (cue: Cue) : Cue * int list =
    match tryAt path cue with
    | Some (Sequence (current, rest)) ->
        let queue = (FDeque.toSeq rest |> List.ofSeq) @ [ child ]
        Sequence(current, FDeque.ofList queue), path @ [ List.length (sequenceChildren current rest) ]
    | Some _ -> invalidOp (sprintf "CueTree: %A is not a Sequence, so it takes no children" path)
    | None -> invalidOp (sprintf "CueTree: no node at %A" path)

// ── what a node's editor offers ──

/// Whether the editor offers an addition at `path`. An addition lands on a `Sequence`'s queue, and a queue's
/// tail is ahead of whatever is running — so this is true even for a sequence that is *executing*, which is
/// the one operation the running path does not refuse (`FR-011`). The pure half of that rule, so the suite
/// asserts it with no device.
let canAppend (path: int list) (cue: Cue) : bool =
    match tryAt path cue with
    | Some (Sequence _) -> true
    | _ -> false

/// The kinds a node can be turned into, in the order the dropdown lists them.
let private kinds = [| "Fin"; "Sequence"; "Parallel"; "Fork"; "Wait"; "Exposit"; "Zone"; "Set camera"; "Fly camera"; "Add advent"; "Remove advent"; "Await"; "If" |]

/// What the dropdown shows for a node: its authored kind. The in-flight `…State` cases show as the op they
/// came from, so re-choosing a kind never shows a name the author did not write.
let private kindName (cue: Cue) : string =
    match cue with
    | Fin -> "Fin"
    | Sequence _ -> "Sequence"
    | Parallel _ -> "Parallel"
    | Fork _ -> "Fork"
    | Wait _ | WaitState _ -> "Wait"
    | Exposit _ -> "Exposit"
    | SwitchZone _ -> "Zone"
    | SetCamera _ -> "Set camera"
    | FlyCamera _ | FlyCameraState _ -> "Fly camera"
    | AddAdvent _ -> "Add advent"
    | RemoveAdvent _ -> "Remove advent"
    | Await _ | AwaitState _ -> "Await"
    | If _ -> "If"

/// What choosing a kind replaces the node with. Every op is reachable without an editor per op.
///
/// Two of these defaults are deliberately not "nothing": a `Sequence` starts with a `Fin` front and an empty
/// queue (the walk pulls the queue's front into the front in the same step, so the author can add to it and
// it), and the defaults for `Zone` and `Exposit` are empty strings (the `Exposit` also defaulting to a
// plain dialogue line). An empty zone name is wrong and
/// **will block** when the op is reached — the type system cannot say "a zone that exists", and preventing
/// it in the editor is out of scope; the name still raises loudly where it always did, in
/// `GameplayState.switchZone`.
let private defaultOfKind (kind: string) : Cue =
    let defaultAdvent = List.head Advent.all
    match kind with
    | "Fin" -> Fin
    | "Sequence" -> Sequence(Fin, FDeque.ofList [])
    | "Parallel" -> Parallel(Fin, Fin)
    | "Fork" -> Fork Fin
    | "Wait" -> Wait 1.0
    | "Exposit" -> Exposit("", Dialogue.Kind.Dialogue)
    | "Zone" -> SwitchZone ""
    | "Set camera" -> SetCamera ""
    | "Add advent" -> AddAdvent defaultAdvent
    | "Remove advent" -> RemoveAdvent defaultAdvent
    | "Await" -> Await defaultAdvent
    | "If" -> If(defaultAdvent, Fin, Fin)
    | _ -> FlyCamera("", 1.0)

/// The node's editable text, or `ValueNone` when it has none. The field that edits it belongs to the caller
/// (the buffer is a widget's scaffolding, not the cue's), so the control names no editor state and says only
/// which nodes carry text and how to write it back.
///
/// The text-bearing kinds are never executing — only the two in-flight leaves are, and neither carries text —
/// so a caller drawing this field needs no lock check of its own.
let textOf (cue: Cue) : string voption =
    match cue with
    | Exposit (text, _) -> ValueSome text
    | SwitchZone zone -> ValueSome zone
    | SetCamera waypoint -> ValueSome waypoint
    | FlyCamera (path, _) -> ValueSome path
    | _ -> ValueNone

/// The node with its text replaced — the write the caller's field commits.
let withText (cue: Cue) (text: string) : Cue =
    match cue with
    | Exposit (_, kind) -> Exposit(text, kind)
    | SwitchZone _ -> SwitchZone text
    | SetCamera _ -> SetCamera text
    | FlyCamera (_, speed) -> FlyCamera(text, speed)
    | _ -> invalidOp (sprintf "CueTree: a %s carries no text" (label cue))

/// The node with its presentation replaced — the write the choosers commit. Only an exposit carries one, and a
/// caller that reaches for it on anything else is a bug (`Principle II`), as `withText` is.
let withExpositKind (kind: Dialogue.Kind) (cue: Cue) : Cue =
    match cue with
    | Exposit (text, _) -> Exposit(text, kind)
    | _ -> invalidOp (sprintf "CueTree: a %s carries no presentation" (label cue))

/// The node with its milestone replaced — the write the chooser commits. Only the advent ops carry one.
let private withAdvent (advent: Advent) (cue: Cue) : Cue =
    match cue with
    | AddAdvent _ -> AddAdvent advent
    | RemoveAdvent _ -> RemoveAdvent advent
    | Await _ -> Await advent
    | AwaitState _ -> AwaitState advent
    | If (_, thenCue, elseCue) -> If(advent, thenCue, elseCue)
    | _ -> invalidOp (sprintf "CueTree: a %s carries no milestone" (label cue))

// ── the drawing ──

let private selectable (label: string) (selected: bool) : bool =
    ImGui.selectableBool(label, selected, 0, ImGui.ImVec2(0.0f, 0.0f))

/// Draw the cue as selectable rows, indented by depth, and return the path of the row clicked this frame, if
/// any. The rows go straight into the window, which scrolls, so a cue of any size is scrolled rather than
/// truncated (`FR-009`) — no `beginChild` and no cap.
///
/// A locked row is drawn like any other, and stays selectable: `FR-011` withholds the *editor*, not the
/// reading, so the author can still open a running op and see what it is. An `If`'s branch label row is drawn
/// as plain text above its branch: it is shown (so the author reads which branch is which) but not selectable,
/// because no node is behind it.
let drawTree (idPrefix: string) (selection: int list option) (cue: Cue) : int list option =
    let mutable clicked = None
    for row in rows cue do
        // Spaces indent the label because the binding has no `indent`.
        let indent = String.replicate (row.Depth * 2) " "
        if row.Branch <> "" then
            do ImGui.text (indent + row.Branch)
        else
            // `##` separates what is shown from the widget's id, which is what keeps two co-visible cues apart
            // (the reference's own row idiom).
            let text = indent + row.Label + "##" + rowId idPrefix row.Path
            if selectable text (selection = Some row.Path) then clicked <- Some row.Path
    clicked

/// One draggable number, committing when the gesture ends **and** the value actually moved — one gesture, one
/// undo step, and a click into the field and out again is not a step at all (research D4).
let private dragNumber (idBase: string) (label: string) (value: float32) (onCommit: float32 -> unit) : unit =
    let mutable edited = value
    do ImGui.dragFloat(label + "##" + idBase, &edited, 0.05f, -10000.0f, 10000.0f, "%.2f", 0) |> ignore
    if ImGui.isItemDeactivatedAfterEdit() && edited <> value then onCommit edited

/// Draw the property editor for the node at `selection`, committing each accepted change through `commit`,
/// which is handed the **next cue** outright — the control owns the rebuild (replacing a node, appending to
/// a queue, dropping one), so removing a node is as expressible as editing one.
///
/// Returns the selection the panel should hold afterwards: `selection` itself when nothing structural
/// happened, the added child's path after an "Add Child", and `None` when the selected node is gone.
///
/// A node that is executing is offered **less** rather than guarded (`FR-011`): no kind, no numbers and no
/// removal, and a running node that is not a `Sequence` is offered nothing at all and says so. The one thing
/// the running path *does* offer is an addition — a `Sequence` takes a child whether or not it is running,
/// because a queue's tail is ahead of the running op, so appending to it cannot disturb it.
let drawEditor
    (idPrefix: string)
    (cue: Cue)
    (selection: int list)
    (commit: Cue -> unit)
    : int list option =

    let idBase = rowId idPrefix selection

    match tryAt selection cue with
    | None -> None
    | Some node ->

    let locked = executing node

    // an addition is always safe: it lands on a queue, which is ahead of whatever is running
    let appendable = canAppend selection cue

    if locked && not appendable then
        do ImGui.text "this op is running and cannot be changed"
        Some selection
    else

    let mutable adopted = Some selection

    // ── the kind and the numbers: what is executing is never retuned or replaced ──
    if not locked then

        // Re-choosing the kind a node already is must not clobber the numbers it carries, so the node is
        // replaced only when the kind actually changes (the Dialogue panel's rule for its own kind combo).
        let current = kindName node
        if ImGui.beginCombo("Kind##" + idBase, current, 0) then
            for kind in kinds do
                if selectable kind (kind = current) && kind <> current then
                    do commit (withAt selection (defaultOfKind kind) cue)
            do ImGui.endCombo()

        // A `…State` case is the in-flight form, so it is normally the op that is running and never reaches
        // here; an author who put one in a queue gets its authored numbers, which is the same edit.
        match node with
        | Wait seconds | WaitState (seconds, _) ->
            dragNumber idBase "Seconds" (float32 seconds) (fun v -> commit (withAt selection (Wait(float v)) cue))

        | FlyCamera (path, speed) ->
            // a flight's own number: how fast to travel. Its path is text, which the caller's field edits,
            // and its start and end are the chain's, so the authored form carries neither.
            dragNumber idBase "Speed" (float32 speed) (fun v -> commit (withAt selection (FlyCamera(path, float v)) cue))

        | AddAdvent advent | RemoveAdvent advent | Await advent | AwaitState advent | If (advent, _, _) ->
            // the milestone, chosen from the closed list — a milestone needs no zone or registry, so the
            // control can offer it where it could not offer a zone or waypoint name (FR-006).
            if ImGui.beginCombo("Advent##" + idBase, Advent.name advent, 0) then
                for candidate in Advent.all do
                    if selectable (Advent.name candidate) (candidate = advent) && candidate <> advent then
                        do commit (withAt selection (withAdvent candidate node) cue)
                do ImGui.endCombo()

        | Exposit (_, kind) ->
            // the presentation, chosen from the closed vocabularies: a character and an emotion need no zone or
            // asset, so the control can offer them where it could not offer a waypoint name (FR-006/FR-007).
            // Re-choosing the current kind must not clobber a portrait pair, so the kind is written only when it
            // actually changes (the Dialogue panel's rule for its own kind combo).
            let presentation =
                match kind with
                | Dialogue.Kind.PortraitDialogue _ -> "Portrait"
                | Dialogue.Kind.Dialogue -> "Dialogue"
                | Dialogue.Kind.Thought -> "Thought"
            if ImGui.beginCombo("Presentation##" + idBase, presentation, 0) then
                let alreadyPortrait = match kind with Dialogue.Kind.PortraitDialogue _ -> true | _ -> false
                if selectable "Dialogue" (kind = Dialogue.Kind.Dialogue) && kind <> Dialogue.Kind.Dialogue then
                    do commit (withAt selection (withExpositKind Dialogue.Kind.Dialogue node) cue)
                if selectable "Thought" (kind = Dialogue.Kind.Thought) && kind <> Dialogue.Kind.Thought then
                    do commit (withAt selection (withExpositKind Dialogue.Kind.Thought node) cue)
                if selectable "Portrait" alreadyPortrait && not alreadyPortrait then
                    let defaultPortrait = Dialogue.Kind.PortraitDialogue(List.head Roster.characters, Dialogue.Emotion.Neutral)
                    do commit (withAt selection (withExpositKind defaultPortrait node) cue)
                do ImGui.endCombo()

            // the speaker and the expression are meaningful only for a portrait line, so they are offered only
            // for that kind: a plain line or a thought has neither.
            match kind with
            | Dialogue.Kind.PortraitDialogue (character, emotion) ->
                if ImGui.beginCombo("Speaker##" + idBase, Roster.characterName character, 0) then
                    for candidate in Roster.characters do
                        if selectable (Roster.characterName candidate) (candidate = character) && candidate <> character then
                            do commit (withAt selection (withExpositKind (Dialogue.Kind.PortraitDialogue(candidate, emotion)) node) cue)
                    do ImGui.endCombo()
                if ImGui.beginCombo("Expression##" + idBase, Dialogue.emotionName emotion, 0) then
                    for candidate in Dialogue.emotions do
                        if selectable (Dialogue.emotionName candidate) (candidate = emotion) && candidate <> emotion then
                            do commit (withAt selection (withExpositKind (Dialogue.Kind.PortraitDialogue(character, candidate)) node) cue)
                    do ImGui.endCombo()
            | _ -> ()

        | Fin | Sequence _ | Parallel _ | Fork _ | SwitchZone _ | SetCamera _ | FlyCameraState _ ->
            // no numbers of their own. `SwitchZone` and `SetCamera` carry text, and a `FlyCamera` carries its
            // path and speed above; the caller's field edits the text (`textOf` / `withText`).
            ()

    // ── Add Child: the one container that takes one, whether or not it is running ──
    if appendable then
        if ImGui.smallButton("Add Child##" + idBase) then
            // a `Wait` rather than a `Fin`: an added op should be a real step the author then edits, and a
            // queued `Fin` is skipped by the walk and reads as nonsense in a queue
            let (node', childPath) = withAppendedChild selection (defaultOfKind "Wait") cue
            do commit (withAt selection node' cue)
            adopted <- Some childPath

    // ── Remove: anything but the root, and nothing that is executing ──
    if not locked && selection <> [] then
        if ImGui.smallButton("Remove##" + idBase) then
            do commit (withoutAt selection cue)
            adopted <- None

    adopted
