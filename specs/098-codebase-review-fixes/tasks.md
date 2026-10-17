---
description: "Task list for Codebase Review Remediation"
---

# Tasks: Codebase Review Remediation

**Input**: Design documents from `specs/098-codebase-review-fixes/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/remediation-invariants.md`

**Tests**: Required. The spec's success criteria and the plan require regression tests for every corrected behaviour and allocation assertions for every named path; the existing 705 tests must stay green.

**Organization**: Tasks are grouped by user story so each story is independently implementable and testable. Register IDs (`F-xx`, `V-xx`) trace each task to `spec.md`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US5
- Every task names the file(s) it changes

## Path Conventions

Single F# project at the repository root: `core/`, `render/`, `audio/`, `world/`, `state/`, `editor/`, `screen/`, `narrative/`, `test/`, `docs/`, `assets/`.

---

## Phase 1: Setup

**Purpose**: Establish the baseline and resolve the spike items before touching code.

- [X] T001 Record the baseline by running `dotnet build` and `dotnet test test/Sugoi.Tests.fsproj`; note test count, warning count and duration in `specs/098-codebase-review-fixes/research.md`.
- [X] T002 [P] Confirm the SDL_gpu `cycle` contract and record the evidence in `specs/098-codebase-review-fixes/research.md`; F-02 is applied unconditionally for recurring maps (F-02).
- [X] T003 [P] Spike the verify-only items F-32, F-33, F-43, F-88, V-03 and V-08; record each outcome in `specs/098-codebase-review-fixes/research.md`. V-03 and V-08 are verify-only: no change is made in 098 without a new or amended FR.
- [X] T004 [P] Spike F-27 by checking whether the exact affected sway subtree is available; record fix/exclude in `specs/098-codebase-review-fixes/research.md` (FR-020 is conditional).

**Checkpoint**: Baseline and spike outcomes recorded; no production code changed.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: One headless test-support layer that every story's regression tests use.

**⚠️ CRITICAL**: No story test task can start until these fixtures exist.

- [X] T005 [P] Add `headlessState`, `headlessShared`, `managerWith`, `emptyManager` and `stateWithZone` fixtures in `test/TestSupport.fs`.
- [X] T006 Add `entity`, `actor`, `prop`, `part`, `triangleSubmesh`, `roomAsset`, `crateAsset` and `populatedClassroom` fixtures in `test/TestSupport.fs` (after T005).
- [X] T007 Add `assertAllocatesNothing`, `withTempDir` and `assertNear` helpers in `test/TestSupport.fs` (after T005).
- [X] T008 Add the shared test-only projections/events and a shared cue description in `test/NarrativeTestSupport.fs` and `test/TestSupport.fs` (after T005).
- [X] T009 Run `dotnet build` and the existing suite to confirm the support layer changes no behaviour; record the result in `specs/098-codebase-review-fixes/research.md`.

**Checkpoint**: Shared fixtures and helpers compile; the suite is still 705/705.

---

## Phase 3: User Story 1 - Authored data cannot crash, corrupt or be lost (Priority: P1) 🎯 MVP

**Goal**: Fix the confirmed safety and data-integrity findings (F-01 to F-10).

**Independent Test**: Type format specifiers into every name field; force a write failure and restore it; drag an actor; switch A→B→A; add a second actor with its own texture directory; press an out-of-range digit key.

### Tests for User Story 1

- [X] T010 [P] [US1] Add the `Key.Digit 0/4/10` raise and `1..3` mask-uniqueness cases in `test/InputTests.fs`.
- [X] T011 [P] [US1] Add the failed-write-then-retry and content-based no-write cases in `test/ProjectTests.fs`.
- [X] T012 [P] [US1] Add the manifest `Textures` field, default-directory and `textureDirectory` cases in `test/AssetsTests.fs`.
- [X] T013 [P] [US1] Add the environment-switch registry reset/release case in `test/EnvironmentBatchTests.fs`.
- [X] T014 [P] [US1] Add the actor move/rotate body-preservation and scale-rebuild case in `test/ZoneTests.fs`.
- [X] T015 [US1] Add the actor and prop partial-load rollback cases (or a recorded integration-only exclusion) in `test/AssetsTests.fs` (after T012). Recorded integration-only exclusion: the headless seam cannot force a mid-build GPU/texture failure.
- [X] T016 [P] [US1] Add the panel-list (Songs present, retired panel absent) check in `test/CuePanelTests.fs`. Recorded integration-only exclusion: the list is private and cycling needs an ImGui frame.

