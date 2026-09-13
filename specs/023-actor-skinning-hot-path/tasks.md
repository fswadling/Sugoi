---

description: "Task list for the Actor Skinning Hot Path feature"
---

# Tasks: Actor Skinning Hot Path

**Input**: Design documents from `/specs/023-actor-skinning-hot-path/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Added — `samplePose` is pure CPU math and `test/ActorTests.fs` already drives it headlessly with synthetic models. Two guards are added: golden pose tests over a model with active channels (FR-003), and an allocation test around `samplePose` (FR-001).

**Organization**: The refactor is two local changes (Foundational); the user stories validate allocation, cost, and output identity.

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

**Purpose**: Record the baseline before the change.

- [X] T001 Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj` and confirm clean / passing before any change.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Capture the current output as a guard, then perform the two refactors.

**⚠️ CRITICAL**: US1–US3 all validate these two changes.

- [X] T002 Extend `test/ActorTests.fs`: add a model builder that populates `FrameChannels`/`NumFrameChannels`/`PoseChannelOffsets` and a `Pose` with a non-zero `ChannelMask`, then add golden tests asserting the sampled `Local`/`World`/`Skinning` for: (a) a joint with active channels, (b) a root joint (no parent), (c) a non-looping clip sampled at its last frame, and (d) the no-op guards (zero frames, zero joints) leave the pose untouched and do not throw. These MUST pass on the current code before the refactor (FR-003, FR-006, US3/AC2).
- [X] T003 In `world/Actor.fs`, change `sampleJoint` to return `struct (Vector3 * Quaternion * Vector3)` and update the two destructuring sites in `samplePose` to `let struct (t0, r0, s0) = …` (FR-001). This is private-only: `samplePose`'s public signature is unchanged (FR-005).
- [X] T004 In `world/Actor.fs`, change `localMatrix` to build `Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r)` and write the translation directly (`m.M41 <- t.X`, `m.M42 <- t.Y`, `m.M43 <- t.Z`), removing the `CreateTranslation` multiply (FR-002). Do not touch the channel sampling in `sampleJoint` (research D3). `localMatrix` is private, so no public signature changes (FR-005).

**Checkpoint**: The sampler allocates no reference tuple and composes the local transform with one fewer multiply; output is unchanged (T002 still passes).

---

## Phase 3: User Story 1 - Sampling a pose allocates nothing per frame (Priority: P1) 🎯 MVP

**Goal**: The sample path allocates no garbage per frame.

**Independent Test**: sample a pose headlessly and assert zero allocated bytes.

- [X] T005 [US1] Add a headless allocation test in `test/ActorTests.fs`: warm up `samplePose`, then assert `System.GC.GetAllocatedBytesForCurrentThread()` does not increase across many `samplePose` calls on a synthetic multi-joint model (FR-001).

**Checkpoint**: Zero allocations per sampled frame.

---

## Phase 4: User Story 2 - Skinning does less work per joint (Priority: P1)

**Goal**: A Debug build is not dominated by CPU skinning; Release does not regress.

**Independent Test**: measure the per-actor `record` delta in Debug and Release with Akane.

- [X] T006 [US2] Run `quickstart.md` scenarios 2 and 3: with one (then two) Akane actor(s) in **Debug**, the per-actor `record` delta is materially lower than the ~3 ms baseline (target ≥25%); in **Release** the ~0.1 ms delta is unchanged (SC-002, SC-003). Inspect `localMatrix` to confirm one fewer multiply (FR-002).

**Checkpoint**: Debug actor cost reduced; Release unchanged.

---

## Phase 5: User Story 3 - The animation is unchanged (Priority: P2)

**Goal**: Same sampled pose and on-screen deformation.

**Independent Test**: golden tests pass; the actor looks identical.

- [X] T007 [US3] Run `quickstart.md` scenario 4: the actor's idle animation is identical in Debug and Release — no popping, offset, or scale drift (SC-004). The golden tests from T002 guard the sampled matrices.

**Checkpoint**: Output and appearance unchanged.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and final validation.

- [X] T008 [P] Note the sampler optimisation in `docs/RENDER_PERFORMANCE.md` §9: record that the Debug-only per-actor cost was reduced by removing a per-joint reference tuple and a redundant local-transform multiply.
- [X] T009 Run `quickstart.md` end to end and confirm `dotnet build` clean + `dotnet test` green (SC-005).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on T001. T002 (golden tests) MUST be in place before T003/T004 so a changed output is caught. T003 and T004 are independent of each other.
- **User Stories (Phases 3–5)**: Depend on T003/T004.
- **Polish (Phase 6)**: Depends on the stories.

### Within Each User Story

- T002 and T005 both edit `test/ActorTests.fs`, so they are **not** parallelisable with each other — write them together or sequentially.
- T006/T007 need the running app (a GPU) and are user-run.

### Parallel Opportunities

- T003 and T004 touch different functions in the same file (`world/Actor.fs`), so run them sequentially to avoid conflicts.
- T008 (docs) is independent of the code and tests.

---

## Parallel Example: Polish

```bash
# Independent of the code and tests:
Task: "Note the sampler optimisation in docs/RENDER_PERFORMANCE.md §9"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Setup + Foundational (T001–T004) — the guard and the two refactors.
2. US1 (T005): prove the sample path allocates nothing.
3. US2 (T006): measure the Debug reduction and unchanged Release.
4. US3 (T007): confirm the animation is identical.

---

## Notes

- [P] tasks = different files, no dependencies.
- The whole code change is two edits in one function pair (`sampleJoint`/`localMatrix`); treat it as one commit.
- The channel-sampling `match` is deliberately left alone (research D3).
- FR-001/FR-002 are structural (inspection + the T005 allocation test); SC-002/SC-003 need the running app (T006); FR-003/SC-004 are guarded by T002 and T007; FR-005 (no public API change) is guarded by the build (T009) — any public signature change would break callers.

---

## Outcome (recorded after implementation)

- **T006 — SC-002 not met.** Measured in Debug with Akane: the per-actor `record`
  delta was ~3.2 ms after the change vs ~3.0 ms before — unchanged within run-to-run
  noise (the baseline itself swings ~1.3–3.0 ms between runs). The two refactors
  removed the per-frame allocation (FR-001, proven by the T005 allocation test) and
  one local-transform multiply (FR-002), but neither was the dominant cost.
- **T007 — animation unchanged.** The actors rendered and animated identically in the
  Debug run, and the T002 golden tests guard the sampled matrices.
- **Decision:** retain the changes as allocation/multiply hygiene and close the
  feature; the Debug per-actor cost is dominated by other managed work (channel
  sampling, `Quaternion.Slerp`, the remaining matrix math) and is recorded as a
  negative result in `RENDER_PERFORMANCE.md` §9. Reducing it further needs profiling
  first, not another guess.
