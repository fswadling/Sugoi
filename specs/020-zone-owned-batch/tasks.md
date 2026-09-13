---

description: "Task list for the Zone-Owned Static Batch feature"
---

# Tasks: Zone-Owned Static Batch

**Input**: Design documents from `/specs/020-zone-owned-batch/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: None added — an ownership/location refactor with no new pure logic (plan.md "Testing"). The existing headless suite (`ZoneTests`, `PlayerTests`, `SpawningTests`) is the guard, and `test/SpawningTests.fs` must be updated because it hand-builds a `GameplayState`.

**Organization**: Tasks are grouped by user story. The ownership move is a single coupled change (Foundational); the user stories validate its outcomes.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- F# single project at the repository root; modules live in domain clusters (`core/`, `physics/`, `render/`, `world/`, `state/`, `editor/`, `screen/`).
- Compile order is defined by `fsharp-game.fsproj`; a file may only reference symbols defined in earlier files.
- Tests live in `test/` (one `<Module>Tests.fs` per module).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Reorder the build so the batch type precedes the zone that owns it.

- [X] T001 Move `<Compile Include="world/EnvironmentBatch.fs" />` in `fsharp-game.fsproj` from after `world/Zone.fs` to immediately after `world/Entity.fs` (and before `world/Zone.fs`), so `Zone` can reference `EnvironmentBatch`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The coupled ownership move — the batch becomes zone-owned, its device use becomes lazy, and the render/log/release call sites follow.

**⚠️ CRITICAL**: The tasks below must land together to compile; no user story can be validated until they do.

- [X] T002 Make `render/InstanceBuffer.fs` device-lazy: drop the `Device` field; `create : unit -> InstanceBuffer` allocates nothing (`Buffer`/`Transfer` = `IntPtr.Zero`, capacity 0); `ensure`/`upload`/`reclaim` take the device; add `release` that frees the current and retired GPU/transfer buffers.
- [X] T003 Update `world/EnvironmentBatch.fs`: `create : unit -> EnvironmentBatch` (device-free); change `gather` to take `(device, batch, props: SlotMap<Prop>, parts: SlotMap<Prop> option)` instead of `(batch, zone)`; `upload` takes the device; add `release`; keep `render` unchanged (one instanced draw per group).
- [X] T004 Add the batch to `world/Zone.fs`: a `Batch : EnvironmentBatch.EnvironmentBatch` field; `Zone.create ()` sets it with `EnvironmentBatch.create ()`; add `gatherStatic : Gpu.Device -> Zone -> unit` that calls the batch's gather with the zone's `Props` and environment `Parts`.
- [X] T005 Remove `Batch` from `state/GameplayState.fs` and revert `create` to `(debugDraw, gizmoDraw)` — it no longer needs shared state now that the zone owns and creates the batch.
- [X] T006 Update `screen/ScreenManager.fs`: call `GameplayState.create debugDraw gizmoDraw` (no shared state).
- [X] T007 Update `screen/GameplayScreen.fs` `renderScene`: gather via `Zone.gatherStatic shared.Gpu.Device state.Zone`, upload via `EnvironmentBatch.upload shared.Gpu.Device state.Zone.Batch pass.Cmd`, and draw via `EnvironmentBatch.render state.Zone.Batch …`.
- [X] T008 Update `Engine.fs`: the per-frame log reads `manager.Gameplay.Zone.Batch.DrawCalls`/`InstanceCount`, and shutdown releases the live zone's batch (`EnvironmentBatch.release shared.Device manager.Gameplay.Zone.Batch`) after `WaitForGPUIdle` and before `Gpu.destroy`.
- [X] T009 Update `test/SpawningTests.fs`: the hand-built `GameplayState` drops the `Batch` field.

**Checkpoint**: The zone owns the batch, `Zone.create ()` is GPU-free, and the app compiles.

---

## Phase 3: User Story 1 - A zone owns the batch that draws it (Priority: P1) 🎯 MVP

**Goal**: The zone owns its static-scene batch; the screen state has none.

**Independent Test**: inspect the two state types.

- [X] T010 [US1] Run `quickstart.md` scenario 1: confirm `state/GameplayState.fs` has no batch field and `world/Zone.fs` owns it.

**Checkpoint**: Ownership is on the zone.

---

## Phase 4: User Story 3 - The scene still renders identically (Priority: P1)

**Goal**: Same scene, same draw counts, GPU-free zone creation, buffers released at shutdown.

**Independent Test**: run the app and compare the frame log; run the headless suite.

- [X] T011 [US3] Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj`: the headless suite (which calls `Zone.create ()` with no device) passes, confirming zone creation stayed GPU-free (SC-004/FR-004).
- [ ] T012 [US3] Run `quickstart.md` scenario 3: the ArtRoom still reports `draws 191 instances 521`, adding a prop still adds only its distinct submeshes, and the scene looks unchanged. Separately (a static check, no run needed) confirm no changes under `physics/` or `state/Command.fs`, and that `world/Zone.fs` changes are limited to the `Batch` field and `gatherStatic` (FR-008).
- [ ] T013 [US3] Run `quickstart.md` scenario 5: start and quit the app; it shuts down cleanly with the live zone's batch released before the device is destroyed.

