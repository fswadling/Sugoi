# Feature Specification: Actor Motion & Animation Cue Ops

**Feature Branch**: `066-actor-motion-cue-ops`

**Created**: 2026-10-04

**Status**: Draft

**Input**: User description: "lets move onto the rest of the actor cues in odin game that aren't in fsharp game yet" — the movement ops (`MoveActor`, `RotateActor`), the player toggles (`EnablePlayer` / `DisablePlayer`), and the animation/morph blends (`Animate`, `CrossFade`, `Morph`, `CrossMorph`).

## Origin

`specs/064-actor-cue-ops` delivered the actor **lifecycle** (`SetActor`, `RemoveActor`) and explicitly deferred
"movement, the player toggle and animation/morph ops" (its FR-010). This feature is the rest of odin's actor family.

The reference (`odin-game/src/cue.odin`) has, keyed by a free-string `actor_name`:

- **Movement** — `MoveActor { waypoint_path, speed, idle_anim, moving_anim }`: build a spline from the actor's
  current position through the waypoint chain to its end, walk it over `length / speed`, turn to face it, and
  blend `moving_anim` down to `idle_anim` as it arrives. `RotateActor { target_waypoint, duration }`: turn the
  actor in place from its current yaw to the waypoint's yaw over `duration`.
- **Player locomotion** — odin's `EnableActorLocomotion { actor_name }`: attach the play controller to that actor
  (and seed its idle/run clips); `DisableActorLocomotion`: detach it if that actor is the one driving. The F#
  port names this the player toggle (`EnablePlayer` / `DisablePlayer`).
- **Animation/morph blends** — `Animate { anim_name, weight, duration }` and `CrossFade { from_anim, to_anim,
  weight, duration }`; `Morph` / `CrossMorph` the same over morph slots. Each interpolates slot weights over a
  duration (`Cue_BlendState`).

The F# pieces these build on already exist: the actor's per-instance slot weights and their setters
(`ActorRender.setClipWeight` / `setMorphWeight`, `specs/036`/`specs/037`), the spline maths the camera flight
already uses (`core/Maths.fs`), and the play-mode player binding (`zone.Player`, `world/Systems.fs`).

Addressing follows `specs/064`: an actor op names a `Roster.Character` (the cast index resolves her place), not
a free string. The non-actor toggles odin also has (`Enable/DisableCameraFollow`, `Enable/DisableInteractions`,
`Enable/DisableTrigger`) and `Print` are **not** actor ops and are out of scope here.

## Clarifications

### Session 2026-10-04

- Q: One feature for all three groups, or a slice? → A: **All three in this feature**, implemented in priority
  order — animation/morph blends (P1), movement (P2), the player toggle (P3).
- Q: What does "the player" mean here? → A: **The player binding** — `EnablePlayer` binds the actor as
  the zone's player, exactly the editor's "Set as Player", and the camera follows her (as it does for that
  designation); `DisablePlayer` clears it. Turning the camera off is a separate op, out of scope.
- Q: An unknown clip/morph name? → A: **Perform nothing** — the op guards with the asset's clip/morph lookup,
  so a cue naming a clip the asset lacks does not fault and a cue stays safe to re-run.
- Q: How does the editor choose a `MoveActor` path / `RotateActor` waypoint, given the op names no place? →
  A: **A combo over the active zone's waypoints** (as the camera ops offer) — the author works in the zone the
  cue acts on, so the active zone is the one whose waypoints to offer. The name is still *stored* as a string
  and resolved in the actor's own zone at run time, so the chooser is a convenience, not a coupling.
- Q: How does the editor choose a clip/morph name? → A: **A combo over the character's asset's names** — the panel
  resolves her asset at edit time (the actor in the scene, else a cast binding for her) and offers its clips/morphs,
  so the name the op names always resolves; it falls back to a typed field when the asset does not resolve.
- Q: How does `MoveActor` move the actor? → A: **Physics-driven** — each tick the op drives the body (a
  horizontal velocity along the path, preserving the vertical component so gravity still acts, and a facing),
  letting Jolt resolve the result, so geometry can block her. It does not write her transform directly.
