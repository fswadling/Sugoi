# Feature Specification: Spawning a Cast Member from the Cast Panel

**Feature Branch**: `033-cast-spawn`

**Created**: 2026-09-20

**Status**: Draft

**Input**: User description: "a button next to the cast roster entries that allows me to
spawn an actor with the connected asset, with the name of the cast DU case as the entity's
name."

**Origin**: the review of `specs/032-cast-binding`, whose Assumptions put placement out of
scope in as many words: "Placing a cast member, and recording a character on a placed entity,
are out of scope." This feature takes the first half of that in and leaves the second half
where it is: a placed actor is an ordinary actor, and which character it *is* remains the
engine's business rather than the scene's.

## User Scenarios & Testing *(mandatory)*

Two parties use this: the **author**, who binds a member to a model in the cast panel and then
wants that model standing in the scene; and the **scene**, which receives an ordinary actor and
knows nothing about the cast.

### User Story 1 - Put a cast member's model into the scene (Priority: P1)

The author opens the Cast panel, finds a member whose look is bound to an asset, and activates
the control beside it. An actor appears in the active zone, drawn with that asset and named for
the member. Placing a second one is the same gesture, and undoing it is Ctrl+Z like anything
else placed in the scene.

**Why this priority**: it is the whole feature. Everything else here is a constraint on how it
must behave.

**Independent Test**: bind an asset to a member's look, activate the control, and confirm an
actor appears in the active zone with that asset and with the member's case name as its name;
then undo and confirm it is gone and the binding is untouched.

**Acceptance Scenarios**:

1. **Given** a slot with an asset assigned, **When** the author activates its spawn control,
   **Then** an actor drawn with that asset appears in the active zone.
2. **Given** the same, **Then** the new entity's name is the member's case name.
3. **Given** a slot with nothing assigned, **Then** its spawn control cannot be activated.
4. **Given** a member whose look is bound, **When** the author activates the control twice,
   **Then** two actors are placed, both named for the member. Duplicate names are allowed:
   a name is a label, not an identity.
5. **Given** a spawn, **Then** the new actor is selected, as every other creation in the editor
   selects what it created.
6. **Given** a spawn, **When** the author undoes it, **Then** that actor is gone, one step
   removed exactly it, and no binding changed.

---

### User Story 2 - The new actor is an ordinary actor (Priority: P2)

What is placed is what the asset browser places: the same authored defaults, the same asset, the
same kind of entity, with a name chosen by the author's cast rather than by the asset's. Nothing
about the cast travels with it.

**Why this priority**: it keeps this feature from growing a second way to create an actor, or a
second meaning for one. It is P2 because the authoring value above does not depend on it — but it
is what stops the two spawn paths drifting apart later.

**Independent Test**: spawn the same asset from the Asset Browser and from the Cast panel and
compare the two actors: same kind, same asset, same placement, differing only in name.

**Acceptance Scenarios**:

1. **Given** the same asset, **When** it is spawned from the Asset Browser and from the Cast
   panel, **Then** the two actors are identical apart from their names.
2. **Given** a spawn from the Cast panel, **Then** the actor carries no field, flag, or
   reference naming a cast member — it is an actor like any other.
3. **Given** the cast binding, **When** an actor is spawned from it, **Then** the binding is
   unchanged, and reassigning the slot afterwards does not affect the actor already placed.

---

### Edge Cases

- A member with two looks bound offers two controls — one per slot, each spawning the asset
  bound to it (see Assumptions for why the control is per slot rather than per member).
- A member with no look bound offers no control that can be activated.
- The control spawns only the asset the slot is bound to: there is no default model and no
  fallback, exactly as there is no fallback for an unassigned slot.
- Spawning does not modify the binding, and modifying the binding does not modify anything
  already placed. The two directions are independent.
- Undo removes the actor and nothing else: the binding was never part of the step.
- A member whose case name is long is still named exactly so; the entity name's length limit is
  whatever every other creation path already accepts.
- Undoing a spawn is one step even though a spawn records a created entity with a fresh body and
  renderer — the reversal is the same kind of operation as any other create.
