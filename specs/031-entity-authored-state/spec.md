# Feature Specification: Entity Authored State

**Feature Branch**: `031-entity-authored-state`

**Created**: 2026-09-20

**Status**: Draft

**Input**: User description: "Give each entity kind's authored state a record of its own, nested in the entity, so the boundary between authored state and derived resources is expressed by the types rather than maintained by a capture step that clears derived fields and a restore that rebuilds them."

**Origin**: the review of `specs/030-entity-command-families`. That feature made
each kind's commands carry its own record, but the boundary between what is
*recorded* and what is *rebuilt* is still maintained by hand in two places that
must agree: a capture that copies the live entity and clears its derived fields,
and a restore that takes the record field by field and rebuilds them. This feature
makes that boundary a property of the entity types instead, so neither function has
to remember it. It was chosen over the alternative of keeping the records flat and
holding the derived resources beside each kind's store in the zone: that alternative
measured at roughly half the read-site churn but makes the resources reachable by an
index that carries no generation, so every access would have to prove liveness
against the authored store first — a per-site obligation of exactly the kind the
project's reference handles exist to remove.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An entity's authored state is a value of its own (Priority: P1)

A developer reads, records, restores, and copies an entity's authored state as a
single value: everything the editor authors for that kind, and nothing else. A
command that creates, deletes, or copies an entity carries that value, so nothing
derived — a physics attachment, a rendering resource — and nothing runtime can be
part of what a command remembers.

**Why this priority**: this is the boundary that is maintained by hand today, in a
capture step and a restore that must mirror it. Every other outcome here follows
from expressing it once, in the types.

**Independent Test**: for each entity kind, record it for a delete and confirm the
recorded value holds every authored property and no body, renderer, or runtime flag;
then delete and undo and confirm the entity returns with every authored property
unchanged and fresh resources.

**Acceptance Scenarios**:

1. **Given** any entity, **When** the editor records it for a deletion, **Then**
   the recorded value carries its authored properties and no physics attachment,
   rendering resource, or runtime flag.
2. **Given** a deleted entity, **When** the deletion is undone, **Then** the entity
   returns with every authored property unchanged and its resources newly built.
3. **Given** an entity, **When** it is duplicated, **Then** the copy carries the
   original's authored properties, references included, and no runtime state.
4. **Given** a created or deleted entity, **When** the operation is redone, **Then**
   the result matches what the first application produced.

---

### User Story 2 - A new field is classified by the compiler (Priority: P2)

A developer adds a property to an entity kind. If it is authored, everything that
records that kind carries it with no further edit. If it is derived or runtime, the
build fails wherever a live entity is produced from an authored value, until the
developer produces it. There is no third outcome in which the field is silently
dropped from a record or silently restored with a stale value.

**Why this priority**: it is what stops the boundary decaying, and it is stronger
than the arrangement it replaces, which depended on a hand-written clear step being
kept in step with a hand-written rebuild.

**Independent Test**: add an authored property and confirm it appears in what a
deletion records with no edit outside the authored value; add a derived one and
confirm the build fails until it is produced.

**Acceptance Scenarios**:

1. **Given** a kind's authored value, **When** a property is added to it, **Then**
   everything that records that kind carries it, with no edit to the recording path.
2. **Given** an entity kind, **When** a derived resource is added to it, **Then**
   the solution fails to build until something produces that resource.
3. **Given** an entity kind, **When** it is created or destroyed, **Then** no path
   exists that records less than that kind's authored value.

---

### User Story 3 - No code maintains the boundary by convention (Priority: P3)

The step that copies a live entity and clears its derived fields is gone, and with it
the risk that it drifts from the restore it mirrors. A live entity is produced from
an authored value in exactly one place per kind. The resource clearing that remains —
when a zone's environment is unloaded — stays because the environment genuinely is
unloaded, not because a record needed to be made safe.

**Why this priority**: it is the maintainability payoff rather than the guarantee.
US1 and US2 are what must hold; this is the machinery that used to hold them going
away.

**Independent Test**: read the recording path and confirm no step copies a live
entity and clears derived fields; swap an environment out and back and confirm its
parts are torn down and rebuilt exactly as today.

**Acceptance Scenarios**:

1. **Given** the path that records an entity, **When** a developer reads it, **Then**
   there is no step that copies a live entity and clears its derived fields.
2. **Given** the environment lifecycle, **When** an environment is swapped out and
   back, **Then** its parts are torn down and rebuilt exactly as they are today.

---

### Edge Cases

- A kind whose record is already pure authored state — a waypoint, which is a base
  entity plus the reference of the waypoint its chain continues at — needs no split
  and keeps its present shape.
- Runtime state is not part of the authored value either, so a restore cannot put
  back the value an entity happened to have when it was recorded.
- The simulation writes an actor's transform in place from its physics body. That is
  *authored* state written by a system rather than by the editor, not derived state: it
  is the case where a write through an authored property has to be spelled out as a
  whole-record update, because `State` is an immutable field.
- An authored value may contain references to other entities, and they are authored
  state: a waypoint's link is carried by a copy and goes stale rather than pointing
  elsewhere when its target is deleted.
