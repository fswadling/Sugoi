# Feature Specification: Per-Actor Physics Modes and Properties

**Feature Branch**: `102-actor-physics`

**Created**: 2026-10-17

**Status**: Draft

**Input**: User description: "Currently actors always have physics that cause them to bounce around when we collide with them. Sometimes actors we place will simply be NPCs that we place and don't expect to move. We will need the ability to change the physics types and properties of placed actors."

**Scope**: this spec extends the actor entity defined by
`specs/064-entity-kind-state-split` and the actor motion ops defined by
`specs/066-actor-motion-cue-ops` and `specs/074-retire-cast`. It adds an
authored per-actor physics mode and a small set of dynamic properties, plus the
editor controls, undo, and persistence for them. It does not change actor
assets, capsule sizing, animation, naming, cue addressing, zone staging, player
locomotion controls, or the physics backend.

## Clarifications

### Session 2026-10-17

- Q: When a kinematic NPC is moved by a cue and the path is blocked by a wall or prop, should she stop at the obstruction or pass through it? → A: Stop at the obstruction; kinematic cue movement preserves the existing collision-aware move behaviour.
- Q: Should actors created at runtime by the cue's add-actor op default to kinematic (standing NPC) or dynamic (the old pushable behaviour)? → A: Kinematic, consistent with newly placed actors.
- Q: Should an actor in the NoPhysics mode still be click-selectable in the 3D viewport, or only from the Outliner? → A: Viewport-selectable in every mode, using an editor-only pick aid for actors with no collision volume.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Place an actor that stands its ground (Priority: P1)

An author places an actor asset as scenery or as a standing NPC, enters play mode,
and walks into her. She stays where she was placed instead of being shoved across
the floor. The author can choose how solid she is: a kinematic NPC who can still be
moved by a cue, or a purely decorative actor the player can walk through.

**Why this priority**: this is the reported problem. Every actor currently reacts to
collisions as a movable body, so an NPC meant to stand still is pushed around by the
player and by other actors. Without this, placed actors cannot be used as stable
scene furniture.

**Independent Test**: place an actor, set each physics mode in turn, enter play mode,
walk into her, and confirm displacement and blocking behaviour match the chosen mode.
Confirm a newly placed actor defaults to the kinematic NPC mode.

**Acceptance Scenarios**:

1. **Given** a newly placed actor with no explicit physics choice, **When** the author
   enters play mode and walks into her, **Then** she does not move and the player is
   blocked by her.
2. **Given** an actor set to the NoPhysics mode, **When** the player walks into her
   position, **Then** the player passes through with no blocking and no displacement.
3. **Given** any physics mode, **When** the author drags, rotates, or scales the actor
   in the editor, **Then** she is repositioned and remains in that mode.
4. **Given** an actor in each mode, **When** the author toggles Show Collider, **Then**
   the overlay reflects whether she has a collision volume and its shape.

---

### User Story 2 - Tune how a dynamic actor feels (Priority: P1)

For actors that should be pushable — a character who can be barged aside, a physics
toy, a crowd member — the author tunes how heavy she is, how much she slides, how
bouncy she is, and how strongly gravity pulls her. The changes are applied
immediately and are observed when play begins; editing them does not move her on
screen.

**Why it matters**: a single "pushable" behaviour is not enough once actors can be
non-pushable. Authors need to distinguish a heavy immovable-feeling character from a
light one, and to remove bounce without making the actor completely unsolid.

**Independent Test**: place two dynamic actors, give them different mass and
restitution, enter play mode, collide with both, and confirm they respond differently.
Edit a property and confirm the actor's position and facing do not change.

**Acceptance Scenarios**:

1. **Given** a dynamic actor, **When** the author edits mass, friction, restitution, or
   gravity factor, **Then** the actor's position, facing, scale, and selection do not
   change.
2. **Given** two dynamic actors with different mass, **When** the player collides with
   each, **Then** the heavier one is displaced less than the lighter one.
3. **Given** a dynamic actor with restitution set to zero, **When** she collides with
   the player or the floor, **Then** she does not bounce.
4. **Given** a non-dynamic mode, **When** the author views the property controls,
   **Then** properties that have no effect in that mode are disabled or hidden.
