module AssetsTests

open System.IO
open Xunit
open Assets

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
let ``scan raises when a prop manifest has no model path`` () =
    let tmp = Directory.CreateTempSubdirectory "fsharp-game-assets-"
    try
        Directory.CreateDirectory(Path.Combine(tmp.FullName, "props")) |> ignore
        File.WriteAllText(Path.Combine(tmp.FullName, "props", "crate.manifest"), "[[Id \"crate\"] [Name \"Crate\"]]")
        let raised =
            try Assets.scan tmp.FullName 0n |> ignore; false
            with _ -> true
        Assert.True(raised)
    finally
        tmp.Delete(recursive = true)

[<Fact>]
let ``scan raises when an environment manifest has no model path`` () =
    let tmp = Directory.CreateTempSubdirectory "fsharp-game-assets-"
    try
        Directory.CreateDirectory(Path.Combine(tmp.FullName, "environments")) |> ignore
        File.WriteAllText(Path.Combine(tmp.FullName, "environments", "art-room.manifest"), "[[Id \"art-room\"] [Name \"Art Room\"]]")
        let raised =
            try Assets.scan tmp.FullName 0n |> ignore; false
            with _ -> true
        Assert.True(raised)
    finally
        tmp.Delete(recursive = true)
