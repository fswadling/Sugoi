module AssetsTests

open System.IO
open Xunit
open Assets

[<Fact>]
let ``scan reads s-syntax manifests into id-keyed collections`` () =
    let tmp = Directory.CreateTempSubdirectory "fsharp-game-assets-"
    try
        Directory.CreateDirectory(Path.Combine(tmp.FullName, "environments")) |> ignore
        Directory.CreateDirectory(Path.Combine(tmp.FullName, "props")) |> ignore

        File.WriteAllText(Path.Combine(tmp.FullName, "environments", "art-room.manifest"), "[[Id \"art-room\"] [Name \"Art Room\"]]")
        File.WriteAllText(Path.Combine(tmp.FullName, "props", "crate.manifest"), "[[Id \"crate\"] [Name \"Crate\"]]")

        let manager = Assets.scan tmp.FullName 0n

        Assert.Equal(0, manager.Actors.Count)

        Assert.Equal(1, manager.Environments.Count)
        Assert.Equal("art-room", manager.Environments.["art-room"].Id)
        Assert.Equal("Art Room", manager.Environments.["art-room"].Name)

        Assert.Equal(1, manager.Props.Count)
        Assert.Equal("crate", manager.Props.["crate"].Id)
        Assert.Equal("Crate", manager.Props.["crate"].Name)
    finally
        tmp.Delete(recursive = true)

[<Fact>]
let ``scan falls back to file stem when fields are omitted`` () =
    let tmp = Directory.CreateTempSubdirectory "fsharp-game-assets-"
    try
        Directory.CreateDirectory(Path.Combine(tmp.FullName, "props")) |> ignore

        // name present, id omitted -> id falls back to the file stem.
        File.WriteAllText(Path.Combine(tmp.FullName, "props", "barrel.manifest"), "[[Name \"Barrel\"]]")

        let manager = Assets.scan tmp.FullName 0n

        Assert.Equal(1, manager.Props.Count)
        Assert.Equal("barrel", manager.Props.["barrel"].Id)
        Assert.Equal("Barrel", manager.Props.["barrel"].Name)
        Assert.Equal("", manager.Props.["barrel"].ModelPath)
    finally
        tmp.Delete(recursive = true)
