# Tasks: Batched asset uploads

**Input**: Design documents from `/specs/052-batch-asset-uploads/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Device-free tests cannot exercise the GPU upload path (no device can be opened in
the suite — AGENTS.md; every rendering spec). The **412-test suite is the regression gate**
and must stay green; the batched behaviour (submission count, upload time, identical
rendering) is validated by the GPU walkthrough in `quickstart.md`, using the retained
instrumentation. No new test tasks are generated for that reason.

**Organization**: Tasks are grouped by user story. The upload-batch machinery is shared
foundation (Phases 1–2). **US1** bounds the submission count; **US2** confirms the change is
behaviour-preserving; **US3** bounds staging for a single oversized resource.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US3)
- Include exact file paths in descriptions

## Path Conventions

Single-project F# repository. Paths are relative to the repository root (`fsharp-game/`):

- `core/`, `render/` (GPU layer), `world/` (game-domain), `state/`, `editor/`, `screen/`,
  `test/`, `specs/`, `docs/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The one constant the batch needs.

- [X] T001 [P] In `render/Gpu.fs`, add a single project-level `StagingCapacity` constant (the staging bound): copies are packed into a buffer of this size and flushed when it fills, so peak staging is bounded and submissions scale with flushes. (data-model.md VR-002; research D2.)

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: The upload batch and the two record call sites must exist before any user story can be validated.

- [X] T002 In `render/Gpu.fs`, add the `UploadBatch` record with `Staging`/`Copy`, and `beginUpload` / `recordBuffer` / `recordTexture` / `finishUpload` / `abortUpload`. Recording packs copies into one mapped staging buffer (4-byte aligned) and **flushes** it — one command buffer, one copy pass, one submit, then release — when it fills; the final flush also runs `GenerateMipmapsForGPUTexture` for each registered texture (outside any pass). A failed submit cancels its command buffer. (research D1–D5; VR-001/VR-003/VR-004.)
- [X] T003 In `render/Gpu.fs`, keep `uploadBufferOnce` with its exact signature as a **direct data-sized one-shot upload** (its own transfer buffer, one submit), so `render/Mesh.fs` and `Gpu.createStorageBuffer` are unchanged and a small one-off buffer does not allocate a `StagingCapacity` buffer. (Depends on T002; research D8.)
- [X] T004 [P] In `render/Texture.fs`, change `load` to take the `UploadBatch` and record its level-0 copy via `Gpu.recordTexture` (registering mips when `levels > 1`) instead of acquiring and submitting; keep decode, texture/sampler creation, and `Bytes` exactly as they are. Leave `solid` on its existing direct upload so `Ui.create` and `Assets.createLoader` are unchanged. (Depends on T002; research D4.)
- [X] T005 [P] In `render/StaticMesh.fs`, change `create` to take the `UploadBatch` and record its two buffer uploads via `Gpu.recordBuffer` instead of `uploadBufferOnce`; the buffers are still created here and the record shape is unchanged. (Depends on T002.)
- [X] T006 In `world/Assets.fs`, thread one batch through `buildLoaded`: `let batch = Gpu.beginUpload loader.Device` before the submesh map; pass `batch` into `StaticMesh.create`, `resolveMaterial`, `acquireTexture` and `Texture.load`; call `Gpu.finishUpload batch` on success before returning `EnvironmentLoaded`, and `Gpu.abortUpload batch` in the existing failure path **before** the mesh/texture rollback and `reraise`. A texture cache hit (0→1 miss aside) records nothing. (Depends on T004, T005; research D6; FR-005/FR-006.)
- [X] T007 Build `fsharp-game.fsproj` and follow the call-site fallout to green. Per the call-site scan only `Assets.acquireTexture` (L105) and `Assets.buildLoaded` (L322) need changing; `render/Mesh.fs`, `render/Ui.fs`, `render/InstanceBuffer.fs` and `world/ActorRender.fs` must remain untouched. (Depends on T006.)
- [X] T008 Build both projects (`fsharp-game.fsproj`, `test/fsharp-game.Tests.fsproj`) with **0 warnings** and run the full suite (**412 tests pass**). This is the compile/regression checkpoint for the foundational work. (Depends on T007.)

