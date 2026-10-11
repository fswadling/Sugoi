---

description: "Task list for feature implementation"
---

# Tasks: Actors Addressed by Name — Retiring the Cast Role and its Index

**Input**: Design documents from `/specs/072-actor-name-addressing/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/actor-name-addressing.md

**Tests**: Included. The feature's success criteria are test-based (SC-002/SC-003/SC-004) and the project
mandates headless verification (constitution N-V; `specs/063`'s fabricated-`ActorAsset` fixture).

**Organization**: Tasks are grouped by user story. Note that this is a single cohesive refactor: the
Foundational phase retires the role and the index and re-bases resolution, because the tree cannot compile
until all of it lands together; the story phases then deliver the verifiable increments on top.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- File paths are repository-relative.

## Path Conventions

Single project, F# — flat domain clusters at the repository root: `world/`, `state/`, `editor/`, `test/`,
`cast/`. Compile order is fixed by `fsharp-game.fsproj` (this feature changes no entry in it).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the change needs no project scaffolding.

- [x] T001 Confirm no `fsharp-game.fsproj` change is required — every touched file keeps its compile position (`cast/Roster.fs` first, `world/*`, then `state/*`, then `editor/*`); no new module is added

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Retire the actor cast role and the runtime cast index, add the name-scan resolution, and keep the
tree building. **⚠️ No user-story work can begin until this phase is complete.**

- [x] T002 [P] Remove `Cast : Roster.Character option` from `ActorState` in `world/Entity.fs`
- [x] T003 [P] Remove `GameplayState.Cast`, `tryCastActor`, `addCast`, `removeCast`, `removeCastZone`, and the `create` initialiser for the index in `state/GameplayState.fs`
- [x] T004 Add `GameplayState.tryActorByName (state) (character) : (Place * Zone.Zone * EntityRef) option` in `state/GameplayState.fs`, scanning `Place.all` order and skipping absent places (research D3)
- [x] T005 [P] Remove the cast-index reconciliation from `ActorCreate`/`ActorDelete` in `History.apply`/`undo` and the zone-delete-undo restore loop in `state/History.fs`
- [x] T006 [P] Drop the now-unused `open Casting` in `state/Command.fs`
- [x] T007 Add `Zone.tryActorByName (zone) (name) : EntityRef option` beside `tryWaypointByName`/`tryTriggerByName` and drop the `cast` parameter from `Zone.spawnActor` in `world/Zone.fs`
- [x] T008 Re-base every `CueEngine` user of the removed index on `GameplayState.tryActorByName` so the module compiles — `tryResolveActor`, the member lookup in `setActor`, and `removeActor` — dropping the `addCast`/`removeCast` writes in `world/CueEngine.fs`
- [x] T009 Update the editor signatures/comments: drop the `cast` argument and the duplicate guard from `Spawning.spawnActor` in `editor/Spawning.fs`, pass no cast from the Spawn control in `editor/CastPanel.fs`, and drop the cast-index citation from `deleteRef`'s comment in `editor/Selection.fs`
- [x] T010 `dotnet build` is clean with no warnings (depends on T002–T009)

**Checkpoint**: the role, the index and their maintenance sites are gone; resolution is a name scan; the tree builds.

---

## Phase 3: User Story 1 - An actor is found by its name, with no index (Priority: P1) 🎯 MVP

**Goal**: A cue op names a character; the actor of that name is found by scanning the present places, or the
op performs nothing. No lookup table tracks which actor is which.

**Independent Test**: place an actor named for a character, drive a cue that names her through `CueEngine.tick`,
assert she is found/removed/addressed; assert no cast index exists on the gameplay state.

### Tests for User Story 1

- [x] T011 [P] [US1] Add `Zone.tryActorByName` unit tests (finds a named actor, misses an unknown name) in `test/ZoneTests.fs`
- [x] T012 [P] [US1] Add `GameplayState.tryActorByName` tests (finds across places in `Place.all` order; first of a duplicate is returned) in `test/GameplayStateTests.fs`
- [x] T013 [P] [US1] Retarget `test/SpawningTests.fs` at the cast-less spawn path
- [x] T014 [US1] Add `CueEngine.tick` tests: `RemoveActor` finds the member by name across places and no-ops when absent; the acting ops (`MoveActor`/`RotateActor`/a blend/`EnablePlayer`) resolve by name — in `test/CueTests.fs`

### Implementation for User Story 1

- [x] T015 [US1] Sweep the tree for any remaining index reference (`tryCastActor`/`addCast`/`removeCast`/`removeCastZone`/`ActorState.Cast`) and remove it; confirm `removeActor` resolves by name with no place named (SC-001)
- [x] T016 [US1] `dotnet test` — the US1 tests and the retargeted suites pass with no device opened

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - The portrait and binding paths are untouched (Priority: P2)

**Goal**: Prove the change did not disturb the dialogue/portrait path, which resolves through the binding and
never through the actor or the index.

**Independent Test**: the dialogue, casting and roster suites pass **unmodified**.

- [x] T017 [P] [US2] Confirm `world/Casting.fs`, `world/Dialogue.fs` and `world/DialogueRender.fs` have no diff
- [x] T018 [US2] Run `test/CastingTests.fs`, `test/DialogueTests.fs` and `test/RosterTests.fs` unmodified and green (`dotnet test --filter`)

**Checkpoint**: portraits are provably unaffected (SC-003).

---

## Phase 5: User Story 3 - The creation op is `AddActor`, and creates an actor (Priority: P3)

**Goal**: Rename `SetActor` → `AddActor` and make it a pure spawn (never moves an existing actor); retire the
editor readouts whose subject was the role or the index; point the clip/morph choosers at the binding alone.

**Independent Test**: author an `AddActor` op, run it twice, assert two actors of the character's name exist;
confirm the op appears under no other name.

### Tests for User Story 3

- [x] T019 [P] [US3] Update `test/CuePanelTests.fs`: the renamed labels, kinds and write helpers, and drop `Cast = None` from the `placedActor` `ActorState` literal
- [x] T020 [US3] Update `test/CueTests.fs`: `AddActor` twice places two actors; no upsert/move; each op still no-ops on an absent place / unbound look / unknown waypoint
- [x] T021 [P] [US3] Update `test/ScenarioTests.fs` (a scenario captures role-free actors; its tag list is gone) and `test/PlayerTests.fs` (cue player toggles resolve by name)

### Implementation for User Story 3

- [x] T022 [US3] Rename the cue case `SetActor` → `AddActor` in `world/Cue.fs` (and its comments)
- [x] T023 [US3] In `world/CueEngine.fs`: rename `setActor` → `addActor`, reduce it to a pure spawn (drop the member-lookup branches and the cross-zone move), and reword the no-op label to `"AddActor"`
- [x] T024 [US3] In `editor/CueTree.fs`: rename `"Set actor"` → `"Add actor"` in `kinds`, `kindName`, `label`, `defaultOfKind`, `executing`, `children`, and the `withCharacter`/`withLook` matches
- [x] T025 [US3] In `editor/CueEditor.fs`: drop the `runtimeCast` parameter and placed-actor branch from `assetNames` (the choosers read the binding alone, research D9)
- [x] T026 [P] [US3] Remove `castTags` and its use on the zone rows in `editor/Outliner.fs`
- [x] T027 [P] [US3] Remove `actorTags` and its use in `editor/ScenarioPanel.fs`
- [x] T028 [P] [US3] Remove the actor "Character" readout from `editor/Inspector.fs`
- [x] T029 [US3] Retire `test/CastIndexTests.fs` — its subject (the index) no longer exists; its fabricated-`ActorAsset` fixture moves to the name-resolution suite
- [x] T030 [US3] `dotnet test` — the full suite is green with no device opened

**Checkpoint**: all three stories are functional; the op is `AddActor`; no readout reads a role.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Docs and final validation.

- [x] T031 [P] Update the runtime-cast paragraph in `docs/ARCHITECTURE.md`
- [x] T032 [P] Update the phase-10 cast paragraph in `docs/ROADMAP.md`
- [x] T033 [P] Update the cast references (D-011, D-022, workspace) in `docs/EDITOR_DESIGN.md`
- [x] T034 Run the `quickstart.md` validation: `dotnet build` warning-free and `dotnet test` green with no device opened

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup — **BLOCKS all user stories**.
- **User Stories (Phases 3–5)**: all depend on Foundational. They are ordered by priority; US2 is verification
  of work US1 already did, and US3 renames/simplifies the op US1 re-based.
  - **User Story 1 (P1)**: after Foundational. No dependency on US2/US3.
  - **User Story 2 (P2)**: after Foundational; verifies US1's untouched paths.
  - **User Story 3 (P3)**: after US1 (it renames and simplifies the same function `T008` touched).
- **Polish (Phase 6)**: after the desired stories.

### Within Each Story

- Tests first; they must fail before the implementation task that satisfies them.
- `world/*` before `state/*` before `editor/*` (compile order).

### Parallel Opportunities

- T002–T006 (different files) can run in parallel.
- T011–T013 (different test files) can run in parallel.
- T026–T028 (different editor files) can run in parallel.
- T031–T033 (different docs) can run in parallel.

---

## Parallel Example: Foundational

```bash
# Retire the role and the index across the data model (different files):
Task: "Remove ActorState.Cast in world/Entity.fs"
Task: "Remove GameplayState.Cast and accessors in state/GameplayState.fs"
Task: "Remove cast reconciliation in state/History.fs"
Task: "Drop open Casting in state/Command.fs"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → Phase 2 Foundational (critical — blocks everything).
2. Phase 3 US1 → **STOP and VALIDATE** (`dotnet test`).
3. US1 alone delivers the whole point: actors addressed by name with no index.

### Incremental Delivery

1. Setup + Foundational → the role and index are gone and the tree builds.
2. US1 → name resolution, verified → MVP.
3. US2 → regression proof that portraits are untouched.
4. US3 → the rename and the pure spawn, plus the retired editor readouts.
5. Polish → docs and quickstart.

---

## Notes

- [P] = different files, no dependencies.
- Because the tree cannot compile between the removals and the re-base, Phase 2 is atomic in practice even
  where its tasks are marked [P].
- No `.fsproj` change; no new module; the roster, the binding and the cast panel are untouched.
- Commit after each task or logical group.
