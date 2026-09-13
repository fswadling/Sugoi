module EnvironmentBatchTests

// Pure planner tests for the static instancing batch: the count -> start-offset
// arithmetic and the per-submesh identity keying. The GPU/asset paths (gather,
// upload, draw) need a device, so they are covered by the quickstart scenarios.

open System.Collections.Generic
open Xunit
open EnvironmentBatch

[<Fact>]
let ``planOffsets gives start offsets, total, and non-empty group count`` () =
    let counts = [| 2; 0; 3 |]
    let starts = Array.zeroCreate 3
    let total, groups = planOffsets counts starts 3
    Assert.Equal(0, starts.[0])
    Assert.Equal(2, starts.[1])
    Assert.Equal(2, starts.[2])
    Assert.Equal(5, total)
    Assert.Equal(2, groups)

[<Fact>]
let ``planOffsets handles an empty scene`` () =
    let starts = Array.zeroCreate 0
    let total, groups = planOffsets [||] starts 0
    Assert.Equal(0, total)
    Assert.Equal(0, groups)

[<Fact>]
let ``register keys on submesh identity, not index`` () =
    // Two assets can both use submesh index 0; their uploaded buffers differ, so
    // they must land in different groups, while a repeat identity reuses its slot.
    let lookup = Dictionary<nativeint, int>()
    let assetA = 1000n
    let assetB = 2000n
    Assert.Equal(0, register lookup 0 assetA)
    Assert.Equal(1, register lookup 1 assetB)
    Assert.Equal(0, register lookup 2 assetA)
