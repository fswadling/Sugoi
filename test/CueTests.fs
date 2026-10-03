module CueTests

// The cue engine, asserted headlessly (spec 046): the three stories, the edge cases, the two allocation
// properties (FR-012) and the two bounded-shape properties (FR-011).
//
// A cue's *walk* is asserted on the value itself (`advance`); its *effects* against a `GameplayState`
// built the way `GameplayStateTests` builds one — `Unchecked.defaultof` for the GPU pieces, a device-free
// asset manager. Nothing here opens a window, a GPU or a device.

open Xunit
open System
open System.Collections.Generic
open System.Numerics
open Prime
open Cue
open CueEngine

// ── Headless fixtures ──

let private sharedState () : SharedState.SharedState =
    { Gpu = Unchecked.defaultof<Gpu.Gpu>
      Assets = TestSupport.manager IntPtr.Zero (Dictionary()) (Dictionary()) (Dictionary())
      Cast = { Casting.Bindings = Dictionary() }
      ZoneRegistry = ZoneRegistry.create ()
      Physics = Physics.create () }

let private newState () : GameplayState.GameplayState =
    GameplayState.create (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<Ui.Ui>)

// One physics world for the file: the zones these tests build are empty, so a switch only walks an empty
// body map and nothing leaks between tests. The test runner serializes collections anyway.
let private shared = sharedState ()

let private withCue (cue: Cue) : GameplayState.GameplayState =
    let mutable state = newState ()
    state.Cue <- cue
    state

/// Authoring sugar: a list of ops as the cue they denote.
let private seqOf (cues: Cue list) : Cue =
    match cues with
    | [] -> Fin
    | [ only ] -> only
    | head :: tail -> Sequence(head, FDeque.ofList tail)

/// The cue as a person (or the editor's panel) reads it. The in-flight `WaitState` renders as the `Wait`
/// it came from: the view is what is left to run, and its front is the running op.
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

/// The nodes actually in flight. The queue behind a sequence's current op is data, not live nodes, so a
/// long cue costs no more than a short one.
let rec private liveCount (cue: Cue) : int =
    match cue with
    | Fin -> 0
    | Sequence (current, _) -> 1 + liveCount current
    | Parallel (l, r) -> 1 + liveCount l + liveCount r
    | Fork c -> 1 + liveCount c
    | _ -> 1

// ══════ Story 1 — a cue runs as written, and shrinks to Fin ══════

[<Fact>]
let ``a sequence runs each op at the step the cue asks for`` () =
    let mutable state = withCue (seqOf [ Wait 2.0; Exposit "hello"; Wait 1.0 ])

    tick shared &state 0.0
    tick shared &state 1.0
    Assert.False state.Dialogue.Shown
    Assert.Equal("Seq[Wait 2, Exposit(hello), Wait 1]", describe state.Cue)

    // t=2: the wait ends, the instant op runs in the same step, the child after it begins
    tick shared &state 2.0
    Assert.True state.Dialogue.Shown
    Assert.Equal("hello", state.Dialogue.Text)
    Assert.Equal("Seq[Wait 1]", describe state.Cue)

    // t=3: over — not later, and not early
    tick shared &state 3.0
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``two branches run at once and the point is Fin only when both have`` () =
    let mutable state = withCue (Parallel(Wait 1.0, Wait 2.0))

    tick shared &state 0.0
    Assert.Equal("Par(Wait 1, Wait 2)", describe state.Cue)

    tick shared &state 1.0
    Assert.Equal("Wait 2", describe state.Cue) // the early branch is gone, the cue is not over

    tick shared &state 2.0
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``a cue of no-progress ops is Fin within one step`` () =
    let mutable state = withCue (seqOf [ Exposit "a"; Exposit "b" ])
    tick shared &state 0.0
    Assert.Equal("Fin", describe state.Cue)
    Assert.Equal("b", state.Dialogue.Text)

