module GltfTests

open System
open System.IO
open Xunit
open Gltf

// Locate a checked-in asset by walking up from the test output directory.
let private findAsset (relative: string) : string =
    let rec up (dir: DirectoryInfo) (depth: int) : string option =
        if depth > 8 then None
        else
        let candidate = Path.Combine(dir.FullName, relative)
        if File.Exists(candidate) then Some candidate
        else if dir.Parent = null then None
        else up dir.Parent (depth + 1)
    match up (DirectoryInfo(AppContext.BaseDirectory)) 0 with
    | Some p -> p
    | None -> failwithf "%s not found relative to the test output directory" relative

[<Fact>]
let ``load parses the Island environment`` () =
    let m = Gltf.load (findAsset "assets/environments/Island.gltf")
    Assert.Equal(1, m.Submeshes.Length)
    let sub = m.Submeshes.[0]
    Assert.Equal(65478, sub.Positions.Length / 3)
    Assert.Equal(65478, sub.Normals.Length / 3)
    Assert.Equal(65478, sub.Uvs.Length / 2)
    Assert.True(sub.Indices.Length % 3 = 0)
    // no material -> default white base color
    Assert.Equal(-1, sub.Material)
    Assert.Equal(System.Numerics.Vector4(1.0f, 1.0f, 1.0f, 1.0f), sub.BaseColor)

[<Fact>]
let ``load parses the multi-primitive Beech prop`` () =
    let m = Gltf.load (findAsset "assets/props/Beech.gltf")
    Assert.Equal(2, m.Submeshes.Length)
    let bark = m.Submeshes.[0]
    let leaves = m.Submeshes.[1]
    Assert.Equal(131015, bark.Positions.Length / 3)
    Assert.Equal(230832, leaves.Positions.Length / 3)
    // Bark has an explicit baseColorFactor (black); the leaves have none (white).
    Assert.Equal(System.Numerics.Vector4(0.0f, 0.0f, 0.0f, 1.0f), bark.BaseColor)
    Assert.Equal(System.Numerics.Vector4(1.0f, 1.0f, 1.0f, 1.0f), leaves.BaseColor)

[<Fact>]
let ``load rejects a non-glTF file`` () =
    let tmp = Path.GetTempFileName()
    try
        File.WriteAllText(tmp, "this is not json")
        let failed =
            try Gltf.load tmp |> ignore; false
            with _ -> true
        Assert.True(failed)
    finally
        File.Delete(tmp)
