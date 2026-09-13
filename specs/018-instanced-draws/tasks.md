---

description: "Task list for the Instanced Static Draws feature"
---

# Tasks: Instanced Static Draws

**Input**: Design documents from `/specs/018-instanced-draws/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — plan.md ("Testing") calls out the pure grouping planner as the only headless-testable part.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

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

**Purpose**: Register the new modules and test file in the build.

- [X] T001 [P] Add `<Compile Include="render/InstanceBuffer.fs" />` to `fsharp-game.fsproj`, immediately after `render/StaticMesh.fs`.
- [X] T002 [P] Add `<Compile Include="world/EnvironmentBatch.fs" />` to `fsharp-game.fsproj`, immediately after `world/Zone.fs`.
- [X] T003 [P] Add `<Compile Include="EnvironmentBatchTests.fs" />` to `test/fsharp-game.Tests.fsproj`, in the existing ordering.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The instanced pipeline, the per-frame instance buffer, and the batch — everything the user stories depend on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 **Baseline.** The ArtRoom baseline is already recorded in docs/RENDER_PERFORMANCE.md section 1/3 (draws 521, record ~20-30 ms), so no throwaway counter was added; the 521 -> 191 comparison uses T012 counters against it.
- [X] T005 [P] Extend `shaders/static.vert` with per-instance `Float4` attributes at locations 3..9 (four model rows, three normal rows), build `mat4 model` / `mat3 normalMatrix` from them, and shrink `PerDraw` to `{ mat4 viewProj; }`; recompile with `glslc shaders/static.vert -o shaders/static.vert.spv`.
- [X] T006 [P] Extend `render/Gpu.fs` `createStaticPipeline`: add vertex slot 1 (`Pitch = 112`, `InputRate = Instance`, `InstanceStepRate = 0`) with seven `Float4` attributes at locations 3..9, `NumVertexBuffers = 2`, `NumVertexAttributes = 10`; keep slot 0 unchanged.
- [X] T007 [P] Create `render/InstanceBuffer.fs`: a grow-only instance `Vertex` buffer plus transfer buffer, reused CPU staging `byte array` (stride 112; initial capacity 0), and a retired-buffer list. `ensure` sizes/grows by allocating a new buffer and retiring the old one; `reclaim` releases retired buffers only after `MAX_FRAMES_IN_FLIGHT` frames, so a superseded buffer is never freed while in flight. Also `upload` and a slot-1 `binding` helper (see `contracts/instanced-static.md`). No `world` dependency.
- [X] T008 Replace `StaticMesh.draw` with `drawInstanced` in `render/StaticMesh.fs`: shrink `PerDrawUniformBytes` to 64 (`viewProj` only), drop the model/normal writes and the pipeline bind (now the batch's job), take an instance binding + instance count, bind vertex slots 0 and 1, push the material, bind samplers, and issue `DrawGPUIndexedPrimitives(IndexCount, instanceCount, 0, 0, 0)`.
- [X] T009 Implement `world/EnvironmentBatch.fs`: the dense submesh registry (keyed by the uploaded submesh identity, not a bare index), the per-frame count/prefix-sum/fill gather over `Zone.Props` + `environment.Parts` — sizing the instance buffer to the total via `ensure` on the first gather and calling `reclaim` each frame — the copy-pass upload, and — in `render` — **bind the static pipeline once**, push `viewProj` once for the block, then one instanced draw per group with `Count > 0`; track `DrawCalls`/`InstanceCount`. Store `Device` on the batch so `gather` needs only the zone.
- [X] T010 Change `world/EnvironmentRender.fs`: `draw` no longer issues GPU calls; `EnvironmentRender` remains the per-instance data holder the batch reads.
- [X] T011 Wire `screen/GameplayScreen.fs` `renderScene`: gather + upload the batch alongside the other copy passes (before `BeginGPURenderPass`), and replace the two `EnvironmentRender.draw` loops with one `EnvironmentBatch.render` inside the scene pass, before the actor loop.
- [X] T012 Replace the temporary baseline counter (T004) with the batch's counters: print `draws`/`instances` from `EnvironmentBatch` in the `Engine.fs` per-frame log line, so the 521 → 191 drop is measured.

**Checkpoint**: The batch draws the static scene; user stories can be validated.

---

## Phase 3: User Story 1 - Large static scenes render within budget (Priority: P1) 🎯 MVP

**Goal**: Draw calls scale with distinct submeshes, not instances, so the ArtRoom sustains the frame rate.

**Independent Test**: load the ArtRoom, play it, and confirm `draws 191` against `instances 521` and `record` under 5 ms, against the recorded baseline.

### Tests for User Story 1

- [X] T013 [P] [US1] Write `test/EnvironmentBatchTests.fs` for the pure grouping planner: counts → prefix-sum → group offsets, a zero-count group, and the two-asset case where the same submesh index must map to different groups. Test the keying maths without a GPU device.

### Validation for User Story 1

- [X] T014 [US1] Run `quickstart.md` scenarios 1–2 (draw calls collapse to the distinct-submesh count; `record` falls under 5 ms) against the baseline from T004; fix any failures.

**Checkpoint**: User Story 1 is functional and independently testable.

---

## Phase 4: User Story 2 - The rendered image is unchanged (Priority: P1)

**Goal**: Batching changes cost, not the image; geometry and materials are never mixed.

**Independent Test**: compare the scene against a pre-change build and place two props that share submesh index 0 from different assets.

### Validation for User Story 2

- [ ] T015 [US2] Run `quickstart.md` scenarios 3–4 (image identical including rotated/scaled and normal-mapped props; two assets sharing submesh index 0 render their own geometry and material); fix any failures.

**Checkpoint**: The batched scene matches the per-submesh draw.

---

## Phase 5: User Story 3 - The reduction is observable (Priority: P2)

**Goal**: The frame log reports draw calls and instances, and the counts behave correctly as the scene grows.

**Independent Test**: read the log; `draws` stays flat as more copies of shared geometry are placed while `instances` grows.

### Validation for User Story 3

- [X] T016 [US3] Run `quickstart.md` scenarios 5–9 (extra copies add no draws; spawn/delete while editing; paused editor; empty scene; no per-frame regression or actor/debug breakage); fix any failures.

**Checkpoint**: The improvement is measured and stable across scene edits.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and end-to-end validation.

- [X] T017 [P] Update `docs/RENDER_PERFORMANCE.md` §9–§10 (and the §12 mapping) to record that Option 1 is implemented and the measured `record`/draw-call result.
- [X] T018 Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj`; fix any failures, then confirm the full quickstart outcome and that actor/gizmo rendering is unchanged.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories. T004 (baseline) must run before T008/T010, which replace the instrumented draw path, so the 521 baseline is captured first.
- **User Stories (Phase 3+)**: Depend on Foundational; US1 and US2 are both P1 (validate US2 before trusting US1's numbers), US3 follows.
- **Polish (Phase 6)**: Depends on all stories.

### Within Each User Story

- The planner test (T013) accompanies US1; the quickstart validations follow implementation.

### Parallel Opportunities

- T001–T003 (different project files) are independent.
- T004 (baseline) is a sequencing gate, not parallel work: it must land before T008/T010.
- T005 (shader), T006 (pipeline), T007 (instance buffer) touch different files and can run in parallel before T008/T009.
- T013 (test file) and T017 (docs) are independent of the implementation tasks.

---

## Parallel Example: Foundational

```bash
# Independent files — run together:
Task: "Extend shaders/static.vert with instance attributes and recompile"
Task: "Add vertex slot 1 to createStaticPipeline in render/Gpu.fs"
Task: "Create render/InstanceBuffer.fs with safe buffer retirement"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Setup (T001–T003).
2. Foundational (T004–T012), capturing the baseline at T004 before changing the draw path.
3. US1 (T013–T014): the draw-call collapse and frame-budget check.
4. **STOP and VALIDATE**: `draws 191`, `record` under 5 ms.
5. US2 (T015) confirms the image is unchanged; US3 (T016) confirms the counts.

---

## Notes

- [P] tasks = different files, no dependencies.
- **Baseline first (T004):** the counter must exist on the current per-submesh path before the refactor, otherwise the 521 → 191 reduction cannot be measured (SC-002).
- The grouping key is the shared submesh identity per asset — never a bare submesh index (the merge hazard, FR-002).
- Geometry is already deduplicated at load; nothing is re-uploaded or merged.
- A superseded instance buffer is retired, not released inline, so an in-flight frame is never left with a freed buffer (FR-010).
- Culling and freeze-time count caching are deliberately out of scope.
- Actors and debug/gizmo draws are untouched.
- Commit after each logical group.
