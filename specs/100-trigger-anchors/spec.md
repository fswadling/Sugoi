# Feature Specification: Trigger Anchors

**Feature Branch**: `100-trigger-anchors`

**Created**: 2026-10-17

**Status**: Draft

**Input**: User description: "Next, let's do the same for triggers — I should be able to add them at the camera and at the player. Can you spec that out?"

**Scope**: this spec extends the trigger authoring surface defined by
`specs/029-triggers` and `specs/058-trigger-cue`. It adds creation anchors and
automatic names; it does not change trigger detection, trigger cues, or the authored
trigger shape.

## Clarifications

### Session 2026-10-17

- Q: When the developer adds a trigger at the camera, where should the trigger's cylinder base be placed? → A: At the camera's exact position; the camera anchor performs no ground projection, so the volume may float at camera height and the author can adjust it afterwards.
- Q: When adding a trigger at the player, should the cylinder's base sit at her feet, or should the volume be centred vertically on her? → A: Base at her feet, lowered slightly: 0.1 m below the feet.
- Q: How should a newly created trigger be named? → A: Anchor-informed names with a per-anchor counter (`Origin 1`, `Camera 1`, `Player 1`); rename remains available.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Stamp a trigger where the player is standing or the camera is aimed (Priority: P1)

A developer designates an actor as the zone's player, walks her to a spot, returns to
edit mode, and creates a trigger volume just under her feet. Alternatively, the
developer flies the camera to a spot and creates a trigger at the camera's position. The new
trigger is selected and immediately editable — radius, height, prompt, cue, and
interaction flag.

**Why this priority**: this is the requested capability. It turns the player and the
editor camera into placement tools for the volume's base position, removing the
"add at the origin, then drag it" step from every trigger.

**Independent Test**: place the player at a distinctive spot and create a trigger at
the player; confirm its base is 0.1 m below her feet. Move the camera to a distinctive
position and create one at the camera; confirm its base is at the camera's position.
Confirm each is selected and independent of the player/camera afterwards.

**Acceptance Scenarios**:

1. **Given** a live designated player at a position, **When** the developer creates a
   trigger at the player, **Then** a new trigger's base is 0.1 m below her feet, with
   the default radius and height, and it is selected.
2. **Given** the editor camera at a position, **When** the developer creates a
   trigger at the camera, **Then** the trigger's base is at the camera's position, and
   it is selected.
3. **Given** no live designated player, **When** the developer opens the creation
   menu, **Then** the player anchor is shown as unavailable and choosing it creates
   nothing.
4. **Given** the camera is high above the floor, **When** the developer creates a
   trigger at the camera, **Then** the volume sits at camera height and may not
   detect a player on the floor until the author resizes or moves it; creation still
   succeeds.
5. **Given** a trigger created at an anchor, **Then** it can be edited, renamed,
   duplicated, deleted, enabled/disabled, wired to a cue, and undone exactly like one
   added at the origin.
6. **Given** a trigger created at the player or camera, **When** the player or camera
   moves afterwards, **Then** the trigger does not move: creation is a one-shot stamp,
   not an attachment.
7. **Given** a player whose feet are not on a floor (for example, mid-air), **When**
   the developer creates a trigger at the player, **Then** the trigger's base is
   0.1 m below her current feet position; the player anchor is a position stamp, not
   a ground query.
8. **Given** a trigger created at the player with the default height, **When** the
   player stands at the anchor position, **Then** she is inside the volume: the base
   is below her feet and the height covers her.

---

### User Story 2 - One creation menu instead of a trigger button per anchor (Priority: P1)

All ways to create a trigger are gathered into one creation control whose menu lists
the anchors: origin, camera, and player. Adding another anchor later extends that menu
rather than adding another button to the panel.

**Why this priority**: the waypoint authoring surface has the same shape, and one
menu keeps the trigger section consistent while bounding the number of visible
controls. A row of per-anchor buttons would reproduce the problem the waypoint work
just removed.

**Independent Test**: open the trigger section and confirm there is one creation
control; open it and confirm every anchor is offered in one list; create from each
enabled entry and confirm the result.

**Acceptance Scenarios**:

