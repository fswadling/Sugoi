# Feature Specification: Editor-Only Reference Grid

**Feature Branch**: `093-editor-only-grid`

**Created**: 2026-10-15

**Status**: Draft

**Input**: User description: "next, i notice that the reference grid is visible even when in play mode - i'd like it to only be visible when the editor is active."

## Context

The 3D viewport draws a **reference grid** on the ground plane — a lattice of minor lines, major
lines, and coloured X/Z axis lines — as a spatial aid while authoring. It is drawn every frame by the
viewport (its own accumulator, separate from the editor's marker/gizmo accumulator) and, unlike the
markers and the transform gizmo, it is **not gated on whether the editor is active**: it is drawn in
play mode too.

The editor's markers, colliders, chain links and gizmo are already gated on the same flag the editor
panels are (`Editing`): they accumulate only while editing and their accumulator is cleared when play
begins, so nothing lingers. The grid breaks that rule, so a play session is shown the authoring grid
over the scene. This specification moves the grid onto the same footing: **drawn while the editor is
active, gone while playing**, with the same clean transition the markers already get.

This is a visibility rule only. The grid is a view aid; it is nothing an author authors, and it is not
stored in a zone or a scenario. Its appearance (colours, extent, orientation) is unchanged.

## Clarifications

### Session 2026-10-15

- Q: When the reference grid is hidden in play mode, should its coloured origin axis lines (the red X and
  blue Z lines through the origin) be hidden too, or kept as an origin reference? → A: **Hide the whole
  grid, axis lines included** — nothing of the ground overlay draws while playing, so the grid stays one
  gated unit rather than being split into a gated lattice and an ungated origin marker.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The grid is visible only while editing (Priority: P1)

An author works in the editor with the reference grid underfoot. They switch to play mode to try the
scene and the grid is gone — the play frame shows the scene against its sky and clear colour with no
authoring lattice over it. They switch back to the editor and the grid returns, unchanged.

**Why this priority**: it is the whole of what the user asked for and the smallest complete slice —
the grid's visibility follows the editor's active state.

**Independent Test**: put the editor in play mode and assert the frame draws no grid lines; return to
editing and assert the grid is drawn again. The visibility decision can be asserted with no window
opened.

**Acceptance Scenarios**:

1. **Given** the editor is active, **When** a frame is drawn, **Then** the reference grid is drawn
   exactly as it is today.
2. **Given** play mode is active, **When** a frame is drawn, **Then** no reference grid is drawn.
3. **Given** the editor is active with the grid drawn, **When** play begins, **Then** the grid is gone
   in that same frame (no lines remain from the previous frame).

---

### User Story 2 - Returning to the editor restores the grid (Priority: P2)

An author leaves play mode and lands back in the editor. The grid is where it was, with the same
colours and extent, and the author continues placing entities against it as if they had never left.

**Why this priority**: it is the other half of the toggle — that the grid is hidden *only* in play —
and it is what makes the rule a toggle rather than a one-way removal.

**Independent Test**: enter play mode (grid gone), return to editing, and assert the grid is drawn
again with an unchanged appearance.

**Acceptance Scenarios**:

1. **Given** play mode is active with no grid drawn, **When** editing is re-entered, **Then** the grid
   is drawn again with the same colours, extent and orientation as before.
2. **Given** the editor is active, **When** the grid is drawn, **Then** its appearance is identical to
   the pre-feature grid (this feature changes only *when* it draws, not *what* it draws).

---

### Edge Cases

- **The transition frame.** Entering play must clear the grid accumulator in the same frame, so a
  line-vertex buffer from the editing frame is not re-uploaded and drawn once more — the same reason
  the editor's markers are cleared on the switch.
- **An inactive zone.** The grid is a viewport aid independent of the active zone; hiding it in play
  changes nothing about zone rendering (props, environment, actors, sky are untouched).
- **A scene with no geometry and no sky.** The play frame is the flat clear colour, as it would be for
  any scene; the grid is not part of that clear.
- **A play session that never edited.** The grid is not drawn in play whether or not the editor was
  ever opened.
- **The origin axis lines.** The red X and blue Z axis lines drawn through the origin are part of the
  grid, so they hide with it in play; nothing of the ground overlay remains on a play-mode frame.
- **Other editor overlays.** Markers, colliders, chain links and the gizmo keep their existing
  behaviour (drawn only while editing); this feature neither adds to nor removes them.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The reference grid MUST be drawn **only while the editor is active** and MUST NOT be
  drawn while playing. "The editor is active" is the same state that gates the editor's panels and
  markers. The grid is hidden **as a whole**: its lattice **and** its coloured origin axis lines (the
  red X and blue Z lines through the origin) are hidden together — the axis lines are part of the grid
  and are not kept as an origin reference in play.
- **FR-002**: Entering play mode MUST leave no grid in the frame it enters on: the grid's line
  vertices MUST be cleared on the transition, so the previous editing frame's grid is not drawn again.
- **FR-003**: Returning to the editor MUST restore the grid.
- **FR-004**: The grid's **appearance** MUST be unchanged while editing — the same colours, extent and
  orientation as before this feature. This feature changes only *when* the grid draws.
- **FR-005**: The grid's visibility MUST be a **view rule only**: it MUST NOT alter any zone or
  scenario data, MUST NOT be stored, and MUST NOT change what is authored or persisted.
- **FR-006**: Drawing the grid MUST remain independent of the editor's other overlays — the node
  markers, colliders, chain links and transform gizmo — which keep their existing editor-only
  behaviour.
- **FR-007**: The visibility rule MUST be expressible as a value that can be **asserted headlessly**
  (no window opened), so a test can check the grid is drawn while editing and not while playing.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A play-mode frame contains no reference-grid lines; an editing frame contains them.
- **SC-002**: On the frame play is entered, no grid lines are drawn (no lingering vertices from the
  previous editing frame).
- **SC-003**: A play-mode frame is identical to what it would be with no grid ever drawn — the scene's
  geometry, sky and clear colour are unaffected.
- **SC-004**: The grid's appearance in the editor is byte-for-byte the same as before this feature.
- **SC-005**: The visibility rule can be exercised and asserted with no window opened; the project
  builds with no warnings and the test suite is green.

## Assumptions

- **"Editor active" is the existing edit/play toggle.** The flag that gates the editor's panels and
  markers is the one that gates the grid; there is no second notion of "editor active".
- **The reference grid is the viewport's ground grid.** It is drawn by the viewport each frame and is
  the only content in its accumulator; the marker/gizmo accumulator is separate and already
  editor-only.
- **The grid is not authored data.** It is a view aid, so nothing about this feature reaches zone or
  scenario staging, the undo history, or on-disk persistence.
- **Only visibility changes.** The grid's colours, extent and orientation are the reference's and are
  left as they are; this feature is a gate, not a restyle.
