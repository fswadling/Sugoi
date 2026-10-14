# Tasks: Play and Mix Music from a Song Panel

**Input**: Design documents from `/specs/087-song-mixing/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/song-panel.md](./contracts/song-panel.md), [quickstart.md](./quickstart.md)

**Tests**: Included. The mix's pure half (gain, add/remove, the start rule, the setters) is device-free
and asserted headlessly like the rest of the suite; the SDL deck calls are verified by the audible
[quickstart.md](./quickstart.md) walkthrough (the spec-086 split).

**Organization**: Tasks are grouped by user story (US1 play a song, US2 mix several, US3 scrub and loop)
so each is an independently testable increment. `editor/SongPanel.fs` is one file grown across the three
stories, so its tasks are sequential (never `[P]`).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1, US2, US3 (maps to spec.md)
- Exact file paths in every description

## Path Conventions

Single F# project at the repository root; modules cluster by domain (`audio/`, `world/`, `state/`,
`editor/`, `test/`). New modules go in the `.fsproj` compile list at the right point
(`render → audio → world → state → editor → screen`). `audio/Music.fs` sits after `audio/Audio.fs` and
before `world/`; it names `Audio` only, never `Assets` or `SharedState` (the callers resolve paths).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the two new modules and register them in the build.

- [X] T001 Register the new files in the compile lists: in `fsharp-game.fsproj`, insert `<Compile Include="audio/Music.fs" />` after `audio/Audio.fs` (before `world/Dialogue.fs`) and `<Compile Include="editor/SongPanel.fs" />` after `editor/DialoguePanel.fs` (before `editor/Editor.fs`); in `test/fsharp-game.Tests.fsproj`, insert `<Compile Include="MusicTests.fs" />` after `AudioTests.fs`. Create `audio/Music.fs` (`module Music`), `editor/SongPanel.fs` (`module SongPanel`) and `test/MusicTests.fs` (`module MusicTests`) as empty skeletons.
- [X] T002 Build clean with the skeletons (`dotnet build`) and the suite green (`dotnet test`).

**Checkpoint**: the two modules are wired into the build and compile empty.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The mix model, the deck device ops, the state home, the authored `SetMix` step and its
history runner, and the asset picker — everything every story builds on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 [P] Declare the mix model and its pure edits in `audio/Music.fs`: `MixRow = { Asset : string; Weight : float32; Loop : bool }`, `Mix = { Rows : MixRow list; Master : float32 }`, the edits `gainOf` (`master * weight`), `empty`, `contains`, `rowOf`, `addRow` (a no-op when the asset is already a row), `removeRow`, `setWeight`, `setLoop`, `setMaster`, and the start rule `type Start = Resume | Restart` with `startOf : paused:bool -> Start`. (data-model.md; contract song-panel.md)
- [X] T004 [P] Add the deck device ops to `audio/Audio.fs`: `type AudioHandle = nativeint`, `type TrackHandle = nativeint`, and the thin wrappers `openStream` (`LoadAudio(..., predecode = false)`), `closeStream`, `createTrack`, `destroyTrack`, `setAudio`, `play`, `pauseTrack`, `resume`, `stopTrack`, `setGain`, `setLoops` (`SetTrackLoops(-1|0)`), `seek`, `position` (seconds, via `TrackFramesToMS`), `duration`, `isPlaying`, `isPaused`. Every op is a no-op when `context.Mixer = 0n`. Leave the spec-086 `Context`/`create`/`unavailable`/`release`/`LoadSound`/`Probe`/`FreeSound` unchanged. (data-model.md; FR-013)
- [X] T005 Declare the runtime rack in `audio/Music.fs`: `type Deck = { Stream : Audio.AudioHandle; Track : Audio.TrackHandle }`, `type Rack = System.Collections.Generic.Dictionary<string, Deck>`, and `reconcile` (free a deck whose row has left the mix; re-apply `gainOf mix.Master row.Weight` via `setGain` and the loop flag via `setLoops` to a survivor) and `release` (free every deck: `stopTrack` + `destroyTrack` + `closeStream`). (FR-012/FR-014, D9) — depends on T003, T004.
- [X] T006 In `state/SharedState.fs`, add `mutable Mix : Music.Mix` and `mutable Rack : Music.Rack` to the record, and seed them in `create` (`Mix = Music.empty`, a fresh `Rack`). (FR-010/D7; contract)
- [X] T007 In `state/Command.fs`, add `| SetMix of Previous : Music.Mix * Next : Music.Mix` to the `Command` union, list it in `placeOf`'s `None` branch among the state-level steps, and confirm the zone-level `apply`/`undo` refuse it. (contract; D8)
- [X] T008 In `state/History.fs`, handle `SetMix` in both runners: `apply (SetMix (_, next))` writes `shared.Mix <- next` then calls `Music.reconcile shared.Assets.Audio shared.Rack shared.Mix`; `undo (SetMix (previous, _))` writes `previous` and reconciles the same way. (FR-010/FR-012; D8/D9) — depends on T005, T006, T007.
- [X] T009 In `world/Assets.fs`, add `musicNames : manager:AssetManager -> string list` — the sorted ids of the assets whose `Class = Music` (the panel's picker). (contract)
- [X] T010 In `Engine.fs`, call `Music.release manager.Shared.Assets.Audio manager.Shared.Rack` **before** `Assets.release manager.Shared.Assets`, so every deck is freed before the mixer (spec 086's shutdown order). (FR-014) — depends on T005.
- [X] T011 Update `test/TestSupport.fs` so its `SharedState`/`AssetManager` fixtures carry `Mix = Music.empty` and a fresh `Rack`; `dotnet build` and `dotnet test` are green with the new fields present. — depends on T006.

**Checkpoint**: the foundation compiles and the whole existing suite passes with an empty mix.

---

## Phase 3: User Story 1 - Play a song from the panel (Priority: P1) 🎯 MVP

**Goal**: the director adds a music asset to the panel and plays it — pausing, resuming and stopping
like a media player.

**Independent Test**: headlessly, `addRow`/`removeRow`/`startOf` behave (one row per song; a re-add is a
no-op; play resumes a paused row, else restarts). On a device, walkthrough A of [quickstart.md](./quickstart.md):
add a track, play/pause/resume/stop and hear it.

- [X] T012 [US1] Add the row transport to `audio/Music.fs`: `play` opens the row's streaming deck on first use (`openStream` from the resolved `path`, `createTrack`, `setAudio`, `setGain`, `setLoops`), then per `startOf (Audio.isPaused track)` **resumes** (`Audio.resume`) or **seeks 0 and plays** (`Audio.seek 0`, `Audio.play`); `pause` holds (`Audio.pauseTrack`); `stop` halts and rewinds (`Audio.stopTrack`, `Audio.seek 0`). Add `isPlaying`/`isPaused` readouts over the deck. Every op is a no-op when the mixer is unavailable or the row has no deck. (FR-003, D5/D10, VR-004) — depends on T005.
- [X] T013 [US1] Create `editor/SongPanel.fs`: draw the `Songs` window (`ImGui.beginWindow "Songs"`) with a combo over `Assets.musicNames` and an **Add** button (a `History.commit … (SetMix (before, addRow …))`), a **Remove** button per row (a `SetMix` remove step), and one row per song with **Play / Pause / Stop** calling `Music.play`/`pause`/`stop` (runtime, never a history step). Resolve each row's path through `Assets.audioAsset manager id`. (FR-001/FR-002/FR-003; contract song-panel.md) — depends on T006, T008, T009, T012.
- [X] T014 [US1] In `editor/Editor.fs`, submit `SongPanel.draw shared &state` (the panel docks at the bottom, `native/cimgui_sdl3.cpp`), after `DialoguePanel.draw` and before returning the transition. (contract Editor.fs) — depends on T013.
- [X] T015 [P] [US1] Write `test/MusicTests.fs` (part 1): `addRow` adds one row and a re-add is a no-op (FR-002); `removeRow` drops it and removing an absent asset leaves the mix unchanged; `contains`/`rowOf` agree; and `startOf true = Resume`, `startOf false = Restart`. (SC-001, D5) — depends on T003.

**Checkpoint**: one song plays, pauses, resumes and stops from the panel — the feature's MVP.

---

## Phase 4: User Story 2 - Mix several songs at once (Priority: P2)

**Goal**: several songs play at the same time, balanced by a per-song weight and one master volume,
with a bed transport that starts or holds the whole mix.

**Independent Test**: headlessly, `gainOf` is the product and `setWeight`/`setMaster` touch only their
target; a weight/master edit is one step on the history while a transport press is none. On a device,
walkthrough B: two tracks sound at once, a weight moves only its track, master moves both, the bed
plays/holds all.

- [X] T016 [US2] In `editor/SongPanel.fs`, add the bed transport and the balance controls: a bed **Play all** that plays every row (each resuming from its position) and a bed **Hold all** that holds every row; a **Master** slider (a `SetMix` set-master step committed on release with `ImGui.isItemDeactivatedAfterEdit`); and a **Weight** slider per row (a `SetMix` set-weight step committed on release). (FR-004/FR-006/FR-007; contract) — depends on T013.
- [X] T017 [P] [US2] Extend `test/MusicTests.fs` (part 2): `gainOf m w` is the product and `gainOf 0.0 w = 0.0`; `setWeight`/`setMaster` change only their target and leave the rest identity-equal; and a `SetMix` weight/master edit is exactly one step on the one history (round-tripped with `History.undoHistory`/`redoHistory` on a `TestSupport` state), while a transport press records none. (SC-002/SC-004, VR-002) — depends on T003, T008, T011.

**Checkpoint**: several songs mix at once, weighted under one master, with a bed transport.

---

## Phase 5: User Story 3 - Scrub and loop (Priority: P3)

**Goal**: the director drags a song's scrub bar to hear it from any point and toggles whether it loops
or stops at its end.

**Independent Test**: headlessly, `setLoop` toggles only its row and a loop edit is one step. On a
device, walkthrough C: drag the scrub and playback continues from the new point; loop on repeats, loop
off stops.

- [X] T018 [US3] Add the scrub ops to `audio/Music.fs`: `position` (seconds, `Audio.position` of the deck's track; 0 when no deck), `duration` (`Audio.duration` of the deck's stream; 0 when no deck), and `seek` (`Audio.seek`). (FR-008, D6) — depends on T005.
- [X] T019 [US3] In `editor/SongPanel.fs`, add per row a **scrub** `sliderFloat` over `[0, duration]` in seconds — its value set from `Music.position` each frame while there is a deck, and `Music.seek` written when it is dragged (runtime, never a step) — and a **loop** checkbox (a `SetMix` set-loop step; `reconcile` applies `Audio.setLoops` to the live track without restarting it). (FR-008/FR-009, D4/D6) — depends on T013, T016, T018.
- [X] T020 [P] [US3] Extend `test/MusicTests.fs` (part 3): `setLoop` toggles only its row's flag and leaves the rest identity-equal, and a `SetMix` loop edit is one step. (SC-003) — depends on T003.

**Checkpoint**: all three stories work independently; scrub and loop are live.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and end-to-end validation.

- [X] T021 [P] Update `docs/PROJECT_STRUCTURE.md`: add `audio/Music.fs` (after `audio/Audio.fs`, before `world/`) and `editor/SongPanel.fs` (beside the other panels), and note the `render → audio → world → state → editor` flow. (plan; T001)
- [X] T022 [P] Confirm no new dependency or binding: the `SDL3.Mixer` surface used is already referenced and no `<PackageReference>` or `render/ImGui.fs` binding is added (the panel uses only `button`, `checkbox`, `sliderFloat`, `text`, `sameLine` and the existing combo). (plan Technical Context; D11)
- [X] T023 Run [quickstart.md](./quickstart.md): the headless suite, then walkthroughs A–D (play, mix, scrub/loop, removal/unavailable) with `SDL_AUDIODRIVER=dummy` where useful. (SC-001…SC-006)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; BLOCKS all stories.
- **User Stories (Phase 3–5)**: all depend on Foundational. US1 is the base (playback + the panel); US2
  and US3 layer onto the same panel file.
- **Polish (Phase 6)**: depends on the stories it documents and validates.

### User Story Dependencies

- **US1 (P1)**: after Foundational. Independent — playback and add/remove are testable with the
  headless model and the device walkthrough.
- **US2 (P2)**: after US1 (extends `editor/SongPanel.fs`); its pure gain/history parts are independent.
- **US3 (P3)**: after US1/US2 (extends `editor/SongPanel.fs`); its `setLoop` part is independent.

### Within Each Story

- Model/ops before the panel that calls them; the panel before its `Editor.fs` hookup; implementation
  before its tests.

### Parallel Opportunities

- Foundational: T003 and T004 (different files) in parallel.
- US1: T015 (tests) can be written alongside T012–T014 (different files).
- US2: T017 (tests) is a different file from T016.
- US3: T020 (tests) is a different file from T018/T019.
- Polish: T021 and T022 are different files.

---

## Parallel Example: Foundational

```text
# Different files:
Task: "Declare the mix model and its pure edits in audio/Music.fs"        (T003)
Task: "Add the deck device ops to audio/Audio.fs"                         (T004)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 Setup → 2. Phase 2 Foundational → 3. Phase 3 US1 → **STOP and VALIDATE**: one song plays,
   pauses, resumes and stops from the panel (walkthrough A).

