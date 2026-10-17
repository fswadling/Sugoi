# Feature Specification: Waypoint Authoring UX

**Feature Branch**: `099-waypoint-authoring`

**Created**: 2026-10-17

**Status**: Draft

**Input**: User description: "Add the ability to add a waypoint at the player position — a useful authoring tool to set up walk / jog to positions in cutscenes. Improve the UX of setting up waypoints and waypoint chains; the current solution adds more and more buttons and scales poorly."

**Scope**: this spec extends the authoring surface defined by `specs/026-waypoints` and
`specs/028-waypoint-chains`; it does not change the waypoint or chain data model.

## Clarifications

### Session 2026-10-17

- Q: When a developer adds a next waypoint to a waypoint that already has one, should the new waypoint replace the existing next, or be inserted before it? → A: Insert before the existing next; the new waypoint's next becomes the old next, preserving the tail of the chain.
- Q: What name should a newly created waypoint receive: the existing generic `Waypoint`, or a name that distinguishes it in the next-waypoint chooser? → A: An anchor-informed name with a per-anchor counter in the zone: `Player 1`, `Player 2`, `Camera 1`, `Origin 1`; rename remains available.
- Q: When the designated player has been moved in edit mode rather than by playing, should the player anchor use her current editor transform? → A: Yes; the player anchor uses her current authored transform whether it came from play or from editing, and never requires a play session.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Stamp a waypoint where the player is standing (Priority: P1)

A developer designates an actor as the zone's player, positions her — by walking or
jogging during play, or by moving her in edit mode — and adds a waypoint at her
current transform. The new waypoint stands where she stands and faces the way she
faces, so a cutscene can later send a character to exactly that spot.

**Why this priority**: this is the requested capability. It turns the player actor
into a live authoring probe: positions are found by walking the world rather than by
flying the camera and nudging a point into place, which is the natural way to choose
walk-to and jog-to targets.

**Independent Test**: place the player at a distinctive position and rotation; add a
waypoint at the player; confirm the waypoint is at her position with the same rotation,
then walk her elsewhere and repeat.

**Acceptance Scenarios**:

1. **Given** a live designated player at a position and facing, **When** the developer
   chooses to add a waypoint at the player, **Then** a new waypoint exists at her
   position, with her full rotation, and with unit scale.
2. **Given** no live designated player, **When** the developer opens the creation
   menu, **Then** the player anchor is shown as unavailable and choosing it creates
   nothing.
3. **Given** a waypoint added at the player, **Then** it is selected and can be
   transformed, renamed, duplicated, deleted, linked, and undone exactly like one
   added at the origin or the camera.
4. **Given** the player's position after walking or jogging in play, **When** the
   developer returns from play to edit, **Then** adding at the player uses that position
   rather than resetting her to an earlier one.
5. **Given** a player placed or moved in edit mode and never played, **When** the
   developer adds a waypoint at the player, **Then** it uses her current editor
   position and facing.

---

### User Story 2 - One creation menu instead of a row of anchor buttons (Priority: P1)

Every way to create a waypoint is gathered into a single creation control that opens
a menu of anchors: origin, camera, and player. Adding another anchor later extends
that menu rather than adding another button to the panel.

**Why this priority**: the previous surface grew one button per anchor and was
already crowded with four creation and chain buttons. A menu makes the set of
anchors discoverable and bounded by the palette rather than by panel width.

**Independent Test**: open the waypoint section and confirm there is one creation
control; open it and confirm every anchor is offered in one list; add a waypoint from
each enabled entry and confirm the result.

**Acceptance Scenarios**:

1. **Given** the waypoint section, **When** the developer looks for creation actions,
   **Then** they are reached from one control with one menu, not one button per
   anchor.
2. **Given** the creation menu, **When** an anchor is chosen, **Then** exactly one
   waypoint is created at that anchor and is selected.
3. **Given** a zone with no waypoints, **When** the developer adds two waypoints at
   the camera and one at the player, **Then** they are named `Camera 1`, `Camera 2`,
   and `Player 1` respectively.
4. **Given** the zone's waypoint capacity is full, **When** the developer views the
   creation control, **Then** creation is unavailable and no waypoint is added.
5. **Given** a new anchor is introduced in future, **Then** it joins the same menu
   without displacing or adding a button beside the existing controls.

---

### User Story 3 - Author a chain as a property of the selected waypoint (Priority: P2)

Selecting a waypoint in the Inspector shows its chain controls: its next waypoint,
chosen from the zone's other waypoints or ended; and an extend action that creates a
new waypoint at an anchor and links it. Chain editing no longer requires a per-row link button or a
separate unlink button.

