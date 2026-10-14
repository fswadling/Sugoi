# Tasks: Music and Sound Assets Load from the Asset Manager

**Input**: Design documents from `/specs/086-audio-asset-loading/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/audio-loading.md](./contracts/audio-loading.md), [quickstart.md](./quickstart.md)

**Tests**: Included. The project tests every non-GPU path, and the audio load logic is device-free
(a fake `Audio.Context`), so it is asserted headlessly like the rest of the suite.

**Organization**: Tasks are grouped by user story (US1 discovery/addressable, US2 residency split,
US3 failure/degraded) so each is an independently testable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1, US2, US3 (maps to spec.md)
- Exact file paths in every description

## Path Conventions

Single F# project at the repository root; modules cluster by domain (`audio/`, `world/`, `state/`,
`test/`). New modules go in the `.fsproj` compile list at the right point (`render → audio → world`).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the `audio/` cluster and register it in the build.

- [X] T001 Add the new cluster to the build: in `fsharp-game.fsproj`, insert `<Compile Include="audio/AudioAsset.fs" />` then `<Compile Include="audio/Audio.fs" />` after the `render/` block and before `world/Dialogue.fs`; create `audio/AudioAsset.fs` (`module AudioAsset`) and `audio/Audio.fs` (`module Audio`) as empty skeletons.
- [X] T002 Build clean with the skeletons (`dotnet build`); confirm `native/libSDL3_mixer.so` is copied to the output by the existing `CopySdl3MixerLibrary` target.

**Checkpoint**: the `audio/` cluster compiles and the native mixer library lands in the output.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The audio model, the injectable device seam, and the manager's new fields — everything
every story builds on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 [P] Declare the audio model in `audio/AudioAsset.fs`: `SoundHandle = nativeint`; `Class = Music | Sound`; the `[<SymbolicExpansion>]` `Manifest` record (`Id`, `Name`, `File`, `Kind`); `Residency = Resident of SoundHandle | Reference | Failed of Message : string`; the `AudioAsset` record (`Id`, `Name`, `Path`, `Class`, `mutable Residency`); and the accessors `isResident`, `sound` (invalidOp when not `Resident`), `isReference`. (data-model.md)
- [X] T004 [P] Declare the device seam in `audio/Audio.fs`: the `Context` record (`Mixer : nativeint`; `LoadSound : string -> SoundHandle`; `Probe : string -> unit`; `FreeSound : SoundHandle -> unit`) and `unavailable` (a `Context` with `Mixer = 0n` whose closures raise "audio unavailable"). Do **not** implement `create`/`release` yet (US2/US3). (data-model.md)
- [X] T005 In `world/Assets.fs`, add `AudioAssets : Dictionary<AssetId, AudioAsset>` and `Audio : Audio.Context` to `AssetManager`, and change `scan` to take `audio: Audio.Context`. (contracts/audio-loading.md)
- [X] T006 In `state/SharedState.fs`, pass a context to `Assets.scan` — `Audio.unavailable` for now (US2 switches it to `Audio.create ()`), so the app compiles and runs with audio unavailable.
- [X] T007 Update `test/TestSupport.fs` (`manager` gains `AudioAssets = Dictionary()` and `Audio = Audio.unavailable`) and the five `Assets.scan … 0n` call sites in `test/AssetsTests.fs` (append `Audio.unavailable`); `dotnet test` is green.

**Checkpoint**: foundation ready — the project builds and the whole suite passes with no audio assets present.

---

## Phase 3: User Story 1 - A sound or track declared in the audio folder is discovered and addressable (Priority: P1) 🎯 MVP

**Goal**: `Assets.scan` discovers every `assets/audio/*.manifest`, defaults id/name, resolves the
source file to an absolute path, classifies it, and exposes it by id — with no code change to add one.

**Independent Test**: place an audio manifest and its file in a temp `assets/audio/`, scan, and assert
the asset is present under its id with its name and its absolute path (and that an empty folder
yields none). Uses a fake `Audio.Context`, so no device is opened.

- [X] T008 [US1] In `world/Assets.fs`, add the audio pass to `scan`: `scanDir root "audio"`, parse each manifest with `scvalue<AudioAsset.Manifest>`, default `Id` to the manifest stem and `Name` to `Id`, resolve `File` to an absolute `Path`, map `Kind` to `Class`, and add the `AudioAsset` to `AudioAssets`. A manifest with an empty `File` or an unknown/empty `Kind` is an authoring error — reported and skipped, not added. (FR-001–FR-004, data-model VR-002/VR-003)
- [X] T009 [US1] In the audio pass, set residency from the context: `Class = Sound` ⇒ `context.LoadSound Path` ⇒ `Resident`; `Class = Music` ⇒ `context.Probe Path` ⇒ `Reference`. A loader failure must not abort the scan (mark `Failed`; reporting is refined in US3). (FR-005/FR-006 wiring)
- [X] T010 [US1] In `world/Assets.fs`, add the readouts `audioAsset`, `isAudioResident` and `hasAudioFailed`, mirroring the environment readouts. (contracts/audio-loading.md)
- [X] T011 [P] [US1] Write `test/AudioTests.fs` (part 1): against a fake `Audio.Context`, assert discovery and addressability — an asset is present by id, it carries its declared name, its `Path` is absolute, an id defaults to the manifest stem, and an empty/absent audio folder yields none. (SC-001)

**Checkpoint**: audio assets are discovered and addressable, headlessly — the feature's MVP.

---

## Phase 4: User Story 2 - A sound is held in memory; a music track is not (Priority: P2)

**Goal**: the real device seam — a sound decoded into a resident buffer, a music track left on disk
(probed, not decoded).

**Independent Test**: with a fake context recording the calls, assert a `Sound` ends `Resident` and a
`Music` ends `Reference`; with the real context, confirm on a device that a WAV decodes and an OGG is
probed.

- [X] T012 [US2] Implement `audio/Audio.fs`: `create` (SDL audio via `SDL_InitSubSystem Audio`, `Mixer.Init`, `Mixer.CreateMixerDevice` with the default playback device) building a `Context` whose `LoadSound` = `Mixer.LoadAudio(mixer, path, predecode = true)`, `Probe` = `Mixer.CreateAudioDecoder(path, 0)` + `DestroyAudioDecoder`, `FreeSound` = `Mixer.DestroyAudio`; and `release` = `Mixer.DestroyMixer` + `Mixer.Quit`. (research D4/D6; FR-005/FR-006)
- [X] T013 [US2] In `state/SharedState.fs`, switch `create` from `Audio.unavailable` to `Audio.create ()`, so the app loads real audio.
- [X] T014 [P] [US2] Extend `test/AudioTests.fs` (part 2): a `Sound` asset ends `Resident` with the handle its fake `LoadSound` returned; a `Music` asset ends `Reference` and never calls `LoadSound`; and `Assets.release` calls `FreeSound` once per resident handle. (SC-002, VR-004/VR-005/VR-007)

**Checkpoint**: sounds are resident in memory and music is referenced on disk, headlessly asserted and device-verified.

---

## Phase 5: User Story 3 - A broken audio asset does not take the game down (Priority: P3)

**Goal**: a missing, corrupt or undecodable file (or an unavailable audio subsystem) is reported and
marks the asset failed, and the game keeps running.

**Independent Test**: point an asset at a missing file and at a garbage file — each is reported,
marked `Failed`, and the scan continues; force audio unavailable and confirm every audio asset is
`Failed` while the app starts.

- [X] T015 [US3] In `world/Assets.fs`, on a `LoadSound`/`Probe` failure report it via `Diagnostics.reportf` naming the asset and the message, set `Residency <- Failed msg`, and continue the pass so every other asset still loads. (FR-008)
- [X] T016 [US3] In `audio/Audio.fs`, make `create` never raise: if the audio subsystem, `Mixer.Init`, or the mixer device cannot be brought up, report and return `unavailable`. (FR-009, research D7)
- [X] T017 [P] [US3] Extend `test/AudioTests.fs` (part 3): a raising `LoadSound`/`Probe` marks the asset `Failed` and the other assets still load; an `unavailable` context marks every audio asset `Failed`; an empty `File` and an unknown `Kind` are skipped. (FR-008/FR-009, SC-004/SC-005)

**Checkpoint**: all three stories work independently; a broken or unavailable audio path degrades gracefully.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, the opt-in diagnostic, and end-to-end validation.

- [X] T018 [P] Document the new `audio/` cluster (layout, cluster list, and its compile position `render → audio → world`) in `docs/PROJECT_STRUCTURE.md`.
- [X] T019 [P] Add the opt-in `FFS_AUDIO_DEBUG=1` line in `world/Assets.fs` — one line per audio asset (id, class, resulting residency) — mirroring `FFS_LOAD_TIMING` (spec 053); off by default, no cost when unset. (contracts/audio-loading.md)
- [X] T020 Run [quickstart.md](./quickstart.md): the headless suite, then walkthroughs A (a sound resident / a track referenced), B (a broken file reported, game runs) and C (audio unavailable, game runs) with `FFS_AUDIO_DEBUG=1`.
- [X] T021 [P] Verify multi-format acceptance (FR-007/SC-003): extend [quickstart.md](./quickstart.md) §2 to declare and load a WAV, an OGG Vorbis, an FLAC and an MP3, confirming each is accepted with no per-format code (no switch on file extension anywhere in the load path).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; BLOCKS all stories.
- **User Stories (Phase 3–5)**: all depend on Foundational. US1 (the pass + readouts) is the base; US2 and US3 layer onto the same pass.
- **Polish (Phase 6)**: depends on the stories it documents/validates (T019 after US1; T020 after US2/US3).

### User Story Dependencies

- **US1 (P1)**: after Foundational. Independent — discovery/addressability is testable with a fake context.
- **US2 (P2)**: after Foundational; integrates with US1's pass (the residency wiring T009) and adds the real seam in `audio/Audio.fs`.
- **US3 (P3)**: after US1 (needs the pass); independent of US2's real seam (its failure/degraded assertions use a fake/`unavailable` context).

### Within Each Story

- Model/type before its use; the pass before its readouts; implementation before its tests.

### Parallel Opportunities

- Foundational: T003 and T004 (different files) in parallel.
- US1: T011 (tests) can be written alongside T008–T010 (different files).
- US2: T012 (audio/Audio.fs) and T014 (tests) are different files; T013 touches state/SharedState.fs.
- Polish: T018 and T019 are different files.

---

## Parallel Example: Foundational + US1

```text
# Foundational, different files:
Task: "Declare the audio model in audio/AudioAsset.fs"        (T003)
Task: "Declare the device seam in audio/Audio.fs"             (T004)

# US1, different files:
Task: "Add the audio pass to scan in world/Assets.fs"         (T008)
Task: "Write test/AudioTests.fs discovery assertions"          (T011)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 Setup → 2. Phase 2 Foundational → 3. Phase 3 US1 → **STOP and VALIDATE**: audio assets are
discovered and addressable, headlessly. The app builds and runs (audio unavailable).

### Incremental Delivery

1. Setup + Foundational → the project builds, suite green, no audio assets.
2. US1 → discovery/addressability (MVP).
3. US2 → real loading: sounds resident, music referenced.
4. US3 → failure handling and the degraded path.
5. Polish → docs, the opt-in diagnostic, the walkthrough.

---

## Notes

- [P] = different files, no dependencies.
- The whole feature is one eager pass in `Assets.scan` plus two small modules — no worker pool, no
  byte budget, no per-frame cost (research, plan).
- Verify `dotnet test` stays green after each phase; the fixture updates land in T007.
- Playback is out of scope (spec FR-011); do not add transport, looping or volume here.

---

## Phase 7: Convergence

**Purpose**: Reconcile `tasks.md` with the codebase after the post-implement asset work. The loader
itself is fully implemented — every FR-001…FR-012, SC-001…SC-006 and T001…T021 is satisfied — so the
findings below are the work done outside spec 086's scope, plus one doc drift.

- [X] T022 Review the changes made outside spec 086's scope and confirm or relocate them — `assets/.gitignore` (asset-ignore policy), the `.zed/settings.json` `file_scan_inclusions` entry, the copied `assets/audio/**` content library, and the `assets/README.md` audio-manifests section — per spec scope (unrequested)
- [X] T023 Align `quickstart.md` §2 with the shipped `assets/audio/{music,sounds}` subfolder layout (it currently declares assets flat under `assets/audio/`) per T020/T021 (partial)
