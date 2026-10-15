# Tasks: Music Cues — play and shape music from the story

**Input**: Design documents from `/specs/088-music-cues/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/cue-music.md](./contracts/cue-music.md),
[quickstart.md](./quickstart.md)

**Tests**: Included. The cue ops' *effects* — palette membership, the levels a ramp lands on, the no-op
rules, the 0-step property — are device-free and asserted headlessly (the existing suite's convention); the
SDL decks are *heard* in the [quickstart.md](./quickstart.md) walkthrough (the spec-086/087 split).

**Organization**: Tasks are grouped by user story (US1 palette + transport, US2 level/master shaping, US3
cross-fade) so each is an independently testable increment. Four files are grown across all three stories —
`world/Cue.fs`, `world/CueEngine.fs`, `editor/CueTree.fs`, `editor/CueEditor.fs` — so their tasks are
sequential per story (never `[P]` across stories).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1, US2, US3 (maps to spec.md)
- Exact file paths in every description

## Path Conventions

Single F# project at the repository root; modules cluster by domain (`world/`, `editor/`, `state/`, `test/`).
No new module is added (the ops are `Cue` cases), so **no `.fsproj` compile-list change** beyond the new test
file. `world/Cue.fs` compiles before `world/CueEngine.fs`, which compiles before `editor/CueTree.fs` →
`editor/CueEditor.fs`; a `Cue` case added in `Cue.fs` must be handled by every exhaustive match below it before
the build is clean.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Wire the new test module into the test build.

- [X] T001 In `test/fsharp-game.Tests.fsproj`, insert `<Compile Include="MusicCueTests.fs" />` after `MusicTests.fs`; create `test/MusicCueTests.fs` as a skeleton (`module MusicCueTests`, `open Xunit`, `open Cue`, `open CueEngine`, `open Music`, `open Command`). `dotnet build` is clean and `dotnet test` stays green with the empty module present.

**Checkpoint**: the new suite is registered and compiles empty.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The vocabulary rename (FR-014) — `CrossFade` → `CrossFadeAnimation`, `CrossMorph` →
`CrossFadeMorph` — so the actor cross-fades and the new `CrossFadeMusic` read alike. Done first because it
touches the same lines and exhaustive matches the three stories then grow.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete (the new cases sit beside the
renamed ones in the same matches).

- [X] T002 [P] In `world/Cue.fs`, rename the two cases `CrossFade` → `CrossFadeAnimation` and `CrossMorph` → `CrossFadeMorph` (the declarations and the surrounding doc comment). Pure rename — no field changes.
- [X] T003 [P] In `world/CueEngine.fs`, follow the rename: `beginBlend`'s `opName` now emits `"CrossFadeAnimation"`/`"CrossFadeMorph"`, and the `advance` perform cases are `| CrossFadeAnimation (place, actor, fromClip, toClip, weight, duration) -> …` / `| CrossFadeMorph (…) -> …` (unchanged bodies).
- [X] T004 [P] In `editor/CueTree.fs`, follow the rename in `executing`, `children`, `label`, `kindName`, `defaultOfKind`, `textOf`, `textOf2`, `withText`, `withText2`, `withPlace`, `withActor`, `placeOf` and `drawEditor`; add the `kinds` labels `"Cross fade animation"` / `"Cross fade morph"`, and make `blendOpName` emit exactly those (so `kindName` and the dropdown agree — a mismatch would look like a kind change).
- [X] T005 [P] In `editor/CueEditor.fs`, follow the rename in `drawNodeText`'s `| CrossFadeAnimation (…) | CrossFadeMorph (…) ->` case.
- [X] T006 In `test/CueTests.fs` and `test/CuePanelTests.fs`, follow the rename in the two exhaustive `describe` matches and the label assertions (`"Cross fade animation: Idle -> Jog in Island"`, `"Cross fade morph: A -> B in Island"`). — depends on T002.
- [X] T007 Build gate: `dotnet build` is clean and `dotnet test` is green after the rename alone (no behaviour changed; the suite is the regression gate). — depends on T002, T003, T004, T005, T006.

**Checkpoint**: the vocabulary rename is in and the whole suite passes unchanged in behaviour.

---

## Phase 3: User Story 1 - A cue sets up the palette and starts and stops music (Priority: P1) 🎯 MVP

**Goal**: a cue adds a song to the palette, plays it, stops it, removes it, and can start or stop the whole
bed — each a runtime effect that is never a step on the undo history.

**Independent Test**: headlessly, a cue of `AddMusic`/`PlayMusic`/`StopMusic`/`RemoveMusic` (and
`PlayBed`/`StopBed`) leaves the expected `Mix`: the rows added and removed, and `state.Editor.Undo.Applied`
empty. On a device, walkthrough A of [quickstart.md](./quickstart.md): a cue adds, plays, stops and removes a
song and it is heard.

- [X] T008 [US1] In `world/Cue.fs`, add the six no-progress cases under a spec-088 music banner: `AddMusic of song: string`, `RemoveMusic of song: string`, `PlayMusic of song: string`, `StopMusic of song: string`, `PlayBed`, `StopBed`. (data-model.md; contract)
- [X] T009 [US1] In `world/CueEngine.fs`, add the private helpers `noOpMusic op song reason`, `tryMusicAsset shared song` (read `shared.Assets.AudioAssets.TryGetValue` + `AudioAsset.isReference`; never `Assets.audioAsset`) and `playMusic shared song` (resolve the row's `Path`, guard membership **before** `Music.play`, which raises on a non-row), then perform the six cases in `advance`: `AddMusic` → `shared.Mix <- Music.addRow song shared.Mix` (reported no-op only if already a row is unnecessary — silent, it is idempotent); `RemoveMusic` → `removeRow` then `Music.reconcile shared.Assets.Audio shared.Rack shared.Mix`; `PlayMusic` → `playMusic`; `StopMusic` → row? `Music.stop rack song` : `noOpMusic`; `PlayBed` → `playMusic` per row; `StopBed` → `Music.stop rack row.Asset` per row. Every case returns `ValueSome Fin`. (research D2/D3/D4; data-model VR-001/002/003/004) — depends on T008.
- [X] T010 [US1] In `editor/CueTree.fs`, add the `"Music", [ "Add music"; "Remove music"; "Play music"; "Stop music"; "Play bed"; "Stop bed" ]` kind group, and the six cases to `kindName`, `defaultOfKind` (`AddMusic ""`, …, `PlayBed`, `StopBed`), `label` (the `adventLabel` verb family: `Add music: song`, `Remove music: song`, `Play music: song`, `Stop music: song`, `"Play bed"`, `"Stop bed"`), `executing` (all six are non-executing), `children` (`[]`), `placeOf` (`ValueNone`). Add the writer `withSong : song:string -> cue:Cue -> Cue` (the `withTrigger` shape) that writes the first song field. (data-model.md; FR-013) — depends on T008.
- [X] T011 [US1] In `editor/CueEditor.fs`, add `drawSongCombo` — a pick-only `filteredCombo` over `Assets.musicNames` (the `drawAssetCombo` pattern) whose write is `CueTree.withSong` — and route the four song-bearing ops (`AddMusic`/`RemoveMusic`/`PlayMusic`/`StopMusic`) to it in `drawNodeText`. (data-model.md; FR-013) — depends on T010.
- [X] T012 [P] [US1] In `test/MusicCueTests.fs` (part 1), add the device-free fixtures (`newState`, a `sharedState` whose manager is `TestSupport.manager IntPtr.Zero (Dictionary()) (Dictionary()) (Dictionary())`) and the assertions: `AddMusic` makes a row and a re-add is a no-op; `RemoveMusic` drops it and removing an absent song is a no-op; `PlayMusic`/`StopMusic`/`PlayBed`/`StopBed` on a device-free state return the cue to `Fin` and never raise; and a cue of these ops records **0** steps on `state.Editor.Undo.Applied`. In `test/CueTests.fs` and `test/CuePanelTests.fs`, add the six cases to the two `describe` matches. (FR-001/002/003/004/005/009/010, SC-001/SC-003) — depends on T008, T009.

**Checkpoint**: a cue sets up the palette and transports it from the story — the feature's MVP.

---

## Phase 4: User Story 2 - A cue shapes a song's level, optionally over time (Priority: P2)

**Goal**: a cue moves a song's level (or the whole bed's master) to a target, over a duration or instantly,
continuing from the current level when retargeted mid-ramp.

**Independent Test**: headlessly, `SetMusicLevel song 0.0 2.0` lands the row's weight at 0.0 at the deadline
(± one tick) and at the target in the same frame when the duration is 0; a retarget continues from the current
level; `SetMusicMaster` moves `Mix.Master`. On a device, walkthrough B: a song fades out and back, then the bed
fades out.

- [X] T013 [US2] In `world/Cue.fs`, add the authored ops `SetMusicLevel of song: string * level: float32 * duration: float` and `SetMusicMaster of level: float32 * duration: float`, and the in-flight states `MusicLevelState of song: string * initial: float32 * target: float32 * duration: float * endsAt: float` and `MusicMasterState of initial: float32 * target: float32 * duration: float * endsAt: float`. (data-model.md) — depends on T008.
- [X] T014 [US2] In `world/CueEngine.fs`, add `beginMusicLevel`/`stepMusicLevel` (over `Music.setWeight`) and `beginMusicMaster`/`stepMusicMaster` (over `Music.setMaster`) — capture `initial` from the row (or `Mix.Master`) where reached, hold the `…State` with `endsAt = now + duration`, write the mix each tick with the `stepFade` formula plus `Music.reconcile`, land the target exactly at `now >= endsAt`, and be `Fin` at once when the duration ≤ 0. A level op on a song that is not a palette row is a reported no-op (`noOpMusic`); on a palette row not playing it writes the mix (FR-011). Perform the two authored cases in `advance` and step the two states. (research D2/D3/D5; FR-006/007/011/012) — depends on T013, T009.
- [X] T015 [US2] In `editor/CueTree.fs`, add `SetMusicLevel`/`SetMusicMaster` and their two states to `kindName` (`"Music level"`, `"Music master"`), `defaultOfKind` (default a level of `1.0f` and a duration of `1.0`), `label` (`sprintf "Music level: %s" song`, `"Music master"`), `executing` (the two states are executing; the ops are not), `children` (`[]`), `placeOf` (`ValueNone`); extend `withSong` to `SetMusicLevel`; and in `drawEditor` draw a `Level` (0..1) and a `Seconds` `dragNumber` for `SetMusicLevel`/`MusicLevelState` and for `SetMusicMaster`/`MusicMasterState`, each writing the authored form back. Add the two labels to the `kinds` Music group. (data-model.md; FR-006/007/013) — depends on T013, T010.
- [X] T016 [US2] In `editor/CueEditor.fs`, route `SetMusicLevel` to `drawSongCombo` in `drawNodeText` (the master op carries no song). (FR-013) — depends on T015, T011.
- [X] T017 [P] [US2] In `test/MusicCueTests.fs` (part 2), assert: a level op with a duration lands on its target at the deadline (± one tick) and a duration of 0 lands in the same frame; the row's weight *is* the value; a master op moves `Mix.Master`; a retarget mid-ramp continues from the current level without a jump; a level op on a non-row is a reported no-op; and a level/master ramp records **0** steps. In `test/CueTests.fs`/`test/CuePanelTests.fs`, add the four cases to the `describe` matches. (FR-006/007/011/012, SC-002/SC-003) — depends on T013, T014.

**Checkpoint**: a cue fades a song or the bed in and out from the story.

---

## Phase 5: User Story 3 - A cue cross-fades between two songs (Priority: P3)

**Goal**: a cue swaps one song for another with one op, lowering `from` to silence and raising `to` to full
together over a duration.

**Independent Test**: headlessly, with two palette rows, a cross-fade lands `from`'s weight at 0 and `to`'s at
1 together at the deadline; a missing side is a reported no-op and the other side still moves; a duration of 0
swaps at once. On a device, walkthrough C: two songs, one cross-fade, an audible swap.

- [X] T018 [US3] In `world/Cue.fs`, add `CrossFadeMusic of fromSong: string * toSong: string * duration: float` and `CrossFadeMusicState of fromSong: string * toSong: string * initialFrom: float32 * initialTo: float32 * duration: float * endsAt: float`. (data-model.md) — depends on T008.
- [X] T019 [US3] In `world/CueEngine.fs`, add `beginCrossFadeMusic`/`stepCrossFadeMusic`: capture both rows' weights at the op (an absent row captures 0 and is reported once via `noOpMusic`); return `Fin` with two reports when both sides are absent; each tick write `Music.setWeight from (initialFrom * (1 - t))` and `Music.setWeight to (initialTo + (1 - initialTo) * t)` (each a no-op on an absent row, so the present side still moves — no branch needed) then `Music.reconcile`; at `now >= endsAt` write `0` and `1` exactly and return `Fin`. Perform the authored case in `advance` and step the state. (research D5; FR-008, US3/AC3) — depends on T018, T009.
- [X] T020 [US3] In `editor/CueTree.fs`, add `CrossFadeMusic`/`CrossFadeMusicState` to `kindName` (`"Cross fade music"`), `defaultOfKind` (`CrossFadeMusic("", "", 1.0)`), `label` (`sprintf "Cross fade music: %s -> %s" fromSong toSong`), `executing` (the state executes), `children` (`[]`), `placeOf` (`ValueNone`); add the **second** song writer `withSong2` (the `withText2` shape) for the incoming name; and in `drawEditor` draw a `Seconds` `dragNumber` for `CrossFadeMusic`/`CrossFadeMusicState`. Add the label to the `kinds` Music group. (data-model.md; FR-008/013) — depends on T018, T015.
- [X] T021 [US3] In `editor/CueEditor.fs`, route `CrossFadeMusic` to **two** song choosers (the outgoing name via `withSong`, the incoming via `withSong2`) in `drawNodeText`, the `CrossFadeAnimation` two-field pattern. (FR-013) — depends on T020, T016.
- [X] T022 [P] [US3] In `test/MusicCueTests.fs` (part 3), assert: a cross-fade lowers `from` to 0 and raises `to` to 1 together, landing exactly at the deadline; a duration of 0 swaps at once; a cross-fade with one side absent reports that side and still moves the other; a cross-fade records **0** steps. In `test/CueTests.fs`/`test/CuePanelTests.fs`, add the two cases to the `describe` matches. (FR-008, US3/AC1..AC3) — depends on T018, T019.

**Checkpoint**: all three stories work independently; the story drives palettes, transport, levels and fades.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and end-to-end validation.

- [X] T023 [P] Update `docs/ROADMAP.md`'s cue-op list (`Animate`/`CrossFade`/`Morph`/`CrossMorph` → `Animate`/`CrossFadeAnimation`/`Morph`/`CrossFadeMorph`) and note the music cue ops arriving from spec 088; if `docs/ARCHITECTURE.md` or `docs/EDITOR_DESIGN.md` enumerate the cue vocabulary, add the music group there too. (FR-014; plan)
- [ ] T024 Run [quickstart.md](./quickstart.md): the headless suite, then walkthroughs A–E (palette/transport, level fade, cross-fade, the 0-step property, unavailability) with `SDL_AUDIODRIVER=dummy` where useful. (SC-001…SC-006) — depends on T012, T017, T022.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2, the rename)**: depends on Setup; BLOCKS all stories (the stories grow the matches it touches).
- **User Stories (Phase 3–5)**: all depend on Foundational. US1 is the base (the palette + transport + the `Music` editor group); US2 and US3 layer onto the same four files.
- **Polish (Phase 6)**: depends on the stories it documents and validates.

### User Story Dependencies

- **US1 (P1)**: after Foundational. Independent — the palette and transport are testable headlessly and in walkthrough A.
- **US2 (P2)**: after US1 (extends `world/Cue.fs`, `world/CueEngine.fs`, `editor/CueTree.fs`, `editor/CueEditor.fs`); its level/master assertions are otherwise independent.
- **US3 (P3)**: after US1/US2 (extends the same four files); its cross-fade assertions build on US2's level ramp.

### Within Each Story

- The `Cue` cases before the engine that performs them; the engine before the kind tables; the kind tables before the field editors; implementation before its tests. Each phase's build gate (`dotnet build` clean, `dotnet test` green) must pass before the next story starts.

### Parallel Opportunities

- Foundational: T002, T003, T004, T005 touch different files and are pure renames.
- US1: T012 (tests) is a different file from T008–T011.
- US2: T017 (tests) is a different file from T013–T016.
- US3: T022 (tests) is a different file from T018–T021.
- Polish: T023 is independent of T024.

---

## Parallel Example: Foundational

```text
# Different files, same pure rename:
Task: "Rename the two cases in world/Cue.fs"                 (T002)
Task: "Follow the rename in world/CueEngine.fs"              (T003)
Task: "Follow the rename in editor/CueTree.fs"               (T004)
Task: "Follow the rename in editor/CueEditor.fs"             (T005)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 Setup → 2. Phase 2 Foundational (the rename) → 3. Phase 3 US1 → **STOP and VALIDATE**: a cue adds,
   plays, stops and removes a song (walkthrough A), with 0 steps on the history.