[<Fact>]
let ``an authored Fin in a sequence is skipped rather than stopping it`` () =
    // `Fin` means "nothing to run", so a `Fin` written into a queue is a *hole* the walk carries past in the
    // same step — it does not terminate the sequence. (The type system cannot say "terminal, and last", so
    // this is a rule about the walk rather than a rule the type enforces: FR-017.)
    let mutable state = withCue (seqOf [ Fin; Exposit "x"; Fin; Wait 0.0 ])
    tick shared &state 0.0 // one tick consumes the whole run: the holes are skipped, the wait is over at once
    Assert.True state.Dialogue.Shown
    Assert.Equal("x", state.Dialogue.Text)
    Assert.Equal("Fin", describe state.Cue)

    // and a queue whose front is `Fin` pulls the op behind it in the same step
    let mutable state2 = withCue (Sequence(Wait 1.0, FDeque.ofList [ Fin; Exposit "after" ]))
    tick shared &state2 0.0
    Assert.Equal("Seq[Wait 1, Fin, Exposit(after)]", describe state2.Cue) // the wait is in flight, the hole waits behind it
    Assert.False state2.Dialogue.Shown
    tick shared &state2 1.0
    Assert.Equal("after", state2.Dialogue.Text) // the `Fin` was skipped, not blocked on
    Assert.Equal("Fin", describe state2.Cue)

[<Fact>]
let ``Fin stays Fin`` () =
    let mutable state = withCue (seqOf [ Wait 1.0 ])
    tick shared &state 0.0
    tick shared &state 1.0
    Assert.Equal("Fin", describe state.Cue)

    tick shared &state 5.0
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``the same authored value can be run again from the start`` () =
    let cue = seqOf [ Wait 2.0; Exposit "again"; Wait 1.0 ]

    let run () =
        let mutable state = withCue cue
        let labels = ResizeArray<string>()
        for t in 0.0 .. 3.0 do
            tick shared &state t
            labels.Add(describe state.Cue)
        labels, state.Dialogue.Text

    let (labels1, text1) = run ()
    let (labels2, text2) = run ()
    Assert.Equal<string list>(List.ofSeq labels1, List.ofSeq labels2)
    Assert.Equal("again", text1)
    Assert.Equal(text1, text2)

[<Fact>]
let ``two runs of the same cue against the same clock agree after every step`` () =
    let cue = seqOf [ Wait 2.0; Exposit "hi"; Wait 1.0; SwitchZone "main" ]
    let mutable a = withCue cue
    let mutable b = withCue cue

    for t in 0.0 .. 0.5 .. 4.0 do
        tick shared &a t
        tick shared &b t
        Assert.Equal(describe a.Cue, describe b.Cue)
        Assert.Equal(a.Dialogue.Text, b.Dialogue.Text)
        Assert.Equal(a.ActiveZone, b.ActiveZone)

[<Fact>]
let ``a camera move interpolates and lands on its target`` () =
    let target = { Position = Vector3(10.0f, 10.0f, 10.0f); Target = Vector3(1.0f, 1.0f, 1.0f) }
    let mutable state = withCue (seqOf [ MoveCamera(target, 2.0) ])
    let from = state.Camera.Position

    tick shared &state 0.0
    Assert.Equal(from, state.Camera.Position) // progress 0: still at the start

    tick shared &state 1.0
    Assert.True(state.Camera.Position.X > from.X && state.Camera.Position.X < target.Position.X)

    tick shared &state 2.0
    Assert.Equal(target.Position, state.Camera.Position)
    Assert.Equal(target.Target, state.Camera.Target)
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``a camera move of zero duration is over before it begins`` () =
    let target = { Position = Vector3(4.0f, 5.0f, 6.0f); Target = Vector3(1.0f, 1.0f, 1.0f) }
    let mutable state = withCue (seqOf [ MoveCamera(target, 0.0) ])
    tick shared &state 0.0
    Assert.Equal("Fin", describe state.Cue)
    Assert.Equal(target.Position, state.Camera.Position)

// ══════ Story 2 — a cue can be forked ══════

[<Fact>]
let ``a fork detaches beside the sequence and both run`` () =
    let mutable state = withCue (seqOf [ Wait 1.0; Fork(Wait 2.0); Wait 3.0 ])

    tick shared &state 0.0
    tick shared &state 1.0
    Assert.Equal("Par(Seq[Wait 3], Wait 2)", describe state.Cue) // remainder left, branch right

    tick shared &state 3.0
    Assert.Equal("Seq[Wait 3]", describe state.Cue) // the branch is done, the remainder still runs

    tick shared &state 6.0
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``a cue forked in runs beside the running one without disturbing it`` () =
    let mutable state = withCue (seqOf [ Wait 5.0; Exposit "queued" ])
    tick shared &state 0.0

    state.Cue <- forkInto (Wait 1.0) state.Cue
    Assert.Equal("Par(Seq[Wait 5, Exposit(queued)], Wait 1)", describe state.Cue)

    tick shared &state 1.0
    Assert.Equal("Par(Seq[Wait 5, Exposit(queued)], Wait 1)", describe state.Cue) // the fork has begun

    tick shared &state 2.0
    Assert.Equal("Seq[Wait 5, Exposit(queued)]", describe state.Cue) // the fork is done

    // and the cue that was running kept its deadline: still waiting at t=4, done at t=5
    tick shared &state 4.0
    Assert.Equal("Seq[Wait 5, Exposit(queued)]", describe state.Cue)
    tick shared &state 5.0
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``forking into Fin makes the arriving cue the cue`` () =
    Assert.Equal("Exposit(arrived)", describe (forkInto (Exposit "arrived") Fin))

