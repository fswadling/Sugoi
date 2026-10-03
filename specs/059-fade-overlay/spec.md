# Feature Specification: Screen Fade

**Feature Branch**: `059-fade-overlay`

**Created**: 2026-10-03

**Status**: Draft

**Input**: User description: "A value between 0 and 1 on the gameplay state which applies a gradual fade to black on the gameplay screen, tunable on a Miscellaneous tab docked to the right."

## Origin

`odin-game` carries exactly this on its gameplay screen: a `fade: f32` field documented as
"0 = transparent, 1 = fully black" and initialised to 0 (`src/gameplay_screen.odin:34,58`), drawn
last as a full-window black rectangle whose alpha is the value, skipped when the value is 0
(`src/gameplay_screen.odin:203-212`). It is the surface the cue ops `FadeOut`/`FadeIn` tween from
and to (`src/cue.odin:962-1259`) — a later feature; this one delivers the value and the overlay
themselves.

This port has neither the field nor the overlay. It also has no control over it: the reference's
editor writes raylib-GUI sliders straight against live state and never exposes `fade`, so the value
is only reachable from a cue. This feature adds the value, the overlay, and an authoring surface —
a **Miscellaneous** tab docked in the editor's right column, alongside the Inspector, Cue and Advent
panels (`docs/EDITOR_DESIGN.md`, Workspace; Module map), which is where the non-entity editing
surfaces live.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The gameplay view fades to black (Priority: P1)

The gameplay screen carries a fade level from 0 to 1. At 0 the view renders exactly as it did before
the feature; at 1 the view is solid black; any value in between darkens the view in proportion. The
overlay sits over the rendered game view — the 3D scene and the in-game 2D layer (dialogue, prompt)
— so a raised level reads as a cinematic fade, and holding an intermediate level reads as a partial
fade.

**Why this priority**: it is the whole capability. The level and the overlay are testable from code,
with no panel, and everything else in the feature exists to set this value.

**Independent Test**: set the fade level on the state, render a frame, and assert the black overlay
was emitted with an opacity equal to the level; assert level 0 emits no overlay and level 1 emits a
fully opaque one covering the whole view.

**Acceptance Scenarios**:

1. **Given** a fade level of 0, **When** the gameplay screen renders, **Then** no overlay is drawn
   and the frame is unchanged from before the feature.
2. **Given** a fade level of 1, **When** the screen renders, **Then** the whole game view is solid
   black and no scene pixel shows through.
3. **Given** an intermediate level, **When** the screen renders, **Then** the overlay is black at
   that opacity, so the game view is proportionally darkened but still visible.
4. **Given** the in-game 2D layer is showing a dialogue line, **When** the level is raised, **Then**
   the overlay covers it too (it is part of the game view being faded).
5. **Given** the editor is open, **When** the level is raised, **Then** the editor's docked panels
   stay fully readable above the overlay.

---

### User Story 2 - The fade is tuned from a Miscellaneous tab (Priority: P2)

The editor has a **Miscellaneous** panel docked as a tab in its right column, beside the Inspector,
Cue and Advent panels. It presents the fade level as a control spanning the full 0-to-1 range, and
dragging it changes the overlay on the next frame — so the author can preview a fade while building
the scene that will use it.

**Why this priority**: it is how the value is exercised by hand, and the surface the request names,
but the overlay is useful without it (a cue drives the same value later).

**Independent Test**: cycle to the panel, change the control, and assert the fade level on the state
follows; open the panel only exists while editing.

**Acceptance Scenarios**:

1. **Given** the editor is open, **When** the author cycles the docked panels, **Then** the
   Miscellaneous panel is among them, docked on the right.
2. **Given** the panel is shown, **When** the author drags its control across the range, **Then**
   the fade level follows continuously and the view darkens or clears on the next frame.
3. **Given** the panel shows the control at the current level, **When** the author opens the editor
   after a fade was set (from code or a previous drag), **Then** the control shows that level, not a
   reset default.
4. **Given** the game is not in the editor (playing), **When** the frame draws, **Then** the
   Miscellaneous panel is not drawn, exactly as the other docked panels are not.

---

### Edge Cases

