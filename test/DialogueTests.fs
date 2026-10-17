module DialogueTests

// The dialogue layer's two pure parts, asserted headlessly (no GPU, no font, no window):
// the panel layout (FR-004, SC-003, and the clarified reserved-column and too-tall rules)
// and, later in this feature, the dismiss rule (FR-008, SC-004).

open Xunit
open System.Collections.Generic
open Prime
open DialogueRender

let private viewports =
    [ (1280.0f, 720.0f); (1920.0f, 1080.0f); (800.0f, 600.0f); (640.0f, 480.0f); (3840.0f, 2160.0f) ]

// ── Panel layout ──

[<Fact>]
let ``the panel and its contents stay inside the viewport at every size`` () =
    for (w, h) in viewports do
        let l = (layout w h true 120.0f 48.0f).Value
        Assert.True(l.Panel.X >= 0.0f && l.Panel.Y >= 0.0f, sprintf "panel origin off screen at %fx%f" w h)
        Assert.True(l.Panel.X + l.Panel.W <= w, sprintf "panel runs off the right at %fx%f" w h)
        Assert.True(l.Panel.Y + l.Panel.H <= h, sprintf "panel runs off the bottom at %fx%f" w h)
        // The portrait and the text box sit inside the panel.
        Assert.True(l.Portrait.X >= l.Panel.X && l.Portrait.X + l.Portrait.W <= l.Panel.X + l.Panel.W)
        Assert.True(l.Portrait.Y >= l.Panel.Y && l.Portrait.Y + l.Portrait.H <= l.Panel.Y + l.Panel.H)
        Assert.True(l.Text.X >= l.Panel.X && l.Text.X + l.Text.W <= l.Panel.X + l.Panel.W)
        Assert.True(l.Text.Y >= l.Panel.Y && l.Text.Y + l.Text.H <= l.Panel.Y + l.Panel.H)

[<Fact>]
let ``the panel and its contents scale with the viewport`` () =
    let small = (layout 1280.0f 720.0f true 120.0f 48.0f).Value
    let large = (layout 2560.0f 1440.0f true 240.0f 96.0f).Value
    Assert.Equal(2.0f * small.Panel.W, large.Panel.W, 2)
    Assert.Equal(2.0f * small.Panel.X, large.Panel.X, 2)
    Assert.Equal(2.0f * small.Portrait.W, large.Portrait.W, 2)

[<Fact>]
let ``a taller measured text grows the panel rather than overflowing it`` () =
    let short = (layout 1280.0f 720.0f false 100.0f 24.0f).Value
    let tall = (layout 1280.0f 720.0f false 100.0f 120.0f).Value
    Assert.True(tall.Panel.H > short.Panel.H)
    Assert.True(tall.Panel.Y < short.Panel.Y)
    Assert.True(tall.Text.Y + tall.Text.H <= tall.Panel.Y + tall.Panel.H)

[<Fact>]
let ``the text box reserves the portrait column, so it does not move between kinds`` () =
    let withPortrait = (layout 1280.0f 720.0f true 100.0f 24.0f).Value
    let without = (layout 1280.0f 720.0f false 100.0f 24.0f).Value
    Assert.Equal(withPortrait.Text.X, without.Text.X, 3)
    Assert.Equal(withPortrait.Text.W, without.Text.W, 3)
    Assert.Equal(withPortrait.Text.H, without.Text.H, 3)
    // The portrait rect is empty only when no portrait is drawn.
    Assert.True(without.Portrait.W = 0.0f && without.Portrait.H = 0.0f)
    Assert.True(withPortrait.Portrait.W > 0.0f && withPortrait.Portrait.H > 0.0f)

[<Fact>]
let ``the wrap width is positive and narrower than the panel`` () =
    for (w, _) in viewports do
        let iw = innerWidth w
        Assert.True(iw > 0.0f, sprintf "wrap width not positive at %f" w)
        Assert.True(iw < w, sprintf "wrap width not inside the viewport at %f" w)

[<Fact>]
let ``a line taller than the viewport is reported rather than drawn clipped`` () =
    Assert.True((layout 1280.0f 720.0f false 100.0f 720.0f).IsNone)
    Assert.True((layout 1280.0f 720.0f true 100.0f 720.0f).IsNone)

// ── The portrait set (FR-006, SC-008) ──