5. **Given** any property drag, **When** the control is released, **Then** the edit is
   recorded as exactly one undoable step.

---

### User Story 3 - Keep kinematic NPCs and the player playable (Priority: P2)

An NPC marked as kinematic can still be walked along a waypoint path or turned to face
a waypoint by a cue. An actor designated as the player can still move under player
input, regardless of what mode she had before being designated.

**Why this priority**: making NPCs immovable must not remove the existing motion and
rotation cue ops. The player is the one actor that must always respond to input, so
the player designation and the physics mode must agree.

**Independent Test**: mark an actor kinematic, run a move and a rotate cue against her,
and confirm both complete. Designate a kinematic actor as the player and confirm she
becomes movable and stays movable after the step is undone and redone.

**Acceptance Scenarios**:

1. **Given** a kinematic actor, **When** a move cue runs against her, **Then** she follows the authored path, is stopped by geometry that obstructs it, and arrives as before when the path is clear.
2. **Given** a kinematic actor, **When** a rotate cue runs against her, **Then** she
   turns as before.
3. **Given** a NoPhysics actor, **When** a move or rotate cue runs against
   her, **Then** the op is a reported no-op and does not corrupt her state.
4. **Given** a non-dynamic actor, **When** the author designates her as the player,
   **Then** her mode becomes dynamic as part of the same undoable step and player
   input moves her.
5. **Given** the designated player, **When** the author edits the actor's physics mode,
   **Then** the editor does not offer a non-dynamic mode while she remains the player.
6. **Given** a cue adds an actor at runtime, **Then** she is created in the default
   kinematic mode, and a cue that enables her as the player promotes her to dynamic.

---

### User Story 4 - Physics choices survive editing and saving (Priority: P2)

An author sets an actor's physics, duplicates her, forks the scenario, undoes and
redoes, saves, and reloads. The mode and properties come back exactly. Projects saved
before this feature still load and keep their actors behaving as they did.

**Why this priority**: physics is authored scene data. If it is lost on duplicate,
fork, undo, or reload, every author has to re-enter it, and a scenario cannot
reproduce a scene.

**Independent Test**: set distinct physics on several actors, duplicate one, capture
the zone into a scenario and fork it, undo and redo each edit, save and reload, then
confirm every actor's mode and properties. Load a project saved before this feature
and confirm its actors are dynamic.

**Acceptance Scenarios**:

1. **Given** an actor with edited physics, **When** the author undoes the edit, **Then**
   the previous mode and properties return, including the collision behaviour; redo
   reapplies them.
2. **Given** an actor with edited physics, **When** she is duplicated, **Then** the copy
   has the same mode and properties.
3. **Given** a zone containing actors with non-default physics, **When** it is captured
   into a scenario and forked, **Then** the materialised actors have the same mode and
   properties.
4. **Given** a project with non-default physics, **When** it is saved and reloaded,
   **Then** every actor's mode and properties are unchanged.
5. **Given** a project saved before this feature, **When** it is loaded, **Then** every
   actor is dynamic and behaves as she did before.
6. **Given** an actor deleted and restored by undo, **Then** her physics is restored
   with her.

---

### Edge Cases

- The designated player is set to, or loaded with, a non-dynamic mode. The player
  designation always implies dynamic; designating an actor promotes her, and a
  designation restored with a zone or by undo/redo is promoted when it is restored or
  the zone becomes active.
- A kinematic actor's cue path is obstructed: she stops at the obstruction, as a
  dynamic actor does, and does not pass through it.
- A NoPhysics actor has no runtime collision volume at all. Show Collider shows no
  capsule, motion cues are reported no-ops, and the player passes through. She remains
  viewport-selectable through an editor-only pick aid that never blocks or is blocked
  by anything.
- The actor's collision capsule is derived from her asset, not from these properties.
  A scale change still rebuilds or repositions the collision volume as before.
- Changing a dynamic actor's mass or restitution while she is resting must not make her
  jump, sink, or visibly shift. Her position and facing are preserved across the body
  rebuild.
- A kinematic actor placed in mid-air stays where she is placed; gravity does not pull
  her down.
- Physics edits are edit-mode only. Play mode remains non-authoring, and the controls
  are not offered while playing.
