# Feature Specification: Actor Cue Ops

**Feature Branch**: `064-actor-cue-ops`

**Created**: 2026-10-04

**Status**: Draft

**Input**: User description: "now we should be ready to move onto the actor cue ops" — scoped to the **lifecycle** ops only: bringing a member into a place and placing her (`SetActor`, unifying odin's `AddActor` and `WarpActor`) and removing her (`RemoveActor`).

## Origin

`specs/061-zones-as-places` gave a runtime zone a closed, authored identity (a `Place`) and recorded its
other successor: *"with zones addressed by a closed place, actor cue ops name a `(place, character)` pair and
resolve through that place's present zone."* `specs/062-zone-cue-ops` then filled the zone vocabulary
(`AddZone` / `RemoveZone` / `PreloadZone`). What remains is **who**: today no cue op can name an actor at all.

`specs/063-cast-index` built exactly the runtime half the ops read: an actor records the `Roster.Character`
it plays (`ActorState.Cast`, authored), and the gameplay state holds an index keyed by `Roster.Character`
that resolves a character to the actor playing her — her `Place` and `EntityRef` — in one lookup, with **one
character at most across all zones**. So "which actor plays Aiko?" is already answerable without a search;
this feature gives a cue the vocabulary to act on the answer.

**The reference.** `odin-game`'s cue vocabulary (`src/cue.odin`) has a family of actor ops — the lifecycle
pair `AddActor` (spawn at a waypoint, with an asset) and `WarpActor` (move to a waypoint), plus `RemoveActor`;
then movement (`MoveActor`, `RotateActor`), locomotion toggles, and animation/morph blends. Every odin
*placement* op positions an actor **at a named waypoint** and faces her along the waypoint's orientation —
never at bare coordinates — and every odin actor op addresses the actor by a free-string `actor_name`.

This feature takes the **lifecycle** slice and ports it faithfully-but-closed:

- **One op, not two.** Decided: *"we should combine spawnActor and warpActor into a single cue ⇒ set actor."*
  A single **set** op places a member whether or not she is already in the scene, so a cue expresses "this
  member is here, like this" without first asking whether she exists.
- **Removal names the character alone.** A **remove** op names just the `Roster.Character`: the runtime cast
  index (`specs/063`) resolves her place and she is unique across all zones, so a place would only repeat what
  the index already knows. Placement still names a place.
- **Closed identity, not names.** Where odin names an actor with a string, this op names a `Roster.Character`
  resolved through the cast index; where odin names a place with a string, this names a `Place`.
- **Lifecycle only.** Movement, locomotion and animation/morph ops are **out of scope** here and deferred to a
  later feature; this one adds the set and remove ops and nothing else.

## Clarifications

### Session 2026-10-04

- Q: Is this feature the whole odin actor family, or just the lifecycle pair? → A: **Lifecycle only** — `SetActor`
  (unifying `AddActor` + `WarpActor`) and `RemoveActor`; movement (`MoveActor`/`RotateActor`), locomotion and
  animation/morph ops are a later feature (FR-010).
- Q: How is a placement expressed? → A: **By a waypoint in the named place** — the actor stands at its position
  and faces along its orientation, exactly as every odin placement op does. No bare coordinates.
- Q: Does the set op name a look? → A: **Yes**, but it is only used to resolve her art on a **first**
  placement. If the character already has an actor **anywhere** in gameplay — the same place *or another
  zone* — her **existing** look/art is kept and the op's look is ignored: a set never re-skins an existing
  actor (a move keeps her outfit).
- Q: When a set op names a character already present in a **different** place? → A: **Move her** — drop her
  actor from the old place and set it in the named place, keeping her existing art. One character, one actor,
  now here.
- Q: Does `RemoveActor` need to name a place? → A: **No** — the cast index resolves the character to the place
  she is in and she is unique across all zones, so a remove names the `character` alone. Naming a place would
  be redundant (and a foot-gun: it would silently no-op when she is elsewhere).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A cue sets an actor by (place, character) (Priority: P1)

A cue **sets** a member: naming a place and a character, it brings that character's actor into the named place
and places her there — her position and facing, taken from the waypoint the op names. It is the **same op**
whether she is not in the scene yet (a spawn) or already standing in that place (a move), so a cue that
re-runs does not spawn a second copy.

**Why this priority**: it is the whole point — the runtime cast is only useful if a cue can put a member
somewhere — and it is testable headlessly against the gameplay state, with no window and no GPU.

**Independent Test**: run a cue carrying a set op for a character with no actor and assert one actor now plays
her, in the named place, at the named waypoint's position and facing; run it again and assert there is still
exactly one, moved to the new placement.

**Acceptance Scenarios**:

1. **Given** a present place and a character with no actor in the scene, **When** a cue reaches a **set** op
   naming them, **Then** an actor plays that character in that place, placed at the op's waypoint, and the cast
   index resolves her there.
2. **Given** a character already placed, **When** a cue reaches a set op naming her, **Then** the same actor
   moves to the new placement — no second actor is created.
3. **Given** a character the game has no art for in the chosen look, **When** a set op names her, **Then** the
   op cannot place her (no asset to build her from).
4. **Given** a place the game does not declare, **When** a cue is authored, **Then** the op cannot name it (the
   place chooser offers only declared places); likewise a character outside the roster is unrepresentable.

---

### User Story 2 - A cue removes an actor (Priority: P2)

A cue **removes** a member: naming a character, it takes her actor out of the scene — resolving where she is
through the cast index, which knows her place and is unique across all zones — releasing what she owned, and
leaving the index no longer resolving her.

**Why this priority**: it is the other half of moving the cast through the world — a member who leaves — and it
is what keeps the index exact when the story moves on.

**Independent Test**: place a member, run a cue carrying a remove op naming her, and assert no actor plays her
and the index no longer resolves her; run it again and assert it performs nothing.

**Acceptance Scenarios**:

1. **Given** a character placed in any present place, **When** a cue reaches a **remove** op naming her,
   **Then** her actor is gone and the cast index no longer resolves her.
2. **Given** a character not in the scene, **When** a cue reaches a remove op naming her, **Then** the op
   performs nothing and the cue continues.

---

### User Story 3 - The actor ops compose with the zone ops and are safe to re-run (Priority: P3)

The actor ops compose with `specs/062`'s zone vocabulary into a scene transition — make a place present, set a
member in it, switch to it — and a cue that carries them behaves sanely when re-run: an op whose effect already
holds, or that names a place, character or waypoint it cannot act on, performs nothing rather than failing.

**Why this priority**: it is the robustness half. The zone ops already established that redundant state is the
ordinary case, not an error; the actor ops must extend the same rule, or a cue that runs twice faults.

**Independent Test**: run a cue that sets an already-placed member, removes one who is not present, and sets a
member in an absent place — each completes without raising; then assert the composition (add zone → set actor →
switch) leaves the expected state.

**Acceptance Scenarios**:

1. **Given** a set op naming a character already in the named place, **When** the cue runs, **Then** the op
   places her there and the cue moves on.
2. **Given** an actor op naming a place that is not present, **When** the cue runs, **Then** it performs nothing.
3. **Given** a cue that makes a place present, sets a member in it, and switches to it, **When** it runs,
   **Then** the final active place is that place with the member present in it.

---

### Edge Cases

- **A place that is not present**: a **set** op on it performs nothing, matching the zone ops' no-op rule
  (`specs/062` FR-007/FR-008) — the author makes the place present with `AddZone` first. (A remove names no
  place.)
- **A waypoint the named place does not hold**: a set op with no waypoint to place her at performs nothing —
  the same no-op rule the zone ops follow, so a re-run cannot fault on a waypoint since removed. (The camera ops
  are stricter with waypoint names and raise; the actor ops deliberately do not copy that.)
- **A character already in the scene, named for a different place**: the runtime cast is unique across all
  zones (`specs/063` FR-003), so the op moves her — her actor leaves the old place and is set in the named
  one, keeping her existing art (FR-005).
- **A character with no art for the named look**: the cast binding is sparse (`Casting.tryAsset`); a set op
  that must create an actor and resolves no asset cannot place her, and must not half-place her. (An existing
  actor is moved as-is and never re-resolved against the look.)
- **Setting the actor who is the zone's player**: the player designation is a zone's own (`specs/045`). A move
  **within** the same place leaves it intact (the handle is unchanged). A cross-zone move takes the handle out of
  the old place, which then keeps a designation that no longer resolves — the same stale-handle state an editor
  delete leaves, which the systems already treat as "no player" (`specs/016`). No clear step is added.
- **Removing the actor who is the zone's player**: the same — the designation is left in place and simply stops
  resolving; the systems treat the zone as having no player, exactly as an editor delete does.
- **While editing**: cues do not run, so no actor op fires from the editor; only Play advances the cue.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The cue MUST gain **lifecycle** actor ops addressed by closed identity: a **set** op names a
  `(Place, Roster.Character)` pair (a place from the closed union, a character from the closed roster) plus a
  `Casting.Look` and a waypoint, and a **remove** op names the `Roster.Character` alone — the cast index
  resolves her place. There MUST be no free string for the place or the character, exactly as `SetZone` and
  the zone ops name a `Place`.
- **FR-002**: A **set** op MUST bring the named character's actor into the named place *and* place her there
  at the **waypoint** it names — standing at the waypoint's position and facing along its orientation (odin's
  shape). It MUST be **one** op serving both a first placement (no actor yet) and a later placement of an
  existing actor (a move), so re-running a cue does not create a second actor.