[<Fact>]
let ``a complete portrait set passes and a gapped one is reported, naming what is missing`` () =
    let complete : ActorAsset.PortraitPaths =
        { Neutral = "n"; Happy = "h"; Sad = "s"; Angry = "a"; Surprised = "u" }
    ActorAsset.requireComplete complete
    let gapped : ActorAsset.PortraitPaths = { complete with Sad = null }
    let ex = Assert.Throws<System.Exception>(fun () -> ActorAsset.requireComplete gapped)
    Assert.Contains("Sad", ex.Message)

[<Fact>]
let ``declaring no portraits at all is legal`` () =
    let none : ActorAsset.PortraitPaths =
        { Neutral = null; Happy = null; Sad = null; Angry = null; Surprised = null }
    ActorAsset.requireComplete none

// The loader reads "every portrait field empty" as "declares no portraits" (FR-006), which rests on
// how Prime defaults an absent `[Portraits …]` block. That is a third-party guarantee this feature
// leans on — if it returned `null` instead, reading the fields would throw — so it is pinned here.
// The shape mirrors the real one: a private manifest record whose nested block type is the public
// `ActorAsset.PortraitPaths` (a private nested type would make Prime's default lookup throw).
[<SymbolicExpansion>]
type private AbsentManifest =
    { Id : string
      Portraits : ActorAsset.PortraitPaths }

[<Fact>]
let ``an absent Portraits block parses to a default set, not null`` () =
    let manifest = scvalue<AbsentManifest> "[[Id \"x\"]]"
    Assert.False(System.Object.ReferenceEquals(manifest.Portraits, null))
    Assert.True(System.String.IsNullOrEmpty manifest.Portraits.Neutral)
    Assert.True(System.String.IsNullOrEmpty manifest.Portraits.Surprised)

// ── The ending rules (FR-001..FR-007, SC-001..SC-004) ──

let private shown : Dialogue.Dialogue =
    { Shown = true
      Kind = Dialogue.Kind.PortraitDialogue ("Akane", Dialogue.Sad)
      Text = "hello"
      Endings = Set.singleton Dialogue.ByPlayer
      Timeout = ValueNone
      ShownAt = 0.0 }

let private at (shownAt: float) (endings: Set<Dialogue.Ending>) (timeout: float voption) : Dialogue.Dialogue =
    { shown with ShownAt = shownAt; Endings = endings; Timeout = timeout }

[<Fact>]
let ``a player-ending line clears on the dismiss edge only`` () =
    let mutable pressed = at 0.0 (Set.singleton Dialogue.ByPlayer) ValueNone
    do Systems.updateDialogue true false 0.0 &pressed
    Assert.False(pressed.Shown)

    let mutable focused = at 0.0 (Set.singleton Dialogue.ByPlayer) ValueNone
    do Systems.updateDialogue true true 0.0 &focused
    Assert.True(focused.Shown)

    let mutable idle = at 0.0 (Set.singleton Dialogue.ByPlayer) ValueNone
    do Systems.updateDialogue false false 0.0 &idle
    Assert.True(idle.Shown)

[<Fact>]
let ``the empty set ignores the dismiss edge and time`` () =
    let mutable line = at 0.0 Set.empty ValueNone
    do Systems.updateDialogue true false 100.0 &line
    Assert.True(line.Shown)
    do Systems.updateDialogue false false 1000000.0 &line
    Assert.True(line.Shown)

[<Fact>]
let ``a timeout line clears at its duration and ignores the dismiss edge`` () =
    let mutable early = at 10.0 (Set.singleton Dialogue.ByTimeout) (ValueSome 2.0)
    do Systems.updateDialogue true false 11.99 &early
    Assert.True(early.Shown)

    let mutable due = at 10.0 (Set.singleton Dialogue.ByTimeout) (ValueSome 2.0)
    do Systems.updateDialogue false false 12.0 &due
    Assert.False(due.Shown)

    let mutable pressed = at 10.0 (Set.singleton Dialogue.ByTimeout) (ValueSome 2.0)
    do Systems.updateDialogue true false 10.5 &pressed
    Assert.True(pressed.Shown)