- Q: Do the blend ops act when the actor's zone is **not** the active one? → A: **Yes** — a blend resolves her
  through the index and drives her slots in her own zone, so a member staged off-screen is as the cue left her on
  switch-in. Consistent with `specs/064`'s ops.
- Q: Do the **motion** ops act when the actor's zone is **not** the active one? → A: **No** — motion is
  physics-driven, and a non-active zone's bodies are detached from the simulation, so a motion op is
  **active-zone only**: it performs nothing when her zone is not the active one. Only an unobstructed move reaches
  the last waypoint; an obstructed one stops where the collision leaves it.
- Q: How does `MoveActor` face the actor? → A: **She slerps from her own facing to the last waypoint's arrow**,
  read as an actor facing (the waypoint's `-Z` arrow mapped to the actor's `+Z` forward). A waypoint the author
  turned along the path therefore leaves her facing it, and her own starting facing is the interpolation's start.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A cue blends an actor's animation or morph (Priority: P1)

A cue fades an actor's animation or morph slot weight over a duration: a single clip to a target weight
(`Animate` / `Morph`), or one slot out while another comes in (`CrossFade` / `CrossMorph`). The blend is a
value in the run — it interpolates each tick and ends when its deadline passes.

**Why this priority**: it is the building block — `MoveActor`'s idle/moving blend is the same machinery — and it
builds directly on the actor's existing slot weights, so it is the most self-contained slice.

**Independent Test**: run a cue carrying an `Animate` op and assert the target clip's slot weight moves from its
start toward the target and reaches it at the deadline; run a `CrossFade` and assert one weight falls as the
other rises.

**Acceptance Scenarios**:

1. **Given** an actor with an `Idle` clip at full weight, **When** a cue reaches `Animate (character, "Jog",
   target, duration)`, **Then** `Jog`'s weight interpolates to `target` over the duration and the op is done.
2. **Given** two clips, **When** a cue reaches `CrossFade (character, from, to, target, duration)`, **Then**
   `from` fades out as `to` fades in, reaching the target at the deadline.
3. **Given** a morph-carrying asset, **When** a cue reaches `Morph` / `CrossMorph`, **Then** the morph slot's
   weight blends the same way.
4. **Given** a blend in flight, **When** the frame ticks mid-way, **Then** the op reports nothing to change
   (it is one leaf with one deadline).

---

### User Story 2 - A cue moves an actor along a path, or turns her in place (Priority: P2)

A cue **drives** an actor's body from where she stands, through a waypoint chain to its end, over a duration set
by the path's length and a speed, turning from her own facing to the path's end facing and blending a moving clip
down to an idle clip as she arrives (`MoveActor`). Separately, a cue **turns** an actor in place to face a waypoint
over a duration (`RotateActor`). Both act only while the actor's zone is the active one, and both drive the body
rather than teleporting her.

**Why this priority**: it is the visible payoff — characters walking during a scene — and it composes with the
camera flight and waypoint chains the project already has.

**Independent Test**: place a chain of waypoints in the actor's place (the active zone), run a cue carrying
`MoveActor` naming the chain's start with the physics stepped, and assert her body follows the spline and arrives
at the last waypoint at the deadline; run `RotateActor` and assert the yaw reaches the waypoint's. Both no-op
when her zone is not the active one.

**Acceptance Scenarios**:

1. **Given** an actor and a waypoint chain, **When** a cue reaches `MoveActor (character, path, speed, moving,
   idle)`, **Then** her body is driven along the spline from her current position to the chain's end over
   `length / speed`, and her facing slerps from her own to the last waypoint's arrow.
2. **Given** an unobstructed move in flight, **When** the deadline passes, **Then** the actor is at the last
   waypoint, facing its orientation, with the idle clip at full weight.
3. **Given** an actor, **When** a cue reaches `RotateActor (character, waypoint, duration)`, **Then** her yaw
   interpolates from its current value to the waypoint's over the duration.
4. **Given** a path naming a waypoint the place does not hold, **When** the cue runs, **Then** the op performs
   nothing. A path *may* be a single waypoint: she walks straight to it from where she stands.
5. **Given** a move in flight blocked by geometry, **When** the deadline passes, **Then** she stops where the
   collision left her rather than passing through.
