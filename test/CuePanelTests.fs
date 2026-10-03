module CuePanelTests

// The cue panel's readable half, asserted headlessly (spec 047): the rows it derives, the rule that decides
// what is read-only, the paths that address a node. Nothing here opens a window, a GPU or a device — the
// panel's drawing is the walkthrough's business, and `FR-007`'s pure function is the whole reason what it
// *shows* can be asserted at all.
//
// The panel's write path and the cue's edits on the one undo history (spec 049) are asserted here too: an
// accepted edit is one `SetCue` step on `state.Editor.Undo`, and `History`'s steppers take it back and re-apply
// it beside a scene step.
//
// Nothing compares two `Cue` values: a cue holds an `FDeque` and the engine deliberately answers "same value"
// by reference identity, so the suite reads cues through `describe` instead — the same view of a cue a person
// has.

open Xunit
open System
open System.Collections.Generic
open System.Numerics
open Prime
open Command
open Cue
open CueTree
open EditorState
open CueEditor
open Entity
open Physics
open Advent

// ── fixtures ──

/// Authoring sugar: a list of ops as the cue they denote.
let private seqOf (cues: Cue list) : Cue =
    match cues with
    | [] -> Fin
    | [ only ] -> only
    | head :: tail -> Sequence(head, FDeque.ofList tail)

/// The cue as a person reads it, for assertions that would otherwise compare cue values.
let rec private describe (cue: Cue) : string =
    match cue with
    | Fin -> "Fin"
    | Sequence (current, rest) ->
        let parts = Seq.append (Seq.singleton (describe current)) (FDeque.toSeq rest |> Seq.map describe)
        "Seq[" + String.concat ", " parts + "]"
    | Parallel (l, r) -> sprintf "Par(%s, %s)" (describe l) (describe r)
    | Fork c -> sprintf "Fork(%s)" (describe c)
    | Wait s | WaitState (s, _) -> sprintf "Wait %g" s
    | Exposit (t, _) -> sprintf "Exposit(%s)" t
    | SwitchZone z -> sprintf "Zone(%s)" z
    | SetCamera w -> sprintf "Camera(%s)" w
    | FlyCamera (p, _) | FlyCameraState (p, _, _, _, _) -> sprintf "Fly(%s)" p
    | AddAdvent m -> sprintf "Add(%s)" (Advent.name m)
    | RemoveAdvent m -> sprintf "Remove(%s)" (Advent.name m)
    | Await m | AwaitState m -> sprintf "Await(%s)" (Advent.name m)
    | If (m, _, _) -> sprintf "If(%s)" (Advent.name m)

let private raises (f: unit -> unit) : bool =
    try
        f ()
        false
    with _ ->
        true

let private lockedPaths (cue: Cue) : int list list =
    rows cue |> List.filter (fun (r: Row) -> r.Locked) |> List.map (fun r -> r.Path)

/// A headless gameplay state, the way `test/CueTests.fs` builds one: `Unchecked.defaultof` for the GPU pieces,
/// so the panel's write path is assertable with no device.
let private newState () : GameplayState.GameplayState =
    GameplayState.create (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<Ui.Ui>)

/// A headless `SharedState` for the history cases: a real physics world and a device-free, empty asset manager.
/// Nothing here spawns an entity — the transform below acts on an actor placed directly in the zone, and a cue
/// step touches no resource — so only the physics world is ever read.
let private sharedState () : SharedState.SharedState =
    let assets : Assets.AssetManager =
        TestSupport.manager IntPtr.Zero (Dictionary()) (Dictionary()) (Dictionary())

    { Gpu = Unchecked.defaultof<Gpu.Gpu>
      Assets = assets
      Cast = { Casting.Bindings = Dictionary() }
      ZoneRegistry = ZoneRegistry.create ()
      Advents = Set.empty
      Physics = Physics.create () }

/// An actor placed directly in a zone, its renderer left default: enough for a `Transform` step, which rebuilds
/// only the physics body and the authored state.
let private placedActor (name: string) : Actor =
    let baseEntity : Entity =
        { Name = name
          Transform = transformIdentity
          Enabled = true
          Visible = true
          Selectable = true
          Draggable = true }

    { State = { Base = baseEntity; AssetId = ""; ShowCollider = false }
      Physics = None
      Renderer = Unchecked.defaultof<ActorRender.ActorRender> }

// ══════ the rows (FR-002, FR-004, SC-001) ══════

