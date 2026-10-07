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
open Place

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
    | WaitState _ | FlyCameraState _ | FadeOutState _ | FadeInState _ | AwaitState _ | BlendState _ | MoveActorState _ | RotateActorState _ -> true
    | Sequence (current, _) -> executing current
    | Parallel (left, right) -> executing left || executing right
    | Fork inner -> executing inner
    | Fin | Wait _ | FadeOut _ | FadeIn _ | Exposit _ | Print _ | SetZone _ | AddZone _ | RemoveZone _ | PreloadZone _
    | SetActor _ | RemoveActor _ | Animate _ | CrossFade _ | Morph _ | CrossMorph _ | MoveActor _ | RotateActor _
    | EnablePlayer _ | DisablePlayer _ | EnableTrigger _ | DisableTrigger _
    | SetCamera _ | FlyCamera _ | AddAdvent _ | RemoveAdvent _ | Await _ | If _ -> false

/// The name a blend shows as: `from = to` is the single-slot op, otherwise the cross-fade, and `morph` picks the
/// morph family. One rule, so an op and its in-flight `BlendState` read the same.
let private blendOpName (morph: bool) (fromName: string) (toName: string) : string =
    if fromName = toName then (if morph then "Morph" else "Animate")
    else (if morph then "Cross morph" else "Cross fade")

/// A blend's row label: the op and the slot(s) it drives.
let private blendLabel (morph: bool) (fromName: string) (toName: string) : string =
    if fromName = toName then sprintf "%s: %s" (blendOpName morph fromName toName) fromName
    else sprintf "%s: %s -> %s" (blendOpName morph fromName toName) fromName toName

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
    | FadeOut duration | FadeOutState (_, duration, _) -> sprintf "Fade out %g" duration
    | FadeIn duration | FadeInState (_, duration, _) -> sprintf "Fade in %g" duration
    | Exposit (text, _) -> "Exposit: " + text
    | SetZone place -> "Zone: " + Place.placeName place
    | AddZone place -> "Add zone: " + Place.placeName place
    | RemoveZone place -> "Remove zone: " + Place.placeName place
    | PreloadZone place -> "Preload zone: " + Place.placeName place
    | SetActor (place, character, look, waypoint) ->
        sprintf "Set actor: %s/%s in %s @ %s" (Roster.characterName character) (Casting.lookName look) (Place.placeName place) waypoint
    | RemoveActor character -> sprintf "Remove actor: %s" (Roster.characterName character)
    | Animate (_, clip, _, _) -> blendLabel false clip clip
    | CrossFade (_, fromClip, toClip, _, _) -> blendLabel false fromClip toClip
    | Morph (_, morph, _, _) -> blendLabel true morph morph
    | CrossMorph (_, fromMorph, toMorph, _, _) -> blendLabel true fromMorph toMorph
    | BlendState (_, morph, fromName, toName, _, _, _, _, _) -> blendLabel morph fromName toName
    | MoveActor (character, _, _, _, _) | MoveActorState (character, _, _, _, _, _, _, _, _, _) ->
        "Move actor: " + Roster.characterName character
    | RotateActor (character, _, _) | RotateActorState (character, _, _, _, _) ->
        "Rotate actor: " + Roster.characterName character
    | EnablePlayer character -> "Enable player: " + Roster.characterName character
    | DisablePlayer character -> "Disable player: " + Roster.characterName character
    | EnableTrigger (place, trigger) -> sprintf "Enable trigger: %s in %s" trigger (Place.placeName place)
    | DisableTrigger (place, trigger) -> sprintf "Disable trigger: %s in %s" trigger (Place.placeName place)
    | Print text -> "Print: " + text
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
    | Fin | Wait _ | WaitState _ | FadeOut _ | FadeIn _ | FadeOutState _ | FadeInState _ | Exposit _
    | SetZone _ | AddZone _ | RemoveZone _ | PreloadZone _ | SetActor _ | RemoveActor _ | SetCamera _
    | Animate _ | CrossFade _ | Morph _ | CrossMorph _ | BlendState _ | MoveActor _ | MoveActorState _
    | RotateActor _ | RotateActorState _ | EnablePlayer _ | DisablePlayer _ | EnableTrigger _ | DisableTrigger _
    | Print _
    | FlyCamera _ | FlyCameraState _ | AddAdvent _ | RemoveAdvent _ | Await _ | AwaitState _ -> []

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

