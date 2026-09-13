---

description: "Task list for the Multiple Zones feature"
---

# Tasks: Multiple Zones

**Input**: Design documents from `/specs/024-multiple-zones/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — the plan (`Testing`) and quickstart specify xUnit coverage in a new `test/GameplayStateTests.fs`, with `test/PlayerTests.fs` and `test/SpawningTests.fs` updated.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing. This feature is a container refactor, so Phase 2 carries the shared model/plumbing that every story reads; the story phases add the user-visible capabilities on top.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- F# single project at the repository root; modules live in domain clusters (`core/`, `physics/`, `render/`, `world/`, `state/`, `editor/`, `screen/`).
- Compile order is defined by `fsharp-game.fsproj`; no new source file is added (there is no `world/ZoneMap.fs`, and the zones UI lives in the existing `editor/Outliner.fs`).
- Tests live in `test/` (one `<Module>Tests.fs` per module).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Register the new test file in the build.

- [X] T001 Create `test/GameplayStateTests.fs` (empty `module GameplayStateTests` skeleton) and add `<Compile Include="GameplayStateTests.fs" />` to `test/fsharp-game.Tests.fsproj`, after `PlayerTests.fs` and before `EnvironmentBatchTests.fs`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The container and the change from a single `state.Zone` to a name-keyed map with one active zone. Every user story reads the active zone, so this must land first.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Add `mutable Player : SlotMap.Handle voption` to the `Zone` record and initialise it to `ValueNone` in `Zone.create` in `world/Zone.fs`.
- [X] T003 [P] Add `removeBody`, `addBody`, and `optimizeBroadPhase` wrappers over `BodyInterface.RemoveBody`/`AddBody` and `PhysicsSystem.OptimizeBroadPhase` in `physics/Physics.fs`.
- [X] T004 Add `Zone.deactivatePhysics` (RemoveBody each attached actor/prop/part body) and `Zone.activatePhysics` (AddBody each, then `optimizeBroadPhase`) in `world/Zone.fs`; no body, shape, renderer, or `Physics` attachment is destroyed or rebuilt (depends on T002, T003).
- [X] T005 Rewrite the zone container in `state/GameplayState.fs`: replace `Zone : Zone` with `Zones : Dictionary<string, Zone>` and `mutable ActiveZone : string`, remove `Player`, add `UndoByZone : Dictionary<string, Command.UndoStack>`; add `activeZone` (one lookup), `switchZone` (validate, no-op-if-active, detach/attach, swap undo, clear selection/drag/nudge, re-frame camera), and `addZone` (reject empty/duplicate name, insert, make active); seed one zone named `main` in `create` (depends on T004).
- [X] T006 Change the player systems in `world/Systems.fs` to read `zone.Player` and drop the `player` parameter from `isPlayerLive`, `playerInput`, `cameraFollow`, and `animate` (depends on T002).
- [X] T007 [P] Resolve the active zone in `editor/Editor.fs` (`GameplayState.activeZone state`) instead of reading `state.Zone` (depends on T005).
- [X] T008 [P] Resolve the active zone in `editor/Outliner.fs` (depends on T005).
- [X] T009 [P] Resolve the active zone in `editor/Selection.fs` (depends on T005).
- [X] T010 [P] Resolve the active zone in `editor/Spawning.fs` (depends on T005).
- [X] T011 [P] Resolve the active zone in `editor/AssetBrowser.fs` so actor/prop spawns and the environment load target the active zone (depends on T005).
- [X] T012 [P] Resolve the active zone in `editor/Inspector.fs` (depends on T005).
- [X] T013 Bind the active zone once per frame in `screen/GameplayScreen.fs` (`let zone = GameplayState.activeZone state`) and drive `update` and `renderScene` from it only, then update its `Systems` calls to the new signatures (depends on T005, T006).
- [X] T014 In `Engine.fs`, make the per-second stats line read the active zone and make shutdown release **every** zone's `EnvironmentBatch` by iterating `GameplayState.Zones.Values` (depends on T005).
- [X] T015 Update `test/SpawningTests.fs` (the `gameplayState` helper builds the new `Zones`/`ActiveZone` fields and no `Player`) and `test/PlayerTests.fs` (set `zone.Player`, call the new system signatures) so the suite compiles (depends on T005, T006, T013).

**Checkpoint**: The app builds and runs with a single zone named `main`, exactly as before; existing tests pass.

---

## Phase 3: User Story 1 - Add, select, and delete zones (Priority: P1) 🎯 MVP

**Goal**: A **Zones** collapsing section at the top of the Outliner lists the zones, adds one under a developer-typed name, deletes one, and selects the active zone; the workspace follows the active zone.

**Independent Test**: Add a zone named `classroom`; give each zone a prop; switch back and forth and confirm each zone shows only its own prop; delete a zone and confirm it and its contents are gone while the others are unchanged; confirm empty/duplicate names create nothing and the last zone cannot be deleted.

### Implementation for User Story 1

- [X] T016 [P] [US1] Add the `inputText` P/Invoke over cimgui's `igInputText` to `render/ImGui.fs` (`extern bool inputText(string label, byte[] buf, unativeint bufSize, int flags, nativeint callback, nativeint userData)`).
- [X] T017 [P] [US1] Add `NewZoneName : byte[]` to `EditorState` and allocate it in `EditorState.create` in `state/EditorState.fs`.
- [X] T018 [US1] Add `Zone.release` to `world/Zone.fs`: destroy every body the zone owns (actors, placed props, environment parts), release each actor/prop/part renderer, release the zone's `EnvironmentBatch`, and clear `BodyToEntity`. The zone is inactive at this point, so its bodies are detached: remove-if-added then destroy (`IsAdded` guard, then `RemoveAndDestroyBody`).
- [X] T019 [US1] Add `GameplayState.deleteZone` to `state/GameplayState.fs`: raise when the name is unknown or it is the last zone; if the name is active, first `switchZone` to another remaining zone; then `Zone.release` it (with `shared.Gpu.Device`) and remove its `Zones` and `UndoByZone` entries (depends on T005, T018).
- [X] T020 [US1] Add a **Zones** `collapsingHeader` at the top of `editor/Outliner.fs`, above the scene objects: sort `state.Zones` by name and draw a select row per zone (marking `state.ActiveZone`, with a *Del* button that calls `GameplayState.deleteZone`), a name text field over `state.Editor.NewZoneName`, and an *Add Zone* button that calls `GameplayState.addZone` only when the decoded name is non-empty and unused (then clears the buffer); selecting a row calls `GameplayState.switchZone`. Disable *Add Zone* while the name is empty or already used, and disable *Del* while only one zone remains, so the container's guards are unreachable through the UI (depends on T005, T016, T017, T019).
- [X] T021 [US1] Add `test/GameplayStateTests.fs` cases: `addZone` inserts and activates a named zone; empty and duplicate names raise; `switchZone` to an unknown name raises and to the active name is a no-op; `deleteZone` rejects the last zone and unknown names, removes the zone and its contents, makes another zone active when deleting the active one, and leaves the active zone unchanged when deleting an inactive one; switching preserves each zone's entities and swaps/loses no undo history; and `Zone.release` destroys the zone's bodies and clears its `BodyToEntity` (depends on T005, T018, T019, T020).

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - Zones are isolated worlds (Priority: P2)

**Goal**: Only the active zone renders and collides; loading an environment affects only the active zone.

**Independent Test**: Give two zones overlapping environments, confirm only the active zone's geometry renders and blocks, switch, and confirm the other.

### Verification for User Story 2

> US2's isolation is implemented by T004 (a switch detaches/attaches bodies) and
> T011 (an environment load targets the active zone); this phase verifies it.

- [X] T022 [US2] Add `test/GameplayStateTests.fs` cases: after a switch the outgoing zone's bodies are detached (not in the broadphase) and the incoming zone's are attached, with entities/transforms preserved; a raycast identifies only the active zone's bodies; and loading an environment changes only the active zone's `Environment` (depends on T004, T005, T011).

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Each zone designates its own player (Priority: P3)

**Goal**: Each zone designates its own player actor; only the active zone's designation is live.

**Independent Test**: Designate an actor as the player in each of two zones, switch between them, enter play, and confirm the correct actor is driven each time and none where there is no designation.

### Implementation for User Story 3

- [X] T023 [US3] Make *Set as Player* in `editor/Inspector.fs` write `(GameplayState.activeZone state).Player` instead of `state.Player` (depends on T005, T012).
- [X] T024 [US3] Add `test/PlayerTests.fs` / `test/GameplayStateTests.fs` cases: a designation set in one zone does not resolve against a same-index actor in another zone; a deleted/recycled designee makes the systems inert; a zone with no designation drives nothing (depends on T006, T023).

**Checkpoint**: All three user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Consistency across the feature and end-to-end validation.

- [X] T025 [P] Update `docs/ARCHITECTURE.md`: note that zones are a name-keyed map with one active zone, that a switch detaches/attaches bodies in the one shared physics world (`RemoveBody`/`AddBody`) while deletion destroys them, and that the player designation and undo history are per zone.
- [X] T026 [P] Update `docs/ROADMAP.md`: mark the Zones phase complete with its spec reference (`specs/024`).
- [ ] T027 Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj`, then walk `specs/024-multiple-zones/quickstart.md` and confirm each scenario.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks all user stories**.
- **User Stories (Phase 3–5)**: all depend on Phase 2.
  - US1 has no dependency on US2/US3.
  - US2's isolation behaviour is implemented by T004/T011 (Phase 2/US1); US2 adds verification only.
  - US3 depends on T002/T006 (Phase 2).