**Why this priority**: a chain link is a property of one waypoint. Editing it in the
Inspector, where the waypoint's other properties are edited, keeps the list panel to
creation and listing, removes the repeated per-row control, and lets the same selected
waypoint be extended repeatedly as the chain is walked out.

**Independent Test**: select a waypoint, choose another waypoint as its next, confirm
the chain order; choose the end entry and confirm the link is broken; extend at the
camera twice and confirm the new waypoints are linked in placement order.

**Acceptance Scenarios**:

1. **Given** a selected waypoint, **When** the developer chooses another live
   waypoint as its next, **Then** the chain continues at that waypoint as one undoable
   step.
2. **Given** a selected waypoint with a next, **When** the developer chooses the end
   entry, **Then** it has no next as one undoable step.
3. **Given** a selected waypoint, **When** the developer extends at an anchor, **Then**
   a new waypoint is created at that anchor, linked as its next, and selected, as one
   undoable step.
4. **Given** a selected waypoint that already has a next, **When** the developer extends
   at an anchor, **Then** the new waypoint sits between them: the selected waypoint
   points to the new one, and the new one points to the previous next, so the tail of
   the chain is preserved.
5. **Given** a repeated extension, **When** the chain is followed, **Then** its order
   matches the order the waypoints were placed.
6. **Given** the end of a chain is extended, **When** the developer undoes, **Then**
   the new waypoint is gone and the previous chain end is restored.
7. **Given** a middle waypoint of a chain was extended and the extension is undone,
   **Then** the chain is restored exactly as it was, including the link that ran past
   the removed waypoint.
8. **Given** a selected waypoint, **When** the next-chooser lists options, **Then**
   the waypoint itself is not offered.
9. **Given** a zone with no player, **When** the developer opens the extend menu,
   **Then** extending at the player is unavailable and extending at the camera remains
   available.

---

### Edge Cases

- No player is designated, or the designation no longer resolves to a live actor: the
  player anchor is unavailable and creates nothing.
- The player is coincident with an existing waypoint: creation is still allowed;
  waypoint names and positions are not required to be unique.
- The player actor is scaled: the waypoint takes her position and full rotation but
  unit scale, because a waypoint is a point, not a copy of the actor.
- A player-anchored waypoint copies the actor's full authored rotation, including any
  pitch or roll set in edit mode. Ops that point an actor at a waypoint flatten that
  rotation to yaw, so a tilted actor does not tilt a walk.
- The zone is at waypoint capacity: creation and extension are unavailable and
  report why; nothing is partially created and no history step is recorded.
- The selected waypoint's next is deleted: the chain stops there, as before; a link
  to a waypoint that no longer resolves does not retarget.
- Extending a middle waypoint inserts the new waypoint rather than replacing the link:
  the remainder of the chain is preserved, and undoing the extension restores the
  original link exactly.
- Automatic names can be made to collide by renaming; duplicate names are legal
  labels, both appear in the next-chooser, and the existing name-addressing convention
  applies. An automatic name still skips a number already in use, so creation itself
  does not introduce a duplicate.
- A waypoint whose automatic name was renamed, or deleted, frees that name: the next
  creation at the same anchor reuses the lowest free number for that prefix.
- A chain is edited only within the active zone; links never cross zones.
- Switching zones preserves waypoints, chains, and the player's current position in
  the zone that owns them.
- Adding at the player is an edit-mode action. Play mode itself remains read-only for
  authoring; edit mode reads the player's current authored transform, whether it was
  last written by playing or by editor manipulation.
- A player who has never been played still has a position: the transform she was
  placed or moved to in the editor. The player anchor is available for her.
- Every creation, link, extension, and undo remains a single step on the one editor
  history.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The editor MUST offer creating a waypoint at the live designated
  player's position, with the player's full authored rotation.
- **FR-002**: A player-anchored waypoint MUST use unit scale, independent of the
  player actor's scale.
- **FR-003**: The player anchor MUST be unavailable when no live player is
  designated, and MUST create nothing when chosen.
- **FR-004**: All waypoint creation anchors MUST be reached from one creation
  control whose menu lists the anchors; a new anchor MUST join that menu rather than
  add a sibling button.
- **FR-005**: The available anchors MUST include origin, editor camera, and player.
- **FR-006**: Creating a waypoint at any anchor MUST select the new waypoint and MUST
  be exactly one undoable step.
- **FR-007**: A selected waypoint MUST expose its next waypoint as an editable
  property: any other live waypoint of the active zone, or no next.
- **FR-008**: Choosing a next waypoint, changing it, and ending the chain MUST each
  be exactly one undoable step.
- **FR-009**: A waypoint MUST NOT be offered as its own next.
- **FR-010**: The selected waypoint MUST offer extending the chain with a new
  waypoint at an anchor; the new waypoint MUST be linked as its next, MUST be
  selected, and the whole effect MUST be one undoable step. When the selected
  waypoint already has a next, the new waypoint MUST be inserted before it, and the
  new waypoint's next MUST be the previous next, preserving the remainder of the
  chain.