[<Fact>]
let ``a bare leaf is one row at depth 0`` () =
    let rs = rows (Wait 2.0)
    Assert.Equal(1, List.length rs)
    Assert.Equal<int list>([], (List.head rs).Path)
    Assert.Equal(0, (List.head rs).Depth)
    Assert.Equal("Wait 2", (List.head rs).Label)

[<Fact>]
let ``a sequence is a row with its children indented one deeper, front first`` () =
    let rs = rows (seqOf [ Wait 1.0; Exposit("hello", Dialogue.Kind.Dialogue); Wait 2.0 ])
    Assert.Equal<string list>([ "Sequence"; "Wait 1"; "Exposit: hello"; "Wait 2" ], List.map (fun r -> r.Label) rs)
    Assert.Equal<int list>([ 0; 1; 1; 1 ], List.map (fun r -> r.Depth) rs)
    Assert.Equal<int list list>([ []; [ 0 ]; [ 1 ]; [ 2 ] ], List.map (fun r -> r.Path) rs)

[<Fact>]
let ``a leaf in flight is labelled as the op it came from`` () =
    // the cue the engine makes of `Wait 2` part-way through: the view is what is left to run, never an
    // internal state's name. The in-flight form and the authored form read the same, which is the point.
    let running = seqOf [ WaitState(2.0, 10.0); Exposit("later", Dialogue.Kind.Dialogue) ]
    Assert.Equal<string list>([ "Sequence"; "Wait 2"; "Exposit: later" ], List.map (fun r -> r.Label) (rows running))

[<Fact>]
let ``Fin is a row with no children`` () =
    let rs = rows Fin
    Assert.Equal(1, List.length rs)
    Assert.Equal("Fin", (List.head rs).Label)
    Assert.Equal<int list list>([ [] ], List.map (fun r -> r.Path) rs)

[<Fact>]
let ``the rows are a function of the cue alone`` () =
    // a caller cannot change what a cue says: the derivation takes a cue and nothing else (SC-003)
    let cue = seqOf [ WaitState(2.0, 10.0); Exposit("x", Dialogue.Kind.Dialogue) ]
    let project (rs: Row list) = List.map (fun r -> r.Path, r.Depth, r.Label, r.Locked) rs
    Assert.Equal<(int list * int * string * bool) list>(project (rows cue), project (rows cue))

[<Fact>]
let ``a row's id follows the caller's prefix and the node's path`` () =
    // two cues drawn in one window cannot collide, and a node keeps its id from frame to frame (FR-006)
    Assert.NotEqual<string>(rowId "cue" [ 0 ], rowId "trigger" [ 0 ])
    Assert.NotEqual<string>(rowId "cue" [ 0 ], rowId "cue" [ 1 ])
    Assert.Equal(rowId "cue" [ 0; 1 ], rowId "cue" [ 0; 1 ])
    Assert.NotEqual<string>(rowId "cue" [], rowId "cue" [ 0 ])

[<Fact>]
let ``every kind's row says what it is, and a leaf in flight as the op it came from`` () =
    // the whole vocabulary, one kind at a time: FR-004's "never an internal state's name" is the point of
    // the last two assertions
    let labelOf (cue: Cue) : string = (List.head (rows cue)).Label
    Assert.Equal("Fin", labelOf Fin)
    Assert.Equal("Sequence", labelOf (seqOf [ Wait 1.0; Exposit("a", Dialogue.Kind.Dialogue) ]))
    Assert.Equal("Parallel", labelOf (Parallel(Wait 1.0, Wait 2.0)))
    Assert.Equal("Fork", labelOf (Fork(Wait 1.0)))
    Assert.Equal("Wait 1", labelOf (Wait 1.0))
    Assert.Equal("Exposit: hello", labelOf (Exposit("hello", Dialogue.Kind.Dialogue)))
    Assert.Equal("Zone: classroom", labelOf (SwitchZone "classroom"))
    Assert.Equal("Camera: balcony", labelOf (SetCamera "balcony"))
    Assert.Equal("Fly: route", labelOf (FlyCamera("route", 1.0)))
    // ... and the in-flight forms read as the ops they came from
    Assert.Equal("Wait 2", labelOf (WaitState(2.0, 10.0)))
    Assert.Equal("Fly: route", labelOf (FlyCameraState("route", [| Vector3.Zero; Vector3.One |], [| Quaternion.Identity; Quaternion.Identity |], 1.0, 5.0)))
    Assert.Equal("Add: MetAiko", labelOf (AddAdvent MetAiko))
    Assert.Equal("Remove: MetAiko", labelOf (RemoveAdvent MetAiko))
    Assert.Equal("Await: MetAiko", labelOf (Await MetAiko))
    Assert.Equal("Await: MetAiko", labelOf (AwaitState MetAiko))
    Assert.Equal("If: MetAiko", labelOf (If(MetAiko, Fin, Fin)))