6. **Given** a motion op whose actor is in a zone that is not the active one, **When** the cue runs, **Then** it
   performs nothing.

---

### User Story 3 - A cue binds or unbinds an actor as the player (Priority: P3)

A cue **enables the player** on an actor — making her the one the play-mode input drives — and later
**disables** it. This is the runtime counterpart of the editor's player designation.

**Why this priority**: it is the gameplay-facing toggle, and the thinnest: it sets the actor the existing player
systems already act on.

**Independent Test**: run `EnablePlayer` on a placed actor and assert the zone's player resolves to her
and the player systems drive her; run `DisablePlayer` and assert she no longer is the player.

**Acceptance Scenarios**:

1. **Given** a placed actor, **When** a cue reaches `EnablePlayer character`, **Then** the zone's
   player is that actor and the play-mode input drives her.
2. **Given** the player actor, **When** a cue reaches `DisablePlayer character`, **Then** she is no
   longer the player.
3. **Given** a `DisablePlayer` naming an actor who is not the player, **When** the cue runs, **Then**
   it performs nothing. (Enable binds her as the player exactly as the editor's "Set as Player"; the camera
   follows her, as it does for that designation.)

---

### Edge Cases

- **The character is not in the scene**: every op here performs nothing (there is no actor to act on) — the
  no-op rule `specs/064` established, so a cue re-runs safely.
- **An unknown clip or morph name**: the actor's asset may not define the named clip/morph; the op performs
  nothing (it guards with the asset's lookup), so a cue naming a clip the asset lacks does not fault.
- **A move whose path does not resolve**: a missing waypoint, or a path with no length (she already stands on
  its only waypoint), performs nothing, as `specs/064`'s waypoint resolution does.
- **A move or blend re-reached on a re-run**: the move re-walks from the actor's current position and the blend
  re-begins from the current weight — both are values in the run, not hidden state.
- **A player actor removed mid-scene**: the player designation becomes stale and the systems treat it as no
  player (`specs/016`/`064`), so nothing needs clearing.
- **An actor in a non-active zone**: a **blend** acts on her there (driving her slot weights), so she is as the
  cue left her on switch-in. A **motion** op performs nothing there — her body is detached from the simulation, so
  a physics-driven move cannot run; motion is active-zone only (Clarifications).
- **A move blocked by geometry**: the op drives her at the path and Jolt resolves the collision, so she stops
  where she is when the deadline passes rather than passing through; only an unobstructed move reaches the last
  waypoint.
- **While editing**: cues do not run, so no op fires from the editor; only Play advances the cue.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The cue MUST gain the remaining actor ops, each addressed by a `Roster.Character` (the cast index
  resolves her place, as `RemoveActor` does — no free string, no place argument). All three groups —
  animation/morph blends, movement and the player toggle — are this feature, built in priority order.
- **FR-002**: The **animation/morph** ops MUST blend the named slot's weight over a duration: `Animate`
  (one animation clip to a target weight, from its current), `CrossFade` (one clip out, one in), `Morph` and
  `CrossMorph` the same over morph slots. The blend is a leaf that interpolates each tick and ends at its
  deadline.
- **FR-003**: The **movement** ops MUST move an actor in the world, **physics-driven** and **only while her zone
  is the active one**: `MoveActor` drives her body along a named waypoint chain to its end over `length / speed`
  (a single waypoint is a straight walk to it from where she stands), turning from her own facing to the last
  waypoint's arrow and blending a moving clip down to an idle clip on arrival; `RotateActor` turns her in place to
  face a named waypoint over a duration. Both are leaves that update each tick and end at their deadline; an
  unobstructed move arrives at the last waypoint, while an obstructed one stops where the collision leaves it.
- **FR-004**: The **player** ops MUST bind and unbind an actor as the player: `EnablePlayer` makes
  her the zone's player — exactly the editor's "Set as Player", so the play-mode input drives her and the camera
  follows — and `DisablePlayer` clears it when she is the player, performing nothing otherwise.
- **FR-005**: An op whose actor does not exist, whose waypoint/path does not resolve, whose clip/morph name the
  asset does not define, or (for a motion op) whose actor's zone is not the active one MUST perform nothing (the
  op guards with the asset's lookup and the active zone), and an op whose deadline never arrives simply stays in
  flight — none faults, so a cue is safe to re-run. A no-op MUST be reported on stderr with its reason, so an
  author can tell a no-op from a working op.
- **FR-006**: Every op here MUST be a **runtime** effect, not a step on the undo history, exactly as
  `specs/064`'s ops and the zone ops are.
- **FR-007**: The editor's cue panel MUST offer the new ops with their fields — a character chooser; a clip/morph
  chooser over the character's asset's names (typed when the asset does not resolve); numbers (weight, duration,
  speed); and, for `MoveActor`/`RotateActor`, a waypoint/path chooser over the **active zone's** waypoints (the
  author works in the zone the cue acts in). Each accepted edit MUST be one step on the one undo history (`specs/049`).
