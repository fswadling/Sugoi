# Feature Specification: Batched asset uploads

**Feature Branch**: `052-batch-asset-uploads`

**Created**: 2026-10-03

**Status**: Implemented (flushing revision pending GPU re-verification)

**Input**: User description: "Loading an environment currently submits one GPU upload per vertex/index buffer and per texture. Measured on ArtRoom, the cost is dominated by per-submission overhead, not data volume: ~0.67 s across 382 geometry-buffer submissions and ~0.55 s across 87 texture submissions — ~1.2 s of a 5.4 s load (about 22%). We want an asset's GPU resources to be uploaded in a bounded number of submissions that does not grow with its resource count, so the upload portion of an asset load becomes a small fraction, and so later work that spreads uploads across frames is cheaper to land. This is behaviour-preserving: assets must render and behave exactly as before."

## Clarifications

### Session 2026-10-03

- Q: When bounding GPU staging memory during an asset's upload, should the bound apply to a single resource copy or to the whole asset? → A: Per resource copy — one submission can carry a whole asset; only an individual buffer/texture larger than the bound is split into multiple copies.
- Q: Should this feature only collapse the upload submissions, or also remove the per-submesh resource-creation overhead by combining an asset's geometry into fewer buffers? → A: Submissions only for now, plus a measurement of the per-submesh creation cost; combining geometry is revisited only if SC-001 is not met by batching alone.
- Q: (revises the staging answer above) Must one submission carry a whole asset, or may peak staging memory be bounded? → A: Bound peak staging — copies are packed into one capacity-sized staging buffer and flushed when full, so a large asset takes a few submissions rather than holding its whole upload volume in memory.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Uploading an asset costs a bounded number of submissions (Priority: P1)

A developer loads an environment or prop. The render layer brings all of that asset's
geometry buffers and textures onto the GPU in a **small, fixed number of submissions**,
no matter how many buffers and textures the asset contains. Today the count grows with
the asset (ArtRoom: 469 submissions); after this change it does not.

**Why this priority**: this is the whole feature. The measured upload cost is
submission overhead, not data volume (ArtRoom's geometry is ~1.8 MB yet its 382 buffer
uploads take ~0.67 s). Bounding the submission count turns a large, resource-count-driven
cost into a small constant one, and it is the structural prerequisite for spreading
uploads across frames later.

**Independent Test**: load the largest environment, read the per-load instrumentation,
and confirm the submission count is a small constant that does not scale with the asset's
buffer + texture count, while the uploaded result renders identically.

**Acceptance Scenarios**:

1. **Given** an asset with many buffers and textures, **When** it is loaded, **Then**
   the number of GPU submissions to upload it is a small constant, not proportional to
   its resource count.
2. **Given** the same asset, **When** the submission count is compared before and after,
   **Then** it drops from hundreds to single digits.
3. **Given** a small asset (few buffers, no textures), **When** it is loaded, **Then** it
   still uploads correctly and does not pay for mechanisms it does not need.

---

### User Story 2 - Batched uploads render and behave exactly as before (Priority: P1)

An asset uploaded in batches is indistinguishable from one uploaded per resource: the
same geometry, the same textures, the same materials and mip chains. Residency, the
byte budget, the LRU, and every spec-050 behaviour are unchanged.

**Why this priority**: batching is a performance change only. If it altered what renders
or how assets are accounted, it would be a regression, not an optimisation. It is P1
because the feature has no value unless it is exactly behaviour-preserving.

**Independent Test**: load every environment and prop, compare rendered output and
residency accounting against the pre-change run; the existing suite stays green.

**Acceptance Scenarios**:

1. **Given** any asset, **When** it is uploaded in batches, **Then** every submesh's
   geometry and material render identically to the per-resource path.
2. **Given** a texture used by two materials, **When** the asset is uploaded, **Then**
   the texture is uploaded once and its residency accounting is unchanged.
3. **Given** a not-loaded environment, **When** a render or physics path reads it,
   **Then** it still fails loudly (spec 050 FR-011), unchanged.

---

### User Story 3 - A single oversized resource stays within a bounded staging footprint (Priority: P2)

An individual buffer or texture larger than the render layer's staging capacity is uploaded in
multiple copies; no single resource forces an unbounded staging allocation, and peak staging
stays bounded regardless of the asset's size.

**Why this priority**: correctness/robustness for the largest assets (ArtRoom is ~1.68 GB
of textures). It matters once batching exists, but the common case (US1/US2) delivers the
value first.

**Independent Test**: load an asset containing a single resource larger than the staging
bound and confirm that resource uploads correctly in multiple copies while peak staging stays
under the fixed bound.

**Acceptance Scenarios**:

1. **Given** a single resource larger than the staging capacity, **When** the asset is
   loaded, **Then** that resource uploads correctly in multiple copies and peak staging stays
   under the bound.
2. **Given** any asset, **When** staging memory is observed, **Then** it stays under the
   fixed bound regardless of the asset's total size.

---

### Edge Cases

- A single buffer or texture larger than the staging capacity: uploaded in multiple
  copies; the submission count never grows with the asset's total size.
- A texture already resident (shared by another loaded asset): not re-uploaded; the batch
  skips it and the existing refcount is preserved.
- A texture referenced by two materials within one asset: uploaded once.
- An asset with no textures (geometry only) or no geometry (textures only): batched
  correctly, with no empty submission.
- Mip-chain generation for a texture: happens after the copy, outside any render pass, as
  it does today.
- A failure partway through an asset's upload: no partially-created resources are left
  behind; the existing rollback (spec 050) is preserved.
- Uploading must not disturb the frame's own rendering: an asset load's submissions are
  separate from, and correctly ordered with respect to, the frame's.