/// The kinds a node can be turned into, grouped into categories for the dropdown, in the order it lists
/// them. A separator divides each group; the group names label the grouping (they are not drawn).
let private kinds : (string * string list) array = [|
    "Structure", [ "Fin"; "Sequence"; "Parallel"; "Fork" ]
    "Timing",    [ "Wait"; "Fade out"; "Fade in" ]
    "Dialogue",  [ "Exposit" ]
    "Zones",     [ "Set zone"; "Add zone"; "Remove zone"; "Preload zone" ]
    "Actors",    [ "Set actor"; "Remove actor"; "Move actor"; "Rotate actor"; "Enable player"; "Disable player" ]
    "Animation", [ "Animate"; "Cross fade"; "Morph"; "Cross morph" ]
    "Camera",    [ "Set camera"; "Fly camera" ]
    "Advent",    [ "Add advent"; "Remove advent"; "Await"; "If" ]
    "Triggers",  [ "Enable trigger"; "Disable trigger" ]
    "Debug",     [ "Print" ]
|]

/// What the dropdown shows for a node: its authored kind. The in-flight `…State` cases show as the op they
/// came from, so re-choosing a kind never shows a name the author did not write.
let private kindName (cue: Cue) : string =
    match cue with
    | Fin -> "Fin"
    | Sequence _ -> "Sequence"
    | Parallel _ -> "Parallel"
    | Fork _ -> "Fork"
    | Wait _ | WaitState _ -> "Wait"
    | FadeOut _ | FadeOutState _ -> "Fade out"
    | FadeIn _ | FadeInState _ -> "Fade in"
    | Exposit _ -> "Exposit"
    | Print _ -> "Print"
    | SetZone _ -> "Set zone"
    | AddZone _ -> "Add zone"
    | RemoveZone _ -> "Remove zone"
    | PreloadZone _ -> "Preload zone"
    | SetActor _ -> "Set actor"
    | RemoveActor _ -> "Remove actor"
    | Animate (_, clip, _, _) -> blendOpName false clip clip
    | CrossFade (_, fromClip, toClip, _, _) -> blendOpName false fromClip toClip
    | Morph (_, morph, _, _) -> blendOpName true morph morph
    | CrossMorph (_, fromMorph, toMorph, _, _) -> blendOpName true fromMorph toMorph
    | BlendState (_, morph, fromName, toName, _, _, _, _, _) -> blendOpName morph fromName toName
    | MoveActor _ | MoveActorState _ -> "Move actor"
    | RotateActor _ | RotateActorState _ -> "Rotate actor"
    | EnablePlayer _ -> "Enable player"
    | DisablePlayer _ -> "Disable player"
    | EnableTrigger _ -> "Enable trigger"
    | DisableTrigger _ -> "Disable trigger"
    | SetCamera _ -> "Set camera"
    | FlyCamera _ | FlyCameraState _ -> "Fly camera"
    | AddAdvent _ -> "Add advent"
    | RemoveAdvent _ -> "Remove advent"
    | Await _ | AwaitState _ -> "Await"
    | If _ -> "If"

