# Feature Specification: Triggers

**Feature Branch**: `029-triggers`

**Created**: 2026-09-19

**Status**: Draft

**Input**: User description: "Trigger volumes. A trigger is a named cylinder placed in a zone that detects the player: while the player actor is inside it the trigger is marked triggered, a disabled trigger never detects, and with no player nothing is inside. A trigger may be interactable, in which case the closest one the player is inside is tracked and its prompt is shown while they are in it. The editor adds, selects, places, sizes, renames, duplicates and deletes triggers, drawing the volume as a cylinder wireframe; firing a cue from a trigger arrives with the cue engine."

**Origin**: roadmap phase 10 ("Story primitives"), the trigger slice, after
`specs/026-waypoints` and `specs/028-waypoint-chains` gave a zone its waypoints.
The behaviour follows the two reference implementations, which agree on the model:
`ocaml-game/lib/core/{entity,zone,systems}.ml` + `lib/editor/{viewport,inspector}.ml`
+ `specs/014-waypoints-triggers-cast`, and `odin-game/src/{entity,systems}.odin` +
`docs/TRIGGERS_CUE_PLAN.md`.

**Deferred by decision**: firing a cue. Both references make a trigger run a cue,
and this project has no cue engine yet (roadmap phase 9). The reference project
staged it the same way — its spec 014 deferred trigger→cue wiring and shipped
detection alone. So this feature delivers everything except the two fire hooks:
the rising edge that runs a cue on entry, and the interact key that runs the
closest interactable trigger's cue. Both become a few lines once cues exist.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Author trigger volumes in a zone (Priority: P1)

A developer adds a trigger to the active zone, moves it into place with the
gizmo, sizes it as a radius and a height, names it, duplicates it, and deletes it
— with undo and redo covering each edit, exactly as for the zone's other entities.
The volume is drawn in the viewport as a cylinder matching its radius and height,
so it can be placed relative to the geometry around it.

**Why this priority**: a trigger that cannot be placed and sized is not a trigger.
This is the same authoring surface the waypoints slice already established, and it
is independent of anything the gameplay does with a trigger.

**Independent Test**: add a trigger, move and rotate it, set a radius and height,
confirm the wireframe matches, rename it and confirm the Outliner row follows,
duplicate and delete it, and undo each step.

**Acceptance Scenarios**:

1. **Given** the active zone, **When** the developer adds a trigger, **Then** it
   exists with default size and editor flags, is listed in the Outliner's
   Triggers section, and becomes the selection.
2. **Given** a selected trigger, **When** the developer moves it with the gizmo,
   **Then** its position changes and the wireframe follows, as one undo step.
3. **Given** a selected trigger, **When** the developer changes its radius and
   height, **Then** the wireframe matches the new size, as one undo step per edit.
4. **Given** a selected trigger, **When** the developer renames it, **Then** the
   Inspector and its Outliner row show the new name, as one undo step.
5. **Given** a trigger, **When** the developer duplicates or deletes it, **Then**
   the copy or the deletion behaves as for any other entity, undoably.
6. **Given** a trigger in the active zone, **When** the viewport renders,
   **Then** its volume is drawn as a cylinder whose radius and height match its
   properties, distinguished from other entity markers.

---

### User Story 2 - Detect the player inside a trigger (Priority: P2)

While the game is playing, a trigger reports whether the player is inside it. The
volume is a cylinder: the player is inside when they are within the radius
horizontally and between the volume's base and its top vertically. A trigger that
is disabled never reports the player inside, and with no player designated (or no
live player) nothing is inside. Leaving clears it, so entering again is detectable.

**Why this priority**: detection is what makes a volume a trigger, and it is the
whole of what the gameplay does with one until cues arrive. It is independent of
the authoring surface, but a volume the author cannot see (US1) could not be
placed to test it.

**Independent Test**: place a trigger, designate an actor as the player, move the
player to just inside and just outside each boundary of the cylinder, and confirm
the reported state in each case; disable the trigger and confirm it reports
nothing; remove the designation and confirm the same.

**Acceptance Scenarios**:

1. **Given** a trigger and a player actor inside its radius and vertical span,
   **When** the game updates, **Then** the trigger reports the player inside.
2. **Given** a player outside the radius, above the top, or below the base,
   **When** the game updates, **Then** the trigger does not report them inside.
