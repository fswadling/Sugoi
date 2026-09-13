# Feature Specification: Multiple Zones

**Feature Branch**: `024-multiple-zones`

**Created**: 2026-09-13

**Status**: Draft

**Input**: User description: "Phase 8 (zones) of the roadmap: multiple zones and switching between them at runtime. A runtime container holds several independent zones with one active zone; only the active zone is rendered, edited, and simulated. A developer can add, select, and delete zones from a section in the outliner, and each zone keeps its own contents, environment, player designation, and undo history."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Add, select, and delete zones (Priority: P1)

A developer working in the editor opens the outliner's zones section, adds a
second zone by typing a name for it, and selects it as the active zone. The
workspace — viewport, outliner, and inspector — now shows the new, empty zone.
Selecting the first zone again brings it back exactly as it was left. The developer
can also delete a zone; the last remaining zone cannot be deleted, so the project
always has an active zone.

**Why this priority**: this is the whole feature. Without more than one zone and
a way to add, select, and remove them, nothing else in the phase has meaning.

**Independent Test**: add a zone and confirm the workspace shows it as an empty
zone; add a prop to each zone, switch back and forth and confirm each zone shows
only its own prop; delete a zone and confirm it and its contents are gone while
the remaining zones are unchanged.

**Acceptance Scenarios**:

1. **Given** a project with one zone, **When** the developer adds a zone by typing a
   name, **Then** a second empty zone with that name exists and becomes the active
   zone.
2. **Given** the developer has typed a name that is empty or already used,
   **When** they view the zones section, **Then** the add control is unavailable
   and no zone is created.
3. **Given** two zones, **When** the developer selects the other zone,
   **Then** the workspace renders, inspects, and edits that zone.
4. **Given** a zone containing actors, props, and an environment, **When** the
   developer switches away and back, **Then** all of its contents are exactly as
   they were left.
5. **Given** several zones, **When** the developer deletes an inactive zone,
   **Then** it and its contents are removed and the active zone and camera are
   unchanged.
6. **Given** the active zone with another zone present, **When** the developer
   deletes it, **Then** another zone becomes active and the deleted zone's contents
   are gone.
7. **Given** exactly one zone, **When** the developer views the zones section,
   **Then** its delete control is unavailable and the zone is not deleted.
8. **Given** several zones, **When** the developer views the zones section,
   **Then** the active zone is visually distinguished from the inactive ones.

---

### User Story 2 - Zones are isolated worlds (Priority: P2)

Each zone is its own world: its own environment, actors, props, and physics.
Only the active zone is rendered, simulated, and collidable; the others lie
dormant and cannot interfere with it. Loading an environment loads it into the
active zone alone.

**Why this priority**: isolation is what makes zones meaningful rather than just
several buffers, but switching alone (US1) already delivers the visible value.

**Independent Test**: give two zones different environments with overlapping
placement, confirm only the active zone's geometry renders and collides, then
switch and confirm the other.

**Acceptance Scenarios**:

1. **Given** a zone with a loaded environment, **When** another zone becomes
   active, **Then** the first zone's geometry no longer renders and does not
   collide.
2. **Given** the active zone, **When** the developer loads an environment,
   **Then** it is loaded into the active zone and no other zone is changed.
3. **Given** two zones with entities at the same position, **When** the world is
   simulated, **Then** only the active zone's entities take part.

---

### User Story 3 - Each zone designates its own player (Priority: P3)

Each zone designates one of its actors as the player. Switching to a zone with a
designated player and entering play drives that actor; switching to a zone with
none drives nothing. Switching also re-frames the camera on the incoming zone so
the view is not stranded in the zone just left.

Only the active zone's designation is ever live. Inactive zones hold their
designation as dormant data alongside their actors, props, and environment, and
the play systems ignore it.

**Why this priority**: it closes the loop with the walk-around phase so every
zone is playable on its own, but adding and switching zones is already useful
without it.

