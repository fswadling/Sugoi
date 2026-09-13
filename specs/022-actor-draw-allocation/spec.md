# Feature Specification: Actor Draw Allocation

**Feature Branch**: `022-actor-draw-allocation`

**Created**: 2026-09-13

**Status**: Draft

**Input**: User description: "The actor draw path allocates a one-element array to bind the bone storage buffer on every frame, for every actor. The game loop runs every frame, so the draw path should allocate nothing — reuse a per-instance binding array instead (the pattern the mesh, static-mesh, and debug-draw paths already use)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Drawing an actor allocates nothing per frame (Priority: P1)

The renderer binds each actor's bone storage buffer before drawing it. That
binding must reuse storage owned by the actor, not build a fresh array every
frame, so a scene with actors produces no recurring garbage from the draw path.

**Why this priority**: per-frame allocation is the project's explicit
performance constraint (the game loop runs every frame); a small per-actor
allocation is still an avoidable recurring cost and contradicts that principle.

**Independent Test**: inspect the actor draw path for any array/list allocation
in the per-frame code; the binding array is created once per actor instance.

**Acceptance Scenarios**:

1. **Given** a drawn actor, **When** it is drawn on successive frames, **Then** no
   array or list is allocated on the draw path.
2. **Given** several actors, **When** they are drawn in the same frame, **Then**
   each binds its own bone buffer with no per-frame allocation.

---

### User Story 2 - The actor still renders identically (Priority: P2)

The optimisation is about allocation, not behaviour: the same bone storage buffer
is bound at the same slot with the same count, so the skinned result is unchanged.

**Why this priority**: a binding that silently changed the buffer, slot, or count
would be a rendering regression; correctness guards the change.

**Independent Test**: draw an animated actor and confirm it deforms identically to
the pre-change build.

**Acceptance Scenarios**:

1. **Given** an actor, **When** it is drawn, **Then** its bone storage buffer is
   bound at vertex storage slot 0 with exactly one binding.
2. **Given** an animated actor, **When** it plays, **Then** the skinned result is
   unchanged from the pre-change build.

---

### Edge Cases

- Several actors: each must bind its own buffer (no shared buffer overwritten
  before the GPU records the bind).
- An actor removed from the zone: its binding array is released with the instance,
  like its other render resources.
- The draw path takes the renderer by value (a struct copy): the copied array
  reference must still point at the instance's buffer.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Drawing an actor MUST NOT allocate any array or list per frame.
- **FR-002**: The actor's bone storage buffer MUST still be bound at vertex storage
  slot 0 with exactly one binding.
- **FR-003**: The rendered result MUST be unchanged (skinning, transform, material).
- **FR-004**: The binding array MUST be created once per actor instance (at
  creation), not per frame.
- **FR-005**: The rest of the actor draw/upload path MUST remain allocation-free per
  frame (pose sampling writes in place; mesh bindings are already reused).
- **FR-006**: The change MUST NOT alter the renderer's public surface, lifecycle, or
  resource ownership (the bone buffer and transfer buffer are still released with
  the instance).

### Key Entities

- **Actor renderer**: the per-instance render state owned by an actor entity — its
  animation slot, pose frame, bone storage buffer, upload buffer, and the reusable
  binding array used to bind the storage buffer.
- **Bone storage buffer**: the per-instance GPU buffer holding the skinning
  matrices; bound at vertex storage slot 0 each draw.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Zero allocations on the actor draw path per frame (verified by
  inspection: the only per-frame binding input is a preallocated instance array).
- **SC-002**: A scene with actors renders identically to the pre-change build
  (skinned deformation, transforms, materials).
- **SC-003**: `dotnet build` is clean and the existing headless test suite passes
  unchanged.

## Assumptions

- The allocation is the one-element binding array in the actor draw; no other
  per-frame allocation exists on the actor path (to be confirmed by inspection as
  part of this change).
- A per-instance array is preferred over a single shared mutable array so a bind is
  always recorded with that actor's own buffer, matching the mesh/static-mesh/
  debug-draw pattern.
- No new rendering feature is added; this is an allocation-removal change on an
  existing path.
- GPU draw behaviour cannot be unit-tested headlessly; correctness is covered by
  build + tests plus a runtime check that the actor still renders identically.
