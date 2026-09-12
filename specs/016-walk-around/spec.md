# Feature Specification: Walk Around

**Feature Branch**: `016-walk-around`

**Created**: 2026-09-12

**Status**: Draft

**Input**: User description: "Phase 7 of the roadmap: player locomotion + camera follow; walk the character around the environment with physics. A deliberately selected player binds to one of the zone's actors; when no player is selected the locomotion and camera-follow systems do nothing. Tank controls, a separate following camera, and idle/jog animation."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Bind an actor as the player and walk around (Priority: P1)

A developer selects an actor in the editor and marks it as the player with a
deliberate **Set as Player** action, then presses the play key. That actor becomes
the character the developer controls: `W`/`S` move it forward and backward along
its facing, `A`/`D` turn it left and right. Movement is physical — the character
resolves against the environment's colliders, stands on the ground under gravity,
and cannot pass through solid geometry. Pressing the play key again returns to the
editor, leaving the character exactly where it stopped.

**Why this priority**: this is the playable backbone the rest of the project hangs
off. Without a character you can drive through the world, nothing downstream
(zones, cues, story primitives) can be experienced.

**Independent Test**: set an actor as the player, enter play, drive it around the
environment with `W`/`A`/`S`/`D`, and confirm it moves, turns, collides with the
environment, stays on the ground, and that leaving play returns to the editor with
the character at its walked-to position.

**Acceptance Scenarios**:

1. **Given** an actor selected in the editor, **When** the developer invokes
   *Set as Player* and enters play mode, **Then** that actor is controlled by
   `W`/`A`/`S`/`D` and the other actors are not.
2. **Given** a player is bound and play mode is active, **When** `W` is held,
   **Then** the player moves forward along its facing at a constant speed.
3. **Given** a player is bound and play mode is active, **When** `A` or `D` is
   held, **Then** the player turns left or right in place without translating.
4. **Given** a player moving toward a wall or prop, **When** it reaches the
   collider, **Then** it is blocked and does not pass through it.
5. **Given** a player above the ground, **When** play mode runs, **Then** it falls
   under gravity and comes to rest standing on the ground.
6. **Given** a player has walked to a new position, **When** play mode is exited,
   **Then** the editor shows the character at that same position and orientation.

---

### User Story 2 - A following camera (Priority: P2)

While the developer walks the character, the view is a third-person camera that
stays behind and above the player, tracking its position and facing. Looking after
the character is handled by its own system, so it can be reasoned about (and later
replaced or extended) independently of movement.

**Why this priority**: without a following view, walking is navigable only by
coincidence; it is what turns locomotion into a playable experience. It is
independently valuable even before animation work.

**Independent Test**: bind a player, enter play, and confirm the camera remains
behind and above the player throughout movement and turning, without any manual
camera input.

**Acceptance Scenarios**:

1. **Given** a player is bound and play mode is active, **When** the player moves,
   **Then** the camera follows so the player stays framed from behind.
2. **Given** a player is bound and play mode is active, **When** the player turns,
   **Then** the camera rotates to stay behind the new facing.
3. **Given** play mode is active, **When** the developer provides no movement
   input, **Then** the camera remains settled behind the stationary player.

---

### User Story 3 - Idle/jog animation from held keys (Priority: P3)

The character reads as alive while being driven: it stands idle when no movement
key is held and plays its jog cycle while a movement key is held, switching as the
developer presses and releases. The animation follows the held movement keys, so a
character pressed against a wall while holding a movement key still reads as
moving.

**Why this priority**: it is the visible polish that makes walking legible, but
movement and camera already deliver the core value without it.

**Independent Test**: bind a player, enter play, hold a movement key and release
it, and confirm the character plays the jog cycle while held and returns to idle
when released.

**Acceptance Scenarios**:

1. **Given** a player is bound and no movement key is held, **When** frames
   advance, **Then** the character displays its idle animation.
2. **Given** a player is bound and a movement key is held, **When** frames
   advance, **Then** the character displays its jog animation.
3. **Given** a held movement key is released, **When** frames advance, **Then**
   the character returns to the idle animation.

---

### Edge Cases

- The bound actor is deleted, or its slot is recycled, while editing: the binding
  stops resolving, so entering play is idle rather than driving a stale or
  recycled slot. No explicit clearing step is required.
- *Set as Player* is offered only for actors; a prop, environment part, or other
  kind shows no such action, and the binding helper rejects a non-actor rather
  than silently binding one.
- Setting a new player while one is already bound replaces the binding.
- Play mode is entered while the player is above the ground (e.g. it was raised in
  the editor): the character falls and lands normally.
- Play mode is toggled on and off repeatedly: each entry resumes control of the
  same actor at its current position without resetting it.