**Independent Test**: designate a different player in each of two zones, switch
between them, enter play, and confirm the correct actor is driven each time and
none is driven where there is no designation.

**Acceptance Scenarios**:

1. **Given** a zone with a designated player, **When** it is active and play is
   entered, **Then** that actor is driven and no other zone's actor is.
2. **Given** a zone with no designated player, **When** it becomes active and
   play is entered, **Then** no character is driven.
3. **Given** a zone is switched to, **When** it becomes active, **Then** the
   camera frames its designated player when one exists, otherwise the zone's
   origin.

---

### Edge Cases

- Adding a zone with a name that is empty or already used: the editor's add
  control is unavailable, so the action cannot be triggered; the container still
  rejects the call if it is made directly.
- Deleting the last remaining zone: the editor's delete control is unavailable for
  it, so the action cannot be triggered; the container still rejects the call if it
  is made directly, so the project always keeps one active zone.
- Adding a zone while the active zone has unsaved in-memory edits: the existing
  zone is left untouched.
- Deleting the active zone while another exists: another zone becomes active first
  (camera re-framed), then the deleted zone's contents and resources are released.
- Deleting an inactive zone: the active zone is unchanged and the camera does not
  move.
- A deleted zone's bodies and GPU resources are released, and its undo history is
  discarded; deletion is not undoable, and deleting then adding does not leak.
- Switching to a zone with no environment: the viewport shows only the reference
  grid, there is no collision geometry, and the camera sits at the origin.
- The active zone changes: the outgoing zone's designation lies dormant and the
  incoming zone's becomes the live one. Because each designation is interpreted
  in its own zone's actor map, a handle from one zone never resolves to a
  same-index actor in another.
- A zone's designated player is deleted after designation: entering play drives
  nothing, with no explicit clearing step (as in the walk-around phase).
- Undo after switching: an edit recorded in one zone must never act on another;
  each zone keeps its own undo and redo history.
- Switching repeatedly: bodies and GPU resources do not accumulate across
  switches.
- Shutting down with several zones: every zone's static-scene resources are
  released, not only the active zone's.
- A zone add, select, or delete while a viewport drag is in progress cannot
  occur (the zones section and the drag do not share input); a keyboard nudge
  around a switch is harmless.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The gameplay state MUST hold several zones and track exactly one
  active zone. A zone MUST be identified durably (by a unique name, not a
  positional array index) so that a reference to it stays valid as other zones are
  added.
- **FR-002**: The editor MUST list the zones in the outliner, in their own
  section above the scene objects, and offer deliberate actions to add a zone
  (naming it), delete a zone, and make a listed zone active; the active zone MUST
  be visually distinguished from the others. The add control MUST be unavailable
  while the typed name is empty or already used, and the delete control MUST be
  unavailable for the last remaining zone, so invalid actions cannot be triggered.
- **FR-003**: Adding a zone MUST take a developer-chosen name, make the new zone
  the active zone, and MUST NOT disturb any existing zone. The name MUST be
  non-empty and unique within the zone map (the editor does not offer the add
  otherwise, and the container rejects a direct call). A new zone MUST start empty
  — no actors, props, or environment.
- **FR-004**: Rendering, editing, inspection, selection, and the play-mode
  systems MUST all operate on the active zone only.
- **FR-005**: Switching MUST preserve each zone exactly — its entities,
  transforms, environment, and player designation — so that switching away and
  back restores it.
- **FR-006**: The shared physics world MUST simulate only the active zone's
  bodies. Switching MUST detach the outgoing zone's bodies and attach the incoming
  zone's, so inactive zones neither collide with nor are simulated alongside the
  active zone.
- **FR-007**: Loading an environment MUST target the active zone and MUST NOT
  change any other zone.
- **FR-008**: Each zone MUST designate which of its actors, if any, is the
  player. Only the active zone's designation is live: the play systems MUST drive
  exactly the active zone's designated actor and MUST ignore every inactive
  zone's designation. A handle MUST be interpreted only against its own zone's
  actor map, so a designation in one zone can never resolve to a same-index actor
  in another.