- Two assets loaded back to back each batch independently; no cross-asset batching is
  required or assumed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Uploading one asset's GPU resources MUST use a bounded number of
  submissions that does not grow with the asset's **resource** (buffer + texture) count.
  Submissions scale with the number of staging flushes (the asset's upload volume divided by
  the staging capacity, FR-004), which is a small handful — single digits for the reference
  assets — and never one per resource.
- **FR-002**: A batched upload MUST produce the same GPU resources and the same rendered
  output as the current per-resource path — byte-identical geometry, textures, materials
  and mip chains.
- **FR-003**: Copy operations and mip-chain generation MUST respect the render API's
  pass rules (copy passes and mip generation outside any render pass), exactly as the
  current upload path does.
- **FR-004**: Peak staging memory MUST be bounded by a fixed capacity. Copies are packed
  into a staging buffer of that capacity and flushed (submitted) when it fills, so a large
  asset is uploaded in several bounded flushes and peak staging never exceeds the capacity;
  it does not grow with the asset's total size. A single buffer or texture larger than the
  capacity is split into copies.
- **FR-005**: A resource that is already resident (a shared texture) MUST NOT be
  re-uploaded; its residency entry, refcount, and byte accounting MUST be unchanged.
- **FR-006**: A failure partway through an upload MUST leave no partially-created GPU
  resources and MUST roll back exactly as the current path does (no leak, no half-built
  asset).
- **FR-007**: The change MUST NOT alter any spec-050 behaviour: the residency byte budget,
  the LRU order, load-on-demand, the loud failure on non-resident use, or shutdown
  release. `ResidentBytes` for an asset MUST be the same as before.
- **FR-008**: The per-load instrumentation (submission count and the upload-time split)
  MUST remain available so the improvement is measurable and regression-checkable.
- **FR-009**: The change MUST apply to both environments and props, since both use the
  same upload path.
- **FR-010**: The implementation MUST measure both the submission cost and the
  per-submesh resource-creation cost, so it is known whether batching submissions alone
  reaches SC-001. Combining an asset's geometry into fewer buffers (which would also
  remove per-submesh creation) is a deferred follow-on, not part of this feature.

### Key Entities *(include if feature involves data)*

- **Asset upload**: the set of GPU resources one asset load must bring onto the device —
  its geometry buffers and its textures.
- **Submission**: one unit of work recorded and handed to the GPU; today one per resource,
  after this feature a small constant per asset (or per chunk).
- **Staging capacity**: the fixed bound on host memory borrowed to move bytes to the GPU; a
  staging buffer never exceeds it, and a large asset is uploaded in several bounded flushes.
- **Resource**: a GPU buffer (geometry) or texture (with its mip chain). Resources are the
  things that today each cost their own submission.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Loading the largest environment uploads it in a **small handful of submissions**
  (single digits; FR-001) and spends no more than ~5% of its total load time on upload
  submission overhead — down from ~820 ms across 469 submissions at baseline. The host data copy
  and per-texture creation are not submission overhead and are out of scope. (Baseline figures
  are measured; the post-change figures await a GPU re-measure of the flushing revision.)
- **SC-002**: The submission count for the largest environment falls from hundreds
  (measured: 469) to a small handful (single digits) that does not grow with its resource
  count.
- **SC-003**: 100% of environments and props render identically to the pre-change build,
  and the existing test suite passes unchanged.
- **SC-004**: Peak staging memory stays within a fixed bound (one staging buffer), and
  does not grow with the asset's total size.
- **SC-005**: Small assets (e.g. a geometry-only environment) remain as fast as before;
  batching adds no measurable cost to them.

## Assumptions

- **Scope is per-asset, synchronous batching.** Combining an asset's own resources into
  few submissions is in scope. Moving uploads off the main thread, spreading them across
  frames, or batching across a frame's worth of assets are **out of scope** (later phases
  of the wider "loading does not stall the frame" effort).
- **Behaviour-preserving.** This feature changes *how many submissions* an asset upload
  takes, not what is uploaded, when, or how it is accounted. Spec 050 is the governing
  prior feature and is unchanged by this one.
- **The render API allows a copy pass to carry many uploads**, and permits multiple copy
  regions plus a per-texture mip generation outside any render pass; the current upload
  path already uses this shape for a single resource and is generalised here.
- **A per-copy staging capacity is chosen by the developer** as a single constant; a
  user-facing setting is out of scope. Copies are packed into one capacity-sized staging
  buffer and flushed when it fills, so peak staging is bounded and does not grow with the
  asset's size.
- **The measured baseline** is the ArtRoom environment on the reference machine: 5.42 s
  total load, 0.67 s geometry-buffer upload (382 submissions), 0.55 s texture upload
  (87 submissions), 469 submissions total. Other assets scale proportionally smaller.
- **The residual upload cost is structural, not slack.** After batching and staging pooling,
  ArtRoom's upload work is ~0.29 s: staging ~0.13 s, host `memcpy` ~0.16 s, submission
  ~3 ms. The host copy (moving ~1.26 GB of level-0 texture data to the device) and per-texture
  creation (~0.22 s for 87 `CreateGPUTexture` + sampler) are not submission overhead and are
  not removed by this feature; the host copy could be fused into the decode step and the
  creation cost reduced only by producing fewer, larger textures.
- **Textures remain decoded on the loading thread.** Decode (the largest single cost,
  ~4.17 s for ArtRoom) is CPU work and is explicitly **not** addressed here; it is a
  later phase. This feature only removes the per-resource submission overhead on the
  device side.
- **Resource creation is out of scope.** This feature removes per-*submission* overhead;
  it does not change how many buffers an asset's geometry is split into. The per-submesh
  creation cost is measured (FR-010); combining geometry into fewer buffers is deferred
  until that measurement says it is needed to meet SC-001.
