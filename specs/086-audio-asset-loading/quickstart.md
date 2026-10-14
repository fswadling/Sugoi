# Quickstart: Music and sound assets load from the asset manager

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Contract**: [contracts/audio-loading.md](./contracts/audio-loading.md)

How to prove the feature works: the headless suite (which asserts the load *logic*), then a device
walkthrough (which shows the real SDL decode/probe and the degraded path).

## Prerequisites

- `dotnet build` is clean.
- For generating scratch audio: `ffmpeg` (or supply your own WAV/OGG/FLAC/MP3 files).

## 1. Headless validation (no device)

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~AudioTests"   # the new, device-free audio-load tests
dotnet test                                            # the whole suite stays green
```

Expected:

- `AudioTests` pass: a `Sound` asset ends `Resident`, a `Music` asset ends `Reference`, a raising
  `LoadSound`/`Probe` marks the asset `Failed` and the scan continues, an empty `File` or an
  unknown `Kind` is skipped, ids/names default to the manifest stem, `File` resolves to an absolute
  path, and `release` frees each resident handle once — all against a fake `Audio.Context`, so no
  audio device is opened.
- The existing suite (actor/prop/environment/editor/gameplay) is unchanged and green — `scan` keeps
  its behaviour for the other kinds.

## 2. Set up a scratch audio tree

Sounds live under `assets/audio/sounds`, music under `assets/audio/music`. From the repo root:

```bash
mkdir -p assets/audio/sounds assets/audio/music
ffmpeg -y -f lavfi -i "sine=frequency=880:duration=0.3" assets/audio/sounds/door-open.wav
ffmpeg -y -f lavfi -i "sine=frequency=220:duration=5" -c:a libvorbis assets/audio/music/town-theme.ogg
```

Declare them (one manifest beside each file):

```bash
printf '[[Id "door-open"]  [Name "Door Open"]   [File "audio/sounds/door-open.wav"]  [Kind "Sound"]]'  > assets/audio/sounds/door-open.manifest
printf '[[Id "town-theme"] [Name "Town Theme"]  [File "audio/music/town-theme.ogg"] [Kind "Music"]]' > assets/audio/music/town-theme.manifest
```

### Extra formats (FR-007 / SC-003)

```bash
ffmpeg -y -f lavfi -i "sine=frequency=660:duration=0.3" -c:a flac assets/audio/sounds/beep.flac
ffmpeg -y -f lavfi -i "sine=frequency=550:duration=0.3" -c:a libmp3lame assets/audio/sounds/blip.mp3
printf '[[Id "beep"] [Name "Beep"] [File "audio/sounds/beep.flac"] [Kind "Sound"]]' > assets/audio/sounds/beep.manifest
printf '[[Id "blip"] [Name "Blip"] [File "audio/sounds/blip.mp3"]  [Kind "Sound"]]' > assets/audio/sounds/blip.manifest
```

With `FFS_AUDIO_DEBUG=1`, `beep` and `blip` both report `resident` — the format is accepted with no
per-format code anywhere in the load path (FR-007 / SC-003).

## 3. Walkthrough A — a sound is held in memory, a track is referenced

```bash
FFS_AUDIO_DEBUG=1 dotnet run
```

Expected (the opt-in `FFS_AUDIO_DEBUG=1` line, mirroring `FFS_LOAD_TIMING` in spec 053, reports one
line per audio asset):

```text
audio door-open  | Sound | resident
audio town-theme | Music | reference
```

- `door-open` decoded into memory (`predecode = true`) — no diagnostic, no error.
- `town-theme` probed once and left on disk — no decoded audio held.
- The game starts normally; nothing about the frame loop, rendering or the editor changed.

## 4. Walkthrough B — a broken file is reported and the game runs (FR-008)

```bash
printf 'garbage, not audio' > assets/audio/music/town-theme.ogg     # corrupt the track
FFS_AUDIO_DEBUG=1 dotnet run
```

Expected:

- A diagnostic is printed naming `town-theme` and the failure; its residency is `failed`.
- The other assets still load and the game starts and runs normally (no crash).

Point a manifest's `File` at a path that does not exist and repeat — same result (reported, one
failed asset, everything else fine).

## 5. Walkthrough C — the game runs with audio unavailable (FR-009)

```bash
SDL_AUDIODRIVER=does-not-exist dotnet run
```

Expected:

- The audio subsystem fails to start; a single diagnostic says audio is unavailable.
- Every audio asset is reported `failed` (reason: audio unavailable), and the game starts and runs
  normally — exactly as a missing font disables text (spec 080).

`SDL_AUDIODRIVER=dummy` is the positive counterpart: audio starts on a silent device, a sound still
decodes `resident`, and the walkthrough above passes.

## Cleanup

```bash
rm -rf assets/audio
```

## What this does not cover (later feature)

Playback — hearing the sound or track — is out of scope (spec FR-011). This phase ends with the
assets loaded and addressable; playing, mixing, looping and volume arrive with the playback feature.
