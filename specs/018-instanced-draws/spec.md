# Feature Specification: Instanced Static Draws

**Feature Branch**: `018-instanced-draws`

**Created**: 2026-09-13

**Status**: Draft

**Input**: User description: "Fix the ArtRoom draw-call cliff: the frame is CPU-bound on draw submission because every environment part and prop draws each of its submeshes with its own call (521 for the ArtRoom, ~20-30 ms in `record`). Batch static geometry by unique submesh identity and draw one instanced call per identity, so 521 collapses to 191. Plan and evidence in `docs/RENDER_PERFORMANCE.md` §6–§7."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Large static scenes render within the frame budget (Priority: P1)

A developer loads a detailed environment — hundreds of parts built from a much
smaller set of shared submeshes — and plays it. The frame keeps up with the
target rate; the cost of drawing the scene tracks how many *distinct* pieces of
geometry there are, not how many copies of them are placed. Placing a hundred
more copies of an existing prop does not add a hundred draws.

**Why this priority**: this is the whole point. The current build cannot sustain
the frame rate on the project's own environment asset, so every scene built on
top of it inherits the problem.

**Independent Test**: load the environment, play it, and confirm the per-frame
cost falls from the current multi-tens-of-milliseconds range to within the frame
budget, while draw calls drop from one-per-submesh-instance to one per unique
submesh.

**Acceptance Scenarios**:

1. **Given** an environment whose parts share a small set of submeshes, **When**
   it is rendered, **Then** each unique submesh is drawn with a single call that
   covers all of its placed instances.
2. **Given** the environment is loaded, **When** frames are rendered, **Then**
   the scene's draw-call count equals the number of distinct submeshes in use,
   not the number of part submeshes.
3. **Given** many copies of the same prop are placed, **When** they are rendered,
   **Then** they add no new draw calls beyond the one for their unique submesh.

---

### User Story 2 - The rendered image is unchanged (Priority: P1)

The batching is a rendering-cost change, not a visual one. Every instance keeps
its own position, orientation, scale, and material, and the scene looks exactly
as it did before the change.

**Why this priority**: a batching bug that silently mis-transforms or mis-assigns
materials is worse than the slowness it fixes; correctness is co-equal with the
speed-up. It is also the fix for a known hazard: geometry is batched by the
*shared submesh*, so two different assets that happen to use the same submesh
index must never be merged.

**Independent Test**: render the same scene before and after and confirm the
image is identical; in particular, place two props that both use submesh index 0
from different assets and confirm each still draws its own geometry and material.

**Acceptance Scenarios**:

1. **Given** an environment and placed props, **When** the scene is rendered,
   **Then** each instance appears at the same position, orientation, and scale as
   before.
2. **Given** two different assets that both use submesh index 0, **When** they
   are batched for rendering, **Then** they are drawn as separate groups with
   their own geometry and material.
3. **Given** a prop is rotated or scaled, **When** it is rendered through the
   batch, **Then** its material, normals, and lighting match the unbatched
   result.

---

### User Story 3 - The reduction is observable (Priority: P2)

The frame log reports how many draw calls (and how many instances) the scene
issued, so the improvement is measured rather than assumed, and any future
regression shows up immediately.

**Why this priority**: the change's value is entirely a reduction in draw calls;
without a visible count it cannot be confirmed in a real run.

**Independent Test**: run the scene and read the per-frame log; the draw-call
count for the environment matches the number of unique submeshes.

**Acceptance Scenarios**:

1. **Given** a scene is rendering, **When** the log is printed, **Then** it
   reports the draw-call count for the frame.
2. **Given** the environment with no props, **When** the log is printed, **Then**
   the draw-call count is the environment's distinct-submesh count.

---

### Edge Cases

- Two different assets using the same submesh index: they must batch as separate
  groups, never merge (each has its own vertex/index buffers and material).
- An empty environment with no parts and no props: no draw calls, no error.
- A prop or part spawned or deleted while the editor is open: the next frame's
  batching reflects it (no stale group).
