# Feature Specification: Selection + Transform Foundations

**Feature Branch**: `005-selection-transform-foundations`

**Created**: 2026-08-30

**Status**: Complete (retrospective)

**Input**: User description: "the 2c foundations — zone container, slot-map index
access, editor transform state, and debug-draw wireframe primitives"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A zone holds the scene's entities (Priority: P1)

The editor needs a single place to put the actors and props that make up a
scene, and a way to address each one individually so it can later be selected,
moved, duplicated, and deleted. The foundation provides a zone that holds a
fixed collection of actors and props, where each entity is read, written, and
removed by a stable index. Removing an entity leaves its slot reusable without
re-numbering any other entity.

**Why this priority**: Every later editor operation (spawn, select, transform,
delete, duplicate) is just a read/write against this storage. Nothing visible
can be built on top of it without it.

**Independent Test**: Unit tests prove add → read → remove → reuse over the
entity storage, and that reading a removed (or out-of-range) index reports
"no entity" rather than returning a stale value.

**Acceptance Scenarios**:

1. **Given** an empty zone, **When** an actor is added, **Then** it can be read
   back by its index and the collection reports one entity.
2. **Given** a zone containing an entity, **When** the entity is removed, **Then**
   reading its index reports "no entity", and adding a new entity reuses that
   index without disturbing the other entities.
3. **Given** a removed or out-of-range index, **When** it is read, **Then** the
   result is "no entity" (never a stale or fabricated value).

---

### User Story 2 - The editor remembers its transform tool and an in-progress drag (Priority: P2)

To move, rotate, or scale an entity, the editor must track which transform tool
is active, whether snapping is on, and — while a drag is happening — which axis
was grabbed and the entity's transform *before* the drag began (so the change
can be undone). The foundation provides this state.

**Why this priority**: The gizmo interaction in the next phase needs this state
to know what a drag is doing and to be able to undo it. It is independent of the
storage layer.

**Independent Test**: Constructing editor state defaults to "move tool, no snap,
no drag"; recording a drag captures the axis and the pre-drag transform.

**Acceptance Scenarios**:

1. **Given** a fresh editor state, **When** inspected, **Then** the active tool
   is "move", snapping is off, and there is no drag in progress.
2. **Given** an entity's transform and a chosen axis, **When** a drag begins,
   **Then** the drag records that axis and the entity's transform as it was
   before the drag.

---

### User Story 3 - Debug drawing renders wireframe boxes and spheres (Priority: P3)

Entity markers and gizmo handles need simple wireframe geometry — a box outline
for actors/props and a sphere outline for selection highlights and gizmo tips.
The foundation adds these two shapes to the existing line primitive.

**Why this priority**: It is visual polish on top of the storage and state
stories; the gizmo and selection work in the next phase consume it but it can be
verified in isolation.

**Independent Test**: The debug-draw accumulator can emit a 12-edge box and a
sphere outline (three circles) without error, reusing the preallocated buffer.

**Acceptance Scenarios**:

1. **Given** a box center and half-extent, **When** drawn, **Then** 12 edges are
   emitted forming the box outline.
2. **Given** a sphere center and radius, **When** drawn, **Then** three great
   circles are emitted forming the sphere outline.

---

### Edge Cases

- Removing an entity that is already removed (or out of range) must fail loudly
  rather than corrupt the free list.
- Writing to a slot that was previously freed must restore it at the same index
  (this is what a future undo needs).
- Reading an out-of-range index must report "no entity", never throw.
- The debug-draw shapes must not allocate per frame; they reuse the same buffer
  as the grid.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a zone that holds fixed-capacity collections
  of actors and props, each addressable by a stable index.
- **FR-002**: The system MUST support O(1) add, read-by-index, write-by-index, and
  remove-by-index on an entity collection.
- **FR-003**: Removing an entity MUST make its index reusable without re-numbering
  other entities.
- **FR-004**: Reading a removed or out-of-range index MUST report "no entity".
- **FR-005**: Writing to a freed index MUST restore an entity at that same index.
- **FR-006**: The system MUST track the active transform tool (move/rotate/scale),
  a snap flag, and the state of an in-progress drag (axis + pre-drag transform).
- **FR-007**: The system MUST provide wireframe box and sphere primitives on the
  existing debug-draw line renderer.
- **FR-008**: A missing entity reference MUST be representable without a sentinel
  value (absence is expressed as "no reference", not a magic index).

### Key Entities

- **Zone**: the world container for one playable area; owns the actors and the
  environment (which owns the props).
- **Actor** / **Prop**: specialised entities that compose a shared base (name,
  transform, enabled/visible/selectable/draggable flags).
- **Environment**: the scene's environment entity plus its own props collection.
- **EntityRef**: a lightweight handle to one entity — its kind plus its index.
- **Transform**: position, rotation, and scale.
- **Tool**: the active transform tool (move, rotate, or scale).
- **GizmoAxis**: which axis (X/Y/Z) a drag is operating on.
- **Drag**: the recorded state of an in-progress transform drag.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Add, read, write, and remove on an entity collection are O(1) —
  no operation walks the collection or re-numbers existing entities.
- **SC-002**: The full entity-storage lifecycle (add/read/remove/reuse, plus
  restore-at-index) is covered by automated tests that pass.
- **SC-003**: The editor state and debug-draw primitives compile and run without
  per-frame allocation in the draw path.
- **SC-004**: No magic "no entity" sentinel value is used anywhere — absence is
  expressed through the language's optional type.

## Assumptions

- Waypoints and triggers are out of scope here; they are added in a later phase
  and will reuse the same index-based storage.
- The zone is the only container for now; a multi-zone registry arrives in a
  later phase.
- Index-based access intentionally omits a generation check (the editor clears
  selection on delete and a future undo stores the full value), matching the
  reference implementation's editor access path.