[<Fact>]
let ``a sequence inside a sequence is indented one deeper again, and a container's sides are siblings`` () =
    let nested = rows (seqOf [ Wait 1.0; seqOf [ Exposit("a", Dialogue.Kind.Dialogue); Wait 2.0 ] ])
    Assert.Equal<string list>([ "Sequence"; "Wait 1"; "Sequence"; "Exposit: a"; "Wait 2" ], List.map (fun r -> r.Label) nested)
    Assert.Equal<int list>([ 0; 1; 1; 2; 2 ], List.map (fun r -> r.Depth) nested)
    Assert.Equal<int list list>([ []; [ 0 ]; [ 1 ]; [ 1; 0 ]; [ 1; 1 ] ], List.map (fun r -> r.Path) nested)

    // a `Parallel`'s two sides are siblings at one depth, and a `Fork`'s branch sits under it
    let both = rows (Parallel(Wait 1.0, Wait 2.0))
    Assert.Equal<string list>([ "Parallel"; "Wait 1"; "Wait 2" ], List.map (fun r -> r.Label) both)
    Assert.Equal<int list>([ 0; 1; 1 ], List.map (fun r -> r.Depth) both)
    Assert.Equal<int list list>([ []; [ 0 ]; [ 1 ] ], List.map (fun r -> r.Path) both)

    let fork = rows (Fork(Wait 1.0))
    Assert.Equal<string list>([ "Fork"; "Wait 1" ], List.map (fun r -> r.Label) fork)
    Assert.Equal<int list>([ 0; 1 ], List.map (fun r -> r.Depth) fork)
    Assert.Equal<int list list>([ []; [ 0 ] ], List.map (fun r -> r.Path) fork)

// ══════ the read-only rule (FR-011, SC-006) ══════

[<Fact>]
let ``a fresh authored cue locks nothing`` () =
    // nothing has begun, so everything is editable — which is what makes the panel an authoring surface for
    // a cue that has not run yet
    Assert.Equal<int list list>([], lockedPaths (seqOf [ Wait 1.0; Exposit("x", Dialogue.Kind.Dialogue); Wait 2.0 ]))

[<Fact>]
let ``a cue part-way through a wait locks the running path and not the queue`` () =
    let running = seqOf [ WaitState(2.0, 10.0); Exposit("later", Dialogue.Kind.Dialogue); Wait 3.0 ]
    // the sequence and the op in flight: the containers above it, from the root down
    Assert.Equal<int list list>([ []; [ 0 ] ], lockedPaths running)
    // and every op queued behind it is editable
    let editable = rows running |> List.filter (fun (r: Row) -> not r.Locked) |> List.map (fun r -> r.Path)
    Assert.Equal<int list list>([ [ 1 ]; [ 2 ] ], editable)

[<Fact>]
let ``a running parallel locks both branches`` () =
    // a parallel runs its sides at once, so neither is behind the other and neither is editable
    let running = Parallel(WaitState(1.0, 5.0), Sequence(WaitState(2.0, 9.0), FDeque.ofList [ Exposit("x", Dialogue.Kind.Dialogue) ]))
    Assert.Equal<int list list>([ []; [ 0 ]; [ 1 ]; [ 1; 0 ] ], lockedPaths running)

[<Fact>]
let ``a fork's inner branch is locked while the remainder behind it is not`` () =
    // a `Fork` is only meaningful as a sequence's front op: the branch runs beside the remainder, so it is
    // executing, while what is queued behind the fork is not
    let running = Sequence(Fork(WaitState(2.0, 10.0)), FDeque.ofList [ Exposit("after", Dialogue.Kind.Dialogue) ])
    Assert.Equal<int list list>([ []; [ 0 ]; [ 0; 0 ] ], lockedPaths running)
    Assert.False((rows running |> List.last).Locked)

