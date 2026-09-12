module SlotMapTests

open Xunit
open SlotMap

[<Struct>]
type TestPayload =
    { mutable Value : int }

[<Fact>]
let ``add returns a handle and increments count`` () =
    let map = SlotMap<TestPayload>(4u)
    let handle = map.Add({ Value = 1 })
    Assert.Equal(1u, map.Count)
    Assert.Equal(0u, handle.Index)
    Assert.Equal(0u, handle.Generation)

[<Fact>]
let ``get returns a live pointer into the slot`` () =
    let map = SlotMap<TestPayload>(4u)
    let handle = map.Add({ Value = 1 })
    (map.Get(handle)).Value <- 42
    Assert.Equal(42, (map.Get(handle)).Value)

[<Fact>]
let ``free decrements count`` () =
    let map = SlotMap<TestPayload>(2u)
    let handle = map.Add({ Value = 1 })
    map.Free(handle)
    Assert.Equal(0u, map.Count)

[<Fact>]
let ``freed slots are reused without reindexing`` () =
    let map = SlotMap<TestPayload>(2u)
    let h0 = map.Add({ Value = 1 })
    let h1 = map.Add({ Value = 2 })
    Assert.Equal(0u, h0.Index)
    Assert.Equal(1u, h1.Index)
    map.Free(h0)
    let h2 = map.Add({ Value = 3 })
    Assert.Equal(0u, h2.Index)
    Assert.Equal(2u, map.Count)

[<Fact>]
let ``add to a full map raises`` () =
    let map = SlotMap<TestPayload>(1u)
    map.Add({ Value = 1 }) |> ignore
    let mutable raised = false
    try
        map.Add({ Value = 2 }) |> ignore
    with :? System.InvalidOperationException ->
        raised <- true
    Assert.True(raised)

[<Fact>]
let ``get on an out-of-range handle raises`` () =
    let map = SlotMap<TestPayload>(1u)
    map.Add({ Value = 1 }) |> ignore
    let mutable raised = false
    try
        map.Get({ Index = 5u; Generation = 0u }) |> ignore
    with :? System.InvalidOperationException ->
        raised <- true
    Assert.True(raised)

[<Fact>]
let ``get on a stale handle raises`` () =
    let map = SlotMap<TestPayload>(1u)
    let h0 = map.Add({ Value = 1 })
    map.Free(h0)
    let h1 = map.Add({ Value = 2 })
    let mutable raised = false
    try
        map.Get(h0) |> ignore
    with :? System.InvalidOperationException ->
        raised <- true
    Assert.True(raised)
    Assert.Equal(2, (map.Get(h1)).Value)

[<Fact>]
let ``get by index and generation resolves a live slot`` () =
    let map = SlotMap<TestPayload>(2u)
    let h = map.Add({ Value = 7 })
    Assert.Equal(7, (map.Get(h.Index, h.Generation)).Value)

[<Fact>]
let ``get by index and generation raises for a stale or out-of-range slot`` () =
    let map = SlotMap<TestPayload>(2u)
    let h = map.Add({ Value = 7 })
    map.Free(h)

    let mutable staleRaised = false
    try
        map.Get(h.Index, h.Generation) |> ignore
    with :? System.InvalidOperationException ->
        staleRaised <- true

    let mutable rangeRaised = false
    try
        map.Get(9u, 0u) |> ignore
    with :? System.InvalidOperationException ->
        rangeRaised <- true

    Assert.True(staleRaised)
    Assert.True(rangeRaised)

// ── Index-based accessors (editor CRUD) ──

[<Fact>]
let ``IsAlive is true for an alive slot`` () =
    let map = SlotMap<TestPayload>(4u)
    let h = map.Add({ Value = 7 })
    Assert.True(map.IsAlive(h.Index, h.Generation))

[<Fact>]
let ``IsAlive is false for a dead or out-of-range slot`` () =
    let map = SlotMap<TestPayload>(2u)
    let h = map.Add({ Value = 1 })
    map.Free(h)
    Assert.False(map.IsAlive(h.Index, h.Generation))
    Assert.False(map.IsAlive(9u, 0u))

[<Fact>]
let ``TryGet returns the value only for a live slot with the given generation`` () =
    let map = SlotMap<TestPayload>(2u)
    let h = map.Add({ Value = 7 })
    Assert.Equal(7, (map.TryGet(h.Index, h.Generation)).Value.Value)
    map.Free(h)
    Assert.True(map.TryGet(h.Index, h.Generation) |> Option.isNone)
    Assert.True(map.TryGet(9u, 0u) |> Option.isNone)

[<Fact>]
let ``Get by index and generation returns a live pointer into the slot`` () =
    let map = SlotMap<TestPayload>(4u)
    let h = map.Add({ Value = 1 })
    let p = &map.Get(h.Index, h.Generation)
    p.Value <- 9
    Assert.Equal(9, (map.Get(h)).Value)

[<Fact>]
let ``assigning through Get overwrites an alive slot`` () =
    let map = SlotMap<TestPayload>(4u)
    let h = map.Add({ Value = 1 })
    map.Get(h.Index, h.Generation) <- { Value = 2 }
    Assert.Equal(2, (map.Get(h.Index, h.Generation)).Value)
    Assert.Equal(1u, map.Count)

[<Fact>]
let ``IsAlive rejects a slot whose generation has advanced`` () =
    let map = SlotMap<TestPayload>(2u)
    let h = map.Add({ Value = 1 })
    map.Free(h)
    let h2 = map.Add({ Value = 2 })
    Assert.True(map.IsAlive(h2.Index, h2.Generation))
    Assert.False(map.IsAlive(h.Index, h.Generation))

