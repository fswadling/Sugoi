# Feature Specification: Entity Rename

**Feature Branch**: `027-entity-rename`

**Created**: 2026-09-19

**Status**: Draft

**Input**: User description: "Edit an entity's name in the inspector. The inspector's name row becomes a text field for every entity kind the inspector can show (actors, props, environment parts, and waypoints). A committed rename is a single undoable step, an empty name is never committed, and the new name shows immediately wherever the entity appears. Names are display labels rather than identities, so duplicates are allowed and nothing else about the entity changes."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Rename the selected entity in the Inspector (Priority: P1)

A developer selects an entity — an actor, a placed prop, an environment part, or a
waypoint — and edits its name in the Inspector. The field shows the current name;
typing a new one and pressing Enter, or clicking away, commits it. The entity now
carries the new name everywhere it is shown, the change is one undo step, and
undo and redo move between the old and new names.

**Why this priority**: this is the whole feature. Everything else in this spec is
about the edges of this interaction.

**Independent Test**: select an entity, rename it, and confirm the new name is
shown in the Inspector and in the entity's row in the Outliner; undo and confirm
both revert to the previous name; redo and confirm both show the new one again.
Repeat for an actor, a prop, a part, and a waypoint.

**Acceptance Scenarios**:

1. **Given** a selected entity, **When** the developer opens the Inspector,
   **Then** its name is shown in a text field rather than as read-only text.
2. **Given** the name field is focused, **When** the developer types a new name
   and presses Enter, **Then** the entity's name is the new one and the change is
   one undo step.
3. **Given** the name field is focused with edited text, **When** the developer
   clicks elsewhere in the workspace, **Then** the edit is committed the same way
   as Enter.
4. **Given** a committed rename, **When** the developer undoes, **Then** the
   entity's name reverts and the field shows the reverted name; **when** they
   redo, the new name returns.
5. **Given** any entity kind the Inspector can show — actor, prop, part, or
   waypoint — **When** the developer renames it, **Then** the behaviour is
   identical for each kind.
6. **Given** a rename is committed, **Then** the name field shows the new name
   at once, and every other surface that displays that entity's name shows it by
   the next frame it is drawn.

---

### User Story 2 - Move between entities without the name field leaking text (Priority: P2)

The developer renames one entity, then selects a different one and renames that,
and neither entity picks up the other's text. An edit that was never committed
does not become anybody's name; an empty or whitespace-only name is never
committed. An edit that was in progress on one entity is never applied to a
different one, even if the selection changes while the field still has focus.

**Why this priority**: this is what makes the feature trustworthy rather than
merely present. A rename that lands on the wrong entity is worse than no rename
at all, and it is the failure this interaction is most prone to, because one
reused text field serves every selected entity.

**Independent Test**: type a new name for one entity without committing, then
select another entity and confirm the field shows the second entity's own name;
clear a name to empty, click away, and confirm the entity keeps its previous
name; rename a sequence of entities and confirm each keeps exactly the name that
was typed for it.

**Acceptance Scenarios**:

1. **Given** a name has been typed but not committed, **When** the developer
   selects a different entity, **Then** the field shows the newly selected
   entity's own name and the abandoned text is not applied to it.
2. **Given** the name field has focus with edited text, **When** the developer
   selects a different entity without pressing Enter, **Then** the edited text is
   committed to the entity it was typed for, not to the newly selected one.
3. **Given** the name field is emptied (or holds only spaces), **When** the
   developer presses Enter or clicks away, **Then** no rename is committed and
   the entity keeps its previous name.
4. **Given** an entity whose name is unchanged, **When** the developer focuses
   the field and clicks away without typing, **Then** no rename is committed and
   no undo step is added.
5. **Given** the selection changes while the field is unfocused, **When** the
   Inspector redraws, **Then** the field shows the newly selected entity's name.

---

### User Story 3 - A rename changes nothing else about the entity (Priority: P3)

Renaming is a label change. The entity keeps its position, rotation, and scale,
keeps colliding and rendering exactly as before, keeps its asset, and stays
selected. Two entities may share a name, because a name is not how anything
addresses an entity.

**Why this priority**: it draws the boundary of the feature and pins down the one
question a rename field invites — "does a name have to be unique?" Adding and
editing entities already works without it; this story exists so that neither
question is left to an implementation accident.

**Independent Test**: rename an actor and confirm its transform and its capsule
are unchanged and that it still collides and renders; give two props the same
name and confirm both exist, are separately selectable, and are separately
editable; rename an entity, then delete it and confirm undoing the delete
restores it with the renamed value.

**Acceptance Scenarios**:

1. **Given** a renamed entity, **When** it is inspected, **Then** its transform,
   asset, and collider are exactly as before the rename.
2. **Given** two entities with the same name, **When** the developer selects and
   edits either, **Then** each is addressed separately and neither edit affects
   the other.
3. **Given** a renamed entity, **When** the developer switches zones and back,
   **Then** the entity is still listed under its new name.
4. **Given** a renamed entity, **When** the developer deletes it and undoes the
   deletion, **Then** it is restored with the renamed value and is still
   selectable and editable.
5. **Given** a reference to an entity that no longer exists (deleted, or its slot
   reused), **When** a rename is attempted, **Then** nothing changes and no
   entity is created or altered.

---

### Edge Cases

- An entity whose name is at or near the buffer limit: a very long name is
  limited to what the field can hold, and never truncated mid-character.
