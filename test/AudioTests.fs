module AudioTests

// The audio-load logic (spec 086), asserted headlessly against a fake `Audio.Context`: discovery,
// id/name defaults, absolute paths, the sound-resident / music-reference split, failure marking and
// release bookkeeping — with no audio device opened. The real SDL decode/probe and the degraded path
// are verified by the walkthrough in quickstart.md.

open System
open System.Collections.Generic
open System.IO
open Xunit
open Assets
open Audio
open AudioAsset
open TestSupport

// A recording fake: `load` returns a handle (or raises), `probe` succeeds (or raises); freed handles
// are collected so `release`'s bookkeeping can be asserted.
let private fakeContext (load: string -> SoundHandle) (probe: string -> unit) : Context * ResizeArray<SoundHandle> =
    let freed = ResizeArray<SoundHandle>()
    { Mixer = IntPtr.Zero
      LoadSound = load
      Probe = probe
      FreeSound = fun handle -> freed.Add handle }, freed

// A temp asset root with an `audio/` folder holding the given manifests (file name → text).
let private withAudioRoot (manifests: (string * string) list) (body: string -> unit) : unit =
    withTempDir (fun root ->
        let dir = Directory.CreateDirectory(Path.Combine(root, "audio"))
        for (name, text) in manifests do
            File.WriteAllText(Path.Combine(dir.FullName, name), text)
        body root)

[<Fact>]
let ``a manifest is discovered, id defaulting to the stem and name to the id`` () =
    withAudioRoot [ "door-open.manifest", "[[File \"audio/door-open.wav\"] [Kind \"Sound\"]]" ] (fun root ->
        let context, _ = fakeContext (fun _ -> 7n) (fun _ -> ())
        let manager = Assets.scan root 0n context
        Assert.Equal(1, manager.AudioAssets.Count)
        let asset = manager.AudioAssets.["door-open"]
        Assert.Equal("door-open", asset.Name)
        Assert.True(Path.IsPathRooted asset.Path)
        Assert.EndsWith(Path.Combine("audio", "door-open.wav"), asset.Path))

[<Fact>]
let ``a sound is decoded into memory and reported resident`` () =
    withAudioRoot [ "beep.manifest", "[[Id \"beep\"] [File \"audio/beep.wav\"] [Kind \"Sound\"]]" ] (fun root ->
        let context, _ = fakeContext (fun _ -> 42n) (fun _ -> ())
        let manager = Assets.scan root 0n context
        Assert.True(isAudioResident manager "beep")
        Assert.Equal(42n, sound manager.AudioAssets.["beep"]))

[<Fact>]
let ``a music track is probed, never loaded, and kept as a reference`` () =
    withAudioRoot [ "theme.manifest", "[[Id \"theme\"] [File \"audio/theme.ogg\"] [Kind \"Music\"]]" ] (fun root ->
        let mutable loaded = false
        let context : Context =
            { Mixer = IntPtr.Zero
              LoadSound = fun _ -> (loaded <- true; 0n)
              Probe = fun _ -> ()
              FreeSound = fun _ -> () }
        let manager = Assets.scan root 0n context
        Assert.False loaded
        Assert.True(isReference manager.AudioAssets.["theme"]))

[<Fact>]
let ``a failing sound is marked failed and the scan continues`` () =
    withAudioRoot
        [ "bad.manifest", "[[Id \"bad\"] [File \"audio/bad.wav\"] [Kind \"Sound\"]]"
          "good.manifest", "[[Id \"good\"] [File \"audio/good.wav\"] [Kind \"Sound\"]]" ]
        (fun root ->
            let context, _ =
                fakeContext (fun path -> if path.EndsWith "bad.wav" then failwith "corrupt" else 3n) (fun _ -> ())
            let manager = Assets.scan root 0n context
            Assert.True(hasAudioFailed manager "bad")
            Assert.True(isAudioResident manager "good"))

[<Fact>]
let ``an unavailable context marks every audio asset failed`` () =
    withAudioRoot [ "beep.manifest", "[[Id \"beep\"] [File \"audio/beep.wav\"] [Kind \"Sound\"]]" ] (fun root ->
        let manager = Assets.scan root 0n Audio.unavailable
        Assert.True(hasAudioFailed manager "beep"))

[<Fact>]
let ``a manifest with no source file or an unknown class is skipped`` () =
    withAudioRoot
        [ "nofile.manifest", "[[Id \"nofile\"] [Kind \"Sound\"]]"
          "badkind.manifest", "[[Id \"badkind\"] [File \"audio/x.wav\"] [Kind \"Nope\"]]" ]
        (fun root ->
            let context, _ = fakeContext (fun _ -> 1n) (fun _ -> ())
            let manager = Assets.scan root 0n context
            Assert.Equal(0, manager.AudioAssets.Count))

[<Fact>]
let ``an absent audio folder yields no audio assets`` () =
    withTempDir (fun root ->
        let context, _ = fakeContext (fun _ -> 1n) (fun _ -> ())
        let manager = Assets.scan root 0n context
        Assert.Equal(0, manager.AudioAssets.Count))
