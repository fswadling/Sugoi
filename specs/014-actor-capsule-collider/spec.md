# Feature Specification: Actor Capsule Collider

**Feature Branch**: `014-actor-capsule-collider`

**Created**: 2026-09-06

**Status**: Draft

**Input**: User description: "Add a checkbox on actors to be able to see their capsule collider, and align the collider's size and position with the rendered actor."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Toggle capsule collider visualization (Priority: P1)

A developer working in the editor wants to see the physical capsule collider attached to an actor so they can verify it lines up with the character model.

**Why this priority**: Visualization is the requested entry point; without it, there is no way to inspect an actor's collider.

**Independent Test**: Spawn an actor, select it, enable the collider toggle in the inspector, and confirm a capsule wireframe appears over that actor and disappears when toggled off.

**Acceptance Scenarios**:

1. **Given** an actor is selected, **When** the developer enables "Show Collider", **Then** a capsule wireframe is drawn over that actor.
2. **Given** the capsule visualization is on, **When** the developer disables "Show Collider", **Then** the wireframe is no longer drawn for that actor.
3. **Given** a non-actor entity (such as a prop) is selected, **Then** the actor capsule toggle is not shown.

---

### User Story 2 - Collider matches the rendered actor (Priority: P2)

The capsule collider must reflect the actor's physical footprint: a consistent torso width and the actor's full height, standing on the actor's feet.

**Why this priority**: A collider that is too small or misplaced makes the visualization misleading and undermines future gameplay physics.

**Independent Test**: With visualization enabled, confirm the capsule spans the actor's full height from feet to head and uses the same width across different actor assets.

**Acceptance Scenarios**:

1. **Given** an actor spawned at the origin, **When** its collider is shown, **Then** the capsule's base sits at the actor's feet and its top reaches the actor's head.
2. **Given** any actor asset, **When** its collider is shown, **Then** the capsule uses a consistent radius rather than varying with the actor's arm span.

---

### Edge Cases

- An actor with no collider should not draw a capsule when the toggle is enabled, and toggling it should have no effect.
- An actor shorter than twice the fixed radius should produce a valid capsule with no negative cylinder length.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The editor MUST provide a per-actor control to show or hide an actor's capsule collider.
- **FR-002**: When enabled, the editor MUST render a wireframe capsule over the actor.
- **FR-003**: The capsule MUST be positioned so its base is at the actor's feet and its top is at the actor's head.
- **FR-004**: The capsule MUST use a consistent radius regardless of the actor's arm span.
- **FR-005**: Toggling the visualization MUST NOT change the actor's position or behavior.
- **FR-006**: The visualized capsule MUST reflect the same collider the physics system uses.

### Key Entities *(include if feature involves data)*

- **Actor**: A placed character entity with a position (its feet), an asset reference, and a collider.
- **Capsule Collider**: The actor's physical collision shape, described by a radius and a height, plus a per-actor visibility flag.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can toggle an actor's collider visualization with a single control.
- **SC-002**: The rendered capsule matches the actor's full height and foot placement for every actor asset.
- **SC-003**: Toggling visualization produces no change in the actor's position or behavior.
- **SC-004**: All actor assets produce a capsule with the same radius.

## Assumptions

- The actor's entity position represents its feet (ground contact), matching the reference implementation.
- The fixed capsule radius matches the reference implementation's default.
- Props are out of scope; only actors receive the capsule visualization toggle.
