module CameraTests

open Xunit
open System.Numerics
open Camera

[<Fact>]
let ``orbit preserves the target distance`` () =
    let c = Camera.create (Vector3(0.0f, 0.0f, 10.0f)) Vector3.Zero
    let before = (c.Position - c.Target).Length()
    let mutable c' = c
    Camera.orbit &c' 0.3f -0.2f
    let after = (c'.Position - c'.Target).Length()
    Assert.True(abs (before - after) < 1e-4f)

[<Fact>]
let ``pan moves position and target together`` () =
    let c = Camera.create (Vector3(1.0f, 2.0f, 3.0f)) Vector3.Zero
    let mutable c' = c
    Camera.pan &c' (Vector3(0.5f, -1.0f, 0.0f))
    Assert.Equal(Vector3(1.5f, 1.0f, 3.0f), c'.Position)
    Assert.Equal(Vector3(0.5f, -1.0f, 0.0f), c'.Target)
