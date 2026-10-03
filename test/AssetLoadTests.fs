module AssetLoadTests

// The pure half of async environment loading (spec 053): the pending set's bound, de-duplication,
// recency, pinning and drop rules, the claim-hold invariant, and the pool's concurrent-decode bound.
// Device-free — no asset is decoded here (`LoadPool` takes an injected decode).

open System.Diagnostics
open System.Threading
open Xunit
open AssetLoad

// ── the pending set (FR-005, FR-006, SC-004) ──

[<Fact>]
let ``a repeat request keeps one entry and moves it to the most recent`` () =
    let q = createQueue 2
    do request q "a" "pa" |> ignore
    do request q "b" "pb" |> ignore
    // Requesting "a" again keeps one entry and makes it most recent, so the next overflow drops "b".
    do request q "a" "pa" |> ignore
    let dropped = request q "c" "pc"
    Assert.Equal<string list>([ "b" ], dropped)
    Assert.True(isPending q "a")
    Assert.False(isPending q "b")

[<Fact>]
let ``a burst beyond the capacity drops the least recently requested and keeps the newest`` () =
    let q = createQueue 3
    for i in 1 .. 3 do
        do request q (sprintf "z%d" i) (sprintf "p%d" i) |> ignore
    let dropped = request q "z4" "p4"
    Assert.Equal<string list>([ "z1" ], dropped)
    Assert.True(isPending q "z4")
    Assert.False(isPending q "z1")

[<Fact>]
let ``a pinned id is never dropped`` () =
    let q = createQueue 2
    do setPinned q [ "active" ]
    do request q "active" "pa" |> ignore
    do request q "b" "pb" |> ignore
    let dropped = request q "c" "pc"
    Assert.DoesNotContain("active", dropped)
    Assert.True(isPending q "active")
    Assert.False(isPending q "b")

[<Fact>]
let ``the uploading id is never dropped`` () =
    let q = createQueue 2
    do request q "up" "pu" |> ignore
    do claimNext q |> ignore
    do beginUpload q "up"
    do request q "b" "pb" |> ignore
    let dropped = request q "c" "pc"
    Assert.DoesNotContain("up", dropped)
    Assert.True(isPending q "up")
    Assert.False(isPending q "b")

[<Fact>]
let ``a dropped claimed id stays claimed but is no longer pending`` () =
    let q = createQueue 1
    do request q "a" "pa" |> ignore
    (match claimNext q with
     | Some (id, _) -> Assert.Equal("a", id)
     | None -> failwith "expected a claim")
    Assert.True(isClaimed q "a")
    // A newer request over capacity drops the older, claimed entry: its decode is not cancelled, but
    // its payload will be discarded (FR-005).
    let dropped = request q "b" "pb"
    Assert.Contains("a", dropped)
    Assert.False(isPending q "a")
    Assert.True(isClaimed q "a")

// ── the claim is held until resident (review: no re-decode while uploading) ──

[<Fact>]
let ``a claimed id is not claimed again until it is resident`` () =
    let q = createQueue 4
    do request q "a" "pa" |> ignore
    (match claimNext q with
     | Some (id, _) -> Assert.Equal("a", id)
     | None -> failwith "expected a claim")
    // Decoding, then uploading: the claim is held throughout, so no worker can claim it again.
    Assert.True(isClaimed q "a")
    do beginUpload q "a"
    Assert.Equal<(AssetId * ModelPath) option>(None, claimNext q)
    // Only making it resident releases the claim (and leaves the wanted set).
    do makeResident q "a"
    Assert.False(isClaimed q "a")
    Assert.False(isPending q "a")

// ── the concurrent-decode bound (FR-009) ──

[<Fact>]
let ``the pool never decodes more than its worker count at once`` () =
    use gate = new ManualResetEventSlim(false)
    let sync = obj ()
    let mutable active = 0
    let mutable peak = 0
    // A stub decode that blocks until released, recording how many run at once.
    let decode (id: AssetId) (_: ModelPath) : LoadOutcome =
        lock sync (fun () ->
            active <- active + 1
            if active > peak then peak <- active)
        gate.Wait()
        lock sync (fun () -> active <- active - 1)
        LoadFailed(id, "stub")
    let workers = 2
    let pool = createPool workers 8 decode
    try
        for i in 1 .. workers + 3 do
            do requestLoad pool (sprintf "z%d" i) (sprintf "p%d" i) |> ignore
        let sw = Stopwatch.StartNew()
        let mutable busy = 0
        while busy < workers && sw.ElapsedMilliseconds < 3000L do
            lock sync (fun () -> busy <- active)
            Thread.Sleep 5
        Assert.Equal(workers, peak)
        Assert.True(inFlight pool <= workers)
    finally
        gate.Set()
        stopPool pool
