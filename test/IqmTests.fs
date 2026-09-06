module IqmTests

open System
open System.IO
open Xunit
open Iqm

// Locate the checked-in Akane IQM by walking up from the test output directory.
let private findAkane () : string =
    let rec up (dir: DirectoryInfo) (depth: int) : string option =
        if depth > 8 then None
        else
        let candidate = Path.Combine(dir.FullName, "assets", "actors", "Akane", "Akane.iqm")
        if File.Exists(candidate) then Some candidate
        else if dir.Parent = null then None
        else up dir.Parent (depth + 1)
    match up (DirectoryInfo(AppContext.BaseDirectory)) 0 with
    | Some p -> p
    | None -> failwith "Akane.iqm not found relative to the test output directory"

[<Fact>]
let ``load parses the Akane IQM model`` () =
    let m = Iqm.load (findAkane ())
    Assert.Equal(20005, m.Positions.Length / 3)
    Assert.Equal(29397, m.Triangles.Length / 3)
    Assert.Equal(166, m.Joints.Length)
    Assert.Equal(166, m.Poses.Length)
    Assert.Equal(2, m.Anims.Length)
    Assert.Equal(1063, m.NumFrameChannels)
    Assert.Equal(208 * 1063, m.FrameChannels.Length)
    Assert.True(Option.isSome m.Normals)
    Assert.True(Option.isSome m.Uvs)
    Assert.True(Option.isSome m.BlendIndices)
    Assert.True(Option.isSome m.BlendWeights)

    // Blend indices/weights are raw ubyte arrays (20005 vertices * 4 influences).
    let indices = Option.get m.BlendIndices
    let weights = Option.get m.BlendWeights
    Assert.Equal(20005 * 4, indices.Length)
    Assert.Equal(20005 * 4, weights.Length)

    // The model splits into 13 meshes; each IQM `material` field is a texture
    // name (not an index), naming the albedo PNG for that mesh.
    Assert.Equal(13, m.Meshes.Length)
    Assert.Equal("_10.png", m.Meshes.[0].Material)
    Assert.Equal("_16.png", m.Meshes.[12].Material)
    Assert.Equal("Body", m.Meshes.[0].Name)
    Assert.Equal("Face", m.Meshes.[5].Name)
    Assert.Equal("Hair", m.Meshes.[12].Name)

[<Fact>]
let ``load rejects a non-IQM file`` () =
    let tmp = Path.GetTempFileName()
    try
        File.WriteAllText(tmp, "this is not an iqm file")
        let failed =
            try Iqm.load tmp |> ignore; false
            with _ -> true
        Assert.True(failed)
    finally
        File.Delete(tmp)