[<Fact>]
let ``a forked cue carries no knowledge of its host`` () =
    let joined = Exposit "j"
    Assert.Equal("Par(Wait 5, Exposit(j))", describe (forkInto joined (Wait 5.0)))
    Assert.Equal("Par(Wait 6, Exposit(j))", describe (forkInto joined (Wait 6.0)))

[<Fact>]
let ``past forks do not accumulate in the cue`` () =
    let mutable state = withCue (seqOf [ Wait 100.0; Exposit "done" ])
    tick shared &state 0.0
    let running = describe state.Cue

    for _ in 1 .. 1000 do
        state.Cue <- forkInto (Exposit "x") state.Cue
        tick shared &state 0.5

    // every fork did its work and dropped out; the cue is exactly what it was, and nothing piled up
    Assert.Equal(running, describe state.Cue)
    Assert.Equal(2, liveCount state.Cue)

[<Fact>]
let ``a Fork outside a sequence raises`` () =
    let mutable state = withCue (Fork(Wait 1.0))
    let mutable raised = false
    try tick shared &state 0.0 with _ -> raised <- true
    Assert.True raised

// ══════ Story 3 — the cue is the screen's, frozen while editing ══════

[<Fact>]
let ``a switch happens at its place and everything after it still runs`` () =
    let mutable state = withCue (seqOf [ Wait 1.0; SwitchZone "classroom"; Wait 1.0 ])
    GameplayState.addZone shared &state "classroom"
    GameplayState.switchZone shared &state "main"
    let mutable state = state

    tick shared &state 0.0
    Assert.Equal("main", state.ActiveZone)

    tick shared &state 1.0
    Assert.Equal("classroom", state.ActiveZone)
    Assert.Equal("Seq[Wait 1]", describe state.Cue) // nothing dropped

    tick shared &state 2.0
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``a detached branch's switch while the sequence waits leaves both running`` () =
    let mutable state = withCue (seqOf [ Fork(SwitchZone "classroom"); Wait 5.0 ])
    GameplayState.addZone shared &state "classroom"
    GameplayState.switchZone shared &state "main"
    let mutable state = state

    tick shared &state 0.0
    Assert.Equal("classroom", state.ActiveZone) // the branch switched
    Assert.Equal("Seq[Wait 5]", describe state.Cue) // the sequence keeps waiting

[<Fact>]
let ``a switch to an unknown zone raises`` () =
    let mutable state = withCue (seqOf [ SwitchZone "nope" ])
    let mutable raised = false
    try tick shared &state 0.0 with _ -> raised <- true
    Assert.True raised

[<Fact>]
let ``the cue resumes rather than restarts across an edit`` () =
    let mutable state = withCue (seqOf [ Wait 10.0 ])
    tick shared &state 0.0

    // "editing" does not advance anything: `PlayTime` is what the play chain advances, and it is frozen.
    // On Play it resumes with the deadline it had.
    tick shared &state 4.0
    Assert.Equal("Wait 10", describe state.Cue)
    tick shared &state 10.0
    Assert.Equal("Fin", describe state.Cue)

// ══════ FR-011 — bounded footprint ══════

[<Fact>]
let ``peak live nodes do not grow with the cue's length`` () =
    // A cue of `count` waits, driven a step at a time to completion: the peak number of live nodes.
    let peakOver (count: int) : int =
        let mutable state = withCue (seqOf (List.replicate count (Wait 5.0)))
        let mutable peak = 0
        for i in 1 .. count + 2 do
            tick shared &state (float i * 5.0)
            peak <- max peak (liveCount state.Cue)
        peak

    let short = peakOver 4
    Assert.Equal(short, peakOver 40) // a long cue costs no more than a short one
    Assert.Equal(short, peakOver 10000) // ... and ten thousand steps no more than the longest op needs

