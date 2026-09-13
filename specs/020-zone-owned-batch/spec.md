# Feature Specification: Zone-Owned Static Batch

**Feature Branch**: `020-zone-owned-batch`

**Created**: 2026-09-13

**Status**: Draft

**Input**: User description: "Move the static-scene render batch out of the gameplay screen's state and onto the zone it draws. The batch is derived entirely from one zone's content (placed props + environment parts), so a screen-level batch is a single buffer shared by every zone once multiple zones exist — it accumulates across zone switches and cannot draw two zones at once. Give each zone its own batch so ownership matches what the batch describes."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A zone owns the batch that draws it (Priority: P1)

The static-scene render batch — the buffer holding every placed object's transform
for the current frame — belongs to the zone whose content it describes, not to the
screen. A developer reading a zone sees everything needed to render it; a developer
reading the screen sees only screen-level state (view, editor, play state).

**Why this priority**: this is the ownership correction. The batch's entire content
is a function of one zone, so putting it on the screen is the same kind of mismatch
as putting device resources there.

**Independent Test**: inspect the zone and the screen state types; the batch is a
zone field and the screen state has none.

**Acceptance Scenarios**:

1. **Given** the zone type, **When** a developer reads it, **Then** it owns the
   static-scene batch.
2. **Given** the gameplay screen's state type, **When** a developer reads it,
   **Then** it has no static-scene batch field.

---

### User Story 2 - Zones never share batching state (Priority: P2)

When several zones exist, each has its own batch: switching between zones never
accumulates one zone's geometry identities into another's, and rendering two zones
in the same frame is a matter of drawing each zone's batch.

**Why this priority**: this is what makes the move future-proof; the current single
zone hides the problem, but the zone phase will not.

**Independent Test**: with two zones loaded, confirm each has a distinct batch and
that rendering one does not use the other's state.

**Acceptance Scenarios**:

1. **Given** two zones, **When** they are created, **Then** each has its own batch.
2. **Given** a zone that has rendered, **When** its batch is released (at shutdown
   today; on unload once zone unload exists), **Then** its GPU buffers are freed
   without touching another zone's.

---

### User Story 3 - The scene still renders identically (Priority: P1)

The move is about ownership, not behaviour: the same scene draws with the same
draw counts and appearance, and creating a zone does not become dependent on the
GPU.

**Why this priority**: co-equal with the ownership goal; a "move" that changes the
image or breaks headless zone creation is a regression.

**Independent Test**: run the game and confirm the same draw counts as before;
confirm the existing test suite (which creates zones without a GPU) still passes.

**Acceptance Scenarios**:

1. **Given** the same scene, **When** it renders, **Then** the draw counts and
   appearance match the pre-change build.
2. **Given** a zone is created without a GPU device, **When** nothing renders,
   **Then** creation succeeds (headless zones stay valid).
3. **Given** the app shuts down, **When** it releases the live zone's batch,
   **Then** the buffers are freed before the device is destroyed.

---

### Edge Cases

- Zones can be created without a GPU device (tests, headless); device-dependent
  buffer allocation must happen only when the batch is first used for rendering.
- A zone with no props and no environment: its batch draws nothing and must not
  allocate buffers needlessly.
- The batch's buffer grows with scene size; growing must not free a buffer still
  in flight, and must not allocate on every frame.
- Releasing a zone's batch must not free another zone's buffers, and must happen
  before the device is destroyed.
- The compile order must still be acyclic: a zone owning the batch, while the batch
  gathers from a zone's contents, must not require a cycle.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The static-scene batch MUST be owned by the zone whose content it
  draws, not by the gameplay screen's state.
- **FR-002**: Each zone MUST own a distinct batch, so no two zones share batching
  state (registry, counts, or buffers).
- **FR-003**: Rendering MUST gather, upload, and draw the active zone's static
  scene through that zone's batch, preserving one instanced draw per distinct
  submesh identity.
- **FR-004**: Creating a zone MUST NOT require a GPU device; buffer allocation is
  deferred until the batch is first used for rendering.
- **FR-005**: A zone's batch GPU buffers MUST be releasable, and released when the
  zone is released (and, for the live zone, before the device is destroyed at
  shutdown).
- **FR-006**: The rendered result MUST be unchanged: same pipelines, same draw
  counts, same appearance.
- **FR-007**: The move MUST NOT add per-frame work or allocations; allocation
  happens only on first use or growth.
- **FR-008**: The change MUST NOT alter the zone/entity model semantics (refs,
  physics attachment, transforms) or the editor command/undo model.

### Key Entities *(include if feature involves data)*

- **Zone**: a playable area owning its entities (actors, props, environment parts).
  Now also owns the static-scene batch that renders them.
- **Static-scene batch**: the per-zone renderer state — the submesh registry,
  per-group counts/offsets, and the per-frame instance buffer.
- **Instance buffer**: the GPU/CPU buffer holding every instance's transform; now
  allocated lazily (first render) rather than at zone creation.
- **Gameplay screen state**: the screen's own state — entities, view, editor/play
  state — and no longer the static-scene batch.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The gameplay screen's state type contains zero static-scene batch
  fields.
- **SC-002**: Every zone owns exactly one batch, and each is a distinct object
  (100% of zones).
- **SC-003**: The rendered scene is unchanged — the same draw counts (e.g.
  `draws 191 instances 521` for the project environment) and appearance as before.
- **SC-004**: Zone creation remains GPU-free: the existing headless test suite
  (which creates zones without a device) passes unchanged.
- **SC-005**: A zone's batch buffers are released before the device is destroyed;
  releasing one zone's batch does not affect another's.

## Assumptions

- Multiple zones and a zone map arrive in the zone phase; today one zone is active
  and only it is drawn, so this change is behaviour-preserving now and correct when
  zones multiply.
- The zone-walking gather currently lives with the batch; because the zone now owns
  the batch, the gather must not require the zone type ahead of it (the compile
  order must stay acyclic) — the gather reads the zone's prop/part maps instead.
- Explicit "release the batch when a zone unloads" is wired when zone unload exists
  (zone map); for now the live zone's batch is released at shutdown.
- Debug line and gizmo buffers are view overlays, not zone content, and stay on the
  screen; only the zone's static-scene batch moves.
- No new rendering feature is added; this is an ownership/location change only.
