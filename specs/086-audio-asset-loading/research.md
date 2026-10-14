# Phase 0 Research: Music and sound assets load from the asset manager

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

## Context

The engine loads assets through one manager, `world/Assets.fs`: it scans a folder per kind for
s-syntax manifests (`scanDir`/`readManifest`), loads **actors** and **props** eagerly at scan, and
defers **environments** (spec 050/053) behind a worker pool and a byte budget. SDL3_mixer is now
wired in: the managed `SDL3.Mixer` bindings ship with the referenced `SDL3-CS` 3.4.14.1 package, the
native `libSDL3_mixer.so` is vendored in `native/` with a copy target, and a spike confirmed the
library resolves, reports **15 decoders** (Vorbis, Opus, FLAC, WAV, WavPack, module/MIDI, …), and
loads and plays both a resident buffer and a streaming one.

Audio is the next kind. This phase settles how the manager discovers and holds it.

## Decision 1 — Audio is a new `audio/` cluster, compiled after `render/` and before `world/`

- **Decision**: two new modules, `audio/AudioAsset.fs` then `audio/Audio.fs`, listed in the
  `.fsproj` after `render/` and before `world/`. `world/Assets.fs` consumes them.
- **Rationale**: audio touches no GPU (so it is not `render/`) and is device state consumed *by* the
  game domain (so it is not `world/`). Placing it below `world/` keeps the dependency direction
  strictly downward (`world → audio → core`), and the compile order makes that a compile error
  rather than a convention (AGENTS.md).
- **Alternatives considered**: **inside `render/`** — it holds no GPU resources and a worker/audio
  module there would falsely imply a render dependency; **inside `world/`** — the asset *record*
  could live there, but the mixer + SDL calls are engine/device concern, and `world/` must not own
  the device.

## Decision 2 — A per-asset manifest, and the class is its `Kind` field

- **Decision**: `assets/audio/<id>.manifest` with `[[Id …] [Name …] [File …] [Kind "Music"|"Sound"]]`,
  parsed with `scvalue<AudioAsset.Manifest>` exactly as the actor manifest is. `Id` defaults to the
  manifest's file stem; `Name` defaults to `Id`; `File` is required; `Kind` is required and parsed
  to the `Class` DU.
- **Rationale**: matches every other kind (reading a manifest, id/name defaults) and gives a home
  for the class and for future metadata (loop count, default volume) without a second mechanism.
  The class cannot be inferred safely from a bare file, and the wrong default (decoding a 35 MB
  track into memory, or streaming an effect) is a real cost — so `Kind` is required and an absent or
  unknown value is an authoring error (skipped + reported), consistent with a prop/environment with
  no model path.
- **Alternatives considered**: **bare files with the stem as id** — no place for the class or future
  metadata; **kind subfolders** — encodes the class but still leaves no home for loop/volume.

## Decision 3 — The mixer is an injectable `Audio.Context`; failure yields an "unavailable" context

- **Decision**: `Audio.Context` is a small record — the `MIX_Mixer` handle plus `LoadSound`,
  `Probe` and `FreeSound` closures — created by `Audio.create ()`. `Audio.unavailable` is a context
  whose closures raise, used when the subsystem cannot start and by headless tests. `Assets.scan`
  takes the context.
- **Rationale**: it keeps `Assets` free of SDL, makes the audio load logic (classify, residency,
  failure, release) assertable with **no device** — the spec-053 injectable-decode precedent — and
  lets the engine pass the same mixer to playback later. `Audio.create` returning a *value* even on
  failure (rather than raising) is what makes FR-009 (run with audio disabled) fall out naturally.
- **Alternatives considered**: **`Assets` creates and owns the mixer directly** — untestable
  headlessly and mixes device state into the asset layer; **a global singleton** — breaks the
  headless suite and hides the lifetime.

## Decision 4 — Sounds pre-decode into memory; music is probed, not decoded

- **Decision**: a sound is loaded with `Mixer.LoadAudio(mixer, path, predecode = true)` — the whole
  file decoded into a resident buffer. A music track is **probed** with `Mixer.CreateAudioDecoder(path, 0)`
  followed by `DestroyAudioDecoder` — the file is opened and validated, no sample data is held.
- **Rationale**: `predecode = true` is exactly "resident in memory" (FR-005); `predecode = false`
  would create a streaming handle, but a streaming handle is only meaningful while playing, so it
  belongs to the playback feature (D11). Probing a music file makes FR-008's failure rule apply
  uniformly — a corrupt or unsupported track fails at startup, not at first play — and the spike
  confirmed `MIX_CreateAudioDecoder` opens a file without a mixer.
- **Alternatives considered**: **streaming handle at load** — the handle is useless without playback
  and would have to be torn down and recreated; **existence check only for music** — leaves a corrupt
  track to surface much later, against the project's fail-loudly stance.

