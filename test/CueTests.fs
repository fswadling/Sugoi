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
open Entity
open Advent
open Place

// The game's places, as these tests name them.
let private mainPlace : Place = Place.start
let private atriumPlace : Place = Place.ArtRoom Period.Evening
let private classroomPlace : Place = Place.Island

// ── Headless fixtures ──

let private sharedState () : SharedState.SharedState =
    { Gpu = Unchecked.defaultof<Gpu.Gpu>
      Assets = TestSupport.manager IntPtr.Zero (Dictionary()) (Dictionary()) (Dictionary())
      Cast = { Casting.Bindings = Dictionary() }
      ZoneRegistry = ZoneRegistry.create ()
      Advents = Set.empty
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

/// A waypoint named `name` at `position`, facing `rotation`, in the state's active zone.
let private placeWaypoint (state: GameplayState.GameplayState) (name: string) (position: Vector3) (rotation: Quaternion) : EntityRef =
    let zone = GameplayState.activeZone state
    let entity : Entity =
        { Name = name
          Transform = { transformIdentity with Position = position; Rotation = rotation }
          Enabled = true
          Visible = true
          Selectable = true
          Draggable = true }
    Zone.spawnWaypoint zone entity

// A shared state whose manager holds one **resident** environment asset, so a load or a request on it
// touches the LRU and never opens the async pool (no device, no threads), and whose registry can be given
// a template for a place. Enough to exercise the cue's add-and-stream and preload paths headlessly.
let private residentShared () : SharedState.SharedState =
    let environments = Dictionary<System.String, EnvironmentAsset.EnvironmentAsset>()
    let submesh = TestSupport.loadedSubmesh [| 0.0f; 0.0f; 0.0f;  1.0f; 0.0f; 0.0f;  0.0f; 1.0f; 0.0f |] [| 0; 1; 2 |]
    environments.["room"] <- TestSupport.residentAsset "room" [| submesh |] [| TestSupport.part "wall" 0 1 |]
    { Gpu = Unchecked.defaultof<Gpu.Gpu>
      Assets = TestSupport.manager IntPtr.Zero (Dictionary()) (Dictionary()) environments
      Cast = { Casting.Bindings = Dictionary() }
      ZoneRegistry = ZoneRegistry.create ()
      Advents = Set.empty
      Physics = Physics.create () }

// Register a template for `place`: the `"room"` environment, no light change and no parts or props —
// enough that a cue's add op streams something and `Zone.Environment` is present.
let private registerTemplate (shared: SharedState.SharedState) (place: Place) : unit =
    let baseEntity : Entity =
        { Name = "room"
          Transform = transformIdentity
          Enabled = true
          Visible = true
          Selectable = true
          Draggable = true }
    shared.ZoneRegistry.Templates.[place] <-
        { AssetId = "room"
          Base = baseEntity
          Orientation = Quaternion.Identity
          Light = Texture.defaultLight
          Parts = [||]
          Props = [||] }

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
    | FadeOut d | FadeOutState (_, d, _) -> sprintf "FadeOut %g" d
    | FadeIn d | FadeInState (_, d, _) -> sprintf "FadeIn %g" d
    | Exposit (t, _) -> sprintf "Exposit(%s)" t
    | Print t -> sprintf "Print(%s)" t
    | SetZone z -> sprintf "Zone(%s)" (Place.placeName z)
    | AddZone z -> sprintf "AddZone(%s)" (Place.placeName z)
    | RemoveZone z -> sprintf "RemoveZone(%s)" (Place.placeName z)
    | PreloadZone z -> sprintf "PreloadZone(%s)" (Place.placeName z)
    | SetActor (p, c, l, w) -> sprintf "SetActor(%s,%s,%s,%s)" (Place.placeName p) (Roster.characterName c) (Casting.lookName l) w
    | RemoveActor c -> sprintf "RemoveActor(%s)" (Roster.characterName c)
    | Animate (c, clip, _, d) -> sprintf "Animate(%s,%s,%g)" (Roster.characterName c) clip d
    | CrossFade (c, f, t, _, d) -> sprintf "CrossFade(%s,%s,%s,%g)" (Roster.characterName c) f t d
    | Morph (c, m, _, d) -> sprintf "Morph(%s,%s,%g)" (Roster.characterName c) m d
    | CrossMorph (c, f, t, _, d) -> sprintf "CrossMorph(%s,%s,%s,%g)" (Roster.characterName c) f t d
    | BlendState (c, morph, f, t, _, _, _, _, _) ->
        if f = t then sprintf "%s(%s,%s)" (if morph then "Morph" else "Animate") (Roster.characterName c) f
        else sprintf "%s(%s,%s,%s)" (if morph then "CrossMorph" else "CrossFade") (Roster.characterName c) f t
    | MoveActor (c, p, _, _, _) -> sprintf "MoveActor(%s,%s)" (Roster.characterName c) p
    | MoveActorState (c, _, _, _, _, _, _, _, _, _) -> sprintf "MoveActor(%s)" (Roster.characterName c)
    | RotateActor (c, w, d) -> sprintf "RotateActor(%s,%s,%g)" (Roster.characterName c) w d
    | RotateActorState (c, _, _, _, _) -> sprintf "RotateActor(%s)" (Roster.characterName c)
    | EnablePlayer c -> sprintf "EnablePlayer(%s)" (Roster.characterName c)
    | DisablePlayer c -> sprintf "DisablePlayer(%s)" (Roster.characterName c)
    | EnableTrigger (p, t) -> sprintf "EnableTrigger(%s,%s)" (Place.placeName p) t
    | DisableTrigger (p, t) -> sprintf "DisableTrigger(%s,%s)" (Place.placeName p) t
    | SetCamera w -> sprintf "SetCamera(%s)" w
    | FlyCamera (p, _) | FlyCameraState (p, _, _, _, _) -> sprintf "FlyCamera(%s)" p
    | AddAdvent m -> sprintf "Add(%s)" (Advent.name m)
    | RemoveAdvent m -> sprintf "Remove(%s)" (Advent.name m)
    | Await m | AwaitState m -> sprintf "Await(%s)" (Advent.name m)
    | If (m, t, e) -> sprintf "If(%s, %s, %s)" (Advent.name m) (describe t) (describe e)

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
    let mutable state = withCue (seqOf [ Wait 2.0; Exposit("hello", Dialogue.Kind.Dialogue); Wait 1.0 ])

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
    let mutable state = withCue (seqOf [ Exposit("a", Dialogue.Kind.Dialogue); Exposit("b", Dialogue.Kind.Dialogue) ])
    tick shared &state 0.0
    Assert.Equal("Fin", describe state.Cue)
    Assert.Equal("b", state.Dialogue.Text)

// ══════ Story 1 (spec 057) — an exposit presents a line, however it is spoken ══════

[<Fact>]
let ``an exposit writes the authored text and kind, for each presentation`` () =
    // a plain dialogue line
    let mutable plain = withCue (Exposit("hello", Dialogue.Kind.Dialogue))
    tick shared &plain 0.0
    Assert.True plain.Dialogue.Shown
    Assert.Equal(Dialogue.Kind.Dialogue, plain.Dialogue.Kind)
    Assert.Equal("hello", plain.Dialogue.Text)

    // a thought
    let mutable thought = withCue (Exposit("...", Dialogue.Kind.Thought))
    tick shared &thought 0.0
    Assert.Equal(Dialogue.Kind.Thought, thought.Dialogue.Kind)
    Assert.Equal("...", thought.Dialogue.Text)

    // a portrait line, carrying its speaker and expression
    let portrait = Dialogue.Kind.PortraitDialogue(Roster.Akane, Dialogue.Emotion.Happy)
    let mutable spoken = withCue (Exposit("You're late.", portrait))
    tick shared &spoken 0.0
    Assert.Equal(portrait, spoken.Dialogue.Kind)
    Assert.Equal("You're late.", spoken.Dialogue.Text)

[<Fact>]
let ``an exposit costs no time and a later one replaces it`` () =
    let second = Dialogue.Kind.PortraitDialogue(Roster.Yuki, Dialogue.Emotion.Sad)
    let mutable state = withCue (seqOf [ Exposit("first", Dialogue.Kind.Dialogue); Exposit("second", second) ])
    tick shared &state 0.0
    Assert.Equal("Fin", describe state.Cue) // both ran in the one step
    Assert.Equal("second", state.Dialogue.Text)
    Assert.Equal(second, state.Dialogue.Kind)

// ══════ Story 2 (spec 057) — an unresolved portrait is an expected miss ══════

[<Fact>]
let ``a portrait line whose speaker is unbound is written and does not block`` () =
    // `shared` binds no cast member and loads no asset, so the portrait cannot resolve: the view will leave
    // it undrawn and the panel will explain (`specs/038`). The op itself still writes the line and the run
    // continues — authoring a line before its art is bound must not break a cue.
    let portrait = Dialogue.Kind.PortraitDialogue(Roster.Akane, Dialogue.Emotion.Neutral)
    let mutable state = withCue (seqOf [ Exposit("...", portrait); Wait 1.0 ])
    tick shared &state 0.0
    Assert.Equal(portrait, state.Dialogue.Kind) // the op succeeded
    Assert.Equal("Seq[Wait 1]", describe state.Cue) // and the cue advanced past it

[<Fact>]
let ``an authored Fin in a sequence is skipped rather than stopping it`` () =
    // `Fin` means "nothing to run", so a `Fin` written into a queue is a *hole* the walk carries past in the
    // same step — it does not terminate the sequence. (The type system cannot say "terminal, and last", so
    // this is a rule about the walk rather than a rule the type enforces: FR-017.)
    let mutable state = withCue (seqOf [ Fin; Exposit("x", Dialogue.Kind.Dialogue); Fin; Wait 0.0 ])
    tick shared &state 0.0 // one tick consumes the whole run: the holes are skipped, the wait is over at once
    Assert.True state.Dialogue.Shown
    Assert.Equal("x", state.Dialogue.Text)
    Assert.Equal("Fin", describe state.Cue)

    // and a queue whose front is `Fin` pulls the op behind it in the same step
    let mutable state2 = withCue (Sequence(Wait 1.0, FDeque.ofList [ Fin; Exposit("after", Dialogue.Kind.Dialogue) ]))
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
    let cue = seqOf [ Wait 2.0; Exposit("again", Dialogue.Kind.Dialogue); Wait 1.0 ]

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
    let cue = seqOf [ Wait 2.0; Exposit("hi", Dialogue.Kind.Dialogue); Wait 1.0; SetZone mainPlace ]
    let mutable a = withCue cue
    let mutable b = withCue cue

    for t in 0.0 .. 0.5 .. 4.0 do
        tick shared &a t
        tick shared &b t
        Assert.Equal(describe a.Cue, describe b.Cue)
        Assert.Equal(a.Dialogue.Text, b.Dialogue.Text)
        Assert.Equal(a.ActiveZone, b.ActiveZone)

// ══════ Story 1 (spec 054) — the camera snaps from a named waypoint ══════

[<Fact>]
let ``a set camera op frames from its waypoint and costs no time`` () =
    let mutable state = newState ()
    // the waypoint's arrow faces +X: `-UnitZ` rotated -90° about +Y (the Viewport arrow convention)
    let rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2.0f)
    do ignore (placeWaypoint state "balcony" (Vector3(4.0f, 5.0f, 6.0f)) rotation)
    state.Cue <- seqOf [ SetCamera "balcony"; Exposit("after", Dialogue.Kind.Dialogue) ]

    tick shared &state 0.0

    Assert.Equal(Vector3(4.0f, 5.0f, 6.0f), state.Camera.Position)
    // the camera looks the way the arrow points, not the other way (`-UnitZ`, not `UnitZ`)
    let arrow = Vector3.Transform(-Vector3.UnitZ, rotation)
    let expected = Vector3(4.0f, 5.0f, 6.0f) + arrow * 3.5f
    Assert.True((state.Camera.Target - expected).Length() < 0.001f, sprintf "look target: expected %A, got %A" expected state.Camera.Target)
    Assert.True state.Dialogue.Shown // the op costs no time: the next op ran in the same step
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``a set camera op with no such waypoint raises`` () =
    let mutable state = newState ()
    let raises (cue: Cue) =
        state.Cue <- cue
        let mutable raised = false
        try tick shared &state 0.0 with _ -> raised <- true
        raised
    Assert.True(raises (SetCamera "nope"))
    Assert.True(raises (SetCamera ""))

