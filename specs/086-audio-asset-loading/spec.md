# Feature Specification: Music and Sound Assets Load from the Asset Manager

**Feature Branch**: `086-audio-asset-loading`

**Created**: 2026-10-14

**Status**: Draft

**Input**: User description: "Music and sounds are assets, so they should be loaded by the asset manager — they should load automatically from a folder in assets. The first part of this is just loading the assets."

## Origin

The engine already has one asset manager (`world/Assets.fs`) that scans a folder per asset
kind and loads what it finds: **actors** and **props** eagerly and synchronously at startup,
**environments** on demand and asynchronously (spec 050, spec 053), under an LRU byte budget
because a decoded environment is enormous. Every kind is declared by a small s-syntax manifest
and addressed by an id (defaulting to the file stem) and a name (defaulting to the id).

Audio is the next kind, and SDL3_mixer is now wired in: the managed `SDL3.Mixer` bindings ship
with the `SDL3-CS` package the project already uses, a vendored `libSDL3_mixer.so` is copied to
the output beside `libSDL3_image.so`/`libSDL3_ttf.so`, and a spike confirmed the library resolves,
initialises, reports **15 decoders** (Vorbis, Opus, FLAC, WAV, WavPack, module/MIDI, …), and
loads and plays both a resident buffer and a streaming one.

This feature is **only the loading half** of music and sound. It discovers audio assets, gives
them the right residency, and releases them with the rest of the manager. Playing them — the
transport, mixing, tags/groups, volume and looping — is a later feature and is explicitly out
of scope here.

Audio differs from environments in a way that shapes the residency model. A sound effect is
short and is replayed constantly, so it wants to be **decoded and held in memory**. A music
track is long and decoded audio is uncompressed (≈192 KB per second of stereo 48 kHz audio — a
three-minute theme is ≈35 MB), so it wants to stay a **reference to its compressed file on disk**,
decoded only as it is used. That split is the central decision this phase makes; it is what lets a
director add a large track without the game paying for it before it is ever heard. Because sounds
are small and music holds nothing, audio does **not** need the environment machinery — no worker
pool, no per-frame upload budget, no byte-budgeted LRU.

## Clarifications

### Session 2026-10-14

- Q: How are audio assets discovered and described — a per-asset manifest, bare audio files, or bare
  files under kind-named subfolders? → A: A per-asset manifest in `assets/audio/`, carrying `Id`,
  `Name`, `File` and `Kind`, consistent with actors/props/environments; the class comes from `Kind`.
- Q: Does this phase decode sounds now, or hold every audio asset as a reference and defer all
  decoding to the playback feature? → A: Decode sounds now — a sound is decoded and held in memory
  at load, while a music track stays a reference to its file on disk.
- Q: Should every sound be decoded once at startup, or only the first time it is used? → A: Eager at
  startup — every sound is decoded during the initial asset scan (as actors and props load), so a
  sound is resident from launch with no deferred-load state.
- Q: If the machine has no working audio output, or the audio system fails to start, should the game
  run with audio disabled or refuse to start? → A: Run with audio disabled — report the failure,
  leave audio assets unavailable, and start normally (as a missing font disables text, spec 080).
- Q: For a music track (not decoded this phase), how much should be verified at load? → A: Probe
  with a decoder — open the file through a decoder at load to confirm it is readable and playable,
  but hold no decoded audio.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A sound or track declared in the audio folder is discovered and addressable (Priority: P1)

The director writes a small manifest in the audio folder under `assets/` and drops the sound or
music file it names beside it; the asset is discovered by the asset manager at startup and
addressable by an id and a name — exactly the way an actor, a prop or an environment is — without
touching any code or any other asset.

**Why this priority**: this is the whole feature. Without discovery there is nothing to load, and
without the id/name contract the asset cannot be named by anything downstream.

**Independent Test**: place an audio manifest and its file in the audio folder, start the game, and
confirm the manager reports it under its id with its declared name carried (and reports none for an
empty folder).

**Acceptance Scenarios**:

1. **Given** an audio folder containing one well-formed audio asset, **When** the manager scans at
   startup, **Then** the asset is present, addressed by its id, and carries its declared name.
2. **Given** an audio manifest with no explicit id, **When** it is scanned, **Then** its id is the
   manifest's file stem and its name defaults to that id (as actors do).
3. **Given** an empty or absent audio folder, **When** the manager scans, **Then** the audio set is
   empty and no error is raised.

---

