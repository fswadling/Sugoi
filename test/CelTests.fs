module CelTests

// The pure logic of the cel actor shading (spec 040): the face-mesh name rule and the idealized
// face-light direction. The shading itself needs a GPU and is the quickstart's interactive
// walkthrough.

open System
open System.Numerics
open Xunit

// ── the face-mesh name rule (FR-007) ──

[<Theory>]
[<InlineData("Face", true)>]
[<InlineData("Body", false)>]
[<InlineData("Hair", false)>]
[<InlineData("face", false)>]
[<InlineData("Face.001", false)>]
[<InlineData("", false)>]
let ``isFaceMeshName matches only the exact name Face`` (name: string) (expected: bool) =
    Assert.Equal(expected, Mesh.isFaceMeshName name)

// ── the idealized face-light direction (FR-007) ──

[<Fact>]
let ``faceLightDir of the identity model is level +Z with the elevation`` () =
    let d = ActorRender.faceLightDir Matrix4x4.Identity
    Assert.True(MathF.Abs(d.X) < 1e-4f)
    Assert.True(MathF.Abs(d.Y - 0.25f) < 1e-4f)
    Assert.True(MathF.Abs(d.Z - 1.0f) < 1e-4f)

[<Fact>]
let ``faceLightDir follows the actor's heading`` () =
    // A quarter turn about Y takes local +Z onto +X.
    let m = Matrix4x4.CreateRotationY(MathF.PI / 2.0f)
    let d = ActorRender.faceLightDir m
    Assert.True(MathF.Abs(d.X - 1.0f) < 1e-4f)
    Assert.True(MathF.Abs(d.Y - 0.25f) < 1e-4f)
    Assert.True(MathF.Abs(d.Z) < 1e-4f)

[<Fact>]
let ``faceLightDir normalizes the horizontal part before the elevation`` () =
    // A non-uniform scale must not change the tilt: the horizontal facing is normalized first.
    let m = Matrix4x4.CreateScale(5.0f, 1.0f, 2.0f)
    let d = ActorRender.faceLightDir m
    Assert.True(MathF.Abs(d.X) < 1e-4f)
    Assert.True(MathF.Abs(d.Y - 0.25f) < 1e-4f)
    Assert.True(MathF.Abs(d.Z - 1.0f) < 1e-4f)
