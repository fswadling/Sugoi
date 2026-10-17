module EnvironmentBatchTests

// The static instancing batch, asserted headlessly: the count -> start-offset arithmetic and the
// per-submesh identity keying, plus the dirty/gather contract and the buffer growth. A zero device is
// safe for `gather` because the prop/part paths only read render data and the instance buffer's
// `ensure` creates zero handles and allocates the CPU staging array; no GPU command is submitted.

open System
open System.Collections.Generic
open Xunit
open EnvironmentBatch
open EnvironmentAsset
open GameplayState
open TestSupport
open Zone

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

// ── the dirty/gather contract (F-11) ──

[<Fact>]
let ``a clean batch performs no gather and no upload for a thousand frames`` () =
    let _, _, zone = populatedClassroom ()
    // The first gather is the one that builds the scene.
    Assert.True(gatherStatic 0n zone)
    Assert.False(zone.Batch.Dirty)
    let draws = zone.Batch.DrawCalls
    let instances = zone.Batch.InstanceCount

    let mutable gathers = 0
    let mutable uploads = 0
    let frame () =
        if gatherStatic 0n zone then
            gathers <- gathers + 1
            uploads <- uploads + 1 // the caller uploads only when gather returned true

    for _ in 1 .. 1000 do frame ()

    Assert.Equal(0, gathers)
    Assert.Equal(0, uploads)
    Assert.Equal(draws, zone.Batch.DrawCalls)
    Assert.Equal(instances, zone.Batch.InstanceCount)
    Assert.False(zone.Batch.Dirty)

[<Fact>]
let ``a clean gather allocates nothing`` () =
    let _, _, zone = populatedClassroom ()
    Assert.True(gatherStatic 0n zone)
    TestSupport.assertAllocatesNothing 50 1000 (fun () ->
        if gatherStatic 0n zone then failwith "a clean batch gathered")

[<Fact>]
let ``markDirty makes the next gather rebuild`` () =
    let _, _, zone = populatedClassroom ()
    Assert.True(gatherStatic 0n zone)
    markDirty zone.Batch
    Assert.True(gatherStatic 0n zone)
    Assert.False(zone.Batch.Dirty)
    Assert.False(gatherStatic 0n zone)

// ── environment switching (F-05) ──

/// An environment whose single submesh has the given uploaded-buffer identity, so a switch is visible
/// in the batch's lookup.
let private environmentWithMesh (id: string) (vertexBuffer: nativeint) : EnvironmentAsset =
    let submesh =
        { triangleSubmesh () with
            Mesh = { Unchecked.defaultof<StaticMesh.StaticMesh> with VertexBuffer = vertexBuffer } }
    residentAsset id [| submesh |] [| environmentPart "wall" 0 1 |]

[<Fact>]
let ``switching environments resets the batch, so no previous registration survives`` () =
    let assets =
        managerWith [] []
            [ "a", environmentWithMesh "a" 1n
              "b", environmentWithMesh "b" 2n ]
    let state = stateWithZone (Zone.create ())
    let zone = activeZone state

    do loadEnvironment zone state.Physics assets "a" |> ignore
    Assert.True(gatherStatic 0n zone)
    Assert.True(zone.Batch.Lookup.ContainsKey 1n)
    Assert.Equal(1, zone.Batch.Registry.Count)

    do loadEnvironment zone state.Physics assets "b" |> ignore
    // The swap reset the batch (clearing the registry and lookup) before activating the incoming one.
    Assert.True(zone.Batch.Dirty)
    Assert.Equal(0, zone.Batch.Registry.Count)
    Assert.Equal(0, zone.Batch.Lookup.Count)

    Assert.True(gatherStatic 0n zone)
    Assert.Equal(1, zone.Batch.Registry.Count)
    Assert.False(zone.Batch.Lookup.ContainsKey 1n)
    Assert.True(zone.Batch.Lookup.ContainsKey 2n)

[<Fact>]
let ``reset clears every derived count and forces a gather`` () =
    let _, _, zone = populatedClassroom ()
    Assert.True(gatherStatic 0n zone)
    Assert.True(zone.Batch.Registry.Count > 0)
    Assert.True(zone.Batch.DrawCalls > 0)

    reset zone.Batch
    Assert.Equal(0, zone.Batch.Registry.Count)
    Assert.Equal(0, zone.Batch.Lookup.Count)
    Assert.Equal(0, zone.Batch.DrawCalls)
    Assert.Equal(0, zone.Batch.InstanceCount)
    Assert.True(zone.Batch.Dirty)
    Assert.True(gatherStatic 0n zone)

// ── buffer growth (F-25) ──

[<Fact>]
let ``the instance buffer grows geometrically rather than per added prop`` () =
    let assets, state, zone = populatedClassroom ()
    let buffer () = zone.Batch.Instances
    Assert.True(gatherStatic 0n zone)
    // One crate prop plus the room's one part: the first allocation is still at least four.
    Assert.Equal(4, (buffer ()).Capacity)
    Assert.Equal(4 * InstanceBuffer.stride, (buffer ()).Staging.Length)

    // Six instances (five more crates) exceed four, so the buffer doubles to eight.
    for i in 1 .. 4 do
        do ignore (spawnProp zone state.Physics assets (entity (sprintf "crate%d" i)) "crate")
    Assert.True(gatherStatic 0n zone)
    Assert.Equal(8, (buffer ()).Capacity)

    // A seventh instance fits the existing capacity: no reallocation.
    do ignore (spawnProp zone state.Physics assets (entity "crate5") "crate")
    Assert.True(gatherStatic 0n zone)
    Assert.Equal(8, (buffer ()).Capacity)
