# Feature Specification: Remove Editor Scroll Zoom

**Feature Branch**: `065-remove-scroll-zoom`

**Created**: 2026-10-04

**Status**: Draft

**Input**: User description: "I'd like to quickly remove the mousewheel zoom from the editor — it's not necessary with camera fly and it collides with trying to scroll in the editor."

## Origin

The editor camera (`specs/003-editor-workspace`) has three controls: **orbit** (right-drag), **fly** (right-drag +
`WASD` / `Q` / `E`), and **dolly / zoom** (the scroll wheel, `0.9^wheel`, clamped). The wheel is accumulated
engine-wide each frame and applied to the camera unconditionally in `Editor.updateCamera` — so turning the wheel
while scrolling **any** editor panel (Outliner, Inspector, Cue, Advent, Dialogue…) also dollies the camera. The
wheel events reach ImGui on their own (forwarded via `processEvent`), so panels already scroll correctly; the
camera move is a second, unwanted effect, and that collision is the bug.

With fly controls the zoom is not needed, so this feature removes the scroll zoom and the wheel input plumbing
that existed only to feed it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Scrolling a panel scrolls the panel only (Priority: P1)

The author scrolls a long panel (the Outliner's zones, a Cue's rows, the Inspector). The panel scrolls; the
camera does not move. Turning the wheel anywhere — panel or viewport — never changes the view.

**Why this priority**: it is the whole point — the collision is what prompted the change, and it is the
behaviour the author sees.

**Independent Test**: with the wheel handler gone, driving a wheel event does not move the camera (a headless
assertion over the input→camera path), and the Cue/Outliner panels still scroll (their own ImGui behaviour,
unchanged).

**Acceptance Scenarios**:

1. **Given** the editor open, **When** the author scrolls over a panel, **Then** the panel scrolls and the
   camera's `Position`/`Target` are unchanged.
2. **Given** the editor open, **When** the author scrolls over the viewport, **Then** nothing happens (there is
   no scroll zoom anywhere).

---

### User Story 2 - Orbit and fly are unaffected, and no wheel path remains (Priority: P2)

The camera still orbits on right-drag and flies on right-drag + `WASD`/`Q`/`E`. The wheel input plumbing that
fed the zoom is gone, so no dead path is left behind.

**Why this priority**: it bounds the change — this is the removal of one control, not a change to navigation.

**Independent Test**: orbit and fly move the camera as before; no `Wheel` field remains on `Input`, and no
`dolly` remains on `Camera`.

**Acceptance Scenarios**:

1. **Given** the editor, **When** the author right-drags, **Then** the camera orbits; with `WASD`/`Q`/`E` held,
   it flies — exactly as before.
2. **Given** the change, **Then** the per-frame input no longer carries a wheel value and the camera module no
   longer offers a dolly.

---

### Edge Cases

- **Wheel over the viewport**: performs nothing — the viewport is a pass-through panel, and there is no other
  zoom control to fall back on.
- **Wheel while flying (right-drag held)**: performs nothing extra; fly is keyboard-only.
- **High-resolution / trackpad scroll** producing many wheel events: they reach ImGui (panel scroll) and
  nothing else.
- **Other wheel consumers**: none — the editor camera was the only reader of the wheel value (verified by
  search), so nothing else regresses.
- **Keyboard shortcuts and nudge** (`specs/004-editor-keyboard-shortcuts`): untouched.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The editor camera MUST NOT move in response to the mouse wheel. The scroll dolly/zoom is removed,
  and no other input path adds a zoom.
- **FR-002**: Mouse-wheel events MUST still reach ImGui, so panel scrolling is unaffected. This is unchanged;
  the removal MUST NOT disturb it.
- **FR-003**: The camera's **orbit** (right-drag) and **fly** (right-drag + `WASD`/`Q`/`E`) controls MUST be
  unchanged.
- **FR-004**: The per-frame wheel input plumbing that existed only to feed the zoom — the engine's wheel
  accumulation and the wheel value on the gathered input (`Input.Wheel`, and the parameter that carries it) —
  MUST be removed, so no dead input path remains.
- **FR-005**: `Camera.dolly`, unused once FR-001 lands, MUST be removed along with its unit tests, so no dead
  production code remains.

### Key Entities *(include if data involved)*

- **Editor camera**: the look-at camera (`render/Camera.fs`) with `orbit`, `pan` and (removed) `dolly`.
- **Per-frame input**: `core/Input.fs`'s `Input` snapshot, gathered once by the engine and carried in
  `Screen.Frame`; its wheel value is removed.
- **Panel scrolling**: Dear ImGui's own, driven by the events the engine forwards; unaffected.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Turning the wheel while the pointer is over any editor panel scrolls the panel and leaves the
  camera's `Position` and `Target` exactly as they were.
- **SC-002**: Turning the wheel over the viewport does not move the camera.
- **SC-003**: Orbit (right-drag) and fly (right-drag + `WASD`/`Q`/`E`) behave exactly as before.
- **SC-004**: No wheel value remains on the gathered input, and no `dolly` remains on the camera module.
- **SC-005**: The project builds with no warnings and the suite is green, with no device opened.

## Assumptions

- **The wheel has no other consumer.** The editor camera is the only reader of the input wheel value; panel
  scrolling is ImGui's and is driven by forwarded events, so removing the app-side value changes nothing for the
  panels.
- **Zoom is not needed.** Fly is the navigation tool; the author explicitly does not want a scroll zoom.
- **Removal over retention.** The wheel input field and the camera's dolly are removed rather than left unused
  (Minimalism: no dead paths). If a zoom is wanted later it is a new control with its own input, chosen on
  purpose rather than a leftover.
- **No new control replaces it.** This removes a control; it adds no key binding or UI for zoom.
