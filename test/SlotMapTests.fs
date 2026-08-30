module SlotMapTests

open Xunit
open SlotMap

[<Struct>]
type TestPayload =
    { mutable Value : int }

[<Fact>]
let ``add returns a handle and increments count`` () =
    let map0 = SlotMap.create<TestPayload> 4u
    let map1, handle = SlotMap.add map0 { Value = 1 }
    Assert.Equal(1u, SlotMap.count map1)
    Assert.Equal(0u, handle.Index)
    Assert.Equal(0u, handle.Generation)

[<Fact>]
let ``get returns the stored payload`` () =
    let map0 = SlotMap.create<TestPayload> 4u
    let map1, handle = SlotMap.add map0 { Value = 42 }
    Assert.Equal(42, (SlotMap.get map1 handle).Value)

[<Fact>]
let ``free decrements count`` () =
    let map0 = SlotMap.create<TestPayload> 2u
    let map1, handle = SlotMap.add map0 { Value = 1 }
    let map2 = SlotMap.free map1 handle
    Assert.Equal(0u, SlotMap.count map2)

[<Fact>]
let ``freed slots are reused without reindexing`` () =
    let map0 = SlotMap.create<TestPayload> 2u
    let map1, h0 = SlotMap.add map0 { Value = 1 }
    let map2, h1 = SlotMap.add map1 { Value = 2 }
    Assert.Equal(0u, h0.Index)
    Assert.Equal(1u, h1.Index)
    let map3 = SlotMap.free map2 h0
    let map4, h2 = SlotMap.add map3 { Value = 3 }
    Assert.Equal(0u, h2.Index)
    Assert.Equal(2u, SlotMap.count map4)

[<Fact>]
let ``add to a full map raises`` () =
    let map0 = SlotMap.create<TestPayload> 1u
    let map1, _ = SlotMap.add map0 { Value = 1 }
    let mutable raised = false
    try
        SlotMap.add map1 { Value = 2 } |> ignore
    with :? System.InvalidOperationException ->
        raised <- true
    Assert.True(raised)

[<Fact>]
let ``get on an out-of-range handle raises`` () =
    let map0 = SlotMap.create<TestPayload> 1u
    let map1, _ = SlotMap.add map0 { Value = 1 }
    let mutable raised = false
    try
        SlotMap.get map1 { Index = 5u; Generation = 0u } |> ignore
    with :? System.InvalidOperationException ->
        raised <- true
    Assert.True(raised)

[<Fact>]
let ``get on a stale handle raises`` () =
    let map0 = SlotMap.create<TestPayload> 1u
    let map1, h0 = SlotMap.add map0 { Value = 1 }
    let map2 = SlotMap.free map1 h0
    let map3, h1 = SlotMap.add map2 { Value = 2 }
    let mutable raised = false
    try
        SlotMap.get map3 h0 |> ignore
    with :? System.InvalidOperationException ->
        raised <- true
    Assert.True(raised)
    Assert.Equal(2, (SlotMap.get map3 h1).Value)