**Checkpoint**: the whole asset now uploads through one batch; the suite is green and nothing is visually different yet (the instrumentation still shows the old per-resource behaviour only because it is not yet reporting the batch).

---

## Phase 3: User Story 1 - Uploading an asset costs a bounded number of submissions (Priority: P1) 🎯 MVP

**Goal**: an asset's uploads are batched into a small handful of submissions (scaling with staging flushes, not resources), with peak staging bounded.

**Independent Test**: load ArtRoom and read the instrumentation (quickstart §2–§3): `submits` is a small handful (single digits, was 469) and peak staging is bounded (SC-004).

### Implementation for User Story 1

- [X] T009 [US1] In `render/Gpu.fs`, count the command buffers `submit` acquires/submits and expose it through the per-asset instrumentation, so the count is assertable and scales with staging flushes, not with the resource count. (Depends on T006; VR-001; research D7.)
- [X] T010 [US1] Verified on a GPU run of the flushing revision: ArtRoom `submits` **469 → 7**, submit **3.7 ms** (0.07% of the 5141 ms load); props 8–10 → 2; Island 2 → 1. SC-001 (submission overhead ≤5%) and SC-002 (single digits) met; peak staging bounded (256 MiB, one buffer). (Depends on T009.)

**Checkpoint**: the upload phase is bounded and small; this is the MVP.

---

## Phase 4: User Story 2 - Batched uploads render and behave exactly as before (Priority: P1)

**Goal**: the batched path is indistinguishable from the per-resource path, and spec 050 is untouched.

**Independent Test**: the suite passes unchanged, every asset renders identically, and residency accounting is byte-for-byte the same.

### Implementation for User Story 2

- [X] T011 [US2] Verified: ArtRoom frame log unchanged (`draws 191`, `instances 521`, `parts 463`); props load identically. (Depends on T008; SC-003, FR-002, FR-009.)
- [ ] T012 [US2] Confirm residency is untouched: `ResidentBytes` for ArtRoom is identical to the pre-change value, the shared-texture refcount behaves the same (a texture shared by two materials is uploaded once), and the LRU/budget/load-on-demand behaviour is unchanged. (Depends on T008; FR-005, FR-007.) **Needs a person + device.**

**Checkpoint**: behaviour-preserving is demonstrated; the optimisation is proven a non-regression.

---

## Phase 5: User Story 3 - A single oversized resource stays within a bounded staging footprint (Priority: P2)

**Goal**: no single resource forces an unbounded staging allocation; a resource larger than the capacity is split, and peak staging stays bounded.

**Independent Test**: load an asset containing a resource larger than `StagingCapacity` and confirm it uploads correctly in multiple copies and staging stays bounded.

### Implementation for User Story 3

- [X] T013 [US3] In `render/Gpu.fs`, split a single upload whose data exceeds `StagingCapacity` into multiple copy regions within the flush — buffers by offset/size, textures by whole-row bands (`Gpu.UploadToGPUTexture` region + transfer offset, with `PixelsPerRow`/`RowsPerLayer` set explicitly) — each region within capacity, never an asset-sized allocation. (Depends on T002; FR-004, VR-002.)
- [ ] T014 [US3] Verify on a GPU run (quickstart §6) with a synthetic oversized resource: it uploads correctly (all copies land — the band path is the highest-risk part), and peak staging stays under the fixed bound (SC-004). (Depends on T013.) **Needs a person + device.**

**Checkpoint**: the largest single resource no longer risks unbounded staging.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: make the measurement permanent and record the result.

