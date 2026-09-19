# Feature Specification: Waypoints

**Feature Branch**: `026-waypoints`

**Created**: 2026-09-19

**Status**: Complete (retrospective)

**Input**: User description: "Phase 10 (story primitives), waypoints slice: author waypoints in a zone. A waypoint is a named point of interest that records where to stand and which way to face, stored as zone entity data with the same selection, gizmo, duplicate, delete and undo behaviour as actors and props. Waypoints can be added at the origin or seeded from the editor camera, are listed in the Outliner, and are drawn in the viewport with a direction arrow."

**Retrospective**: this spec was written after the behaviour was built and
demonstrated, to record what was ratified rather than to propose it. It is the
waypoints slice of roadmap phase 10 ("Story primitives"); triggers, cast, and
adventure flags remain that phase's remaining work.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Author waypoints in a zone (Priority: P1)

A developer working in the editor adds a waypoint to the active zone, positions
and orients it with the transformed-selection machinery the editor already has
(the gizmo, keyboard nudge, inspector), duplicates it, and deletes it — with
undo and redo covering each of those edits exactly as they do for actors and
props. Waypoints belong to the zone they were added to, so switching zones and
coming back finds them unchanged, and a reference to a deleted waypoint never
resolves to a different waypoint that reused its slot.

**Why this priority**: this is the substance of the feature. A waypoint that
cannot be placed, oriented, removed, or undone is not usable authoring data.

**Independent Test**: add a waypoint; move and rotate it and confirm the position
and facing stick; duplicate it and confirm an independent copy; delete it and
undo and confirm it returns with the same transform; switch zones and back and
confirm it is still there.

**Acceptance Scenarios**:

1. **Given** the active zone, **When** the developer adds a waypoint, **Then** a
   new waypoint exists at the origin with identity facing, is listed in the
   Outliner, and becomes the selection.
2. **Given** a selected waypoint, **When** the developer drags it with the move
   gizmo, **Then** its position changes and that change undoes as a single step.
3. **Given** a selected waypoint, **When** the developer rotates it with the
   rotate gizmo, **Then** its facing changes and that change undoes as a single
   step.
4. **Given** a waypoint, **When** the developer duplicates it, **Then** a second
   waypoint with the same transform exists, the copy is selected, and the
   original is untouched.
5. **Given** a waypoint, **When** the developer deletes it and then undoes,
   **Then** it is restored at the same slot with its name and transform intact.
6. **Given** a zone holding waypoints, **When** the developer switches to another
   zone and back, **Then** its waypoints are exactly as they were left.
7. **Given** a waypoint that was deleted, **When** another waypoint reuses its
   slot, **Then** the stale reference resolves to neither the old nor the new
   waypoint.
8. **Given** a waypoint, **When** the editor inspects it, **Then** its name,
   position, rotation, and scale are shown as for any other entity.

---

### User Story 2 - Place a waypoint where the camera is looking (Priority: P2)

Instead of adding a waypoint at the origin and then moving and rotating it into
place, the developer adds one at the editor camera's position, already facing the
direction the camera is looking.

**Why this priority**: this is the common authoring gesture — fly or frame the
view on the spot you mean, then stamp a waypoint there. Adding at the origin
(US1) is already useful without it, but this removes a move and a rotate from
every placement.

**Independent Test**: frame the camera at a distinctive position looking a
distinctive direction, add a waypoint at the camera, and confirm the waypoint
sits at the camera position and its facing points along the view direction.

**Acceptance Scenarios**:

1. **Given** the editor camera at a position looking in some direction,
   **When** the developer adds a waypoint at the camera, **Then** the new
   waypoint is at the camera's position and faces along the camera's view
   direction.
2. **Given** a waypoint added at the camera, **When** the developer edits it,
   **Then** it is selected, transformable, duplicable, deletable, and undoable
   exactly like one added at the origin.
3. **Given** the developer adds a waypoint at the origin, **Then** the result is
   unchanged from before this feature: the origin, with identity facing.

---

### User Story 3 - See which way a waypoint faces (Priority: P3)

Every waypoint in the active zone is drawn in the viewport as a marker with a
direction arrow, so its facing is visible without selecting it, and the selected
waypoint is drawn distinctly.

**Why this priority**: a waypoint's value is its orientation — it says which way
somebody should face when they reach that spot — and an orientation that cannot
be seen cannot be authored or checked. Placing waypoints (US1) still works
without it.

**Independent Test**: place two waypoints with different rotations and confirm
the two arrows point different ways; select one and confirm it is drawn in the
selection colour.

**Acceptance Scenarios**:

1. **Given** a waypoint in the active zone, **When** the viewport renders,
   **Then** the waypoint is drawn as a marker with a direction arrow pointing
   along its facing.
2. **Given** a waypoint, **When** the developer rotates it, **Then** the arrow
   follows the rotation in the same frame.
3. **Given** a selected waypoint, **When** the viewport renders, **Then** it is
   drawn in the selection colour, distinct from unselected waypoints.
4. **Given** a waypoint facing straight up or straight down, **When** the
   viewport renders, **Then** the arrow is still drawn with a readable head.
5. **Given** a waypoint in an inactive zone, **When** another zone is active,
   **Then** it is not drawn.

---

### Edge Cases

- Clicking a waypoint in the viewport does not select it: a waypoint has no
  collider, so it takes no part in viewport picking. It is selected from the
  Outliner's Waypoints list, after which the gizmo, inspector, nudge, duplicate,
  and delete all apply to it.
- A waypoint carries no renderer and no collider, so it draws no geometry and
  neither collides with nor supports anything.