### Implementation for User Story 1

- [X] T017 [US1] Bind `igTextUnformatted` and add `textUnformatted` in `render/ImGui.fs` (F-01).
- [X] T018 [US1] Replace every dynamic `ImGui.text` call in `render/DebugOverlay.fs`, `editor/AdventPanel.fs`, `editor/Inspector.fs`, `editor/ScenarioPanel.fs`, `editor/SongPanel.fs`, `editor/SoundPanel.fs`, `editor/CueTree.fs`, `editor/DialoguePanel.fs` with `textUnformatted` (F-01).
- [X] T019 [US1] Map recurring transfer buffers with `cycle = true` in `render/InstanceBuffer.fs`, `render/DebugDraw.fs`, `render/Ui.fs` and `world/render/ActorRender.fs` (F-02).
- [X] T020 [US1] Add `writeFailed`/bounded retry and `persistFrame` in `world/Project.fs`, updating `lastWritten`/`lastText` only on success (F-03).
- [X] T021 [US1] Call `Project.persistFrame frame.Dt` from `screen/GameplayScreen.fs` (F-03).
- [X] T022 [US1] Add the `Textures` manifest field and return the `Manifest` record from `readManifest`/`scanDir` in `world/assets/Assets.fs`; update `assets/actors/Akane/akane.manifest` (F-04, F-59).
- [X] T023 [US1] Add `textureDirectory` to `world/assets/ActorAsset.fs` and use it in `loadActorAsset` (`world/assets/Assets.fs`) (F-04).
- [X] T024 [US1] Add `reset` to `world/render/EnvironmentBatch.fs` and call it on environment swap and zone release in `world/Zone.fs` (F-05).
- [X] T025 [US1] Move an actor body in place in `world/Zone.fs` using `Physics.setBodyTransform`, keeping the scale rebuild (F-06).
- [X] T026 [US1] Guard `Key.Digit` to `1..3` with `invalidArg` in `core/Input.fs` (F-07).
- [X] T027 [US1] Release acquired textures, meshes and storage buffers when `loadActorAsset` or the prop `buildSync`/`advanceCursor` path fails partway in `world/assets/Assets.fs` (F-09).
- [X] T028 [US1] Replace `"Zone Registry"` with `"Songs"` in `editor/Editor.fs` (F-10).
- [X] T029 [US1] Remove or document the `EnvironmentRender.release` no-op in `world/render/EnvironmentRender.fs`/`world/Entity.fs` per T003 (F-43).
- [X] T030 [US1] Run the US1 test filters and the manual checks in `quickstart.md` sections 1–4; fix regressions.

**Checkpoint**: US1 is independently green and manually verified.

---

## Phase 4: User Story 2 - Frame cost follows change, not scene size (Priority: P1)

**Goal**: Remove the confirmed per-frame gathers, panel bodies, sorts, locks and allocations (F-11 to F-35).

**Independent Test**: Idle frames perform zero static gathers/uploads and zero hidden-panel work; allocation harnesses report zero bytes on the named paths; a music ramp touches only its row.

### Tests for User Story 2