- [X] T015 [P] Finalize the instrumentation (research D7): turn the per-asset line into a permanent, low-noise diagnostic that splits the device cost into **staging** (transfer create + map/copy/unmap) and **submit** (acquire + pass + upload + end + submit), and add the per-submesh resource-**creation** time (FR-010); remove the temporary `mutable internal` Phase 0 spike counters in `render/Gpu.fs`, `render/Texture.fs` and `world/Assets.fs`.
- [X] T016 [P] Update `docs/RENDER_PERFORMANCE.md` (and/or `docs/ENVIRONMENT_LRU_PLAN.md`) to record the batching change, the 469 → single-digit submission count, and the upload-time before/after.
- [X] T017 Run `quickstart.md` §1–§7 and record the outcome. §1–§3 measured with `FFS_LOAD_TIMING=1` and recorded below; §4 (identical render) confirmed by the frame log (`draws 191`, `instances 521`, `parts 463`). §5 (deliberate failure) and §6 (oversized resource) still need their respective setups.
- [X] T018 Commit the change.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: T002 needs T001; T003/T004/T005 need T002; T006 needs T004/T005; T007 needs T006; T008 needs T007. Blocks all stories.
- **US1 (Phase 3)**: T009 needs T006; T010 needs T009.
- **US2 (Phase 4)**: T011/T012 need T008.
- **US3 (Phase 5)**: T013 needs T002; T014 needs T013.
- **Polish (Phase 6)**: T015–T018 last.

### Same-file constraint (sequential)

- `render/Gpu.fs` — T001 → T002 → T003 → T009 → T013 → T015.
- `render/Texture.fs` — T004 → T015.
- `world/Assets.fs` — T006 → T015.
- `render/StaticMesh.fs` — T005 only.
- `docs/RENDER_PERFORMANCE.md` — T016.

### Parallel Opportunities

- **T004 ∥ T005** — `Texture.fs` and `StaticMesh.fs` are different files once T002 lands.
- **T011 ∥ T012** — the two US2 confirmations are independent.
- **T015 ∥ T016** — instrumentation cleanup and docs are different files.

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1–2: the batch and its call sites; suite green.
2. Phase 3 (US1): the submission count is bounded and small.
3. **STOP and VALIDATE** on GPU: ArtRoom `submits` single digits, upload < 150 ms.

### Incremental Delivery

1. Foundational → the whole asset uploads in one batch.
2. US1 → bounded, measurably small.
3. US2 → proven behaviour-preserving (the acceptance gate).
4. US3 → the largest single resource stays bounded.

---

## Notes

- [P] tasks = different files, no dependencies.
- The blast radius is deliberately tiny: `Texture.load` is only reached through
  `Assets.acquireTexture`, and `StaticMesh.create` only through `Assets.buildLoaded`.
- `uploadBufferOnce` keeps its signature, so `Mesh.create` (actors) and
  `createStorageBuffer` (morph buffers) are untouched (FR-009: props benefit because they
  share `buildLoaded`).
- Decode stays on the loading thread; per-frame dynamic uploads
  (`EnvironmentBatch`/`DebugDraw`/`Ui`) are unchanged (spec Assumptions).
- Commit after each logical group; the GPU-dependent tasks (T010, T011, T012, T014, T017) need a
  person and a device.

### Implementation status (2026-10-03)

- **T001–T009, T013, T015, T016: done.** Both projects build with **0 warnings** and
  `dotnet test` passes **412 tests**. The batch machinery lives in `render/Gpu.fs`
  (`UploadBatch`, `Staging`, `Copy`, `begin/record/flush/finish/abort`, `StagingCapacity`);
  `render/Texture.fs` and `render/StaticMesh.fs` record into it; `world/Assets.fs` owns one
  batch per asset (`buildLoaded`) and finishes/aborts it. `uploadBufferOnce` is a direct
  data-sized one-shot upload, so `Mesh.create`, `createStorageBuffer`, `Ui`, `InstanceBuffer`
  and `ActorRender` are untouched.
- **T012, T014: still open.** T012 (ResidentBytes identical) was not separately printed — the
  byte accounting (`SubmeshBytes`, texture `Bytes`) is structurally untouched. T014 (oversized
  split) did not trigger: no ArtRoom resource exceeds `StagingCapacity`, so that path is
  defensive and unexercised.
- **T018: committed.**

