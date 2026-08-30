# Feature Specification: Selection + Transform

**Feature Branch**: `006-selection-transform`

**Created**: 2026-08-30

**Status**: Draft

**Input**: User description: "interact with entities in the viewport — spawn them from
the asset browser, select them, and move/rotate/scale them with gizmos"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Spawn an entity from the asset browser (Priority: P1)

A developer picks an actor or prop in the Asset Browser and it appears in the 3D
viewport as a wireframe marker and becomes the current selection. This is the
entry point that puts something into the scene to work with.

**Why this priority**: Without a way to get entities into the scene, nothing else
can be selected or transformed. It is the first visible, end-to-end editor
behaviour.

**Independent Test**: open the editor, click an asset in the Asset Browser, and
observe a marker appear in the viewport and become selected.

**Acceptance Scenarios**:

1. **Given** an empty zone and a list of discovered assets, **When** the user
   clicks an actor asset, **Then** an actor marker appears in the viewport and it
   becomes the selection.
2. **Given** an empty zone, **When** the user clicks a prop asset, **Then** a prop
   marker appears in the viewport and it becomes the selection.
3. **Given** a zone with one entity, **When** the user spawns another, **Then**
   both markers are visible and the newly spawned one is selected.

---

### User Story 2 - Select, focus, delete, and duplicate an entity (Priority: P2)

A developer clicks an entity's marker in the viewport to select it (it becomes
highlighted). With a selection active they can press `F` to focus the camera on
it, `Del` to delete it, and `Ctrl+D` to duplicate it.

**Why this priority**: Selection is the core editing gesture and the prerequisite
for the transform gizmos; focus/delete/duplicate are the first edits it enables.

**Independent Test**: with two entities in the viewport, click each in turn and
observe the highlight move; press `F`/`Del`/`Ctrl+D` and observe the expected
result.

**Acceptance Scenarios**:

1. **Given** two entities in the viewport, **When** the user clicks one's marker,
   **Then** that marker is highlighted and the other is not.
2. **Given** a selection, **When** the user presses `F`, **Then** the camera
   re-centres on the selected entity.
3. **Given** a selection, **When** the user presses `Del`, **Then** the entity is
   removed and the selection clears.
4. **Given** a selection, **When** the user presses `Ctrl+D`, **Then** a copy is
   added and becomes the selection.

---

### User Story 3 - Transform an entity with gizmos (Priority: P3)

A developer moves, rotates, or scales the selected entity by dragging its gizmo
handles in the viewport. The active tool is chosen with `1`/`2`/`3` and snapping
toggles with `Q`.

**Why this priority**: gizmos are the primary way to author a scene, but they
build on selection (US2) and are the last piece of the "spawn → select →
transform" loop.

**Independent Test**: select an entity and drag a gizmo handle; observe the
marker move/rotate/scale accordingly, and `1`/`2`/`3`/`Q` change the behaviour.

**Acceptance Scenarios**:

1. **Given** a selection in move mode, **When** the user drags an axis handle,
   **Then** the entity translates along that axis.
2. **Given** a selection in rotate mode, **When** the user drags a ring, **Then**
   the entity rotates about that axis.
3. **Given** a selection in scale mode, **When** the user drags an axis handle,
   **Then** the entity scales along that axis.
4. **Given** any tool, **When** the user presses `Q`, **Then** the transform snaps
   to fixed increments while dragging.

---

### Edge Cases

- Clicking an empty region of the viewport clears the selection rather than
  leaving a stale selection.
- Clicking on an ImGui panel (Outliner, Inspector, Asset Browser) must not be
  treated as a viewport click.
- Dragging a gizmo handle when the mouse leaves the viewport must not corrupt the
  entity's transform.
- Deleting the selected entity must clear the selection (no dangling reference).
- Duplicating a deleted/absent entity must be a no-op (no fabricated entity).
- Releasing a gizmo drag must leave the transform at the final value (the pre-drag
  value is retained for a future undo).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST let the user spawn an actor or prop from the Asset
  Browser into the active zone, and make it the selection.
- **FR-002**: The system MUST render a wireframe marker for every actor and prop
  in the active zone, with the selected entity visually distinct.
- **FR-003**: The system MUST select the entity under the cursor on a viewport
  click (a screen ray against each entity's box), and clear the selection when
  clicking empty space.
- **FR-004**: The system MUST support `F` (focus), `Del` (delete), and `Ctrl+D`
  (duplicate) on the selection.
- **FR-005**: The system MUST provide translate, rotate, and scale gizmos on the
  selection, selectable via `1`/`2`/`3`, with `Q` toggling snap.
- **FR-006**: Dragging a gizmo handle MUST transform the selection along/around
  the grabbed axis for the active tool.
- **FR-007**: The system MUST record the entity's pre-drag transform when a gizmo
  drag begins (for a future undo).
- **FR-008**: Viewport interaction MUST NOT fire while an ImGui control has
  captured the mouse.

### Key Entities

- **Ray**: a screen ray (origin + direction) derived from the camera and a cursor
  position, used to pick entities and gizmo handles.
- **Selection**: the single currently-selected entity (or none).
- **Tool**: the active transform tool — move, rotate, or scale.
- **GizmoAxis**: which axis (X/Y/Z) a gizmo drag is operating on.
- **Drag**: the state of an in-progress gizmo drag — the grabbed axis, the
  pre-drag transform, and the mouse grab point.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can spawn, select, and transform an entity in a single
  session, using only the viewport and keyboard, without restarting.
- **SC-002**: Clicking an entity's marker selects it immediately (no perceptible
  delay, no extra click).
- **SC-003**: Delete removes exactly the selected entity; duplicate adds exactly
  one copy and selects it.
- **SC-004**: The marker/gizmo draw path introduces no per-frame allocation, so
  the editor holds its target frame rate while dragging.
- **SC-005**: Viewport clicks never interfere with typing in a text field or
  clicking an ImGui panel.

## Assumptions

- The minimal raycast targets each entity's bounding box; accurate
  collider/physics picking arrives with the physics phase and replaces this
  without changing the selection API.
- Environments are listed in the Asset Browser but are not spawnable this phase
  (they become zone settings later).
- Waypoints and triggers are out of scope this phase.
- There is no undo this phase; the pre-drag transform is recorded now so the
  command/undo phase can wrap it without re-plumbing.
- The viewport uses the full window for picking (panels are excluded by the
  mouse-capture guard), matching the reference implementation.
