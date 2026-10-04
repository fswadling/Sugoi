---

description: "Task list for Zones as Places"
---

# Tasks: Zones as Places

**Input**: Design documents from `/specs/061-zones-as-places/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/place-identity.md

**Tests**: Included — the project's convention (and SC-005) require the suite green, and the identity change
retypes zone names throughout the tests, so test edits are part of the work.

**Organization**: Tasks are grouped by user story (US1 P1, US2 P2, US3 P3).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 (setup, foundational, polish carry none)

## Path Conventions

- Single F# project at the repository root; sources in `world/`, `state/`, `editor/`, tests in `test/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The vocabulary module and its place in the compile order.

- [x] T001 Create `world/Place.fs`: `Period` (`Morning`/`Evening`), `Place` (`ArtRoom of Period`/`Island`), and the vocabulary helpers `placeName : Place -> string` (matched over the cases, as `Advent.name`), `all : Place list` (declaration order), and `start : Place` (the designated start).
- [x] T002 Register `world/Place.fs` in `fsharp-game.fsproj` immediately after `world/Advent.fs` and before `world/Cue.fs`; retarget `world/ZoneRegistry.fs` to `open Place` and delete its now-duplicate `Place`/`Period` definitions (keep `variantCases`, `byZone`, `ZoneGroup`/`PlaceEntry`, and `Registry`).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Retype the runtime identity, the steps, and the cue; the project compiles only once these land together.

**⚠️ CRITICAL**: No user story can be implemented until this phase is complete.

- [x] T003 `state/GameplayState.fs`: `Zones : Dictionary<Place, Zone>`, `ActiveZone : Place`; `activeZone`; `switchZone`/`selectZone` take `Place`; `create` makes `Place.start` present and active; `addZone : Place -> unit` makes an absent place present and active; `deleteZone : Place -> unit` makes an **inactive** present place absent and **refuses the active place** (FR-011).
- [x] T004 `state/Command.fs`: `ZoneMove of Previous: Place * Next: Place`, `ZoneAdd of Previous: Place * Name: Place`, `ZoneDelete of Previous: Place * Name: Place * Contents: ZoneContents`.
- [x] T005 `state/History.fs`: carry out the zone steps by `Place`; `ZoneDelete`'s undo re-adds an **inactive** place (create it, switch to it to rebuild bodies, `occupyZone`, switch back to the still-active place).
- [x] T006 `world/Zone.fs`: remove the `Name : string` field and its initialisation in `create`.
- [x] T007 `world/CueEngine.fs`: replace the `zone.Name` diagnostics with `Place.placeName` of the active place (thread the active `Place`/its name through `namedWaypoint`/`resolvedPath`/`snapPose`).
- [x] T008 `world/Cue.fs`: `SetZone of place: Place`; `open Place`.
- [x] T009 `state/EditorState.fs`: remove `NewZoneName` (and `zoneNameBufferSize`).
- [x] T010 `fsharp-game.fsproj`/build checkpoint: `dotnet build fsharp-game.fsproj` compiles once T003–T009 are in place.

**Checkpoint**: the runtime, steps and cue all speak `Place`; user stories can proceed.

---

## Phase 3: User Story 1 - A zone is one of the game's declared places (Priority: P1) 🎯 MVP

**Goal**: The editor manages a place's presence: add an absent declared place, remove an inactive present one
(unloading its environment), select the active one — with no free-text zone names.

**Independent Test**: create a game state and assert its zones are keyed by places; add a declared place and
confirm it is present and active; confirm it cannot be added twice and a lone present place cannot be removed;
confirm removal unloads the environment.