[<Fact>]
let ``a set camera op resolves names only in the active zone`` () =
    let mutable state = newState ()
    GameplayState.addZone shared &state atriumPlace // atrium is now active
    do ignore (placeWaypoint state "arch" Vector3.Zero Quaternion.Identity)
    GameplayState.switchZone shared &state mainPlace

    state.Cue <- SetCamera "arch"
    let mutable raised = false
    try tick shared &state 0.0 with _ -> raised <- true
    Assert.True raised

// ══════ Story 2 (spec 054) — the camera flies along a named chain ══════

[<Fact>]
let ``a fly camera op travels its chain and lands on the last waypoint`` () =
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    let a = placeWaypoint state "a" (Vector3(0.0f, 0.0f, 0.0f)) Quaternion.Identity
    let b = placeWaypoint state "b" (Vector3(3.0f, 0.0f, 0.0f)) Quaternion.Identity
    let c = placeWaypoint state "c" (Vector3(6.0f, 0.0f, 0.0f)) Quaternion.Identity
    do Zone.setWaypointNext zone a (Some b)
    do Zone.setWaypointNext zone b (Some c)

    state.Cue <- FlyCamera("a", 1.0) // collinear 0..6 at speed 1: duration 6

    tick shared &state 0.0
    Assert.Equal(Vector3(0.0f, 0.0f, 0.0f), state.Camera.Position) // cut to the path start

    tick shared &state 3.0
    Assert.True(abs (state.Camera.Position.X - 3.0f) < 0.1f, sprintf "expected mid-path, got %A" state.Camera.Position)

    tick shared &state 6.0
    Assert.Equal(Vector3(6.0f, 0.0f, 0.0f), state.Camera.Position)
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``a flight is pinned to the path it resolved`` () =
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    let a = placeWaypoint state "a" (Vector3(0.0f, 0.0f, 0.0f)) Quaternion.Identity
    let b = placeWaypoint state "b" (Vector3(4.0f, 0.0f, 0.0f)) Quaternion.Identity
    do Zone.setWaypointNext zone a (Some b)

    state.Cue <- FlyCamera("a", 1.0)
    tick shared &state 0.0
    do Zone.removeWaypoint zone b // the end waypoint goes while the camera flies
    tick shared &state 5.0
    Assert.Equal(Vector3(4.0f, 0.0f, 0.0f), state.Camera.Position)
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``a fly camera op with a bad path or speed raises`` () =
    let mutable state = newState ()
    do ignore (placeWaypoint state "solo" Vector3.Zero Quaternion.Identity)
    let raises (cue: Cue) =
        state.Cue <- cue
        let mutable raised = false
        try tick shared &state 0.0 with _ -> raised <- true
        raised
    Assert.True(raises (FlyCamera("nope", 1.0)))    // no such waypoint
    Assert.True(raises (FlyCamera("solo", 1.0)))    // a chain of one
    Assert.True(raises (FlyCamera("solo", 0.0)))    // speed is not positive
    Assert.True(raises (FlyCamera("solo", -1.0)))

[<Fact>]
let ``a cyclic chain terminates`` () =
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    let a = placeWaypoint state "a" (Vector3(0.0f, 0.0f, 0.0f)) Quaternion.Identity
    let b = placeWaypoint state "b" (Vector3(2.0f, 0.0f, 0.0f)) Quaternion.Identity
    do Zone.setWaypointNext zone a (Some b)
    do Zone.setWaypointNext zone b (Some a) // a -> b -> a

    state.Cue <- FlyCamera("a", 1.0)
    tick shared &state 0.0
    tick shared &state 100.0
    Assert.True(state.Cue.IsFin, "a cyclic chain must terminate rather than loop")

