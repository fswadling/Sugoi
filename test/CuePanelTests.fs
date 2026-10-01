module CuePanelTests

// The cue panel's readable half, asserted headlessly (spec 047): the rows it derives, the rule that decides
// what is read-only, the paths that address a node, and the cue's own history. Nothing here opens a window, a
// GPU or a device — the panel's drawing is the walkthrough's business, and `FR-007`'s pure function is the
// whole reason what it *shows* can be asserted at all.
//
// Nothing compares two `Cue` values: a cue holds an `FDeque` and the engine deliberately answers "same value"
// by reference identity, so the suite reads cues through `describe` instead — the same view of a cue a person
// has.

open Xunit
open System.Numerics
open Prime
open Cue
open CueTree
open EditorState
open CueEditor

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
    | Exposit t -> sprintf "Exposit(%s)" t
    | SwitchZone z -> sprintf "Zone(%s)" z
    | MoveCamera _ | MoveCameraState _ -> "MoveCamera"

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
    let rs = rows (seqOf [ Wait 1.0; Exposit "hello"; Wait 2.0 ])
    Assert.Equal<string list>([ "Sequence"; "Wait 1"; "Exposit: hello"; "Wait 2" ], List.map (fun r -> r.Label) rs)
    Assert.Equal<int list>([ 0; 1; 1; 1 ], List.map (fun r -> r.Depth) rs)
    Assert.Equal<int list list>([ []; [ 0 ]; [ 1 ]; [ 2 ] ], List.map (fun r -> r.Path) rs)

[<Fact>]
let ``a leaf in flight is labelled as the op it came from`` () =
    // the cue the engine makes of `Wait 2` part-way through: the view is what is left to run, never an
    // internal state's name. The in-flight form and the authored form read the same, which is the point.
    let running = seqOf [ WaitState(2.0, 10.0); Exposit "later" ]
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
    let cue = seqOf [ WaitState(2.0, 10.0); Exposit "x" ]
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
    Assert.Equal("Sequence", labelOf (seqOf [ Wait 1.0; Exposit "a" ]))
    Assert.Equal("Parallel", labelOf (Parallel(Wait 1.0, Wait 2.0)))
    Assert.Equal("Fork", labelOf (Fork(Wait 1.0)))
    Assert.Equal("Wait 1", labelOf (Wait 1.0))
    Assert.Equal("Exposit: hello", labelOf (Exposit "hello"))
    Assert.Equal("Zone: classroom", labelOf (SwitchZone "classroom"))
    Assert.Equal("Move camera", labelOf (MoveCamera({ Position = Vector3.Zero; Target = Vector3.Zero }, 1.0)))
    // ... and the two in-flight forms read as the ops they came from
    Assert.Equal("Wait 2", labelOf (WaitState(2.0, 10.0)))
    Assert.Equal("Move camera", labelOf (MoveCameraState({ Position = Vector3.Zero; Target = Vector3.Zero }, { Position = Vector3.One; Target = Vector3.One }, 1.0, 5.0)))

[<Fact>]
let ``a sequence inside a sequence is indented one deeper again, and a container's sides are siblings`` () =
    let nested = rows (seqOf [ Wait 1.0; seqOf [ Exposit "a"; Wait 2.0 ] ])
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
    Assert.Equal<int list list>([], lockedPaths (seqOf [ Wait 1.0; Exposit "x"; Wait 2.0 ]))

[<Fact>]
let ``a cue part-way through a wait locks the running path and not the queue`` () =
    let running = seqOf [ WaitState(2.0, 10.0); Exposit "later"; Wait 3.0 ]
    // the sequence and the op in flight: the containers above it, from the root down
    Assert.Equal<int list list>([ []; [ 0 ] ], lockedPaths running)
    // and every op queued behind it is editable
    let editable = rows running |> List.filter (fun (r: Row) -> not r.Locked) |> List.map (fun r -> r.Path)
    Assert.Equal<int list list>([ [ 1 ]; [ 2 ] ], editable)