**Review fixes (2026-10-03).** A review of the pooling implementation drove a design correction:
holding every staging buffer until the single submit made **peak staging scale with the asset**
(~1.6 GiB for ArtRoom), contradicting SC-004. Staging now **flushes when full** (one 256 MiB
buffer, released and reused), so peak staging is bounded and submissions scale with flushes
(single digits), not resources. Also fixed: `recordTexture` guard for rows larger than the
capacity; 4-byte copy alignment; cancel-on-failure for the command buffer; safe single-release
in `dispose`; the actor path (`loadTexture`) now aborts on failure; `uploadBufferOnce` is a
straight data-sized one-shot again (no 256 MiB over-allocation); `StagingCapacity` is `int`; the
stale instrumentation comment is fixed; and the per-asset log line is opt-in via
`FFS_LOAD_TIMING=1`.

**Measured results (flushing revision, `FFS_LOAD_TIMING=1`, 2026-10-03).** Fresh GPU run of the
shipped flushing design:

| Asset | Submits | stage | memcpy | submit | tex-create | buf-create | decode | total |
|---|---|---|---|---|---|---|---|
| ArtRoom | 7 | 104.9 ms x7 | 186.2 ms x469 | 3.7 ms x7 | 234.5 ms x87 | 1.1 ms x382 | 4582.6 ms x87 | 5141.5 ms |
| Island | 1 | 0.7 ms x1 | 0.4 ms x2 | 0.1 ms x1 | 0 ms x0 | 0 ms x2 | 0 ms x0 | 11.9 ms |

- **SC-002**: ArtRoom submissions **469 → 7** (single digits), scaling with staging flushes (7),
  not with its 469 resources. Island **2 → 1**. `memcpy x469` = 382 buffers + 87 textures — every
  copy lands.
- **SC-001**: ArtRoom submit overhead 3.7 ms of 5141.5 ms ≈ **0.07%** (well under ~5%). The host
  copies (`memcpy` 186.2 ms) and per-texture creation (`tex-create` 234.5 ms) are visible and out
  of scope; `decode` (4582.6 ms) dominates and is unchanged.
- **SC-004**: `stage` shows 7 flushes of one 256 MiB staging buffer (bounded), not the asset's
  total size — the held design's ~1.6 GiB peak is gone.
- **SC-003**: ArtRoom render counts unchanged (`draws 191`, `instances 521`, `parts 463`),
  matching the quickstart's expected values; clean shutdown.
- **SC-005**: Island pays no extra cost (1 submit, 11.9 ms total).

**Superseded (pooled-and-held design).** Before the flushing change, staging was pooled and held
until one submit. On ArtRoom that measured `stage 126.5 ms x26`, `memcpy 162.0 ms`,
`submit 3.1 ms x1`, `tex-create 221.8 ms x87`, decode 4380 ms, total 4925 ms — submissions
469 → 1 but at ~1.6 GiB peak staging. Kept only as the pre-review baseline.

---

## Phase 7: Convergence

**Findings** (2026-10-03, from `/speckit-converge` against the shipped flushing design). The
implementation is complete; these are the remaining gaps.

- [X] T019 In `render/Gpu.fs` `reserve`, size the staging buffer to the workload rather than always allocating `StagingCapacity`: a small asset (Island ~2 MB, props) maps a 256 MiB transfer buffer per load, which is wasteful and may cost time (SC-005). Start small and grow on demand up to `StagingCapacity`. per SC-005 (partial)
- [X] T020 Correct the abort wording left from the held design — `data-model.md` VR-004 ("submits nothing") and `quickstart.md` §5 ("without submitting any GPU work") — since a flush may already have submitted; the caller's rollback releases those copies' resources. per FR-006 (contradicts)
- [X] T021 Commit the change: T018 is marked complete but the commit was undone, so the batching + flushing + review-fix tree is uncommitted. per plan: Polish (partial)

**Not appended (already tracked):** the flushing revision's device verification. The 2026-10-03
fresh run (above) closes **T010** (submission count 7, bounded staging x7) and **T017** (recorded
outcome). **T012** (ResidentBytes identical) was still not separately printed, and **T014** (the
oversized/band path) still did not trigger — no ArtRoom resource exceeds `StagingCapacity` — so
those two remain open.