### User Story 2 - A sound is held in memory; a music track is not (Priority: P2)

A sound effect is decoded and held in memory, so it is resident and needs no further disk access;
a music track is kept as a reference to its file on disk and no decoded audio for it is held. A
long track therefore costs the game almost nothing until the playback feature uses it.

**Why this priority**: it is the value of loading audio as an asset rather than as a side effect —
it fixes the memory cost by kind. It ranks below discovery because without discovery there is
nothing to hold, but it is the reason the feature exists.

**Independent Test**: load one sound and one music track, and confirm the sound is resident while
the music track holds no decoded sample data (its residency is a reference, not a buffer).

**Acceptance Scenarios**:

1. **Given** a sound asset, **When** the startup asset scan completes, **Then** its decoded audio is
   held in memory and it is reported resident.
2. **Given** a music asset, **When** the startup asset scan completes, **Then** no decoded audio is
   held for it, it remains a reference to its file on disk, and it has been probed and confirmed
   playable.
3. **Given** several sounds and several tracks, **When** loading completes, **Then** every sound is
   resident and no track holds decoded audio.

---

### User Story 3 - A broken audio asset does not take the game down (Priority: P3)

A manifest or file that names an audio source that is missing, unreadable or corrupt is reported
and the asset is marked failed; the scan continues, the rest of the game runs, and every other
audio asset still loads — the same tolerance a prop or environment already has.

**Why this priority**: it is the guard that keeps a single bad asset from being fatal. It ranks
last because it is a failure path, but the project's user-facing policy (spec 080) makes it
required rather than optional.

**Independent Test**: point an audio asset at a missing and at a corrupt file, and confirm both are
reported, marked failed, and the run continues with the other assets loaded.

**Acceptance Scenarios**:

1. **Given** an audio asset whose source file does not exist, **When** loading is attempted, **Then**
   the failure is reported and the asset is marked failed without aborting the scan.
2. **Given** an audio asset whose source file exists but cannot be decoded, **When** loading is
   attempted, **Then** the failure is reported, the asset is marked failed, and other assets load.
3. **Given** an audio asset that fails, **Then** the game starts and runs normally.

---

### Edge Cases

- **The audio folder does not exist**: the audio set is empty and no error is raised (as an absent
  `actors`/`props`/`environments` folder already behaves).
- **A manifest names no source file, or declares no class**: it is an authoring error — reported
  and skipped, not a crash (as a prop or environment with no model path is skipped).
- **Two manifests declare the same id**: the existing id collision behaviour applies — the asset
  scanned later replaces the earlier one under that id (the collection is keyed by id). Audio has no
  name index this phase, so two assets sharing a `Name` is not a collision to resolve.
- **A source file with an extension the platform decoder does not support**: treated as an
  undecodable file (failure path above), reported rather than aborting.
- **A very large music track**: still costs no more than a reference; it is not decoded at load.
- **A sound played later on many tracks at once** (a later feature): unaffected by this phase, but
  the resident-in-memory choice is what makes it possible.
- **The machine has no audio device, or the audio system fails to start**: the game runs with audio
  unavailable (reported) rather than failing to start (FR-009).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The asset manager MUST discover every audio asset declared by a manifest in the audio
  folder (`assets/audio/`) at startup, so that adding an audio asset is writing a small s-syntax
  manifest and placing the file it names in the folder — with no code change. This matches how
  actors, props and environments are declared.
- **FR-002**: Each audio asset MUST be addressed by an **id** (defaulting to the manifest's file
  stem) and MUST carry a **name** (defaulting to the id), matching the contract actors, props and
  environments follow. The name is carried for later use (playback, tooling); this phase adds no
  name-based lookup for audio.
- **FR-003**: Each manifest MUST name its source file, and that path MUST be resolved to an
  **absolute path once, at startup**, so that loading it later needs no knowledge of the asset root
  (as `EnvironmentAsset.ModelPath` already does). A manifest that names no source file is an
  authoring error — reported and skipped, not a crash.
- **FR-004**: Each manifest MUST declare the asset's class — **Music** or **Sound** — and that
  classification MUST be available to whatever uses the asset. A missing or invalid class is an
  authoring error — reported and skipped.
- **FR-005**: A **sound** asset MUST be decoded and held in memory **eagerly at startup**, during
  the initial asset scan (as actors and props load), so it is resident from launch and needs no
  further disk access.
