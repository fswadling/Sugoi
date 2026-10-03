module FadeTests

open Xunit
open System.Numerics
open GameplayScreen

// The fade overlay is pure (spec 059, FR-003): these assert the default, the no-op at 0 and the
// full-view coverage, so the two edges that matter cannot regress without a device. The drawing and the
// panel are the interactive walkthrough.

let private newState () : GameplayState.GameplayState =
    GameplayState.create
        (Unchecked.defaultof<DebugDraw.DebugDraw>)
        (Unchecked.defaultof<DebugDraw.DebugDraw>)
        (Unchecked.defaultof<Ui.Ui>)

[<Fact>]
let ``a fresh gameplay screen has no fade`` () =
    let state = newState ()
    Assert.Equal(0.0f, state.Fade)

[<Fact>]
let ``a fade of zero draws no overlay`` () =
    match fadeOverlay 1280.0f 720.0f 0.0f with
    | ValueNone -> ()
    | ValueSome _ -> Assert.Fail "expected no overlay at level 0"

[<Fact>]
let ``a fade covers the whole view at the level's opacity`` () =
    let cases = [ (1280.0f, 720.0f, 0.25f); (800.0f, 600.0f, 1.0f); (1920.0f, 1080.0f, 0.5f) ]
    for (w, h, level) in cases do
        match fadeOverlay w h level with
        | ValueSome (struct (overlay, colour)) ->
            Assert.Equal(0.0f, overlay.X)
            Assert.Equal(0.0f, overlay.Y)
            Assert.Equal(w, overlay.W)
            Assert.Equal(h, overlay.H)
            Assert.Equal(Vector4(0.0f, 0.0f, 0.0f, level), colour)
        | ValueNone -> Assert.Fail(sprintf "expected an overlay at level %f" level)
