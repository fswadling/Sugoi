# Tasks: Entity Reference Integrity

**Input**: Design documents from `/specs/015-entity-ref-integrity/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — the feature's guarantees (stale-ref non-aliasing, epoch scoping, restore conflicts) are only verifiable by tests. `test/SlotMapTests.fs`, `test/ZoneTests.fs`, `test/SpawningTests.fs` cover them directly.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

**Status**: All tasks complete — documented retroactively; the implementation already exists in the working tree.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Single project**: `core/`, `world/`, `state/`, `editor/`, `screen/`, `test/` at repository root (see `fsharp-game.fsproj` compile order).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Land the new modules and slot-map shape the stories build on.

- [X] T001 Convert `core/SlotMap.fs` from an immutable struct to a `[<Sealed>]` class mutated in place: read-only `Slots : ReadOnlySpan<Slot<'T>>`, `byref` `Get(handle)`/`Get(index, generation)`, `Add`/`Free`/`Count`, and `IteriAlive(index, generation, value)`
- [X] T002 Add `editor/Spawning.fs`, `test/ZoneTests.fs`, `test/SpawningTests.fs`, and `test/xunit.runner.json` to `fsharp-game.fsproj` / `test/fsharp-game.Tests.fsproj` compile lists (Spawning before `editor/Selection.fs`; runner config disables test-collection parallelization)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The reference identity model, the store API, and the zone/environment seams that every story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 [P] Add `Generation` + `Epoch` to `EntityRef` with `actorRef`/`propRef`/`partRef` constructors and a `Part` case on `EntityKind`; add `Epoch` + `Parts : SlotMap<Prop>` to `Environment` in `world/Entity.fs` (and fix `test/EntityTests.fs` construction)
- [X] T004 [P] Add editor-facing accessors `IsAlive`, `IsOccupied`, `TryGet`, `Occupy` (raise on an alive slot; rebuild the free list), and generation-checked `RemoveByIndex` to `core/SlotMap.fs` (depends T001)
- [X] T005 Rewrite `world/Zone.fs` ref resolution and mutation: `partsFor`/`isAlive`/`tryEntityAt`, generation-checked `bindingAt`/`setTransform`/`removeEntity`, and per-kind `spawnActor`/`spawnProp`/`spawnPropBinding` returning generation-stamped refs (depends T002, T003, T004)
- [X] T006 Move environment parts into the environment's own store in `world/Zone.fs`: `buildEnvironment` (fresh parts map + epoch), `attachPart`/`deactivateEnvironment`/`activateEnvironment`, `setEnvironment`, `loadEnvironment`, `spawnPartBinding`; drop the old shared-store `clearEnvironment` (depends T005)
- [X] T007 Update `screen/GameplayScreen.fs` to the new API: span scan + `byref` `Get` for animation advance, and draw placed props and the current environment's parts (depends T005, T006)

**Checkpoint**: Foundation ready — the three user stories can now proceed in parallel.

---

## Phase 3: User Story 1 - A stale reference never acts on a different entity (Priority: P1) 🎯 MVP

**Goal**: References carry the slot's generation, so a deleted entity's ref resolves to nothing and never aliases a reused slot.

**Independent Test**: `ZoneTests` deletes an actor/prop, reuses the slot, and asserts the old ref resolves to nothing while the new occupant is intact; `SlotMapTests` proves `Get`/`RemoveByIndex` reject stale generations.

### Implementation & Tests for User Story 1

- [X] T008 [US1] Route editor read/write through `tryEntityAt`/`setTransform` in `editor/Editor.fs` (`selectedTransform`, `setSelectedTransform`, `focus`, `finishDrag`, `updateNudge`) and `editor/Selection.fs` (`deleteRef`, `duplicateRef`)
- [X] T009 [P] [US1] Compare full incarnation refs in `editor/Viewport.fs` (`buildLines`) and re-resolve the collider toggle through generation-checked `Get` in `editor/Inspector.fs`
- [X] T010 [P] [US1] Stamp each outliner row with its slot generation in `editor/Outliner.fs` (`actorRef`/`propRef`)
- [X] T011 [P] [US1] Rewrite `test/SlotMapTests.fs` for the class API and add generation tests: stale `Get` raises, `IsAlive`/`IsOccupied` semantics, `TryGet`, recycled-slot rejection
- [X] T012 [P] [US1] Add `test/ZoneTests.fs` stale-ref cases: live ref resolves, remove makes it stale, a reused slot is not aliased, `setTransform` stays on the right entity (depends T005)

**Checkpoint**: User Story 1 is functional and testable independently.

---

## Phase 4: User Story 2 - Environment parts are scoped and listed per environment (Priority: P2)

**Goal**: Parts belong to their environment and are listed in the outliner; swaps never disturb placed content or resolve a stale part ref.

**Independent Test**: `ZoneTests` loads two environments and asserts placed props/actors are untouched, the first environment's part refs resolve to nothing, and swapping back revives them; the outliner shows a Parts section.

### Implementation & Tests for User Story 2