[<Fact>]
let ``a player-or-timeout line ends on whichever comes first`` () =
    let endings = Set.ofList [ Dialogue.ByPlayer; Dialogue.ByTimeout ]
    let mutable byPress = at 10.0 endings (ValueSome 2.0)
    do Systems.updateDialogue true false 10.5 &byPress
    Assert.False(byPress.Shown)

    let mutable byTime = at 10.0 endings (ValueSome 2.0)
    do Systems.updateDialogue false false 12.0 &byTime
    Assert.False(byTime.Shown)

[<Fact>]
let ``the focus gate suppresses only the player condition`` () =
    let mutable playerFocused = at 10.0 (Set.singleton Dialogue.ByPlayer) ValueNone
    do Systems.updateDialogue true true 10.0 &playerFocused
    Assert.True(playerFocused.Shown)

    let mutable timeoutFocused = at 10.0 (Set.singleton Dialogue.ByTimeout) (ValueSome 1.0)
    do Systems.updateDialogue false true 11.0 &timeoutFocused
    Assert.False(timeoutFocused.Shown)

[<Fact>]
let ``an empty ending set never clears`` () =
    let mutable line = at 0.0 Set.empty ValueNone
    do Systems.updateDialogue true false 1000000.0 &line
    Assert.True(line.Shown)

[<Fact>]
let ``a non-positive timeout clears on the next check`` () =
    let mutable zero = at 10.0 (Set.singleton Dialogue.ByTimeout) (ValueSome 0.0)
    do Systems.updateDialogue false false 10.0 &zero
    Assert.False(zero.Shown)

    let mutable negative = at 10.0 (Set.singleton Dialogue.ByTimeout) (ValueSome -1.0)
    do Systems.updateDialogue false false 10.0 &negative
    Assert.False(negative.Shown)

[<Fact>]
let ``a ByTimeout without a duration never times out`` () =
    let mutable line = at 10.0 (Set.singleton Dialogue.ByTimeout) ValueNone
    do Systems.updateDialogue false false 1000.0 &line
    Assert.True(line.Shown)

[<Fact>]
let ``retiming measures from the original show time`` () =
    let mutable line = at 10.0 (Set.singleton Dialogue.ByTimeout) (ValueSome 10.0)
    do Systems.updateDialogue false false 14.0 &line
    Assert.True(line.Shown)

    // Four seconds already elapsed; a shorter duration counts them.
    line.Timeout <- ValueSome 3.0
    do Systems.updateDialogue false false 14.0 &line
    Assert.False(line.Shown)

[<Fact>]
let ``a portrait miss still honours its ending`` () =
    // `shown` is a portrait line; the runtime tick never consults the view, so an unresolvable
    // portrait cannot stop a player ending (FR-018).
    let mutable line = at 0.0 (Set.singleton Dialogue.ByPlayer) ValueNone
    do Systems.updateDialogue true false 0.0 &line
    Assert.False(line.Shown)

[<Fact>]
let ``pressing with nothing shown changes nothing`` () =
    let mutable pressed = Dialogue.empty
    do Systems.updateDialogue true false 0.0 &pressed
    Assert.False(pressed.Shown)

    let mutable idle = Dialogue.empty
    do Systems.updateDialogue false false 0.0 &idle
    Assert.False(idle.Shown)

[<Fact>]
let ``a frame with no press leaves a shown line shown`` () =
    let mutable kept = shown
    do Systems.updateDialogue false false 0.0 &kept
    Assert.True(kept.Shown)
    Assert.Equal(shown.Text, kept.Text)

[<Fact>]
let ``the dialogue tick allocates nothing`` () =
    let mutable line = at 0.0 (Set.ofList [ Dialogue.ByPlayer; Dialogue.ByTimeout ]) (ValueSome 10.0)
    TestSupport.assertAllocatesNothing 100 2000 (fun () -> Systems.updateDialogue false false 1.0 &line)

[<Fact>]
let ``the dialogue tick with nothing shown allocates nothing`` () =
    let mutable line = Dialogue.empty
    TestSupport.assertAllocatesNothing 100 2000 (fun () -> Systems.updateDialogue false false 1.0 &line)

// ── The ending helpers (FR-001, FR-017) ──

