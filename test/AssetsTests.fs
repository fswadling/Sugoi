module AssetsTests

open System
open System.IO
open System.Collections.Generic
open Xunit
open Assets
open EnvironmentAsset
open TestSupport

// ── scanning ──

/// The asset kinds whose manifest without a model is reported and skipped.
let private modelLessManifests : (string * string * string) list =
    [ "props", "crate", "[[Id \"crate\"] [Name \"Crate\"]]"
      "environments", "art-room", "[[Id \"art-room\"] [Name \"Art Room\"]]"
      "actors", "aiko", "[[Id \"aiko\"] [Name \"Aiko\"]]" ]

[<Fact>]
let ``scan with no asset directories returns empty collections`` () =
    withTempDir (fun root ->
        let manager = Assets.scan root 0n Audio.unavailable
        Assert.Equal(0, manager.Actors.Count)
        Assert.Equal(0, manager.Props.Count)
        Assert.Equal(0, manager.Environments.Count))

[<Fact>]
let ``scan reports and skips a manifest that names no model, continuing with the rest`` () =
    // spec 080, US7: a bad asset is per-asset, not fatal. The model path is checked before the loader
    // is forced, so this stays device-free for all three kinds.
    withTempDir (fun root ->
        for (kind, stem, text) in modelLessManifests do
            let dir = Path.Combine(root, kind)
            do Directory.CreateDirectory dir |> ignore
            do File.WriteAllText(Path.Combine(dir, stem + ".manifest"), text)
        let before = Diagnostics.errorCount ()
        let manager = Assets.scan root 0n Audio.unavailable
        Assert.Equal(0, manager.Props.Count)
        Assert.Equal(0, manager.Environments.Count)
        Assert.Equal(0, manager.Actors.Count)
        Assert.True(Diagnostics.errorCount () >= before + modelLessManifests.Length))

// ── texture directory (F-04, F-59) ──

[<Fact>]
let ``textureDirectory uses a declared root and otherwise the model directory`` () =
    // The default keeps every existing manifest loading; an explicit directory is used verbatim (it
    // is already root-relative, resolved by the caller).
    Assert.Equal("actors/Akane/akane", ActorAsset.textureDirectory "actors/Akane/akane" "")
    Assert.Equal("textures/shared", ActorAsset.textureDirectory "actors/Akane/akane" "textures/shared")

/// Locate a checked-in asset by walking up from the test output directory.
let private findCheckedIn (relative: string) : string =
    let rec up (dir: DirectoryInfo) (depth: int) : string option =
        if depth > 8 then None
        else
        let candidate = Path.Combine(dir.FullName, relative)
        if File.Exists candidate then Some candidate
        else if dir.Parent = null then None
        else up dir.Parent (depth + 1)
    match up (DirectoryInfo AppContext.BaseDirectory) 0 with
    | Some path -> path
    | None -> failwithf "%s not found relative to the test output directory" relative

[<Fact>]
let ``the shipped Akane manifest declares its texture directory`` () =
    // The manifest field is the author-facing half of F-04: Akane's material names live under
    // `actors/Akane/akane`, not beside the model. Parsing it through the loader needs a device, so the
    // declared value and the pure `textureDirectory` rule are asserted here; the shaded result is a
    // quickstart check.
    let text = File.ReadAllText(findCheckedIn (Path.Combine("assets", "actors", "Akane", "akane.manifest")))
    Assert.Contains("[Textures \"actors/Akane/akane\"]", text)

// ── partial-load rollback (T015) — integration-only exclusion ──
//
// An actor load that fails after acquiring textures or creating a mesh, and a prop build that fails
// partway through `buildSync`/`advanceCursor`, both depend on GPU buffer/transfer creation and the
// texture decoder. With a zero device the first staging map fails, so the rollback path cannot be
// driven headlessly. It is verified by the quickstart's bad-asset scenario; the scan-level behaviour
// (skip and continue) is asserted above.

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
    withTempDir (fun root ->
        let dir = Path.Combine(root, "environments")
        do Directory.CreateDirectory dir |> ignore
        do File.WriteAllText(Path.Combine(dir, "hall.manifest"), "[[Id \"hall\"] [Name \"Hall\"] [Model \"environments/hall/hall.gltf\"]]")
        // The model file deliberately does not exist: a metadata-only scan never reads it.
        let manager = Assets.scan root 0n Audio.unavailable
        Assert.Equal(1, manager.Environments.Count)
        Assert.True(manager.Environments.["hall"].Residency = NotLoaded)
        Assert.Equal(0, Lru.count manager.Cache))

// ── Name caches (F-13) ──

let private audio (id: string) (classOf: AudioAsset.Class) : AudioAsset.AudioAsset =
    { Id = id
      Name = id
      Path = ""
      Class = classOf
      Residency = AudioAsset.Reference }

[<Fact>]
let ``name caches are reused until invalidated, then rebuilt from the dictionaries`` () =
    let manager = managerWith [ "aiko", actorAsset "aiko"; "akane", actorAsset "akane" ] [] []
    manager.AudioAssets.["theme"] <- audio "theme" AudioAsset.Music
    manager.AudioAssets.["beep"] <- audio "beep" AudioAsset.Sound

    // The first call builds; later calls return the same immutable list (the contract's reuse).
    let actors = Assets.actorNames manager
    let music = Assets.musicNames manager
    let sounds = Assets.soundAssets manager
    Assert.Same(actors, Assets.actorNames manager)
    Assert.Same(music, Assets.musicNames manager)
    Assert.Same(sounds, Assets.soundAssets manager)

    // The dictionaries remain the source of truth: a change is invisible until invalidation.
    manager.Actors.["yuki"] <- actorAsset "yuki"
    manager.AudioAssets.["dark"] <- audio "dark" AudioAsset.Music
    Assert.DoesNotContain("yuki", Assets.actorNames manager)
    Assert.DoesNotContain("dark", Assets.musicNames manager)

    Assets.invalidateNameCaches manager
    Assert.Contains("yuki", Assets.actorNames manager)
    Assert.Contains("dark", Assets.musicNames manager)

[<Fact>]
let ``release drops every name cache`` () =
    let manager = emptyManager ()
    do ignore (Assets.actorNames manager)
    do ignore (Assets.musicNames manager)
    do ignore (Assets.soundAssets manager)
    Assert.True(manager.ActorNamesCache.IsSome)
    Assets.release manager
    Assert.True(manager.ActorNamesCache.IsNone)
    Assert.True(manager.MusicNamesCache.IsNone)
    Assert.True(manager.SoundAssetsCache.IsNone)