- **FR-003**: A **remove** op MUST remove the named character's actor — resolving her place from the cast
  index — releasing what she owned (body, renderer), and MUST leave the cast index no longer resolving her.
- **FR-004**: A set op MUST name a **look**, used to resolve the actor asset through the game's cast binding
  (`Casting.tryAsset`, character + look) **when it must create the actor**. If the character already has an
  actor anywhere in the scene — the named place or another — her **existing** art MUST be kept and the op's
  look ignored, so a set never re-skins an existing actor. A character with no art for the named look MUST NOT
  be placed — the op performs nothing rather than building an actor from nothing.
- **FR-005**: The ops MUST keep the cast index exact (`specs/063`): a placement that creates an actor MUST add
  the index entry; a removal MUST drop it; the one-character-across-all-zones invariant MUST hold. A set op
  naming a character already present in a **different** place MUST **move** her — drop her entry for the old
  place and add it for the named one — rather than add a second actor.
- **FR-006**: Every actor op on a place that is **not present**, or that it cannot act on, MUST perform nothing
  and let the cue continue — the no-op rule the zone ops established (`specs/062` FR-007). "Cannot act on" is
  per op: a **set** performs nothing when it must create the actor and the named look resolves no art, or when
  the named waypoint does not resolve in that place's zone; a **remove** performs nothing when the character is
  not in the scene. (A set on a character who is not in the scene is the ordinary **create** case, not a
  no-op.) Re-running a cue MUST NOT fault on state a previous run produced.
