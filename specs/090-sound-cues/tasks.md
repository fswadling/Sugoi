# Tasks: Sound Cues — play a sound from the story

**Input**: Design documents from `/specs/090-sound-cues/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/sound-cue-op.md](./contracts/sound-cue-op.md), [quickstart.md](./quickstart.md)

**Tests**: Included. The op's logic is device-free and asserted headlessly — `Sound.play` records each
voice's asset id as a plain data move, so "the sound was played" is assertable with no device — plus the
derived editor surfaces, as `test/MusicCueTests.fs` asserts the music ops. The audible result is heard in
[quickstart.md](./quickstart.md).

**Organization**: This is one op in a **closed vocabulary** (Constitution N-III), so adding the case
forces every exhaustive match to update **together** — the build is not green until the vocabulary, the
engine, the editor and the tests' `describe` functions all handle `PlaySound`. That compile-forced work
is Phase 2; the user-story phases then carry the **tests** that prove each story (and any story-specific
editor code, which is the chooser in US2).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1, US2, US3 (maps to spec.md)
- Exact file paths in every description

## Path Conventions

Single F# project at the repository root. This feature adds **no module and no compile entry**: it edits
`world/Cue.fs`, `world/CueEngine.fs`, `editor/CueTree.fs`, `editor/CueEditor.fs`, and two test files.

---

## Phase 1: Setup

**Purpose**: Add the op to the closed vocabulary.

- [X] T001 In `world/Cue.fs`, add `| PlaySound of sound: string` beside the music cues (with its doc comment: a no-progress leaf, the runtime counterpart of the Sounds panel's Play, a runtime effect that never lands on the one history), and add it to the module doc's no-progress-op list at the top of the file. (contract sound-cue-op.md; FR-001)

**Checkpoint**: the case exists; the build is now **red** at every exhaustive match — Phase 2 restores it.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Handle the case everywhere the closed vocabulary requires, so the build is green again.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] In `world/CueEngine.fs`, add `trySoundAsset` (the asset with `Class = Sound` and `isResident`, else `None`), `noOpSound` (mirroring `noOpMusic`), and `playSound` (report `"its asset is unavailable"` for an unresolvable id, else `Sound.play shared.Voices sound (AudioAsset.sound asset)`); add the `| PlaySound sound -> do playSound "PlaySound" shared sound; ValueSome Fin` case to `advance`. (contract; FR-001/FR-002/FR-005; research D2/D3)
- [X] T003 [P] In `editor/CueTree.fs`, add `PlaySound` to `executing` (`false`), `label` (`namedLabel "Play sound" sound`), `children` (`[]`), `kindName` (`"Play sound"`), `defaultOfKind` (`"Play sound" -> PlaySound ""`), `withSong` (`PlaySound _ -> PlaySound name`; broaden its doc to "the op's one name"), `drawEditor`'s no-numbers list, and the `kinds` dropdown list (a `"Sound", [ "Play sound" ]` group — found during implementation: without it the op is not offered). (`placeOf` needs no arm: it ends in `| _ -> ValueNone`, which already covers a sound.) (contract; FR-006; research D5)
- [X] T004 [P] In `editor/CueEditor.fs`, generalise `drawSongCombo` to `drawPickCombo` (take `names: string list` and a `label: string`; the music callers pass `Assets.musicNames assets` and `"Song"`), and add `| PlaySound sound ->` to `drawNodeText` drawing it over `Assets.soundAssets assets |> List.map (fun a -> a.Id)` with `CueTree.withSong`. (contract; FR-006; research D4)
- [X] T005 [P] Add `PlaySound` to the `describe` functions in `test/CueTests.fs` and `test/CuePanelTests.fs` (`| PlaySound sound -> sprintf "PlaySound(%s)" sound`). (contract)
- [X] T006 Build clean (`dotnet build`) and the suite green (`dotnet test`) with the op handled everywhere. (FR-010)

**Checkpoint**: the closed vocabulary is satisfied and the whole suite passes with the op wired.

---

## Phase 3: User Story 1 - A cue plays a sound at the point it is reached (Priority: P1) 🎯 MVP

**Goal**: a cue's `PlaySound` op sounds the named sound where the run reaches it, and the cue carries on
at once.

**Independent Test**: headlessly, a cue holding `PlaySound "beep"` over a `SharedState` whose assets
carry a resident `beep` ticks to `Fin` and leaves `shared.Voices.Playing.[0] = "beep"`; the tick adds no
history step. On a device, walkthrough A: author the op and hear the sound.

- [X] T007 [US1] Extend `test/CueTests.fs`: with a resident sound asset inserted into `shared.Assets.AudioAssets`, tick a `seqOf [ PlaySound "beep" ]` and assert the pool recorded `"beep"` and the cue reached `Fin`; a second `PlaySound "beep"` records on the next voice (overlap); the fixture's fake `Audio.Context` records no `LoadSound` call (the op plays the resident handle, FR-002); and `state.Editor.Undo.Applied` is empty (0 steps). (SC-001/SC-002; FR-001/FR-002/FR-004/FR-007) — depends on T002.

**Checkpoint**: a cue plays a sound — the feature's MVP.

---

## Phase 4: User Story 2 - The director authors the sound op in the cue editor (Priority: P2)

**Goal**: the cue editor offers a `Play sound` op and a chooser of the discovered sounds.

**Independent Test**: headlessly, `CueTree` derives the op (label/kind/default/`withSong`/`placeOf`),
and the chooser's write sets the op's sound.

- [X] T008 [US2] Extend `test/CueTests.fs`: `CueTree.label (PlaySound "beep")` is `"Play sound: beep"`
  (and `"Play sound"` when unnamed), `kindName` is `"Play sound"`, `defaultOfKind "Island" "Play sound"`
  is `PlaySound ""`, `CueTree.withSong "x" (PlaySound "beep")` is `PlaySound "x"`, and
  `CueTree.placeOf` is `None` (a sound is global); and `Assets.soundAssets` returns every sound-class
  asset (the chooser's source, SC-004). (SC-004; FR-006) — depends on T003, T004.

**Checkpoint**: the op is authorable and renders in the cue editor.

---

## Phase 5: User Story 3 - A broken or silent audio system never takes the story down (Priority: P3)

**Goal**: an absent/failed sound or an unavailable audio subsystem leaves the cue running.

**Independent Test**: headlessly, an unknown id, a music-class asset, a failed sound and an unavailable
mixer each leave the cue at `Fin` with nothing recorded and nothing raised.

- [X] T009 [US3] Extend `test/CueTests.fs`: a `PlaySound` naming an id no manifest declared, naming a
  **music-class** asset, naming a **failed** sound, and run on an **unavailable** mixer each tick to
  `Fin`, record nothing on the pool, and raise nothing. (SC-003; FR-005) — depends on T002.

**Checkpoint**: all three stories work independently; failure and unavailability are quiet no-ops.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and end-to-end validation.

- [X] T010 [P] Update `docs/ROADMAP.md`: record the sound cue op (`PlaySound`) as landed, and that the remaining deferred audio families are a sound palette, per-sound volume, looping, start/end points, cross-fade, ducking and spatialisation. (spec Origin/Assumptions)
- [X] T011 [P] Confirm no new dependency or module: the op composes `audio/Sound.fs`/`AudioAsset` as-is, and no `<PackageReference>`, `.fsproj` entry, shader or native change is added. (plan Technical Context; contract)
- [X] T012 Run [quickstart.md](./quickstart.md): the headless tests, the whole suite, then walkthroughs A–B (author and hear a sound; failure/unavailable) with `SDL_AUDIODRIVER=dummy` where useful. (SC-001…SC-005)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies (leaves the build red by design).
- **Foundational (Phase 2)**: depends on Setup; BLOCKS all stories — it satisfies the closed vocabulary.
- **User Stories (Phase 3–5)**: depend on Foundational; they carry the tests (US2 also carries the
  chooser, which is in T004).
- **Polish (Phase 6)**: depends on the stories it documents and validates.

### User Story Dependencies

- **US1 (P1)**: after Foundational (T002 implements the play); its test is independent.
- **US2 (P2)**: after Foundational (T003/T004 render and author it).
- **US3 (P3)**: after Foundational (T002's no-op branch); its test is independent.

### Within Each Phase

- The vocabulary case (T001) before everything; the engine and editor before their tests.

### Parallel Opportunities

- Foundational: T002 (`world/CueEngine.fs`), T003 (`editor/CueTree.fs`), T004 (`editor/CueEditor.fs`) and
  T005 (the two test files) are different files — parallel.
- `test/CueTests.fs` is one file grown across US1–US3 (T007/T008/T009): those are **sequential**.
- Polish: T010 and T011 are different files.

---

## Parallel Example: Foundational

```text
# Different files, all after T001:
Task: "Add trySoundAsset/playSound + the advance case in world/CueEngine.fs"  (T002)
Task: "Handle PlaySound in editor/CueTree.fs"                                  (T003)
Task: "Generalise the chooser + handle PlaySound in editor/CueEditor.fs"       (T004)
Task: "Add PlaySound to the test describe functions"                           (T005)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 Setup → 2. Phase 2 Foundational → 3. Phase 3 US1 → **STOP and VALIDATE**: a cue playing
   `PlaySound` sounds the sound and reaches `Fin` (the headless test), or walkthrough A on a device.