- Actors in an inactive zone keep their mode and properties when the zone is left and
  returned to.
- Duplicate actor names, capacity limits, selection, and the existing actor cue ops are
  unchanged.
- The property values are bounded to sensible ranges so an authored zero or negative
  value cannot produce an invalid collision body. The Inspector enforces the bounds,
  and an out-of-range value in a hand-edited project is reported through the project
  error path rather than installed.
- A physics edit that changes nothing records no history step.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every actor's authored state MUST include a physics mode with three
  values: Dynamic (gravity and collisions move her), Kinematic (solid, stands still,
  but cue-driven move and rotate ops work on her), and NoPhysics (no collision volume).
- **FR-002**: Every actor's authored state MUST include dynamic physics properties:
  mass, friction, restitution, and gravity factor, each with a defined default and a
  bounded range.
- **FR-003**: A newly placed actor MUST default to the Kinematic mode; an actor loaded
  from a project saved before this feature MUST be dynamic.
- **FR-004**: The editor MUST expose the physics mode and the dynamic properties for
  the selected actor in the Inspector.
- **FR-005**: Kinematic actors MUST NOT be displaced by collisions with the
  player or with other actors, and they MUST block those moving bodies.
- **FR-006**: A NoPhysics actor MUST NOT block or be blocked by any body.
- **FR-007**: Dynamic actors MUST be displaced by collisions and pulled by gravity in
  proportion to the authored properties.
- **FR-008**: Changing any physics setting MUST preserve the actor's position, facing,
  scale, name, asset, animation state, visibility, and selection.
- **FR-009**: Every committed physics edit MUST be exactly one undoable step; undoing
  it MUST restore the previous mode and properties and the corresponding collision
  behaviour, and redoing it MUST reapply them. An edit that changes nothing MUST record
  no step.
- **FR-010**: Duplicating an actor MUST copy her complete physics setting.
- **FR-011**: Scenario capture, fork, and zone staging MUST preserve each actor's
  complete physics setting.
- **FR-012**: Saving and reloading a project MUST preserve each actor's complete
  physics setting. A project saved before this feature MUST load without conversion and
  MUST treat its actors as dynamic.
- **FR-013**: The editor's player designation MUST imply the dynamic mode: designating
  a non-dynamic actor MUST promote her to dynamic in the same undoable step, and the
  Inspector MUST NOT offer a non-dynamic mode for the currently designated player.
- **FR-014**: A player designation restored by undo/redo, or restored or activated
  with a zone, whose actor is not dynamic MUST promote that actor to dynamic.
- **FR-015**: Move and rotate cue ops MUST work on Dynamic and Kinematic actors. A
  Kinematic actor's cue-driven move MUST stop at geometry that obstructs the path rather
  than passing through it. Both ops MUST be reported no-ops on NoPhysics actors.
- **FR-016**: The runtime actor-add cue op MUST create the actor in the default Kinematic
  mode; the runtime player-enable cue op MUST promote the actor to dynamic.
- **FR-017**: The Show Collider overlay MUST continue to describe the actor's actual
  collision volume for every mode.
- **FR-018**: Physics settings MUST be editable only in edit mode, consistent with the
  editor's existing non-authoring play mode.
- **FR-019**: Actors in an inactive zone MUST retain their mode and properties when the
  zone is left and returned to.
- **FR-020**: The feature MUST NOT change actor capsule shape derivation from the
  asset, actor animation, naming, cue addressing, deletion, zone switching, or player
  locomotion controls.
- **FR-021**: The feature MUST NOT add a per-frame cost proportional to the number of
  actors beyond the existing actor sync pass.
- **FR-022**: An actor MUST remain selectable in the viewport in every physics mode.
  Selection MUST NOT depend on the actor's runtime collision mode, and any editor-only
  selection aid for a NoPhysics actor MUST NOT participate in gameplay collision.
- **FR-023**: Every actor mode MUST keep the actor upright. No mode may allow the actor
  to tip over; where gravity applies, it may only move her along the up axis.

### Key Entities