- A player walks off the edge of the environment's floor: it falls; there is no
  requirement to recover it.
- The player turns while adjacent to a collider: turning does not push it through
  the collider.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The editor MUST provide a deliberate *Set as Player* action on an
  actor that binds that actor as the player, replacing any existing binding.
- **FR-002**: The player binding MUST be held at the top level of the gameplay
  state as the actor's slot index and generation, so it resolves only while that
  exact actor incarnation is alive.
- **FR-003**: The player binding MUST be play-binding state, not scene data; it is
  not part of the undo history and does not itself change the scene.
- **FR-004**: The *Set as Player* action MUST be shown only for actors; the
  binding helper MUST reject a non-actor target loudly rather than silently
  accepting it.
- **FR-005**: When no player is bound, or the binding no longer resolves to a live
  actor, the player systems MUST do nothing; deleting or recycling the actor's
  slot requires no explicit clearing.
- **FR-006**: While in play mode with a player bound, `W`/`S` MUST move the player
  forward/backward along its current facing and `A`/`D` MUST rotate its facing,
  without lateral strafing.
- **FR-007**: The player MUST collide with the environment, placed props,
  environment parts, and other actors, and MUST NOT pass through solid geometry.
- **FR-008**: The player MUST be subject to gravity and MUST come to rest on
  ground geometry when it lands.
- **FR-009**: Camera follow MUST be a system separate from locomotion, deriving the
  camera's placement from the player's position and facing (third-person, behind
  and above), and MUST do nothing when no player is bound.
- **FR-010**: The gameplay update MUST run the play chain in a fixed order —
  player input, the physics step, transform sync, then camera follow — only while
  in play mode. Transform sync MUST cover every actor's body, not only the player's,
  because all actors are simulated; the player systems do nothing with no player bound,
  but the world simulation continues. While editing, nothing is simulated.
- **FR-011**: Entering and leaving play mode MUST NOT reposition or reset the
  player; the editor observes the player wherever it was left.
- **FR-012**: The character's animation MUST display idle when no movement key is
  held and jog while a movement key is held, driven by the held key state.
- **FR-013**: Play mode MUST remain toggleable from the editor with the existing
  play/edit control, and the editor MUST remain a passive view of the current game
  state while play is inactive.

### Key Entities *(include if feature involves data)*

- **Player binding**: the deliberate association between the gameplay state and one
  of the zone's actors; absent means no player is driven.
- **Player body**: the player actor's dynamic capsule; while playing it is the
  source of truth for the player's position, velocity, and upright facing.
- **Camera-follow state**: the parameters of the following view — its orientation
  and its offset behind and above the player.
- **Player actor**: an actor in the zone that has a physical presence and an
  animation set that includes idle and jog.
- **Zone / environment**: the world, its static geometry, placed props, environment
  parts, and other actors that the player collides with.
- **System chain**: the fixed-order sequence of player systems run each gameplay
  frame.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With an actor bound as the player, a developer can drive it around
  the environment and it remains on the ground and outside solid geometry in 100%
  of attempts.
- **SC-002**: The following camera keeps the player framed from behind and above
  throughout movement and turning, requiring no manual camera input (100% of
  movement).
- **SC-003**: With no player bound, entering play mode drives no character and leaves
  the camera unchanged, while the world simulation continues (100%).
- **SC-004**: Leaving play mode shows the player at the exact position and facing
  it had when play was exited (100%).
- **SC-005**: The character displays idle when no movement key is held and jog
  while a movement key is held (100% of observed frames).
- **SC-006**: Movement responds to key input without perceptible delay (the
  response is visible in the same frame the key is held).
- **SC-007**: The gameplay loop sustains the project's target frame rate while
  walking (60 frames per second on the target platform).

## Assumptions

- There is a single active zone and a single player at a time.
- The player's actor asset provides both an idle and a jog animation clip, and
  those are the only locomotion clips needed (there is no separate walk or run).
- Movement is a single constant jog speed; sprinting, jumping, and crouching are
  out of scope for this feature.
- Controls are tank-style (turn-to-face, move-along-facing); mouse-look and
  strafing are out of scope.
- All actors are physical bodies: the player collides with them and may nudge them
  on contact. Broader dynamic-body interactions (carrying, standing on a pushed
  actor) are out of scope.
- The player stays upright while walking and cannot be knocked over; actors are
  simulated physically (gravity and collision).
- The world is simulated throughout play mode (player or not) and not while editing;
  a play session with no player bound simulates the world but drives no character.
- The player binding is in-memory only and is not persisted across sessions.
- The editor remains a passive observer; play-mode movement is not recorded as
  undoable scene edits.
