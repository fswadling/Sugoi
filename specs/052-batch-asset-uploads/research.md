# Phase 0 Research: Batched asset uploads

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

## Context

The measured cost of loading an environment is dominated by per-**submission** overhead.
ArtRoom: 469 submissions — 382 geometry-buffer and 87 texture — costing ~1.2 s of a 5.42 s
load, while its geometry is only ~1.84 MB. Each submission today is an
`SDL_AcquireGPUCommandBuffer` → `BeginGPUCopyPass` → upload → `EndGPUCopyPass` → `Submit`
cycle, so the cost is the round trip, not the bytes. This phase settles how to collapse
those into one.

## Decision 1 — The batch unit is the asset, submitted in bounded flushes

- **Decision**: One `UploadBatch` per asset (an environment or a prop) — the unit the loader
  builds and the spec bounds. Recording packs copies into a staging buffer and flushes it (one
  command buffer, one copy pass, one submit) when it fills; the number of flushes is the
  submission count.
- **Rationale**: FR-001 requires the submission count to be bounded and independent of the
  resource count. Flushing keeps it a small handful (single digits) however many buffers and
  textures the asset has, while bounding peak staging (D2).
- **Alternatives considered**: **per-frame batching** (defer all asset uploads to a frame
  boundary) — couples an asset load to the frame loop and changes *when* resources become
  resident; belongs to a later async phase, not here. **One long-lived command buffer for
  the whole process** — the same submission count, but couples all loads through one
  recording buffer and its pass state, for no benefit.

## Decision 2 — Staging is one bounded buffer, flushed when full

- **Decision**: Copies are **packed** into one staging transfer buffer and **flushed** — one
  submit — when it fills, releasing it and starting a fresh one. Peak staging memory is therefore
  one buffer. The first buffer is sized to the workload (up to `InitialStaging`, 4 MiB) so a small
  asset stays small (SC-005); after a flush later buffers use `StagingCapacity` (256 MiB), keeping
  the flush count single digits (FR-001). A resource larger than the capacity gets a buffer of its
  own (a row band is the smallest splittable unit).
- **Rationale**: SC-004 requires peak staging to be bounded, but a transfer buffer cannot be
  overwritten while a recorded copy still references it, so the whole asset cannot share one
  bounded buffer in a single submission. Flushing trades a handful of submissions (single
  digits for the reference assets) for bounded memory, which is the project's priority. The
  measurements drove this: the per-copy design left 469 live buffers (~1.6 GiB), and pooling
  held until one final submit left ~26 (~1.6 GiB); flushing bounds it to one buffer.
- **Alternatives considered**: **one transfer buffer per copy** (first implementation) — 469
  create/map/unmap cycles. **pooled and held until one final submit** — bounded per buffer,
  but peak staging scaled with the asset (~1.6 GiB, contradicting SC-004). **one staging
  buffer sized to the asset** — unbounded, rejected. Flushing is the only option that bounds
  peak staging while keeping the submission count low.

## Decision 3 — Mip generation is recorded after the copy pass, in the same command buffer

- **Decision**: For each texture with more than one level, call
  `SDL_GenerateMipmapsForGPUTexture` on the batch's command buffer **after** the copy pass
  ends and **before** the single submit.
- **Rationale**: SDL_gpu requires mip generation "must not be called inside of any pass"
  (`SDL_gpu.h` doc for `SDL_GenerateMipmapsForGPUTexture`); the level-0 copy must be inside
  a copy pass, so the two cannot share one open pass. Both are recorded into the flush's command
  buffer, so mips add no extra submission.
- **Alternatives considered**: a second pass per texture — defeats batching; leaving mips to
  a separate submit — no.

## Decision 4 — Texture creation stays in `Texture.load`; the upload is recorded

- **Decision**: `Texture.load` keeps decoding and creating the GPU texture + sampler, but
  instead of acquiring and submitting, it records its level-0 copy into the batch and
  registers the texture for mip generation.
