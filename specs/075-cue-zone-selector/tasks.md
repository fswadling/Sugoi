---

description: "Task list for feature implementation"
---

# Tasks: An Explicit Zone on Every Entity Cue Op

**Input**: Design documents from `/specs/075-cue-zone-selector/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/cue-entity-zone.md

**Tests**: Included — the project mandates headless verification (constitution N-V) and the feature's success
criteria are test-based.

**Organization**: This is a single cohesive change — nine op argument lists change at once across the cue
trio and the editor — so the Foundational phase migrates the types and keeps the tree compiling, and the
story phases then deliver the three verifiable increments (resolution in the named place; the editor's zone
selector and choosers; defaults and in-flight stability).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US3)
- Paths are repository-relative.

## Path Conventions

Single project, F# — flat clusters at the repository root: `world/`, `state/`, `editor/`, `test/`. Compile
order is fixed by `fsharp-game.fsproj`; this feature changes no entry in it.

---

## Phase 1: Setup (Shared Infrastructure)

- [x] T001 Confirm no `fsharp-game.fsproj` change is required — every touched file keeps its compile position; no new module

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the `place` argument to the nine ops, re-base resolution, and keep the tree compiling.
**⚠️ No user-story work can begin until this is complete.**

- [x] T002 [P] `world/Cue.fs`: `RemoveActor`, `Animate`, `CrossFade`, `Morph`, `CrossMorph`, `EnablePlayer`, `DisablePlayer`, `SetCamera`, `FlyCamera` take `place: Place` as their **first** argument; `MoveActor`/`RotateActor` and every `…State` case are unchanged; update the module comment
- [x] T003 [P] `state/GameplayState.fs`: remove the global `tryActorByName` (no remaining caller after T004)
- [x] T004 `world/CueEngine.fs`: replace `tryResolveActor` with `tryActorIn (state) (place) (actor)` resolving `state.Zones.[place]` then `Zone.tryActorByName`; `removeActor`/`beginBlend`/`EnablePlayer`/`DisablePlayer` take the op's place; `beginMove`/`beginRotate` resolve in `state.ActiveZone` and **drop their begin-time `place <> state.ActiveZone` guard** (the `stepMove`/`stepRotate` guards stay — they end the op when the zone stops being active); `SetCamera`/`FlyCamera` read the named place's zone and `invalidOp` with a clear message when the place is not present; the `noOp` reason strings name the place (depends T002, T003)
- [x] T005 `editor/CueTree.fs`: extend `withPlace` to the nine ops; `withActor`/`withText`/`withText2`/`withWaypoint` preserve the new `place`; update `label`/`kindName` (labels show the place, research D10); extend the **place combo** in `drawEditor` to the nine ops; `defaultOfKind` takes a `defaultPlace: Place` and `drawEditor` takes it too (depends T002)
- [x] T006 `editor/CueEditor.fs`: update `drawNodeText`/`drawTrigger`/`draw` for the new op arities so the module compiles, and pass `state.ActiveZone` as `drawEditor`'s default place for **both** call sites (the Cue panel and the inline trigger editor); the chooser behaviour itself is T013 (depends T002, T005)
- [x] T007 `dotnet build` is clean with no warnings (depends T002–T006)

**Checkpoint**: the nine ops carry a place, the engine resolves per place, the editor compiles, the tree builds.

---

## Phase 3: User Story 1 - An entity op names the zone it acts on (Priority: P1) 🎯 MVP

**Goal**: An actor/camera op resolves its entity in its **named place's zone**; a miss (absent place, or a
name not there) is a no-op for actor ops and raises for camera ops.

**Independent Test**: place two actors of one name in two present zones and assert an op naming the second
acts on the second and not the first; assert an op naming an absent place performs nothing.

### Tests for User Story 1

- [x] T008 [P] [US1] `test/CueTests.fs`: retarget `describe` and the actor-op tests to the place-first shapes; add "an op acts in its named zone" (two present zones, same actor name, the op's zone decides), "an absent place no-ops", and "a name not in the place no-ops"; the camera-op tests name a place and still assert the raise on a miss
- [x] T009 [P] [US1] Player cue toggles name a place — covered in `test/CueTests.fs` (the player-toggle test); `test/PlayerTests.fs` tests `Systems`, not cue ops, so it needs no change
- [x] T010 [US1] Sweep `world/Cue.fs`/`world/CueEngine.fs` and confirm no `GameplayState.tryActorByName` remains (per the Assumption "resolution by named place replaces the global scan"); then `dotnet test` — the resolution suite is green (depends T008, T009)

**Checkpoint**: User Story 1 is functional and independently testable.

---

## Phase 4: User Story 2 - Each op's editor shows a zone selector its choosers read (Priority: P1)

**Goal**: Each of the nine ops shows a place combo (writing one undo step) and its actor/waypoint/clip-morph
choosers read that place's zone, falling back to a typed field when the place is absent.

**Independent Test**: with two present zones holding different actors, set an op's zone and assert its
chooser offers that zone's actors and no others; change it and assert one undo step.

### Tests for User Story 2

- [x] T011 [US2] `test/CuePanelTests.fs`: update `describe`/labels/kinds for the nine new shapes; assert `withPlace` on each of the nine ops and that `withActor`/`withText`/`withText2`/`withWaypoint` preserve the place (the `defaultOfKind` assertion is T016)
- [x] T012 [P] [US2] `test/CuePanelTests.fs` (or a small pure test file): assert the labels include the place (`Remove actor: Aiko in Island`) and the kind dropdown names are unchanged

### Implementation for User Story 2

- [x] T013 [US2] `editor/CueEditor.fs`: finish the per-node zone read so the actor chooser, the waypoint chooser and the clip/morph chooser populate from the op's place's zone; the chooser is not drawn when the place is absent, and the clip/morph fields fall back to typing (the chooser is the scene the op targets) (depends T006)
- [x] T014 [US2] `dotnet test` — the panel suite is green with no device opened (depends T011–T013)

**Checkpoint**: the selector is the op's zone; the choosers follow it.

---

## Phase 5: User Story 3 - Defaults, in-flight ops, and the two editors (Priority: P2)

**Goal**: A newly-created op defaults its place to the active zone; a begun op keeps the place it began in;
the Cue panel and the inline trigger editor remain independent.

**Independent Test**: add an op from the kind dropdown while in a zone and assert its place is that zone;
begin a blend, switch zones, and assert the in-flight blend keeps its place.

### Tests for User Story 3

- [x] T015 [P] [US3] `test/CueTests.fs`: an in-flight blend keeps the place it began in across a zone switch (the `BlendState` place is unchanged and the blend still ends on schedule)
- [x] T016 [P] [US3] `test/CuePanelTests.fs`: `defaultOfKind place kind` uses `place` for every place-carrying kind

### Implementation for User Story 3

- [x] T017 [US3] Verify both `drawEditor` call sites default to the active zone and add **no** shared editor state, so a new op's place is the active zone and the two editors stay independent (structural review; T006 wires the default) (depends T005, T006)
- [x] T018 [US3] `dotnet test` — the full suite is green with no device opened (depends T015–T017)

**Checkpoint**: all three stories functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [x] T019 [P] Update the cue-actor-op / cue-module passage in `docs/ARCHITECTURE.md` (the zone is an op argument now)
- [x] T020 [P] Update the phase-9/10 cue passage in `docs/ROADMAP.md`
- [x] T021 Run the `quickstart.md` validation: `dotnet build` warning-free and `dotnet test` green with no device opened, including the manual SC-003 check (changing an op's zone is one undo step that undo reverses)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup — **BLOCKS all user stories** (the tree cannot compile until the types migrate).
- **User Stories (Phases 3–5)**: all depend on Foundational. US1 is the resolution behaviour; US2 the editor
  selector and choosers; US3 the defaults and in-flight stability. They touch overlapping files
  (`CueTree.fs`, `CueEditor.fs`, the two test files), so run them in order unless staffed with a clear file split.
- **Polish (Phase 6)**: after the desired stories.

### Within Each Story

- `world/*` before `state/*` before `editor/*` (compile order).
- Tests accompanying the change they cover.

### Parallel Opportunities

- T002 and T003 (different files) can run in parallel.
- T008 and T009 (different test files) can run in parallel.
- T011 and T012 (different assertions) and T015/T016 (different test files) can run in parallel.
- T019 and T020 (different docs) can run in parallel.

---

## Parallel Example: Foundational

```bash
# Migrate the two independent leaves of the change:
Task: "world/Cue.fs — the nine ops take place: Place first; the …State cases unchanged"
Task: "state/GameplayState.fs — remove the global tryActorByName"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → Phase 2 Foundational (critical — blocks everything).
2. Phase 3 US1 → **STOP and VALIDATE** (`dotnet test`).
3. US1 alone delivers the vocabulary change: an op names the zone it acts on.

### Incremental Delivery

1. Setup + Foundational → the ops carry a place and the tree builds.
2. US1 → resolution in the named place verified → MVP.
3. US2 → the editor's zone selector and choosers follow it.
4. US3 → defaults and in-flight stability.
5. Polish → docs and quickstart.

---

## Notes

- [P] = different files, no dependencies.
- Phase 2 is atomic in practice: the tree cannot compile between the op-shape change and the editor migration.
- No `.fsproj` change; no new module; no `Command` case changes (cue ops are runtime-only).
- The selector offers the closed `Place.all` set, matching the existing place combos (research D6, spec FR-005).
- Commit after each task or logical group.
