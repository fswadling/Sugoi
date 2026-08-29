# Feature Specification: Entity Store

**Feature Branch**: `002-entity-store`

**Created**: 2026-08-28

**Status**: Draft

**Input**: User description: "Generational slot map and entity model for data-oriented entity storage"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Store and retrieve entities in constant time (Priority: P1)

A system needs to add entities to a fixed-capacity store, retrieve them by a
lightweight handle, and free them — all in O(1) — so per-frame code can look up
and mutate entities without heap allocation or index churn.

**Why this priority**: the slot map is the foundational container; every later
phase (zones, actors, props, cues) stores entities in one. Without it, nothing
else can be built.

**Independent Test**: a unit test adds entities, retrieves them, frees some, and
adds more, asserting each add/get/free completes in constant time and the count
is correct.

**Acceptance Scenarios**:

1. **Given** an empty store, **When** an entity is added, **Then** a valid handle
   is returned and the store's count increases by one.
2. **Given** a handle, **When** the entity is retrieved, **Then** the stored
   entity is returned.
3. **Given** a handle, **When** its entity is freed, **Then** the slot becomes
   available for reuse and the count decreases by one.
4. **Given** slots have been freed, **When** a new entity is added, **Then** a
   freed slot is reused without moving or reindexing surviving entities.

---

### User Story 2 - Detect stale handles after reuse (Priority: P2)

When a slot is freed and later reused by a new entity, any code still holding the
old handle must not silently reach the new entity.

**Why this priority**: stale handles are a real correctness bug in a data-driven
engine; generation tracking is what makes the slot map safe under reuse.

**Independent Test**: a unit test frees an entity, reuses its slot, and asserts
the old handle no longer resolves.

**Acceptance Scenarios**:

1. **Given** a handle, **When** its entity is freed and the slot is reused, **Then**
   resolving the old handle fails (does not return the new entity).
2. **Given** a current handle, **When** it is resolved, **Then** it returns its
   entity (the generation still matches).

---

### User Story 3 - Model entity kinds and a shared base (Priority: P3)

Entities are categorised (actor, environment, prop, waypoint, trigger) and share
a common base (name, transform, visibility, selection flags), with specialised
entities composing the base rather than duplicating it.

**Why this priority**: the entity model is the vocabulary the rest of the engine
uses; it must exist before zones/actors/props are built on top of it.

**Independent Test**: a compile-and-run test constructs each entity kind and
asserts the shared base fields are present and initialised.

**Acceptance Scenarios**:

1. **Given** the entity model, **When** a specialised entity is created, **Then**
   it carries the shared base fields plus its own fields.
2. **Given** the entity model, **When** a handle is created, **Then** it records
   both the entity's kind and its index.

---

### Edge Cases

- Adding to a full store must fail loudly (not silently overwrite).
- Resolving a handle that was never issued must fail.
- The "no entity" sentinel must be distinguishable from every valid handle.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a fixed-capacity store of entities with
  O(1) add, get, and free.
- **FR-002**: The system MUST reuse freed slots for subsequent adds without
  moving or reindexing live entities.
- **FR-003**: The system MUST reject stale handles (a handle whose slot has been
  freed and reused) rather than resolving them to a different entity.
- **FR-004**: The system MUST expose a sentinel value representing "no entity".
- **FR-005**: The system MUST distinguish entity kinds (actor, environment, prop,
  waypoint, trigger).
- **FR-006**: The system MUST provide a shared entity base with the common
  fields (name, transform, enabled, visible, selectable, draggable) and allow
  specialised entities to compose it.
- **FR-007**: The system MUST fail loudly (raise) on out-of-capacity adds and
  invalid handles.

### Key Entities

- **Slot Map**: a fixed-size array of slots plus a free list and generation
  tracking; the single container that stores all entities of a collection.
- **Slot**: a storage cell holding an entity, a generation, an alive flag, and a
  free-list link.
- **Entity Handle**: a lightweight reference (kind + index) used to look up an
  entity; not a pointer.
- **Entity**: the shared base record (name, transform, enabled, visible,
  selectable, draggable).
- **Entity Kind**: the category discriminator (actor, environment, prop,
  waypoint, trigger).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Add, get, and free each complete in O(1) time (constant, independent
  of store size).
- **SC-002**: Freed slots are reused such that no live entity is moved or
  reindexed on a free/add.
- **SC-003**: 100% of stale-handle resolutions are rejected (verified by tests).
- **SC-004**: Each entity kind compiles and composes the shared base without
  duplicating the base fields.

## Assumptions

- The store is fixed-capacity (sized up front); dynamic growth is out of scope
  for this milestone.
- Iteration is by direct scan of the slot array (exposed to systems), not via an
  iterator abstraction.
- Persistence and serialization of entities are out of scope (later phase 15).
- Waypoints and triggers are standalone (no shared entity base); actors,
  environments, and props compose the base.