- [X] T031 [P] [US2] Add clean-batch `gather`/`upload` cases, including 1,000 consecutive clean frames with zero gathers and zero uploads, in `test/EnvironmentBatchTests.fs`.
- [X] T032 [P] [US2] Add asset-name cache reuse/invalidation cases in `test/AssetsTests.fs`.
- [X] T033 [P] [US2] Add zero-allocation assertions for camera follow, dialogue/prompt layout and diagnostics reads in `test/PlayerTests.fs`, `test/DialogueTests.fs`, `test/PromptTests.fs` and `test/DiagnosticsTests.fs`.
- [X] T034 [P] [US2] Add targeted-music-ramp and no-full-reconcile cases in `test/MusicCueTests.fs`.
- [X] T035 [P] [US2] Add the actor palette/model single-computation, blend frame-window, unchanged-locomotion clip fast-path and `modelMatrix` equivalence cases in `test/ActorTests.fs`.
- [X] T036 [US2] Add the geometric instance-growth case in `test/EnvironmentBatchTests.fs` (after T031).
- [X] T037 [P] [US2] Add the blocked-fork idle-shape, zero-allocation and IQM retained-field cases in `test/CueTests.fs`/`test/IqmTests.fs`.

### Implementation for User Story 2

- [X] T038 [US2] Add `Dirty`, `markDirty`, bool `gather` and the upload gate in `world/render/EnvironmentBatch.fs`; return the bool from `Zone.gatherStatic` in `world/Zone.fs` and gate `EnvironmentBatch.upload` in `screen/GameplayScreen.fs` (F-11).
- [X] T039 [US2] Mark the batch dirty at prop/part transform, add, remove, environment activation and `occupyZone` sites in `world/Zone.fs` (F-11).
- [X] T040 [P] [US2] Guard every panel body on the `beginWindow` result in `editor/AdventPanel.fs`, `AssetBrowser.fs`, `CueEditor.fs`, `CueTree.fs`, `DialoguePanel.fs`, `Inspector.fs`, `MiscPanel.fs`, `Outliner.fs`, `ScenarioPanel.fs`, `SongPanel.fs`, `SoundPanel.fs`, `screen/MainMenuScreen.fs` and `render/DebugOverlay.fs`; move pre-window setup inside (F-12).
- [X] T041 [P] [US2] Add the three name caches and `invalidateNameCaches` in `world/assets/Assets.fs`; update `scan`/`release` (F-13).
- [X] T042 [US2] Cache cue rows by cue value in `editor/CueTree.fs` (after T040) (F-14).
- [X] T043 [US2] Add the unchanged-pin fast path in `screen/GameplayScreen.fs`, `world/assets/AssetLoad.fs` and `world/assets/Assets.fs` (after T038/T041) (F-15).
- [X] T044 [P] [US2] Cache the editor grid and use a byref accumulator in `editor/Viewport.fs`/`render/DebugDraw.fs` (F-16).
- [X] T045 [US2] Replace the four outliner slot loops with one row helper; use `pushId` with static `Del`/`Dup`/`Link` labels and avoid per-row `sprintf` identifiers in `editor/Outliner.fs` (after T040) (F-17).
- [X] T046 [US2] Replace `%A` transform formatting with numeric formatting in `editor/Inspector.fs` (after T040) (F-18).
- [X] T047 [US2] Return the live report storage from `Diagnostics.recent` in `core/Diagnostics.fs`; cache the fps line and the formatted report rows in `render/DebugOverlay.fs`, rebuilding only when the report changes (after T040) (F-19).
- [X] T048 [P] [US2] Return a struct tuple from `cameraPose` in `world/Systems.fs` (F-20).
- [X] T049 [P] [US2] Return struct tuples from `Ui.setLine` in `render/Ui.fs`, `DialogueRender.content` in `world/render/DialogueRender.fs` and the prompt measurement in `world/render/PromptRender.fs` (F-21).
- [X] T050 [P] [US2] Upload the skinning palette directly and cache the posed model/normal matrix in `world/render/ActorRender.fs`; compute the normal matrix once in `render/Mesh.fs` (F-22).
- [X] T051 [P] [US2] Precompute per-slot animation frame windows and write slots byref in `world/Actor.fs` (F-23).
- [X] T052 [P] [US2] Add a targeted `Music.applyRow` and use it from `state/CueEngine.fs`; reconcile only on structural changes (F-24).
- [X] T053 [P] [US2] Grow the instance buffer geometrically and reduce `reclaim` churn in `render/InstanceBuffer.fs` (F-25).
- [X] T054 [P] [US2] Bind `&slots.[i]` before the alive test in `core/SlotMap.fs` (F-26).
- [X] T055 [US2] If T004 confirms the exact affected sway subtree is available, recompose only that subtree in `world/Sway.fs`; otherwise record the exclusion (FR-020 is conditional). Excluded: `Iqm.Model` has no exact subtree index.
- [X] T056 [US2] Persist the rewritten `Parallel` shape for a blocked `Fork` in `state/CueEngine.fs` (after T052) (F-28).
- [X] T057 [US2] Cache the current locomotion clip and skip the rebuild in `world/Systems.fs`/`world/render/ActorRender.fs` (after T048/T050) (F-29).
- [X] T058 [US2] Hoist the gather closures and cache submesh slots/lookups in `world/render/EnvironmentBatch.fs` (after T038) (F-30).
- [X] T059 [P] [US2] Write the translation row directly in `Entity.modelMatrix` in `world/Entity.fs` (F-31).
- [X] T060 [P] [US2] Drop unread IQM geometry/header fields and store frame channels as `uint16` in `core/Iqm.fs` (F-35).
- [X] T061 [US2] Apply the load-path list/pin reductions from T003 to `world/assets/AssetLoad.fs`/`Assets.fs` only if measured (after T041/T043) (F-32). Excluded: bounded by `LoadQueueCapacity`.
- [X] T062 [US2] Apply the `SlotMap.Occupy` link-only write from T003 if it compiles (after T054) (F-33).
- [X] T063 [US2] Run the US2 test filters and the SC-015 editor-idle checks plus the idle-frame/allocation checks in `quickstart.md` sections 5–8.