[<Fact>]
let ``ending helpers combine and summarise`` () =
    Assert.True(Dialogue.playerCloses (Set.singleton Dialogue.ByPlayer))
    Assert.False(Dialogue.playerCloses Set.empty)
    Assert.True(Dialogue.timed (Set.singleton Dialogue.ByTimeout))
    Assert.False(Dialogue.timed Set.empty)

    Assert.Equal<Dialogue.Ending list>([ Dialogue.ByPlayer ], Set.toList (Dialogue.withPlayer true Set.empty))
    Assert.Equal<Dialogue.Ending list>([], Set.toList (Dialogue.withPlayer false (Set.singleton Dialogue.ByPlayer)))
    Assert.Equal<Dialogue.Ending list>([ Dialogue.ByTimeout ], Set.toList (Dialogue.withTimeout true Set.empty))
    Assert.Equal<Dialogue.Ending list>([], Set.toList (Dialogue.withTimeout false (Set.singleton Dialogue.ByTimeout)))

    Assert.Equal("cue", Dialogue.summary Set.empty ValueNone)
    Assert.Equal("player", Dialogue.summary (Set.singleton Dialogue.ByPlayer) ValueNone)
    Assert.Equal("3.5s", Dialogue.summary (Set.singleton Dialogue.ByTimeout) (ValueSome 3.5))
    Assert.Equal("player or 3.5s", Dialogue.summary (Set.ofList [ Dialogue.ByPlayer; Dialogue.ByTimeout ]) (ValueSome 3.5))

[<Fact>]
let ``the empty dialogue defaults to a dismissible hand-shown line`` () =
    Assert.False(Dialogue.empty.Shown)
    Assert.Equal<Dialogue.Ending list>([ Dialogue.ByPlayer ], Set.toList Dialogue.empty.Endings)
    Assert.Equal<float voption>(ValueNone, Dialogue.empty.Timeout)

// ── A draw-time portrait miss is reported, not raised (FR-005, FR-006, SC-002) ──

let private portraits : ActorAsset.ActorPortraits =
    { Neutral = Unchecked.defaultof<Texture.Texture>
      Happy = Unchecked.defaultof<Texture.Texture>
      Sad = Unchecked.defaultof<Texture.Texture>
      Angry = Unchecked.defaultof<Texture.Texture>
      Surprised = Unchecked.defaultof<Texture.Texture> }

let private assetsWith (portraitsOpt: ActorAsset.ActorPortraits option) : Assets.AssetManager =
    TestSupport.managerWith
        [ "akane", { TestSupport.actorAsset "akane" with Name = "Akane"; Portraits = portraitsOpt } ]
        []
        []

[<Fact>]
let ``a speaker with no such asset resolves to no portrait, as an expected miss`` () =
    match DialogueRender.tryPortrait (assetsWith (Some portraits)) "Nobody" Dialogue.Sad with
    | Missed reason -> Assert.Equal(NoAsset, reason)
    | Resolved _ -> Assert.Fail("a speaker with no asset must not resolve")

[<Fact>]
let ``an asset that declares no portraits is an expected miss`` () =
    match DialogueRender.tryPortrait (assetsWith None) "Akane" Dialogue.Sad with
    | Missed reason -> Assert.Equal(NoPortraits, reason)
    | Resolved _ -> Assert.Fail("an asset with no portraits must not resolve")

[<Fact>]
let ``an asset named for the speaker with portraits resolves`` () =
    match DialogueRender.tryPortrait (assetsWith (Some portraits)) "Akane" Dialogue.Sad with
    | Resolved _ -> ()
    | Missed reason -> Assert.Fail(sprintf "expected a portrait, got %A" reason)

// ── The pure draw path allocates nothing per call (FR-010, SC-007) ──
// Both of these run on every drawn frame, so an allocation here is per-frame garbage.

[<Fact>]
let ``layout allocates nothing per call`` () =
    TestSupport.assertAllocatesNothing 1000 10000 (fun () -> layout 1280.0f 720.0f true 120.0f 48.0f |> ignore)

[<Fact>]
let ``tryPortrait allocates nothing per call`` () =
    let assets = assetsWith (Some portraits)
    // Both the resolved path and the miss path run every frame a portrait line is shown.
    TestSupport.assertAllocatesNothing 1000 10000 (fun () ->
        DialogueRender.tryPortrait assets "Akane" Dialogue.Sad |> ignore
        DialogueRender.tryPortrait assets "Nobody" Dialogue.Sad |> ignore)