[<Fact>]
let ``a flight in progress allocates nothing per tick`` () =
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    let a = placeWaypoint state "a" (Vector3(0.0f, 0.0f, 0.0f)) Quaternion.Identity
    let b = placeWaypoint state "b" (Vector3(1000.0f, 0.0f, 0.0f)) Quaternion.Identity
    do Zone.setWaypointNext zone a (Some b)
    state.Cue <- FlyCamera("a", 1.0)

    tick shared &state 0.0
    for _ in 1 .. 50 do tick shared &state 0.5

    let before = GC.GetAllocatedBytesForCurrentThread()
    for _ in 1 .. 2000 do tick shared &state 0.5
    let bytes = GC.GetAllocatedBytesForCurrentThread() - before
    Assert.Equal(0L, bytes)

// ── the fade ops (spec 060): the level over a timeline, the part-way start, the exact endpoint ──

[<Fact>]
let ``a fade out darkens to black over its duration and a fade in clears it`` () =
    let mutable state = newState ()
    state.Fade <- 0.0f
    state.Cue <- FadeOut 2.0
    tick shared &state 0.0
    Assert.Equal(0.0f, state.Fade) // the begin step holds the captured level
    tick shared &state 1.0
    Assert.Equal(0.5f, state.Fade)
    tick shared &state 2.0
    Assert.Equal(1.0f, state.Fade) // exactly the endpoint
    Assert.Equal("Fin", describe state.Cue)

    state.Cue <- FadeIn 2.0
    tick shared &state 0.0
    Assert.Equal(1.0f, state.Fade)
    tick shared &state 1.0
    Assert.Equal(0.5f, state.Fade)
    tick shared &state 2.0
    Assert.Equal(0.0f, state.Fade)
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``a fade starts from the level it finds, not an endpoint`` () =
    let mutable state = newState ()
    state.Fade <- 0.4f
    state.Cue <- FadeOut 2.0
    tick shared &state 0.0
    tick shared &state 1.0
    Assert.True(abs (state.Fade - 0.7f) < 0.0001f) // 0.4 + 0.5 * (1 - 0.4)
    tick shared &state 2.0
    Assert.Equal(1.0f, state.Fade)

    state.Fade <- 0.4f
    state.Cue <- FadeIn 2.0
    tick shared &state 0.0
    tick shared &state 1.0
    Assert.True(abs (state.Fade - 0.2f) < 0.0001f) // 0.4 + 0.5 * (0 - 0.4)
    tick shared &state 2.0
    Assert.Equal(0.0f, state.Fade)

[<Fact>]
let ``a fade of zero or less is over before it begins`` () =
    let mutable state = newState ()
    state.Fade <- 0.3f
    state.Cue <- FadeOut 0.0
    tick shared &state 5.0
    Assert.Equal(1.0f, state.Fade)
    Assert.Equal("Fin", describe state.Cue)

    state.Fade <- 0.3f
    state.Cue <- FadeIn -1.0
    tick shared &state 5.0
    Assert.Equal(0.0f, state.Fade)
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``a fade in flight allocates nothing per tick`` () =
    let mutable state = newState ()
    state.Fade <- 0.0f
    state.Cue <- FadeOut 1000.0
    tick shared &state 0.0
    for _ in 1 .. 50 do tick shared &state 0.5

    let before = GC.GetAllocatedBytesForCurrentThread()
    for _ in 1 .. 2000 do tick shared &state 0.5
    let bytes = GC.GetAllocatedBytesForCurrentThread() - before
    Assert.Equal(0L, bytes)

// ══════ The advent ops (spec 056) — milestones drive a cue ══════

[<Fact>]
let ``an add advent op sets the flag and costs no time`` () =
    shared.Advents <- Set.empty
    let mutable state = newState ()
    state.Cue <- seqOf [ AddAdvent MetAiko; Exposit("after", Dialogue.Kind.Dialogue) ]
    tick shared &state 0.0
    Assert.True(Set.contains MetAiko shared.Advents)
    Assert.True state.Dialogue.Shown // the next op ran in the same step
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``a remove advent op clears the flag, and both writes are idempotent`` () =
    shared.Advents <- Set.ofList [ MetAiko ]
    let mutable state = newState ()
    state.Cue <- seqOf [ RemoveAdvent MetAiko; AddAdvent MetAkane ]
    tick shared &state 0.0
    Assert.False(Set.contains MetAiko shared.Advents)
    Assert.True(Set.contains MetAkane shared.Advents)

    // adding a set flag and removing a clear one are no-ops, not errors
    state.Cue <- seqOf [ AddAdvent MetAkane; RemoveAdvent MetAiko ]
    tick shared &state 1.0
    Assert.True(Set.contains MetAkane shared.Advents)
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``an await op waits until its flag is set, then continues`` () =
    shared.Advents <- Set.empty
    let mutable state = newState ()
    state.Cue <- seqOf [ Await MetAkane; Exposit("after", Dialogue.Kind.Dialogue) ]
    tick shared &state 0.0
    Assert.Equal("Seq[Await(MetAkane), Exposit(after)]", describe state.Cue)
    Assert.False state.Dialogue.Shown

    shared.Advents <- Set.add MetAkane shared.Advents
    tick shared &state 1.0
    Assert.True state.Dialogue.Shown
    Assert.Equal("after", state.Dialogue.Text)
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``an await of an already-set flag costs no time`` () =
    shared.Advents <- Set.ofList [ MetAiko ]
    let mutable state = newState ()
    state.Cue <- seqOf [ Await MetAiko; Exposit("after", Dialogue.Kind.Dialogue) ]
    tick shared &state 0.0
    Assert.True state.Dialogue.Shown
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``an add earlier in the same cue satisfies a later await in the same step`` () =
    shared.Advents <- Set.empty
    let mutable state = newState ()
    state.Cue <- seqOf [ AddAdvent MetAiko; Await MetAiko; Exposit("after", Dialogue.Kind.Dialogue) ]
    tick shared &state 0.0
    Assert.True state.Dialogue.Shown
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``clearing the flag while waiting leaves the await waiting`` () =
    shared.Advents <- Set.empty
    let mutable state = newState ()
    state.Cue <- Await MetAiko
    tick shared &state 0.0
    Assert.Equal("Await(MetAiko)", describe state.Cue)

    // set then clear between steps: membership is re-read, clearing is not remembered
    shared.Advents <- Set.add MetAiko shared.Advents
    shared.Advents <- Set.remove MetAiko shared.Advents
    tick shared &state 1.0
    Assert.Equal("Await(MetAiko)", describe state.Cue)

[<Fact>]
let ``a waiting await allocates nothing per step`` () =
    shared.Advents <- Set.empty
    let mutable state = newState ()
    state.Cue <- Await MetAiko
    tick shared &state 0.0
    for _ in 1 .. 50 do tick shared &state 1.0

    let before = GC.GetAllocatedBytesForCurrentThread()
    for _ in 1 .. 2000 do tick shared &state 1.0
    let bytes = GC.GetAllocatedBytesForCurrentThread() - before
    Assert.Equal(0L, bytes)

[<Fact>]
let ``an if runs the branch its flag selects`` () =
    shared.Advents <- Set.empty
    let mutable state = newState ()
    state.Cue <- If(MetAiko, Exposit("then", Dialogue.Kind.Dialogue), Exposit("else", Dialogue.Kind.Dialogue))
    tick shared &state 0.0
    Assert.Equal("else", state.Dialogue.Text) // flag clear: the else branch
    Assert.Equal("Fin", describe state.Cue)

    shared.Advents <- Set.add MetAiko shared.Advents
    state.Cue <- If(MetAiko, Exposit("then", Dialogue.Kind.Dialogue), Exposit("else", Dialogue.Kind.Dialogue))
    tick shared &state 1.0
    Assert.Equal("then", state.Dialogue.Text)

