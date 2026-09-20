---

description: "Task list for the Entity Command Families feature"
---

# Tasks: Entity Command Families

**Input**: Design documents from `/specs/030-entity-command-families/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — the plan's `Testing` section and `quickstart.md` specify xUnit coverage for the model, the commands, and the editor paths, extending `test/EntityTests.fs`, `test/ZoneTests.fs`, `test/SpawningTests.fs`, and `test/SelectionTests.fs`. Everything but the editor's own behaviour is headless.

**Organization**: Tasks are grouped by user story. Phase 2 is larger here than in a typical feature and deliberately so: this is a refactor of the command model, and changing the command type without rewiring its callers does not compile. Phase 2 therefore lands the per-kind families (the *mechanism* US1 needs) and keeps the app green, Phase 3 **proves** US1's exactness guarantee per kind, Phase 4 moves the link onto the waypoint (US2), and Phase 5 makes FR-006 a case the compiler names (US3).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- F# single project at the repository root; modules live in domain clusters (`world/`, `state/`, `editor/`, `render/`, `screen/`).
- Compile order is defined by `fsharp-game.fsproj`; no new source file is added, so both the app's and the test project's compile lists are unchanged.
- Tests live in `test/` (one `<Module>Tests.fs` per module).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the feature needs no build change.

- [X] T001 Verify that the feature extends existing modules only — `world/Entity.fs`, `world/Zone.fs`, `state/Command.fs`, `editor/Spawning.fs`, `editor/Selection.fs` — so `fsharp-game.fsproj` and `test/fsharp-game.Tests.fsproj` need no new `<Compile>` entry and no new test module is needed.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The per-kind command families, the `Zone` surface they restore through, and the callers that must move with the type change. Every user story reads this.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 Reshape the `Command` type in `state/Command.fs`: remove `Spawn`, `Delete`, and `Duplicate`; add `ActorCreate`/`ActorDelete`, `PropCreate`/`PropDelete`, `PartCreate`/`PartDelete`, `WaypointCreate`/`WaypointDelete`, `TriggerCreate`/`TriggerDelete`, each carrying that kind's own record; change `LinkSpawn`'s payload from `Entity` to `Waypoint`; keep `Transform`, `Rename`, `Link`, `TriggerSet`, and `SetEnvironment` as they are (contracts/entity-commands.md).
- [X] T003 Rewrite `apply`/`undo` in `state/Command.fs` for the new cases: a create and a delete are mirror images through `occupyK`/`removeK`; `Link`/`TriggerSet` keep their bodies; `LinkSpawn` occupies the waypoint, links `From` to it, and its undo restores `Before` then removes the waypoint (depends on T004-T006).
- [X] T004 [P] Add the per-kind capture functions to `world/Zone.fs` — `captureActor`, `captureProp`, `capturePart`, `captureWaypoint`, `captureTrigger` — each a record-update over the live record that clears the derived resources (`Physics`, `Renderer`) and the runtime flag (`Triggers.Triggered`), raising for a stale or wrong-kind ref. This is the single place a blanked record is produced (research Decision 2).
- [X] T005 Add the per-kind restore functions to `world/Zone.fs` — `occupyActor`, `occupyProp`, `occupyPart`, `occupyWaypoint`, and `occupyTrigger` sharing its established name — rebuilding derived resources from the record's asset id and submesh range, checking the slot is free, and resolving a part's environment by epoch; delete `setActorWithAsset` (depends on T002). Each builds its record field by field over the payload rather than updating it, so a field added to a kind is a build failure until it is classified as authored, derived, or runtime (FR-006, SC-004).
- [X] T006 Add the per-kind teardown functions to `world/Zone.fs` — `removeActor`, `removeProp`, `removePart`, `removeWaypoint`, `removeTrigger` — freeing the slot and releasing what the kind owns, and delete `removeEntity` (depends on T002).
- [X] T007 Rewire `editor/Spawning.fs`: every `spawn*` and `duplicate` builds the matching `*Create` from a captured record, so the copy carries every authored property of the original and the trigger branch no longer hand-resets `Triggered` or calls `setTrigger` (depends on T002-T004).
- [X] T008 Rewire `editor/Selection.fs`: `deleteRef` builds the matching `*Delete` from a captured record, which removes its trigger special case (`tryTriggerAt` → `TriggerDelete`, else `Delete`); `renameRef`, `linkRef`, and `updateTriggerRef` are unchanged (depends on T002, T004).
- [X] T009 [P] Update `test/ZoneTests.fs` for the new surface: replace `removeEntity` calls with the matching `remove*`, and keep every assertion that still describes the behaviour.
- [X] T010 [P] Update `test/SpawningTests.fs` for the new surface: command-shape assertions become `*Create`/`*Delete`, and the occupied-slot test targets the per-kind `occupy*` rather than `setActorWithAsset`.
- [X] T011 Retire `bindingAt` and `bindingNone` in `world/Zone.fs` if T007 and T008 leave them with no caller, and move the `bindingAt` assertions in `test/ZoneTests.fs` onto the per-kind paths; if a caller remains, leave them and note why (depends on T007, T008).

**Checkpoint**: `dotnet build` is clean and the full suite passes with the per-kind families in place — behaviour is unchanged except that an actor's collider overlay now round-trips and a trigger's payload is recorded by the same mechanism as every other kind. The waypoint's link still lives in `Zone.Links`, so US2 has not landed yet.

---

## Phase 3: User Story 1 - Undo and redo restore an entity exactly (Priority: P1) 🎯 MVP

**Goal**: Prove that every entity kind's authored state survives deletion and undo, and duplication and redo, with nothing silently reset.

**Independent Test**: for each kind, set every property it exposes to a non-default value, delete the entity, undo, and confirm nothing changed; then repeat the cycle and confirm stability.

**Note**: Phase 2 lands the mechanism this story needs (the capture is what makes the overlay and every kind's own fields round-trip), so this phase is where the guarantee is *proven* and encoded as the regression guard the spec's SC-001…SC-004 measure.

### Tests for User Story 1 ⚠️

- [X] T012 [US1] Add the per-kind exactness test to `test/SelectionTests.fs`: for actor, prop, part, waypoint, and trigger, set every property that kind exposes to a non-default value, delete through `Selection.deleteRef`, undo, and assert field by field that every authored property is unchanged (FR-001, FR-002, SC-001).
- [X] T013 [US1] Add the cycle-stability test to `test/SelectionTests.fs`: delete → undo → redo → undo 50 times, asserting the entity is present or absent as expected and identical at every step where it is present (SC-002).
- [X] T014 [US1] Add the overlay test to `test/SelectionTests.fs`: an actor's collider overlay is part of the record a delete carries, and toggling the overlay pushes no undo step (FR-003, US1/AC1, US1/AC6). The undo half of FR-003 needs a GPU device to build and release a renderer, so it is covered by the quickstart's interactive walk instead.
- [X] T015 [P] [US1] Add the recorded-payload test to `test/SpawningTests.fs`: a deleted trigger's recorded payload carries its authored shape, prompt, and flags with `Triggered = false`, and duplicating a trigger carries the authored shape but not the runtime flag (FR-004, FR-008).
- [X] T016 [P] [US1] Add the no-live-resource test to `test/ZoneTests.fs`: the record a `*Delete` holds has `Physics = None` and a defaulted `Renderer`, so no command carries a released body or GPU object (FR-007).
- [X] T017 [US1] Add the resource-baseline test to `test/ZoneTests.fs`: 50 delete and undo cycles on an actor leave live body and renderer counts at their pre-cycle baseline (SC-003, depends on T016).

**Checkpoint**: User Story 1 is proven — every kind round-trips exactly, and the tests that fail today (the overlay) now pass. Run `test/SelectionTests.fs`, `test/ZoneTests.fs`, and `test/SpawningTests.fs`.

---

## Phase 4: User Story 2 - A waypoint's link travels with the waypoint (Priority: P2)

**Goal**: Move the link onto the waypoint and delete the zone's link table, so a link survives every waypoint operation and a copy carries it.

**Independent Test**: build a three-waypoint chain, delete the middle waypoint, undo, and confirm the chain reads in order; duplicate a linked waypoint and confirm the copy links to the same next.

- [X] T018 [US2] Add `Next : EntityRef option` to `Waypoint` in `world/Entity.fs`, documented as authored state that ends a chain when `None`.
- [X] T019 [US2] In `world/Zone.fs`, delete the `Links` field and its initialisation in `Zone.create`, rewrite `waypointNext` to read the live waypoint's `Next` (source alive and target resolving, else `None`, with no mutation), and rewrite `setWaypointNext` to write it (refusing a self-link and a ref of another kind, not requiring the target alive) (depends on T018).
- [X] T020 [P] [US2] Update the `Waypoint` construction in `test/EntityTests.fs` for the new field.
- [X] T021 [US2] Rewrite the two `test/SpawningTests.fs` cases this story inverts: "a duplicated waypoint starts its own chain" becomes "a duplicated waypoint carries the link to the same successor", and add the case where deleting that successor leaves the copy's link stale rather than pointing elsewhere (FR-005, FR-013, US2/AC2).
- [X] T022 [US2] Update `test/ZoneTests.fs`: drop the `second.Links.Count` assertion from "chains live with their zone" (asserting the property through the waypoint's own state instead), keep the chain-order, staleness, slot-reuse, and self-link cases, and add an assertion that deleting a linked waypoint leaves no link state behind (SC-007, SC-008) (depends on T019).
- [X] T023 [US2] Update `test/SelectionTests.fs`: the mid-chain delete and undo test now asserts the chain is restored from the delete's payload, and that the link edit is still exactly one `Link` step (FR-005, US2/AC1, US2/AC3).

**Checkpoint**: User Stories 1 and 2 both hold — every kind round-trips exactly, and a waypoint's link is the waypoint's own state, carried by a copy and gone with a deletion.

---

## Phase 5: User Story 3 - A new kind or property cannot silently lose state (Priority: P3)

**Goal**: Make FR-006 a case the compiler names rather than a convention.

**Independent Test**: remove a kind's create or delete path and confirm the compiler names it as an unhandled case rather than the build passing with a silent default; add an authored property and confirm the build fails.

- [X] T024 [US3] In `world/Zone.fs`, remove the `| _ ->` arms remaining on the create, delete, and restore paths and write the `EntityKind` matches out over all six kinds (`Actor`, `Environment`, `Prop`, `Part`, `Waypoint`, `Trigger`), so a kind with no path is a non-exhaustive match the compiler names (FR-006). F# warns rather than errors on it and no build-wide flag is added, so a missing kind is a reported case, not a failed build.
- [X] T025 [US3] Verify the guarantee by temporarily deleting one kind's create case in `state/Command.fs` and one arm in `world/Zone.fs`, confirming the compiler reports both, and restoring them.
- [X] T026 [US3] Add the carry test to `test/ZoneTests.fs`: capture each kind and assert field by field that every non-derived, non-runtime field of the live record appears in the captured one, so a new authored field that the capture forgot is visible as a failing assertion rather than silent loss (FR-006, supports SC-004).

**Checkpoint**: All three user stories hold: a kind added without its round-trip fails to build, and the capture carries every non-derived, non-runtime field of every kind. The property half of SC-004 — a field added to a kind breaking the restore's build — is verified in Phase 6.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Whole-feature validation.

- [X] T027 Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj` and confirm the whole suite passes with the command families in place.
- [ ] T028 Walk the interactive scenarios 12–16 in `specs/030-entity-command-families/quickstart.md` in the editor (with a display): exactness by hand, trigger editing, mid-chain delete and undo, copying a linked waypoint, and nothing else changed. **Not run if the agent environment has no display.**
- [X] T029 [P] Confirm `editor/Inspector.fs` and `editor/Viewport.fs` are untouched and behave as recorded: the overlay is still a direct slot write with no undo step, and the connector still reads `Zone.waypointNext` without knowing where the link is stored. Also confirm no `EntityKind` match was added to a per-frame path (`world/Systems.fs`, `screen/GameplayScreen.fs`, `render/`), which is what keeps FR-010 true.
- [X] T030 Verify the property half of SC-004: temporarily add a field to `Actor` in `world/Entity.fs` and confirm the build fails in `world/Zone.fs`'s `occupyActor` (T005's explicit construction) until the field is classified as authored, derived, or runtime, and that `captureActor` carries it with no further change; then remove the field.
- [X] T031 [P] Add the undo-granularity test to `test/SelectionTests.fs`: two consecutive `Transform` commands on the same ref collapse into one step, and transforms on different refs do not — the positive half of FR-009 and SC-005, which no existing test asserts (`SelectionTests` only asserts that `Rename` does *not* merge).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks all user stories**. It is one compilation unit: T002 → T003/T005/T006, with T004 before T007/T008.
- **User Stories (Phase 3–5)**: all depend on Phase 2.
  - US1 depends only on Phase 2.
  - US2 depends on Phase 2, and its `Next` field (T018) must land before T019; US2 does not depend on US1.
  - US3 depends on Phase 2 and is best done after US2, so the matches it writes out cover the final set.
