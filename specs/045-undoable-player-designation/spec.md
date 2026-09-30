# Feature Specification: Undoable Player Designation

**Feature Branch**: `045-undoable-player-designation`

**Created**: 2026-09-30

**Status**: Draft

**Input**: User description: "setting an actor as the player wasn't a command and therefore wasn't an undoable action. it should be"

**Origin**: This is a consistency fix inside the port's own editor model, not a port of new reference
behaviour.

The port's rule (`docs/EDITOR_DESIGN.md`, principle 4) is that **every editor mutation of *scene* state
is an undoable command**, and that the exceptions are session- or project-scoped authoring state that
**outlives a zone** — which is why the cast binding (D-022) and the zone registry's registrations
(D-023) are direct writes: the undo stacks are per zone, so an entry parked in one zone's stack would
be unreachable from the others and discarded when that zone is deleted.

The player designation was listed as a third exception beside those two. That grouping is a **mistake**:
unlike them, the designation *is* a zone's own state. It lives on `Zone.Player`, per zone, and
`docs/ARCHITECTURE.md` already says "the player designation and the undo history are held per zone". It
is exactly the case the lighting section later reasoned about for the light (D-024): *"this is a zone's
own setting rather than project-scoped state, so it lives on the zone and its undo rides the zone's own
stack — which is why it is a command at all"*. The light became a command on that reasoning (spec 043);
the player designation, filed with the project-scoped exceptions, did not.

The only place the designation is set today is the Inspector's actor section
(`editor/Inspector.fs`):

```fsharp
if ImGui.button("Set as Player", ImGui.ImVec2(0.0f, 0.0f)) then
    zone.Player <- ValueSome (Systems.playerHandleOf ref)
```

— a direct write into the live zone. An accidental click cannot be taken back, and nothing in the editor
says who the player currently is. (The reference has a stateful control for this: `odin-game`'s
properties panel gives an actor a **"Player Locomotion" checkbox**, `src/editor_properties.odin:969`,
which both attaches and detaches. The port kept only the setting half, as a button.)

**Scope note**: this changes *how* the existing action is recorded, not what it does. No new way to
designate a player is added, play-mode behaviour is unchanged (`Systems.isPlayerLive` and the systems it
gates are untouched), and the designation is still not part of a registered place (`world/ZoneTemplate.fs`
carries none). Clearing the designation — which the reference's checkbox can do and the port's button
cannot — is **not** added here (Assumptions).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A player designation can be taken back (Priority: P1)

The author sets an actor as the player, changes her mind, and undoes it: the zone is back to the
designation it had before the click. Redo puts it back.

**Why this priority**: this is the capability. Everything else — that the step lands on the right stack,
that the control reads its state — is about doing it correctly in context.

**Independent Test**: select an actor in a zone with no player designated, press "Set as Player", press
undo, and confirm the zone has no player again; press redo and confirm the actor is the player again.

**Acceptance Scenarios**:

1. **Given** a zone with no player designated, **When** the author sets an actor as the player and then
   undoes, **Then** the zone has no player designated — exactly the state before the click.
2. **Given** a zone whose player is actor A, **When** the author sets actor B as the player and then
   undoes, **Then** A is the player again (the previous designation, not "no player").
3. **Given** an undone designation, **When** the author redoes, **Then** that designation is applied
   again.
4. **Given** a designation already in place, **When** the author presses the control for that same
   actor again, **Then** nothing changes and no step is added to the history.

---

### User Story 2 - The step belongs to the zone it was made in (Priority: P2)

The designation is a zone's own, so the step that changes it belongs to that zone's history: after a
zone switch, an undo takes back the *incoming* zone's last edit, not the outgoing zone's.

**Why this priority**: it is what makes the step safe to put on a per-zone stack at all, and it is the
reasoning that reclassifies the designation (Origin). It ranks below Story 1 because the capability
stands without it.

**Independent Test**: designate a player in zone A, switch to zone B, and undo — zone A is untouched.

**Acceptance Scenarios**:

1. **Given** a designation made in zone A, **When** the author switches to zone B and undoes, **Then**
   zone B's own most recent change is taken back and **zone A's designation is unchanged**.
2. **Given** a designation made in zone A, **When** the author switches back to zone A and undoes,
   **Then** A's designation is taken back.

---

### User Story 3 - The editor shows who the player is (Priority: P3)

The actor section reads the current designation, so designating, undoing and redoing are each visible in
the editor without entering play mode.

**Why this priority**: an undo whose effect cannot be seen is not verifiable, and today no editor surface
shows the designation at all. It ranks last because the designation still *works* without it — pressing
Play shows the follow camera and the walk-around — so the capability does not depend on it.

**Independent Test**: designate an actor, undo, and redo, and confirm the section's indication follows
each.

**Acceptance Scenarios**:

1. **Given** an actor who is the active zone's player, **When** the Inspector shows her, **Then** the
   actor section indicates that she is the player — not merely that she could be set as one.
2. **Given** her designation undone, **When** the Inspector shows her, **Then** the indication is gone.
3. **Given** two zones with different players, **When** the author switches zones, **Then** the
   indication follows the active zone's own designation.

---

### Edge Cases

- **The designated actor is deleted**: unchanged. The designation goes stale and every player system
  no-ops (`Systems.isPlayerLive`); undoing the *delete* restores the actor at the same reference, so the
  designation resolves again. This feature neither clears a designation on delete nor resurrects one.