[<Fact>]
let ``an if's Fin branch costs no time and a timed branch begins in its place`` () =
    shared.Advents <- Set.ofList [ MetAiko ]
    let mutable state = newState ()
    state.Cue <- seqOf [ If(MetAiko, Fin, Exposit("nefarious", Dialogue.Kind.Dialogue)); Wait 1.0 ]
    tick shared &state 0.0
    Assert.Equal("Seq[Wait 1]", describe state.Cue) // the `Fin` branch was over at once; the wait is the run
    Assert.False state.Dialogue.Shown // the untaken branch never ran

    let mutable state2 = newState ()
    state2.Cue <- If(MetAiko, Wait 2.0, Fin)
    tick shared &state2 0.0
    Assert.Equal("Wait 2", describe state2.Cue)
    tick shared &state2 2.0
    Assert.Equal("Fin", describe state2.Cue)

[<Fact>]
let ``an if nests inside a sequence and the untaken branch never runs`` () =
    shared.Advents <- Set.ofList [ MetAiko ]
    let mutable state = newState ()
    state.Cue <- seqOf [ If(MetAiko, Exposit("then", Dialogue.Kind.Dialogue), Exposit("nefarious", Dialogue.Kind.Dialogue)); Exposit("after", Dialogue.Kind.Dialogue) ]
    tick shared &state 0.0
    Assert.Equal("after", state.Dialogue.Text)
    Assert.Equal("Fin", describe state.Cue)

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
    let mutable state = withCue (seqOf [ Wait 5.0; Exposit("queued", Dialogue.Kind.Dialogue) ])
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
    Assert.Equal("Exposit(arrived)", describe (forkInto (Exposit("arrived", Dialogue.Kind.Dialogue)) Fin))

[<Fact>]
let ``a forked cue carries no knowledge of its host`` () =
    let joined = Exposit("j", Dialogue.Kind.Dialogue)
    Assert.Equal("Par(Wait 5, Exposit(j))", describe (forkInto joined (Wait 5.0)))
    Assert.Equal("Par(Wait 6, Exposit(j))", describe (forkInto joined (Wait 6.0)))

[<Fact>]
let ``past forks do not accumulate in the cue`` () =
    let mutable state = withCue (seqOf [ Wait 100.0; Exposit("done", Dialogue.Kind.Dialogue) ])
    tick shared &state 0.0
    let running = describe state.Cue

    for _ in 1 .. 1000 do
        state.Cue <- forkInto (Exposit("x", Dialogue.Kind.Dialogue)) state.Cue
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
    let mutable state = withCue (seqOf [ Wait 1.0; SetZone classroomPlace; Wait 1.0 ])
    GameplayState.addZone shared &state classroomPlace
    GameplayState.switchZone shared &state mainPlace
    let mutable state = state

    tick shared &state 0.0
    Assert.Equal(mainPlace, state.ActiveZone)

    tick shared &state 1.0
    Assert.Equal(classroomPlace, state.ActiveZone)
    Assert.Equal("Seq[Wait 1]", describe state.Cue) // nothing dropped

    tick shared &state 2.0
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``a detached branch's switch while the sequence waits leaves both running`` () =
    let mutable state = withCue (seqOf [ Fork(SetZone classroomPlace); Wait 5.0 ])
    GameplayState.addZone shared &state classroomPlace
    GameplayState.switchZone shared &state mainPlace
    let mutable state = state

    tick shared &state 0.0
    Assert.Equal(classroomPlace, state.ActiveZone) // the branch switched
    Assert.Equal("Seq[Wait 5]", describe state.Cue) // the sequence keeps waiting

[<Fact>]
let ``a switch to an absent place performs nothing`` () =
    // spec 062 FR-008: the cue no-op rule 061 deferred — a cue op on a place that is not present does not throw.
    let mutable state = withCue (seqOf [ SetZone atriumPlace ])
    tick shared &state 0.0
    Assert.Equal("Fin", describe state.Cue)
    Assert.Equal(mainPlace, state.ActiveZone)
    Assert.False(state.Zones.ContainsKey atriumPlace)

// ══════ Zone cue ops (spec 062) ══════

[<Fact>]
let ``an add op makes an absent place present with its registered template, without entering it`` () =
    let shared = residentShared ()
    registerTemplate shared atriumPlace
    let mutable state = withCue (seqOf [ AddZone atriumPlace ])
    let activeBefore = state.ActiveZone

    tick shared &state 0.0

    Assert.Equal("Fin", describe state.Cue)
    Assert.True(state.Zones.ContainsKey atriumPlace)
    Assert.Equal(activeBefore, state.ActiveZone) // an add prepares; it does not enter
    Assert.Equal(Some "room", state.Zones.[atriumPlace].Environment |> Option.map (fun e -> e.AssetId))

    // a place already present is a no-op
    let mutable again = state
    tick shared &again 0.0
    Assert.Equal(activeBefore, again.ActiveZone)

[<Fact>]
let ``a remove op makes a present inactive place absent, and is a no-op for the active or an absent place`` () =
    let mutable state = withCue (seqOf [ RemoveZone atriumPlace ])
    GameplayState.addZone shared &state atriumPlace
    GameplayState.switchZone shared &state mainPlace
    Assert.True(state.Zones.ContainsKey atriumPlace)

    tick shared &state 0.0
    Assert.False(state.Zones.ContainsKey atriumPlace)
    Assert.Equal(mainPlace, state.ActiveZone)

    // the active place cannot be removed by a cue, and an absent place is nothing to remove
    let mutable active = withCue (seqOf [ RemoveZone mainPlace ])
    tick shared &active 0.0
    Assert.True(active.Zones.ContainsKey mainPlace)

    let mutable absent = withCue (seqOf [ RemoveZone atriumPlace ])
    tick shared &absent 0.0
    Assert.False(absent.Zones.ContainsKey atriumPlace)

[<Fact>]
let ``a preload op requests a present zone's environment and leaves the active zone's alone`` () =
    let shared = residentShared ()
    registerTemplate shared atriumPlace
    let mutable state = newState ()
    // the active zone already has the shared environment resident, so "untouched" is a real claim
    do ignore (Zone.loadEnvironment (GameplayState.activeZone state) shared.Physics shared.Assets "room")
    Assert.True(Assets.isEnvironmentResident shared.Assets "room")
    GameplayState.presentZone shared &state atriumPlace (ZoneRegistry.tryTemplate atriumPlace shared.ZoneRegistry)

    state.Cue <- PreloadZone atriumPlace
    let activeBefore = state.ActiveZone
    tick shared &state 0.0

    Assert.Equal("Fin", describe state.Cue)
    Assert.Equal(activeBefore, state.ActiveZone)
    Assert.True(Assets.isEnvironmentResident shared.Assets "room")
    Assert.True(Option.isSome (GameplayState.activeZone state).Environment)

    // an absent place is nothing to preload
    let mutable bare = withCue (seqOf [ PreloadZone atriumPlace ])
    tick shared &bare 0.0
    Assert.False(bare.Zones.ContainsKey atriumPlace)

[<Fact>]
let ``a preload op brings a present but empty registered place its content`` () =
    let shared = residentShared ()
    registerTemplate shared atriumPlace
    let mutable state = newState ()
    GameplayState.addZone shared &state atriumPlace // the editor's add: present but with no environment
    GameplayState.switchZone shared &state mainPlace
    Assert.True(Option.isNone state.Zones.[atriumPlace].Environment)

    state.Cue <- PreloadZone atriumPlace
    tick shared &state 0.0

    Assert.Equal("Fin", describe state.Cue)
    Assert.Equal(Some "room", state.Zones.[atriumPlace].Environment |> Option.map (fun e -> e.AssetId))
    Assert.Equal(mainPlace, state.ActiveZone) // preloading does not enter

    // and it works while the place is the active one too
    let mutable active = newState ()
    GameplayState.addZone shared &active atriumPlace
    Assert.True(Option.isNone active.Zones.[atriumPlace].Environment)
    active.Cue <- PreloadZone atriumPlace
    tick shared &active 0.0
    Assert.Equal(atriumPlace, active.ActiveZone)
    Assert.Equal(Some "room", active.Zones.[atriumPlace].Environment |> Option.map (fun e -> e.AssetId))