- **Polish (Phase 6)**: depends on all three stories.

### Within the Foundational phase

- T002 ∥ T004 (different files, and the capture does not depend on the command type), then T005 → T006, then T003 (needs T002, T004, T005, T006), then T007 ∥ T008, then T009 ∥ T010, then T011.
- T005 ∥ T006 are separate additions to `world/Zone.fs`, so they are one file and must be written in order despite touching different functions.

### Within the User Story phases

- US1: T012–T014 share `test/SelectionTests.fs` and are written in order; T015 (SpawningTests) and T016 (ZoneTests) are independent files; T017 follows T016.
- US2: T018 → T019 → T022; T020 ∥ T021 ∥ T023 are independent files.
- US3: T024 → T025; T026 is independent.

### Parallel Opportunities

- T009 ∥ T010 (`test/ZoneTests.fs` and `test/SpawningTests.fs`).
- US1's T012–T016 span three test files, so the ones in different files can be written together.
- US2's T020, T021, T023 are three different test files and can be done in parallel once T019 lands.
- T029 ∥ T030 in Polish.

---

## Parallel Example: User Story 2

```bash
# After T019 (the link is on the record), these are on different files:
Task: "Update the Waypoint construction in test/EntityTests.fs"          # T020
Task: "Rewrite the duplicated-waypoint cases in test/SpawningTests.fs"   # T021
Task: "Update the waypoint link cases in test/SelectionTests.fs"         # T023
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational) — the per-kind families and the `Zone` surface.
2. Complete Phase 3 (US1) — the exactness proof, which is the invariant the whole feature exists to restore.
3. **STOP and VALIDATE**: run `test/SelectionTests.fs`, `test/ZoneTests.fs`, `test/SpawningTests.fs`; confirm an actor's overlay survives delete and undo.
4. Phase 2 is not independently shippable without US1's tests: it changes the command model without proving the guarantee, which is why they land together.

### Incremental Delivery

1. Foundational → the app compiles and behaves as before, with the trigger's parallel path folded in and the overlay fixed as a by-product.
2. US1 → exactness is proven per kind, and the regression guard exists.
3. US2 → the link moves onto the waypoint, `Zone.Links` is deleted, and a copy carries the link.
4. US3 → a kind without a round-trip fails to build.

---

## Notes

- [P] tasks touch different files and have no incomplete dependencies; tasks in the same file are ordered even when they are independent.
- **Why Phase 2 is large**: `Command` is a discriminated union every caller matches on, so the type change and the rewiring are one compilation unit. Splitting them would leave a build that does not compile at a phase checkpoint.
- **Why US1's phase is test-dominant**: the capture added in Phase 2 is what satisfies FR-001 to FR-003 and FR-007 to FR-008; nothing further is needed in code, so the phase's job is to prove it per kind and leave the regression guard behind.
- **Three existing tests assert behaviour this feature changes and must be rewritten, not adjusted** (listed in `quickstart.md`): `SpawningTests` "a duplicated waypoint starts its own chain" (T021), `ZoneTests` "chains live with their zone" (T022), and every `Spawn`/`Delete`/`Duplicate` assertion (T010).
- **The capture and the restore are deliberate mirrors**: the capture is a record-update (a new field is carried, never dropped silently) and the restore is an explicit construction (a new field breaks the build until it is classified). Together they are what makes FR-006 and SC-004 hold for a property as well as for a kind.
- Nothing in `render/`, `physics/`, `screen/`, or `core/` changes; `editor/Inspector.fs` and `editor/Viewport.fs` are deliberately untouched (T029).
- Verify `dotnet test` passes after each phase; commit per task or logical group.