- **Actor physics setting**: authored data on an actor — a mode (Dynamic, Kinematic,
  NoPhysics) plus mass, friction, restitution, and gravity factor. Part of
  the actor's authored state, so it is captured by undo, duplicate, and scenario
  staging.
- **Actor collision volume**: the capsule derived from the actor's asset and scale.
  It exists for Dynamic and Kinematic modes and is absent for NoPhysics. Its
  shape is unchanged by this feature.
- **Physics mode**:
  - *Dynamic* — gravity and collisions move her.
  - *Kinematic* — solid; collisions do not move her; cues can move and rotate her,
    with cue-driven translation stopped by geometry.
  - *NoPhysics* — no collision volume; the player passes through.
- **Player designation**: the zone's current player actor. It implies the dynamic
  mode, and moving the designation is one undoable step that carries any required mode
  change.
- **Project record**: the saved scenario library. Each actor's physics setting is part
  of its recorded authored state; an older record without one means dynamic.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An actor in the Kinematic mode is displaced by zero units under
  sustained collision from the player or another actor (0 displacement).
- **SC-002**: An actor in the NoPhysics mode allows the player to pass through with
  zero blocking contacts.
- **SC-003**: 100% of newly placed actors begin in the Kinematic mode.
- **SC-004**: 100% of actors loaded from a pre-feature project are dynamic and behave
  as they did before.
- **SC-005**: 100% of physics edits undo and redo to exactly the previous and next
  settings, with no change to the actor's position or facing during the edit.
- **SC-006**: 100% of physics settings survive duplicate, scenario fork, save, and
  reload.
- **SC-007**: A dynamic actor's response to collision is visibly different for
  materially different mass or restitution values; equal values produce equal responses.
- **SC-008**: Each property drag records exactly one history step, and a no-op edit
  records none (0 violations).
- **SC-009**: The existing test suite passes, and existing actor behaviour not named in
  this feature is unchanged.
- **SC-010**: The gameplay loop keeps the project's target frame rate with the feature
  present.
- **SC-011**: A Kinematic actor moved along a path blocked by geometry stops at the
  obstruction (0 pass-throughs).
- **SC-012**: 100% of actors are viewport-selectable in every physics mode, including
  NoPhysics actors (0 unselectable actors).
- **SC-013**: No actor tips over in any mode (0 tipped actors).

## Assumptions

- The mode names follow Jolt's motion types for the bodies: Dynamic and Kinematic.
  NoPhysics is the project's extension for an actor with no body at all.
- The three modes are the scope: Dynamic, Kinematic, and NoPhysics. A never-moving
  NPC is a Kinematic actor with no move cues; non-moving scenery is a prop or
  environment, not an actor. Finer distinctions (per-axis locking, collision layers,
  sensor volumes) are out of scope.
- Newly placed actors default to Kinematic. This is the behaviour change the feature
  exists for: an author who wants a pushable actor chooses Dynamic.
- Actors in projects saved before this feature default to dynamic, preserving their
  previous behaviour. No project format conversion is required.
- The player designation always implies dynamic. Designating promotes; a non-dynamic
  mode is not offered for the current player.
- Kinematic actors are solid but do not fall. They stand where placed and are moved only
  by cues; cue-driven translation is collision-aware and stops at geometry.
- Viewport selection is independent of the runtime physics mode. A NoPhysics actor
  keeps an editor-only selection aid that has no gameplay collision.
- The exposed dynamic properties are mass, friction, restitution, and gravity factor.
  Mass and gravity factor are dynamic-only; friction and restitution remain contact
  properties for Kinematic bodies. The Inspector hides mass and gravity factor for
  Kinematic and all four for NoPhysics. Damping, velocity limits, collider shape,
  collider offset, and per-axis rotation locking are out of scope.
- Actor rotation remains upright-locked as it is today; actors do not tip over.
- Show Collider keeps its current meaning: it visualises the actor's capsule, and a
  NoPhysics actor has no capsule to show.
- The runtime actor-add cue op has no physics field. It creates the actor in the
  default Kinematic mode, and the player-enable cue op promotes the actor when needed.
- Physics edits are edit-mode only; play mode stays non-authoring.
- Existing capacity, selection, undo, duplicate, scenario capture, and persistence
  paths are reused; no new entity kind or zone-level setting is introduced.