[<Fact>]
let ``every zone op on a place it cannot act on performs nothing and the cue still finishes`` () =
    let mutable state = withCue (seqOf
        [ AddZone mainPlace          // already present
          RemoveZone atriumPlace     // absent
          RemoveZone mainPlace       // the active place
          SetZone atriumPlace        // absent
          PreloadZone atriumPlace ]) // absent
    tick shared &state 0.0
    Assert.Equal("Fin", describe state.Cue)
    Assert.True(state.Zones.ContainsKey mainPlace)
    Assert.False(state.Zones.ContainsKey atriumPlace)
    Assert.Equal(mainPlace, state.ActiveZone)

[<Fact>]
let ``preload, set and remove compose into a prepared transition`` () =
    let shared = residentShared ()
    registerTemplate shared atriumPlace
    let mutable state = newState ()
    GameplayState.addZone shared &state classroomPlace // present and active
    GameplayState.switchZone shared &state mainPlace
    state.Cue <- seqOf [ AddZone atriumPlace; PreloadZone atriumPlace; SetZone atriumPlace; RemoveZone classroomPlace ]

    tick shared &state 0.0

    Assert.Equal("Fin", describe state.Cue)
    Assert.Equal(atriumPlace, state.ActiveZone)
    Assert.True(state.Zones.ContainsKey atriumPlace)
    Assert.False(state.Zones.ContainsKey classroomPlace)

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

// ══════ Actor cue ops (spec 064) — the lifecycle pair ══════

// A shared state whose manager holds one fabricated actor asset (its GPU handles defaulted, so the real
// spawn/remove paths run with no device — the `test/CastIndexTests.fs` shape) and a cast binding for
// `(Aiko, SchoolUniform)`. Akane is deliberately unbound, and the other look too.
let private actorShared () : SharedState.SharedState =
    let anim (name: string) : Iqm.Anim =
        { Name = name; FirstFrame = 0; NumFrames = 2; FrameRate = 24.0f; Loop = true }
    let model : Iqm.Model =
        { Meshes = [||]
          Positions = [||]
          Normals = None
          Uvs = None
          Tangents = None
          BlendIndices = None
          BlendWeights = None
          Triangles = [||]
          Joints = [||]
          Poses = [||]
          Anims = [| anim "Idle"; anim "Jog" |]
          FrameChannels = [||]
          NumFrameChannels = 0
          PoseChannelOffsets = [||] }
    let morphs : ActorAsset.ActorMorphs =
        { Names = [| "Smile" |]; Count = 1; Buffer = Unchecked.defaultof<Gpu.Buffer> }
    let asset : ActorAsset.ActorAsset =
        { Id = "aiko"
          Name = "aiko"
          ModelPath = ""
          Bounds = { Min = Vector3.Zero; Max = Vector3.One }
          Mesh = Unchecked.defaultof<Mesh.Mesh>
          Model = Actor.createModel model
          Morphs = Some morphs
          Portraits = None }
    let actors = Dictionary<string, ActorAsset.ActorAsset>()
    actors.["aiko"] <- asset
    let bindings = Dictionary<Casting.Slot, string>()
    bindings.[{ Character = Roster.Aiko; Look = Casting.Look.SchoolUniform }] <- "aiko"
    { Gpu = Unchecked.defaultof<Gpu.Gpu>
      Assets = TestSupport.manager IntPtr.Zero actors (Dictionary()) (Dictionary())
      Cast = { Casting.Bindings = bindings }
      ZoneRegistry = ZoneRegistry.create ()
      Advents = Set.empty
      Physics = Physics.create () }

/// A waypoint named `name` at `position`, facing `rotation`, in `zone`.
let private waypointIn (zone: Zone.Zone) (name: string) (position: Vector3) (rotation: Quaternion) : EntityRef =
    let entity : Entity =
        { Name = name
          Transform = { transformIdentity with Position = position; Rotation = rotation }
          Enabled = true
          Visible = true
          Selectable = true
          Draggable = true }
    Zone.spawnWaypoint zone entity

let private castRef (state: GameplayState.GameplayState) (character: Roster.Character) : EntityRef =
    match GameplayState.tryCastActor state character with
    | Some (_, ref) -> ref
    | None -> failwith "expected the character to resolve"