[<Fact>]
let ``a running flight is locked and reads as the op it came from`` () =
    let flying = FlyCameraState("route", [| Vector3.Zero; Vector3.One |], [| Quaternion.Identity; Quaternion.Identity |], 2.0, 2.0)
    Assert.Equal<string list>([ "Fly: route" ], List.map (fun (r: Row) -> r.Label) (rows flying))
    Assert.True((List.head (rows flying)).Locked)

[<Fact>]
let ``an if shows its two branches under then and else labels, and a running await is locked`` () =
    let rs = rows (If(MetAiko, Exposit("t", Dialogue.Kind.Dialogue), Exposit("e", Dialogue.Kind.Dialogue)))
    // the two branches are told apart by a label row above each, one indentation deeper than the `If`
    Assert.Equal<string list>([ "If: MetAiko"; "then"; "Exposit: t"; "else"; "Exposit: e" ], List.map (fun (r: Row) -> r.Label) rs)
    Assert.Equal<string list>([ ""; "then"; ""; "else"; "" ], List.map (fun (r: Row) -> r.Branch) rs)
    Assert.Equal<int list>([ 0; 1; 2; 1; 2 ], List.map (fun (r: Row) -> r.Depth) rs)
    Assert.Equal<int list list>([ []; [ 0 ]; [ 0 ]; [ 1 ]; [ 1 ] ], List.map (fun (r: Row) -> r.Path) rs)

    // a running await is read-only; an authored one is not
    let waiting = rows (AwaitState MetAiko)
    Assert.Equal("Await: MetAiko", (List.head waiting).Label)
    Assert.True((List.head waiting).Locked)
    Assert.False((List.head (rows (Await MetAiko))).Locked)

[<Fact>]
let ``an addition is offered to a sequence whether or not it is running`` () =
    let running = seqOf [ WaitState(2.0, 10.0); Exposit("later", Dialogue.Kind.Dialogue) ]
    // a sequence that is executing still takes a child: its queue is ahead of the op in flight, so an
    // addition to it disturbs nothing (FR-011)
    Assert.True(canAppend [] running)
    // the op in flight takes nothing, and neither does the queued op (it is not a container)
    Assert.False(canAppend [ 0 ] running)
    Assert.False(canAppend [ 1 ] running)
    // a sequence that has not started is the plain authoring case, and takes one too (`seqOf` collapses a
    // single op, so the nested sequence needs two)
    let authored = seqOf [ Wait 1.0; seqOf [ Exposit("a", Dialogue.Kind.Dialogue); Wait 2.0 ] ]
    Assert.True(canAppend [] authored)
    Assert.True(canAppend [ 1 ] authored)
    // a leaf takes none, a parallel's sides are the type's rather than the author's, and a path that names
    // nothing is a question this answers rather than raises
    Assert.False(canAppend [] (Wait 1.0))
    Assert.False(canAppend [] (Parallel(Fin, Fin)))
    Assert.False(canAppend [ 7 ] running)

// ══════ the paths (FR-005, SC-007) ══════

[<Fact>]
let ``tryAt finds the node a path names and reports one that names nothing`` () =
    let cue = seqOf [ Wait 1.0; seqOf [ Exposit("a", Dialogue.Kind.Dialogue); Wait 2.0 ] ]
    Assert.Equal("Wait 1", describe (Option.defaultValue Fin (tryAt [ 0 ] cue)))
    Assert.Equal("Exposit(a)", describe (Option.defaultValue Fin (tryAt [ 1; 0 ] cue)))
    Assert.Equal("Seq[Wait 1, Seq[Exposit(a), Wait 2]]", describe (Option.defaultValue Fin (tryAt [] cue)))
    Assert.Equal(None, tryAt [ 1; 0; 0 ] cue)
    Assert.Equal(None, tryAt [ 9 ] cue)

[<Fact>]
let ``withAt replaces exactly the node a path names and leaves the rest alone`` () =
    let cue = seqOf [ Wait 1.0; Exposit("a", Dialogue.Kind.Dialogue); Wait 2.0 ]
    Assert.Equal("Seq[Wait 1, Exposit(b), Wait 2]", describe (withAt [ 1 ] (Exposit("b", Dialogue.Kind.Dialogue)) cue))
    // ... including a node inside a nested sequence
    let nested = seqOf [ Wait 1.0; seqOf [ Exposit("a", Dialogue.Kind.Dialogue); Wait 2.0 ] ]
    Assert.Equal("Seq[Wait 1, Seq[Exposit(b), Wait 2]]", describe (withAt [ 1; 0 ] (Exposit("b", Dialogue.Kind.Dialogue)) nested))