**Checkpoint**: US2 is independently green; idle scene/panel/music costs match the success criteria.

---

## Phase 5: User Story 3 - One implementation per concept (Priority: P2)

**Goal**: Remove the second stepper and duplicated helpers (F-36 to F-63); each named pattern has exactly one implementation.

**Independent Test**: Grep counts are one for each helper; the compiler finds no deleted-export caller; the suite is green.

### Tests / verification for User Story 3

- [X] T064 [P] [US3] Add or move `UndoStack` tests to `Undo.undoHistory`/`redoHistory` (or `Command.apply`/`undo`) in `test/SpawningTests.fs`, `test/ZoneTests.fs` and `test/SelectionTests.fs`.

### Implementation for User Story 3

- [X] T065 [US3] Delete `UndoStack.undo`, `UndoStack.redo`, `undoCommand` and `applyCommand` from `state/Command.fs` (F-36).
- [X] T066 [US3] Replace the six apply-and-record blocks in `editor/Selection.fs` and the one in `editor/AssetBrowser.fs` with `Undo.commit` (F-37).
- [X] T067 [US3] Replace the three state-level command matches with one classifier in `state/Command.fs` (F-38).
- [X] T068 [P] [US3] Add `bufferText`, `seedText` and `selectable` to `render/ImGui.fs`; delete the 7/3/3 copies in `editor/AdventPanel.fs`, `ScenarioPanel.fs`, `Outliner.fs`, `Inspector.fs`, `CueEditor.fs`, `CueTree.fs`, `DialoguePanel.fs`, `SongPanel.fs` (F-47).
- [X] T069 [P] [US3] Add one pipeline builder and one shared shader load in `render/Gpu.fs`; route `render/DebugDraw.fs` and `render/Ui.fs` through it (F-52).
- [X] T070 [US3] Replace the local `loadShader`, sampler and `writeMatrix` copies in `render/DebugDraw.fs`/`render/Ui.fs` with `Gpu`/`Texture` helpers (after T069) (F-53).
- [X] T071 [US3] Extract one cue-editor pipeline used by both `CueEditor.draw` and `CueEditor.drawTrigger` in `editor/CueEditor.fs` (after T068) (F-48).
- [X] T072 [US3] Extract one dialogue kind/speaker/expression control used by `editor/DialoguePanel.fs` and `editor/CueTree.fs` (after T068) (F-49).
- [X] T073 [US3] Extract one waypoint/trigger/actor name combo in `editor/CueEditor.fs` (after T068/T071) (F-50).
- [X] T074 [US3] Extract shared colour-picker and committed-text scaffolds in `editor/Inspector.fs` (after T068) (F-51).
- [X] T075 [P] [US3] Extract one glTF float-array helper in `core/Gltf.fs` (F-54).
- [X] T076 [US3] Add `Entity.capsuleOffset` and use it in `world/Zone.fs`, `world/Systems.fs` and `state/CueEngine.fs` (F-55).
- [X] T077 [US3] Extract one body traversal for deactivate/activate/release in `world/Zone.fs` (after T076) (F-56).
- [X] T078 [US3] Extract one by-name slot scan in `world/Zone.fs` (after T077) (F-57).
- [X] T079 [US3] Use `Entity.baseEntity` and one asset-binding constructor in `world/Zone.fs` (after T077) (F-58).
- [X] T080 [US3] Extract one load-report helper and keep the `Manifest` record in `world/assets/Assets.fs` (F-59; continues T022).
- [X] T081 [US3] Extract one `progress`/`rampAt` rule and one single-pass waypoint-chain resolution in `state/CueEngine.fs` (after T076) (F-60, F-61).
- [X] T082 [US3] Move actor body detach into `world/Zone.fs` and call it from `state/CueEngine.fs` and `state/ScenarioFork.fs` (F-62).
- [X] T083 [US3] Collapse the five `noOp*` variants in `state/CueEngine.fs` (after T081) (F-39).
- [X] T084 [P] [US3] Remove the `LoadPool` forwarders and dead accessors in `world/assets/AssetLoad.fs` (F-40, F-42).
- [X] T085 [US3] Remove the confirmed dead exports listed in F-41 across `core/Lru.fs`, `core/Input.fs`, `core/Gltf.fs`, `core/Maths.fs`, `audio/Music.fs`, `audio/Sound.fs`, `world/assets/EnvironmentAsset.fs`, `world/render/EnvironmentRender.fs`, `editor/Raycast.fs`, and `state/SharedState.fs` (after T075) (F-41, F-46).
- [X] T086 [US3] Remove the throwaway zone and duplicate ray in `state/ScenarioFork.fs`/`editor/Editor.fs`/`editor/Viewport.fs` (after T085) (F-44, F-45).
- [X] T087 [US3] Run a grep/compile audit for every task T065–T086 and record the one-implementation counts in `specs/098-codebase-review-fixes/research.md` (F-63 decision recorded).

