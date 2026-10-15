# Tasks: Play One-Shot Sounds and Audition Them from a Sound Panel

**Input**: Design documents from `/specs/089-sound-effects/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/sound-panel.md](./contracts/sound-panel.md), [quickstart.md](./quickstart.md)

**Tests**: Included. The pool's pure half (round-robin reuse, the per-asset Stop, Stop all, the readout)
is device-free and asserted headlessly — against `Audio.unavailable`, so no device is opened — like the
rest of the suite; the audible result is verified by the [quickstart.md](./quickstart.md) walkthrough
(the spec-086 split).

**Organization**: Tasks are grouped by user story (US1 audition from the panel, US2 one-shot
semantics, US3 failure/unavailability) so each is an independently testable increment.
`test/SoundTests.fs` is one file grown across the three stories, so its tasks are sequential (never
`[P]` with each other).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1, US2, US3 (maps to spec.md)
- Exact file paths in every description

## Path Conventions

Single F# project at the repository root; modules cluster by domain (`audio/`, `world/`, `state/`,
`editor/`, `test/`). New modules go in the `.fsproj` compile list at the right point
(`render → audio → world → state → editor → screen`). `audio/Sound.fs` sits after `audio/Music.fs`
and before `world/`; it names `Audio`/`AudioAsset` only, never `Assets` or `SharedState` (the panel
resolves each sound's handle).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the two new modules and register them in the build.

- [X] T001 Register the new files in the compile lists: in `fsharp-game.fsproj`, insert `<Compile Include="audio/Sound.fs" />` after `audio/Music.fs` (before `world/Dialogue.fs`) and `<Compile Include="editor/SoundPanel.fs" />` after `editor/SongPanel.fs` (before `editor/Editor.fs`); in `test/fsharp-game.Tests.fsproj`, insert `<Compile Include="SoundTests.fs" />` after `MusicTests.fs`. Create `audio/Sound.fs` (`module Sound`), `editor/SoundPanel.fs` (`module SoundPanel`) and `test/SoundTests.fs` (`module SoundTests`) as empty skeletons.
- [X] T002 Build clean with the skeletons (`dotnet build`) and the suite green (`dotnet test`).

**Checkpoint**: the two modules are wired into the build and compile empty.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The voice pool and its ops, the asset readout, the state home, the shutdown line, and the
fixture update — everything every story builds on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 [P] Declare the voice pool in `audio/Sound.fs`: a fixed `capacity` (`[<Literal>] let capacity = 16`), `type Voices = { Tracks : Audio.TrackHandle array; Playing : string array; mutable Next : int }`, `newVoices` (create `capacity` tracks via `Audio.createTrack`, all-`""` records, `Next = 0`), and the ops — `play` (take the next voice round-robin, `Audio.setAudio` the resident `SoundHandle`, `Audio.play`, record the asset id, advance `Next`), `stop` (stop every voice whose record matches the id; clear those records), `stopAll` (stop every voice; clear every record), `release` (stop every track via `Audio.stopTrack`, then `Audio.destroyTrack` every track; clear every record). Names `Audio`/`AudioAsset` only. (data-model.md; contract sound-panel.md; FR-001/FR-002/FR-003/FR-004/FR-005/FR-009)
- [X] T004 [P] In `world/Assets.fs`, add `soundAssets : manager:AssetManager -> AudioAsset.AudioAsset list` — the assets whose `Class = Sound`, sorted by `Name`. (contract; FR-004)
- [X] T005 In `state/SharedState.fs`, add `Voices : Sound.Voices` to the record and seed it in `create` with `Sound.newVoices audio` (the context the asset manager already owns). (data-model.md; contract; D5) — depends on T003.
- [X] T006 Update every `SharedState` literal fixture to carry `Voices = Sound.newVoices Audio.unavailable`: `test/ActorNameTests.fs`, `test/AdventTests.fs`, `test/CuePanelTests.fs`, `test/CueTests.fs` (three fixtures), `test/GameplayStateTests.fs`, `test/MusicCueTests.fs`, `test/MusicTests.fs`, `test/ScenarioForkTests.fs`, `test/ScenarioTests.fs`, `test/SelectionTests.fs`, `test/SpawningTests.fs`; `dotnet build` and `dotnet test` are green with the new field present. (data-model.md) — depends on T005.
- [X] T007 In `Engine.fs`, call `Sound.release manager.Shared.Assets.Audio manager.Shared.Voices` before `Assets.release manager.Shared.Assets`, beside the music decks, so every voice is freed before the mixer (spec 086's shutdown order). (FR-009, D5) — depends on T003, T005.

**Checkpoint**: the foundation compiles and the whole existing suite passes with an empty pool.

---

## Phase 3: User Story 1 - Audition a discovered sound from the panel (Priority: P1) 🎯 MVP

**Goal**: the director opens the Sounds panel, sees every sound the asset manager discovered, and plays
one to hear it — with a per-row Stop and a global Stop all.

**Independent Test**: headlessly, `Assets.soundAssets` lists only sound-class assets sorted by name, and
the pool records an id on `play`. On a device, walkthrough A of [quickstart.md](./quickstart.md): open
the Sounds tab, press Play and hear it, press Stop and Stop all.

- [X] T008 [US1] Create `editor/SoundPanel.fs`: draw the `Sounds` window (`ImGui.beginWindow "Sounds"`), a **Stop all** button at the top, and one row per `Assets.soundAssets` entry — the asset's `Name`, a resident/failed marker, a **Play** button (guard with `AudioAsset.isResident`; when resident call `Sound.play shared.Voices asset.Id (AudioAsset.sound asset)`) and a **Stop** button (`Sound.stop shared.Voices asset.Id`). Give each row a unique widget id (`ImGui.pushId asset.Id` … `popId`) so two assets that share a `Name` — the duplicate-name edge case — do not collide. Show `(no sounds)` when the list is empty. Reference `AudioAsset.*` qualified (do not `open AudioAsset`). Nothing is written to the history or any authored state. (FR-004/FR-005/FR-006/FR-007; contract sound-panel.md; D7/D8) — depends on T003, T004.
- [X] T009 [US1] In `editor/Editor.fs`, submit `SoundPanel.draw shared &state` after `SongPanel.draw`, and add `"Sounds"` to `panelNames` (the Ctrl+Tab focus list). (contract Editor.fs) — depends on T008.
- [X] T010 [P] [US1] In `native/cimgui_sdl3.cpp`, add `ImGui::DockBuilderDockWindow("Sounds", bottom)` in `cimgui_setup_dock_layout`, beside `"Songs"`/`"Asset Browser"`/`"Dialogue"`; rebuild with `native/build.sh`. (contract native; plan) — no code dependency.
- [X] T011 [US1] Write `test/SoundTests.fs` (part 1): `Assets.soundAssets` returns only `Kind = Sound` assets, sorted by name, carrying residency, from a temp root and a recording fake `Audio.Context` (as `test/AudioTests.fs` does); a `play` on a fresh pool records the id on voice 0 and advances the cursor; `stop` clears only the matching records and `stopAll` clears all; and a play/stop leaves the one history untouched (`state.Editor.Undo.Applied` stays empty — FR-006/SC-004). (SC-001/SC-002/SC-004/SC-007; FR-004/FR-005/FR-006) — depends on T003, T004, T006.

**Checkpoint**: a sound plays, stops and stops-all from the panel — the feature's MVP.

---

## Phase 4: User Story 2 - One-shot playback semantics (Priority: P2)

**Goal**: a sound sounds once and stops on its own; playing it again starts a new instance that overlaps
the previous one, up to the pool's bound, the oldest cut when the pool is full.

**Independent Test**: headlessly, repeated plays fill voices round-robin and wrap (reusing the
least-recently-used voice) so the pool never grows past `capacity`. On a device, walkthrough B: play a
short sound, play it again to hear overlap, rapid-fire past the bound to hear the oldest cut.

- [X] T012 [US2] Extend `test/SoundTests.fs` (part 2): playing N different sounds fills N voices in
  round-robin order; playing more than `capacity` sounds wraps the cursor and reuses the
  least-recently-used voice (the pool length never changes); playing the same sound again records it
  again on the next voice (overlap). (FR-001/FR-003; SC-003; D3) — depends on T003.

**Checkpoint**: one-shots overlap up to a bound and are cut oldest-first; the pool is bounded.

---

## Phase 5: User Story 3 - A broken or silent audio system never takes the game down (Priority: P3)

**Goal**: a sound whose asset failed to load, or a machine with no working audio output, leaves the
panel usable and the game running — playing is a quiet no-op.

**Independent Test**: headlessly, a pool on `Audio.unavailable` plays/stops/stops-all/releases without
raising, and `soundAssets` still lists a failed asset. On a device, walkthrough D: a corrupt sound is
listed and marked and its Play is a no-op; with the audio subsystem unavailable the panel still draws.

- [X] T013 [US3] Extend `test/SoundTests.fs` (part 3): `newVoices Audio.unavailable` yields `capacity`
  `0n` tracks; `play`/`stop`/`stopAll`/`release` on it raise nothing; and `soundAssets` returns a
  failed asset marked `Failed` (so the panel can show it and no-op on Play). (FR-007/FR-008; SC-005) —
  depends on T003, T004, T006.

**Checkpoint**: all three stories work independently; failure and unavailability are quiet no-ops.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and end-to-end validation.

- [X] T014 [P] Update `docs/PROJECT_STRUCTURE.md`: add `audio/Sound.fs` (after `audio/Music.fs`, before `world/`) and `editor/SoundPanel.fs` (beside the other panels), keeping the `render → audio → world → state → editor` flow. (plan; T001)
- [X] T015 [P] Update `docs/ROADMAP.md`: record the sound playback facility and the Sounds panel, and note that the sound cue op (the analogue of spec 088's music ops) remains the deferred audio family. (spec Origin/Assumptions)
- [X] T016 [P] Confirm no new dependency or binding: the `SDL3.Mixer` surface used (`CreateTrack`/`DestroyTrack`/`SetTrackAudio`/`PlayTrack`/`StopTrack`, via `audio/Audio.fs`) is already referenced, and no `<PackageReference>` or `render/ImGui.fs` binding is added (the panel uses only `button`, `text`, `sameLine` and `beginDisabled`). (plan Technical Context; D4)
- [X] T017 Run [quickstart.md](./quickstart.md): the headless `SoundTests`, the whole suite, then walkthroughs A–D (audition, overlap, runtime-only, failure/unavailable) with `SDL_AUDIODRIVER=dummy` where useful. (SC-001…SC-007)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; BLOCKS all stories.
- **User Stories (Phase 3–5)**: all depend on Foundational. US1 carries the panel (the MVP); US2 and US3
  are properties of the same pool, verified by their tests and the walkthrough.
- **Polish (Phase 6)**: depends on the stories it documents and validates.

### User Story Dependencies

- **US1 (P1)**: after Foundational. Independent — listing/playing from the panel is testable headlessly
  and by walkthrough A.
- **US2 (P2)**: after Foundational (met by `play` in T003); its tests are independent of US1's panel.
- **US3 (P3)**: after Foundational (the guards live in `Audio` and the panel's `isResident` check); its
  tests are independent of US1/US2.

### Within Each Story

- Model/ops before the panel that calls them; the panel before its `Editor.fs` hookup; the implementation
  before its tests.

### Parallel Opportunities

- Foundational: T003 (`audio/Sound.fs`) and T004 (`world/Assets.fs`) are different files — parallel.
- US1: T010 (`native/cimgui_sdl3.cpp`) is a different file from T008/T009 — parallel.
- Polish: T014, T015 and T016 are different files — parallel.
- `test/SoundTests.fs` is one file grown across US1–US3 (T011/T012/T013): those are **sequential**.

---

## Parallel Example: Foundational

```text
# Different files:
Task: "Declare the voice pool in audio/Sound.fs"        (T003)
Task: "Add soundAssets to world/Assets.fs"              (T004)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 Setup → 2. Phase 2 Foundational → 3. Phase 3 US1 → **STOP and VALIDATE**: open the Sounds
   tab, press Play and hear a sound; press Stop and Stop all (walkthrough A).

### Incremental Delivery

1. Setup + Foundational → the pool, the readout, the state home, the shutdown line; suite green.
2. US1 → audition a sound from the panel (MVP).
3. US2 → overlap up to a bound, oldest cut.
4. US3 → failure and unavailability are quiet no-ops.
5. Polish → docs and the walkthrough.

---

## Notes

- [P] = different files, no dependencies.
- `test/SoundTests.fs` is one file grown across US1–US3: its tasks are sequential.
- The panel is a **runtime audition surface**: it records no history step and writes no authored state
  (FR-006) — verify in the Edit menu that Undo stays disabled after an audition.
- One-shot only: no looping, no cue ops, no start/end points, no per-sound metadata (FR-010) — do not
  add them here.
- Verify `dotnet test` stays green after each phase; the fixture update lands in T006.
