module InputTests

open Xunit
open Screen

// All Key cases must map to distinct bits so the held/pressed bitmask can't
// confuse two keys.
[<Fact>]
let ``Key.mask assigns a unique bit to every key`` () =
    let keys =
        [ Key.Escape; Key.Tab; Key.Backtick; Key.Delete
          Key.W; Key.A; Key.S; Key.D; Key.Q; Key.E; Key.F; Key.Z
          Key.Digit 1; Key.Digit 2; Key.Digit 3
          Key.LeftCtrl; Key.RightCtrl; Key.LeftShift; Key.RightShift ]

    let masks = keys |> List.map Key.mask
    let distinct = masks |> List.distinct |> List.length
    Assert.Equal(List.length masks, distinct)

[<Fact>]
let ``isCtrl is true when either control key is held`` () =
    let input = { MouseDX = 0.0f; MouseDY = 0.0f; Wheel = 0.0f; RightDown = false
                  Held = Key.mask Key.LeftCtrl; Pressed = KeyMask.zero }
    Assert.True(Input.isCtrl input)

    let input = { input with Held = Key.mask Key.RightCtrl }
    Assert.True(Input.isCtrl input)

    let input = { input with Held = KeyMask.zero }
    Assert.False(Input.isCtrl input)

[<Fact>]
let ``isShift is true when either shift key is held`` () =
    let input = { MouseDX = 0.0f; MouseDY = 0.0f; Wheel = 0.0f; RightDown = false
                  Held = Key.mask Key.LeftShift; Pressed = KeyMask.zero }
    Assert.True(Input.isShift input)

    let input = { input with Held = Key.mask Key.RightShift }
    Assert.True(Input.isShift input)

    let input = { input with Held = KeyMask.zero }
    Assert.False(Input.isShift input)

[<Fact>]
let ``isPressed is edge-triggered via the Pressed bitmask`` () =
    let input = { MouseDX = 0.0f; MouseDY = 0.0f; Wheel = 0.0f; RightDown = false
                  Held = Key.mask Key.Tab; Pressed = Key.mask Key.Tab }
    Assert.True(Input.isPressed Key.Tab input)
    Assert.False(Input.isPressed Key.Escape input)
