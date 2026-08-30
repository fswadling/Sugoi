module RaycastTests

open Xunit
open System.Numerics
open Camera
open Raycast

let private assertVec3 (expected: Vector3) (actual: Vector3) =
    Assert.True((expected - actual).Length() < 1e-4f, sprintf "expected %A, got %A" expected actual)

[<Fact>]
let ``rayFromScreen points at the camera forward at the centre`` () =
    let camera = Camera.create (Vector3(0.0f, 0.0f, 10.0f)) Vector3.Zero
    let ray = Raycast.rayFromScreen camera 1280.0f 720.0f 640.0f 360.0f
    assertVec3 (Vector3(0.0f, 0.0f, 10.0f)) ray.Origin
    assertVec3 (Vector3(0.0f, 0.0f, -1.0f)) ray.Dir

[<Fact>]
let ``intersectAabb hits a box the ray points at`` () =
    let ray : Ray = { Origin = Vector3(0.0f, 0.0f, 10.0f); Dir = Vector3(0.0f, 0.0f, -1.0f) }
    let hit = Raycast.intersectAabb ray Vector3.Zero (Vector3(0.5f, 0.5f, 0.5f))
    Assert.True(Option.isSome hit)
    let t = Option.get hit
    Assert.True(abs (t - 9.5f) < 1e-4f)

[<Fact>]
let ``intersectAabb misses a box off-axis`` () =
    let ray : Ray = { Origin = Vector3(0.0f, 0.0f, 10.0f); Dir = Vector3(0.0f, 0.0f, -1.0f) }
    let hit = Raycast.intersectAabb ray (Vector3(5.0f, 0.0f, 0.0f)) (Vector3(0.5f, 0.5f, 0.5f))
    Assert.True(Option.isNone hit)

[<Fact>]
let ``planeIntersect returns the hit point`` () =
    let ray : Ray = { Origin = Vector3(0.0f, 0.0f, 10.0f); Dir = Vector3(0.0f, 0.0f, -1.0f) }
    match Raycast.planeIntersect ray Vector3.Zero (Vector3(0.0f, 0.0f, 1.0f)) with
    | Some p -> assertVec3 Vector3.Zero p
    | None -> Assert.True(false, "expected a hit")

[<Fact>]
let ``planeIntersect returns None when behind or parallel`` () =
    let behind : Ray = { Origin = Vector3(0.0f, 0.0f, 10.0f); Dir = Vector3(0.0f, 0.0f, 1.0f) }
    Assert.True(Raycast.planeIntersect behind Vector3.Zero (Vector3(0.0f, 0.0f, 1.0f)) |> Option.isNone)
    let perp : Ray = { Origin = Vector3.Zero; Dir = Vector3(1.0f, 0.0f, 0.0f) }
    Assert.True(Raycast.planeIntersect perp Vector3.Zero (Vector3(0.0f, 0.0f, 1.0f)) |> Option.isNone)

[<Fact>]
let ``raySegmentDistance is zero when the ray passes through the segment`` () =
    let ray : Ray = { Origin = Vector3(0.0f, 10.0f, 0.0f); Dir = Vector3(0.0f, -1.0f, 0.0f) }
    let d = Raycast.raySegmentDistance ray (Vector3(-1.0f, 0.0f, 0.0f)) (Vector3(1.0f, 0.0f, 0.0f))
    Assert.True(d < 1e-4f)