### Incremental Delivery

1. Setup + Foundational → the op handled everywhere; suite green.
2. US1 → a cue plays a sound (MVP).
3. US2 → the op is authorable and renders.
4. US3 → failure and unavailability are quiet no-ops.
5. Polish → docs and the walkthrough.

---

## Notes

- [P] = different files, no dependencies.
- This feature is **one op in a closed vocabulary**: the case cannot land without its engine, editor and
  test-match updates, so the implementation is all in Foundational and the stories carry their tests.
- Implementation found one extra edit beyond the plan: `CueTree.kinds` (the kind dropdown) needs a
  `"Sound"` group, or the op cannot be authored (folded into T003).
- The op is a **runtime effect**: it records no history step (FR-004) — verify in the Edit menu.
- One-shot only, `PlaySound` only: no palette, level, loop, points, cross-fade, ducking, spatialisation
  or stop (FR-008/FR-009) — do not add them here.
- Verify `dotnet test` stays green after Phase 2.

---

## Phase 7: Convergence

**Purpose**: Reconcile `tasks.md` with the delivered tests after implement. The feature itself
(FR-001…FR-010, SC-001…SC-005) is fully implemented and green; two `partial` gaps remained, each an
assertion T007/T008 promised that the code did not make — both now added.

- [X] T013 Assert in `test/CueTests.fs`, as T007 promised, that a `PlaySound` naming a **resident** sound does not raise on an unavailable context — making explicit what is now only implicit: the op plays the resident handle and never re-decodes (`Audio.unavailable.LoadSound` raises, so a decode attempt would fail). per FR-002/T007 (partial)
- [X] T014 Assert in `test/CueTests.fs`, as T008 promised, that `Assets.soundAssets` returns every sound-class asset and none of the music-class ones — the cue chooser's source (SC-004). (The same readout is asserted in `test/SoundTests.fs`, spec 089; this is the feature-local check T008 asked for.) per SC-004/T008 (partial)