/// What choosing a kind replaces the node with. Every op is reachable without an editor per op.
///
/// A `Sequence` defaults deliberately to something rather than nothing: a `Fin` front and an empty queue
/// (the walk pulls the queue's front into the front in the same step, so the author can add to it and it).
/// A `Set zone` defaults to the start place — a place is a closed value, so there is no empty-name state to
/// guard; an `Exposit` defaults to an empty line and a plain dialogue kind.
let private defaultOfKind (kind: string) : Cue =
    let defaultAdvent = List.head Advent.all
    match kind with
    | "Fin" -> Fin
    | "Sequence" -> Sequence(Fin, FDeque.ofList [])
    | "Parallel" -> Parallel(Fin, Fin)
    | "Fork" -> Fork Fin
    | "Wait" -> Wait 1.0
    | "Fade out" -> FadeOut 1.0
    | "Fade in" -> FadeIn 1.0
    | "Exposit" -> Exposit("", Dialogue.Kind.Dialogue)
    | "Print" -> Print ""
    | "Set zone" -> SetZone Place.start
    | "Add zone" -> AddZone Place.start
    | "Remove zone" -> RemoveZone Place.start
    | "Preload zone" -> PreloadZone Place.start
    | "Set actor" -> SetActor(Place.start, List.head Roster.characters, List.head Casting.allLooks, "")
    | "Remove actor" -> RemoveActor(List.head Roster.characters)
    | "Move actor" -> MoveActor(List.head Roster.characters, "", 3.5, "Jog", "Idle")
    | "Rotate actor" -> RotateActor(List.head Roster.characters, "", 1.0)
    | "Enable player" -> EnablePlayer(List.head Roster.characters)
    | "Disable player" -> DisablePlayer(List.head Roster.characters)
    | "Enable trigger" -> EnableTrigger(Place.start, "")
    | "Disable trigger" -> DisableTrigger(Place.start, "")
    | "Animate" -> Animate(List.head Roster.characters, "Idle", 1.0f, 1.0)
    | "Cross fade" -> CrossFade(List.head Roster.characters, "Idle", "Jog", 1.0f, 1.0)
    | "Morph" -> Morph(List.head Roster.characters, "", 1.0f, 1.0)
    | "Cross morph" -> CrossMorph(List.head Roster.characters, "", "", 1.0f, 1.0)
    | "Set camera" -> SetCamera ""
    | "Add advent" -> AddAdvent defaultAdvent
    | "Remove advent" -> RemoveAdvent defaultAdvent
    | "Await" -> Await defaultAdvent
    | "If" -> If(defaultAdvent, Fin, Fin)
    | _ -> FlyCamera("", 1.0)

/// The node's editable text, or `ValueNone` when it has none. The field that edits it belongs to the caller
/// (the buffer is a widget's scaffolding, not the cue's), so the control names no editor state and says only
/// which nodes carry text and how to write it back. A `SetZone` carries a *place* now, chosen from the
/// closed set (`drawEditor`), not text.
///
/// The text-bearing kinds are never executing — only the two in-flight leaves are, and neither carries text —
/// so a caller drawing this field needs no lock check of its own.
let textOf (cue: Cue) : string voption =
    match cue with
    | Exposit (text, _) -> ValueSome text
    | Print text -> ValueSome text
    | SetActor (_, _, _, waypoint) -> ValueSome waypoint
    | Animate (_, clip, _, _) -> ValueSome clip
    | CrossFade (_, fromClip, _, _, _) -> ValueSome fromClip
    | Morph (_, morph, _, _) -> ValueSome morph
    | CrossMorph (_, fromMorph, _, _, _) -> ValueSome fromMorph
    | MoveActor (_, _, _, movingClip, _) -> ValueSome movingClip
    | SetCamera waypoint -> ValueSome waypoint
    | FlyCamera (path, _) -> ValueSome path
    | _ -> ValueNone

/// The node's **second** editable text field, or `ValueNone` when it has none. Only the ops that carry two
/// strings do: a cross-fade's target clip/morph, and a move's idle clip (the first field is its moving clip).
let textOf2 (cue: Cue) : string voption =
    match cue with
    | CrossFade (_, _, toClip, _, _) -> ValueSome toClip
    | CrossMorph (_, _, toMorph, _, _) -> ValueSome toMorph
    | MoveActor (_, _, _, _, idleClip) -> ValueSome idleClip
    | _ -> ValueNone

/// The node with its text replaced — the write the caller's field commits.
let withText (cue: Cue) (text: string) : Cue =
    match cue with
    | Exposit (_, kind) -> Exposit(text, kind)
    | Print _ -> Print text
    | SetActor (place, character, look, _) -> SetActor(place, character, look, text)
    | Animate (character, _, weight, duration) -> Animate(character, text, weight, duration)
    | CrossFade (character, _, toClip, weight, duration) -> CrossFade(character, text, toClip, weight, duration)
    | Morph (character, _, weight, duration) -> Morph(character, text, weight, duration)
    | CrossMorph (character, _, toMorph, weight, duration) -> CrossMorph(character, text, toMorph, weight, duration)
    | MoveActor (character, path, speed, _, idleClip) -> MoveActor(character, path, speed, text, idleClip)
    | SetCamera _ -> SetCamera text
    | FlyCamera (_, speed) -> FlyCamera(text, speed)
    | _ -> invalidOp (sprintf "CueTree: a %s carries no text" (label cue))

