# Feature Specification: Transform Inspector + Keyboard Nudge

**Feature Branch**: `007-transform-inspector-nudge`

**Created**: 2026-08-30

**Status**: Draft

**Input**: User description: "show rotation and scale in the Inspector (in degrees),
make the rotate/scale gizmos visibly work, and add keyboard nudge (Shift+WASD/QE)"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See the full transform in the Inspector (Priority: P1)

A developer selects an entity and reads its complete transform in the Inspector:
position, rotation (Euler angles in degrees), and scale. This is the feedback
that makes rotate/scale operations observable — today only position is shown, so
dragging the rotate/scale gizmos appears to do nothing.

**Why this priority**: it is the missing feedback loop that makes the existing
rotate/scale gizmos usable; without it the gizmos silently mutate state no one
can see.

**Independent Test**: select an entity, note its rotation/scale in the Inspector,
drag the rotate/scale gizmos, and watch the values change.

**Acceptance Scenarios**:

1. **Given** a selected entity, **When** the Inspector renders, **Then** it shows
   the entity's name, position, rotation (Euler, degrees), and scale.
2. **Given** a selected entity in rotate mode, **When** the user drags a rotation
   ring, **Then** the rotation values in the Inspector change.
3. **Given** a selected entity in scale mode, **When** the user drags a scale
   handle, **Then** the scale values in the Inspector change.
4. **Given** no selection, **When** the Inspector renders, **Then** it shows
   "(no selection)" and no transform fields.

---

### User Story 2 - Nudge the selection with Shift+WASD/QE (Priority: P2)

A developer holds Shift and taps W/A/S/D/Q/E to nudge the selected entity
camera-relatively — move/rotate/scale by small steps for the active tool, with
snap reusing the gizmo increments. The camera's fly keys (right-drag + WASD/QE)
and the `Q` snap toggle must not fire during a nudge.

**Why this priority**: fine-grained keyboard control complements the gizmos and is
the remaining editor interaction from the reference implementation; it depends on
the Inspector (US1) for visible feedback.

**Independent Test**: select an entity, hold Shift and tap W/A/S/D/Q/E, and watch
the Inspector transform change; confirm the camera does not fly and snap does not
toggle.

**Acceptance Scenarios**:

1. **Given** a selection in move mode, **When** the user holds Shift and taps
   W/A/S/D/Q/E, **Then** the entity moves along the camera's forward/back/left/
   right/up/down axes by the move step.
2. **Given** a selection in rotate mode, **When** the user holds Shift and taps
   W/A/S/D/Q/E, **Then** the entity rotates about the camera-relative axis for
   each key by the rotate step.
3. **Given** a selection in scale mode, **When** the user holds Shift and taps W
   (or S), **Then** the entity scales uniformly up (or down) by the scale step.
4. **Given** a selection, **When** the user holds Shift and taps Q, **Then** snap
   does not toggle.
5. **Given** right-drag with Shift held, **When** the user presses W, **Then** the
   camera does not fly (the key is reserved for nudge).

---

### Edge Cases

- Nudging with no selection is a no-op.
- Nudging while a text field has focus must not fire (typing is preserved).
- Nudging must not run while a gizmo drag is in progress.
- A nudge must not register as a camera fly when the right mouse button is held.
- The `Q` snap-toggle shortcut must not fire while Shift is held (Shift+Q nudges
  down instead).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Inspector MUST display the selected entity's rotation as Euler
  angles in degrees and its scale as a 3-component value, alongside position.
- **FR-002**: The rotation display MUST convert the stored quaternion to Euler
  angles (ZYX order) for readability.
- **FR-003**: Holding Shift with W/A/S/D/Q/E MUST nudge the selection
  camera-relatively for the active tool (move/rotate/scale).
- **FR-004**: Nudge steps MUST match the gizmo snap increments when snap is on
  (move 0.5, rotate π/12, scale 0.25) and the fine steps when off (0.05, 0.02,
  0.01).
- **FR-005**: The camera fly keys (W/A/S/D/Q/E during right-drag) MUST NOT fire
  while Shift is held.
- **FR-006**: The `Q` snap-toggle shortcut MUST NOT fire while Shift is held.
- **FR-007**: Nudging MUST NOT run while an ImGui text field has focus or a gizmo
  drag is in progress.

### Key Entities

- **Transform**: position, rotation (quaternion), and scale — the values shown by
  the Inspector and mutated by the nudge.
- **Nudge direction**: the camera-relative direction (forward/back/left/right/
  up/down) mapped from a held key.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After selecting an entity, a user sees rotation (degrees) and scale
  in the Inspector without extra clicks.
- **SC-002**: Dragging a rotate/scale gizmo produces a visible change in the
  Inspector (the rotate/scale gizmos are demonstrably working).
- **SC-003**: A user can position/rotate/scale an entity using only Shift+WASD/QE
  without touching the mouse, and the camera does not fly during the nudge.
- **SC-004**: Nudge and snap-toggle never fire while typing in a text field.

## Assumptions

- Rotation is displayed in degrees (per the product decision); the stored value
  remains a quaternion.
- Nudge steps are per-frame (not delta-time scaled), matching the reference
  implementation's 60 fps cap behaviour.
- The Inspector remains read-only this phase; field editing arrives with the
  command/undo phase (2d).
- Nudge undo coalescing (`nudge_before`) is deferred to 2d; this phase applies
  the transform directly, exactly as the gizmo drag already does.
