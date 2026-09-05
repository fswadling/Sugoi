module EntityTests

open Xunit
open System.Numerics
open Entity
open SlotMap

let baseEntity name =
    { Name = name
      Transform = Unchecked.defaultof<Transform>
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

[<Fact>]
let ``Actor composes the shared Entity base`` () =
    let actor : Actor = { Base = baseEntity "akane"; AssetId = ""; Renderer = Unchecked.defaultof<ActorRender.ActorRender> }
    Assert.Equal("akane", actor.Base.Name)
    Assert.True(actor.Base.Enabled)

[<Fact>]
let ``Environment composes the shared Entity base`` () =
    let environment : Environment = { Base = baseEntity "forest"; AssetId = ""; Orientation = Quaternion.Identity; Props = SlotMap.create<Prop> 0u }
    Assert.Equal("forest", environment.Base.Name)
    Assert.True(environment.Base.Visible)

[<Fact>]
let ``Prop composes the shared Entity base`` () =
    let prop : Prop = { Base = baseEntity "crate"; AssetId = ""; SubmeshIndex = 0; SubmeshCount = 0; Renderer = Unchecked.defaultof<EnvironmentRender.EnvironmentRender> }
    Assert.Equal("crate", prop.Base.Name)
    Assert.True(prop.Base.Selectable)

[<Fact>]
let ``Waypoint and Trigger are standalone`` () =
    let waypoint : Waypoint = { Position = Vector3(1.0f, 2.0f, 3.0f) }
    let trigger : Trigger = { Position = Vector3.Zero }
    Assert.Equal(1.0f, waypoint.Position.X)
    Assert.Equal(Vector3.Zero, trigger.Position)

[<Fact>]
let ``eulerDegrees of identity is zero`` () =
    let e = eulerDegrees Quaternion.Identity
    Assert.Equal(Vector3.Zero, e)

[<Fact>]
let ``eulerDegrees of a quarter turn about Y is about 90 degrees`` () =
    // 90° is at the gimbal-lock singularity, so float32 round-trip lands within
    // ~0.02° of 90 (not exactly) — assert with a small tolerance.
    let q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, System.MathF.PI / 2.0f)
    let e = eulerDegrees q
    Assert.True(abs e.X < 0.001f)
    Assert.True(abs (e.Y - 90.0f) < 0.1f)
    Assert.True(abs e.Z < 0.001f)
