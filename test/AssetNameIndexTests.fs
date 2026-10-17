module AssetNameIndexTests

// The asset manager's actor name index (spec 074): a portrait line's speaker is a plain string, resolved to
// her actor asset by its **declared `Name`**. The index is built once at scan and is pure data, so it is
// asserted here with no device — the manager's GPU fields are never read.

open System
open System.Collections.Generic
open System.IO
open Xunit
open TestSupport

let private index (assets: ActorAsset.ActorAsset list) : Dictionary<string, ActorAsset.ActorAsset> =
    let byId = Dictionary<string, ActorAsset.ActorAsset>()
    for a in assets do byId.[a.Id] <- a
    Assets.indexByName byId

[<Fact>]
let ``a name resolves to its actor asset`` () =
    let byName = index [ { actorAsset "akane" with Name = "Akane" } ]
    Assert.True(byName.ContainsKey "Akane")
    Assert.Equal("akane", byName.["Akane"].Id)

[<Fact>]
let ``the match is exact, so a differently-cased name resolves to nothing`` () =
    let byName = index [ { actorAsset "akane" with Name = "Akane" } ]
    Assert.False(byName.ContainsKey "akane")
    Assert.False(byName.ContainsKey "AKANE")

[<Fact>]
let ``a name two assets share resolves to the lowest id`` () =
    let byName = index [ { actorAsset "z_akane" with Name = "Akane" }; { actorAsset "a_akane" with Name = "Akane" } ]
    Assert.Equal("a_akane", byName.["Akane"].Id)

[<Fact>]
let ``an asset whose name defaults to its id resolves that exact string`` () =
    // The loader defaults `Name` to `Id` when the manifest omits it; the index is exact, so such an
    // asset answers a speaker spelled exactly like its id (and not the capitalised character name).
    let asset = actorAsset "akane"
    Assert.Equal("akane", asset.Name)
    let byName = index [ asset ]
    Assert.Equal("akane", byName.["akane"].Id)
    Assert.False(byName.ContainsKey "Akane")

[<Fact>]
let ``a name that matches no asset resolves to nothing`` () =
    let byName = index [ { actorAsset "akane" with Name = "Akane" } ]
    Assert.False(byName.ContainsKey "Aiko")

[<Fact>]
let ``the discovered names are sorted and de-duplicated`` () =
    let actors = Dictionary<string, ActorAsset.ActorAsset>()
    actors.["z"] <- { actorAsset "z" with Name = "Yuki" }
    actors.["a"] <- { actorAsset "a" with Name = "Aiko" }
    actors.["dup"] <- { actorAsset "dup" with Name = "Aiko" }
    let manager = TestSupport.manager IntPtr.Zero actors (Dictionary()) (Dictionary())
    Assert.Equal<string list>([ "Aiko"; "Yuki" ], Assets.actorNames manager)

[<Fact>]
let ``the loader defaults a manifest's id and name to its file stem`` () =
    // The loader-level half of F-74: `readManifest` supplies `Id` and `Name` before an asset is loaded.
    // An environment manifest is recorded as metadata only, so this is observable without a model file
    // (or a device).
    withTempDir (fun root ->
        let dir = Path.Combine(root, "environments")
        do Directory.CreateDirectory dir |> ignore
        do File.WriteAllText(Path.Combine(dir, "hall.manifest"), "[[Model \"environments/hall/hall.gltf\"]]")

        let manager = Assets.scan root 0n Audio.unavailable
        Assert.Equal(1, manager.Environments.Count)
        let asset = manager.Environments.["hall"]
        Assert.Equal("hall", asset.Id)
        Assert.Equal("hall", asset.Name))