[<Fact>]
let ``IsOccupied tracks any live slot regardless of generation`` () =
    let map = SlotMap<TestPayload>(2u)
    let h = map.Add({ Value = 1 })
    Assert.True(map.IsOccupied(h.Index))
    map.Free(h)
    Assert.False(map.IsOccupied(h.Index))
    Assert.False(map.IsOccupied(9u))

[<Fact>]
let ``IsOccupied stays true when a slot is recycled at a new generation`` () =
    let map = SlotMap<TestPayload>(2u)
    let h = map.Add({ Value = 1 })
    map.Free(h)
    let h2 = map.Add({ Value = 2 })
    // Occupied, but not alive at the original generation — the state that makes a
    // restore of the original ref a bug rather than a no-op.
    Assert.True(map.IsOccupied(h.Index))
    Assert.False(map.IsAlive(h.Index, h.Generation))
    Assert.True(map.IsAlive(h2.Index, h2.Generation))

[<Fact>]
let ``Occupy re-occupies a freed slot at the same index`` () =
    let map = SlotMap<TestPayload>(2u)
    let h = map.Add({ Value = 1 })
    map.Free(h)
    map.Occupy(h.Index, h.Generation, { Value = 9 })
    Assert.Equal(9, (map.Get(h.Index, h.Generation)).Value)
    Assert.Equal(1u, map.Count)

[<Fact>]
let ``Occupy restores the recorded generation so the old handle resolves`` () =
    let map = SlotMap<TestPayload>(2u)
    let h = map.Add({ Value = 1 })
    map.Free(h)
    // Free bumped the slot's generation; re-occupying writes the handle's back.
    map.Occupy(h.Index, h.Generation, { Value = 9 })
    Assert.True(map.IsAlive(h.Index, h.Generation))
    Assert.Equal(9, (map.Get(h)).Value)

[<Fact>]
let ``Occupy restores a generation after a slot was recycled and freed again`` () =
    let map = SlotMap<TestPayload>(2u)
    let h0 = map.Add({ Value = 1 })
    map.Free(h0)
    let h1 = map.Add({ Value = 2 })
    map.Free(h1)
    // The slot is free again after the recycle; the original generation restores.
    map.Occupy(h0.Index, h0.Generation, { Value = 9 })
    Assert.True(map.IsAlive(h0.Index, h0.Generation))
    Assert.Equal(9, (map.Get(h0)).Value)

[<Fact>]
let ``Occupy on a slot recycled by another entity raises`` () =
    let map = SlotMap<TestPayload>(2u)
    let h0 = map.Add({ Value = 1 })
    map.Free(h0)
    let h1 = map.Add({ Value = 2 })
    let mutable raised = false
    try
        map.Occupy(h0.Index, h0.Generation, { Value = 9 })
    with :? System.InvalidOperationException ->
        raised <- true
    Assert.True(raised)
    Assert.Equal(2, (map.Get(h1)).Value)

[<Fact>]
let ``Occupy on an already-alive slot raises and leaves it intact`` () =
    let map = SlotMap<TestPayload>(2u)
    let h = map.Add({ Value = 1 })
    let mutable raised = false
    try
        map.Occupy(h.Index, h.Generation, { Value = 2 })
    with :? System.InvalidOperationException ->
        raised <- true
    Assert.True(raised)
    Assert.Equal(1, (map.Get(h.Index, h.Generation)).Value)

[<Fact>]
let ``removeByIndex frees a slot and bumps its generation`` () =
    let map = SlotMap<TestPayload>(4u)
    let h = map.Add({ Value = 1 })
    map.RemoveByIndex(h.Index, h.Generation, None)
    Assert.False(map.IsAlive(h.Index, h.Generation))
    Assert.Equal(0u, map.Count)
    // re-adding the slot gives a fresh generation, so the old handle is stale
    let h2 = map.Add({ Value = 2 })
    Assert.Equal(h.Index, h2.Index)
    Assert.NotEqual(h.Generation, h2.Generation)

[<Fact>]
let ``removeByIndex on an already-dead slot raises`` () =
    let map = SlotMap<TestPayload>(2u)
    let h = map.Add({ Value = 1 })
    map.RemoveByIndex(h.Index, h.Generation, None)
    let mutable raised = false
    try
        map.RemoveByIndex(h.Index, h.Generation, None)
    with :? System.InvalidOperationException ->
        raised <- true
    Assert.True(raised)

[<Fact>]
let ``removeByIndex runs the cleanup on the removed value`` () =
    let map = SlotMap<TestPayload>(4u)
    let h = map.Add({ Value = 7 })
    let mutable cleaned = -1
    map.RemoveByIndex(h.Index, h.Generation, Some (fun v -> cleaned <- v.Value))
    Assert.Equal(7, cleaned)
    Assert.False(map.IsAlive(h.Index, h.Generation))
    Assert.Equal(0u, map.Count)

[<Fact>]
let ``iteriAlive yields only alive slots with their indices`` () =
    let map = SlotMap<TestPayload>(4u)
    map.Add({ Value = 10 }) |> ignore
    let h1 = map.Add({ Value = 20 })
    map.Add({ Value = 30 }) |> ignore
    map.Free(h1)

    let mutable seen : (uint32 * int) list = []
    map.IteriAlive(fun i _ v -> seen <- (i, v.Value) :: seen)
    let seen = seen |> List.sortBy fst

    Assert.Equal<uint32 list>([ 0u; 2u ], List.map fst seen)
    Assert.Equal<int list>([ 10; 30 ], List.map snd seen)
