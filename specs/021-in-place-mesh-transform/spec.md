# Feature Specification: In-Place Mesh Transform

**Feature Branch**: `021-in-place-mesh-transform`

**Created**: 2026-09-13

**Status**: Draft

**Input**: User description: "Dragging or nudging a placed prop (or an environment part) in the editor stalls the frame for ~200-280 ms, because the entity's collider body — and the mesh shape with its BVH — is destroyed and rebuilt on every transform change. A mesh shape cannot be scaled on the body, so the scale is baked into the collider geometry; that means scale is the only property that invalidates the shape. Move/rotate should update the existing body in place; only a scale change should rebuild."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Dragging a prop stays smooth (Priority: P1)

A level designer drags a placed prop across the scene to position it. Each step of
the drag updates the prop's position; the prop's collider must follow, but the
editor must stay responsive — the drag should feel continuous, not hitch on every
frame.

**Why this priority**: dragging and nudging props is the core editor interaction.
A multi-hundred-millisecond stall per frame makes precise placement impossible and
is the largest known editor stall.

**Independent Test**: load a scene with a mesh-colliding prop, drag it, and watch
the per-frame timing log: the `update` phase must stay under 5 ms during the drag.

**Acceptance Scenarios**:

1. **Given** a placed prop (or environment part) with a mesh collider, **When** the
   designer drags it so its position changes each frame, **Then** the per-frame
   `update` time stays under 5 ms (no multi-hundred-millisecond stall).
2. **Given** the same prop, **When** it is moved or rotated, **Then** its collider
   follows the rendered geometry exactly (picking/selecting the prop at its new
   position still resolves it).
3. **Given** the same prop, **When** the designer nudges it repeatedly, **Then**
   each nudge is as cheap as a plain move (no collider rebuild).

---

### User Story 2 - Scaling a prop still rebuilds its collider (Priority: P1)

A designer resizes a prop with the scale gizmo. Unlike a move or rotate, a scale
change alters the prop's collider shape, so the collider must be rebuilt to match.

**Why this priority**: co-equal with US1 — the optimisation must not break the one
case that genuinely needs a rebuild. A collider that silently ignores scale would
be a correctness regression.

**Independent Test**: scale a prop and confirm its collider matches the new size
(selection/picking still hugs the geometry).

**Acceptance Scenarios**:

1. **Given** a placed prop with a mesh collider, **When** its scale changes,
   **Then** its collider is rebuilt so it matches the new scaled geometry.
2. **Given** a prop scaled non-uniformly, **When** the scale changes on any axis,
   **Then** the rebuilt collider matches that non-uniform scale.

---

### User Story 3 - The collider never drifts from the geometry (Priority: P2)

Whatever the transform change, the collider must remain a faithful proxy for the
rendered geometry, and entity identity must be preserved so picking and undo keep
working.

**Why this priority**: correctness guard for the optimisation. The user-visible
promise is "the prop behaves the same, it is just faster".

**Independent Test**: after a mix of moves, rotates, and scales, pick the prop and
confirm the same entity is selected, and confirm the collider volume still matches
the mesh.

**Acceptance Scenarios**:

1. **Given** a moved prop, **When** the designer picks it, **Then** the same entity
   is resolved (the body→entity mapping is intact).
2. **Given** a prop with no collider (a part with no collider can be derived),
   **When** its transform changes, **Then** the transform still updates and no
   error occurs.
3. **Given** any transform change on a prop, **When** the change is undone, **Then**
   the prop (and its collider) return to the prior transform as before.

---

### Edge Cases

- Transform change with an unchanged scale but a changed rotation: should take the
  cheap in-place path.
- Scale changed by the gizmo: must rebuild so the collider shape tracks.
- Non-uniform scale: must still rebuild correctly.
- Entity with no collider (`Physics = None`): transform still updates; no body work.
- Scale values that differ only by floating-point rounding: taking the rebuild path
  is acceptable (a performance fallback, never a correctness failure).
- A stale or absent ref: the transform change is a no-op, as today.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When a mesh-backed entity's transform changes and its scale is
  unchanged, the collider body MUST be updated in place (position and rotation)
  without destroying and recreating the body or rebuilding the collider shape.
- **FR-002**: When the entity's scale changes, the collider MUST be rebuilt so its
  shape reflects the new scale (including non-uniform scale).
- **FR-003**: After any transform change (move, rotate, or scale), the collider MUST
  continue to match the transformed rendered geometry.
- **FR-004**: The body→entity association MUST remain correct after an in-place
  update, so picking and selection resolve the same entity.
- **FR-005**: An entity with no collider MUST still have its transform updated, with
  no collider work.
- **FR-006**: The change MUST NOT alter entity/ref semantics, the command/undo
  model, or the behaviour of actor (capsule) colliders.
- **FR-007**: The in-place path MUST NOT allocate (no re-baked geometry) or rebuild
  the collider shape and its acceleration structure.
- **FR-008**: An unchanged-scale transform change MUST NOT rebuild the collider
  shape, no matter how many such changes occur (e.g. every drag frame).

### Key Entities

- **Mesh-backed entity**: a placed prop or an environment part; owns a transform
  (position, rotation, scale) and, usually, a mesh collider.
- **Mesh collider**: node-local, unscaled geometry (vertices + indices) paired with
  a physics body. The body carries position + rotation; the entity's scale is baked
  into the collision shape built from the collider (the stored collider geometry is
  itself unscaled), which is why only scale invalidates it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Dragging or nudging a mesh-colliding prop produces no per-frame stall:
  the `update` phase in the timing log stays under 5 ms for the whole of a sustained
  drag (previously ~200-280 ms per affected frame).
- **SC-002**: A moved or rotated prop's collider matches its rendered geometry: a
  pick at the prop's new position resolves it.
- **SC-003**: A scaled prop's collider matches the new scale (uniform and
  non-uniform).
- **SC-004**: No change to the rendered scene or the entity model: the existing tests
  continue to pass unchanged and `dotnet build` is clean.
- **SC-005**: Entity identity is preserved across a move: the same entity is
  selected before and after (body→entity mapping intact).

## Assumptions

- The collider shape is invalidated only by scale, because scale is baked into the
  mesh collider's vertices while position and rotation live on the body.
- Actor (capsule) colliders are out of scope: capsules are scale-independent and
  cheap to build, and dynamic-body activation needs separate consideration. Actors
  keep their current transform path.
- The transform-change call sites (drag, nudge, gizmo, undo) are unchanged; the
  optimisation is internal to the single mesh-transform path.
- Comparing the entity's stored scale with the incoming scale is a safe trigger:
  the worst case (a needless rebuild) is only slower, never incorrect.
- No new editor feature is added; this is a performance/correctness-preserving
  change to an existing path.
