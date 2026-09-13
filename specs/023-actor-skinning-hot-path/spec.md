# Feature Specification: Actor Skinning Hot Path

**Feature Branch**: `023-actor-skinning-hot-path`

**Created**: 2026-09-13

**Status**: Draft

**Input**: User description: "One skinned actor adds ~3 ms to the per-frame `record` time in Debug builds (Release is ~0.1 ms). The cost is unoptimized managed work in the pose sampler: it allocates a reference tuple per joint per frame, composes the local joint transform with an unnecessary extra matrix multiply, and dispatches a multi-way match per animation channel. Reduce the per-frame work in the sampler so developing in Debug is not dominated by actor skinning, without changing the sampled pose."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sampling a pose allocates nothing per frame (Priority: P1)

A developer running a scene with actors in a Debug build should not see the
pose sampler churn the heap. Sampling a pose — which happens once per actor per
frame — must reuse existing storage and allocate no tuples, arrays, or lists.

**Why this priority**: the game loop runs every frame; a per-joint allocation is a
recurring cost and contradicts the project's no-per-frame-allocation rule. For a
166-joint character this is currently ~330 allocations per frame per actor.

**Independent Test**: sample a pose and confirm the sampler allocates nothing
(measured; the sampler's per-joint helper returns a value, not a reference tuple).

**Acceptance Scenarios**:

1. **Given** an actor model, **When** a pose is sampled, **Then** no tuple, array,
   or list is allocated on the sample path.
2. **Given** the sampler's per-joint helper, **Then** its result is a value type
   (it cannot produce a reference tuple).

---

### User Story 2 - Skinning does less work per joint (Priority: P1)

The sampler's per-joint work should be reduced so a Debug build is not dominated
by CPU skinning, while a Release build is not made slower.

**Why this priority**: co-equal with US1 — the measured cost is unoptimized managed
work, and the per-joint transform composition and channel application are the bulk
of it.

**Independent Test**: measure the per-actor `record` time in Debug before and after
with the Akane character; confirm it is materially lower and that Release does not
regress.

**Acceptance Scenarios**:

1. **Given** an animated actor, **When** its pose is sampled, **Then** the local
   joint transform is composed with one fewer full matrix multiply.

---

### User Story 3 - The animation is unchanged (Priority: P2)

The optimisation must not alter the sampled pose: the same local, world, and
skinning matrices, and therefore the same on-screen deformation, as before.

**Why this priority**: correctness guard. A faster sampler that produces a
different pose is a regression.

**Independent Test**: golden sample tests (a model with active channels) produce
identical matrices; an animated actor looks identical in the editor.

**Acceptance Scenarios**:

1. **Given** a model with active animation channels, **When** a pose is sampled,
   **Then** the resulting local/world/skinning matrices match the pre-change values.
2. **Given** a non-looping clip at its last frame and a root joint, **When**
   sampled, **Then** the result is unchanged.

---

### Edge Cases

- Animation frame landing exactly on a keyframe (blend = 0).
- Non-looping clip at the final frame (the second keyframe equals the first).
- Root joint (no parent).
- Non-uniform scale in the local joint transform.
- A pose joint with no active channels (bind pose).
- A model with zero joints or zero frames (sampler no-ops, as today).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Sampling a pose MUST NOT allocate any tuple, array, or list per
  frame (no reference tuples on the sample path).
- **FR-002**: Composing a joint's local transform MUST NOT use a redundant full
  matrix multiply to apply the translation.
- **FR-003**: The sampled `Local`, `World`, and `Skinning` matrices MUST be
  numerically identical to the pre-change output.
- **FR-004**: The sampler MUST NOT regress the optimized (Release) build.
- **FR-005**: The change MUST NOT alter the public surface or signatures of the
  actor sampler or the actor renderer, and MUST NOT change the upload/draw path
  (already allocation-free).
- **FR-006**: Models with zero joints, zero frames, or no channels MUST remain
  no-ops, as today.

### Key Entities

- **Actor model**: the parsed joints, inverse bind-pose matrices, per-joint pose
  channel metadata, and frame channel data used to sample a pose.
- **Pose frame**: the per-joint local, world, and skinning matrices, allocated once
  and mutated in place each frame.
- **Animation slot**: the current clip index, frame, playback rate, and loop flag.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Zero allocations on the pose sample path per frame (measured; the
  per-joint helper returns a value type).
- **SC-002**: With the Akane character, the per-actor `record` cost in a Debug build
  falls from the ~3 ms baseline to **≤ 2.2 ms** (at least 25% lower).
- **SC-003**: Release per-actor `record` does not regress from the ~0.1 ms baseline.
- **SC-004**: The rendered animation is unchanged (golden sample tests pass; the
  actor deforms identically in the editor).
- **SC-005**: `dotnet build` is clean and the existing test suite passes.

## Assumptions

- The measurement baseline is the 2026-09-13 run: Akane (166 joints), one actor
  adds ~3 ms to `record` in Debug and ~0.1 ms in Release (`RENDER_PERFORMANCE.md`
  §9).
- Debug builds run with optimizations off, so reference tuples are not folded to
  value tuples and struct copies are not elided; Release is unaffected by the
  changes beyond possible minor gains.
- The sampled pose is a pure function of the model, clip, and frame, so a
  like-for-like refactor can be guarded by golden tests.
- The channel-sampling `match` named in the input description was considered and
  deliberately left unchanged — it is a small share of the cost and unrolling it
  would hurt readability (see `plan.md` / `research.md` D3). The requirement set
  covers the per-joint tuple allocation and the redundant local-transform multiply.
- **Outcome (measured after implementation):** SC-002 was not met. In Debug the
  per-actor `record` delta was ~3.2 ms after the change vs ~3.0 ms before —
  unchanged within noise. The removed work (a per-joint tuple allocation and one
  local-transform multiply) was not dominant. The changes are retained for their
  allocation/FR-002 hygiene; the remaining Debug cost needs profiling to localise
  (see `RENDER_PERFORMANCE.md` §9).
- No new rendering feature is added; this is a hot-path optimisation of an existing
  computation.