- [x] T011 [US1] `editor/Outliner.fs`: the Zones section lists **present** places in `Place.all` order with the active marker and environment-residency markers; replace the free-text add with a chooser over the **absent** declared places; disable Del for the active place.
- [x] T012 [US1] `editor/ZoneRegistryPanel.fs`: offer **Register** for the active place (captures its zone as that place's template); **Instantiate** makes the named place present and active (adding it when absent) and loads its template, as one undoable step (`Batch`).
- [x] T013 [US1] Environment unload on removal (FR-010): in `state/GameplayState.fs`/`world/Zone.fs`, release the removed zone and unload its environment from the GPU via the asset manager, except an environment a still-present place uses.
- [x] T014 [US1] Tests in `test/GameplayStateTests.fs` (and `test/ZoneRegistryTests.fs` if needed): retype zone names to places; assert place-keyed identity, add-once refusal, active-removal refusal, and environment unload on removal.

**Checkpoint**: US1 works and is testable independently.

---

## Phase 4: User Story 2 - Zone steps and the cue name a place (Priority: P2)

**Goal**: Switching/adding/deleting zones and the cue's zone op name a place, and undo/redo round-trips.

**Independent Test**: move between two present places and undo/redo; run a cue carrying a zone op naming a
place and assert the active place moved.

- [x] T015 [US2] `editor/CueTree.fs`: draw `SetZone` with a place chooser (the closed set, as the advent chooser does) and label it via `placeName`; stop routing it through `textOf`/`withText`.
- [x] T016 [US2] `editor/CueEditor.fs`: route `SetZone` through the place chooser (remove it from the text-field path).
- [x] T017 [US2] Tests: retype `test/CueTests.fs`, `test/CuePanelTests.fs`, `test/AdventTests.fs`, `test/SelectionTests.fs`, `test/SpawningTests.fs` for places; assert the cue's zone op switches to a place, and undoing a zone step returns the previous place.

**Checkpoint**: US1 and US2 both work and are independently testable.

---

## Phase 5: User Story 3 - Nothing else about a zone changes (Priority: P3)

**Goal**: Bound the change to identity; every other zone behaviour is unchanged.

**Independent Test**: the retyped suite passes; physics attach/detach, zone capture/restore, residency markers
and shutdown behave as before.

- [x] T018 [US3] Regression in `test/GameplayStateTests.fs`: assert a zone delete→undo restores the zone's contents, that switching attaches/detaches the right zone's bodies, and that `Engine`'s shutdown iterates the place-keyed map unchanged.

**Checkpoint**: all three stories function.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [x] T019 `dotnet build` warning-free and `dotnet test` green (SC-005).
- [x] T020 Update `docs/ROADMAP.md` (record zones-as-places under the zone-registry phase) and `docs/ARCHITECTURE.md` if it describes zone identity.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks** every user story.
- **US1 (Phase 3)**, **US2 (Phase 4)**, **US3 (Phase 5)**: all depend on Foundational; run sequentially in priority order (or in parallel if staffed).
- **Polish (Phase 6)**: depends on the stories.

### Within and between stories

- T003/T004/T005 are one coupled change (the state and the steps that act on it).
- T015/T016 are one coupled change (the cue control and the panel that draws it).
- Test retypes (T014, T017) follow the code they cover.

### Parallel Opportunities

- T006, T007, T009 touch distinct files and can run once T001–T005 compile.
- T011 and T015/T016 touch distinct editor files.

---

## Implementation Strategy

### MVP First (User Story 1)

1. Setup + Foundational (the identity refactor compiles).
2. US1 (presence management in the Outliner + registry panel).
3. **STOP and VALIDATE**: places appear only as declared, add/remove/select works, removal unloads.
4. US2 (steps + cue), then US3 (regression), then polish.

### Incremental notes

- The Foundational phase is deliberately one landing: the type change ripples through state, commands, history
  and the cue, and does not half-compile.
- Test files are retyped as their subjects change, so the suite stays honest at each checkpoint.

## Notes

- `[P]` = different files, no dependencies.
- A zone is a compile-time constant; only its **presence** changes at run time.
- "Present/absent" is the zone-map level; "environment residency" is the GPU level — do not conflate them.
