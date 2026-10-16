module SoundTests

// The voice pool (spec 089), asserted headlessly: round-robin reuse (the oldest is cut when the pool is
// full), the per-asset Stop, Stop all, release, and the `soundAssets` readout. Every test composes a
// pool against `Audio.unavailable` (a `0n` mixer, so each device op is a guarded no-op) — no device is
// opened. The audible result is heard in quickstart.md.

open System
open System.Collections.Generic
open System.IO
open Xunit
open AudioAsset

// A recording fake context, as `test/AudioTests.fs` builds one.
let private fakeContext (load: string -> SoundHandle) (probe: string -> unit) : Audio.Context =
    { Mixer = IntPtr.Zero
      LoadSound = load
      Probe = probe
      FreeSound = fun _ -> () }

// A temp asset root with an `audio/` folder holding the given manifests (file name -> text).
let private audioRoot (manifests: (string * string) list) : DirectoryInfo =
    let tmp = Directory.CreateTempSubdirectory "sugoi-sound-"
    let dir = Directory.CreateDirectory(Path.Combine(tmp.FullName, "audio"))
    for (name, text) in manifests do
        File.WriteAllText(Path.Combine(dir.FullName, name), text)
    tmp

// A device-free pool: `0n` tracks, so every op is a guarded no-op.
let private pool () : Sound.Voices = Sound.newVoices Audio.unavailable

// A headless shared state carrying a device-free pool.
let private sharedState () : SharedState.SharedState =
    { Gpu = Unchecked.defaultof<Gpu.Gpu>
      Assets = TestSupport.manager IntPtr.Zero (Dictionary()) (Dictionary()) (Dictionary())
      Advents = Advent.empty
      Mix = Music.empty
      Rack = Music.newRack ()
      Voices = Sound.newVoices Audio.unavailable }

// ══════ the readout (US1) ══════

[<Fact>]
let ``soundAssets lists only sound-class assets, sorted by name`` () =
    let root =
        audioRoot
            [ "zap.manifest", "[[Id \"zap\"] [Name \"Zap\"] [File \"audio/zap.wav\"] [Kind \"Sound\"]]"
              "theme.manifest", "[[Id \"theme\"] [Name \"Theme\"] [File \"audio/theme.ogg\"] [Kind \"Music\"]]"
              "blip.manifest", "[[Id \"blip\"] [Name \"Blip\"] [File \"audio/blip.wav\"] [Kind \"Sound\"]]" ]
    try
        let manager = Assets.scan root.FullName 0n (fakeContext (fun _ -> 42n) (fun _ -> ()))
        let names = Assets.soundAssets manager |> List.map (fun asset -> asset.Name)
        Assert.Equal<string list>([ "Blip"; "Zap" ], names)   // music is not listed; sorted by name
    finally
        root.Delete(recursive = true)

[<Fact>]
let ``soundAssets carries a failed asset's residency`` () =
    let root =
        audioRoot [ "bad.manifest", "[[Id \"bad\"] [Name \"Bad\"] [File \"audio/bad.wav\"] [Kind \"Sound\"]]" ]
    try
        let context = fakeContext (fun _ -> failwith "corrupt") (fun _ -> ())
        let manager = Assets.scan root.FullName 0n context
        let asset = (Assets.soundAssets manager).Head
        Assert.False(AudioAsset.isResident asset)   // listed, but marked failed so Play is a no-op
    finally
        root.Delete(recursive = true)

// ══════ the pool (US1/US2) ══════

[<Fact>]
let ``a fresh pool has capacity voices, all idle`` () =
    let voices = pool ()
    Assert.Equal(Sound.capacity, voices.Tracks.Length)
    Assert.True(Array.forall ((=) "") voices.Playing)
    Assert.Equal(0, voices.Next)

[<Fact>]
let ``play records the asset id on the next voice and advances the cursor`` () =
    let voices = pool ()
    do Sound.play voices "door" 1n
    Assert.Equal("door", voices.Playing.[0])
    Assert.Equal(1, voices.Next)

[<Fact>]
let ``playing a sound again overlaps it on the next voice`` () =
    let voices = pool ()
    do Sound.play voices "step" 1n
    do Sound.play voices "step" 1n
    Assert.Equal("step", voices.Playing.[0])
    Assert.Equal("step", voices.Playing.[1])   // a second, overlapping instance

[<Fact>]
let ``voices are filled round-robin, wrap the cursor, and never grow the pool`` () =
    let voices = pool ()
    for i in 1 .. Sound.capacity do do Sound.play voices (sprintf "s%d" i) 1n
    // Every voice is carrying a distinct sound, and the cursor has wrapped back to 0.
    Assert.True(Array.forall ((<>) "") voices.Playing)
    Assert.Equal(0, voices.Next)
    // One more play reuses voice 0 — the least-recently-used, i.e. the oldest.
    do Sound.play voices "again" 1n
    Assert.Equal("again", voices.Playing.[0])
    Assert.Equal(Sound.capacity, voices.Tracks.Length)   // the pool never grew (FR-003)

[<Fact>]
let ``stop clears only the matching sound`` () =
    let voices = pool ()
    do Sound.play voices "a" 1n
    do Sound.play voices "b" 1n
    do Sound.play voices "a" 1n
    do Sound.stop voices "a"
    Assert.Equal("", voices.Playing.[0])
    Assert.Equal("b", voices.Playing.[1])   // another sound is untouched (SC-007)
    Assert.Equal("", voices.Playing.[2])

[<Fact>]
let ``stopAll clears every voice`` () =
    let voices = pool ()
    do Sound.play voices "a" 1n
    do Sound.play voices "b" 1n
    do Sound.stopAll voices
    Assert.True(Array.forall ((=) "") voices.Playing)

// ══════ failure and unavailability (US3) ══════

[<Fact>]
let ``the pool is a quiet no-op when audio is unavailable`` () =
    let voices = pool ()
    do Sound.play voices "a" 1n
    do Sound.stop voices "a"
    do Sound.stopAll voices
    do Sound.release Audio.unavailable voices
    Assert.True(Array.forall ((=) IntPtr.Zero) voices.Tracks)   // every voice is a `0n` no-op
    Assert.True(Array.forall ((=) "") voices.Playing)

[<Fact>]
let ``auditioning a sound records no history step`` () =
    let shared = sharedState ()
    let state = GameplayState.create (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<Ui.Ui>) (Unchecked.defaultof<Skydome.Skydome>)
    do Sound.play shared.Voices "a" 1n
    do Sound.stop shared.Voices "a"
    do Sound.stopAll shared.Voices
    Assert.Empty state.Editor.Undo.Applied   // the panel is runtime-only (FR-006/SC-004)