- **FR-008**: This feature MUST add **no** zone or narrative vocabulary, and MUST NOT touch the non-actor
  toggles (`CameraFollow`, `Interactions`, `Trigger`) or `Print`.

### Key Entities *(include if data involved)*

- **Character**: a value from `Roster.Character` — how an actor op names its actor.
- **Animation slot / morph slot**: the per-instance weights the actor's renderer holds (`specs/036`/`037`);
  what the blend ops drive.
- **Waypoint chain**: the per-zone linked waypoints (`specs/028`); what `MoveActor` walks and `RotateActor`
  faces.
- **Player designation**: the zone's bound actor (`zone.Player`, `specs/045`); what the player ops set and
  clear.
- **Actor**: the zone entity a member is realised as (authored state plus a body and a renderer).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A cue can blend an actor's animation or morph weights, walk her along a waypoint path, turn her in
  place, and make her (or stop making her) the player — naming only a rostered character.
- **SC-002**: A blend reaches its target weight exactly at its deadline; a move arrives at the last waypoint
  with the idle clip at full weight when unobstructed, and stops where it is when blocked.
- **SC-003**: A cue carrying any of these ops can be run more than once without raising on state its earlier run
  produced.
- **SC-004**: The editor offers the ops with their fields, and each accepted edit is one step on the one undo
  history.
- **SC-005**: The project builds with no warnings and the suite is green, with no device opened.

## Assumptions

- **Addressing is by character.** Every op names a `Roster.Character`; the cast index (`specs/063`) resolves her
  place, so none names a place — consistent with `RemoveActor` (`specs/064`).
- **The actor's existing art defines the names.** Clip and morph names are the asset's own; the blend ops
  resolve them through the actor's renderer, and `MoveActor`'s moving/idle clips are its asset's too.
- **The movement shape follows odin, with a reworked turn.** A move uses the actor's current position as the
  spline's start and the chain's end as its end, and blends moving→idle; the F# port slerps her facing from her
  own to the last waypoint's arrow (not odin's look-at steering) and physics-drives the body rather than writing
  the transform. `RotateActor` is a separate in-place turn.
- **The author works in the zone the cue acts on.** The editor's waypoint chooser for `MoveActor`/`RotateActor`
  offers the **active zone's** waypoints (as the camera ops do); the op still stores the name and resolves it in
  the actor's own zone at run time, so the chooser's zone is a convenience, not a coupling.
- **The player binding.** F# has no separate locomotion subsystem to attach: the player *is* the actor
  the input drives (`world/Systems.fs`), so enable/disable map to setting/clearing `zone.Player`, and the camera
  follows (as for the editor's designation). Turning camera-follow off is a separate, out-of-scope op.
- **A blend acts in the actor's own zone, active or not; motion only in the active zone.** The index resolves her
  place; a blend drives her slot weights there regardless of the active zone (matching `specs/064`, whose ops act
  on an inactive place). A motion op is **physics-driven** — it drives the body (velocity and facing) and lets
  Jolt resolve the result rather than writing the transform directly — and a non-active zone's bodies are detached
  from the simulation, so motion performs nothing unless her zone is the active one.
- **An unknown clip/morph name is a no-op.** The op checks the asset's names and does nothing when the name is
  absent, rather than raising as `ActorRender` does — a cue must stay safe to re-run (Clarifications).
- **This is one slice of a larger actor family.** The non-actor toggles and `Print` remain separate.
