module LightTests

// The pure logic of a zone's authored light (spec 043): the pitch/yaw -> direction derivation and
// the defaults that keep an unedited scene identical to the constant they replace. The render, the
// Inspector's controls and the recorded light need a GPU or a live editor and are the quickstart's
// interactive walkthrough.

open System
open System.Numerics
open Xunit
open Texture

// ── the derivation's convention (FR-008) ──

[<Fact>]
let ``toLight of pitch 0 yaw 0 points along +Z`` () =
    let light = toLight { defaultLight with Pitch = 0.0f; Yaw = 0.0f }
    Assert.True(MathF.Abs(light.Direction.X) < 1e-5f)
    Assert.True(MathF.Abs(light.Direction.Y) < 1e-5f)
    Assert.True(MathF.Abs(light.Direction.Z - 1.0f) < 1e-5f)

[<Fact>]
let ``toLight of pitch 90 points at the zenith`` () =
    let light = toLight { defaultLight with Pitch = 90.0f }
    Assert.True(MathF.Abs(light.Direction.X) < 1e-5f)
    Assert.True(MathF.Abs(light.Direction.Y - 1.0f) < 1e-5f)
    Assert.True(MathF.Abs(light.Direction.Z) < 1e-5f)

[<Fact>]
let ``toLight of yaw 90 points along +X`` () =
    let light = toLight { defaultLight with Pitch = 0.0f; Yaw = 90.0f }
    Assert.True(MathF.Abs(light.Direction.X - 1.0f) < 1e-5f)
    Assert.True(MathF.Abs(light.Direction.Y) < 1e-5f)
    Assert.True(MathF.Abs(light.Direction.Z) < 1e-5f)

[<Fact>]
let ``toLight always yields a unit direction`` () =
    for pitch in [ 0.0f; 10.0f; 30.0f; 54.7356f; 70.0f; 90.0f ] do
        for yaw in [ 0.0f; 45.0f; 120.0f; 200.0f; 359.0f ] do
            let light = toLight { defaultLight with Pitch = pitch; Yaw = yaw }
            Assert.True(MathF.Abs(light.Direction.Length() - 1.0f) < 1e-5f)

// ── the defaults reproduce the replaced constant (FR-004, SC-002) ──

[<Fact>]
let ``the default light reproduces the direction the feature replaced`` () =
    // The hard-coded light was normalize(0.4, 0.8, 0.4); a zone that is never edited must match it.
    let light = toLight defaultLight
    let expected = Vector3.Normalize(Vector3(0.4f, 0.8f, 0.4f))
    Assert.True(MathF.Abs(light.Direction.X - expected.X) < 1e-5f)
    Assert.True(MathF.Abs(light.Direction.Y - expected.Y) < 1e-5f)
    Assert.True(MathF.Abs(light.Direction.Z - expected.Z) < 1e-5f)

[<Fact>]
let ``the default light keeps the replaced colour and ambient`` () =
    let light = toLight defaultLight
    Assert.Equal(Vector3(1.0f, 1.0f, 1.0f), light.Color)
    Assert.Equal(Vector3(0.15f, 0.15f, 0.15f), light.Ambient)

// ── the colour and ambient passthrough (FR-001) ──

[<Fact>]
let ``the authored colour passes through unchanged`` () =
    let light = toLight { defaultLight with Color = Vector3(0.9f, 0.8f, 0.7f) }
    Assert.Equal(Vector3(0.9f, 0.8f, 0.7f), light.Color)

[<Fact>]
let ``a scalar ambient expands to a neutral vector`` () =
    let light = toLight { defaultLight with Ambient = 0.4f }
    Assert.Equal(Vector3(0.4f, 0.4f, 0.4f), light.Ambient)