- Copying carries every authored property and no runtime state.
- A zone's environment: its parts' resources are still torn down when it is unloaded
  and rebuilt when it is activated, because the environment is unloaded rather than
  because a record needed to be safe.
- A derived resource that nothing produces must be caught when the solution is built,
  not when the entity is first restored.
- The zone still holds one store per kind, and no per-frame path gains a check of
  which kind an entity is.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each entity kind that carries any state that is not authored — a derived
  resource or a runtime flag — MUST express its authored state as a value of its own,
  holding exactly the properties the editor authors for that kind.
- **FR-002**: Such a value MUST NOT be able to hold a physics attachment, a rendering
  resource, or runtime state.
- **FR-003**: An operation that creates, removes, or copies an entity MUST carry that
  entity's authored value, so nothing derived or runtime can be recorded.
- **FR-004**: Producing a live entity from an authored value MUST happen in exactly
  one place per kind, and it MUST produce every derived resource.
- **FR-005**: There MUST be no step whose purpose is to make an entity safe to record
  by clearing its derived fields. Recording an entity MUST be reading its authored
  value.
- **FR-006**: Adding an authored property to a kind MUST flow into everything that
  records or restores that kind with no edit to the recording or restoring path — the
  only place that must be told is where a new entity's initial value is decided — and
  adding a derived resource MUST fail the build until it is produced.
- **FR-007**: Every existing behaviour MUST be unchanged: delete, undo, redo,
  duplicate, link, transform, rename, trigger property edits, zone switch, and the undo
  granularity of each; the environment lifecycle is FR-011. (Preserves
  `specs/030-entity-command-families` FR-001 to FR-015.)
- **FR-008**: Each entity kind MUST keep its own separate storage, and no per-frame
  path may gain a check of which kind an entity is. (Preserves 030's FR-010.)
- **FR-009**: The feature MUST NOT change physics, rendering, or gameplay behaviour.
- **FR-010**: Runtime state MUST remain mutable and recomputed in place, as it is
  today — a trigger's detection flag, an actor's advancing animation.
- **FR-011**: The environment lifecycle MUST be unchanged: swapping an environment
  still tears down the outgoing parts' resources and rebuilds the incoming ones.

### Key Entities *(include if feature involves data)*

- **Authored value**: the properties of an entity of a given kind that the editor
  authors and that must survive undo, redo, duplication, and deletion. It excludes
  everything derived and everything runtime.
- **Derived resource**: state computed from an authored value and owned by the
  engine — a physics attachment, a rendering resource. Rebuilt from the authored
  value, never recorded and never carried across a deletion.
- **Runtime state**: state recomputed while playing. Never recorded, never restored.
- **Entity kind**: actor, placed prop, environment part, waypoint, trigger — each
  with its own properties and its own separate storage. The set is small and stable,
  and this feature does not merge them. A zone's environment is the container rather
  than one of these kinds: it is not addressed by a reference and no entity operation
  records it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every entity kind, the value a create, delete, or copy operation
  carries contains no physics attachment, rendering resource, or runtime flag
  (100%; verifiable by reading the type, not by inspecting a value).
- **SC-002**: No code path clears derived fields in order to make something recordable
  (0 occurrences).
- **SC-003**: The existing suite passes with every behaviour test unchanged, including
  the round-trip exactness of every kind, the link travelling with a waypoint, a copy
  carrying it, and the transform-merge undo granularity (100%).
- **SC-004**: Adding an authored property to a kind requires no edit to the recording
  or restoring paths (only to wherever a new entity's initial value is decided), and
  adding a derived resource fails the build until it is produced (100%).
- **SC-005**: No per-frame path gains work: the systems still scan their own kind's
  store with no kind check, and the project's frame-rate target is unaffected.
- **SC-006**: Physics, rendering, and gameplay behaviour are unchanged: the behaviour
  tests pass unmodified.

## Assumptions

- **The "user" is the developer working in this codebase.** This is a
  behaviour-preserving refactor: no editor-visible behaviour changes, and the value
  is that the boundary between authored and derived becomes structural.
- **Only kinds that carry derived or runtime state are split.** A kind whose record
  is already pure authored state keeps its shape, so the change is confined to the
  kinds that need it.
- **How an authored read is spelled is a plan decision.** The requirement is that the
  recorded value *is* the authored one; how a read of an authored property is spelled at
  each call site is a design decision for the plan. The plan first kept reads spelled as
  they were, through accessors, and then qualified every one of them with `State`; either
  satisfies this spec.
- **The environment's part teardown stays.** It exists because the environment is
  unloaded, not to make a record safe; it is a lifecycle behaviour, and FR-011 keeps
  it exactly as it is.
- **Alternative considered and rejected**: keeping the entity records flat and the
  derived resources beside each kind's store in the zone. It measured at roughly half
  the read-site churn, but the resources would be reachable by an index carrying no
  generation, so every access would have to prove liveness against the authored store
  first.
- **No persistence change.** A zone's contents remain in memory only, so no recorded
  operation needs migrating and stored data needs no conversion.
- **Scope is the entity records and the operations that carry them.** The editor's
  surfaces, its interaction model, and its values are untouched *by this feature*. The one
  change the same working tree makes to the interaction model is the name-attribution
  correction recorded in `tasks.md`, and it is not part of this scope.