**Checkpoint**: US3 is independently green and each pattern has one implementation.

---

## Phase 6: User Story 4 - The test suite is a reliable authoring tool (Priority: P2)

**Goal**: One fixture layer, no shared mutable state, no drifted description, deterministic async waits (F-64 to F-80).

**Independent Test**: Every test passes alone and in normal order; one fixture edit absorbs a shared-state field; the cue description exists once.

### Implementation for User Story 4

- [X] T088 [P] [US4] Replace the `sharedState` copies in `test/ScenarioTests.fs`, `ScenarioForkTests.fs`, `SelectionTests.fs`, `SkydomeTests.fs`, `SoundTests.fs`, `SpawningTests.fs`, `AdventTests.fs`, `CueTests.fs`, `CuePanelTests.fs`, `GameplayStateTests.fs`, `MusicCueTests.fs`, `MusicTests.fs` and `ActorNameTests.fs` with `TestSupport.headlessShared` (F-64).
- [X] T089 [US4] Replace the hand-written state records in `test/SelectionTests.fs`, `test/SkydomeTests.fs` and `test/SpawningTests.fs` with `GameplayState.create`/`stateWithZone` (after T088) (F-65).
- [X] T090 [US4] Replace the local entity/actor/prop constructors in `test/ScenarioTests.fs`, `ProjectTests.fs`, `ScenarioForkTests.fs`, `SelectionTests.fs`, `SystemsTests.fs`, `ZoneTests.fs`, `SpawningTests.fs` and `PlayerTests.fs` with the shared fixtures (after T088) (F-66).
- [X] T091 [US4] Replace the duplicated headless asset composites and triangle literals in `test/ZoneTests.fs`, `test/SpawningTests.fs`, `test/CueTests.fs` and `test/GameplayStateTests.fs` with `TestSupport` helpers (after T090) (F-67).
- [X] T092 [US4] Make `test/CueTests.fs` state per-test; remove the module-level mutable `shared` and any order dependence (F-68).
- [X] T093 [US4] Use one `describe`/`describeEndings`/`seqOf` in `test/CueTests.fs`, `test/CuePanelTests.fs` and `test/MusicCueTests.fs` (F-69).
- [X] T094 [US4] Replace the sleep-poll in `test/AssetLoadTests.fs` with a deterministic signal and a bounded wait of at most 3 seconds (F-70).
- [X] T095 [US4] Replace `weightOf` with `ActorRender.tryClipWeight` and `weightOr` with option assertions in `test/ActorTests.fs`/`test/CueTests.fs` (after T092) (F-71).
- [X] T096 [US4] Replace the 15+ allocation boilerplates with `TestSupport.assertAllocatesNothing` across `test/ActorTests.fs`, `CueTests.fs` and `DialogueTests.fs` (after T095) (F-72).
- [X] T097 [P] [US4] Use `TestSupport.withTempDir` and table-drive the near-identical cases in `test/AssetsTests.fs` and `test/AudioTests.fs` (F-73).
- [X] T098 [P] [US4] Correct the name-defaulting test in `test/AssetNameIndexTests.fs` and add the loader-level default case (F-74).
- [X] T099 [US4] Replace the reflection and reference-identity assertions in `test/CuePanelTests.fs` and `test/ZoneTests.fs` with behavioural assertions (F-75).
- [X] T100 [US4] Reduce the repeated narrative runs, use `Harness.report` in `test/NarrativeRuleSetTests.fs`, and share the test-only projections (F-76, F-79).
- [X] T101 [P] [US4] Remove the dead/single-use helpers and hoist one `assertNear` in `test/TestSupport.fs`, `test/SwayTests.fs`, `test/RaycastTests.fs` and `test/IqmTests.fs` (F-77).
- [X] T102 [US4] Split `test/SpawningTests.fs` into asset and bare rigs; strengthen the no-write assertion in `test/ProjectTests.fs` (after T090) (F-78, F-80).
- [X] T103 [US4] Run the 19 most-affected test classes individually and the full suite in normal order; record duration against the T001 baseline in `specs/098-codebase-review-fixes/research.md` (F-64 to F-80).