3. **Given** a player inside a trigger, **When** they leave and enter again,
   **Then** the trigger reports inside again, and each entry is distinguishable
   from remaining inside.
4. **Given** a disabled trigger, **When** the player stands inside it, **Then** it
   does not report them inside, and any state it held is cleared.
5. **Given** no designated player, or a designation whose actor is gone, **When**
   the game updates, **Then** no trigger reports anything inside.
6. **Given** the player is inside several triggers, **When** the game updates,
   **Then** each of them reports it, without exclusivity.

---

### User Story 3 - Interactable triggers and their prompt (Priority: P3)

A trigger can be marked interactable. Rather than firing on entry, the closest
interactable trigger the player is inside is tracked, and its prompt text is shown
while they are in it, so the author can see both that detection works and what the
interaction would say.

**Why this priority**: it completes the reference behaviour and gives this slice
its only in-play feedback, but a trigger is useful for detection (US2) without it.

**Independent Test**: place two interactable triggers with prompts, walk the player
from one into the other, and confirm the prompt switches to the nearer one and
disappears on leaving; mark one non-interactable and confirm it never becomes the
prompted one.

**Acceptance Scenarios**:

1. **Given** an interactable trigger with a prompt and the player inside it,
   **When** the game is playing, **Then** its prompt is shown.
2. **Given** two interactable triggers the player is inside, **When** the game
   updates, **Then** the nearer one is the tracked one and its prompt is shown.
3. **Given** an interactable trigger the player has left, **When** the game
   updates, **Then** no prompt is shown for it.
4. **Given** a non-interactable trigger, **When** the player is inside it,
   **Then** it is never the tracked interactable trigger.
5. **Given** an interactable trigger with no prompt text, **When** the player is
   inside it, **Then** nothing is shown for it.

---

### Edge Cases

- A player exactly on the boundary: inside is inclusive at the radius and at the
  base and top, so a boundary case is deterministic rather than incidental.
- A trigger with a radius of zero, or a height of zero: it contains nothing but
  the exact point, and nothing breaks (the wireframe degenerates to a line).
- A disabled trigger that the player is standing in: it reports nothing inside,
  and re-enabling re-detects them on the next update.
- The player leaving the world (their actor deleted, or the designation dropped):
  every trigger clears, so no stale "inside" survives a play session.
- A trigger overlapping another: both report the player, and for interactables the
  nearer wins. No trigger shadows another.
- Switching zones: triggers are zone data, so an inactive zone's triggers are not
  updated and not drawn, and its state is not carried into the active zone.
- The player standing still inside a trigger across many frames: it stays inside,
  and entry is reported once rather than every frame.
- Editing a trigger while playing: the editor is passive during play (panels are
  hidden), so nothing edits a trigger mid-frame; the size used is the one it had.
- A trigger is not a collider: the player walks through it, it never blocks or
  pushes anything, and it takes part in no physics query.
- Renaming, moving, or re-sizing a trigger does not change what is inside it at
  that instant beyond what the new volume implies.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A trigger MUST be a zone entity with a name, a position, an
  orientation, the editor flags shared by other entities, a radius, a height, an
  interactable flag, a prompt, and a per-frame "player inside" state; it MUST be
  addressable by the same kind of reference as the zone's other entities.
- **FR-002**: The trigger volume MUST be a cylinder: its position is the centre of
  its base, it extends upward by its height, and its cross-section is its radius.
  Its orientation MUST NOT affect containment (a cylinder is radially symmetric);
  it exists so the volume can be aimed consistently with other entities.
- **FR-003**: The editor MUST add, select, move, rotate, re-size, re-name,
  duplicate, and delete triggers, and every one of those edits MUST be a single
  undoable step, redoable.
- **FR-004**: The editor MUST draw every trigger in the active zone as a cylinder
  wireframe matching its radius and height, distinguishable from other entity
  markers and highlighted when selected.
- **FR-005**: The gameplay MUST report, for each trigger of the active zone,
  whether the player is inside it: within the radius horizontally and between the
  base and the top vertically, inclusively at the boundaries.
- **FR-006**: A trigger that is disabled MUST report the player outside, and MUST
  clear any state it held; with no designated player, or a designation that no
  longer resolves, every trigger MUST report the player outside.
- **FR-007**: Remaining inside a trigger MUST be distinguishable from entering it,
  so that a once-per-entry action can be derived from the reported state.