1. **Given** the trigger section, **When** the developer looks for creation actions,
   **Then** they are reached from one control with one menu, not one button per anchor.
2. **Given** the creation menu, **When** an anchor is chosen, **Then** exactly one
   trigger is created at that anchor and is selected.
3. **Given** the zone's trigger capacity is full, **When** the developer views the
   creation control, **Then** creation is unavailable and no trigger is added.
4. **Given** a new anchor is introduced in future, **Then** it joins the same menu
   without adding a button beside the existing controls.

---

### User Story 3 - Readable automatic trigger names (Priority: P2)

A newly created trigger is named from its anchor and a per-anchor counter, so the
trigger list and the cue's trigger chooser do not fill with identical `Trigger`
entries. Renaming remains available and frees the automatic name.

**Why this priority**: trigger cues address triggers by name, so a list of identical
labels is a real authoring hazard. It is secondary to placement, but it is part of
making the creation menu usable.

**Independent Test**: create two triggers at the camera and one at the player;
confirm the names are `Camera 1`, `Camera 2`, and `Player 1`; rename one and create
again and confirm the freed number is reused.

**Acceptance Scenarios**:

1. **Given** a zone with no triggers, **When** the developer creates two at the camera
   and one at the player, **Then** they are named `Camera 1`, `Camera 2`, and
   `Player 1` respectively.
2. **Given** a trigger named automatically, **When** the developer renames or deletes
   it, **Then** the next creation at the same anchor reuses the lowest free number.
3. **Given** a trigger whose automatic name was changed, **When** a cue names the
   trigger, **Then** the cue uses the trigger's current name; automatic naming changes
   no cue semantics.
4. **Given** duplicate names created by renaming, **Then** the existing name-based
   trigger addressing applies unchanged.

---

### Edge Cases

- No player is designated, or the designation no longer resolves to a live actor: the
  player anchor is unavailable and creates nothing.
- The camera anchor uses the camera's exact position, so a trigger created at eye
  height may float above the walkable floor. That is accepted: the author can resize
  the volume, move the trigger, or use the player anchor.
- The camera is inside geometry: creation still succeeds at the camera's position;
  the authored trigger volume is not validated against collision.
- The player is coincident with an existing trigger: creation is still allowed;
  trigger names and positions are not required to be unique.
- The player is mid-air: the player anchor uses her current feet position lowered by
  0.1 m; the trigger may be floating, which is the author's explicit placement.
- The zone is at trigger capacity: creation is unavailable and reports why; nothing is
  partially created and no history step is recorded.
- A new trigger uses the existing default volume shape (the current default radius
  and height), identity rotation, and unit scale; the anchor supplies position only.
- A trigger created at an anchor is independent: the player or camera moving later
  does not move it.
- Triggers remain zone data: creation targets the active zone, and the player anchor
  reads that zone's designation.
- Play mode remains non-authoring. The player's current authored transform is used
  when edit mode draws the action; no play-mode stamping surface is introduced.
- Every creation is one step on the one editor history and is redoable.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The editor MUST offer creating a trigger at the active zone's live
  designated player, at the editor camera, or at the world origin.
- **FR-002**: All trigger creation anchors MUST be reached from one creation control
  whose menu lists the anchors; a new anchor MUST join that menu rather than add a
  sibling button.
- **FR-003**: A player-anchored trigger MUST use the player's current authored feet
  position, lowered by 0.1 m, as the centre of its cylinder base, and MUST be
  unavailable when no live player is designated.
- **FR-004**: A camera-anchored trigger MUST use the editor camera's current position
  as the centre of its cylinder base. It MUST NOT ground-project or otherwise query a
  surface.
- **FR-005**: A newly created trigger MUST use the existing default radius and height,
  identity rotation, and unit scale; an anchor supplies position only.
- **FR-006**: Creating a trigger at any anchor MUST select the new trigger and MUST be
  exactly one undoable step.
- **FR-007**: Creation MUST refuse at capacity without creating a partial trigger or
  recording a step.
- **FR-008**: Creation MUST target the active zone; the player anchor MUST use that
  zone's player designation, and no trigger created at an anchor may reference or
  attach to the player or camera.
