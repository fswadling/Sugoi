---

description: "Task list for the Actor Draw Allocation feature"
---

# Tasks: Actor Draw Allocation

**Input**: Design documents from `/specs/022-actor-draw-allocation/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: None added — the GPU bind can't be exercised headlessly, and the change is an allocation removal with no new logic. Verification is inspection + the existing suite + a runtime look. This feature is recorded retroactively for a change already made.

**Organization**: The change is one field and its call site (Foundational); the user stories validate its outcomes.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)
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

**Purpose**: The allocation removal — a per-instance binding array, created once and reused.

**⚠️ CRITICAL**: Both user stories validate this one change.

- [X] T002 In `world/ActorRender.fs`, add `BoneStorageBuffers : nativeint array` to the `ActorRender` struct and populate it as `[| boneBuffer |]` in `ActorRender.create` (alongside `BoneBuffer`). Use `nativeint array` to match the SDL3-CS `BindGPUVertexStorageBuffers` signature (there is no named `GPUBuffer` type).
- [X] T003 In `world/ActorRender.fs`, change `ActorRender.draw` to bind `r.BoneStorageBuffers` instead of the `[| r.BoneBuffer |]` array literal. No other change to the draw call.

**Checkpoint**: The draw path binds a preallocated array; no array literal remains in the per-frame code.

---

## Phase 3: User Story 1 - Drawing an actor allocates nothing per frame (Priority: P1) 🎯 MVP

**Goal**: No array/list allocation on the actor draw path.

**Independent Test**: inspect the per-frame actor path for allocations.

- [X] T004 [US1] Inspect the actor per-frame path for any other allocation: `ActorRender.draw` (only the preallocated bind), `ActorRender.upload` (reuses `BoneBytes`/`BoneTransfer`), `Actor.samplePose`/`Actor.advanceInPlace` (write into the preallocated `PoseFrame`), and `Mesh.draw` (reuses `VertexBinding`). Record the finding (FR-001, FR-005).

**Checkpoint**: The actor draw path allocates nothing per frame.

---

## Phase 4: User Story 2 - The actor still renders identically (Priority: P2)

**Goal**: Same bind, same slot/count, unchanged skinned result.

**Independent Test**: draw an animated actor and confirm identical deformation, including with several actors.

- [X] T005 [US2] Run `quickstart.md` scenarios 2 and 3: an animated actor renders exactly as before (skinning, transform, material), and with several actors each renders its own deformation (FR-002, FR-003).

**Checkpoint**: Rendering is unchanged.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Final validation.

- [X] T006 Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj` and confirm clean build + all tests green (SC-003).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on T001. T002 blocks T003.
- **User Stories (Phases 3–4)**: Depend on T003. US1 and US2 are independent verifications.
- **Polish (Phase 5)**: Depends on the change.

### Parallel Opportunities

- T004 (inspection) and T005 (runtime) are independent, but T005 needs the running app.
- T002 and T003 both edit `world/ActorRender.fs`, so they are sequential.

---

## Implementation Strategy

### MVP First (User Story 1)

1. Setup + Foundational (T001–T003) — the whole change.
2. US1 (T004): confirm the path allocates nothing.
3. US2 (T005): confirm rendering is unchanged.

---

## Notes

- [P] tasks = different files, no dependencies.
- The change is one field plus its use in `draw`; treat it as one commit.
- `BoneStorageBuffers` owns no unmanaged resource, so `release` needs no change (FR-006).
- FR-001/FR-005 are static (inspection); FR-002/FR-003 need the running app (T005).