[<Fact>]
let ``withoutAt removes exactly the node a path names`` () =
    let cue = seqOf [ Wait 1.0; Exposit("a", Dialogue.Kind.Dialogue); Wait 2.0 ]
    Assert.Equal("Seq[Wait 1, Wait 2]", describe (withoutAt [ 1 ] cue))
    // removing a branch leaves the other, the engine's own rule for a side that is finished
    Assert.Equal("Wait 1", describe (withoutAt [ 1 ] (Parallel(Wait 1.0, Wait 2.0))))
    // removing the queue's last entry leaves the front op alone: the same shape the engine makes when a
    // queue empties (`Sequence(current, empty)` is a value it produces, not a degenerate one)
    Assert.Equal("Seq[Wait 1]", describe (withoutAt [ 1 ] (Sequence(Wait 1.0, FDeque.ofList [ Exposit("a", Dialogue.Kind.Dialogue) ]))))

[<Fact>]
let ``a path that names nothing raises from every write`` () =
    let cue = seqOf [ Wait 1.0 ]
    Assert.True(raises (fun () -> withAt [ 7 ] Fin cue |> ignore))
    Assert.True(raises (fun () -> withoutAt [ 7 ] cue |> ignore))
    Assert.True(raises (fun () -> withAppendedChild [ 7 ] (Wait 1.0) cue |> ignore))
    // a leaf takes no children, so appending under it is a bug rather than a no-op
    Assert.True(raises (fun () -> withAppendedChild [ 0 ] (Wait 1.0) cue |> ignore))
    // and the harmless question answers
    Assert.Equal(None, tryAt [ 7 ] cue)

[<Fact>]
let ``the root cannot be removed`` () =
    Assert.True(raises (fun () -> withoutAt [] (seqOf [ Wait 1.0 ]) |> ignore))

[<Fact>]
let ``appending to a sequence gives the node and the new child's path`` () =
    let cue = seqOf [ Wait 1.0; Exposit("a", Dialogue.Kind.Dialogue) ]
    let node, childPath = withAppendedChild [] (Exposit("b", Dialogue.Kind.Dialogue)) cue
    Assert.Equal<int list>([ 2 ], childPath)
    Assert.Equal("Seq[Wait 1, Exposit(a), Exposit(b)]", describe node)
    // the two agree: writing it back is what the panel commits, and the path is the child just added
    Assert.Equal("Seq[Wait 1, Exposit(a), Exposit(b)]", describe (withAt [] node cue))

[<Fact>]
let ``only the text-bearing kinds carry text, and writing it replaces exactly that`` () =
    Assert.Equal(ValueSome "hello", textOf (Exposit("hello", Dialogue.Kind.Dialogue)))
    Assert.Equal(ValueSome "classroom", textOf (SwitchZone "classroom"))
    Assert.Equal(ValueSome "balcony", textOf (SetCamera "balcony"))
    Assert.Equal(ValueSome "route", textOf (FlyCamera("route", 1.0)))
    Assert.Equal(ValueNone, textOf (Wait 1.0))
    Assert.Equal(ValueNone, textOf (AddAdvent MetAiko))
    Assert.Equal(ValueNone, textOf (Await MetAiko))
    Assert.Equal(ValueNone, textOf (If(MetAiko, Fin, Fin)))
    Assert.Equal(ValueNone, textOf Fin)
    Assert.Equal("Exposit(hi)", describe (withText (Exposit("hello", Dialogue.Kind.Dialogue)) "hi"))
    Assert.Equal("Zone(atrium)", describe (withText (SwitchZone "classroom") "atrium"))
    Assert.Equal("Camera(atrium)", describe (withText (SetCamera "balcony") "atrium"))
    Assert.Equal("Fly(route)", describe (withText (FlyCamera("lo", 2.0)) "route"))
    Assert.True(raises (fun () -> withText (Wait 1.0) "x" |> ignore))