## Decision 5 — Residency is `Resident | Reference | Failed`, eager, single-writer

- **Decision**: `Residency = Resident of SoundHandle | Reference | Failed of Message : string`.
  A sound is `Resident` or `Failed`; a music track is `Reference` or `Failed`. Loading is eager and
  synchronous on the render thread, so there is no `NotLoaded`/`Loading`.
- **Rationale**: eager loading (clarified in the spec) means there is no deferred state to model;
  the two-value model is the smallest that expresses the load outcome and the failure message, and
  matches the environment lifecycle minus the states audio never enters.
- **Alternatives considered**: **reuse `EnvironmentAsset.Residency`** — it is GPU-shaped
  (`Resident of EnvironmentLoaded`); **a `NotLoaded` state** — meaningless when the scan loads
  everything in one pass.

## Decision 6 — The mixer outlives the sounds; the manager frees sounds, the context frees the mixer

- **Decision**: `AssetManager` gains `AudioAssets : Dictionary<AssetId, AudioAsset>` and
  `Audio : Audio.Context`. `Assets.release` frees every resident sound (via `FreeSound`) and then
  calls `Audio.release` (`MIX_DestroyMixer` + `MIX_Quit`).
- **Rationale**: a `MIX_Audio` belongs to its mixer, so the mixer must outlive every loaded sound;
  freeing sounds before the mixer is the safe order. Holding the context on the manager (as it
  already holds `Device`) lets `release` do this without threading a parameter, and gives playback a
  handle later. `Engine.fs`'s existing `Assets.release` call needs no change.
- **Alternatives considered**: **the engine releases the mixer separately** — more ordering to get
  wrong across two call sites; **the manager creates the mixer** — conflates device init with the
  asset layer and breaks headless tests (D3).

## Decision 7 — Audio is a *separate* `SDL_InitSubSystem`, never part of Engine's `SDL.Init`

- **Decision**: `Audio.create` calls `SDL.InitSubSystem(SDL.InitFlags.Audio)` itself and tolerates
  failure. `Engine.run`'s existing `SDL.Init(SDL.InitFlags.Video)` is **unchanged**.
- **Rationale**: `SDL.Init` is all-or-nothing — adding `Audio` to Engine's flags would make a machine
  with no audio device fail the whole startup, contradicting FR-009. A subsystem init that can fail
  on its own is what lets the game fall back to running with audio unavailable.
- **Alternatives considered**: **add `Audio` to Engine's `SDL.Init`** — fatal on a machine with no
  audio backend.

## Decision 8 — All format support comes from SDL_mixer's decoders

- **Decision**: no format-specific code. `MIX_LoadAudio`/`MIX_CreateAudioDecoder` select a decoder by
  content; the spike reported 15 (WAV, Vorbis/StbVorbis, Opus, FLAC/dr_flac, WavPack, GME, XMP, VOC,
  AIFF, AU, …).
- **Rationale**: FR-007 asks for the common formats "without per-format handling"; the library
  already provides it.
- **Alternatives considered**: none — writing decoders would violate Minimalism.

## Decision 9 — Failure is caught at the scan boundary and marked, never fatal

- **Decision**: `Audio.loadSound`/`Audio.probe` raise on failure; the scan pass wraps each audio
  asset's load in a `try/with`, reports via `Diagnostics.reportf`, and sets `Residency <- Failed msg`;
  the scan continues.
- **Rationale**: this is exactly how `scan` already tolerates a bad prop or environment, and it is
  the user-facing graceful path (spec 080). A failure is a *value on the asset*, not an exception
  that escapes.
- **Alternatives considered**: letting the loader return a `Result` — the `try/with` at the existing
  scan boundary already provides the same guarantee with no new type.

## Decision 10 — Headless tests use a fake context; the device path is verified by walkthrough

- **Decision**: `test/AudioTests.fs` builds a fake `Audio.Context` (recorded stubs) and asserts
  classification, id/name defaults, absolute-path resolution, residency (sound resident, music
  reference), failure marking and release bookkeeping — all without a device. The real SDL decode/
  probe and the degraded path are shown in [quickstart.md](./quickstart.md).
- **Rationale**: mirrors spec 053 (pure queue asserted headlessly; worker/GPU path by walkthrough).
  It keeps the suite green on a machine with no audio backend.
- **Alternatives considered**: opening a real mixer in tests — device-dependent and flaky in CI.

## Decision 11 — Scope: loading only

- **Decision**: no playback, mixing, tracks/groups/tags, transport, looping, volume, crossfade or
  spatialisation; no loop/gain manifest metadata; no editor/Asset Browser surface.
- **Rationale**: the spec scopes this to loading (FR-011). Surfacing audio in the editor belongs with
  the feature that *uses* it.
- **Alternatives considered**: adding loop/volume fields now — speculative until playback defines
  what it needs.