- Adding a waypoint at the camera while the camera points exactly along the world
  up axis: the camera's own basis is degenerate there (its right/up vectors are
  undefined), so a camera-seeded waypoint inherits that. This is a pre-existing
  property of the camera's look-at basis, not a new failure mode.
- A zone with no waypoints shows an explicit "(none)" in its Waypoints section.
- Deleting the selected waypoint clears the selection.
- Deleting a waypoint and then undoing frees and re-occupies the same slot; the
  next spawn after an undo reuses that slot with a fresh generation, so a command
  recorded before the delete cannot act on the new occupant.
- Waypoints are zone data: adding, transforming, duplicating, and deleting them
  are edits to the active zone, recorded in that zone's history only.
- A waypoint's name is displayed but not yet editable; every new waypoint is
  named `Waypoint`.
- Waypoints are in-memory only, like the rest of a zone's contents; nothing is
  written to disk or to the narrative engine yet.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A zone MUST hold its own waypoints, independently of every other
  zone's, and switching zones MUST preserve them.
- **FR-002**: A waypoint MUST record a position and a facing (an orientation),
  and MUST be a first-class zone entity — with a stable reference, a name, and
  the same editor flags (enabled, visible, selectable, draggable) as actors and
  props — so the editor's existing selection, transform, and inspection
  behaviour applies to it without special cases.
- **FR-003**: The editor MUST list the active zone's waypoints in the Outliner in
  their own section, and MUST offer a deliberate action to add a waypoint at the
  origin with identity facing.
- **FR-004**: The editor MUST offer a deliberate action to add a waypoint at the
  editor camera's position, facing the direction the camera is looking.
- **FR-005**: A waypoint MUST be selectable, and the existing transform editing
  MUST apply to it: move and rotate with the gizmo, keyboard nudge, inspector
  display, and focus-on-selection.
- **FR-006**: A waypoint MUST be duplicable and deletable as actors and props
  are, with both operations undoable and redoable.
- **FR-007**: A waypoint's reference MUST stay valid across unrelated spawns and
  deletions of other entities, and a stale reference MUST NOT resolve to a
  waypoint that has reused its slot.
- **FR-008**: Restoring a deleted waypoint (undo) MUST put it back in the slot it
  was recorded against with its recorded transform and name.
- **FR-009**: A waypoint MUST take part in no physics simulation and MUST carry
  no renderer and no asset binding; adding, moving, or removing one MUST NOT
  affect collision, geometry rendering, or any other entity.
- **FR-010**: Every waypoint in the active zone MUST be drawn in the viewport
  with a marker and a direction indicator that follows its facing, and the
  selected waypoint MUST be visually distinguished from the others.
- **FR-011**: The direction indicator MUST remain readable for any orientation,
  including facing straight up and straight down.
- **FR-012**: A waypoint MUST NOT change the behaviour of any existing entity
  kind, and the application MUST start with no waypoints so existing content is
  unaffected.

### Key Entities *(include if feature involves data)*

- **Waypoint**: a named point of interest in a zone — where to stand (position)
  and which way to face (orientation). It composes the shared entity base (name,
  transform, editor flags) and carries nothing else: no renderer, no collider, no
  asset.
- **Waypoint reference**: a durable handle to one waypoint, valid within the zone
  that owns it; the way selection, duplicate, delete, and undo address it.
- **Waypoint facing**: the direction a waypoint points, derived from its
  orientation. It uses the same convention as the gameplay camera's view
  direction, so "the way the waypoint faces" and "the way a camera looks" mean
  the same thing.
- **Zone waypoints**: the set of waypoints a zone owns, alongside its actors,
  props, and environment.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can add, place, orient, duplicate, and delete a
  waypoint, and undo and redo each of those, entirely from the editor (100% of
  actions).
- **SC-002**: A waypoint's position and facing survive switching to another zone
  and back (100% of switches).
- **SC-003**: Adding a waypoint at the camera produces it at the camera's
  position facing the camera's view direction, with no follow-up adjustment
  needed (100% of such adds).
- **SC-004**: A reference to a deleted waypoint never resolves to a waypoint that
  reuses its slot (0 aliasing across repeated add/delete/reuse cycles).
- **SC-005**: Every waypoint in the active zone is visible in the viewport with
  its facing discernible without selecting it (100%), for every orientation.
- **SC-006**: The gameplay loop keeps the project's target frame rate (60 FPS on
  the target platform) with waypoints present, and repeated add/delete/switch
  cycles do not accumulate bodies or GPU resources.
- **SC-007**: Adding, deleting, or duplicating a waypoint never alters any other
  entity (100%).
- **SC-008**: Launching the application is unchanged for existing content: no
  waypoints exist until one is added, and the existing test suite passes.

## Assumptions

- This feature is the waypoints slice of roadmap phase 10. Triggers, cast, and
  adventure flags are that phase's remaining work and are out of scope;
  `Trigger` keeps its current placeholder shape.
- Nothing consumes a waypoint yet. It is authoring data: the cue engine (phase 9)
  and the story systems that act on waypoints arrive later.
- Waypoint rename has no editor UI yet; new waypoints are named `Waypoint`. The
  name is display-only for now, and because waypoints are referenced by handle
  rather than by name, renaming later needs no re-keying.
- Viewport click-picking of waypoints is out of scope; selection is from the
  Outliner. Picking the existing waypoint marker through the physics query would
  require giving every waypoint a collider, which contradicts FR-009.
- Waypoints are in-memory only; persistence is a later phase.
- A zone holds a small number of waypoints (tens), so no spatial indexing,
  culling, or budget is needed.
- The waypoint facing convention (the orientation applied to the −Z axis) is the
  project's existing camera-forward convention, used by the gameplay camera and
  the gizmo; this feature adopts it rather than introducing a second one.