[<Fact>]
let ``an exposit's text and presentation are written independently`` () =
    // the panel edits the line's text and its presentation as two separate writes, so each preserves the
    // other; the presentation is the closed `Dialogue.Kind`, so a portrait line always keeps its pair
    let portrait = Dialogue.Kind.PortraitDialogue(Roster.Akane, Dialogue.Emotion.Angry)
    let kindOf (cue: Cue) : Dialogue.Kind =
        match cue with
        | Exposit (_, k) -> k
        | _ -> failwith "not an exposit"

    // `textOf` reads the text whatever the presentation is
    Assert.Equal(ValueSome "x", textOf (Exposit("x", portrait)))
    // `withText` replaces the text and preserves the presentation
    let retitled = withText (Exposit("x", portrait)) "y"
    Assert.Equal("Exposit(y)", describe retitled)
    Assert.Equal(portrait, kindOf retitled)
    // `withExpositKind` replaces the presentation and preserves the text
    let rekinded = withExpositKind Dialogue.Kind.Thought (Exposit("x", portrait))
    Assert.Equal("Exposit(x)", describe rekinded)
    Assert.Equal(Dialogue.Kind.Thought, kindOf rekinded)
    // and it refuses a node that carries no presentation, as `withText` refuses one with no text
    Assert.True(raises (fun () -> withExpositKind Dialogue.Kind.Dialogue (Wait 1.0) |> ignore))

// ══════ the cue's edits on the one history (spec 049: FR-001, FR-004, SC-001, SC-004) ══════

[<Fact>]
let ``one accepted edit is one step, and the steppers walk it`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    state.Cue <- seqOf [ WaitState(2.0, 10.0); Exposit("a", Dialogue.Kind.Dialogue); Wait 3.0 ]
    let before = describe state.Cue
    let edited = withAt [ 1 ] (Exposit("b", Dialogue.Kind.Dialogue)) state.Cue

    do CueEditor.commit &state edited
    Assert.Equal("Seq[Wait 2, Exposit(b), Wait 3]", describe state.Cue)
    Assert.Equal(1, List.length state.Editor.Undo.Applied) // one edit, exactly one step
    match state.Editor.Undo.Applied with
    | [ SetCue (previous, next) ] ->
        Assert.Equal(before, describe previous)
        Assert.Equal("Seq[Wait 2, Exposit(b), Wait 3]", describe next)
    | other -> Assert.Fail(sprintf "expected one SetCue step, got %A" other)

    do History.undoHistory shared &state
    Assert.Equal(before, describe state.Cue)
    Assert.Equal(0, List.length state.Editor.Undo.Applied)
    Assert.Equal(1, List.length state.Editor.Undo.Redo)

    do History.redoHistory shared &state
    Assert.Equal("Seq[Wait 2, Exposit(b), Wait 3]", describe state.Cue)
    Assert.Equal(1, List.length state.Editor.Undo.Applied)
    Assert.Equal(0, List.length state.Editor.Undo.Redo)

[<Fact>]
let ``a cue edit and a scene edit are undone in the reverse of the order they were made`` () =
    // SC-001: one undo covers the cue and the scene.
    let shared = sharedState ()
    let mutable state = newState ()
    state.Cue <- seqOf [ Exposit("a", Dialogue.Kind.Dialogue); Wait 1.0 ]
    let cueBefore = describe state.Cue

    // Step 1: the cue edit.
    do CueEditor.commit &state (withAt [ 0 ] (Exposit("b", Dialogue.Kind.Dialogue)) state.Cue)

    // Step 2: an actor transform. The actor is placed directly in the zone, its renderer left default.
    let zone = GameplayState.activeZone state
    let handle = zone.Actors.Add(placedActor "player")
    let ref' = actorRef handle.Index handle.Generation
    let before = transformIdentity
    let after = { transformIdentity with Position = Vector3.One }
    do History.commit shared &state (Command.Transform (ref', before, after))
    Assert.Equal(after, (zone.Actors.Get handle).State.Base.Transform)

    // Undo twice: the transform first, the cue second.
    do History.undoHistory shared &state
    Assert.Equal(before, (zone.Actors.Get handle).State.Base.Transform)
    Assert.Equal("Seq[Exposit(b), Wait 1]", describe state.Cue) // the transform's undo leaves the cue alone
    do History.undoHistory shared &state
    Assert.Equal(cueBefore, describe state.Cue)

    // Redo twice: both re-applied in the order they were made.
    do History.redoHistory shared &state
    Assert.Equal("Seq[Exposit(b), Wait 1]", describe state.Cue)
    do History.redoHistory shared &state
    Assert.Equal(after, (zone.Actors.Get handle).State.Base.Transform)

[<Fact>]
let ``a cue step changes the cue and nothing else`` () =
    // FR-006: no active zone, no camera, no selection.
    let shared = sharedState ()
    let mutable state = newState ()
    state.Cue <- seqOf [ Exposit("a", Dialogue.Kind.Dialogue); Wait 1.0 ]
    do CueEditor.commit &state (withAt [ 0 ] (Exposit("b", Dialogue.Kind.Dialogue)) state.Cue)

    let zoneBefore = state.ActiveZone
    let cameraBefore = state.Camera
    state.Editor <- { state.Editor with CueSelection = Some [ 0 ] }

    do History.undoHistory shared &state
    Assert.Equal(zoneBefore, state.ActiveZone)
    Assert.Equal(cameraBefore, state.Camera)
    Assert.Equal<int list option>(Some [ 0 ], state.Editor.CueSelection)

[<Fact>]
let ``undo through the whole history returns the cue to where it started`` () =
    // SC-004, and a new edit after an undo clears the redo side.
    let shared = sharedState ()
    let mutable state = newState ()
    let cueAtStart = describe state.Cue
    let zoneAtStart = state.ActiveZone

    do CueEditor.commit &state (seqOf [ Wait 1.0 ])
    do CueEditor.commit &state (seqOf [ Wait 1.0; Exposit("a", Dialogue.Kind.Dialogue) ])
    let zone = GameplayState.activeZone state
    let handle = zone.Actors.Add(placedActor "player")
    do History.commit shared &state (Command.Transform (actorRef handle.Index handle.Generation, transformIdentity, { transformIdentity with Position = Vector3.One }))

    while not (List.isEmpty state.Editor.Undo.Applied) do
        do History.undoHistory shared &state

    Assert.Equal(cueAtStart, describe state.Cue)
    Assert.Equal(zoneAtStart, state.ActiveZone)

    // A new edit after an undo is a new branch: what was taken back is no longer reachable.
    Assert.False(List.isEmpty state.Editor.Undo.Redo)
    do CueEditor.commit &state (seqOf [ Wait 2.0 ])
    Assert.True(List.isEmpty state.Editor.Undo.Redo)

[<Fact>]
let ``an undo with nothing applied is a no-op`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    state.Cue <- seqOf [ Wait 1.0 ]
    let before = describe state.Cue

    do History.undoHistory shared &state
    Assert.Equal(before, describe state.Cue)
    do History.redoHistory shared &state
    Assert.Equal(before, describe state.Cue)