**Checkpoint**: The move is behaviour-preserving and lifecycle-correct.

---

## Phase 5: User Story 2 - Zones never share batching state (Priority: P2)

**Goal**: Each zone has its own batch.

**Independent Test**: create two zones with no device; confirm their batch objects are distinct.

- [X] T014 [US2] Confirm headlessly (no device, no run) that two `Zone.create ()` calls yield distinct batch objects — cross-zone independence is structural, since each zone owns its own batch. Simultaneous-zone *rendering* is deferred to the zone phase, so there is nothing to run for this check.

**Checkpoint**: Batching state is per-zone.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and final validation.

- [X] T015 [P] Update `docs/PROJECT_STRUCTURE.md` / `docs/ARCHITECTURE.md` if they describe the zone or gameplay-screen state, so the zone-owned batch is recorded.
- [ ] T016 Run the full `quickstart.md` end to end and confirm `dotnet build` + `dotnet test` pass.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies (compile-order reorder).
- **Foundational (Phase 2)**: Depends on Setup. T001 is required before T004 (Zone referencing EnvironmentBatch). T002 before T003 (batch uses the instance buffer); T003 before T004; T004 before T007; T005 before T006/T009; T008 depends on T004.
- **User Stories (Phase 3+)**: Depend on Foundational. US1 (P1) and US3 (P1) gate on the build; US2 follows.
- **Polish (Phase 6)**: Depends on all stories.

### Within Each User Story

- Build/verification after the move; the render/shutdown checks need a running app.

### Parallel Opportunities

- T015 (docs) is independent of the code tasks.
- Nothing else is safely parallel: the type changes ripple through interdependent modules.

---

## Parallel Example: Polish

```bash
# Independent of the code:
Task: "Update docs/PROJECT_STRUCTURE.md / ARCHITECTURE.md for the zone-owned batch"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Setup + Foundational (T001–T009) — the whole move, landed together.
2. US1 (T010): the batch is zone-owned.
3. US3 (T011–T013): headless suite passes, rendering unchanged, shutdown clean.
4. US2 (T014): per-zone independence.

---

## Notes

- [P] tasks = different files, no dependencies.
- The Foundational tasks are interdependent by nature — treat them as one commit.
- The compile cycle (zone owns batch; batch gathered from zone content) is broken by moving the batch before `Zone.fs` and having `gather` read the maps, not the `Zone` type.
- `DebugDraw`, `GizmoDraw`, and `Camera` stay on `GameplayState` (view overlays, not zone content).
- The run-based checks (T012, T013, T016) need a running app; T011, T014, and T015 are static.