/// The node with its second text field replaced — the write the caller's second field commits.
let withText2 (cue: Cue) (text: string) : Cue =
    match cue with
    | CrossFade (character, fromClip, _, weight, duration) -> CrossFade(character, fromClip, text, weight, duration)
    | CrossMorph (character, fromMorph, _, weight, duration) -> CrossMorph(character, fromMorph, text, weight, duration)
    | MoveActor (character, path, speed, movingClip, _) -> MoveActor(character, path, speed, movingClip, text)
    | _ -> invalidOp (sprintf "CueTree: a %s carries no second text" (label cue))

/// The node with its waypoint (or path) replaced — the write the waypoint chooser commits: a camera op's
/// waypoint, the set op's waypoint, the move op's path, or the rotate op's waypoint.
let withWaypoint (name: string) (cue: Cue) : Cue =
    match cue with
    | SetCamera _ -> SetCamera name
    | FlyCamera (_, speed) -> FlyCamera(name, speed)
    | SetActor (place, character, look, _) -> SetActor(place, character, look, name)
    | MoveActor (character, _, speed, movingClip, idleClip) -> MoveActor(character, name, speed, movingClip, idleClip)
    | RotateActor (character, _, duration) -> RotateActor(character, name, duration)
    | _ -> invalidOp (sprintf "CueTree: a %s carries no waypoint" (label cue))

/// The node with its trigger name replaced — the write the trigger chooser commits. Only the trigger toggles
/// carry one.
let withTrigger (name: string) (cue: Cue) : Cue =
    match cue with
    | EnableTrigger (place, _) -> EnableTrigger(place, name)
    | DisableTrigger (place, _) -> DisableTrigger(place, name)
    | _ -> invalidOp (sprintf "CueTree: a %s carries no trigger" (label cue))

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

/// The node with its place replaced — the write the place chooser commits. Only the zone ops carry one
/// (specs 061, 062).
let withPlace (place: Place) (cue: Cue) : Cue =
    match cue with
    | SetZone _ -> SetZone place
    | AddZone _ -> AddZone place
    | RemoveZone _ -> RemoveZone place
    | PreloadZone _ -> PreloadZone place
    | SetActor (_, character, look, waypoint) -> SetActor(place, character, look, waypoint)
    | EnableTrigger (_, trigger) -> EnableTrigger(place, trigger)
    | DisableTrigger (_, trigger) -> DisableTrigger(place, trigger)
    | _ -> invalidOp (sprintf "CueTree: a %s carries no place" (label cue))

/// The node with its character replaced — the write the character chooser commits. Both actor ops carry one;
/// the set op's look is kept.
let withCharacter (character: Roster.Character) (cue: Cue) : Cue =
    match cue with
    | SetActor (place, _, look, waypoint) -> SetActor(place, character, look, waypoint)
    | RemoveActor _ -> RemoveActor character
    | Animate (_, clip, weight, duration) -> Animate(character, clip, weight, duration)
    | CrossFade (_, fromClip, toClip, weight, duration) -> CrossFade(character, fromClip, toClip, weight, duration)
    | Morph (_, morph, weight, duration) -> Morph(character, morph, weight, duration)
    | CrossMorph (_, fromMorph, toMorph, weight, duration) -> CrossMorph(character, fromMorph, toMorph, weight, duration)
    | MoveActor (_, path, speed, movingClip, idleClip) -> MoveActor(character, path, speed, movingClip, idleClip)
    | RotateActor (_, waypoint, duration) -> RotateActor(character, waypoint, duration)
    | EnablePlayer _ -> EnablePlayer character
    | DisablePlayer _ -> DisablePlayer character
    | _ -> invalidOp (sprintf "CueTree: a %s carries no character" (label cue))