[<Fact>]
let ``a cue edit made before a zone move is still reachable after it`` () =
    // US1/AC4. A cue spans zones, so a cue edit survives a move exactly as a zone edit does: two cue edits
    // with a move between them come back in reverse, and the earlier one is reached only once the move is
    // stepped back — the same shape `specs/048`'s cross-zone case has, with the cue in it.
    let shared = sharedState ()
    let mutable state = newState ()
    state.Cue <- seqOf [ Exposit("a", Dialogue.Kind.Dialogue); Wait 1.0 ]
    let cueBefore = describe state.Cue

    do CueEditor.commit &state (withAt [ 0 ] (Exposit("b", Dialogue.Kind.Dialogue)) state.Cue) // cue edit A, in main
    let afterA = describe state.Cue
    do History.commit shared &state (ZoneAdd ("main", "classroom")) // a real move: main → classroom
    Assert.Equal("classroom", state.ActiveZone)
    do CueEditor.commit &state (withAt [ 0 ] (Exposit("c", Dialogue.Kind.Dialogue)) state.Cue) // cue edit B, in classroom
    let afterB = describe state.Cue
    Assert.NotEqual<string>(afterA, afterB)

    // B first, with classroom still on screen.
    do History.undoHistory shared &state
    Assert.Equal("classroom", state.ActiveZone)
    Assert.Equal(afterA, describe state.Cue)

    // Then the move steps back, carrying the view to main — where A is now reachable.
    do History.undoHistory shared &state
    Assert.Equal("main", state.ActiveZone)
    Assert.Equal(afterA, describe state.Cue)

    // Then A itself.
    do History.undoHistory shared &state
    Assert.Equal(cueBefore, describe state.Cue)

// ══════ the panel has no undo of its own (spec 049: FR-003, SC-002) ══════

