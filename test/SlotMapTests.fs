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

// ── Index-based accessors (editor CRUD) ──

[<Fact>]
let ``tryGetByIndex returns Some for an alive slot`` () =
    let map0 = SlotMap.create<TestPayload> 4u
    let map1, h = SlotMap.add map0 { Value = 7 }
    let got = SlotMap.tryGetByIndex map1 h.Index
    Assert.Equal(7, got.Value.Value)

[<Fact>]
let ``tryGetByIndex returns None for a dead or out-of-range slot`` () =
    let map0 = SlotMap.create<TestPayload> 2u
    let map1, h = SlotMap.add map0 { Value = 1 }
    let map2 = SlotMap.free map1 h
    Assert.True(SlotMap.tryGetByIndex map2 h.Index |> Option.isNone)
    Assert.True(SlotMap.tryGetByIndex map2 9u |> Option.isNone)

[<Fact>]
let ``setByIndex overwrites an alive slot`` () =
    let map0 = SlotMap.create<TestPayload> 4u
    let map1, h = SlotMap.add map0 { Value = 1 }
    let map2 = SlotMap.setByIndex map1 h.Index { Value = 2 }
    Assert.Equal(2, (SlotMap.tryGetByIndex map2 h.Index).Value.Value)
    Assert.Equal(1u, SlotMap.count map2)

[<Fact>]
let ``setByIndex re-occupies a freed slot at the same index`` () =
    let map0 = SlotMap.create<TestPayload> 2u
    let map1, h = SlotMap.add map0 { Value = 1 }
    let map2 = SlotMap.free map1 h
    let map3 = SlotMap.setByIndex map2 h.Index { Value = 9 }
    Assert.Equal(9, (SlotMap.tryGetByIndex map3 h.Index).Value.Value)
    Assert.Equal(1u, SlotMap.count map3)

[<Fact>]
let ``removeByIndex frees a slot and bumps its generation`` () =
    let map0 = SlotMap.create<TestPayload> 4u
    let map1, h = SlotMap.add map0 { Value = 1 }
    let map2 = SlotMap.removeByIndex map1 h.Index None
    Assert.True(SlotMap.tryGetByIndex map2 h.Index |> Option.isNone)
    Assert.Equal(0u, SlotMap.count map2)
    // re-adding the slot gives a fresh generation, so the old handle is stale
    let map3, h2 = SlotMap.add map2 { Value = 2 }
    Assert.Equal(h.Index, h2.Index)
    Assert.NotEqual(h.Generation, h2.Generation)

[<Fact>]
let ``removeByIndex on an already-dead slot raises`` () =
    let map0 = SlotMap.create<TestPayload> 2u
    let map1, h = SlotMap.add map0 { Value = 1 }
    let map2 = SlotMap.removeByIndex map1 h.Index None
    let mutable raised = false
    try
        SlotMap.removeByIndex map2 h.Index None |> ignore
    with :? System.InvalidOperationException ->
        raised <- true
    Assert.True(raised)

[<Fact>]
let ``removeByIndex runs the cleanup on the removed value`` () =
    let map0 = SlotMap.create<TestPayload> 4u
    let map1, h = SlotMap.add map0 { Value = 7 }
    let mutable cleaned = -1
    let map2 = SlotMap.removeByIndex map1 h.Index (Some (fun v -> cleaned <- v.Value))
    Assert.Equal(7, cleaned)
    Assert.True(SlotMap.tryGetByIndex map2 h.Index |> Option.isNone)
    Assert.Equal(0u, SlotMap.count map2)

[<Fact>]
let ``iteriAlive yields only alive slots with their indices`` () =
    let map0 = SlotMap.create<TestPayload> 4u
    let map1, _ = SlotMap.add map0 { Value = 10 }
    let map2, h1 = SlotMap.add map1 { Value = 20 }
    let map3, _ = SlotMap.add map2 { Value = 30 }
    let map4 = SlotMap.free map3 h1

    let mutable seen : (uint32 * int) list = []
    SlotMap.iteriAlive map4 (fun i v -> seen <- (i, v.Value) :: seen)
    let seen = seen |> List.sortBy fst

    Assert.Equal<uint32 list>([ 0u; 2u ], List.map fst seen)
    Assert.Equal<int list>([ 10; 30 ], List.map snd seen)
