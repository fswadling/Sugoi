# Feature Specification: Entity Reference Integrity

**Feature Branch**: `015-entity-ref-integrity`

**Created**: 2026-09-12

**Status**: Draft

**Input**: User description: "Harden entity references against slot reuse and environment churn, scope environment parts to their environment, and centralise undo-paired entity creation"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A stale reference never acts on a different entity (Priority: P1)

A developer deletes an entity (an actor or a placed prop) and then creates another
one, which reuses the freed position. Any lingering reference to the deleted
entity — the editor's selection, an undo command, or a physics mapping — must not
act on the newcomer. The editor treats the old reference as absent, and the new
occupant is left untouched.

**Why this priority**: deleting and re-creating entities is routine editing, and
without this guarantee a stale handle silently edits or deletes the wrong entity.
It is the foundation every other story builds on.

**Independent Test**: delete an actor, spawn a new actor that reuses the freed
position, then attempt to transform and delete via the old reference; confirm both
are inert, the new actor is unchanged, and a fresh reference to it works.

**Acceptance Scenarios**:

1. **Given** an entity has been deleted, **When** another entity reuses its
   position, **Then** a reference to the deleted entity resolves to nothing.
2. **Given** a stale reference, **When** the editor applies a transform or delete
   through it, **Then** it is a no-op and the current occupant is unchanged.
3. **Given** the new occupant, **When** a reference is taken for it, **Then** that
   reference resolves to the new entity.

---

### User Story 2 - Environment parts are scoped and listed per environment (Priority: P2)

A developer loads an environment, and its imported parts appear in the outliner as
"parts" that can be selected and edited like other entities. Loading a different
environment swaps only that environment's parts: placed props and actors are left
untouched, and a reference to a part from the previous environment resolves to
nothing rather than acting on a same-position part in the new one.

**Why this priority**: environments are swapped frequently. Without scoping, a swap
either destroys placed content (because parts share its store) or makes stale part
references address the wrong geometry.

**Independent Test**: load environment A and note its parts; load environment B;
confirm A's part references resolve to nothing, B's resolve, placed props/actors
are unchanged, and the outliner shows a Parts section for the current environment.

**Acceptance Scenarios**:

1. **Given** an environment with imported parts is loaded, **When** the outliner is
   shown, **Then** those parts are listed under a Parts section.
2. **Given** a loaded environment, **When** a different environment is loaded,
   **Then** placed props and actors are unchanged and the previous environment's
   part references resolve to nothing.
3. **Given** a part is selected, **When** the environment is swapped, **Then** the
   selection is dropped rather than left pointing at a non-existent entity.
4. **Given** an environment swap, **When** it is undone, **Then** the original
   environment's parts resolve again.

---

### User Story 3 - Undo restores the exact entity without clobbering (Priority: P3)

Undoing a delete re-creates the entity with the same identity, so references that
named it address it again; redoing removes it once more. If a restore would
overwrite a *different* live entity that reused the position, the editor reports
an error instead of silently replacing it. Every entity the editor creates is
paired with the command that reverses it, so the undo history is complete.

**Why this priority**: it extends the existing undo system across deletes and
environment swaps and guarantees that undo cannot corrupt the scene.

**Independent Test**: delete an entity and undo — it is restored and
re-addressable; redo — it is removed. Spawn then undo/redo round-trips. Restoring
onto an occupied position reports an error and leaves the occupant intact.

**Acceptance Scenarios**:

1. **Given** a deleted entity, **When** the delete is undone, **Then** the entity is
   restored such that the same reference resolves to it; redo deletes it again.
2. **Given** a spawn or duplicate, **When** it is undone and redone, **Then** it is
   removed and re-created with the same identity.
3. **Given** an environment load, **When** it is undone and redone, **Then** the
   previous environment returns with its part references live, and the load is
   re-applied.
4. **Given** a deleted entity whose position is now occupied by a different live
   entity, **When** the delete is undone, **Then** an error is reported and the
   occupant is unchanged.

---

### Edge Cases

- Deleting the currently selected entity clears the selection; undoing the delete
  does not automatically re-select it.
- Undo with an empty history, or redo with nothing undone, is a no-op.
- Loading an environment when none is loaded, and clearing a loaded environment,
  both behave as ordinary swaps.
- Duplicating an entity kind that has no duplicable entity fails loudly.
- Continuous transform edits must merge only when they target the same entity
  *incarnation*, so a position reused by a later entity cannot merge across two
  different entities.
- A part reference used while a different environment is loaded is inert for
  transform/delete, and is reported as an error for a restore.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An entity reference MUST identify the specific incarnation of the
  entity (its position plus a marker that changes on delete), so a reference to a
  deleted entity never resolves to one that later reuses its position.
- **FR-002**: Reading, transforming, or deleting through a reference MUST validate
  it, and be inert (or report an error) when it no longer names a live entity.
- **FR-003**: The editor MUST list the current environment's imported parts
  alongside actors and placed props.
- **FR-004**: Environment parts MUST belong to the environment that produced them,
  scoped so a part reference resolves only against its own environment.
- **FR-005**: Loading or swapping an environment MUST NOT alter placed props or
  actors.
- **FR-006**: A reference to a part of a non-current environment MUST NOT read or
  mutate a part in the current environment, even at the same position.
- **FR-007**: Selecting an environment part and then swapping environments MUST
  drop the now-unresolvable selection rather than show a dangling entity.
- **FR-008**: Undo of a delete MUST restore the entity at its recorded identity so
  existing references address it again.
- **FR-009**: Undo and redo of an environment swap MUST restore the previous
  environment (with its part references live) and re-apply the swap.
- **FR-010**: Restoring a deleted entity onto a position now occupied by a
  different live entity MUST report an error rather than overwrite it.
- **FR-011**: Every entity the editor creates MUST be paired with the command that
  reverses it, so the undo history stays complete.
- **FR-012**: Continuous transform edits MUST coalesce only when they target the
  same entity incarnation.

### Key Entities *(include if feature involves data)*

- **Entity reference**: a lightweight handle identifying an entity by kind,
  position, incarnation marker, and — for environment parts — the environment that
  owns it.
- **Zone**: the world container holding actors, placed props, and the current
  environment.
- **Environment**: a loaded static scene that owns its imported parts and is
  identified so its parts can be scoped.
- **Entity store**: fixed-capacity storage that reuses freed positions while
  distinguishing the incarnations that occupy them over time.
- **Undo command / history**: a record of one editor mutation and the values needed
  to apply and reverse it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In every delete-then-reuse scenario, the new occupant is left
  completely unchanged by operations through the stale reference (100%).
- **SC-002**: Swapping environments leaves the count and identity of placed props
  and actors unchanged (100%).
- **SC-003**: A deleted entity can be undone so that its original reference resolves
  to it again, with no need to re-select or re-create it.
- **SC-004**: A restore onto an occupied position reports an error in 100% of cases;
  it never silently overwrites the occupant.
- **SC-005**: Every entity creation action is reversible in exactly one undo step
  and re-appliable in exactly one redo step.
- **SC-006**: The editor displays the loaded environment's parts in the outliner.

## Assumptions

- There is a single active zone; references are in-memory and are not persisted
  across sessions.
- Undo/redo is editor-only and does not run during play.
- Entity stores are fixed-capacity for a given kind; capacity is only changed in
  code.
- Environment parts reuse the same representation and editing surface as placed
  props.
- "Report an error" means a loud, developer-visible failure rather than a silent
  default, consistent with the project's Fail Loudly principle.