/// Aiko placed at a waypoint in the active zone, so the blend/motion/locomotion tests start from a real actor.
let private placeAiko (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : EntityRef =
    let zone = GameplayState.activeZone state
    do ignore (waypointIn zone "spawn" Vector3.Zero Quaternion.Identity)
    state.Cue <- SetActor(mainPlace, Roster.Aiko, Casting.Look.SchoolUniform, "spawn")
    tick shared &state 0.0
    castRef state Roster.Aiko

/// The named clip's current weight on the actor, or `None` when the asset lacks it (the reader a blend uses).
let private clipWeight (zone: Zone.Zone) (ref: EntityRef) (name: string) : float32 option =
    let actor = &zone.Actors.Get(handleOf ref)
    ActorRender.tryClipWeight &actor.Renderer name

/// The named morph's current weight on the actor, or `None` when the asset lacks it.
let private morphWeight (zone: Zone.Zone) (ref: EntityRef) (name: string) : float32 option =
    let actor = &zone.Actors.Get(handleOf ref)
    ActorRender.tryMorphWeight &actor.Renderer name

let private weightOr (fallback: float32) (weight: float32 option) : float32 =
    match weight with Some w -> w | None -> fallback

let private bodyOf (zone: Zone.Zone) (ref: EntityRef) : Physics.BodyId =
    (zone.Actors.Get(handleOf ref)).Physics.Value.Body

[<Fact>]
let ``a set op brings a member into the named place at its waypoint and indexes her`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    do ignore (waypointIn zone "door" (Vector3(1.0f, 0.0f, 2.0f)) Quaternion.Identity)
    state.Cue <- SetActor(mainPlace, Roster.Aiko, Casting.Look.SchoolUniform, "door")

    tick shared &state 0.0

    Assert.Equal("Fin", describe state.Cue)
    let ref = castRef state Roster.Aiko
    Assert.Equal(Some (mainPlace, ref), GameplayState.tryCastActor state Roster.Aiko)
    let actor = zone.Actors.Get(handleOf ref)
    Assert.Equal(Vector3(1.0f, 0.0f, 2.0f), actor.State.Base.Transform.Position)
    Assert.Equal(Some Roster.Aiko, actor.State.Cast)

[<Fact>]
let ``a set in the same place moves the same actor; a set elsewhere moves her across zones keeping her art`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    let main = GameplayState.activeZone state
    do ignore (waypointIn main "a" Vector3.Zero Quaternion.Identity)
    do ignore (waypointIn main "b" (Vector3(5.0f, 0.0f, 0.0f)) Quaternion.Identity)
    state.Cue <- SetActor(mainPlace, Roster.Aiko, Casting.Look.SchoolUniform, "a")
    tick shared &state 0.0
    let ref = castRef state Roster.Aiko
    let count = main.Actors.Count

    // same place: the same actor moves, and a different look is ignored (a set never re-skins her)
    state.Cue <- SetActor(mainPlace, Roster.Aiko, Casting.Look.Casual, "b")
    tick shared &state 0.0
    Assert.Equal(count, main.Actors.Count)
    Assert.Equal(Some (mainPlace, ref), GameplayState.tryCastActor state Roster.Aiko)
    Assert.Equal(Vector3(5.0f, 0.0f, 0.0f), (main.Actors.Get(handleOf ref)).State.Base.Transform.Position)

    // another present place: she moves across zones, keeping her existing art
    GameplayState.addZone shared &state atriumPlace
    GameplayState.switchZone shared &state mainPlace
    let atrium = state.Zones.[atriumPlace]
    do ignore (waypointIn atrium "far" (Vector3(0.0f, 0.0f, 9.0f)) Quaternion.Identity)
    let assetBefore = (main.Actors.Get(handleOf ref)).State.AssetId

    state.Cue <- SetActor(atriumPlace, Roster.Aiko, Casting.Look.Casual, "far")
    tick shared &state 0.0

    Assert.Equal(0u, main.Actors.Count) // she left the old place
    match GameplayState.tryCastActor state Roster.Aiko with
    | Some (place, ref2) ->
        Assert.Equal(atriumPlace, place)
        let moved = atrium.Actors.Get(handleOf ref2)
        Assert.Equal(assetBefore, moved.State.AssetId) // art kept, the op's look not consulted
        Assert.Equal(Some Roster.Aiko, moved.State.Cast)
        Assert.Equal(Vector3(0.0f, 0.0f, 9.0f), moved.State.Base.Transform.Position)
    | None -> Assert.Fail "expected Aiko to resolve in the atrium"

[<Fact>]
let ``an actor op is a runtime effect, not a step on the undo history`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    do ignore (waypointIn zone "door" Vector3.Zero Quaternion.Identity)
    let undoBefore = List.length state.Editor.Undo.Applied

    state.Cue <- seqOf [ SetActor(mainPlace, Roster.Aiko, Casting.Look.SchoolUniform, "door"); RemoveActor Roster.Aiko ]
    tick shared &state 0.0

    Assert.Equal("Fin", describe state.Cue)
    Assert.Equal(undoBefore, List.length state.Editor.Undo.Applied) // no step recorded
    Assert.Equal(None, GameplayState.tryCastActor state Roster.Aiko) // and the scene reflects it

[<Fact>]
let ``a remove takes the member out of the named place and drops her index entry`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    do ignore (waypointIn zone "door" Vector3.Zero Quaternion.Identity)
    state.Cue <- SetActor(mainPlace, Roster.Aiko, Casting.Look.SchoolUniform, "door")
    tick shared &state 0.0
    Assert.Equal(1u, zone.Actors.Count)

    state.Cue <- RemoveActor Roster.Aiko
    tick shared &state 0.0

    Assert.Equal("Fin", describe state.Cue)
    Assert.Equal(None, GameplayState.tryCastActor state Roster.Aiko)
    Assert.Equal(0u, zone.Actors.Count)

    // a second remove, and a remove of a character never in the scene, perform nothing
    state.Cue <- seqOf [ RemoveActor Roster.Aiko; RemoveActor Roster.Akane ]
    tick shared &state 0.0
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``a remove of the zone's player leaves a designation that no longer resolves`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    do ignore (waypointIn zone "door" Vector3.Zero Quaternion.Identity)
    state.Cue <- SetActor(mainPlace, Roster.Aiko, Casting.Look.SchoolUniform, "door")
    tick shared &state 0.0
    let ref = castRef state Roster.Aiko
    zone.Player <- ValueSome (Systems.playerHandleOf ref)
    Assert.True(Systems.isPlayerLive zone)

    state.Cue <- RemoveActor Roster.Aiko
    tick shared &state 0.0

    // no clear step is added: the stale handle simply reads as "no player", as an editor delete leaves it
    Assert.False(Systems.isPlayerLive zone)

[<Fact>]
let ``a same-place set on a player actor leaves the designation intact`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    do ignore (waypointIn zone "a" Vector3.Zero Quaternion.Identity)
    do ignore (waypointIn zone "b" (Vector3(3.0f, 0.0f, 0.0f)) Quaternion.Identity)
    state.Cue <- SetActor(mainPlace, Roster.Aiko, Casting.Look.SchoolUniform, "a")
    tick shared &state 0.0
    let ref = castRef state Roster.Aiko
    zone.Player <- ValueSome (Systems.playerHandleOf ref)

    state.Cue <- SetActor(mainPlace, Roster.Aiko, Casting.Look.SchoolUniform, "b")
    tick shared &state 0.0

    Assert.Equal(ValueSome (Systems.playerHandleOf ref), zone.Player)
    Assert.True(Systems.isPlayerLive zone)

[<Fact>]
let ``every actor op on state it cannot act on performs nothing and the cue still finishes`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    do ignore (waypointIn zone "door" Vector3.Zero Quaternion.Identity)
    GameplayState.addZone shared &state atriumPlace
    GameplayState.switchZone shared &state mainPlace

    state.Cue <- seqOf
        [ SetActor(classroomPlace, Roster.Akane, Casting.Look.SchoolUniform, "door") // place not present
          SetActor(mainPlace, Roster.Akane, Casting.Look.Casual, "door")            // no art for the look
          SetActor(mainPlace, Roster.Akane, Casting.Look.SchoolUniform, "ghost")   // unknown waypoint
          RemoveActor Roster.Akane ]                                                  // not in the scene
    tick shared &state 0.0

    Assert.Equal("Fin", describe state.Cue) // the cue ran to the end; nothing raised
    Assert.Equal(None, GameplayState.tryCastActor state Roster.Akane)
    Assert.Equal(0u, (state.Zones.[atriumPlace]).Actors.Count)

[<Fact>]
let ``a set into an inactive place then a switch composes, and set then remove empties the index`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    GameplayState.addZone shared &state atriumPlace
    do ignore (waypointIn (state.Zones.[atriumPlace]) "far" (Vector3(0.0f, 0.0f, 9.0f)) Quaternion.Identity)
    GameplayState.switchZone shared &state mainPlace

    state.Cue <- seqOf [ SetActor(atriumPlace, Roster.Aiko, Casting.Look.SchoolUniform, "far"); SetZone atriumPlace ]
    tick shared &state 0.0

    Assert.Equal("Fin", describe state.Cue)
    Assert.Equal(atriumPlace, state.ActiveZone)
    match GameplayState.tryCastActor state Roster.Aiko with
    | Some (place, _) -> Assert.Equal(atriumPlace, place)
    | None -> Assert.Fail "expected Aiko in the atrium"

    state.Cue <- RemoveActor Roster.Aiko
    tick shared &state 0.0
    Assert.Equal(None, GameplayState.tryCastActor state Roster.Aiko)

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
    let mutable state = withCue (seqOf (List.replicate 200000 (Exposit("x", Dialogue.Kind.Dialogue))))
    tick shared &state 0.0 // one tick must consume the whole run
    Assert.Equal("Fin", describe state.Cue)

// ══════ FR-012 — allocation ══════

[<Fact>]
let ``an idle tick allocates nothing, flat and several sequences deep, and Fin costs nothing`` () =
    let mutable flat = withCue (seqOf [ Wait 100.0; Exposit("later", Dialogue.Kind.Dialogue) ])
    let mutable deep = withCue (seqOf [ seqOf [ seqOf [ Wait 100.0 ] ]; Exposit("later", Dialogue.Kind.Dialogue) ])
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
    let cue = Sequence(Exposit("x", Dialogue.Kind.Dialogue), FDeque.ofList [ WaitState(5.0, 10.0) ])
    match advance shared &state 0.0 cue with
    | ValueSome moved ->
        Assert.Equal("Seq[Wait 5]", describe moved) // the `Exposit` ran, the moved-up wait is the front
        Assert.True state.Dialogue.Shown
        Assert.Equal("x", state.Dialogue.Text)
    | ValueNone -> Assert.Fail "expected the moved-up sequence, not nothing to change"

[<Fact>]
let ``a placed actor's facing reaches its physics body, so it survives the play sync`` () =
    // The body is the source of truth while playing (`Systems.syncActors` copies body -> entity), so an
    // authored facing that never reached the body would be lost the moment Play begins.
    let shared = actorShared ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    // a waypoint turned so its arrow points +X
    let arrow = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2.0f)
    do ignore (waypointIn zone "door" Vector3.Zero arrow)
    state.Cue <- SetActor(mainPlace, Roster.Aiko, Casting.Look.SchoolUniform, "door")
    tick shared &state 0.0

    let ref = castRef state Roster.Aiko
    let body = bodyOf zone ref
    let entityFacing = Vector3.Transform(Vector3.UnitZ, (zone.Actors.Get(handleOf ref)).State.Base.Transform.Rotation)
    let (_, bodyRotation) = Physics.getBodyTransform shared.Physics body
    let bodyFacing = Vector3.Transform(Vector3.UnitZ, bodyRotation)
    Assert.True(Vector3.Dot(entityFacing, Vector3.UnitX) > 0.999f, sprintf "expected the entity to face the waypoint's arrow, got %A" entityFacing)
    Assert.True(Vector3.Dot(bodyFacing, Vector3.UnitX) > 0.999f, sprintf "expected the body to face it too, got %A" bodyFacing)

// ══════ Actor blend ops (spec 066) ══════

[<Fact>]
let ``an animate op moves a clip's weight to the target over its duration and lands exactly`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    let ref = placeAiko shared &state
    let zone = GameplayState.activeZone state
    Assert.Equal(0.0f, weightOr -1.0f (clipWeight zone ref "Jog")) // the asset defines Jog, but she holds it at 0

    state.Cue <- Animate(Roster.Aiko, "Jog", 1.0f, 2.0)
    tick shared &state 0.0
    Assert.Equal(0.0f, weightOr -1.0f (clipWeight zone ref "Jog")) // begun; the first tick writes nothing yet

    tick shared &state 1.0
    Assert.Equal(0.5f, weightOr -1.0f (clipWeight zone ref "Jog"))

    tick shared &state 2.0
    Assert.Equal(1.0f, weightOr -1.0f (clipWeight zone ref "Jog"))
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``a cross fade takes one clip out as another comes in, reaching the target at the deadline`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    let ref = placeAiko shared &state
    let zone = GameplayState.activeZone state
    Assert.Equal(1.0f, weightOr -1.0f (clipWeight zone ref "Idle")) // created holding Idle at full weight

    state.Cue <- CrossFade(Roster.Aiko, "Idle", "Jog", 1.0f, 2.0)
    tick shared &state 0.0

    tick shared &state 1.0
    Assert.Equal(0.5f, weightOr -1.0f (clipWeight zone ref "Idle"))
    Assert.Equal(0.5f, weightOr -1.0f (clipWeight zone ref "Jog"))

    tick shared &state 2.0
    Assert.Equal(0.0f, weightOr -1.0f (clipWeight zone ref "Idle"))
    Assert.Equal(1.0f, weightOr -1.0f (clipWeight zone ref "Jog"))
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``a morph op blends a morph slot the same way`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    let ref = placeAiko shared &state
    let zone = GameplayState.activeZone state
    Assert.Equal(0.0f, weightOr -1.0f (morphWeight zone ref "Smile"))

    state.Cue <- Morph(Roster.Aiko, "Smile", 1.0f, 2.0)
    tick shared &state 0.0
    tick shared &state 1.0
    Assert.Equal(0.5f, weightOr -1.0f (morphWeight zone ref "Smile"))

    tick shared &state 2.0
    Assert.Equal(1.0f, weightOr -1.0f (morphWeight zone ref "Smile"))
    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``a blend of an unknown clip or morph name, and of a missing actor, performs nothing`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    let ref = placeAiko shared &state
    let zone = GameplayState.activeZone state

    state.Cue <- seqOf [ Animate(Roster.Aiko, "Nope", 1.0f, 1.0); Morph(Roster.Aiko, "Nope", 1.0f, 1.0); CrossFade(Roster.Aiko, "Idle", "Nope", 1.0f, 1.0); Animate(Roster.Akane, "Idle", 1.0f, 1.0) ]
    tick shared &state 0.0

    Assert.Equal("Fin", describe state.Cue)
    Assert.Equal(1.0f, weightOr -1.0f (clipWeight zone ref "Idle")) // untouched
    Assert.Equal(0.0f, weightOr -1.0f (clipWeight zone ref "Jog"))

[<Fact>]
let ``a blend is a runtime effect, not a step on the undo history`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    do ignore (placeAiko shared &state)
    let undoBefore = List.length state.Editor.Undo.Applied

    state.Cue <- Animate(Roster.Aiko, "Jog", 1.0f, 1.0)
    tick shared &state 0.0
    tick shared &state 1.0

    Assert.Equal(undoBefore, List.length state.Editor.Undo.Applied)

// ══════ Actor motion ops (spec 066) — physics-driven, active-zone only ══════

[<Fact>]
let ``a move op drives the actor along the chain and arrives at the last waypoint when unobstructed`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    let ref = placeAiko shared &state
    let zone = GameplayState.activeZone state
    let start = waypointIn zone "start" Vector3.Zero Quaternion.Identity
    let mid = waypointIn zone "mid" (Vector3(2.0f, 0.0f, 0.0f)) Quaternion.Identity
    let finish = waypointIn zone "finish" (Vector3(4.0f, 0.0f, 0.0f)) Quaternion.Identity
    do Zone.setWaypointNext zone start (Some mid)
    do Zone.setWaypointNext zone mid (Some finish)
    let body = bodyOf zone ref

    state.Cue <- MoveActor(Roster.Aiko, "start", 2.0, "Jog", "Idle")
    tick shared &state 0.0

    // the moving clip is preset, the idle clip off, and the op is in flight
    Assert.Equal(1.0f, weightOr -1.0f (clipWeight zone ref "Jog"))
    Assert.Equal(0.0f, weightOr -1.0f (clipWeight zone ref "Idle"))

    let dt = 0.02f
    let mutable now = 0.0
    while not state.Cue.IsFin && now < 5.0 do
        do Physics.step shared.Physics dt
        now <- now + float dt
        tick shared &state now

    Assert.Equal("Fin", describe state.Cue)
    let (position, _) = Physics.getBodyTransform shared.Physics body
    Assert.True(abs (position.X - 4.0f) < 0.5f, sprintf "expected the last waypoint, got %A" position)
    Assert.Equal(1.0f, weightOr -1.0f (clipWeight zone ref "Idle")) // arrived idle
    Assert.Equal(0.0f, weightOr -1.0f (clipWeight zone ref "Jog"))

[<Fact>]
let ``a move op walks to a single waypoint and turns to face its arrow`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    let ref = placeAiko shared &state
    let zone = GameplayState.activeZone state
    let target = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2.0f)
    do ignore (waypointIn zone "there" (Vector3(4.0f, 0.0f, 0.0f)) target)
    let body = bodyOf zone ref

    // a single waypoint is a straight walk from where she stands, not a no-op
    state.Cue <- MoveActor(Roster.Aiko, "there", 2.0, "Jog", "Idle")
    tick shared &state 0.0
    Assert.True((match state.Cue with MoveActorState _ -> true | _ -> false), "expected the move to begin")

    let dt = 0.02f
    let mutable now = 0.0
    while not state.Cue.IsFin && now < 5.0 do
        do Physics.step shared.Physics dt
        now <- now + float dt
        tick shared &state now

    Assert.Equal("Fin", describe state.Cue)
    let (position, _) = Physics.getBodyTransform shared.Physics body
    Assert.True(abs (position.X - 4.0f) < 0.5f, sprintf "expected the waypoint, got %A" position)

