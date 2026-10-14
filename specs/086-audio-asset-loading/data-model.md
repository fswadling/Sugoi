# Data Model: Music and sound assets load from the asset manager

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Research**: [research.md](./research.md)

All types below are declared in the new `audio/` cluster unless noted. `AudioAsset.fs` is compiled
before `Audio.fs` (the model precedes the device ops, because `Residency` names `SoundHandle`), and
both precede `world/Assets.fs`.

## `Class` (`audio/AudioAsset.fs`)

```fsharp
/// The one distinction this phase acts on: a long track referenced on disk, or a short effect held
/// in memory.
type Class =
    | Music
    | Sound
```

- Parsed from the manifest's `Kind` string (`"Music"` / `"Sound"`). A missing or unrecognised value
  is an authoring error (research D2).

## `Manifest` (`audio/AudioAsset.fs`) — the on-disk shape

```fsharp
/// The audio manifest's s-syntax shape. Named fields, so the parser is robust to reordering, and an
/// absent field parses to null/the default.
[<SymbolicExpansion>]
type Manifest =
    { Id : string        // defaults to the manifest's file stem
      Name : string      // defaults to Id
      File : string      // the source file, relative to the asset root; required
      Kind : string      // "Music" | "Sound"; required }
```

- `Id`/`Name` are defaulted by the scan exactly as the actor manifest's are.
- `File` resolves against the asset root to an absolute path once, at scan (FR-003).
- `Kind` maps to `Class`; an empty or unknown value is skipped and reported.

Example:

```text
[[Id "town-theme"]  [Name "Town Theme"]  [File "audio/town-theme.ogg"] [Kind "Music"]]
[[Id "door-open"]   [Name "Door Open"]   [File "audio/door-open.wav"]  [Kind "Sound"]]
```

## `Residency` and `AudioAsset` (`audio/AudioAsset.fs`)

```fsharp
/// A loaded sound's decoded buffer, owned by the mixer. Opaque (a MIX_Audio pointer).
type SoundHandle = nativeint

/// The asset's coarse lifecycle, single-writer on the render thread. A sound is Resident or Failed;
/// a music track is Reference or Failed (research D5).
type Residency =
    | Resident of SoundHandle   // a sound decoded into memory (FR-005)
    | Reference                 // music: a probed path on disk, no decoded audio (FR-006)
    | Failed of Message : string

type AudioAsset =
    { Id : string
      Name : string
      Path : string             // absolute; resolved once at scan
      Class : Class
      mutable Residency : Residency }
```

### Accessors

```fsharp
val isResident : asset:AudioAsset -> bool                 // Resident _
val sound     : asset:AudioAsset -> SoundHandle           // matches Resident, else invalidOp
val isReference : asset:AudioAsset -> bool                // Reference
```

- Mirrors `EnvironmentAsset`: the unit is reached only where it must be present; a read over a
  not-resident asset raises rather than returning a default.

## `Audio.Context` (`audio/Audio.fs`) — the injectable device seam

```fsharp
/// The mixer and the operations the asset layer needs. A record of a handle and closures, so `Assets`
/// never names SDL and the suite can inject a fake (research D3).
/// The opaque `MIX_Mixer` handle; `0n` when audio is unavailable. Declared here (the counterpart of
/// `SoundHandle`, which lives in `AudioAsset` because `Residency` names it).
type MixerHandle = nativeint

type Context =
    { Mixer : MixerHandle
      LoadSound : string -> SoundHandle        // decode a sound to memory; raises on failure
      Probe : string -> unit                   // open + discard a decoder; raises on failure
      FreeSound : SoundHandle -> unit }        // free a resident sound

/// Bring up SDL audio + SDL_mixer and a default mixer. Any failure returns `unavailable` (FR-009),
/// reported; it never raises.
val create : unit -> Context

/// The degraded context: `Mixer = 0n` and closures that raise "audio unavailable". Used when the
/// subsystem cannot start and by headless tests.
val unavailable : Context

/// Destroy the mixer and quit SDL_mixer. Called after every sound has been freed.
val release : context:Context -> unit
```

- `create` initialises the audio subsystem itself (`SDL_InitSubSystem Audio`) — never through
  `Engine`'s all-or-nothing `SDL.Init` (research D7).
- `LoadSound` is `Mixer.LoadAudio(mixer, path, predecode = true)`; `Probe` is
  `Mixer.CreateAudioDecoder(path, 0)` + `DestroyAudioDecoder`; `FreeSound` is `Mixer.DestroyAudio`
  (research D4).

## `AssetManager` (`world/Assets.fs`) — CHANGED

Two fields added; the existing fields are unchanged.

```fsharp
type AssetManager =
    { …
      AudioAssets : Dictionary<AssetId, AudioAsset>   // NEW
      Audio : Audio.Context                            // NEW — the mixer, held for load/release }
```

- The audio assets are keyed by id, alongside `Actors`/`Props`/`Environments`.
- The context is held the way `Device` is: the engine creates it, the scan uses it, `release` frees
  sounds through it and then releases it (research D6).

## Lifecycle / state transitions

```text
manifest found
      │
      ├─ File missing/empty  → reported, skipped (not added)
      ├─ Kind missing/unknown → reported, skipped (not added)
      │
      ▼  resolve File → absolute Path, add to AudioAssets
   ┌──┴───────────────┐
   │ Class = Sound    │ Class = Music
   ▼                  ▼
LoadSound(path)     Probe(path)
   │ ok               │ ok
   ▼                  ▼
Resident handle     Reference
   │ fail             │ fail
   ▼                  ▼
Failed "…"          Failed "…"
```

- Loading is eager and runs during `Assets.scan` on the render thread — one pass, no deferred state.
- A degraded context (`Mixer = 0n`) makes every audio asset take the `Failed "audio unavailable"`
  edge; the scan continues and the game runs (FR-009).

## Validation rules

- **VR-001 (id/name)**: `Id` defaults to the manifest file stem, `Name` to `Id` (FR-002).
- **VR-002 (path)**: `File` is required and non-empty; it is resolved to an absolute path once at
  scan. Empty `File` ⇒ authoring error, asset skipped (FR-003).
- **VR-003 (class)**: `Kind` is required and one of `"Music"`/`"Sound"`; anything else ⇒ authoring
  error, asset skipped (FR-004).
- **VR-004 (sound residency)**: a sound ends `Resident` (a decoded buffer) or `Failed`; never a bare
  reference (FR-005).
- **VR-005 (music residency)**: a music track ends `Reference` (probed, no decoded audio) or
  `Failed`; it never holds a decoded buffer (FR-006).
- **VR-006 (failure)**: a missing/unreadable/corrupt/undecodable file ⇒ `Failed msg` + a reported
  diagnostic; the scan continues and other assets load (FR-008).
- **VR-007 (release)**: `Assets.release` frees every resident sound before releasing the mixer; no
  handle outlives the context (FR-010).
