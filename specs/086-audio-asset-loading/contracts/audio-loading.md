# Contract: Audio asset loading

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Data model**: [data-model.md](./data-model.md)

The surfaces that change, what they guarantee, and what explicitly does not.

## `audio/AudioAsset.fs` — NEW

```fsharp
/// A loaded sound's decoded buffer, owned by the mixer.
type SoundHandle = nativeint

/// A long track referenced on disk, or a short effect held in memory.
type Class =
    | Music
    | Sound

/// The audio manifest's on-disk shape.
[<SymbolicExpansion>]
type Manifest =
    { Id : string; Name : string; File : string; Kind : string }

/// A sound is Resident or Failed; a music track is Reference or Failed.
type Residency =
    | Resident of SoundHandle
    | Reference
    | Failed of Message : string

type AudioAsset =
    { Id : string; Name : string; Path : string; Class : Class; mutable Residency : Residency }

val isResident  : asset:AudioAsset -> bool
val sound       : asset:AudioAsset -> SoundHandle     // invalidOp when not Resident
val isReference : asset:AudioAsset -> bool
```

- `Id`/`Name` defaults and the required-`File`/required-`Kind` rules are the scan's (see `Assets`);
  this module only declares the shape and the resident accessors, mirroring `EnvironmentAsset`.

## `audio/Audio.fs` — NEW

```fsharp
type MixerHandle = nativeint          // opaque MIX_Mixer; 0n when audio is unavailable

type Context =
    { Mixer : MixerHandle
      LoadSound : string -> SoundHandle
      Probe : string -> unit
      FreeSound : SoundHandle -> unit }

val create      : unit -> Context        // SDL audio + MIX_Init + mixer; never raises
val unavailable : Context                // Mixer = 0n; closures raise "audio unavailable"
val release     : context:Context -> unit
```

- **Guarantee (FR-009)**: `create` never raises. If the audio subsystem, `MIX_Init`, or the default
  playback device cannot be brought up, it reports and returns `unavailable`. The game continues.
- **Guarantee (D7)**: `create` initialises the audio subsystem with `SDL_InitSubSystem`, so a missing
  audio backend cannot fail `Engine`'s `SDL.Init`.
- **Guarantee (FR-005)**: `LoadSound` returns a decoded, in-memory buffer (`predecode = true`) or
  raises.
- **Guarantee (FR-006)**: `Probe` opens a decoder and discards it — it validates a track without
  holding decoded audio — or raises.
- **Guarantee (FR-010)**: `FreeSound` releases a resident buffer; `release` destroys the mixer and
  quits SDL_mixer. A context is released only after every sound it produced has been freed.
- The context is a **value**, so `Assets` holds one and the suite injects a fake (research D3).

## `world/Assets.fs` — CHANGED

```fsharp
type AssetManager =
    { …
      AudioAssets : Dictionary<AssetId, AudioAsset>
      Audio : Audio.Context }

/// Scan actors/props/environments AND audio. `audio` is the mixer context the audio pass loads
/// through (a stub in tests; `Audio.create ()` in the app).
val scan : root:string -> device:Gpu.Device -> audio:Audio.Context -> AssetManager

/// Free every resident sound, then release the mixer context, alongside the existing teardown.
val release : manager:AssetManager -> unit

/// Readouts, mirroring `isEnvironmentResident`/`hasFailed`.
val isAudioResident : manager:AssetManager -> assetId:AssetId -> bool
val hasAudioFailed  : manager:AssetManager -> assetId:AssetId -> bool
val audioAsset      : manager:AssetManager -> assetId:AssetId -> AudioAsset
```

- The audio pass is a third `scanDir root "audio"` after the actor/prop/environment passes. For each
  manifest it defaults `Id`/`Name`, resolves `File` to an absolute path, maps `Kind` to `Class`,
  then loads (Sound ⇒ `LoadSound`) or probes (Music ⇒ `Probe`), marking the asset `Resident`/
  `Reference`/`Failed`. A skipped or failed asset never aborts the scan (FR-008).
- `scan` keeps its existing behaviour for actors/props/environments byte-for-byte; only the added
  `audio` parameter and the audio pass are new.
- An **opt-in** `FFS_AUDIO_DEBUG=1` diagnostic reports one line per audio asset (id, class and
  resulting residency), mirroring `FFS_LOAD_TIMING` (spec 053), so the device walkthrough in
  [quickstart.md](./quickstart.md) is observable. Off by default; no cost when unset.
- `release` frees resident sounds (via `context.FreeSound`) **before** `Audio.release manager.Audio`,
  then runs the existing actor/prop/environment/pool teardown.

## `state/SharedState.fs` — CHANGED

```fsharp
let create (gpu: Gpu.Gpu) : SharedState =
    let audio = Audio.create ()
    { Gpu = gpu
      Assets = Assets.scan "assets" gpu.Device audio
      Advents = Advent.empty }
```

- The engine owns the mixer's lifetime through `Assets`: it is created here and released by
  `Assets.release` (research D6). `SharedState` itself gains no field this phase; playback may add
  one later.

## `Engine.fs` / `program` — UNCHANGED

- `Engine.run`'s `SDL.Init(SDL.InitFlags.Video)` is unchanged (research D7); its existing
  `Assets.release manager.Shared.Assets` call now also frees audio and the mixer. `SDL.Quit()` ends
  the audio subsystem.

## `fsharp-game.fsproj` — CHANGED

```xml
<!-- audio (device state, no GPU): consumed by world/, so it sits below it -->
<Compile Include="audio/AudioAsset.fs" />
<Compile Include="audio/Audio.fs" />
```

- Inserted after the `render/` block and before `world/Dialogue.fs` (the first `world/` file).

## `docs/PROJECT_STRUCTURE.md` — CHANGED

- Adds the `audio/` cluster to the layout and the cluster list, and its compile position
  (`render → audio → world`).

## Explicitly NOT changed

- **Playback and everything downstream of it**: mixing, tracks, groups, tags, transport
  (play/stop/pause), volume, looping, crossfade, spatialisation. (Later feature; spec FR-011.)
- **The actor/prop/environment load paths**: eager actor/prop load, deferred environment load, the
  worker pool, the byte budget, the LRU and the texture refcount cache — all unchanged.
- **The draw path, shaders, pipelines, passes, formats, MSAA/depth, the swapchain**: untouched — this
  phase touches no GPU resource.
- **The editor and the Asset Browser**: no audio surface this phase (research D11).
- **The manifest fields for loop count / default volume**: deferred with playback.

## Test surface

- **Device-free** (`test/AudioTests.fs`, a fake `Audio.Context`): classification, id/name defaults
  (and the manifest-stem rule), absolute-path resolution, sound ⇒ `Resident` / music ⇒ `Reference`,
  a raising `LoadSound`/`Probe` ⇒ `Failed` + reported + scan continues, empty/unknown `Kind` and
  empty `File` ⇒ skipped, and `release` freeing each resident handle exactly once. The existing suite
  stays the regression gate once `TestSupport.manager` and the `AssetsTests` `scan` calls gain
  `Audio.unavailable`.
- **Device-dependent**: the real SDL decode/probe and the degraded path are shown in
  [quickstart.md](./quickstart.md).