[<Fact>]
let ``the cue panel exposes no undo or redo of its own`` () =
    // The two controls and the four functions behind them are gone; the one undo is the keyboard's and the Edit
    // menu's. Asserted on the compiled surface, because there is no device to draw the panel with.
    let surface =
        match typeof<GameplayState.GameplayState>.Assembly.GetType "CueEditor" with
        | null -> failwith "the CueEditor module type was not found in the assembly"
        | surface -> surface

    let names = surface.GetMethods() |> Array.map (fun m -> m.Name) |> Set.ofArray
    Assert.Contains("commit", names) // the anchor: the lookup resolved the module's own type
    for gone in [ "undo"; "redo"; "canUndo"; "canRedo" ] do
        Assert.DoesNotContain(gone, names)

[<Fact>]
let ``a cue edit leaves the one history something to take back`` () =
    // US2/AC3: the Edit menu's and the shortcut's enabled state reads the one history, so a cue edit is visible
    // there — no second control is needed for it.
    let mutable state = newState ()
    Assert.True(List.isEmpty state.Editor.Undo.Applied)
    do CueEditor.commit &state (seqOf [ Wait 1.0 ])
    Assert.False(List.isEmpty state.Editor.Undo.Applied)
    Assert.True(List.isEmpty state.Editor.Undo.Redo)

// ══════ the Inspector's inline trigger-cue editor (spec 058) ══════

let private baseEntity (name: string) : Entity =
    { Name = name
      Transform = transformIdentity
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

[<Fact>]
let ``a trigger cue edit is one TriggerSet step and undo restores the before-cue`` () =
    // FR-008: the cue rides the trigger's own command, so it is one step recovered like any other trigger edit.
    let shared = sharedState ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    let ref = Zone.spawnTrigger zone (baseEntity "area")
    Assert.Equal(Cue.Fin, (Zone.tryTriggerAt zone ref).Value.State.Cue)

    let edited = seqOf [ Wait 1.0; Exposit("a", Dialogue.Kind.Dialogue) ]
    do Selection.updateTriggerRef shared &state ref (fun t -> { t with Cue = edited })

    Assert.Equal(1, List.length state.Editor.Undo.Applied)
    match state.Editor.Undo.Applied with
    | [ TriggerSet (r, before, after) ] ->
        Assert.Equal<EntityRef>(ref, r)
        Assert.Equal(Cue.Fin, before.Cue)
        Assert.Equal("Seq[Wait 1, Exposit(a)]", describe after.Cue)
    | other -> Assert.Fail(sprintf "expected one TriggerSet step, got %A" other)

    do History.undoHistory shared &state
    Assert.Equal(Cue.Fin, (Zone.tryTriggerAt zone ref).Value.State.Cue)

[<Fact>]
let ``the inline trigger editor resets when the selected trigger changes`` () =
    // FR-010: a node selection or text typed for one trigger is never applied to another.
    let shared = sharedState ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    let first = Zone.spawnTrigger zone (baseEntity "first")
    let second = Zone.spawnTrigger zone (baseEntity "second")

    state.Editor <-
        { state.Editor with
            TriggerCueRef = Some first
            TriggerCueSelection = Some [ 0 ]
            TriggerCueTextPath = Some [ 0 ]
            TriggerCueEditing = true }

    // The same trigger: nothing resets.
    do CueEditor.resetTriggerEditor &state first
    Assert.Equal<int list option>(Some [ 0 ], state.Editor.TriggerCueSelection)

    // A different trigger: the fields reset and re-point.
    do CueEditor.resetTriggerEditor &state second
    Assert.Equal<EntityRef option>(Some second, state.Editor.TriggerCueRef)
    Assert.Equal<int list option>(None, state.Editor.TriggerCueSelection)
    Assert.Equal<int list option>(None, state.Editor.TriggerCueTextPath)
    Assert.False state.Editor.TriggerCueEditing

[<Fact>]
let ``the trigger inline editor keeps its own selection and text buffer`` () =
    // FR-007: the two editors cannot share a selection or a buffer.
    let mutable state = newState ()
    Assert.False(System.Object.ReferenceEquals(state.Editor.CueTextBuffer, state.Editor.TriggerCueTextBuffer))

    state.Editor <- { state.Editor with CueSelection = Some [ 0 ]; TriggerCueSelection = Some [ 1 ] }
    Assert.Equal<int list option>(Some [ 0 ], state.Editor.CueSelection)
    Assert.Equal<int list option>(Some [ 1 ], state.Editor.TriggerCueSelection)