- **Rationale**: the batch needs the texture handle at record time, and creation is not a
  submission (it must not be deferred). Keeping decode/creation in `Texture.load` means the
  later CPU/GPU split (a different phase) is not pulled forward here.
- **Alternatives considered**: fully splitting decode/create/upload now — that is the Phase 3
  async work, out of scope per the spec's assumptions.

## Decision 5 — A command buffer is acquired per flush, and no pass is held across recording

- **Decision**: `beginUpload` allocates only host state. During recording, copies are packed into
  a staging buffer and, when it fills, a **flush** acquires a command buffer, opens one copy pass,
  issues the recorded copies, submits, and releases the staging; the final flush (`finishUpload`)
  additionally generates mips. A command buffer's lifetime is one flush, so no pass is held across
  the slow CPU work (texture decode, ~48 ms each) that happens between records.
- **Rationale**: it keeps no GPU pass open across decode, and a flush releases its staging so peak
  staging stays bounded (D2). `abortUpload` releases the current staging and drops the records,
  with no additional submit.
- **Caveat**: because a flush submits, an asset large enough to flush before finishing has
  **already submitted** copies for some resources when a later failure aborts. There is no leak —
  the caller's rollback releases the GPU resources those copies targeted — but an aborted load is
  not "no GPU work submitted".
- **Alternatives considered**: acquire one command buffer in `beginUpload` and hold the pass open
  across everything — one submit, but an open pass across tens of milliseconds of decode and
  unbounded staging (D2, rejected).

## Decision 6 — Failure: finish on success, abort on failure, then the existing rollback

- **Decision**: `buildLoaded` runs its build inside the existing `try/with`; the success path
  calls `finishUpload`, the failure path calls `abortUpload` (release the current staging, drop
  the records) **before** the current mesh/texture rollback and `reraise`. A submit that throws
  cancels its own command buffer (D5).
- **Rationale**: FR-006 requires no leaked GPU resources and the same rollback as today. An asset
  small enough to fit one staging buffer submits nothing before success; a larger asset may have
  flushed (submitted) earlier copies, and the caller's rollback releases exactly those resources,
  so nothing leaks either way.
- **Alternatives considered**: `SDL_CancelGPUCommandBuffer` on a buffer held from `beginUpload`
  — rejected with D5.

## Decision 7 — Keep and sharpen the instrumentation

- **Decision**: Keep the per-asset instrumentation line and split the device cost into
  **staging** (transfer-buffer create + map/copy/unmap) and **submit** (acquire + pass +
  upload + end + submit), and add the per-submesh **resource-creation** timing (FR-010).
  One line per asset build, like `Engine`'s per-second phase log.
- **Rationale**: FR-008 requires the measurement to remain; FR-010 requires the creation cost
  to be known so the "combine geometry?" question (spec Q2) can be answered from data. The
  staging/submit split attributes the win to batching and flags if map/unmap (D2) dominates.
- **Alternatives considered**: instrument only in a throwaway spike — the spec requires the
  measurement to remain available. The Phase-0 temporary `mutable internal` counters have been
  removed; this line (opt-in via `FFS_LOAD_TIMING=1`) is what remains.

## Decision 8 — Scope boundaries

- **Decision**: Only asset-load uploads are batched. Texture **decode** stays on the loading
  thread; the **per-frame** dynamic uploads (`EnvironmentBatch.upload`, `DebugDraw.upload`,
  `Ui.upload`) are unchanged; the residency machinery (`Loaded`, `ResidentBytes`, the LRU,
  the budget) is untouched; `uploadBufferOnce` remains for one-shot callers (actor storage
  buffers, the white placeholder) as a direct data-sized upload, so their call sites do not
  change.
- **Rationale**: spec Assumptions — async/frame-budgeted/cross-asset batching are later
  phases; spec-050 FR-007 requires residency to be unaltered.
- **Alternatives considered**: folding the per-frame uploads into the same batching — their
  ordering is per frame and already uses one command buffer per frame; nothing to gain here.
