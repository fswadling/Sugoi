module PromptTests

open Xunit

// The prompt's layout is pure (spec 039, FR-007): these assert it is centred and stays on screen, so
// the placement cannot regress without a device. The measuring, the drawing and the font are the
// interactive walkthrough.

// A spread of viewport and measured sizes, including a tall (multi-line, wrapped) prompt.
let private cases : (float32 * float32 * float32 * float32) list =
    [ (1280.0f, 720.0f, 120.0f, 24.0f)
      (800.0f, 600.0f, 300.0f, 26.0f)
      (1920.0f, 1080.0f, 40.0f, 24.0f)
      (640.0f, 480.0f, 500.0f, 72.0f) ]

[<Fact>]
let ``the prompt block is centred horizontally`` () =
    for (viewportW, _, _, _) in cases do
        let r = PromptRender.layout viewportW 720.0f 120.0f 24.0f
        Assert.True(abs (r.X + r.W * 0.5f - viewportW * 0.5f) < 0.5f, sprintf "centred at %f" viewportW)

[<Fact>]
let ``the prompt block is fully on screen`` () =
    for (viewportW, viewportH, textW, textH) in cases do
        let r = PromptRender.layout viewportW viewportH textW textH
        Assert.True(r.X >= 0.0f, "left edge on screen")
        Assert.True(r.X + r.W <= viewportW, "right edge on screen")
        Assert.True(r.Y >= 0.0f, "top edge on screen")
        Assert.True(r.Y + r.H <= viewportH, "bottom edge on screen")

[<Fact>]
let ``the prompt keeps a proportional bottom margin`` () =
    let gap (viewportW: float32) (viewportH: float32) : float32 =
        let r = PromptRender.layout viewportW viewportH 100.0f 24.0f
        viewportH - (r.Y + r.H)
    // The gap is a fixed fraction of the height, so doubling the viewport doubles the gap.
    Assert.True(abs (gap 2560.0f 1440.0f - 2.0f * gap 1280.0f 720.0f) < 0.5f)

[<Fact>]
let ``a tall wrapped block still sits on screen`` () =
    // A three-line prompt at 720p grows upward from its bottom anchor and stays on screen.
    let r = PromptRender.layout 1280.0f 720.0f 400.0f 72.0f
    Assert.True(r.Y >= 0.0f)
    Assert.True(r.Y + r.H <= 720.0f)
