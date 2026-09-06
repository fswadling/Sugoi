module GltfTests

open System
open System.IO
open System.Numerics
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
    let m = Gltf.load (findAsset "assets/environments/Island/Island.gltf")
    Assert.Equal(1, m.Submeshes.Length)
    let sub = m.Submeshes.[0]
    Assert.Equal(65478, sub.Positions.Length / 3)
    Assert.Equal(65478, sub.Normals.Length / 3)
    Assert.Equal(65478, sub.Uvs.Length / 2)
    Assert.True(sub.Indices.Length % 3 = 0)
    // no material -> the submesh references none; no materials are defined.
    Assert.Equal(-1, sub.Material)
    Assert.Empty(m.Materials)

[<Fact>]
let ``load parses the multi-primitive Beech prop`` () =
    let m = Gltf.load (findAsset "assets/props/Beech/Beech.gltf")
    Assert.Equal(2, m.Submeshes.Length)
    let bark = m.Submeshes.[0]
    let leaves = m.Submeshes.[1]
    Assert.Equal(131015, bark.Positions.Length / 3)
    Assert.Equal(230832, leaves.Positions.Length / 3)
    // Bark has an explicit baseColorFactor (black) and an emissive texture; the
    // leaves have no factor (white), a normal map, an albedo map, and a
    // metallic-roughness map.
    Assert.Equal(2, m.Materials.Length)
    Assert.Equal(0, bark.Material)
    Assert.Equal(1, leaves.Material)
    let barkMat = m.Materials.[0]
    let leavesMat = m.Materials.[1]
    Assert.Equal(System.Numerics.Vector4(0.0f, 0.0f, 0.0f, 1.0f), barkMat.BaseColor)
    Assert.Equal(System.Numerics.Vector4(1.0f, 1.0f, 1.0f, 1.0f), leavesMat.BaseColor)
    Assert.Equal(1.0f, barkMat.Metallic)   // default metallicFactor
    Assert.Equal(1.0f, barkMat.Roughness)  // default roughnessFactor
    Assert.Equal(0.0f, leavesMat.Metallic) // explicit metallicFactor
    Assert.Equal(Some { Uri = "textures/SingleBeechNormal.png" }, leavesMat.NormalTexture)
    Assert.Equal(Some { Uri = "textures/SingleBeechColour-SingleBeechOpacity2.png" }, leavesMat.AlbedoTexture)
    Assert.Equal(Some { Uri = "textures/SingleBeechRoughness.png" }, leavesMat.MetallicRoughnessTexture)
    Assert.Equal(None, leavesMat.EmissiveTexture)
    Assert.Equal(Some { Uri = "textures/Bark001_2K-JPG_Color.jpg" }, barkMat.EmissiveTexture)
    Assert.Equal(None, barkMat.AlbedoTexture)

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

[<Fact>]
let ``load rejects a data-URI embedded image`` () =
    let tmp = Directory.CreateTempSubdirectory "fsharp-game-gltf-"
    try
        let json = """{"images": [{"uri": "data:image/png;base64,AAAA"}]}"""
        let gltfPath = Path.Combine(tmp.FullName, "test.gltf")
        File.WriteAllText(gltfPath, json)
        let failed =
            try Gltf.load gltfPath |> ignore; false
            with _ -> true
        Assert.True(failed)
    finally
        tmp.Delete(recursive = true)

[<Fact>]
let ``load emits one part per mesh node with a world transform and submesh range`` () =
    let m = Gltf.load (findAsset "assets/environments/Island/Island.gltf")
    Assert.Equal(1, m.Parts.Length)
    let part = m.Parts.[0]
    Assert.Equal(0, part.SubmeshIndex)
    Assert.Equal(1, part.SubmeshCount)
    // The island node's uniform scale (~32.496) lives in the part transform; the
    // submesh positions stay local (un-scaled unit plane).
    let sx = MathF.Sqrt(part.World.M11 * part.World.M11 + part.World.M12 * part.World.M12 + part.World.M13 * part.World.M13)
    Assert.True(abs (sx - 32.496166f) < 0.001f)
    let sub = m.Submeshes.[0]
    let mutable maxAbs = 0.0f
    for i in 0 .. sub.Positions.Length - 1 do
        maxAbs <- max maxAbs (abs sub.Positions.[i])
    Assert.True(maxAbs <= 1.0f + 0.001f)

[<Fact>]
let ``load spans a multi-primitive mesh as one part`` () =
    let m = Gltf.load (findAsset "assets/props/Beech/Beech.gltf")
    Assert.Equal(1, m.Parts.Length)
    Assert.Equal(0, m.Parts.[0].SubmeshIndex)
    Assert.Equal(2, m.Parts.[0].SubmeshCount)
    Assert.Equal(Matrix4x4.Identity, m.Parts.[0].World)

[<Fact>]
let ``load composes parent and child node transforms in row-vector order`` () =
    let tmp = Directory.CreateTempSubdirectory "fsharp-game-gltf-"
    try
        // Parent: rotate 90° about Y then translate (10,0,0). Child: translate
        // (0,0,5) in parent space -> world (15,0,0).
        let bin =
            use ms = new MemoryStream()
            use bw = new BinaryWriter(ms)
            for v in [| 0.0f; 0.0f; 0.0f; 1.0f; 0.0f; 0.0f; 0.0f; 1.0f; 0.0f |] do
                bw.Write(v)
            ms.ToArray()
        File.WriteAllBytes(Path.Combine(tmp.FullName, "test.bin"), bin)
        let json =
            """{
              "asset": {"version": "2.0"},
              "scene": 0,
              "scenes": [{"nodes": [0]}],
              "nodes": [
                {"name": "parent", "children": [1], "rotation": [0, 0.7071067811865476, 0, 0.7071067811865476], "translation": [10, 0, 0]},
                {"name": "child", "mesh": 0, "translation": [0, 0, 5]}
              ],
              "meshes": [{"name": "tri", "primitives": [{"attributes": {"POSITION": 0}}]}],
              "accessors": [{"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3"}],
              "bufferViews": [{"buffer": 0, "byteLength": 36, "byteOffset": 0}],
              "buffers": [{"byteLength": 36, "uri": "test.bin"}]
            }"""
        let gltfPath = Path.Combine(tmp.FullName, "test.gltf")
        File.WriteAllText(gltfPath, json)

        let m = Gltf.load gltfPath
        Assert.Equal(1, m.Parts.Length)
        let child = m.Parts.[0]
        Assert.Equal("child", child.Name)
        let mutable s = Vector3.Zero
        let mutable q = Quaternion.Identity
        let mutable t = Vector3.Zero
        Matrix4x4.Decompose(child.World, &s, &q, &t) |> ignore
        Assert.True(Vector3.Distance(t, Vector3(15.0f, 0.0f, 0.0f)) < 0.001f)
    finally
        tmp.Delete(recursive = true)
