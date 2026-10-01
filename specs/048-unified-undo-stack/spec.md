# Feature Specification: One undo history on the gameplay state

**Feature Branch**: `048-unified-undo-stack`

**Created**: 2026-10-01

**Status**: Draft

**Input**: User description: "Undo/redo is scoped per zone, but a cue is scoped on the gameplay
screen. Scoping the command stack per zone was a mistake. Pull it back to the gameplay-state
level, which means switching zones, adding zones and deleting zones have to become commands
as well."

## Clarifications

### Session 2026-10-01

- Q: Is making the deletion of a zone undoable part of this feature, or deferred to a later one? → A: In scope — undoing a delete restores the zone with its entities (in place), environment, light and player, and redo deletes it again (US3, FR-007/FR-008; reverses `specs/024` D-011).
- Q: Should the history record moving between zones as undoable steps, or should each edit remember which zone it belongs to and activate that zone when it is undone? → A: Record the moves — a command carries no zone, and last-in-first-out keeps the active zone the one the step belongs to (FR-004/FR-005; reverses `specs/024` D-5's rejected "commands carrying their zone").
- Q: When adding or deleting a zone also changes which zone is active, is that one undoable step or two? → A: One step — the add or delete carries the zone that was active before it and restores it on undo; only the developer's own move is a move step (FR-005/FR-006).
- Q: When undo or redo steps over a zone move, should it also put the camera and the selection back where they were, or only change which zone is active? → A: Only the zone — undo re-runs the zone change, re-framing the camera on the zone it lands in and leaving the selection cleared (FR-004; the camera is a viewport, not authored scene state, so a move step does not carry it).
- Q: When a developer moves between several zones without editing anything in between, should each move be its own undo step, or should consecutive moves collapse into one? → A: Every move is its own step — a run of navigation takes one undo per move and moves never merge with each other (FR-005).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Undo steps back through edits made in any zone (Priority: P1)

A developer edits an actor in one zone, switches to another zone, edits a prop there, and then
presses undo. Both edits come back off the history in the order they were made — whichever zone
they were made in — and the view follows to the zone each undo touches, so what changed is on
screen rather than in a zone the developer is not looking at.

**Why this priority**: this is the whole point. Today a zone switch swaps the undo history out:
the moment the developer looks at another zone, the edit made a second ago can no longer be
undone. Every later feature that edits something other than a single zone (the cue, and the
project-scoped surfaces after it) inherits that defect.

**Independent Test**: edit in zone A; switch to B; edit in B; undo twice. B's edit reverts first
with B still active, then A's edit reverts with A active again.

**Acceptance Scenarios**:

1. **Given** an edit in A followed by a switch to B and an edit in B, **When** the user undoes
   twice, **Then** the B edit is reverted first and the A edit second, and A is the active zone
   at the end.
2. **Given** the same sequence, **When** the user redoes twice, **Then** both edits are re-applied
   in the order they were made, and the active zone is the zone of the last edit re-applied.
3. **Given** a zone switch with no edit after it, **When** the user undoes, **Then** the switch is
   stepped back — the zone that was active before it is active again — and nothing else changes.

---

### User Story 2 - Adding a zone is undoable (Priority: P2)

A developer adds a zone, realises it was a mistake, and undoes it: the zone is gone and the zone
they were in before is active again. Redo brings the zone back.

**Why this priority**: adding a zone is the one structural edit a developer reaches for routinely,
and today it is the only action that cannot be taken back at all. It is also the smallest case that
proves a non-entity edit can ride the same history as an entity edit.

**Independent Test**: add a zone; undo (it is gone, the previous zone is active); redo (it is back
and active).

**Acceptance Scenarios**:

1. **Given** zones A and B with A active, **When** the user adds zone C and undoes, **Then** C is
   gone and A is active again.
2. **Given** zone C added and then undone, **When** the user redoes, **Then** C exists again and is
   active.

---

### User Story 3 - Deleting a zone is undoable (Priority: P3)

A developer deletes a zone that turned out to matter, and undoes it: the zone returns with what was
in it — its actors and props, its waypoints and triggers, its loaded environment, its light, and its
designated player. Redo deletes it again.

**Why this priority**: deletion is the destructive case, so it is the one where an undo matters most,
but it is also the heaviest step (it has to retain the whole zone), so it lands after the switch and
the add. It is what makes the history a complete account of the editing session rather than a partial
one.

**Independent Test**: build a zone with several entities and a loaded environment; delete it; undo;
assert the zone is back with the same entities (restored in place), the same environment, light and
player; redo deletes it again.

**Acceptance Scenarios**:

1. **Given** a zone holding actors, props, waypoints, triggers and an environment, **When** the user
   deletes it and undoes, **Then** the zone is back with those entities at the same identities, the
   same environment loaded, and the same light and player.
2. **Given** a zone deleted and then undone, **When** the user redoes, **Then** the zone is gone
   again, exactly as the first delete left it.
3. **Given** the active zone deleted (the editor makes another zone active first), **When** the user
   undoes, **Then** the deleted zone is restored **and** made active again.

---

### Edge Cases

- Undo with an empty history, or redo with an empty redo history, is a no-op.
- Making the zone that is already active the active zone records nothing (nothing changed).
- Adding a zone with an empty or already-used name, or deleting the last zone, is not offered by the
  editor and is refused if reached — unchanged from today.
- Deleting an inactive zone leaves the active zone and the camera untouched; the step records which
  zone was active, so undoing it does not switch.
- Undoing or redoing a move re-frames the camera on the zone it lands in and leaves the selection
  cleared: the camera is a viewport rather than part of the authored scene, so a move step does not
  restore it.
- A continuous transform gesture still collapses into one step, and a zone switch between two transforms
  splits them (they belong to different zones and must never merge). Zone moves never merge with each other:
  a run of navigation is one step per move, each undone on its own.
- A new edit after an undo clears the redo history (unchanged).
- Undo/redo does not fire while a gizmo drag is in progress or a text field has focus (unchanged).
- The cue's own history (spec 047) is untouched by this feature: while the editor is open, undoing a
  zone edit and undoing a cue edit remain two different controls.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The editor MUST keep **one** history of scene edits for the gameplay state, not one
  history per zone.
- **FR-002**: The history MUST preserve the order in which edits were made, across zones, so that
  undoing walks the session's edits in reverse order regardless of which zone each touched.
- **FR-003**: Undo MUST revert the most recently made edit whichever zone it was made in; redo MUST
  re-apply the most recently undone edit.
- **FR-004**: When undo or redo reaches an edit recorded in a zone that is not active, that zone
  MUST become active first, through the same path a zone switch takes (bodies detached and
  attached, camera re-framed, zone-scoped selection dropped), so the change is visible and the
  zone's bodies are live while the step runs.
- **FR-005**: Choosing a different zone to be active MUST itself be recorded as a step, so stepping back walks
the developer's zone visits in order and the active zone at any point matches the edit being undone. Each move
is its own step, and consecutive moves MUST NOT merge (unlike a transform gesture). A zone change that an add
or a delete causes is part of that step, not a step of its own (FR-006).
- **FR-006**: Adding a zone MUST be recorded as a step that makes the new zone active; undoing it
  MUST remove the zone and restore the zone that was active before it.
- **FR-007**: Deleting a zone MUST be recorded as a step that retains what is needed to put the zone
  back — its name and its authored contents (entities of every kind, the loaded environment, the
  light, the designated player) — and undoing it MUST restore that zone with those contents.
- **FR-008**: Redo MUST re-apply a zone add and a zone delete.
- **FR-009**: An entity restored by undoing a zone delete MUST have the identity it had before the
  delete, so it is restored in place and any earlier step that names it still resolves.
- **FR-010**: Commands MUST continue to store values rather than closures, and the existing per-kind
  entity command family MUST be unchanged.
- **FR-011**: `Ctrl+Z` / `Ctrl+Y` and the Edit menu MUST operate on this one history.
- **FR-012**: The last zone MUST remain protected, and an add or delete MUST still refuse an invalid
  name or the last zone.
- **FR-013**: Undo/redo MUST NOT fire while a drag is in progress or a text field has focus.
- **FR-014**: The cue's own history (spec 047) MUST be unchanged; moving the cue onto this history is
  a separate feature.

### Key Entities *(include if feature involves data)*

- **The edit history**: the ordered steps applied, plus those undone and available to redo. It
  belongs to the gameplay state rather than to a zone. (Today it is parked per zone and swapped on a
  switch.)
- **A zone step**: one of "make this zone active", "add this zone", "delete this zone" — each
  reversible, each an entry on the same history as an entity edit.
- **A zone's retained contents**: the authored state a delete keeps so the zone can be put back —
  the entities of every kind, the environment, the light and the player. Authored state only: bodies
  and renderers are rebuilt, never retained.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Edits made in two different zones, with a switch between them, are both undone by two
  undos in the reverse order they were made, with the active zone following each — with **zero**
  edits lost by switching zones.
- **SC-002**: Adding a zone and undoing the add leaves the project exactly as it was: the same set of
  zones with the same active zone (0 zones gained or lost).
- **SC-003**: Deleting a zone and undoing the delete restores the zone with the same number of
  entities of every kind and the same environment, light and player, and redoing the delete removes
  it again — 0 entities lost in the round trip.
- **SC-004**: Undoing through the entire history returns the project to its initial state with no
  lost or corrupted zones or entities.
- **SC-005**: `dotnet build` is clean (warnings are errors) and `dotnet test` is green, including
  the existing entity-command and zone tests adapted to the single history.

## Assumptions

- **The cue is out of scope.** Its history (spec 047) stays where it is; this feature only makes the
  history the *scene* edits ride a single one. Joining the cue onto it is the next feature.
- **The cast binding and the zone registry stay direct writes.** Their stated reason for bypassing
  the command system — a per-zone history could not reach them (D-022, D-023) — is removed by this
  feature, but whether they *should* become undoable is a separate decision, deliberately not taken
  here.
- **Deleting a zone becomes undoable, reversing `specs/024` Decision 11.** That decision rejected a
  reversible delete as "out of scope and inconsistent with add/switch"; with one history, add and
  switch are revertible, so an irreversible delete would be the inconsistency. This reversal is
  deliberate and is recorded where the original decision lives.
- **Zone rename does not exist** and is not part of this feature.
- **Undo/redo remains editor-only** (it does not apply while playing), and the history remains
  in-memory (not persisted).
- **A zone's retained contents hold authored state only.** Undoing a delete rebuilds bodies and
  renderers through the same paths an entity create already uses; nothing GPU-side is snapshotted.