- **FR-011**: Chain editing MUST be offered in the Inspector for the selected
  waypoint, and MUST NOT be repeated as a per-row control on the waypoint list.
- **FR-012**: Creation and extension MUST refuse at capacity without creating a
  partial waypoint or recording a step.
- **FR-013**: Adding at the player MUST use the active zone's player designation;
  waypoints remain zone data and links remain zone-scoped.
- **FR-014**: The feature MUST NOT change waypoint or chain semantics beyond the
  insertion rule in FR-010 and the automatic naming rule in FR-017: transforms,
  renaming, selection, duplicate, delete, undo, chain resolution, connector drawing,
  and scenario capture MUST behave as before.
- **FR-015**: Every anchor's creation and every chain edit MUST be undoable and
  redoable, restoring or removing the waypoint and every link involved. Undoing an
  insertion MUST restore the predecessor's original next and remove the inserted
  waypoint; redoing it MUST restore the inserted waypoint and its link to the
  original next.
- **FR-016**: The editor's play mode MUST remain non-authoring: the player position
  used is the one already recorded on the actor, not a new play-mode edit surface.
- **FR-017**: A newly created waypoint MUST be named from its anchor and a per-anchor
  counter: the anchor's name plus the lowest positive number not already used by a
  live waypoint with that prefix (for example `Player 1`, `Player 2`, `Camera 1`).
  Names remain editable, and a renamed waypoint frees its automatic name.
- **FR-018**: The player anchor MUST use the player's current authored transform,
  whether it was last written by playing or by editing, and MUST NOT require the
  player to have been played.

### Key Entities

- **Waypoint anchor**: a named source for a new waypoint's position and facing —
  origin, editor camera, or designated player — and the prefix of the created
  waypoint's automatic name.
- **Waypoint**: a named point with a position and facing, owned by a zone
  (unchanged from `specs/026-waypoints`).
- **Chain link**: the record that a waypoint continues at another waypoint of the
  same zone, or at no waypoint (unchanged from `specs/028-waypoint-chains`).
- **Player designation**: the zone's current player actor, whose current authored
  transform is the player anchor (unchanged from
  `specs/045-undoable-player-designation`).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can create a waypoint at the player in one action, with no
  follow-up position or facing adjustment needed (100% of such adds).
- **SC-002**: Every creation anchor is reachable from one creation control, and the
  number of visible creation buttons does not grow when an anchor is added (0
  additional buttons per new anchor).
- **SC-003**: A developer can author a complete chain — link, change next, end a
  chain, and insert a waypoint at an anchor — entirely from the selected waypoint,
  and the chain order matches placement order (100%).
- **SC-004**: Each creation, link change, and extension (including insertion before
  an existing next), and the undo of each, is exactly one history step (100%).
- **SC-005**: No waypoint is ever offered as its own next, and no chain link crosses
  zones (0 violations).
- **SC-006**: Existing waypoint and chain behaviour is unchanged: the existing test
  suite passes, and transform, rename, duplicate, delete, chain resolution, connector
  drawing, and scenario capture behave as before.
- **SC-007**: The gameplay loop keeps the project's target frame rate with the new
  authoring controls present.
- **SC-008**: Automatically named waypoints created through the menu are
  distinguishable in the next-chooser: no creation introduces a duplicate name among
  live waypoints with the same anchor prefix (0 duplicates).

## Assumptions

- This feature is authoring-only. It adds no runtime consumer of waypoints or chains
  and adds no persisted field or format; automatic names are ordinary `Name` values
  captured like any other authored name.
- The player is designated through the existing editor action (`specs/045`); a zone
  with no designation has no player anchor. Play-mode stamping is out of scope.
- The player's current transform survives the return to edit mode because the actor
  retains the transform the play systems last wrote; an actor never played has the
  transform the editor gave her, which is equally valid.
- The anchor palette is closed in this feature: origin, editor camera, and player.
  Future anchors (for example a selected actor or a viewport click) join the same
  creation and extend menus.
- The next-chooser follows the project's existing name-based chooser convention;
  duplicate waypoint names are legal labels and do not alter chain identity, which is
  by entity reference. Automatic anchor naming keeps the chooser readable in normal
  use, and rename remains available for meaningful names.
- "Selected waypoint" is the editor's one selection, so the Inspector shows chain
  controls alongside its other properties and they disappear when another kind is
  selected.
- Existing key bindings and shortcuts are unchanged; no new binding is required for
  this feature.
- The existing waypoint capacity and refusal reporting (`specs/080`) apply unchanged
  to the new anchor.