- The name field with a name containing spaces: spaces are ordinary characters;
  only leading and trailing whitespace is not preserved.
- An entity deleted while its name was being edited: the pending edit is dropped
  (there is nothing to rename), and selecting another entity afterwards is
  unaffected.
- Undo or redo while the field is unfocused: the field shows the entity's current
  name, so an undone rename is visible in the field as well as the Outliner.
- A zone switch while an entity's name was being edited: the edit belongs to the
  zone that owns the entity, and the field shows the newly active zone's
  selection (or nothing, as selection is cleared on a switch).
- Typing in the field must not be read as editor shortcuts: a name containing
  `f`, `3`, `q`, or `Delete` must neither focus the camera, switch the tool,
  toggle snapping, nor delete the entity.
- The environment itself and triggers have no editable name: the environment has
  no base entity and triggers are not selectable, so neither can appear in the
  Inspector.
- Zone names are edited in the Outliner (their own field) and are unaffected by
  this feature.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Inspector MUST present the selected entity's name as an
  editable text field, for every entity kind the Inspector can show (actor,
  placed prop, environment part, and waypoint).
- **FR-002**: A committed rename MUST be exactly one undoable step, and MUST be
  redoable, returning to the previous name and back.
- **FR-003**: A rename MUST be committed when the developer submits the field
  (Enter) or leaves it with uncommitted changes (clicking away), and MUST NOT be
  committed while they are still typing.
- **FR-004**: An empty name, or one containing only whitespace, MUST NOT be
  committed: the entity keeps the name it had.
- **FR-005**: A rename MUST be applied to the entity whose name was being edited.
  Text typed for one entity MUST NOT be applied to another, including when the
  selection changes while the field still has focus.
- **FR-006**: The field MUST display the selected entity's current name, and MUST
  show it after a rename is undone, after a different entity is selected, and
  after the active zone changes.
- **FR-007**: A name MUST be a display label, not an identity: the same name MUST
  be allowed on more than one entity, and no behaviour MUST depend on a name
  being unique.
- **FR-008**: Committing a rename MUST leave everything else about the entity
  unchanged — its transform, its asset, its collider and physics body, its
  renderer, and its selection state. A rename MUST NOT create, destroy, move, or
  simulate anything.
- **FR-009**: A rename addressed to an entity that no longer exists MUST be a
  no-op, and MUST NOT resurrect it, create anything, or alter any other entity.
- **FR-010**: A committed name MUST be visible in every surface that displays
  that entity's name, by the next frame that surface is drawn at the latest. The
  name field itself MUST show it at once, so the field and the entity's row never
  disagree for longer than a frame.
- **FR-011**: A committed name MUST NOT keep leading or trailing whitespace.
- **FR-012**: While the name field has focus, the editor's keyboard shortcuts
  MUST NOT act: typing a name MUST NOT change the selection, delete or duplicate
  an entity, change the tool or snapping, focus the camera, or nudge the entity.

### Key Entities *(include if feature involves data)*

- **Entity name**: the human-readable label of an entity, shown in the Inspector
  and in the entity's Outliner row. It is not an identifier: entities are
  addressed by reference, names need not be unique, and nothing resolves an
  entity by name.
- **Rename**: one committed change of one entity's name, with the name it had
  before and the name it now has — the unit of undo.
- **Name field**: the Inspector's single editable name control, which follows the
  selection; it holds at most one entity's edit at a time and is a plain text
  field, not a dialog.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can rename any entity kind the Inspector shows and
  undo and redo that rename, entirely from the editor (100% of kinds, 100% of
  renames).
- **SC-002**: An empty or whitespace-only name never becomes an entity's name
  (0 occurrences across any sequence of edits).
- **SC-003**: A rename never lands on an entity other than the one it was typed
  for, including when the selection changes mid-edit (0 misapplied renames).
- **SC-004**: After a rename, the name field shows the new name at once and
  every other surface showing it does so by the next frame drawn (100% — no stale
  name persists anywhere).
- **SC-005**: Renaming leaves the entity's behaviour identical: same transform,
  same collision, same rendering, same asset; repeated renames add no physics
  bodies and no GPU resources (0 growth).
- **SC-006**: Duplicate names are accepted and both entities remain separately
  selectable and editable (100%).
- **SC-007**: The editor sustains the project's target frame rate (60 FPS on the
  target platform) with the name field in use, and the existing behaviour of
  every other feature is unchanged (the existing test suite still passes).

## Assumptions

- "Entity" here means the kinds the Inspector can show: actors, placed props,
  environment parts, and waypoints. The environment and triggers are not
  renameable because they have no base entity or cannot be selected; zone names
  are already editable in the Outliner and are out of scope.
- Renaming happens in the Inspector only. Outliner rows keep selecting on click;
  they are not made inline-editable.
- Names need not be unique, so no validation beyond "not empty" is required, and
  duplicating an entity may produce two with the same name.
- A name is a label for the author. Nothing consumes it yet (no scene file, no
  cue, no lookup by name), so changing it cannot break a reference.
- Renames are in-memory, like the rest of the scene; persistence is a later
  phase.
- A rename is a scene edit, so it is undoable, matching transform edits; the
  entity's *play binding* and its collider-visualisation toggle are not edits and
  remain unundoable.
- The length of a name is bounded by the field's buffer, which is ample for
  author-chosen labels; no per-name size limit is imposed beyond that.
