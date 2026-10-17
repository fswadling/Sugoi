module InputTests

open System
open Xunit
open Input

// All Key cases must map to distinct bits so the held/pressed bitmask can't
// confuse two keys.
[<Fact>]
let ``Key.mask assigns a unique bit to every key`` () =
    let keys =
        [ Key.Escape; Key.Tab; Key.Backtick; Key.Delete
          Key.W; Key.A; Key.S; Key.D; Key.Q; Key.E; Key.F; Key.Z; Key.Y; Key.C
          Key.Digit 1; Key.Digit 2; Key.Digit 3
          Key.LeftCtrl; Key.RightCtrl; Key.LeftShift; Key.RightShift ]

    let masks = keys |> List.map Key.mask
    let distinct = masks |> List.distinct |> List.length
    Assert.Equal(List.length masks, distinct)

[<Fact>]
let ``Key.Digit outside 1..3 raises rather than colliding with another key's bit`` () =
    // The digit keys occupy bits 13..15. A digit outside that window would alias one of them (or be
    // shifted out of the mask), so it is rejected at the abstraction rather than silently colliding.
    for digit in [ 0; 4; 10 ] do
        Assert.Throws<ArgumentException>(fun () -> Key.mask (Key.Digit digit) |> ignore) |> ignore

[<Fact>]
let ``isCtrl is true when either control key is held`` () =
    let input = { MouseX = 0.0f; MouseY = 0.0f; MouseDX = 0.0f; MouseDY = 0.0f; LeftDown = false; LeftPressed = false; LeftReleased = false; RightDown = false
                  Held = Key.mask Key.LeftCtrl; Pressed = KeyMask.zero }
    Assert.True(Input.isCtrl input)

    let input = { input with Held = Key.mask Key.RightCtrl }
    Assert.True(Input.isCtrl input)

    let input = { input with Held = KeyMask.zero }
    Assert.False(Input.isCtrl input)

[<Fact>]
let ``isShift is true when either shift key is held`` () =
    let input = { MouseX = 0.0f; MouseY = 0.0f; MouseDX = 0.0f; MouseDY = 0.0f; LeftDown = false; LeftPressed = false; LeftReleased = false; RightDown = false
                  Held = Key.mask Key.LeftShift; Pressed = KeyMask.zero }
    Assert.True(Input.isShift input)

    let input = { input with Held = Key.mask Key.RightShift }
    Assert.True(Input.isShift input)

    let input = { input with Held = KeyMask.zero }
    Assert.False(Input.isShift input)

[<Fact>]
let ``isPressed is edge-triggered via the Pressed bitmask`` () =
    let input = { MouseX = 0.0f; MouseY = 0.0f; MouseDX = 0.0f; MouseDY = 0.0f; LeftDown = false; LeftPressed = false; LeftReleased = false; RightDown = false
                  Held = Key.mask Key.Tab; Pressed = Key.mask Key.Tab }
    Assert.True(Input.isPressed Key.Tab input)
    Assert.False(Input.isPressed Key.Escape input)