- **FR-007**: An actor op MUST be a **runtime** effect, not a step on the undo history — the cue is the
  screen's own value, as `SetZone` and the zone ops already are. When play ends, the scene reflects what the
  cue did.
- **FR-008**: The editor's cue panel MUST offer the new ops with choosers over the closed sets — a character
  for either op, plus a place, a look and the named place's waypoints for a set op — and each accepted edit
  MUST be one step on the one undo history, like every other cue edit (`specs/049`).
- **FR-009**: This feature MUST add **no** zone vocabulary (`specs/062` has it) and MUST NOT touch the
  narrative engine; it reads the runtime cast and the place's waypoints and nothing more.
- **FR-010**: This feature MUST add **no** movement, locomotion or animation/morph op. `MoveActor`,
  `RotateActor`, `Enable/DisablePlayer`, `Animate`/`CrossFade`/`Morph`/`CrossMorph` and the
  camera/interaction/trigger toggles are **deferred** to a later feature.
- **FR-011**: The ops MUST leave a zone's player designation coherent the way an editor actor delete already
  does (`specs/016`/`045`): a designation pointing at an actor that has left is tolerated as "no player", so no
  clear step is added. A move **within the same place** MUST leave the designation, and the actor's identity,
  intact.

### Key Entities *(include if data involved)*

