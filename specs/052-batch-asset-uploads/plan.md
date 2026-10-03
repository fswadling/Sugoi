# Implementation Plan: Batched asset uploads

**Branch**: `052-batch-asset-uploads` | **Date**: 2026-10-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/052-batch-asset-uploads/spec.md`

## Summary

Loading an environment or prop uploads every vertex/index buffer and every texture with
its **own** command buffer and submit: `Gpu.uploadBufferOnce` (twice per submesh) and
`Texture.load` (once per texture) each acquire a command buffer, open a copy pass, upload,
and submit. Measured on ArtRoom this is **469 submissions costing ~1.2 s of a 5.4 s load**
— dominated by per-submission overhead, not data (ArtRoom's geometry is ~1.8 MB yet its
382 buffer uploads take ~0.67 s).

This feature introduces an **upload batch**: an asset's buffer and texture uploads are packed
into a staging transfer buffer and flushed into their own command buffer(s), so the per-resource
submission overhead disappears. Peak staging memory is bounded by a fixed capacity (copies are
flushed when it fills), and the submission count is a small handful — scaling with staging
flushes, not with the asset's resource count. Behaviour is unchanged: same GPU resources, same
mip chains, same residency accounting.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: **none new.** The batch acquires a command buffer per flush over the
existing `Gpu` wrappers. A failed submit uses `SDL_CancelGPUCommandBuffer`, which exists in the
pinned SDL3 (cancels; executes nothing; must run on the acquiring thread).

**Storage**: none. Nothing is persisted.

**Testing**: behaviour-preserving, so the existing suite (**412 tests**) is the regression
gate. The submission count and upload time are read from the per-asset instrumentation on a
GPU run — tests cannot open a device (AGENTS.md; every rendering spec). The instrumentation
is retained and extended (FR-008, FR-010).

**Target Platform**: Linux x64 desktop, SDL3 + SDL_gpu (Vulkan backend).

**Project Type**: single-project native desktop game.

**Performance Goals**: the per-submission overhead of the largest environment is removed —
469 submissions → a small handful (single digits), submit time ~820 ms → a few ms — while peak
staging memory stays bounded (one `StagingCapacity`-sized buffer). The path is off the per-frame
hot path, so 60 FPS elsewhere is unchanged.

**Constraints**: submissions scale with staging flushes, not with resource count (FR-001); peak
staging bounded by `StagingCapacity` (FR-004); identical output and mip chains (FR-002/003);
spec-050 behaviour untouched (FR-007); failure rollback preserved (FR-006); instrumentation
retained and creation cost measured (FR-008/010).

**Scale/Scope**: `render/Gpu.fs` (the batch type and its operations), `render/Texture.fs`
(record uploads instead of submitting), `render/StaticMesh.fs` (record uploads),
`world/Assets.fs` (own one batch per asset build; finish on success, abort on failure). No
new module, shader, pipeline, pass, or dependency. The draw path and residency machinery
are untouched.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one small batch value over wrappers the code already has; no library, no new module, no new pass. |
| II. Fail Loudly | Pass — a failed upload aborts and rolls back exactly as today; the batch is finished or aborted in one place, and a partial upload never reaches the GPU. |
| III. Spec-First | Pass — `specs/052-batch-asset-uploads/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — a record holding a device handle, one staging buffer and a small copy list; explicit record/flush ordering; no object graph. |
| V. Minimise GC Pressure | Pass — off the hot path entirely (asset loads only); the batch reuses its arrays. |
| N-V. Testing is the Authoring Tool | Not engaged — narrative subsystem, untouched. |

## Complexity Tracking

No violations. There is nothing to justify: the batch is a thin grouping over the existing
SDL_gpu calls, and the failure path is the same rollback the loader already performs.

## Project Structure

### Documentation (this feature)

```text
specs/052-batch-asset-uploads/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── batched-upload.md  # Phase 1 output — the surfaces that change
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
render/
├── Gpu.fs          # CHANGED: `UploadBatch` + `Staging` + `Copy`; `beginUpload` /
│                   #   `recordBuffer` / `recordTexture` / `finishUpload` / `abortUpload`,
│                   #   packing into one staging buffer and flushing when full. `uploadBufferOnce`
│                   #   is a direct data-sized one-shot upload, so existing callers are unchanged.
├── Texture.fs      # CHANGED: `load` creates the texture and records its level-0 copy into
│                   #   a batch instead of acquiring/submitting itself; mip generation is
│                   #   deferred to the batch. Decode and creation are otherwise unchanged;
│                   #   `solid` uses its existing direct upload.
└── StaticMesh.fs   # CHANGED: `create` records its two buffer uploads into a batch.

world/
└── Assets.fs       # CHANGED: `buildLoaded` begins one batch, threads it through submesh and
                    #   material building, and finishes it on success / aborts it on failure;
                    #   the per-load instrumentation is extended (FR-008, FR-010).

test/               # unchanged — device-free; the suite is the regression gate.
```

**Structure Decision**: the batch belongs with the SDL_gpu wrappers it groups
(`render/Gpu.fs`), beside `uploadBufferOnce` it generalises. The two record call sites
(`Texture`, `StaticMesh`) are the ones that submit today. The one place that knows an asset
is a single unit — and therefore the natural owner of the batch — is
`buildLoaded` in `world/Assets.fs`. No new module is warranted.

## Phase 0: Research

See [research.md](./research.md). It settles the batch unit (D1), the bounded-staging flush rule
(D2), mip generation position (D3), the texture create/record split (D4), the per-flush command
buffer with no pass held across decode (D5), the failure/abort path (D6), the instrumentation
change (D7), and scope (D8).

## Phase 1: Design

- [data-model.md](./data-model.md) — the `UploadBatch` / `Staging` / `Copy` shapes, the
  record/flush/finish/abort lifecycle and its validation rules.
- [contracts/batched-upload.md](./contracts/batched-upload.md) — the surfaces that change
  (`Gpu`, `Texture`, `StaticMesh`, `Assets`) and what explicitly does not (residency, the
  LRU and budget, the draw path, per-frame uploads, shaders/pipelines).
- [quickstart.md](./quickstart.md) — headless validation and the GPU walkthrough that
  records the before/after submission count and upload time.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one batch type, five small operations; no new module or dependency. |
| II. Fail Loudly | Pass — finish/abort in one place; failed uploads roll back as today. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — a record of a device handle plus a pending list. |
| V. Minimise GC Pressure | Pass — asset-load path only; no per-frame work. |
| N-V. Testing is the Authoring Tool | Not engaged. |