[<Fact>]
let ``a running parallel locks both branches`` () =
    // a parallel runs its sides at once, so neither is behind the other and neither is editable
    let running = Parallel(WaitState(1.0, 5.0), Sequence(WaitState(2.0, 9.0), FDeque.ofList [ Exposit "x" ]))
    Assert.Equal<int list list>([ []; [ 0 ]; [ 1 ]; [ 1; 0 ] ], lockedPaths running)

[<Fact>]
let ``a fork's inner branch is locked while the remainder behind it is not`` () =
    // a `Fork` is only meaningful as a sequence's front op: the branch runs beside the remainder, so it is
    // executing, while what is queued behind the fork is not
    let running = Sequence(Fork(WaitState(2.0, 10.0)), FDeque.ofList [ Exposit "after" ])
    Assert.Equal<int list list>([ []; [ 0 ]; [ 0; 0 ] ], lockedPaths running)
    Assert.False((rows running |> List.last).Locked)

[<Fact>]
let ``an addition is offered to a sequence whether or not it is running`` () =
    let running = seqOf [ WaitState(2.0, 10.0); Exposit "later" ]
    // a sequence that is executing still takes a child: its queue is ahead of the op in flight, so an
    // addition to it disturbs nothing (FR-011)
    Assert.True(canAppend [] running)
    // the op in flight takes nothing, and neither does the queued op (it is not a container)
    Assert.False(canAppend [ 0 ] running)
    Assert.False(canAppend [ 1 ] running)
    // a sequence that has not started is the plain authoring case, and takes one too (`seqOf` collapses a
    // single op, so the nested sequence needs two)
    let authored = seqOf [ Wait 1.0; seqOf [ Exposit "a"; Wait 2.0 ] ]
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
    let cue = seqOf [ Wait 1.0; seqOf [ Exposit "a"; Wait 2.0 ] ]
    Assert.Equal("Wait 1", describe (Option.defaultValue Fin (tryAt [ 0 ] cue)))
    Assert.Equal("Exposit(a)", describe (Option.defaultValue Fin (tryAt [ 1; 0 ] cue)))
    Assert.Equal("Seq[Wait 1, Seq[Exposit(a), Wait 2]]", describe (Option.defaultValue Fin (tryAt [] cue)))
    Assert.Equal(None, tryAt [ 1; 0; 0 ] cue)
    Assert.Equal(None, tryAt [ 9 ] cue)

[<Fact>]
let ``withAt replaces exactly the node a path names and leaves the rest alone`` () =
    let cue = seqOf [ Wait 1.0; Exposit "a"; Wait 2.0 ]
    Assert.Equal("Seq[Wait 1, Exposit(b), Wait 2]", describe (withAt [ 1 ] (Exposit "b") cue))
    // ... including a node inside a nested sequence
    let nested = seqOf [ Wait 1.0; seqOf [ Exposit "a"; Wait 2.0 ] ]
    Assert.Equal("Seq[Wait 1, Seq[Exposit(b), Wait 2]]", describe (withAt [ 1; 0 ] (Exposit "b") nested))

[<Fact>]
let ``withoutAt removes exactly the node a path names`` () =
    let cue = seqOf [ Wait 1.0; Exposit "a"; Wait 2.0 ]
    Assert.Equal("Seq[Wait 1, Wait 2]", describe (withoutAt [ 1 ] cue))
    // removing a branch leaves the other, the engine's own rule for a side that is finished
    Assert.Equal("Wait 1", describe (withoutAt [ 1 ] (Parallel(Wait 1.0, Wait 2.0))))
    // removing the queue's last entry leaves the front op alone: the same shape the engine makes when a
    // queue empties (`Sequence(current, empty)` is a value it produces, not a degenerate one)
    Assert.Equal("Seq[Wait 1]", describe (withoutAt [ 1 ] (Sequence(Wait 1.0, FDeque.ofList [ Exposit "a" ]))))

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
    let cue = seqOf [ Wait 1.0; Exposit "a" ]
    let node, childPath = withAppendedChild [] (Exposit "b") cue
    Assert.Equal<int list>([ 2 ], childPath)
    Assert.Equal("Seq[Wait 1, Exposit(a), Exposit(b)]", describe node)
    // the two agree: writing it back is what the panel commits, and the path is the child just added
    Assert.Equal("Seq[Wait 1, Exposit(a), Exposit(b)]", describe (withAt [] node cue))