- **FR-008**: An interactable trigger MUST NOT be treated as firing on entry; the
  closest interactable trigger the player is inside MUST be identifiable, with
  distance measured to the volume, and MUST be cleared when the player leaves or
  the trigger stops being interactable or enabled.
- **FR-009**: While playing, the prompt of the tracked interactable trigger MUST be
  shown, and MUST NOT be shown when there is none. A prompt that is empty MUST
  show nothing.
- **FR-010**: This feature MUST NOT run any cue: firing on entry and interacting
  with the tracked trigger MUST be left to the cue engine, to be added as the
  action a trigger performs. Nothing else about the detection behaviour may be
  deferred with them.
- **FR-011**: Triggers MUST take part in no physics and MUST carry no renderer,
  collider, or asset; adding, moving, or deleting one MUST NOT affect collision,
  geometry rendering, or any other entity.
- **FR-012**: Detection MUST require no graphics device: it MUST be exercisable
  over a zone and a player position with no window and no GPU.
- **FR-013**: Triggers MUST be zone data: preserved across a zone switch, drawn
  only for the active zone, and updated only for the active zone.
- **FR-014**: A trigger's size MUST be independent of its position and rotation
  and MUST NOT be affected by the transform's scale.

### Key Entities *(include if feature involves data)*

- **Trigger**: a named cylindrical volume in a zone — where its base sits, how far
  it reaches up, how wide it is, whether it is interactable, the prompt it shows,
  and its per-frame "player inside" state. It composes the shared entity base
  (name, transform, editor flags) and carries nothing else: no renderer, no
  collider, no asset, no cue yet.
- **Player inside state**: whether the designated player is within the volume as
  of the last update. It is runtime state, not authored data, and is never
  recorded as an edit.
- **Tracked interactable trigger**: the nearest interactable trigger the player is
  inside, which is what a prompt is shown for and what an interaction would act
  on once triggers can run cues.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can add, place, size, name, duplicate, and delete a
  trigger, and undo and redo each of those, entirely from the editor (100%).
- **SC-002**: Every trigger in the active zone is visible in the viewport as a
  cylinder whose radius and height match its properties (100%).
- **SC-003**: For any player position, the reported inside state matches the
  cylinder containment rule, at and around each boundary (100% of positions
  tested).
- **SC-004**: Entry is distinguishable from remaining inside, and standing still
  inside a trigger across many updates never reports as repeatedly entering
  (0 false entries).
- **SC-005**: A disabled trigger, and any trigger with no live designated player,
  never report the player inside (0 occurrences).
- **SC-006**: The tracked interactable trigger is the nearest one the player is
  inside, and its prompt is shown exactly while it is tracked (100%).
- **SC-007**: The gameplay keeps the project's target frame rate (60 FPS on the
  target platform) with triggers present, and trigger detection allocates nothing
  per frame.
- **SC-008**: Existing behaviour is unchanged: the existing test suite passes,
  no other entity kind is affected, and triggers collide with nothing.

## Assumptions

- **Cue wiring is the cue engine's job.** This feature reports detection; the two
  actions a trigger performs — running a cue on entry, and running the tracked
  trigger's cue on interaction — arrive with the cue engine (roadmap phase 9). A
  trigger therefore has no cue property yet, and neither the rising edge nor the
  interact key is implemented here.
- A trigger's "disabled" state is the shared entity enabled flag: it currently
  means nothing anywhere else in the port, and a second per-kind flag would
  duplicate it.
- Inside is inclusive on the boundary: the radius comparison and the base/top
  comparison use `<=`, so a boundary position has one defined answer.
- Only the designated player is detected. Other actors, props, and the camera are
  irrelevant to a trigger.
- Triggers may overlap freely and there is no exclusivity: several may contain the
  player, and the interactable tracking resolves only among them.
- A trigger has no collider and is not clickable in the viewport, exactly as
  waypoints are not (`specs/026-waypoints`): it is selected from the Outliner.
- The prompt is shown by the editor's play-mode overlay, which is also where the
  author sees which triggers the player is inside, since nothing else in the
  gameplay reports it.
- Triggers are in-memory only, like the rest of a zone's contents; persistence is
  a later phase.
- The reference projects' `enabled` flag, `interactable` flag, prompt text, and
  cylinder geometry are adopted as-is; their cue ownership and lifetime machinery
  is not, because it exists only to hand a cue to the runtime, which this feature
  does not do.