- **A zone with no environment loaded**: the designation is a zone's, not an environment's; it is set and
  undone the same way.
- **A designation already in memory when this feature lands**: there is no persistence, so nothing is on
  disk to migrate; the live value is simply the "before" of the next step.
- **Undo with an empty history, or an edit after an undo**: unchanged — the existing stack behaviour (a
  new step clears the redo list) applies to this step like any other.
- **Two zones whose actors share a slot index**: handles are per zone and generation-checked, so a
  designation in one zone never resolves in another (`test/GameplayStateTests.fs` already asserts this).
  A restored designation must keep that property.
- **Deleting a zone**: unchanged; its history — including this step — is discarded with it.
- **Play mode**: the designation is an editor action. Play mode neither records one nor clears one, and a
  designation changed while editing is what play mode then drives.
- **An actor in a zone other than the active one**: unreachable — an actor is only shown by the Inspector
  when it belongs to the active zone, so a designation is always made in the active zone.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Setting an actor as the active zone's player MUST be recorded as **one** reversible edit:
  a single undo returns the zone to the designation it had before the click, and a single redo applies it
  again.
- **FR-002**: The previous designation MUST be restored exactly — undoing a *re-designation* MUST restore
  the previous actor, not "no player".
- **FR-003**: The edit MUST belong to the active zone's own history, so an undo or redo applies the
  change in the zone it was made in, and a zone switch carries each zone's history with it (the existing
  per-zone behaviour).
- **FR-004**: Re-applying the designation of the actor who is already the zone's player MUST NOT alter
  the state and MUST NOT add a step to the history.
- **FR-005**: The editor MUST reflect the active zone's current designation, so that designating,
  undoing and redoing it are observable without entering play mode.
- **FR-006**: Play-mode behaviour MUST be unchanged: the systems gated on a live player
  (`playerInput`, `cameraFollow`, `animate`, `updateTriggers`) MUST behave exactly as before for a live,
  absent or stale designation.
- **FR-007**: The designation MUST remain a zone's own session state: never written to disk, never part
  of a registered place, and never shared or aliased between zones.
- **FR-008**: The feature MUST NOT add a new way to designate a player and MUST NOT add clearing the
  designation — the existing action is made reversible, and no action is added.
- **FR-009**: All other editor behaviour MUST be unchanged: the other entity edits, the light's own
  command (spec 043), the changes that are deliberately direct writes (the cast binding, the zone
  registry, the actor's animation and morph edits), and the existing test assertions.
- **FR-010**: `dotnet build` MUST be clean and `dotnet test` MUST pass, with new tests covering the
  reversible designation — the first designation, the re-designation and the no-op — as pure logic that
  needs no GPU.

### Key Entities *(include if feature involves data)*

- **Player designation**: which of a zone's actors is the avatar — the value this feature makes
  revertible. "No designation" is a real state, and is what the first designation undoes to.
- **Zone** (`world/Zone.fs`): owns the designation, and is the unit the editor's history is per.
- **Undo history**: the per-zone applied/redo stacks the edit joins, swapped on a zone switch.
- **Actor reference** (`Entity.EntityRef`): the actor a designation names, with the generation that lets
  a restored designation be told apart from one whose slot was later reused.
- **Inspector actor section** (`editor/Inspector.fs`): the existing surface for the action, and where the
  designation is shown.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After any designation, one undo restores the previous state (100% of cases — no previous
  player, and a previous player) and one redo re-applies it.
- **SC-002**: A designation is exactly one step: 1 click = 1 undo, and re-pressing the control for the
  current player adds 0 steps.
- **SC-003**: A designation made in one zone is never taken back by an undo in another (0 cross-zone
  effects).
- **SC-004**: Play mode is unchanged: for a live, absent or stale designation the player systems behave
  identically to before (0 behaviour differences).
- **SC-005**: The editor shows the current designation (0 ambiguity about who the player is), and the
  indication follows an undo and a redo.
- **SC-006**: `dotnet build` is clean and `dotnet test` passes with the existing assertions unmodified
  and new tests covering the reversible designation.

## Assumptions

- **The action itself is unchanged.** The Inspector's actor section remains the only way to designate a
  player; this feature changes only how the change is recorded. Adding a way to **clear** the designation
  — which the reference's "Player Locomotion" checkbox has and the port's button lacks — is deliberately
  out of scope: that is a new capability, where this is the reversibility of an existing one.
- **The designation is a scene edit, not project-scoped state.** It lives on the zone, so its undo rides
  the zone's own stack — the reasoning spec 043 used for the light (D-024). `docs/EDITOR_DESIGN.md`
  principle 4 and D-022 are amended to move it out of the direct-write list.
- **The control reads its own state.** The smallest change that makes an undo observable; the exact
  affordance (a disabled control, a label, or the reference's stateful checkbox) is the design phase's to
  choose.
- **The designation is not recorded with a place.** `ZoneTemplate` carries no player and this feature
  adds none — instantiating a registered place still sets only the environment, props and light.
- **No persistence.** The project has no save/load, so a designation is session state and there are no
  saved projects to migrate.
- **A stale designation is tolerated, not repaired.** Deleting the designated actor leaves a handle whose
  systems no-op, and undoing the delete makes it resolve again; clearing a designation on delete is not
  this feature.
- **The step is not coalesced.** A designation is a discrete act like a checkbox, not a drag, so
  consecutive designations are separate steps — the same rule the trigger fields and the light controls
  already follow.