[<Fact>]
let ``only the text-bearing kinds carry text, and writing it replaces exactly that`` () =
    Assert.Equal(ValueSome "hello", textOf (Exposit "hello"))
    Assert.Equal(ValueSome "classroom", textOf (SwitchZone "classroom"))
    Assert.Equal(ValueNone, textOf (Wait 1.0))
    Assert.Equal(ValueNone, textOf Fin)
    Assert.Equal("Exposit(hi)", describe (withText (Exposit "hello") "hi"))
    Assert.Equal("Zone(atrium)", describe (withText (SwitchZone "classroom") "atrium"))
    Assert.True(raises (fun () -> withText (Wait 1.0) "x" |> ignore))

// ══════ the cue's own history (FR-013, SC-008) ══════

[<Fact>]
let ``the cue's history takes an edit back and puts it forward again`` () =
    let before = seqOf [ Wait 1.0 ]
    let after = seqOf [ Wait 2.0 ]
    let pushed = CueHistory.push before (CueHistory.create ())

    let restored, undone = CueHistory.undo after pushed
    Assert.Equal(describe before, describe restored)

    let redone, redoneHistory = CueHistory.redo restored undone
    Assert.Equal(describe after, describe redone)
    Assert.True(List.isEmpty redoneHistory.Redo)
    Assert.Equal(1, List.length redoneHistory.Applied)

[<Fact>]
let ``nothing to take back or put forward is a no-op`` () =
    let empty = CueHistory.create ()
    let cue, history = CueHistory.undo (Wait 1.0) empty
    Assert.Equal("Wait 1", describe cue)
    Assert.True(List.isEmpty history.Applied)
    let cue', history' = CueHistory.redo cue empty
    Assert.Equal("Wait 1", describe cue')
    Assert.True(List.isEmpty history'.Redo)

[<Fact>]
let ``an accepted edit clears the redo side`` () =
    let one = CueHistory.push (Wait 1.0) (CueHistory.create ())
    let _, undone = CueHistory.undo (Wait 2.0) one
    Assert.False(List.isEmpty undone.Redo)
    // a new edit is a new branch: what was taken back is no longer reachable
    let two = CueHistory.push (Wait 3.0) undone
    Assert.True(List.isEmpty two.Redo)

// ══════ the panel's own write path (SC-007, SC-008) ══════

[<Fact>]
let ``the panel's write path changes the screen's cue once and undoes it exactly`` () =
    // The glue between the pieces: `commit` writes `state.Cue` and records one step, and `undo`/`redo` walk
    // that step. Asserting `withAt` and `CueHistory` separately is not the same claim (SC-007, SC-008).
    let mutable state = newState ()
    state.Cue <- seqOf [ WaitState(2.0, 10.0); Exposit "a"; Wait 3.0 ]
    let before = describe state.Cue
    let edited = withAt [ 1 ] (Exposit "b") state.Cue

    do CueEditor.commit &state edited
    Assert.Equal("Seq[Wait 2, Exposit(b), Wait 3]", describe state.Cue)
    Assert.Equal(1, List.length state.Editor.CueHistory.Applied) // one edit, exactly one step
    Assert.True(CueEditor.canUndo state)
    Assert.False(CueEditor.canRedo state)

    CueEditor.undo &state
    Assert.Equal(before, describe state.Cue)
    Assert.False(CueEditor.canUndo state)
    Assert.True(CueEditor.canRedo state)

    CueEditor.redo &state
    Assert.Equal("Seq[Wait 2, Exposit(b), Wait 3]", describe state.Cue)
    Assert.True(CueEditor.canUndo state)
    Assert.False(CueEditor.canRedo state)

[<Fact>]
let ``a fresh cue has nothing to take back, and an undo with nothing applied is a no-op`` () =
    let mutable state = newState ()
    state.Cue <- seqOf [ Wait 1.0 ]
    Assert.False(CueEditor.canUndo state)
    Assert.False(CueEditor.canRedo state)

    let before = describe state.Cue
    CueEditor.undo &state
    Assert.Equal(before, describe state.Cue)
    CueEditor.redo &state
    Assert.Equal(before, describe state.Cue)