**Checkpoint**: US4 is independently green; no test depends on execution order.

---

## Phase 7: User Story 5 - The narrative engine cannot silently miss a conclusion (Priority: P3)

**Goal**: Complete `Mentions` and remove the confirmed redundant traversals (F-08, F-81 to F-90).

**Independent Test**: A nested second premise derived later produces its conclusion; a no-projection commit performs no derivation; reasons read without re-sorting.

### Tests for User Story 5

- [X] T104 [P] [US5] Add the nested-second-premise regression in `test/NarrativeDerivationTests.fs`.
- [X] T105 [US5] Add no-projection-commit, reason-order and no-rebuild cases in `test/NarrativeCommitTests.fs`/`test/NarrativeDerivationTests.fs` (after T104).

### Implementation for User Story 5

- [X] T106 [US5] Declare the complete `Mentions` list on every derivation and update the units in `narrative/Rules.fs`, `narrative/Units/Cast.fs`, `Friendship.fs` and `Romance.fs` (F-08).
- [X] T107 [US5] Skip `dropInferred`/`infer` for a no-projection event and apply reason-only edits in `narrative/Engine.fs` (F-81).
- [X] T108 [US5] Return novelty from `Facts.add` with `Map.change`; make `dropInferred` one traversal; read reasons without recanonicalising in `narrative/Facts.fs`/`narrative/Engine.fs` (after T107) (F-82, F-85, F-87).
- [X] T109 [P] [US5] Use `Map.change` and avoid the per-fact list in `narrative/ParticipantIndex.fs`; count conclusions during the fold in `narrative/Derive.fs` (F-84, F-86).
- [X] T110 [US5] Reduce per-conclusion/per-bind allocation in `narrative/Rules.fs` (after T106) (F-83).
- [X] T111 [P] [US5] Decorate-sort on precomputed text and reuse the computed history/facts in `narrative/Harness.fs` (F-89).
- [X] T112 [US5] Remove the dead narrative surface and unreachable canonical branch in `narrative/Facts.fs`, `History.fs`, `Canonical.fs` and `Rules.fs` (after T106/T108) (F-90).
- [X] T113 [US5] Apply the `factsOf` index-retention decision from T003 in `narrative/Engine.fs` (F-88). Excluded: the scan is kept and now uses `foldParticipants`.
- [X] T114 [US5] Run the narrative suites and `test/NarrativePerformanceTests.fs`; record the result in `specs/098-codebase-review-fixes/research.md` and confirm determinism and the 0-byte idle paths.

