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
    let actor : Actor = { Base = baseEntity "akane" }
    Assert.Equal("akane", actor.Base.Name)
    Assert.True(actor.Base.Enabled)

[<Fact>]
let ``Environment composes the shared Entity base`` () =
    let environment : Environment = { Base = baseEntity "forest"; Props = SlotMap.create<Prop> 0u }
    Assert.Equal("forest", environment.Base.Name)
    Assert.True(environment.Base.Visible)

[<Fact>]
let ``Prop composes the shared Entity base`` () =
    let prop : Prop = { Base = baseEntity "crate" }
    Assert.Equal("crate", prop.Base.Name)
    Assert.True(prop.Base.Selectable)

[<Fact>]
let ``Waypoint and Trigger are standalone`` () =
    let waypoint : Waypoint = { Position = Vector3(1.0f, 2.0f, 3.0f) }
    let trigger : Trigger = { Position = Vector3.Zero }
    Assert.Equal(1.0f, waypoint.Position.X)
    Assert.Equal(Vector3.Zero, trigger.Position)
