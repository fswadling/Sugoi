---

description: "Task list for feature implementation"
---

# Tasks: Retire the Cast — Actors and Speakers Addressed by Name

**Input**: Design documents from `/specs/074-retire-cast/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/portraits-by-name.md

**Tests**: Included — the project mandates headless verification (constitution N-V) and the feature's success
criteria are test-based (`test/DialogueTests.fs`, a new asset-name-index test, and the narrative suites as the
regression proof).

**Organization**: As with `specs/072`/`specs/073`, this is a single cohesive refactor: the speaker's type changes
everywhere at once, so the Foundational phase migrates the game's types and removals and keeps the tree compiling;
the story phases then deliver the verifiable increments (the by-name resolution test, the "cast is gone" proof,
the asset-manager choosers).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US3)
- Paths are repository-relative.

## Path Conventions

Single project, F# — flat clusters at the repository root: `cast/` (removed), `narrative/`, `world/`, `state/`,
`editor/`, `screen/`, `test/`. Compile order is fixed by `fsharp-game.fsproj`; this feature moves one entry and
removes three.

---

## Phase 1: Setup (Shared Infrastructure)

- [x] T001 Confirm the only build-file change is `fsharp-game.fsproj`: drop `cast/Roster.fs`, `world/Casting.fs` and `editor/CastPanel.fs`; add `narrative/Roster.fs` first among the narrative files. No new package; `test/fsharp-game.Tests.fsproj` drops one entry (T016).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Move the roster out of the game, make the speaker a string, re-base resolution on the asset manager,
and remove the cast — keeping the tree compiling. **⚠️ No user-story work can begin until this is complete.**

- [x] T002 [P] Move `cast/Roster.fs` → `narrative/Roster.fs` (keep the `module Roster` declaration so `narrative/*.fs` and `test/RosterTests.fs` compile unchanged); rewrite its header comment to say it is the narrative engine's vocabulary; update `fsharp-game.fsproj` (drop the `cast/` `<Compile>`, add `narrative/Roster.fs` before `narrative/Vocabulary.fs`)
- [x] T003 [P] `world/Dialogue.fs`: `Kind.PortraitDialogue of speaker: string * Emotion`; drop the `Roster` dependency; update the module comment (it no longer names a character)
- [x] T004 [P] `world/Assets.fs`: add `ActorsByName : Dictionary<string, ActorAsset.ActorAsset>` to `AssetManager`; add a private `indexByName` (iterate the actors sorted by `Id`, keep the first per `Name` — the lowest-`Id` tie-break); build it in `loadActors`; add `actorNames : AssetManager -> string list` (sorted, distinct)
- [x] T005 `world/DialogueRender.fs`: `tryPortrait (assets) (speaker: string) (emotion)` reads `assets.ActorsByName` (contains + indexer, no tuple); drop the `cast` parameter from `missReason`/`draw`; `speaker` returns the string directly; reduce `MissReason` to `NotMissed | NoAsset | NoPortraits | TooTallForViewport` (depends T003, T004)
- [x] T006 [P] `state/SharedState.fs`: remove `Cast : Casting.Cast`; `create` no longer builds one
- [x] T007 Delete `world/Casting.fs` and `editor/CastPanel.fs`; update `editor/Editor.fs` (drop the `"Cast"` entry from `panelNames` and the `CastPanel.draw shared &state` call); update `screen/GameplayScreen.fs` (drop `shared.Cast` from the `DialogueRender.draw` call) (depends T005, T006)
- [x] T008 [P] `editor/DialoguePanel.fs`: the speaker combo offers `Assets.actorNames shared.Assets`; a new portrait defaults to the first name (or `""`); reword `advisory` (no Cast panel); drop the `cast` argument (depends T005)
- [x] T009 `editor/CueTree.fs`: `drawEditor` gains a `names: string list` parameter; the `Exposit` speaker combo offers it; a newly-chosen portrait defaults to the first name (or `""`) instead of `List.head Roster.characters` (depends T003)
- [x] T010 `editor/CueEditor.fs`: pass `Assets.actorNames shared.Assets` into both `CueTree.drawEditor` call sites (the Cue panel and the inline trigger editor) (depends T004, T009)
- [x] T011 `dotnet build fsharp-game.fsproj` is clean with no warnings (depends T002–T010)

**Checkpoint**: the roster is the engine's, the speaker is a string, the cast is gone, and the game builds.

---

## Phase 3: User Story 1 - A portrait line's speaker is a string, resolved by name (Priority: P1) 🎯 MVP

**Goal**: A portrait line resolves its speaker to an actor asset by name, with no binding and no roster; the
dialogue suite is migrated and green.

**Independent Test**: show a portrait line naming a speaker and an expression against an asset manager holding an
asset of that name and assert its declared portrait is resolved.

### Tests for User Story 1

- [x] T012 [P] [US1] New `test/AssetNameIndexTests.fs`: assert the name index resolves by `Name` exactly, the lowest `Id` wins a shared name, a missing name resolves to none, and the `Name`→`Id` fallback behaves
- [x] T013 [US1] `test/DialogueTests.fs`: retarget to string speakers (`PortraitDialogue ("Akane", Sad)`), delete the `castBinding`/`emptyCast` helpers, and call the new `tryPortrait (assets) (speaker) (emotion)`; keep the `allocates nothing per call` guard
- [x] T014 [P] [US1] Drop `Cast = { Casting.Bindings = … }` from every `SharedState` literal in `test/` (`ActorNameTests.fs`, `AdventTests.fs`, `CuePanelTests.fs`, `CueTests.fs`, `DialogueTests.fs`, `GameplayStateTests.fs`, `NarrativeHarnessTests.fs`, `ScenarioTests.fs`, `SelectionTests.fs`, `SpawningTests.fs`)
- [x] T015 [P] [US1] Migrate `Dialogue.Kind.PortraitDialogue (Roster.X, …)` → a string in `test/CueTests.fs` and `test/CuePanelTests.fs`
- [x] T016 [US1] Delete `test/CastingTests.fs` and remove its `<Compile>` from `test/fsharp-game.Tests.fsproj`
- [x] T017 [US1] `dotnet test` — the suite is green, with no device (depends T012–T016)

**Checkpoint**: the by-name resolution is verified and the suite runs.

---

## Phase 4: User Story 2 - The cast leaves the game (Priority: P1)

**Goal**: Prove the cast is gone from the game and the roster retirement cost the engine nothing.

**Independent Test**: grep the game for cast types; run the narrative suites unmodified.

- [x] T018 [US2] Sweep the game for `Casting`, `Look`, `Slot`, `Bindings`, `Cast`, and `Roster.`/`open Roster` (expect none in `world/`, `state/`, `editor/`, `screen/`) and confirm `cast/` no longer exists (SC-002)
- [x] T019 [P] [US2] Confirm `test/RosterTests.fs` and the narrative suites (`Narrative*Tests.fs`) are unmodified and pass — the proof the relocation cost the engine nothing (SC-003)
- [x] T020 [P] [US2] Confirm `editor/Editor.fs` no longer registers a "Cast" window and the remaining panels lay out (no `CastPanel` reference anywhere)

**Checkpoint**: the cast is provably gone; the engine is provably untouched.

---

## Phase 5: User Story 3 - Speakers are chosen from discovered actors (Priority: P2)

**Goal**: The speaker choosers offer discovered actor names, and nothing else changes.

**Independent Test**: assert `Assets.actorNames` returns the discovered names; confirm both choosers read it.

- [x] T021 [P] [US3] Test `Assets.actorNames` (sorted, distinct) — a small pure test in `test/AssetNameIndexTests.fs`
- [x] T022 [US3] Confirm both speaker choosers offer the discovered names and no roster (`editor/CueTree.fs` `drawEditor` fed by `editor/CueEditor.fs`; `editor/DialoguePanel.fs`), and that the expression combo, layout and drawing rules are unchanged
- [x] T023 [US3] `dotnet test` — the full suite is green with no device opened (depends T021, T022)

**Checkpoint**: the choosers read the asset manager; portraits resolve regardless of placement.

---

## Phase 6: User Story 4 - An in-flight actor op resolves her once (Priority: P2)

**Goal**: The in-flight actor state cases carry the resolved slot, so the tick is an O(1) lookup with no scan and
no allocation.

**Independent Test**: begin a move and assert the op reuses the same actor (a same-named newcomer does not steal
it), ends when the actor is removed, and adds no allocation per tick.

- [x] T024 [P] [US4] `world/Cue.fs`: the actor `…State` cases (`MoveActorState`, `RotateActorState`, `BlendState`) gain `place: Place * handle: Handle` (from `core/SlotMap`); `open SlotMap`
- [x] T025 [US4] `world/CueEngine.fs`: `beginMove`/`beginRotate`/`beginBlend` store `handleOf ref` and `place`; `stepMove`/`stepRotate`/`stepBlend` look the actor up in O(1) (`ContainsKey` + indexer, `IsAlive`/`Get`) and end the op on an absent place / dead slot / changed name; the one-shot ops keep the name scan (depends T024)
- [x] T026 [P] [US4] `test/CueTests.fs`: assert the in-flight ops reuse the actor found at begin (a same-named newcomer does not steal it; a mid-flight removal ends the op), alongside the existing actor-op suite
- [x] T027 [US4] `test/CueTests.fs`: an allocation guard on `stepMove` (`MoveActorState`) — 0 bytes per tick, beside the existing cue allocation properties (SC-008)
- [x] T028 [US4] `dotnet test` — the suite is green with no device opened (depends T024–T027)

**Checkpoint**: an in-flight actor op is O(1) and allocation-free per tick.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [x] T029 [P] Update the cue-actor passage in `docs/ARCHITECTURE.md` (an in-flight op resolves once, carrying the handle)
- [x] T030 [P] Update the cast / portrait passages in `docs/ARCHITECTURE.md` (the roster is the engine's; the speaker is a string; the cast binding is gone)
- [x] T031 [P] Update `docs/PROJECT_STRUCTURE.md` (the `cast/` cluster is gone; `narrative/Roster.fs`; the compile order)
- [x] T032 [P] Update the phase-10 cast passage in `docs/ROADMAP.md`
- [x] T033 [P] Update the Cast panel entry in `docs/EDITOR_DESIGN.md` (D-022)
- [x] T034 Run the `quickstart.md` validation: `dotnet build` warning-free and `dotnet test` green with no device

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup — **BLOCKS all user stories** (the tree cannot compile until the
  types migrate and the cast is removed).
- **User Stories (Phases 3–6)**: US1 depends on Foundational; US2, US3 and US4 depend on US1 (the test project
  only compiles once the shared-state literals, `DialogueTests` and `CastingTests` are dealt with in US1).
- **Polish (Phase 7)**: after the desired stories.

### Within Each Story

- `narrative/` and `world/` before `state/` before `editor/` before `screen/` (compile order).
- Tests accompanying the change they cover.

### Parallel Opportunities

- T002, T003, T004, T006 (different files).
- T012, T014, T015 (different test files).
- T019, T020 (different checks); T021 (test) beside T022 (inspection).
- T024 (a `world/Cue.fs` type change) beside T026/T027 (tests), once T025 lands.
- T029–T033 (different docs).

---

## Parallel Example: Foundational

```bash
# The independent leaves of the change:
Task: "T002 move cast/Roster.fs → narrative/Roster.fs and update fsharp-game.fsproj"
Task: "T003 world/Dialogue.fs — PortraitDialogue of speaker: string * Emotion"
Task: "T004 world/Assets.fs — ActorsByName index + actorNames"
Task: "T006 state/SharedState.fs — drop Cast"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → Phase 2 Foundational (critical — blocks everything).
2. Phase 3 US1 → **STOP and VALIDATE** (`dotnet test`).
3. US1 alone delivers the feature's core: a speaker string resolving to her actor asset by name.

### Incremental Delivery

1. Setup + Foundational → the speaker is a string, the cast is gone, the game builds.
2. US1 → the resolution is verified and the suite is green → MVP.
3. US2 → the removal is proven; the engine is proven untouched.
4. US3 → the choosers read the asset manager.
5. US4 → an in-flight actor op resolves once (O(1), allocation-free per tick).
6. Polish → docs and quickstart.

---

## Notes

- [P] = different files, no dependencies.
- Phase 2 is atomic in practice: the tree cannot compile between the type change and the editor/panel migration.
- No new module in the game; one module moves into `narrative/`; three `<Compile>` entries are removed and one
  added.
- The narrative engine and its tests (`test/RosterTests.fs`, `Narrative*Tests.fs`) are the regression proof and are
  **not** edited.
- US4 changes `world/Cue.fs` and `world/CueEngine.fs`, which the earlier phases leave untouched; it adds
  `core/SlotMap.fs`'s `Handle` to the cue vocabulary (legal — `core/` is below `world/`).
- US4 also removed three residual per-frame allocations so the step guard holds at 0 bytes: `Physics.getBodyTransform`
  now returns a `struct (Vector3 * Quaternion)`, and `Actor.tryFindAnimIndex` / `ActorRender.setMorphWeight` use
  loops rather than `Array.tryFindIndex` closures (research D13). That touches `physics/Physics.fs`,
  `world/Actor.fs`, `world/ActorRender.fs`, `world/Systems.fs` and the callers in tests.
- Commit after each task or logical group.

---

## Phase 8: Convergence

- [x] T035 Add a typed fallback to both speaker choosers (the cue `Exposit` editor and the Dialogue panel), so a speaker whose asset is not yet discovered can still be authored — the spec's edge case "A speaker named before her asset exists" per FR-009 / US3-AC1 (partial)
- [x] T036 State the actor-name → portraits contract (`assets/actors/*` manifest `Name` is the speaker; declare `[Portraits …]`) in an author-facing document, not only the editor advisory per FR-012 (partial)
- [x] T037 Add allocation guards for an in-flight rotate and an in-flight blend beside the move guard (`test/CueTests.fs`), covering `stepRotate` and the morph-lookup path of `stepBlend` per FR-016 / SC-008 (partial)
- [x] T038 Fix the stale comments in `world/DialogueRender.fs` ("*(character, emotion)* pair") and `editor/DialoguePanel.fs` ("beside the cast binding") per FR-013 (partial)