- **Polish (Phase 6)**: depends on the desired stories being complete.

### Within the Foundational phase

- T002 (Zone.fs) and T003 (Physics.fs) can run in parallel.
- T004 depends on T002 and T003; T005 depends on T004.
- T006 depends on T002.
- T007–T012 depend on T005 and touch disjoint files, so they can run in parallel.
- T013 depends on T005 and T006; T014 depends on T005; T015 depends on T005, T006, T013.

### Within User Story 1

- T016 ∥ T017 (different files).
- T018 (Zone.fs) before T019 (GameplayState.fs) before T020 (Outliner.fs).
- T021 depends on T018, T019, and T020.

### Parallel Opportunities

- T002 ∥ T003.
- T007 ∥ T008 ∥ T009 ∥ T010 ∥ T011 ∥ T012 (after T005).
- T016 ∥ T017 (US1).
- T025 ∥ T026 (Polish).

```bash
# After T005, resolve the active zone in parallel:
Task: "Resolve the active zone in editor/Editor.fs"
Task: "Resolve the active zone in editor/Outliner.fs"
Task: "Resolve the active zone in editor/Selection.fs"
Task: "Resolve the active zone in editor/Spawning.fs"
Task: "Resolve the active zone in editor/AssetBrowser.fs"
Task: "Resolve the active zone in editor/Inspector.fs"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational) — the container and active-zone plumbing.
2. Complete Phase 3 (US1) — the Zones section (add/select/delete).
3. **Stop and validate**: manage zones from the Outliner; run `test/GameplayStateTests.fs`.

### Incremental Delivery

1. Foundational → the app behaves as before with one zone named `main`.
2. US1 → add/select/delete zones (MVP).
3. US2 → physics/render isolation across zones.
4. US3 → per-zone player designation.

---

## Notes

- [P] tasks touch different files and have no incomplete dependencies.
- No new source module is added; the container is a `Dictionary` field on `GameplayState` and the zones UI is a section in the existing Outliner.
- A switch never destroys or rebuilds bodies or shapes; it detaches/attaches them (`RemoveBody`/`AddBody`) and re-optimises the broadphase. Deletion destroys the bodies and releases GPU resources.
- Verify `dotnet test` passes after each phase; commit per task or logical group.

---

## Phase 7: Convergence

- [X] T028 Update the `docs/ARCHITECTURE.md` mermaid so the runtime zone container is the name-keyed `Zones` dictionary on `Gameplay_Screen` with an active zone name, matching the F#-port prose rather than a `Zone_Map` slot map, per FR-001 (contradicts).
- [X] T029 Update `docs/PROJECT_STRUCTURE.md`: remove the planned `world/ZoneMap.fs` entry (zones are a name-keyed dictionary field on `GameplayState`), per plan: structure decision (contradicts).
- [X] T030 Update the `docs/ROADMAP.md` "Next step" section to point at phase 9 (cue engine + editor) now that phase 8 is complete, per plan (partial).
- [X] T031 Add a camera-re-frame assertion to `test/GameplayStateTests.fs`: after switching zones the camera target follows the incoming zone (its designated player, otherwise the origin), per FR-009 / US3 AC3 (partial).