/// The node with its look replaced — the write the look chooser commits. Only the set op carries one.
let withLook (look: Casting.Look) (cue: Cue) : Cue =
    match cue with
    | SetActor (place, character, _, waypoint) -> SetActor(place, character, look, waypoint)
    | _ -> invalidOp (sprintf "CueTree: a %s carries no look" (label cue))

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
let drawTree (idPrefix: string) (selection: int list option) (focusPath: int list option) (cue: Cue) : int list option =
    let mutable clicked = None
    for row in rows cue do
        // Spaces indent the label because the binding has no `indent`.
        let indent = String.replicate (row.Depth * 2) " "
        if row.Branch <> "" then
            do ImGui.text (indent + row.Branch)
        else
            // A structural edit re-focuses the newly selected node's row, so Tab resumes from it rather than
            // restarting at the top of the panel (the caller asks for this the frame after an Add Child / Remove).
            if focusPath = Some row.Path then do ImGui.setKeyboardFocusHere(0)
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

/// Whether `name` is offered for `filter`: a case-insensitive substring match. An empty filter offers every
/// name, which is what an unfiltered dropdown shows. Pure, so it is asserted headlessly (spec 068).
let matches (filter: string) (name: string) : bool =
    filter = "" || name.Contains(filter, StringComparison.OrdinalIgnoreCase)

/// The filter text a combo's buffer holds, read up to its NUL.
let private comboFilterText (buffer: byte[]) : string =
    let zero = Array.IndexOf(buffer, 0uy)
    let length = if zero < 0 then buffer.Length else zero
    if length = 0 then "" else System.Text.Encoding.UTF8.GetString(buffer, 0, length)

