module AssetsTests

open System.IO
open System.Collections.Generic
open Xunit
open Assets
open EnvironmentAsset
open TestSupport

[<Fact>]
let ``scan with no asset directories returns empty collections`` () =
    let tmp = Directory.CreateTempSubdirectory "fsharp-game-assets-"
    try
        let manager = Assets.scan tmp.FullName 0n
        Assert.Equal(0, manager.Actors.Count)
        Assert.Equal(0, manager.Props.Count)
        Assert.Equal(0, manager.Environments.Count)
    finally
        tmp.Delete(recursive = true)

[<Fact>]
let ``scan skips a prop manifest with no model path`` () =
    // spec 080, US7: a bad asset is reported and skipped, not fatal — the scan continues.
    let tmp = Directory.CreateTempSubdirectory "fsharp-game-assets-"
    try
        Directory.CreateDirectory(Path.Combine(tmp.FullName, "props")) |> ignore
        File.WriteAllText(Path.Combine(tmp.FullName, "props", "crate.manifest"), "[[Id \"crate\"] [Name \"Crate\"]]")
        let manager = Assets.scan tmp.FullName 0n
        Assert.Equal(0, manager.Props.Count)
    finally
        tmp.Delete(recursive = true)

[<Fact>]
let ``scan skips an environment manifest with no model path`` () =
    let tmp = Directory.CreateTempSubdirectory "fsharp-game-assets-"
    try
        Directory.CreateDirectory(Path.Combine(tmp.FullName, "environments")) |> ignore
        File.WriteAllText(Path.Combine(tmp.FullName, "environments", "art-room.manifest"), "[[Id \"art-room\"] [Name \"Art Room\"]]")
        let manager = Assets.scan tmp.FullName 0n
        Assert.Equal(0, manager.Environments.Count)
    finally
        tmp.Delete(recursive = true)

[<Fact>]
let ``scan skips an actor manifest with no model path`` () =
    // spec 080, US7: the eager actor load is per-asset too — a bad actor is skipped, not fatal. The
    // model path is checked before the loader is forced, so this stays device-free.
    let tmp = Directory.CreateTempSubdirectory "fsharp-game-assets-"
    try
        Directory.CreateDirectory(Path.Combine(tmp.FullName, "actors")) |> ignore
        File.WriteAllText(Path.Combine(tmp.FullName, "actors", "aiko.manifest"), "[[Id \"aiko\"] [Name \"Aiko\"]]")
        let manager = Assets.scan tmp.FullName 0n
        Assert.Equal(0, manager.Actors.Count)
    finally
        tmp.Delete(recursive = true)

// ── Residency decisions (spec 050 FR-001, FR-004, FR-005, SC-001) ──

[<Fact>]
let ``evictionCandidates walks the LRU tail-first, least recently used first`` () =
    // `keys` is head-first; `a` is least recently used, so it is offered for eviction first.
    Assert.Equal<string list>([ "a"; "b"; "c" ], evictionCandidates [ "c"; "b"; "a" ] [])

[<Fact>]
let ``evictionCandidates never offers a protected id`` () =
    Assert.Equal<string list>([ "b" ], evictionCandidates [ "b"; "a" ] [ "a" ])

[<Fact>]
let ``evictionCandidates offers nothing when every candidate is protected`` () =
    Assert.Empty(evictionCandidates [ "a" ] [ "a" ])

// ── Residency reader (spec 050 FR-013, FR-016) ──

[<Fact>]
let ``isEnvironmentResident reflects the loaded unit`` () =
    let resident = residentAsset "room" [||] [||]
    let evicted = { resident with Id = "hall"; Residency = NotLoaded }
    let envs = Dictionary<string, EnvironmentAsset>()
    envs.["room"] <- resident
    envs.["hall"] <- evicted
    let manager = TestSupport.manager 0n (Dictionary()) (Dictionary()) envs
    Assert.True(isEnvironmentResident manager "room")
    Assert.False(isEnvironmentResident manager "hall")

// ── One residency entry per asset id (spec 050 FR-008) ──

[<Fact>]
let ``ensuring the same asset id twice keeps one residency entry`` () =
    let envs = Dictionary<string, EnvironmentAsset>()
    envs.["room"] <- residentAsset "room" [||] [||]
    let manager = TestSupport.manager 0n (Dictionary()) (Dictionary()) envs
    requestEnvironment manager "room"
    requestEnvironment manager "room"
    Assert.Equal(1, Lru.count manager.Cache)

// ── Metadata-only startup (spec 050 FR-010, SC-005) ──

[<Fact>]
let ``scan records an environment without parsing its model`` () =
    let tmp = Directory.CreateTempSubdirectory "fsharp-game-assets-"
    try
        Directory.CreateDirectory(Path.Combine(tmp.FullName, "environments")) |> ignore
        File.WriteAllText(Path.Combine(tmp.FullName, "environments", "hall.manifest"), "[[Id \"hall\"] [Name \"Hall\"] [Model \"environments/hall/hall.gltf\"]]")
        // The model file deliberately does not exist: a metadata-only scan never reads it.
        let manager = Assets.scan tmp.FullName 0n
        Assert.Equal(1, manager.Environments.Count)
        Assert.True(manager.Environments.["hall"].Residency = NotLoaded)
        Assert.Equal(0, Lru.count manager.Cache)
    finally
        tmp.Delete(recursive = true)
