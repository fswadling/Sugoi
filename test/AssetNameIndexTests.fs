module AssetNameIndexTests

// The asset manager's actor name index (spec 074): a portrait line's speaker is a plain string, resolved to
// her actor asset by its **declared `Name`**. The index is built once at scan and is pure data, so it is
// asserted here with no device — the manager's GPU fields are never read.

open System
open System.Collections.Generic
open Xunit

// An actor asset in the loaded shape, its GPU model defaulted: `indexByName` reads only `Id` and `Name`.
let private asset (id: string) (name: string) : ActorAsset.ActorAsset =
    { Id = id
      Name = name
      ModelPath = ""
      Bounds = Unchecked.defaultof<Gltf.Bounds>
      Mesh = Unchecked.defaultof<Mesh.Mesh>
      Model = Unchecked.defaultof<Actor.ActorModel>
      Morphs = None
      Portraits = None; SwayChains = [||] }

let private index (assets: ActorAsset.ActorAsset list) : Dictionary<string, ActorAsset.ActorAsset> =
    let byId = Dictionary<string, ActorAsset.ActorAsset>()
    for a in assets do byId.[a.Id] <- a
    Assets.indexByName byId

[<Fact>]
let ``a name resolves to its actor asset`` () =
    let byName = index [ asset "akane" "Akane" ]
    Assert.True(byName.ContainsKey "Akane")
    Assert.Equal("akane", byName.["Akane"].Id)

[<Fact>]
let ``the match is exact, so a differently-cased name resolves to nothing`` () =
    let byName = index [ asset "akane" "Akane" ]
    Assert.False(byName.ContainsKey "akane")
    Assert.False(byName.ContainsKey "AKANE")

[<Fact>]
let ``a name two assets share resolves to the lowest id`` () =
    let byName = index [ asset "z_akane" "Akane"; asset "a_akane" "Akane" ]
    Assert.Equal("a_akane", byName.["Akane"].Id)

[<Fact>]
let ``an asset that omits its name resolves by its id`` () =
    // The loader defaults `Name` to `Id`, so such an asset resolves a speaker only if that id is the string.
    let byName = index [ asset "akane" "akane" ]
    Assert.Equal("akane", byName.["akane"].Id)

[<Fact>]
let ``a name that matches no asset resolves to nothing`` () =
    let byName = index [ asset "akane" "Akane" ]
    Assert.False(byName.ContainsKey "Aiko")

[<Fact>]
let ``the discovered names are sorted and de-duplicated`` () =
    let actors = Dictionary<string, ActorAsset.ActorAsset>()
    actors.["z"] <- asset "z" "Yuki"
    actors.["a"] <- asset "a" "Aiko"
    actors.["dup"] <- asset "dup" "Aiko"
    let manager = TestSupport.manager IntPtr.Zero actors (Dictionary()) (Dictionary())
    Assert.Equal<string list>([ "Aiko"; "Yuki" ], Assets.actorNames manager)
