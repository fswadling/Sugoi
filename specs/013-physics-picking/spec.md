# Feature Specification: Physics + Picking

**Feature Branch**: `013-physics-picking`

**Created**: 2026-09-06

**Status**: Draft

**Input**: User description: "Move onto the next phase — physics and picking —
using Jolt Physics."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Props and the environment have colliders (Priority: P1)

Placed props and the parts imported from an environment are given physical
colliders, so the scene has a real set of shapes for picking and (later)
simulation. Each prop/environment part gets a mesh collider derived from its
actual triangles (not its AABB), created when the entity is spawned and removed
when it is deleted.

**Why this priority**: colliders are the foundation — picking (US2) and the
character body (US3) both build on them, and without them the phase has nothing
physical to work with.

**Independent Test**: import an environment and spawn a prop; each spawned entity
owns a collider, and deleting an entity removes its collider (no orphaned
shapes).

**Acceptance Scenarios**:

1. **Given** an environment with imported parts, **When** it loads, **Then** each
   part has a static mesh collider whose shape matches the part's geometry.
2. **Given** a spawned prop, **When** it is deleted, **Then** its collider is
   removed with it.

---

### User Story 2 - Click-select hits physical shapes (Priority: P2)

Selecting an entity in the viewport raycasts against its collider rather than an
axis-aligned bounding box, so selection matches what is actually drawn — a
rotated or non-uniformly scaled prop selects by its real shape, not a loose box.

**Why this priority**: this is the headline "accurate picking" result, and it is
only meaningful once colliders (US1) exist.

**Independent Test**: import a rotated/scaled prop and click its visible surface;
it selects even where the old AABB approximation would have missed, and a click
on empty space near (but outside) the shape does not select it.

**Acceptance Scenarios**:

1. **Given** a prop with a collider, **When** the user clicks its surface, **Then**
   the prop is selected.
2. **Given** a click on empty space adjacent to a prop, **When** the raycast runs,
   **Then** no entity is selected.

---

### User Story 3 - Character capsule + colliders stay in sync (Priority: P3)

The player character (actor) gets a capsule collider representing its body, and
moving/rotating/scaling any entity in the editor keeps its collider matched to
its rendered transform, including through undo/redo.

**Why this priority**: the character body and transform sync complete the phase
("character/props have colliders"), but they depend on the collider (US1) and
picking (US2) plumbing already being in place.

**Independent Test**: spawn an actor (it has a capsule); move a prop in the
editor and confirm a click on the moved shape still selects it; undo the move and
confirm the collider returns with the entity.

**Acceptance Scenarios**:

1. **Given** a spawned actor, **When** it is created, **Then** it has a capsule
   collider sized to its bounds.
2. **Given** a prop moved/rotated/scaled in the editor, **When** the transform is
   applied, **Then** its collider follows the new transform.
3. **Given** a transform change that is undone, **When** the command is reverted,
   **Then** the collider returns to its previous transform.

---

### Edge Cases

- What happens when an asset has no usable geometry (or an actor has no usable
  bounds)? It fails loudly rather than producing a zero-sized or placeholder
  collider.
- What happens when a prop is non-uniformly scaled? The entity scale is baked
  into the mesh collider's vertices, so picking stays accurate.
- What happens when a ray misses every collider? Selection clears to nothing.
- What happens when an entity is deleted? Its collider is removed so no orphan
  body remains in the world.
- What happens when the same entity is moved many times in one edit? The body is
  destroyed and rebuilt each time, so no stale bodies accumulate.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST create a single physics world with gravity and step
  it once per frame.
- **FR-002**: Every prop and imported environment part MUST own a static mesh
  collider derived from its actual geometry, created on spawn and removed on
  delete.
- **FR-003**: The actor MUST own a capsule collider representing its body, sized
  from its asset bounds.
- **FR-004**: Viewport click-select MUST raycast against colliders (not
  axis-aligned bounds), returning the entity hit by the ray.
- **FR-005**: When an entity's transform changes in the editor (move/rotate/scale,
  including undo/redo), its collider MUST be updated to match.
- **FR-006**: Collider create/remove/update MUST be wired through the existing
  spawn, remove, and command/undo paths so colliders never drift from entities.
- **FR-007**: The system MUST fail loudly when a collider cannot be derived
  (missing or empty mesh geometry, or missing/invalid actor bounds).

### Key Entities *(include if feature involves data)*

- **Physics world**: the Jolt physics system, its body interface, gravity, and
  the per-frame step.
- **Collider/body**: a shape (mesh or capsule) plus a transform, associated with a
  single entity.
- **Entity↔body association**: the link from an entity reference to its body, used
  for picking results and transform sync.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Click-select returns the entity whose physical shape the ray hits,
  including rotated and non-uniformly scaled props.
- **SC-002**: Every prop, environment part, and actor owns exactly one collider;
  deleting an entity leaves no orphan collider.
- **SC-003**: Moving/rotating/scaling an entity (and undoing it) keeps the
  collider matched to the rendered transform.
- **SC-004**: `dotnet build` is clean and `dotnet test` passes (new collider and
  picking tests included).

## Assumptions

- **Jolt Physics is the engine**, via the `JoltPhysicsSharp` .NET binding; it
  replaces the previously-referenced (unused) `Box3D` package.
- **Mesh colliders for props/environment.** Props and environment parts use Jolt
  mesh (BVH) colliders built from their actual glTF triangles, kept CPU-side on
  the asset. This supersedes the initial box-collider-from-AABB approach: a room
  shell's AABB encloses the whole interior, so a ray cast from inside hit the
  shell at fraction 0 and won every pick.
- **Capsule for the actor**, with radius/height derived from asset bounds;
  missing or invalid bounds fail loudly.
- **No locomotion or gravity-driven dynamics yet.** The world is created and
  stepped, but the player is not yet moved by input/gravity — that is the
  following "walk around" phase.
- **One world for picking and simulation**, matching the reference architecture;
  the world is shared rather than split per concern.
- **Screen-ray reuse.** Picking reuses the existing camera-to-screen ray; only the
  intersection test changes from AABB math to a collider raycast.