- **Character**: a value from `Roster.Character`, the closed roster — *who* a cue can name.
- **Place**: a value from the closed place union (`world/Place.fs`) — *where* a cue can name.
- **Waypoint**: a per-zone named entity giving a position and orientation; a set op names one, and the actor
  stands at it and faces along it (FR-002).
- **Cast index**: the gameplay state's map from a character to the actor playing her (place and ref), one
  character at most across all zones (`specs/063`). What the ops read and keep exact.
- **Cast binding**: `Casting.tryAsset (character, look)`, the game's sparse map from a member-and-look to an
  actor asset; its absence is "no art for that look" (`specs/032` / `specs/063`).
- **Actor**: the zone entity a member is realised as (authored `ActorState` plus a body and a renderer).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A cue can bring a cast member into the scene and place her, and remove her again, naming only a
  declared place and a rostered character — with no free string for *who* or *where*.
- **SC-002**: A cue that sets a member twice leaves exactly one actor playing her, at the latest placement —
  never two.
- **SC-003**: After any placement or removal, the cast index is exactly consistent with the actors that exist
  (`specs/063` SC-003).
- **SC-004**: A cue carrying the lifecycle actor ops can be run more than once without raising on state its
  earlier run produced.
- **SC-005**: The editor offers the ops with choosers over the closed sets — a character for either op, and a
  place, a look and the named place's waypoints for a set op — and each accepted edit is one step on the one
  undo history.
- **SC-006**: The project builds with no warnings and the suite is green, with no device opened.

## Assumptions

- **Scope is the lifecycle pair.** `SetActor` (unifying `AddActor` + `WarpActor`) and `RemoveActor`; odin's
  movement, locomotion and animation/morph ops are a later feature (FR-010). Decided in the 2026-10-04 session.
- **Placement names a `(place, character)` pair; removal names the character.** `specs/061` recorded that an
  actor op resolves a character through the named place's present zone; that holds for `SetActor`, which must
  say *where*. `RemoveActor` names the character alone: the cast index (`specs/063`) resolves her place and she
  is unique across all zones, so a place would repeat what the index knows.
- **Placement is by waypoint.** Every odin placement op stands an actor at a named waypoint and faces her
  along it; this feature follows that shape, and F# already names waypoints by string (`SetCamera` /
  `FlyCamera`).
- **The look is a creation-time concern.** The set op names a look, but it only chooses her art when she is
  first placed; an existing actor is moved as-is, her current look kept (Clarifications, FR-004).
- **The editor defaults to a declared place.** The op carries a place; the cue panel's chooser needs no
  gameplay state, so it starts at a declared place (`Place.start`) rather than reading the active one.
- **Spawn and warp are one op.** A single set op replaces separate spawn *and* warp/move ops, so a cue
  expresses "this member is here, like this" without first asking whether she exists.
- **The cast index is the resolution path.** Actor ops read and maintain `GameplayState.Cast` (`specs/063`);
  they do not scan zones for an actor, and a character not in the scene has no actor to find.
- **The runtime cast is global.** A character is unique across all zones, so a set op naming a place can
  name a member who is elsewhere and **moves** her there (FR-005), and a remove op needs no place at all.
- **Cue actor changes are runtime, not authored.** They are not steps on the undo history and are not persisted;
  this matches `SetZone` and the zone ops.
- **No new vocabulary of people or places.** No characters or places are declared here; the ops act on the
  existing unions.
- **The debug `Print` op is out of scope.** It is a separate feature.
