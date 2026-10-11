---

description: "Task list for feature implementation"
---

# Tasks: Cue Actors Addressed by Name — a Scene-Driven Chooser

**Input**: Design documents from `/specs/073-cue-actor-by-name/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/cue-actor-by-name.md

**Tests**: Included — the project mandates headless verification (constitution N-V) and the feature's success
criteria are test-based.

**Organization**: As with `specs/072`, this is a single cohesive refactor: the op argument changes type
everywhere at once, so the Foundational phase migrates the types and keeps the tree compiling; the story
phases then deliver the verifiable increments (the chooser, the asset-sourced names, `AddActor`).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)
- Paths are repository-relative.

## Path Conventions

Single project, F# — flat clusters at the repository root: `world/`, `state/`, `editor/`, `test/`. Compile
order is fixed by `fsharp-game.fsproj`; this feature changes no entry in it.

---

## Phase 1: Setup (Shared Infrastructure)

- [x] T001 Confirm no `fsharp-game.fsproj` change is required — every touched file keeps its compile position; no new module

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Change the actor argument from `Roster.Character` to `string`, re-base the engine, and keep the
tree compiling. **⚠️ No user-story work can begin until this is complete.**

- [x] T002 [P] `world/Cue.fs`: every actor op takes `actor: string`; `AddActor` becomes `place * actor * asset * waypoint` (drops `Casting.Look`); the `…State` cases carry strings; update the module comment
- [x] T003 [P] `state/GameplayState.fs`: `tryActorByName` takes `name: string` (drop the `Roster.characterName` conversion)
- [x] T004 `world/CueEngine.fs`: resolve actors from the op's string (`tryResolveActor`/`removeActor`/blends/motion/player); `addActor` builds from the named **asset** and no-ops when it is not discovered; `noOp` takes a string; the `BlendState`/`MoveActorState`/`RotateActorState` cases carry strings (depends T002, T003)
- [x] T005 `editor/CueTree.fs`: migrate the **types** — op patterns/labels/`kindName`/`defaultOfKind` to strings; rename `withCharacter` → `withActor`; **delete `withLook`**; make `textOf`/`withText`/`textOf2`/`withText2`/`withPlace`/`withWaypoint` compile against the new shapes. The `AddActor` label reads `Add actor: <name>/<asset> in <place> @ <waypoint>`. **Remove only the actor ops' `characterCombo` and the `AddActor` look combo from `drawEditor`** (keep the place combo for `AddActor`) — **leave the `Exposit` speaker combo and its `Roster.characters` use untouched** (portraits, FR-007)
- [x] T006 `editor/CueEditor.fs`: update `drawNodeText`/`drawTrigger`/`draw` for the string ops and switch `assetNamesOf` from the cast-binding lookup to a **placed-actor** lookup (resolve the name in the viewed zone → `ActorRender.Asset` → its clips/morphs) so the module compiles; T013 then factors out the pure helper
- [x] T007 `dotnet build` is clean with no warnings (depends T002–T006)

**Checkpoint**: actor ops are string-addressed, the engine and editor compile, the tree builds.

---

## Phase 3: User Story 1 - A cue names an actor by name, not by roster position (Priority: P1) 🎯 MVP

**Goal**: A cue op names its actor by a plain string, resolved by the scene scan; `world/Cue.fs` names no
roster.

**Independent Test**: author an actor op with a string equal to a placed actor's `Name` and assert the op acts
on her; assert `world/Cue.fs` names no `Roster`/`Casting` value.

### Tests for User Story 1

- [x] T008 [P] [US1] `test/ActorNameTests.fs`: `tryActorByName` takes `"Aiko"`; keep the miss/duplicate cases
- [x] T009 [US1] `test/CueTests.fs`: retarget `describe`, `resolve` and the actor-op tests to string names; `AddActor` now takes `(name, asset, …)`

### Implementation for User Story 1

- [x] T010 [US1] Sweep `world/Cue.fs` (and `world/CueEngine.fs`) for any `Roster.`/`Casting.` reference and confirm none remains (SC-001); then `dotnet test` — the actor-op suite is green (depends T009)

**Checkpoint**: User Story 1 is functional and independently testable.

---

## Phase 4: User Story 2 - The editor offers only the place being viewed's actors (Priority: P1)

**Goal**: The actor chooser lists the viewed place's live actors (a combo, mirroring the waypoint chooser) —
nothing from the roster.

**Independent Test**: place one actor, open an actor op's chooser, assert it offers exactly that actor.

### Implementation for User Story 2

- [x] T011 [US2] `editor/CueEditor.fs`: add `drawActorCombo` (offer the viewed place's live `Base.Name`s through `CueTree.filteredCombo`, committing `CueTree.withActor`) and draw it in `drawNodeText` for the existing-actor ops (the active place; a typed field only when no zone is available)
- [x] T012 [P] [US2] `test/CuePanelTests.fs`: update the `describe` pattern and the write-helper assertions for `withActor`/`withAsset`; **rewrite (not just retype) the `AddActor` text-field assertions** — `textOf`/`withText` now name the actor and the waypoint moves to `withText2`; keep the `Add actor`/`Remove actor` label assertions (`Add actor: <name>/<asset> in <place> @ <waypoint>`)

**Checkpoint**: the chooser is the scene; `dotnet test` green.

---

## Phase 5: User Story 3 - Clip and morph names come from the placed actor's asset (Priority: P2)

**Goal**: The clip/morph choosers read the selected actor's asset, not the binding.

**Independent Test**: place an actor whose asset defines clips/morphs, bind no cast member, and assert the
choosers offer that asset's names.

### Implementation for User Story 3

- [x] T013 [US3] `editor/CueEditor.fs`: factor the asset→names lookup into a pure helper (`assetNamesOfAsset : Assets.AssetManager -> string -> string list * string list`) and have `assetNamesOf` resolve the actor in the viewed zone and call it; an absent actor or unknown asset yields `[]`
- [x] T014 [P] [US3] `test/CuePanelTests.fs` (or a small pure test file): assert the asset helper returns an asset's clip/morph names and `[]` for an unknown asset id — the testable core, with no device

**Checkpoint**: blend/motion choosers populate from the scene.

---

## Phase 6 - User Story 4 - `AddActor` names an asset, and portraits are untouched (Priority: P2)

**Goal**: `AddActor`'s editor offers discovered assets and a typed name; the roster/binding/portraits are
provably unchanged.

**Independent Test**: add an actor naming a discovered asset + name and assert the actor appears with them;
run the dialogue and casting suites **unmodified**.

### Implementation for User Story 4

- [x] T015 [US4] `editor/CueEditor.fs`: add `drawAssetCombo` over the discovered actor assets (committing `CueTree.withAsset`); draw it for `AddActor` alongside a typed **name** field and the named place's waypoint combo
- [x] T016 [US4] `editor/CueTree.fs`: add `withAsset`; make `textOf`/`withText` for `AddActor` the **name** and route its waypoint's typed fallback through `withText2`; set `defaultOfKind "Add actor"` to `AddActor(Place.start, "", "", "")`
- [x] T017 [US4] Confirm `world/Casting.fs`, `world/Dialogue.fs`, `world/DialogueRender.fs`, `editor/CastPanel.fs` have no diff, and that `test/DialogueTests.fs` and `test/CastingTests.fs` pass **unmodified**
- [x] T018 [US4] `dotnet test` — the full suite is green with no device opened

**Checkpoint**: all four stories functional; portraits unaffected.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [x] T019 [P] Update the cue-actor-op passage in `docs/ARCHITECTURE.md`
- [x] T020 [P] Update the phase-10 cast passage in `docs/ROADMAP.md`
- [x] T021 Run the `quickstart.md` validation: `dotnet build` warning-free and `dotnet test` green with no device opened

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup — **BLOCKS all user stories** (the tree cannot compile until the types migrate).
- **User Stories (Phases 3–6)**: all depend on Foundational. US1 is the string-op behaviour; US2 adds the
  chooser; US3 reworks the name source; US4 adds the `AddActor` surface. They touch overlapping files
  (`CueEditor.fs`, `CueTree.fs`), so run them in order unless staffed with a clear file split.
- **Polish (Phase 7)**: after the desired stories.

### Within Each Story

- `world/*` before `state/*` before `editor/*` (compile order).
- Tests accompanying the change they cover.

### Parallel Opportunities

- T002 and T003 (different files) can run in parallel.
- T008 and T012/T014 (different test files) can run in parallel.
- T019 and T020 (different docs) can run in parallel.

---

## Parallel Example: Foundational

```bash
# Migrate the two independent leaves of the change:
Task: "world/Cue.fs — actor ops take actor: string; AddActor takes (place, actor, asset, waypoint)"
Task: "state/GameplayState.fs — tryActorByName takes name: string"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → Phase 2 Foundational (critical — blocks everything).
2. Phase 3 US1 → **STOP and VALIDATE** (`dotnet test`).
3. US1 alone delivers the vocabulary change: actor ops addressed by name.

### Incremental Delivery

1. Setup + Foundational → the ops are strings and the tree builds.
2. US1 → string ops verified → MVP.
3. US2 → the chooser reads the scene.
4. US3 → clip/morph names from the placed actor.
5. US4 → `AddActor` names an asset; portraits proven untouched.
6. Polish → docs and quickstart.

---

## Notes

- [P] = different files, no dependencies.
- Phase 2 is atomic in practice: the tree cannot compile between the type change and the editor migration.
- No `.fsproj` change; no new module; the roster, looks, binding, cast panel and portraits are untouched.
- Commit after each task or logical group.