- **FR-009**: A newly created trigger MUST be named from its anchor and a per-anchor
  counter: the anchor's name plus the lowest positive number not already used by a
  live trigger with that prefix (`Player 1`, `Player 2`, `Camera 1`, `Origin 1`).
  Names remain editable, and a renamed or deleted trigger frees its automatic name.
- **FR-010**: The feature MUST NOT change trigger semantics beyond the automatic
  naming rule in FR-009: containment, enabled/disabled detection, interactable
  behaviour, prompts, authored cues, duplicate, delete, undo, trigger-name
  resolution, and scenario capture MUST behave as before.
- **FR-011**: The editor's play mode MUST remain non-authoring; the player anchor uses
  the transform already recorded on the actor, not a new play-mode edit surface.
- **FR-012**: The feature MUST add no persisted field or format; automatic names are
  ordinary trigger names captured with the trigger like any other authored name.
- **FR-013**: The camera anchor MUST be available whenever the editor is in edit
  mode; it performs no surface query and does not fail for lack of a floor.

### Key Entities

- **Trigger**: a cylindrical volume owned by a zone, with a base position, radius,
  height, interactable flag, prompt, and authored cue (unchanged from
  `specs/029-triggers` and `specs/058-trigger-cue`).
- **Trigger anchor**: an editor-only source for a new trigger's base position —
  origin, editor camera (exact position), or designated player (0.1 m below the
  feet). It is not authored data and is not persisted.
- **Editor camera position**: the camera-anchored trigger's base position; the
  anchor copies it directly without a surface query.
- **Player designation**: the active zone's current player actor, whose authored feet
  position, lowered by 0.1 m, is the player anchor (unchanged from
  `specs/045-undoable-player-designation`).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can create a trigger at the player in one action, with the
  base 0.1 m below her feet and no follow-up position adjustment needed (100% of such
  adds).
- **SC-002**: A developer can create a trigger at the camera in one action, with the
  base at the camera's position (100% of such adds).
- **SC-003**: Every trigger creation anchor is reachable from one creation control,
  and the number of visible creation buttons does not grow when an anchor is added
  (0 additional buttons per new anchor).
- **SC-004**: Each trigger creation, and the undo of each, is exactly one history step
  (100%).
- **SC-005**: A player anchor with no live player never creates a partial trigger or
  records a history step (0 violations).
- **SC-006**: Existing trigger behaviour is unchanged: the existing test suite passes,
  and containment, cue wiring, prompts, duplicate, delete, name resolution, and
  scenario capture behave as before.
- **SC-007**: The gameplay loop keeps the project's target frame rate with the new
  authoring controls present.
- **SC-008**: Automatically named triggers created through the menu are distinguishable
  in the trigger chooser: no creation introduces a duplicate name among live triggers
  with the same anchor prefix (0 duplicates).
- **SC-009**: A trigger created at the player or camera remains stationary when the
  player or camera moves (0 attachments).

## Assumptions

- This feature is authoring-only. It adds no runtime consumer and no new persisted
  field or format; automatic names are ordinary trigger names captured as before.
- The camera anchor copies the camera's exact position. A trigger created high above
  the floor may float; this is accepted, and the author can resize or move it.
- The camera anchor performs no collision query, so it is available in empty zones and
  does not depend on the editor grid being collidable.
- The player anchor is a position stamp, not a ground query: it uses the actor's
  authored feet position, lowered by 0.1 m, even when she is mid-air.
- The trigger's existing default radius and height are the resulting volume; the
  anchor does not resize it.
- Trigger rotation is identity and scale is unit because containment is radially
  symmetric; the anchor supplies no orientation.
- The anchor palette is closed at origin, camera, and player. Future anchors join the
  same creation menu; triggers have no extend/chain gesture.
- The automatic naming convention matches the waypoint authoring convention
  (`Origin 1`, `Camera 1`, `Player 1`), and names remain editable.
- Trigger cues address triggers by name; automatic naming changes the default label,
  not the addressing rule.
- Creation is an edit-mode action; play-mode stamping is out of scope.
- Existing duplicate, delete, capacity, and undo behaviour applies unchanged to the
  new anchors.
