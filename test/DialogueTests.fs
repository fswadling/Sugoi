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

// ── The dismiss rule (FR-008, SC-004) ──

let private shown : Dialogue.Dialogue =
    { Shown = true; Kind = Dialogue.Kind.PortraitDialogue (Roster.Akane, Dialogue.Sad); Text = "hello" }

[<Fact>]
let ``a shown line clears only when the key is pressed and no field has focus`` () =
    let mutable cleared = shown
    do Systems.dismissDialogue true false &cleared
    Assert.False(cleared.Shown)

    let mutable focused = shown
    do Systems.dismissDialogue true true &focused
    Assert.True(focused.Shown)

    let mutable idle = shown
    do Systems.dismissDialogue false false &idle
    Assert.True(idle.Shown)

[<Fact>]
let ``pressing with nothing shown changes nothing`` () =
    let mutable pressed = Dialogue.empty
    do Systems.dismissDialogue true false &pressed
    Assert.False(pressed.Shown)

    let mutable idle = Dialogue.empty
    do Systems.dismissDialogue false false &idle
    Assert.False(idle.Shown)

[<Fact>]
let ``a frame with no press leaves a shown line shown`` () =
    let mutable kept = shown
    do Systems.dismissDialogue false false &kept
    Assert.True(kept.Shown)
    Assert.Equal(shown.Text, kept.Text)

// ── A draw-time portrait miss is reported, not raised (FR-005, FR-006, SC-002) ──

let private portraits : ActorAsset.ActorPortraits =
    { Neutral = Unchecked.defaultof<Texture.Texture>
      Happy = Unchecked.defaultof<Texture.Texture>
      Sad = Unchecked.defaultof<Texture.Texture>
      Angry = Unchecked.defaultof<Texture.Texture>
      Surprised = Unchecked.defaultof<Texture.Texture> }

let private assetsWith (portraitsOpt: ActorAsset.ActorPortraits option) : Assets.AssetManager =
    let actors = Dictionary<string, ActorAsset.ActorAsset>()
    actors.["akane"] <-
        { Id = "akane"; Name = "Akane"; ModelPath = ""; Bounds = Unchecked.defaultof<Gltf.Bounds>
          Mesh = Unchecked.defaultof<Mesh.Mesh>; Model = Unchecked.defaultof<Actor.ActorModel>
          Morphs = None; Portraits = portraitsOpt }
    TestSupport.manager System.IntPtr.Zero actors (Dictionary()) (Dictionary())

let private castBinding (character: Roster.Character) : Casting.Cast =
    let bindings = Dictionary<Casting.Slot, string>()
    bindings.[{ Casting.Character = character; Casting.Look = Casting.Look.SchoolUniform }] <- "akane"
    { Casting.Bindings = bindings }

let private emptyCast : Casting.Cast = { Casting.Bindings = Dictionary() }

[<Fact>]
let ``an unbound character resolves to no portrait, as an expected miss`` () =
    match DialogueRender.tryPortrait (assetsWith (Some portraits)) emptyCast Roster.Akane Dialogue.Sad with
    | Missed reason -> Assert.Equal(Unbound, reason)
    | Resolved _ -> Assert.Fail("an unbound character must not resolve")

[<Fact>]
let ``a bound character whose asset declares no portraits is an expected miss`` () =
    match DialogueRender.tryPortrait (assetsWith None) (castBinding Roster.Akane) Roster.Akane Dialogue.Sad with
    | Missed reason -> Assert.Equal(NoPortraits, reason)
    | Resolved _ -> Assert.Fail("an asset with no portraits must not resolve")

[<Fact>]
let ``a bound character with portraits resolves`` () =
    match DialogueRender.tryPortrait (assetsWith (Some portraits)) (castBinding Roster.Akane) Roster.Akane Dialogue.Sad with
    | Resolved _ -> ()
    | Missed reason -> Assert.Fail(sprintf "expected a portrait, got %A" reason)

// ── The pure draw path allocates nothing per call (FR-010, SC-007) ──
// The same guard `test/ActorTests.fs` puts on `Actor.samplePose`: warm up the JIT, then measure the
// current thread's allocated bytes. Both of these run on every drawn frame, so an allocation here is
// per-frame garbage.

[<Fact>]
let ``layout allocates nothing per call`` () =
    for _ in 1 .. 1000 do layout 1280.0f 720.0f true 120.0f 48.0f |> ignore
    let before = System.GC.GetAllocatedBytesForCurrentThread()
    for _ in 1 .. 10000 do layout 1280.0f 720.0f true 120.0f 48.0f |> ignore
    let after = System.GC.GetAllocatedBytesForCurrentThread()
    Assert.Equal(0L, after - before)

[<Fact>]
let ``tryPortrait allocates nothing per call`` () =
    let assets = assetsWith (Some portraits)
    let cast = castBinding Roster.Akane
    // Both the resolved path and the miss path run every frame a portrait line is shown.
    for _ in 1 .. 1000 do
        DialogueRender.tryPortrait assets cast Roster.Akane Dialogue.Sad |> ignore
        DialogueRender.tryPortrait assets emptyCast Roster.Akane Dialogue.Sad |> ignore
    let before = System.GC.GetAllocatedBytesForCurrentThread()
    for _ in 1 .. 10000 do
        DialogueRender.tryPortrait assets cast Roster.Akane Dialogue.Sad |> ignore
        DialogueRender.tryPortrait assets emptyCast Roster.Akane Dialogue.Sad |> ignore
    let after = System.GC.GetAllocatedBytesForCurrentThread()
    Assert.Equal(0L, after - before)
