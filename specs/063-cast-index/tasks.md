---

description: "Task list for Cast Index"
---

# Tasks: Cast Index

**Input**: Design documents from `/specs/063-cast-index/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/cast-identity.md, quickstart.md

**Tests**: Included — the project's convention (and SC-004) require the suite green, and the index's exactness
across undo/redo/zone delete is exactly what the suite can assert headlessly with no device.

**Organization**: Tasks are grouped by user story (US1 P1, US2 P2, US3 P3).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 (setup, foundational, polish carry none)

## Path Conventions

- Single F# project at the repository root; sources in `world/`, `state/`, `editor/`, tests in `test/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The shared data every story reads and writes.

- [X] T001 `world/Entity.fs`: add `Cast : Roster.Character option` to `ActorState` (the authored role), and set
  it at the `ActorState` construction sites it breaks (`Zone.spawnActor` → `None`, and the test fixtures that
  build one: `test/GameplayStateTests.fs`, `test/SelectionTests.fs`, `test/CuePanelTests.fs`, others the build
  reports).
- [X] T002 `state/GameplayState.fs`: add `Cast : Dictionary<Roster.Character, Place * EntityRef>` to
  `GameplayState` (initialised empty in `create`) and the helpers beside it — `tryCastActor`,
  `addCast`, `removeCast`, `removeCastZone` (research D1, D3).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The spawn path must carry the cast before any story can resolve one.

**⚠️ CRITICAL**: No user story can be implemented until this phase is complete.

- [X] T003 `editor/Spawning.fs` + `editor/CastPanel.fs` + `editor/AssetBrowser.fs`:
  `Spawning.spawnActor` takes the actor's `Cast : Roster.Character option` and records it via `addCast` after
  `Zone.spawnActor`; `CastPanel` passes the character it bound, `AssetBrowser` passes `None`.
- [X] T004 Build checkpoint: `dotnet build fsharp-game.fsproj` and `dotnet build test/fsharp-game.Tests.fsproj`
  compile with the new field and index.

**Checkpoint**: a spawned cast member can carry and record its character; the stories can proceed.

---

## Phase 3: User Story 1 - An actor knows which character it plays (Priority: P1) 🎯 MVP

**Goal**: a spawned cast member records its character and the state resolves the character to that actor in one
lookup; a non-cast actor resolves for none.

**Independent Test**: spawn a cast member and resolve its character to it; spawn a non-cast actor and assert it
resolves for no character; assert resolution does not depend on the active zone.

- [X] T005 [P] [US1] `test/CastIndexTests.fs`: a spawned cast member
  resolves by its character to the same ref and zone; a non-cast actor resolves for none; resolving a cast
  member does not depend on which zone is active.

**Checkpoint**: US1 works and is testable independently.

---

## Phase 4: User Story 2 - One character exists at most once across all zones (Priority: P2)

**Goal**: a spawn of a character already in the scene performs nothing, logging when the existing member is in
another zone.

**Independent Test**: spawn a character, attempt it again in the same and in another zone, and assert exactly
one actor plays it.

- [X] T006 [US2] `editor/Spawning.fs` (moved here from `CastPanel`, so the panel and the next feature's cue
  op inherit one rule): check `tryCastActor` before spawning — perform nothing when the character is present,
  and record an `eprintfn` naming the character and the zone it is already in when that zone is not the one
  being spawned into (FR-007, research D4).
- [X] T007 [P] [US2] `test/CastIndexTests.fs`: a duplicate spawn leaves the
  count at one and the original entry intact; the cross-zone case logs and still no-ops.

**Checkpoint**: US1 and US2 both work and are independently testable.

---

## Phase 5: User Story 3 - The binding survives undo/redo and zone delete/restore (Priority: P3)

**Goal**: the index stays exact across every actor-existence change.

**Independent Test**: create, delete, undo, redo, delete a zone holding a member, and undo — after each, the
index matches the actors that exist.

- [X] T008 [US3] `state/History.fs`: add `ActorCreate`/`ActorDelete` arms to `History.apply`/`History.undo`
  that delegate to `Command.apply`/`undo` and update the index from `ActorState.Cast` and the active place;
  re-add the restored actors' casts in the `ZoneDelete` undo arm after `occupyZone`.
- [X] T009 [US3] `editor/Selection.fs`: route `deleteRef`'s `ActorDelete` through the state-level runner
  (`History.commit`) so its forward application updates the index in the same place as undo/redo; keep the
  selection bookkeeping it does today.
- [X] T010 [US3] `state/GameplayState.fs`: in `deleteZone`, call `removeCastZone` for the removed place.
- [X] T011 [P] [US3] `test/CastIndexTests.fs`: after create → undo → redo,
  delete → undo → redo, and zone delete → undo → redo, the index is exactly consistent (each present member has
  one entry, no absent actor has one).

**Checkpoint**: all three stories function.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T012 [P] Docs: note the runtime cast in `docs/ARCHITECTURE.md` (the entity/state sections) and
  `docs/ROADMAP.md` (the cast phase); update `world/Casting.fs`'s note if it should mention the runtime half.
- [X] T013 `dotnet build` warning-free and `dotnet test` green (SC-004), and run the automated section of
  `quickstart.md`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks** every user story.
- **US1–US3 (Phases 3–5)**: all depend on Foundational; run in priority order (or in parallel if staffed).
- **Polish (Phase 6)**: depends on the stories.

### Within and between stories

- T001 and T002 are one data change (the field and the index) and compile together.
- T003 is what makes a member resolvable; the US1 test follows it.
- T008/T009/T010 are the three maintenance sites; T011 exercises them.
- Test tasks follow the code they cover, and several touch the same files.

### Parallel Opportunities

- T005, T007 and T011 are different tests (but several share a file, so not all parallel).
- T012 is independent of the code.

---

## Implementation Strategy

### MVP First (User Story 1)

1. Setup + Foundational (a member can carry and record its character).
2. US1 (resolution).
3. **STOP and VALIDATE**: a spawned member resolves by character; a non-cast actor does not.
4. US2 (uniqueness), then US3 (undo/redo/zone exactness), then polish.

### Incremental notes

- The whole feature is small; the bulk of the work is the tests that pin the index's exactness at every
  actor-existence change, which is the point of the feature.

## Notes

- `[P]` = different files, no dependencies.
- The index is **maintained, not searched** — resolution is a lookup; writes happen only when an actor enters
  or leaves existence (research D3).
- One character at most, across all zones; a duplicate spawn no-ops and logs the cross-zone case.
- No cue ops here — addressing actors from a cue is the next feature, built on this index.
- No `.fsproj` change: no module is added or moved.