- **FR-009**: Switching MUST re-frame the camera on the incoming zone — its
  designated player when one exists, otherwise the zone's origin — so the view is
  not left in the previous zone.
- **FR-010**: Undo and redo MUST apply to the zone in which the edit was
  recorded; switching zones MUST NOT cause an edit's undo to act on a different
  zone.
- **FR-011**: Adding, switching, and deleting zones are container/view actions,
  not scene edits: they MUST NOT themselves be recorded as undoable scene edits
  (matching the play/edit toggle and *Set as Player*).
- **FR-012**: The application MUST start with exactly one active zone, preserving
  current behaviour for existing content.
- **FR-013**: Switching the active zone MUST be available while editing; play
  mode always drives the active zone.
- **FR-014**: Shutdown MUST release every zone's static-scene resources, not only
  the active zone's.
- **FR-015**: Deleting a zone MUST remove it entirely — its entities, environment,
  player designation, and undo history — and MUST release its physics bodies, its
  actors'/props'/parts' renderers, and its static-scene batch, so no memory or GPU
  resource leaks.
- **FR-016**: Deletion MUST be rejected when only one zone remains, so there is
  always exactly one active zone. Deleting the active zone MUST first make another
  zone active (re-framing the camera); deleting an inactive zone MUST leave the
  active zone and camera unchanged.

### Key Entities *(include if feature involves data)*

- **Zone**: one independent playable world — its actors, placed props, optional
  environment, body-to-entity map, player designation, and its own undo history.
- **Zone container**: the set of zones and the identity of the active one; the
  single place that answers "which zones exist, and which is active".
- **Active zone**: the zone currently rendered, edited, inspected, and simulated.
- **Zone name**: the durable identity of a zone — a unique name that survives
  other zones being added and is how a zone is addressed.
- **Player designation**: which actor in a zone represents the player. At most
  one designation is live at a time (the active zone's); the others are inert.
  Absent means the zone drives no character.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can add a zone and switch between zones from the
  editor, and the workspace reflects the active zone in the same frame it is
  selected (100% of switches).
- **SC-002**: Switching away from a zone and back leaves its entities,
  transforms, environment, and player designation unchanged (100%).
- **SC-003**: Only the active zone's geometry renders and collides; inactive
  zones never intersect or interact with the active zone (100%).
- **SC-004**: Each zone drives its own designated player when active and none
  when none is designated; a designation made in one zone never drives an actor
  in another (100%).
- **SC-005**: An undo recorded in one zone never alters another zone (100% of
  edits).
- **SC-006**: The gameplay loop sustains the project's target frame rate while
  switching and playing (60 FPS on the target platform), with no growth in
  physics bodies or GPU resources across repeated switches.
- **SC-007**: Launching the application still lands on the existing single-zone
  workspace with unchanged behaviour for current content.
- **SC-008**: A developer can add, select, and delete zones; the last remaining
  zone cannot be deleted, so exactly one zone is always active (100%).
- **SC-009**: Deleting zones and adding them again does not leak physics or GPU
  resources (no growth in bodies or renderers across repeated add/delete cycles).

## Assumptions

- Zones are created and used within a session; persistence and the zone registry
  are later phases.
- Zone renaming and duplication are out of scope for this feature; zones can be
  added (with a name), selected, and deleted. The application starts with a single
  zone named `main`.
- Adding, switching, and deleting zones are not undoable scene edits; deleting a
  zone discards that zone's undo history.
- Each zone designates its own player actor, so every zone is independently
  playable; only the active zone's designation is live, so there is still exactly
  one player at a time. Each zone also retains its own undo and redo history.
- A zone loads at most one environment at a time (unchanged from the current
  model).
- Only the active zone is rendered and simulated; inactive zones are inert, not
  background-simulated or streamed.
- The number of zones in a session is small; no streaming or level-of-detail
  concerns.
- Switching the active zone happens in edit mode; play mode always drives the
  active zone.