- **Level exactly 0**: the overlay is skipped entirely — the frame is byte-for-byte the pre-feature
  frame (the reference skips the draw at 0 too).
- **Level exactly 1**: the view is fully black; the scene and in-game 2D layer are entirely hidden.
- **A zone switch while the level is raised**: the level is screen-scoped, so it is unchanged and
  the incoming zone is darkened to the same degree.
- **Leaving the gameplay screen for the main menu and returning**: the level persists on the
  gameplay screen's state (it is not reset on activation), matching the reference, where only
  construction sets it to 0.
- **Editing vs playing**: the overlay is drawn in both modes; the Miscellaneous panel exists only
  while editing, like every other editor panel.
- **A value outside 0..1**: the slider cannot produce one and no other path sets the field, so the
  domain is the contract; the overlay treats the level as an opacity. No clamping is specified.
- **The main menu**: untouched — the overlay is a gameplay-screen effect only.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The gameplay screen's state MUST carry a **fade level** in the range 0 to 1, defaulting
  to 0. It MUST be screen-scoped runtime state: a zone switch MUST NOT change it, and it MUST NOT be
  authored, persisted, or attached to an entity or zone.
- **FR-002**: The gameplay screen MUST render a **black overlay** over the game view whose opacity is
  the fade level, drawn above the 3D scene and above the in-game 2D layer (dialogue, prompt).
- **FR-003**: At level 0 the overlay MUST be a no-op (the frame is unchanged); at level 1 the view
  MUST be fully black; intermediate levels MUST darken in proportion to the level.
- **FR-004**: The overlay MUST apply only to the gameplay screen; the main menu MUST be unaffected.
- **FR-005**: A **Miscellaneous** panel MUST be docked as a tab in the editor's right column, beside
  the Inspector.
- **FR-006**: The Miscellaneous panel MUST present the fade level as a control over the full 0-to-1
  range, seeded from the current level, and changing it MUST change the overlay on the next frame.
- **FR-007**: The Miscellaneous panel MUST be part of the editor's docked-panel cycling and MUST be
  drawn only while editing, like the other docked panels.
- **FR-008**: The overlay MUST NOT cover the editor's docked panels — they remain fully readable at
  every fade level, so the control can be used while watching the fade.

### Key Entities *(include if data involved)*

- **Fade level**: a scalar in 0..1 on the gameplay screen's state. 0 leaves the game view untouched,
  1 makes it solid black; values between darken in proportion. Screen-scoped and runtime-only.
- **Miscellaneous panel**: the right-docked editor panel for game-wide, non-entity runtime tuning
  (things that belong to no zone and no selection). The fade level is its first control.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author can darken the gameplay view from untouched to fully black and back again,
  continuously, with one control and with no other change to the view.
- **SC-002**: A change to the level is visible on the next rendered frame.
- **SC-003**: At level 0 the rendered frame is indistinguishable from the pre-feature frame (no
  overlay pixels are drawn).
- **SC-004**: The editor's docked panels remain fully readable at every fade level from 0 to 1.
- **SC-005**: A zone switch leaves the fade level unchanged.

## Assumptions

- **Live tuning, not authored data**: the fade level is runtime state like `Editing`, `Follow` and
  `PlayTime`; it is not a step on the undo history and is not persisted, matching the reference
  (which also has no undo for it) and the other screen-level runtime values.
- **"Gradual" is the continuous range, not an automatic timed transition**: the level can hold any
  intermediate value, so moving it produces a gradual fade. Tweening the level over a duration — a
  cue's `FadeOut`/`FadeIn` driving this field — is a later feature, not this one.
- **The overlay covers the game view, not the editor chrome**: the reference draws the rectangle over
  the whole window because its editor shares one immediate-mode pass; here the editor panels are a
  separate layer above the game view, and keeping them readable is what makes the tuning control
  usable (FR-008).
- **The value stays in 0..1**: the control cannot produce an out-of-range value and no other path
  sets the field, so clamping is unnecessary and unspecified.
- **The main menu is out of scope**; only the gameplay screen fades.
- **No new asset is required**: black is a solid colour the in-game 2D layer can already produce.