[<Fact>]
let ``a move op keeps a correctly-turned actor facing the path, not a waypoint's arrow`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    let ref = placeAiko shared &state
    let zone = GameplayState.activeZone state
    // the waypoint is turned to face along the path too, as the author had it
    do ignore (waypointIn zone "ahead" (Vector3(4.0f, 0.0f, 0.0f)) (Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2.0f)))
    let facingX = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2.0f)
    let actorAtStart = &zone.Actors.Get(handleOf ref)
    let turned = { actorAtStart.State.Base.Transform with Rotation = facingX }
    do Zone.setTransform zone shared.Physics ref turned
    let body = bodyOf zone ref

    state.Cue <- MoveActor(Roster.Aiko, "ahead", 2.0, "Jog", "Idle")
    tick shared &state 0.0

    let dt = 0.02f
    let mutable now = 0.0
    while not state.Cue.IsFin && now < 5.0 do
        do Physics.step shared.Physics dt
        now <- now + float dt
        tick shared &state now
        // she faces the path throughout — never the waypoint's default arrow
        let (_, rotation) = Physics.getBodyTransform shared.Physics body
        let facing = Vector3.Transform(Vector3.UnitZ, rotation)
        Assert.True(Vector3.Dot(facing, Vector3.UnitX) > 0.999f, sprintf "expected to face along the path, got %A" facing)

    Assert.Equal("Fin", describe state.Cue)