[<Fact>]
let ``200,000 no-progress ops in a row do not grow the stack`` () =
    let mutable state = withCue (seqOf (List.replicate 200000 (Exposit "x")))
    tick shared &state 0.0 // one tick must consume the whole run
    Assert.Equal("Fin", describe state.Cue)

// ══════ FR-012 — allocation ══════

[<Fact>]
let ``an idle tick allocates nothing, flat and several sequences deep, and Fin costs nothing`` () =
    let mutable flat = withCue (seqOf [ Wait 100.0; Exposit "later" ])
    let mutable deep = withCue (seqOf [ seqOf [ seqOf [ Wait 100.0 ] ]; Exposit "later" ])
    let mutable fin = withCue Fin

    for _ in 1 .. 50 do
        tick shared &flat 1.0
        tick shared &deep 1.0
        tick shared &fin 1.0

    let beforeFlat = GC.GetAllocatedBytesForCurrentThread()
    for _ in 1 .. 2000 do tick shared &flat 1.0
    let flatBytes = GC.GetAllocatedBytesForCurrentThread() - beforeFlat

    let beforeDeep = GC.GetAllocatedBytesForCurrentThread()
    for _ in 1 .. 2000 do tick shared &deep 1.0
    let deepBytes = GC.GetAllocatedBytesForCurrentThread() - beforeDeep

    let beforeFin = GC.GetAllocatedBytesForCurrentThread()
    for _ in 1 .. 2000 do tick shared &fin 1.0
    let finBytes = GC.GetAllocatedBytesForCurrentThread() - beforeFin

    Assert.Equal(0L, flatBytes)
    Assert.Equal(0L, deepBytes)
    Assert.Equal(0L, finBytes)

[<Fact>]
let ``a tick that completes an op allocates a bounded amount that does not grow with the cue`` () =
    let perOp (count: int) : int64 =
        let mutable state = withCue (seqOf (List.replicate count (Wait 0.0)))
        let before = GC.GetAllocatedBytesForCurrentThread()
        for _ in 1 .. count do tick shared &state 0.0
        let bytes = GC.GetAllocatedBytesForCurrentThread() - before
        Assert.Equal("Fin", describe state.Cue)
        bytes / int64 count

    let short = perOp 40
    let long = perOp 4000

    Assert.True(short > 0L, sprintf "expected a per-op allocation, measured %d" short)
    Assert.True(long < 1024L, sprintf "per-op allocation should be bounded, measured %d" long)
    Assert.True(long <= short * 2L, sprintf "per-op allocation should not grow with length: %d vs %d" short long)

// ══════ The reporting contract — what `advance`'s answer means ══════

[<Fact>]
let ``Fin reports itself, not nothing to change`` () =
    // `ValueSome Fin` and deliberately *not* `ValueNone`: the fixed point is a value the walk can hand on,
    // so a `Fin` hole at the front of a sequence is skipped rather than read as "still working, unmoved".
    let mutable state = newState ()
    match advance shared &state 0.0 Fin with
    | ValueSome c -> Assert.Equal("Fin", describe c)
    | ValueNone -> Assert.Fail "expected Fin to report itself, not nothing to change"

[<Fact>]
let ``an op still in flight reports nothing to change`` () =
    let mutable state = newState ()
    match advance shared &state 0.0 (WaitState(5.0, 10.0)) with // not due at t=0
    | ValueSome c -> Assert.Fail(sprintf "expected the wait to stay put, got %s" (describe c))
    | ValueNone -> ()

[<Fact>]
let ``a wait coming due reports Fin`` () =
    let mutable state = newState ()
    match advance shared &state 10.0 (WaitState(5.0, 10.0)) with
    | ValueSome c -> Assert.Equal("Fin", describe c)
    | ValueNone -> Assert.Fail "expected the due wait to report Fin"

[<Fact>]
let ``a sequence reports the moved-up queue even when its new front is in flight`` () =
    // The queue's front is data, so stepping *onto* a new front changes the sequence even though that op
    // itself reports nothing: the answer is the sequence with the new front, not `ValueNone`.
    let mutable state = newState ()
    let cue = Sequence(Exposit "x", FDeque.ofList [ WaitState(5.0, 10.0) ])
    match advance shared &state 0.0 cue with
    | ValueSome moved ->
        Assert.Equal("Seq[Wait 5]", describe moved) // the `Exposit` ran, the moved-up wait is the front
        Assert.True state.Dialogue.Shown
        Assert.Equal("x", state.Dialogue.Text)
    | ValueNone -> Assert.Fail "expected the moved-up sequence, not nothing to change"