- **FR-006**: A **music** asset MUST NOT be decoded into memory; it MUST remain a reference to its
  (compressed) file on disk, so a long track costs the game only a reference until the playback
  feature uses it. Sounds are decoded now (FR-005); music is not. To catch a broken track early, a
  music asset MUST nevertheless be **probed** at load — opened through a decoder to confirm it is
  readable and playable — without holding any decoded audio (FR-008).
- **FR-007**: The engine MUST accept the common compressed and uncompressed audio formats without
  per-format handling — WAV, OGG Vorbis, Opus, FLAC, MP3 and the module/MIDI formats the platform
  decoder already reports.
- **FR-008**: A source file that is missing, unreadable or corrupt MUST be reported and its asset
  marked failed; it MUST NOT abort the scan, and every other audio asset MUST still load.
- **FR-009**: If the audio subsystem cannot be brought up — for example, the machine has no playback
  device — the game MUST still start and run with audio unavailable: the failure is reported, and
  audio assets are left unavailable rather than the run aborting (spec 080).
- **FR-010**: Releasing the asset manager MUST free every resident sound and drop every audio
  reference, alongside the existing actor/prop/environment teardown.
- **FR-011**: This phase MUST NOT play, mix, transport (play/stop/pause), loop, or set the volume
  of any audio asset; those are the later playback feature.
- **FR-012**: `dotnet build` MUST be clean and `dotnet test` MUST pass, with the existing suite
  unmodified.

### Key Entities *(include if feature involves data)*

- **Audio manifest**: the small s-syntax file (`assets/audio/<id>.manifest`) that declares an audio
  asset — its id, its name, its source file and its class — exactly as actor, prop and environment
  manifests declare theirs.
- **Audio asset**: one discoverable sound or music item — its id, its name, the absolute path of
  its source file, its class (music or sound) and its residency (whether decoded audio is held).
- **Class**: whether an asset is **music** (long; referenced on disk, not held) or a **sound**
  (short; decoded and held). The one distinction this phase acts on.
- **Audio set**: the manager's id-keyed collection of audio assets, alongside the existing actors,
  props and environments.
- **Residency**: the coarse lifecycle of an audio asset — resident (a sound held in memory),
  reference (music held on disk), or failed (with its message). A sound is resident from startup
  (FR-005); there is no deferred-load state in this phase, and no byte budget.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new audio asset dropped into the audio folder is discovered and addressable by its
  id (carrying its declared name) with no code change, for 100% of well-formed assets (0 requiring a
  code edit).
- **SC-002**: After startup, 100% of sound assets are resident in memory, and 0 music assets hold
  decoded sample data — the decoded bytes attributable to music are zero.
- **SC-003**: Every audio format the platform decoder reports is accepted without per-format code
  (100% of reported formats load).
- **SC-004**: A missing or corrupt source file is reported and marks its asset failed, and never
  prevents startup or another asset from loading (0 crashes, 0 unrelated assets lost).
- **SC-005**: On a machine with no working audio device, the game still starts and runs with audio
  unavailable (reported; 0 crashes).
- **SC-006**: `dotnet build` is clean and `dotnet test` passes, with the existing suite unmodified.

## Assumptions

- **Audio is declared by a manifest in `assets/audio/`.** The manifest carries `Id`, `Name`, `File`
  and `Kind`, matching the actors/props/environments convention; this is the clarified authoring
  surface (a per-asset manifest rather than bare files).
- **Sounds are decoded at load; music is not.** This is the clarified scope of the phase: the audio
  subsystem is brought up in this phase (SDL audio + SDL_mixer — the mixer is what decodes a
  sound), a sound is held in memory once loaded (FR-005), and a music track stays a reference
  (FR-006). Where the initialisation lives is a planning concern, not a specification one.
- **Music is streaming, and streaming is a playback concern.** Because a streaming handle is only
  meaningful while it is playing, this phase keeps a music asset as its path and probes it once at
  load (FR-006); the streaming handle is created by the later playback feature.
- **Sounds are small.** The project's sounds are short effects, so holding every sound in memory is
  the right default and is what the earlier design discussion settled on; a byte budget for sounds
  is deliberately not introduced (no just-in-case machinery).
- **No loop, gain or other per-asset metadata this phase.** Loop count and default volume are
  playback concerns and are deferred with the transport; the classification (music vs sound) is the
  only audio metadata this phase needs.
- **Non-goals (the later playback feature):** playing, mixing, tracks/groups/tags, transport
  (play/stop/pause), runtime volume, looping, crossfade and spatialisation.