### Incremental Delivery

1. Setup + Foundational → the mix model, the deck ops, the state home and the `SetMix` step; suite green.
2. US1 → play a song (MVP).
3. US2 → mix several, weighted under one master, with a bed transport.
4. US3 → scrub and loop.
5. Polish → docs and the walkthrough.

---

## Notes

- [P] = different files, no dependencies.
- `editor/SongPanel.fs` is one file grown across US1–US3: its tasks are sequential by story.
- The authored mix is one `SetMix` step per completed drag; transport and scrub never touch the history
  (FR-010/FR-011) — verify with the Edit menu's Undo/Redo.
- Music only: no one-shot sounds, no cue ops, no start/loop points (FR-015) — do not add them here.
- Verify `dotnet test` stays green after each phase; the fixture update lands in T011.

---

## Phase 7: Convergence

**Purpose**: Reconcile `tasks.md` with the codebase after implement + the post-implement fixes
(the bed transport, the drop-down/Add fix, and the native dock). Two behavioural gaps and one
unrequested addition remain; the rest of the feature (FR-001…FR-016, SC-001…SC-006, D1–D12) is satisfied.

- [X] T024 Fix the bed transport so **Play all** does not restart a row that is already playing: in `editor/SongPanel.fs`, start only rows not already playing (`if not (Music.isPlaying rack row.Asset) then playRow shared row.Asset`), so every row resumes from its own current position — a paused row resumes, a stopped one starts from 0, a playing one is left running. per FR-004 / US2-AC5 (contradicts)
- [X] T025 Resolve the FR-008 pre-play scrub gap: a row shows `(play to scrub)` and its scrub has a zero range until the row is first played. Either make the duration available when a row is added (open its deck on add, superseding research D10) so the scrub spans the song's duration from the start, **or** record the limitation in `spec.md`'s Assumptions. per FR-008 / US3 (partial)
- [X] T026 Confirm or remove the in-session `FFS_SONG_DEBUG` diagnostic in `editor/SongPanel.fs` (the `log` helper and its pick/click/commit lines): keep it deliberately as an opt-in diagnostic, or drop it to keep the panel minimal. per plan / Constitution I (unrequested)