[<Fact>]
let ``a rotate op turns the actor to face a waypoint over its duration`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    let ref = placeAiko shared &state
    let zone = GameplayState.activeZone state
    let target = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2.0f)
    do ignore (waypointIn zone "face" Vector3.Zero target)
    let body = bodyOf zone ref

    state.Cue <- RotateActor(Roster.Aiko, "face", 1.0)
    tick shared &state 0.0

    let dt = 0.02f
    let mutable now = 0.0
    while now < 1.0 do
        do Physics.step shared.Physics dt
        now <- now + float dt
        tick shared &state now

    Assert.Equal("Fin", describe state.Cue)
    let (_, rotation) = Physics.getBodyTransform shared.Physics body
    // the actor's forward (local +Z) must look where the waypoint's arrow (local -Z) points
    let facing = Vector3.Transform(Vector3.UnitZ, rotation)
    let arrow = Vector3.Transform(-Vector3.UnitZ, target)
    Assert.True(Vector3.Dot(facing, arrow) > 0.999f, sprintf "expected to face the waypoint's arrow %A, got %A" arrow facing)

[<Fact>]
let ``a motion op on state it cannot act on performs nothing`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    let ref = placeAiko shared &state
    let zone = GameplayState.activeZone state
    do ignore (waypointIn zone "lonely" Vector3.Zero Quaternion.Identity) // a chain of one
    let before = (zone.Actors.Get(handleOf ref)).State.Base.Transform.Position

    state.Cue <- seqOf [
        MoveActor(Roster.Aiko, "missing", 1.0, "Jog", "Idle")   // unresolved path
        MoveActor(Roster.Aiko, "lonely", 1.0, "Jog", "Idle")    // a path with no length (she is already there)
        MoveActor(Roster.Aiko, "lonely", 0.0, "Jog", "Idle")    // non-positive speed
        RotateActor(Roster.Aiko, "missing", 1.0)                 // unresolved waypoint
        MoveActor(Roster.Akane, "lonely", 1.0, "Jog", "Idle") ] // missing actor
    tick shared &state 0.0

    Assert.Equal("Fin", describe state.Cue)
    Assert.Equal(before, (zone.Actors.Get(handleOf ref)).State.Base.Transform.Position)

[<Fact>]
let ``a motion op performs nothing when the actor's zone is not the active one`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    do ignore (placeAiko shared &state)

    // make a second place present (not active) with a chain, and move her into it
    GameplayState.addZone shared &state atriumPlace
    GameplayState.switchZone shared &state mainPlace // `addZone` entered it; go back so the atrium is inactive
    let atrium = state.Zones.[atriumPlace]
    let a = waypointIn atrium "a" Vector3.Zero Quaternion.Identity
    let b = waypointIn atrium "b" (Vector3(4.0f, 0.0f, 0.0f)) Quaternion.Identity
    do Zone.setWaypointNext atrium a (Some b)
    state.Cue <- SetActor(atriumPlace, Roster.Aiko, Casting.Look.SchoolUniform, "a")
    tick shared &state 0.0
    let ref = castRef state Roster.Aiko
    Assert.Equal(Some (atriumPlace, ref), GameplayState.tryCastActor state Roster.Aiko)

    state.Cue <- seqOf [ MoveActor(Roster.Aiko, "a", 1.0, "Jog", "Idle"); RotateActor(Roster.Aiko, "b", 1.0) ]
    tick shared &state 0.0

    Assert.Equal("Fin", describe state.Cue)
    Assert.Equal(Vector3.Zero, (atrium.Actors.Get(handleOf ref)).State.Base.Transform.Position)

// ══════ The player toggle ops (spec 066) ══════

[<Fact>]
let ``enable player binds the actor as the zone's player and disable clears it`` () =
    let shared = actorShared ()
    let mutable state = newState ()
    let ref = placeAiko shared &state
    let zone = GameplayState.activeZone state
    Assert.False(Systems.isPlayerLive zone)

    state.Cue <- EnablePlayer Roster.Aiko
    tick shared &state 0.0
    Assert.True(Systems.isPlayerLive zone)
    Assert.Equal(ValueSome (Systems.playerHandleOf ref), zone.Player)

    state.Cue <- DisablePlayer Roster.Aiko
    tick shared &state 0.0
    Assert.False(Systems.isPlayerLive zone)

    // a disable when she is not the player, and an op on an actor not in the scene, perform nothing
    let undoBefore = List.length state.Editor.Undo.Applied
    state.Cue <- seqOf [ DisablePlayer Roster.Aiko; EnablePlayer Roster.Akane ]
    tick shared &state 0.0
    Assert.Equal("Fin", describe state.Cue)
    Assert.False(Systems.isPlayerLive zone)
    Assert.Equal(undoBefore, List.length state.Editor.Undo.Applied)

// ── spec 067: the trigger toggles and print ──

let private spawnTrigger (state: GameplayState.GameplayState) (name: string) : EntityRef =
    let entity : Entity =
        { Name = name
          Transform = transformIdentity
          Enabled = true
          Visible = true
          Selectable = true
          Draggable = true }
    Zone.spawnTrigger (GameplayState.activeZone state) entity

let private triggerEnabled (state: GameplayState.GameplayState) (ref: EntityRef) : bool =
    (Zone.tryTriggerAt (GameplayState.activeZone state) ref).Value.State.Base.Enabled

[<Fact>]
let ``a disable trigger op flips the flag and an enable trigger op restores it`` () =
    let mutable state = newState ()
    let ref = spawnTrigger state "area"
    Assert.True(triggerEnabled state ref)

    state.Cue <- DisableTrigger(mainPlace, "area")
    tick shared &state 0.0
    Assert.Equal("Fin", describe state.Cue)
    Assert.False(triggerEnabled state ref)

    state.Cue <- EnableTrigger(mainPlace, "area")
    tick shared &state 0.0
    Assert.True(triggerEnabled state ref)

[<Fact>]
let ``a redundant toggle, a missing name and an absent place perform nothing`` () =
    let mutable state = newState ()
    let ref = spawnTrigger state "area"
    state.Cue <- DisableTrigger(mainPlace, "area")
    tick shared &state 0.0
    Assert.False(triggerEnabled state ref)

    // redundant: already disabled
    state.Cue <- DisableTrigger(mainPlace, "area")
    tick shared &state 0.0
    Assert.Equal("Fin", describe state.Cue)
    Assert.False(triggerEnabled state ref)

    // a name the place does not hold
    state.Cue <- EnableTrigger(mainPlace, "ghost")
    tick shared &state 0.0
    Assert.Equal("Fin", describe state.Cue)
    Assert.False(triggerEnabled state ref)

    // a place that is not present
    state.Cue <- EnableTrigger(atriumPlace, "area")
    tick shared &state 0.0
    Assert.Equal("Fin", describe state.Cue)
    Assert.False(state.Zones.ContainsKey atriumPlace)
    Assert.False(triggerEnabled state ref)

[<Fact>]
let ``a trigger op is a runtime effect, not a step on the undo history`` () =
    let mutable state = newState ()
    do ignore (spawnTrigger state "area")
    let undoBefore = List.length state.Editor.Undo.Applied
    state.Cue <- DisableTrigger(mainPlace, "area")
    tick shared &state 0.0
    Assert.Equal("Fin", describe state.Cue)
    Assert.Equal(undoBefore, List.length state.Editor.Undo.Applied)

[<Fact>]
let ``a print op is a no-progress leaf that ends the tick it is reached`` () =
    let mutable state = withCue (seqOf [ Print "hello"; Wait 1.0 ])
    tick shared &state 0.0
    Assert.Equal("Seq[Wait 1]", describe state.Cue)

[<Fact>]
let ``a print op is not a step on the undo history`` () =
    let mutable state = newState ()
    let undoBefore = List.length state.Editor.Undo.Applied
    state.Cue <- Print "hello"
    tick shared &state 0.0
    Assert.Equal("Fin", describe state.Cue)
    Assert.Equal(undoBefore, List.length state.Editor.Undo.Applied)
