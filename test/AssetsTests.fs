module AssetsTests

open System.IO
open Xunit
open Entity
open Assets

[<Fact>]
let ``scan reads s-syntax manifests and derives kind from directory`` () =
    let tmp = Directory.CreateTempSubdirectory "fsharp-game-assets-"
    try
        Directory.CreateDirectory(Path.Combine(tmp.FullName, "actors")) |> ignore
        Directory.CreateDirectory(Path.Combine(tmp.FullName, "environments")) |> ignore
        Directory.CreateDirectory(Path.Combine(tmp.FullName, "props")) |> ignore

        File.WriteAllText(Path.Combine(tmp.FullName, "actors", "akane.manifest"), "[[Id \"akane\"] [Name \"Akane\"]]")
        File.WriteAllText(Path.Combine(tmp.FullName, "environments", "art-room.manifest"), "[[Id \"art-room\"] [Name \"Art Room\"]]")
        File.WriteAllText(Path.Combine(tmp.FullName, "props", "crate.manifest"), "[[Id \"crate\"] [Name \"Crate\"]]")

        let assets = Assets.scan tmp.FullName |> Array.sortBy (fun a -> a.Id)

        Assert.Equal(3, assets.Length)
        Assert.Equal("akane", assets[0].Id)
        Assert.Equal("Akane", assets[0].Name)
        Assert.Equal(EntityKind.Actor, assets[0].Kind)
        Assert.Equal("art-room", assets[1].Id)
        Assert.Equal("Art Room", assets[1].Name)
        Assert.Equal(EntityKind.Environment, assets[1].Kind)
        Assert.Equal("crate", assets[2].Id)
        Assert.Equal("Crate", assets[2].Name)
        Assert.Equal(EntityKind.Prop, assets[2].Kind)
    finally
        tmp.Delete(recursive = true)

[<Fact>]
let ``scan falls back to file stem when fields are omitted`` () =
    let tmp = Directory.CreateTempSubdirectory "fsharp-game-assets-"
    try
        Directory.CreateDirectory(Path.Combine(tmp.FullName, "props")) |> ignore

        // name present, id omitted -> id falls back to the file stem.
        File.WriteAllText(Path.Combine(tmp.FullName, "props", "barrel.manifest"), "[[Name \"Barrel\"]]")

        let assets = Assets.scan tmp.FullName

        Assert.Equal(1, assets.Length)
        Assert.Equal("barrel", assets[0].Id)
        Assert.Equal("Barrel", assets[0].Name)
        Assert.Equal(EntityKind.Prop, assets[0].Kind)
    finally
        tmp.Delete(recursive = true)