/// A combo with a filter field (spec 068): type to narrow `items` by the name `nameOf` gives (case-insensitive
/// substring); Enter takes the top match and closes the dropdown; a click picks as today. `buffer` belongs to the
/// caller and is cleared when the dropdown appears, so a newly opened combo starts unfiltered.
let filteredCombo (buffer: byte[]) (label: string) (preview: string) (items: 'a list) (nameOf: 'a -> string) (current: 'a) (onPick: 'a -> unit) : unit =
    if ImGui.beginCombo(label, preview, 0) then
        if ImGui.isWindowAppearing() then
            do Array.Clear(buffer, 0, buffer.Length)
            do ImGui.setKeyboardFocusHere(0)
        let submitted = ImGui.inputText("##filter" + label, buffer, unativeint buffer.Length, ImGui.enterReturnsTrue, IntPtr.Zero, IntPtr.Zero)
        let query = comboFilterText buffer
        let visible = items |> List.filter (fun item -> matches query (nameOf item))
        for item in visible do
            let name = nameOf item
            if selectable (name + "##" + label + name) (item = current) && item <> current then
                do onPick item
        match visible with
        | top :: _ when submitted ->
            do onPick top
            do ImGui.closeCurrentPopup()
        | _ -> ()
        do ImGui.endCombo()

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
    (filter: byte[])
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

    // The character chooser every actor op shares: the roster is a closed set, so the control offers it with no
    // zone or asset read (`FR-006`).
    let characterCombo (character: Roster.Character) (node: Cue) : unit =
        filteredCombo filter ("Character##" + idBase) (Roster.characterName character) Roster.characters Roster.characterName character (fun candidate ->
            do commit (withAt selection (withCharacter candidate node) cue))

    // ── the kind and the numbers: what is executing is never retuned or replaced ──
    if not locked then

        // Re-choosing the kind a node already is must not clobber the numbers it carries, so the node is
        // replaced only when the kind actually changes (the Dialogue panel's rule for its own kind combo).
        let current = kindName node
        if ImGui.beginCombo("Kind##" + idBase, current, 0) then
            if ImGui.isWindowAppearing() then
                do Array.Clear(filter, 0, filter.Length)
                do ImGui.setKeyboardFocusHere(0)
            let submitted = ImGui.inputText("##filterKind##" + idBase, filter, unativeint filter.Length, ImGui.enterReturnsTrue, IntPtr.Zero, IntPtr.Zero)
            let query = comboFilterText filter
            let offered =
                if query = "" then
                    // categories: a separator divides each group, since ImGui combos have no submenus and the op
                    // list is long. (The group names in `kinds` label the grouping; only the divider is drawn.)
                    let mutable first = true
                    for (_, groupKinds) in kinds do
                        if not first then do ImGui.separator()
                        first <- false
                        for kind in groupKinds do
                            if selectable kind (kind = current) && kind <> current then
                                do commit (withAt selection (defaultOfKind kind) cue)
                    []
                else
                    // flat while filtering
                    let visible = kinds |> Array.toList |> List.collect snd |> List.filter (matches query)
                    for kind in visible do
                        if selectable kind (kind = current) && kind <> current then
                            do commit (withAt selection (defaultOfKind kind) cue)
                    visible
            match offered with
            | top :: _ when submitted ->
                do commit (withAt selection (defaultOfKind top) cue)
                do ImGui.closeCurrentPopup()
            | _ -> ()
            do ImGui.endCombo()

        // A `…State` case is the in-flight form, so it is normally the op that is running and never reaches
        // here; an author who put one in a queue gets its authored numbers, which is the same edit.
        match node with
        | Wait seconds | WaitState (seconds, _) ->
            dragNumber idBase "Seconds" (float32 seconds) (fun v -> commit (withAt selection (Wait(float v)) cue))

        | FadeOut duration | FadeOutState (_, duration, _) ->
            // a fade's own number: how long it takes. Its endpoint is the op's, so the authored form carries
            // only the duration.
            dragNumber idBase "Seconds" (float32 duration) (fun v -> commit (withAt selection (FadeOut(float v)) cue))

        | FadeIn duration | FadeInState (_, duration, _) ->
            dragNumber idBase "Seconds" (float32 duration) (fun v -> commit (withAt selection (FadeIn(float v)) cue))

        | FlyCamera (path, speed) ->
            // a flight's own number: how fast to travel. Its path is text, which the caller's field edits,
            // and its start and end are the chain's, so the authored form carries neither.
            dragNumber idBase "Speed" (float32 speed) (fun v -> commit (withAt selection (FlyCamera(path, float v)) cue))

        | AddAdvent advent | RemoveAdvent advent | Await advent | AwaitState advent | If (advent, _, _) ->
            // the milestone, chosen from the closed list — a milestone needs no zone or registry, so the
            // control can offer it where it could not offer a zone or waypoint name (FR-006).
            filteredCombo filter ("Advent##" + idBase) (Advent.name advent) Advent.all Advent.name advent (fun candidate ->
                do commit (withAt selection (withAdvent candidate node) cue))

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
            filteredCombo filter ("Presentation##" + idBase) presentation [ "Dialogue"; "Thought"; "Portrait" ] id presentation (fun choice ->
                let next =
                    match choice with
                    | "Dialogue" -> Dialogue.Kind.Dialogue
                    | "Thought" -> Dialogue.Kind.Thought
                    | _ -> Dialogue.Kind.PortraitDialogue(List.head Roster.characters, Dialogue.Emotion.Neutral)
                do commit (withAt selection (withExpositKind next node) cue))

            // the speaker and the expression are meaningful only for a portrait line, so they are offered only
            // for that kind: a plain line or a thought has neither.
            match kind with
            | Dialogue.Kind.PortraitDialogue (character, emotion) ->
                filteredCombo filter ("Speaker##" + idBase) (Roster.characterName character) Roster.characters Roster.characterName character (fun candidate ->
                    do commit (withAt selection (withExpositKind (Dialogue.Kind.PortraitDialogue(candidate, emotion)) node) cue))
                filteredCombo filter ("Expression##" + idBase) (Dialogue.emotionName emotion) Dialogue.emotions Dialogue.emotionName emotion (fun candidate ->
                    do commit (withAt selection (withExpositKind (Dialogue.Kind.PortraitDialogue(character, candidate)) node) cue))
            | _ -> ()

        | SetZone place | AddZone place | RemoveZone place | PreloadZone place ->
            // the place, chosen from the closed set — a place needs no zone or registry read, so the control can
            // offer it where it could not offer a zone name (spec 061 FR-005); the four zone ops share it.
            filteredCombo filter ("Place##" + idBase) (Place.placeName place) Place.all Place.placeName place (fun candidate ->
                do commit (withAt selection (withPlace candidate node) cue))

        | SetActor (place, character, look, _) ->
            // the place, the character and the look are all closed sets, so the control offers them with no
            // zone or asset read (FR-006); the waypoint it names is edited by the caller's field (`CueEditor`).
            filteredCombo filter ("Place##" + idBase) (Place.placeName place) Place.all Place.placeName place (fun candidate ->
                do commit (withAt selection (withPlace candidate node) cue))
            filteredCombo filter ("Character##" + idBase) (Roster.characterName character) Roster.characters Roster.characterName character (fun candidate ->
                do commit (withAt selection (withCharacter candidate node) cue))
            filteredCombo filter ("Look##" + idBase) (Casting.lookName look) Casting.allLooks Casting.lookName look (fun candidate ->
                do commit (withAt selection (withLook candidate node) cue))

        | RemoveActor character ->
            // no place: the runtime cast index knows where she is, so removal names the character alone.
            filteredCombo filter ("Character##" + idBase) (Roster.characterName character) Roster.characters Roster.characterName character (fun candidate ->
                do commit (withAt selection (withCharacter candidate node) cue))

        | Animate (character, clip, weight, duration) ->
            do characterCombo character node
            dragNumber idBase "Weight" weight (fun v -> commit (withAt selection (Animate(character, clip, v, duration)) cue))
            dragNumber idBase "Seconds" (float32 duration) (fun v -> commit (withAt selection (Animate(character, clip, weight, float v)) cue))

        | CrossFade (character, fromClip, toClip, weight, duration) ->
            do characterCombo character node
            dragNumber idBase "Weight" weight (fun v -> commit (withAt selection (CrossFade(character, fromClip, toClip, v, duration)) cue))
            dragNumber idBase "Seconds" (float32 duration) (fun v -> commit (withAt selection (CrossFade(character, fromClip, toClip, weight, float v)) cue))

        | Morph (character, morph, weight, duration) ->
            do characterCombo character node
            dragNumber idBase "Weight" weight (fun v -> commit (withAt selection (Morph(character, morph, v, duration)) cue))
            dragNumber idBase "Seconds" (float32 duration) (fun v -> commit (withAt selection (Morph(character, morph, weight, float v)) cue))

        | CrossMorph (character, fromMorph, toMorph, weight, duration) ->
            do characterCombo character node
            dragNumber idBase "Weight" weight (fun v -> commit (withAt selection (CrossMorph(character, fromMorph, toMorph, v, duration)) cue))
            dragNumber idBase "Seconds" (float32 duration) (fun v -> commit (withAt selection (CrossMorph(character, fromMorph, toMorph, weight, float v)) cue))

        | MoveActor (character, path, speed, movingClip, idleClip) ->
            // the character and the speed; its path and clips are the caller's fields (`CueEditor`)
            do characterCombo character node
            dragNumber idBase "Speed" (float32 speed) (fun v -> commit (withAt selection (MoveActor(character, path, float v, movingClip, idleClip)) cue))

        | RotateActor (character, waypoint, duration) ->
            do characterCombo character node
            dragNumber idBase "Seconds" (float32 duration) (fun v -> commit (withAt selection (RotateActor(character, waypoint, float v)) cue))

        | EnablePlayer character | DisablePlayer character ->
            // only the character; the op is otherwise fixed
            do characterCombo character node

        | EnableTrigger (place, _) | DisableTrigger (place, _) ->
            // the place, chosen from the closed set; the trigger name is the caller's field (`CueEditor`)
            filteredCombo filter ("Place##" + idBase) (Place.placeName place) Place.all Place.placeName place (fun candidate ->
                do commit (withAt selection (withPlace candidate node) cue))

        | Fin | Sequence _ | Parallel _ | Fork _ | SetCamera _ | Print _ | FlyCameraState _ | BlendState _ | MoveActorState _ | RotateActorState _ ->
            // no numbers of their own. `SetCamera` and `Print` carry text (the caller's field edits it) and a
            // `FlyCamera` carries its path and speed above; a zone op's place is chosen above.
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