- A submesh with many instances and one with a single instance: both draw once,
  with the correct instance count; a zero-instance group draws nothing.
- The editor is paused/open and the world is not simulating: the batched scene
  still renders correctly.
- Scene geometry changing size between frames (spawning assets): buffers grow as
  needed without re-allocating per frame, and without a frame-time spike from
  per-frame allocation.
- The batched draw must not disturb the animated actors or the debug/gizmo draws
  that share the frame.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Static geometry MUST be drawn with one instanced call per unique
  submesh identity, where an identity is the shared submesh within its owning
  asset.
- **FR-002**: The batching key MUST distinguish submeshes from different assets
  even when their indices coincide, so geometry and materials are never mixed.
- **FR-003**: Each instance's own model transform (position, orientation, scale)
  and derived normal transform MUST be supplied per instance; only the
  view-projection is shared across the batch.
- **FR-004**: Instances that share a submesh MUST be grouped without re-uploading,
  re-merging, or duplicating the shared vertex/index data.
- **FR-005**: The rendered result MUST be unchanged from the current per-submesh
  draw: same positions, orientations, scales, materials, normals, and lighting.
- **FR-006**: Batching MUST cover both placed props and the environment's imported
  parts, the two sources of static geometry.
- **FR-007**: Per-frame draw-call count MUST scale with the number of distinct
  submeshes in use, NOT with the number of instances placed.
- **FR-008**: The static pipeline and its shader MUST accept a per-instance data
  source, so a single call can draw every instance of a submesh.
- **FR-009**: The frame log MUST report the number of draw calls issued and the
  number of instances drawn, so the reduction is measurable.
- **FR-010**: Per-frame batching MUST reuse its buffers and MUST NOT allocate on
  the render hot path.
- **FR-011**: Actors (animated meshes) and debug/gizmo draws MUST continue to
  render as they do today; this feature does not change their pipelines.

### Key Entities *(include if feature involves data)*

- **Submesh identity**: a single shared static submesh within one asset (its
  uploaded vertex/index buffers plus material). The unit of one draw call.
- **Instance**: one placed occurrence of a submesh — an environment part or a
  placed prop — carrying its model and normal transforms.
- **Instance buffer**: the per-frame, reused buffer holding every instance's
  transforms, grouped so each group is a contiguous run.
- **Batch group**: the set of instances sharing one submesh identity, drawn with
  one instanced call.
- **Static scene**: the placed props plus the environment's parts; the union the
  gather walks each frame.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With the project's environment loaded (463 parts, 521 part-submesh
  draws), the frame's draw-submission cost falls from the current multi-tens of
  milliseconds into the frame budget (target `record` under 5 ms), measured by the
  existing per-phase frame log.
- **SC-002**: Draw calls for that environment fall from 521 to its distinct-submesh
  count (191), reported by the frame log.
- **SC-003**: The rendered image is visually identical to the per-submesh draw
  (100% of instances correct in transform and material).
- **SC-004**: Placing additional copies of existing geometry adds zero draw calls
  (100% of the time).
- **SC-005**: Draw calls (and instance count) are reported every frame in the log.
- **SC-006**: No regression in actor or debug/gizmo rendering, and no per-frame
  allocation added on the render path.

## Assumptions

- The frame is CPU-bound on draw submission, established in
  `docs/RENDER_PERFORMANCE.md` §4–§6; GPU throughput is not the bottleneck.
- Geometry is already deduplicated at glTF load (each unique submesh is uploaded
  once and shared by parts), so batching needs no re-upload or merge.
- Frustum culling remains out of scope (it was tried and reverted; it reduces
  instances, not call counts).
- Static environments and props share the existing static pipeline; animated
  actors keep their own pipeline.
- Per-submesh instance counts are recomputed per frame for correctness; caching
  them at load (Nu's freeze-time idea) is deferred until profiling justifies it.
- A single environment is loaded at a time, but many prop assets may be present.