- [X] T013 [US2] Add a Parts section to `editor/Outliner.fs` listing the loaded environment's parts with epoch-stamped refs (depends T006)
- [X] T014 [P] [US2] Draw environment parts and include them in line building in `editor/Viewport.fs` (`buildLines`) and `screen/GameplayScreen.fs` (`renderScene`) (depends T007)
- [X] T015 [US2] Add `dropStalePartSelection` to `editor/Selection.fs` and call it after an environment load in `editor/AssetBrowser.fs` (depends T006)
- [X] T016 [P] [US2] Add `test/ZoneTests.fs` epoch-scoping and lifecycle cases: parts use their own map, a foreign-epoch ref resolves to nothing/is inert, `loadEnvironment`/`setEnvironment` create/destroy bodies per part, retain slots, and revive refs on return (depends T006)

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Undo restores the exact entity without clobbering (Priority: P3)

**Goal**: Undo restores a deleted entity into its recorded slot or reports a conflict; every editor-created entity is paired with its reversing command.

**Independent Test**: `SpawningTests` duplicates a part (copy selected + reversing command), fails loudly for a non-entity kind, and reports a conflict when restoring onto a live slot; `ZoneTests` round-trips an environment load through `UndoStack.undo`/`redo`.

### Implementation & Tests for User Story 3

- [X] T017 [US3] Change `Command.SetEnvironment` to store whole `Environment` values; route `Transform` through `setTransform` and restore through `setActorWithAsset`; coalesce `Transform`s only on equal full refs in `state/Command.fs` (depends T005, T006)
- [X] T018 [US3] Add `Zone.ensureSlotFree` and make `setActorWithAsset` restore via `Occupy` (checking the slot before allocating renderer/physics resources), raising on a taken slot, in `world/Zone.fs` (depends T005, T006)
- [X] T019 [US3] Add `editor/Spawning.fs` as the single creation surface (spawnActor/spawnProp/duplicate, each pairing a `Zone.spawn*` with its reversing command and selecting the result) and route `editor/AssetBrowser.fs` + `editor/Selection.fs` through it (depends T017)
- [X] T020 [US3] Add shared `undoStep`/`redoStep` in `editor/Editor.fs` that reconcile a stale Part selection, used by both the keyboard shortcut and the Edit menu (depends T015)
- [X] T021 [P] [US3] Add `test/SpawningTests.fs`: headless fixture, duplicate pairing, loud failure for non-entity kinds, occupied-slot conflict on restore; add `UndoStack` environment round-trip cases to `test/ZoneTests.fs` (depends T017, T018, T019)

**Checkpoint**: All three user stories are functional and independently testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and end-to-end verification.

- [X] T022 [P] Update docs for the implemented behaviour: `docs/ARCHITECTURE.md` (ref generation/epoch, environment parts), `docs/EDITOR_DESIGN.md` (Spawning + restore invariant, selection reconcile), `docs/ROADMAP.md` (phase 2 entity-store deliverables), `docs/PROJECT_STRUCTURE.md` (current cluster layout), `docs/RENDERING_NOTES.md` (`world/` renderer paths)
- [X] T023 Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj`, then walk the `quickstart.md` scenarios to confirm stale-ref non-aliasing, environment scoping, and undo restore/conflict behaviour

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Stories (Phases 3–5)**: Depend on Foundational. US1 and US2 are independent; US3's `Spawning` surface (T019) sets up the shared creation path but its tests (T021) also cover US1/US2 behaviour.
- **Polish (Phase 6)**: Depends on all stories.

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational; no dependency on US2/US3.
- **User Story 2 (P2)**: Depends on the environment store (T006) only; independent of US1.
- **User Story 3 (P3)**: Uses the ref model (US1) and environment store (US2) but is independently testable through `Spawning`/`UndoStack`.

### Within Each User Story

- Store/entity changes before the editor integration that consumes them.
- Pure-logic tests alongside the change they cover.
- Story complete before moving to the next priority.

### Parallel Opportunities

- `T003` and `T004` touch different files and can run in parallel.
- Once Foundational completes, US1 (`T008`–`T012`) and US2 (`T013`–`T016`) can proceed in parallel.
- Test tasks `T011`, `T012`, `T016`, `T021` touch `test/` files and can be written alongside their story's implementation.

---

## Parallel Example

```text
# Foundational, in parallel:
Task: T003 — EntityRef generation/epoch + Environment.Parts (world/Entity.fs)
Task: T004 — SlotMap editor accessors (core/SlotMap.fs)

# User Story 1 and 2 implementation, in parallel:
Task: T009 — Viewport/Inspector incarnation-aware access (editor/Viewport.fs, editor/Inspector.fs)
Task: T013 — Outliner Parts section (editor/Outliner.fs)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 + 2.
2. Complete Phase 3 (US1).
3. **STOP and VALIDATE**: run `SlotMapTests` + `ZoneTests` stale-ref cases.
4. Then deliver US2 (environment scoping) and US3 (undo restore).

### Incremental Delivery

1. Setup + Foundational → slot-map class API, ref identity, environment store.
2. US1 → stale refs are inert (MVP).
3. US2 → parts are environment-scoped and listed.
4. US3 → undo restores exactly, or reports a conflict.
5. Polish → docs + build/test/quickstart verification.

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps a task to US1/US2/US3 for traceability.
- All tasks are marked complete: this task list was generated after the change
  landed, to record the work against `specs/015-entity-ref-integrity`.
- The load-bearing invariant (`editor/Spawning.fs` is the only caller of
  `Zone.spawn*`) is documented in `docs/EDITOR_DESIGN.md` and guarded by
  `SlotMap.Occupy` raising on a taken slot.