- What is placed is placed where the Asset Browser places one, because that path is the only
  precedent and a second placement rule would be a second answer to one question.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The cast panel MUST offer a spawn control for each slot that has an asset assigned
  to it.
- **FR-002**: Activating a slot's control MUST create an actor in the active zone drawn with the
  asset that slot is bound to.
- **FR-003**: The created entity's name MUST be the cast member's case name.
- **FR-004**: Creation MUST go through the editor's single entity-creation surface, so it is
  paired with the operation that reverses it and is selected exactly as every other creation is.
- **FR-005**: A slot with no assignment MUST NOT offer a control that can be activated.
- **FR-006**: Activating a control MUST NOT alter the binding, its assignment behaviour, or any
  entity other than the one created.
- **FR-007**: The created actor MUST be authored exactly as the Asset Browser authors one — the
  same kind, the same asset, the same placement, the same initial properties — differing only in
  its name, so the two spawn paths cannot drift apart.
- **FR-008**: Every existing behaviour MUST be unchanged: the panel's tree and its assignment,
  the Asset Browser, the Outliner, the Inspector, and the undo granularity of every gesture.
  (Preserves `specs/032-cast-binding` FR-007 to FR-011.)
- **FR-009**: A created actor MUST carry no reference to the cast member it came from.
  Recording which character a placed entity is remains out of scope.

### Key Entities *(include if feature involves data)*

- **Cast member, slot, binding** — as `specs/032-cast-binding` defines them: a member is an
  identity, a slot is a member and a look, and the binding is the sparse map from a slot to the
  actor asset that plays it.
- **Spawn control**: the affordance a bound slot offers, which creates the actor for that slot.
- **Placed actor**: an ordinary actor entity — a model, a collider, a name — created through
  the same surface every other creation uses. It holds no link to the cast.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every slot with an assignment offers exactly one spawn control, and no slot without
  one offers a control that can be activated (100%).
- **SC-002**: Every actor created this way is drawn with its slot's asset and named with its
  member's case name (100%).
- **SC-003**: One activation is one undoable step that removes exactly the actor it created
  (100%).
- **SC-004**: Spawning writes no binding (0 writes), and assigning writes nothing already placed.
- **SC-005**: The two spawn paths produce actors that differ only in name (100%).
- **SC-006**: No existing behaviour changes: the behaviour tests from `specs/032-cast-binding`
  pass unmodified (100%).

## Assumptions

- **The control is per slot, not per member.** A member may hold several looks, each with its
  own asset, so a control on the member's row would have to invent which of them it spawns; the
  slot is where the asset is, and one control per slot needs no rule. With a single look bound —
  today's case — that is one control per member, on the row that shows the asset. If the author
  would rather have one control on the member's own row, that is a small change and this
  assumption is the thing to overturn.
- **Placement follows the Asset Browser.** The actor is authored with that path's defaults (its
  placement and its initial properties), because it is the only existing way an
  actor is placed and a second one would be a second answer to the same question. Where a spawned
  actor *should* go — at the camera, in front of the author — is a separate question this feature
  does not settle.
- **The name is the case name and may repeat.** Names are labels rather than identities: nothing
  in the engine, the zone, or the Outliner requires them to be unique, and two actors named for
  one member is a legitimate scene (she is in two rooms, or the same one twice). The panel does
  not prevent it and no numbering is invented.
- **The binding stays a direct write** (`specs/032-cast-binding` FR-011). Placing an actor is a
  scene edit and is undoable through the scene's own command; assigning a model is not a scene
  edit and remains outside every undo stack. The two sit next to each other in one panel and
  behave differently on purpose.
- **Play-mode gating is unchanged.** This feature adds no rule about whether the panel may place
  an actor while playing; it behaves as the Asset Browser already does.
- **The other half of 032's deferral stays deferred.** Recording which character a placed entity
  is — so the simulation can act on Akane rather than on an actor that looks like her — is
  separate work, and this feature deliberately leaves an actor with nothing but a name.