**Checkpoint**: US5 is independently green; the nested-premise regression passes.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, full verification and closing the finding register.

- [X] T115 [P] Correct the drifted comments and docs in `render/Camera.fs`, `docs/EDITOR_DESIGN.md`, `world/Cue.fs`, `editor/CueTree.fs`, `world/render/EnvironmentBatch.fs`, `world/render/DialogueRender.fs`, `world/render/PromptRender.fs` and `world/Zone.fs` (F-91).
- [X] T116 [P] Update any measurement or decision statements in `docs/RENDER_PERFORMANCE.md` and `docs/ARCHITECTURE.md` that the implementation changes.
- [X] T117 Run `dotnet build` and `dotnet test test/Sugoi.Tests.fsproj`; record 0 warnings, 0 failures and the duration against T001 in `specs/098-codebase-review-fixes/research.md`.
- [ ] T118 Run the full `quickstart.md` manual matrix (sections 1–8) and record the results. Not run in this environment (no display); the automated build, full suite and headless checks passed.
- [X] T119 Record every `Verify`/`Exclude` outcome in `specs/098-codebase-review-fixes/research.md` and confirm every `Fix` register item is closed.
- [X] T120 Review the complete diff for unintended behaviour changes, new dependencies or module moves against `specs/098-codebase-review-fixes/plan.md`; record `SC-014` compliance in `specs/098-codebase-review-fixes/research.md`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: starts immediately; T002–T004 can run in parallel.
- **Foundational (Phase 2)**: depends on Setup; T005–T008 can run in parallel; T009 gates all story tests.
- **US1 (Phase 3)**: depends on Foundational; it is the MVP and should finish before US2.
- **US2 (Phase 4)**: depends on Foundational; T038/T039 (the dirty batch) depend on T024 (US1's `reset`).
- **US3 (Phase 5)**: depends on Foundational; it can start after US1/US2 or in parallel by file cluster, but T065–T067 (undo/classifier) must precede T066.
- **US4 (Phase 6)**: depends on Foundational and on the production changes it tests; migrate in groups.
- **US5 (Phase 7)**: depends on Foundational; independent of US1–US4.
- **Polish (Phase 8)**: depends on all desired stories.

### User Story Dependencies

- **US1 (P1)**: no story dependency; MVP.
- **US2 (P1)**: depends on US1 T024 (batch reset) for T038/T039; otherwise independent.
- **US3 (P2)**: independent; its helper extraction can reuse US2's changes where they touch the same file.
- **US4 (P2)**: independent fixtures; its tests may exercise US1–US3 behaviour.
- **US5 (P3)**: independent.

### Same-file constraints

Do not run these in parallel with each other:

- `world/Zone.fs`: T024, T025, T039, T076, T077, T078, T079, T082.
- `world/assets/Assets.fs`: T022, T023, T027, T041, T043, T061, T080.
- `state/CueEngine.fs`: T052, T056, T076, T081, T082, T083.
- `editor/CueTree.fs`: T040, T042, T068, T072.
- `editor/CueEditor.fs`: T040, T068, T071, T073.
- `editor/Inspector.fs`: T040, T046, T068, T074.
- `render/ImGui.fs`: T017, T068.
- `render/DebugDraw.fs`: T019, T044, T069, T070.
- `render/Ui.fs`: T019, T049, T069, T070.
- `world/render/ActorRender.fs`: T019, T050, T057.
- `world/render/EnvironmentBatch.fs`: T024, T038, T058.
- `narrative/Rules.fs`: T106, T110, T112.
- `narrative/Engine.fs`: T107, T108, T113.

### Parallel Opportunities

- Setup spikes T002–T004.
- Foundational fixtures T005–T008.
- US1 tests T010–T016; implementations on disjoint files (T017–T021, T026, T028, T029 can run together; T022/T023/T027 sequence on `Assets.fs`; T024/T025 sequence on `Zone.fs`).
- US2 tests T031–T037; implementations on disjoint files, respecting the same-file list above.
- US3 helper extractions on disjoint files (T068–T070, T072–T075, T077–T079, T081, T083–T086).
- US4 migrations by test-file group (T088–T091, T095–T098, T101–T102).
- US5 T108–T112 on disjoint narrative files.

### Within each story

- Write the regression test and watch it fail before the fix (or, for an exclusion spike, record the evidence).
- Core helper before its call sites.
- Run the story's test filter and the full suite before the checkpoint.

---

## Parallel Example: US1

```text
T010 InputTests digit range
T011 ProjectTests retry
T012 AssetsTests texture directory
T013 EnvironmentBatchTests reset
T014 ZoneTests actor body
T015 AssetsTests rollback
T016 panel list assertion
```

Then, on disjoint files:

```text
T017 render/ImGui.fs textUnformatted
T019 render/InstanceBuffer + DebugDraw + Ui + ActorRender cycle
T020 world/Project.fs retry
T026 core/Input.fs digit guard
T028 editor/Editor.fs panel list
```

## Parallel Example: US2

```text
T031 EnvironmentBatchTests clean batch
T032 AssetsTests name cache
T033 allocation tests
T034 MusicCueTests ramps
T035 ActorTests palette/frame window
T036 EnvironmentBatchTests growth
T037 CueTests fork
```

## Implementation Strategy

### MVP first

Complete Setup, Foundational and US1. Validate the safety/data-integrity matrix in `quickstart.md` sections 1–4. This is the smallest shippable slice: no crash on authored text, no lost edit, no stale environment, no per-frame body rebuild.

### Incremental delivery

1. US1 → validate → commit.
2. US2 (dirty batch first, then the allocation paths) → validate → commit.
3. US3 in two groups: dead surface/undo first, helper extraction second → validate → commit.
4. US4 in fixture groups → validate → commit.
5. US5 → validate → commit.
6. Polish and the full suite.

### Notes

- `[P]` means different files and no incomplete dependency; the same-file constraints above always win.
- Keep the build and suite green at every checkpoint; do not defer a failing test to a later phase.
- Record every exclusion with evidence in `research.md`; do not implement a change to satisfy a non-reproducing finding.
- No new dependency, module or `.fsproj` order change is expected; if one becomes necessary, stop and update `plan.md` first.
