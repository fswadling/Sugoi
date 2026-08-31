# Feature Specification: Command / Undo

**Feature Branch**: `008-command-undo`

**Created**: 2026-08-30

**Status**: Draft

**Input**: User description: "make every editor mutation undoable/redoable — spawn,
delete, duplicate, and transform — with a single undo step per gizmo drag"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Undo/redo spawn, delete, and duplicate (Priority: P1)

A developer edits the scene by spawning, deleting, and duplicating entities, and
can step backward and forward through those edits. Deleting an entity and then
undoing restores it exactly (same position, name, and settings); duplicating and
then undoing removes the copy.

**Why this priority**: it establishes the command core — the value-storing command,
the undo/redo history, and the keyboard entry points — that every later editor
mutation builds on. It is the minimum viable undo system.

**Independent Test**: spawn an entity, undo (it disappears), redo (it reappears);
delete an entity, undo (it is restored), redo (it is removed again); duplicate an
entity, undo (the copy is removed).

**Acceptance Scenarios**:

1. **Given** an empty scene, **When** the user spawns an actor/prop, **Then** undo
   removes it and redo restores it at the same place.
2. **Given** an entity in the scene, **When** the user deletes it, **Then** undo
   restores it with its prior name/transform/settings and redo deletes it again.
3. **Given** an entity in the scene, **When** the user duplicates it, **Then** undo
   removes the copy and leaves the original untouched.
4. **Given** a non-empty undo history, **When** the user undoes to the very first
   edit, **Then** the scene matches its initial state, and further undo is a no-op.
5. **Given** an undone edit, **When** the user performs a new edit, **Then** the
   redo history is cleared (no re-apply of a stale branch).

---

### User Story 2 - Undo/redo transform as a single gesture (Priority: P2)

A developer drags a translate/rotate/scale gizmo (or holds a keyboard nudge) and
can revert the entire gesture with a single undo, rather than one undo per frame
of the drag. The same applies to redo.

**Why this priority**: transform is the most frequent editor edit, and without
coalescing a single drag would flood the undo history with dozens of per-frame
steps, making undo useless. It depends on the command core from US1.

**Independent Test**: select an entity, drag a gizmo across many frames, and press
undo once — the entity returns to its pre-drag transform in one step; redo
re-applies the whole drag. Repeat for a held keyboard nudge.

**Acceptance Scenarios**:

1. **Given** a selected entity, **When** the user drags a translate/rotate/scale
   gizmo through many frames, **Then** a single undo restores the pre-drag
   transform.
2. **Given** a selected entity, **When** the user holds Shift+WASD/QE to nudge,
   **Then** the whole held nudge collapses into one undo step.
3. **Given** a coalesced transform, **When** the user redoes it, **Then** the final
   (post-gesture) transform is re-applied in one step.

---

### Edge Cases

- Undo with an empty history is a no-op.
- Redo with an empty redo history is a no-op.
- Undo/redo must not fire while a gizmo drag is in progress.
- Undo/redo must not fire while an ImGui text field has focus (typing is preserved).
- A new edit performed after undoing clears the redo history.
- Undo restoring a deleted entity does not automatically re-select it (selection
  stays cleared after the delete).
- Deleting an entity that is not currently selected (e.g. via the Outliner row
  button) leaves the current selection untouched.
- Transform changes must coalesce only for the same entity; interleaved edits to
  different entities must not merge.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The editor MUST record spawn, delete, duplicate, and transform edits
  as commands that store the values needed to apply and undo the change.
- **FR-002**: A command MUST capture the affected entity's identity (not a raw
  pointer) so undoing survives that entity's deletion and re-creation.
- **FR-003**: Undo MUST revert the most recent edit; redo MUST re-apply the most
  recently undone edit.
- **FR-004**: The user MUST be able to trigger undo and redo while editing, from
  the keyboard (`Ctrl+Z` undo, `Ctrl+Y` redo) and from an Edit menu.
- **FR-005**: Delete/duplicate/spawn/transform MUST each be independently
  undoable and redoable.
- **FR-006**: A continuous transform gesture (gizmo drag or held nudge) MUST
  coalesce into a single undo step.
- **FR-007**: Undo/redo MUST NOT mutate the scene while a drag is in progress or a
  text field has focus.
- **FR-008**: The editor MUST mutate the scene in place (the active zone is updated
  in memory), not by replacing the zone with a copy on each edit.

### Key Entities

- **Command**: a record of one editor mutation — its affected entity and the
  before/after values needed to apply and undo it (spawn/delete/duplicate store the
  entity; transform stores the before/after transforms).
- **Undo history**: an ordered stack of applied commands plus a stack of undone
  commands, with a merge rule for continuous edits to the same entity.
- **Entity identity**: a stable handle (kind + position in the scene) that remains
  valid across deletion, so undo can target a since-deleted entity.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every spawn, delete, duplicate, and transform edit can be reverted
  with a single undo and re-applied with a single redo.
- **SC-002**: A multi-frame gizmo drag or held nudge reverts with exactly one undo
  (no per-frame steps in the history).
- **SC-003**: Undo/redo never fires while dragging or typing.
- **SC-004**: Undoing through the entire history returns the scene to its initial
  state with no lost or corrupted entities.

## Assumptions

- The active zone is a single in-memory scene; there is no zone switching yet
  (that arrives with phase 8), so commands operate on the one active zone.
- Inspector field editing (renaming, toggling enabled, per-axis edits) is deferred
  to a later phase; this phase covers the structural edits (spawn/delete/duplicate)
  and transform, which the roadmap names as phase 2d's scope.
- Undo/redo is editor-only (it does not apply while playing).
- The undo history is per-session (in memory) and is not persisted.