### Incremental Delivery

1. Setup + the rename → the vocabulary is parity-read and the suite is green.
2. US1 → the palette and transport (MVP).
3. US2 → level and master shaping, over time or instant.
4. US3 → the cross-fade.
5. Polish → docs and the walkthrough.

---

## Notes

- [P] = different files, no dependencies on incomplete work.
- The four source files (`world/Cue.fs`, `world/CueEngine.fs`, `editor/CueTree.fs`, `editor/CueEditor.fs`) are grown across all three stories: their tasks are sequential by story, never `[P]` across stories.
- **A cue's music op mutates `shared.Mix`/`shared.Rack` and records 0 steps** — only the Songs panel's edits are `SetMix` steps (FR-009) — verify with the Edit menu's Undo/Redo (walkthrough D).
- **Do not add a runtime level overlay**: a level *is* the mix weight, and the engine composes `Music` (no new `Music` API). Do not add one-shot sounds, start/loop points, ducking or spatialisation (FR-015).
- The `…State` cases carry only the captured level(s), duration and deadline — no deck or handle; a song is addressed by its id, the `Rack`'s key.
- Verify `dotnet build` is clean (the exhaustive matches in `CueEngine`/`CueTree` must gain every new case) and `dotnet test` stays green after each phase.

---

## Phase 7: Convergence

**Purpose**: Reconcile `tasks.md` with the codebase after implement (populated by `/speckit-converge`).

_(empty — no convergence tasks yet)_
