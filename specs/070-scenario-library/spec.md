# Feature Specification: Scenario Library

**Feature Branch**: `070-scenario-library`

**Created**: 2026-10-07

**Status**: Draft

**Input**: User description: "A scenario is a set of zones, with associated actors, props, triggers and waypoints and a cue — effectively a unit of narrative. The user authors scenarios in the editor and later brings them into the game via special cues. Like actors and zones, scenarios are registered against a typed union rather than a string. Rather than a special scenario editor, a Scenario panel like the Cast and Zone Registry panels offers a Register button that saves the current game state to that scenario. Forking a scenario into the game is a later increment; for now, just set scenarios up with their panel and registration."

## Origin

The scripted half of the story model is the cue (`world/Cue.fs`) plus the things a cue acts on — the
zones, their actors, props, waypoints and triggers. A **scenario** is the unit that bundles those into one
authored narrative: a set of zones, each as the author left it, together with the cue to run. `docs/ARCHITECTURE.md`
names it (`Scenario_Library` → `Scenario` = fixed zones of authoring data plus a `cue_root`), and
`docs/ROADMAP.md` phase 11 is where it lands.

The two established shapes this feature follows:

- **A closed vocabulary plus a sparse store.** A place (`world/Place.fs`) is a value of a closed union and
  the zone registered there is a `world/ZoneRegistry.fs` entry keyed by it; a member (`cast/Roster.fs`) is a
  value and the actor asset assigned to her is a `world/Casting.fs` binding keyed by it. A scenario takes the
  same shape: a declared set of scenarios, and the contents registered against one. A scenario that is not
  declared cannot exist — the identity is checked, not spelled.
- **A panel that edits in place rather than through the Inspector.** The Cast panel (`editor/CastPanel.fs`) and
  the Zone Registry panel (`editor/ZoneRegistryPanel.fs`) offer their controls per row because what they edit
  is not a selectable item (`docs/EDITOR_DESIGN.md`, D-022/D-023). The Scenario panel is the third of these:
  one row per scenario, a Register control that files the current game state under it.

Registering captures the *scene* rather than performing a scene edit: it copies the present zones and the
screen's cue into one value filed against a scenario. Unlike the Zone Registry's write, it rides the one undo
history (`state/History.fs`), because the capture is immutable data and a before/after pair makes undoing a
registration — and the overwrite a re-registration performs — total.

A captured zone is **its own type, not `ZoneContents`**. `ZoneContents` (`world/Zone.fs`) exists to reverse a
zone *delete*: it must keep each entity's `EntityRef` and the live `Environment` (epoch and parts map), so a
restored zone lands in the same slots. A scenario wants none of that — a fork instantiates into fresh zones — so
reusing it would push zone-scoped identity and list structures onto scenarios. A scenario zone is instead a
ref-free authored record (arrays of authored states; waypoint links and the player designation by index).
`docs/ZONE_MODEL_DECISION.md` records the reasoning.

What a registered scenario is *for* — forking it into the running cue — is a later increment and is out of
scope here.

## Clarifications

### Session 2026-10-07

- Q: When an author registers a scenario, should the capture also record which place was active? → A: Yes — the active place is part of the captured state, recorded alongside the zones (how a fork later uses it is the deferred increment's concern; it will typically leave the active place unchanged).
- Q: Should the Scenario panel offer a way to clear a registered scenario, or is Register (which overwrites) the only control? → A: Offer a Clear control per scenario that empties it as one undoable step.
- Q: Should a captured zone reuse `ZoneContents` (the delete payload), or be its own type? → A: Its own type — a scenario zone is **ref-free** (arrays of authored states; waypoint links and the player designation by index), so nothing tied to the origin zone's slots is stored.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Register the current game state as a scenario (Priority: P1)

An author builds a scene in the editor — places zones, drops in actors, props, waypoints and triggers, and
authors the cue — then opens the Scenario panel and presses **Register** on a scenario. The scene as it stands
is filed under that scenario, and the row shows it has contents. The author can re-register at any time to
update a scenario to the current state.

**Why this priority**: it is the whole feature. There is no way to turn an authored scene into a reusable
narrative unit without it, and everything later (forking, persistence) depends on a scenario holding the scene.

**Independent Test**: author a scene with more than one zone and a non-empty cue, register it, and assert the
scenario now reports contents and that its contents are exactly the scene that was present (every zone, every
actor/prop/waypoint/trigger, the environment and light, the active place, and the cue); re-register and assert
the contents are the newer scene; clear it and assert it reports no contents.

**Acceptance Scenarios**:

1. **Given** a scenario with no contents and a scene with one or more present zones, **When** the author
   presses Register, **Then** the scenario stores every present zone, the active place and the screen's cue,
   and the row shows it is registered.
2. **Given** a scene with more than one present zone, **When** the author registers it, **Then** every present
   zone is captured, each keyed by the place it sits at.
3. **Given** a scenario already registered, **When** the author presses Register again after editing the scene,
   **Then** the stored contents are replaced by the current scene.
4. **Given** a scene in which a part or prop was deleted or duplicated, **When** the author registers it,
   **Then** the capture matches the scene exactly — the deleted one is absent, the duplicate appears twice.
5. **Given** a registered scenario, **When** the author presses Clear, **Then** the scenario holds no contents
   and the row shows it is unregistered; undoing the Clear restores exactly the contents it held.
6. **Given** a registered scenario whose zones contain cast actors, **When** the panel draws, **Then** their
   character names appear as a short tag list on the scenario's row (the shape the Outliner's zone rows use); a
   scenario whose actors are not in the cast shows no tags.

---

### User Story 2 - A registration is one undoable step (Priority: P2)

An author registers a scenario, changes their mind, and presses Undo: the scenario goes back to whatever it
held before (its previous contents, or none if it was unregistered). Redo re-applies the registration.

**Why this priority**: every other editor edit is a step on the one undo history (`docs/EDITOR_DESIGN.md`,
D-011), and a registration is easy to make by accident against the wrong scenario. It is a smaller win than the
registration itself, so it rides second — but it is in scope, not a follow-up.

**Independent Test**: register over an unregistered scenario, undo, and assert it is unregistered again; then
redo and assert the contents are back. Repeat with a re-registration and assert undo restores the *previous*
contents rather than clearing them.

**Acceptance Scenarios**:

1. **Given** a scenario that was unregistered, **When** the author registers it and then undoes, **Then** the
   scenario is unregistered again and the row shows no contents.
2. **Given** a scenario that was registered, **When** the author re-registers it and then undoes, **Then** the
   scenario holds its *previous* contents again.
3. **Given** an undone registration, **When** the author redoes, **Then** the registered contents are back
   exactly as they were.
4. **Given** a registration on the history, **When** the author undoes it, **Then** no zone, entity, camera or
   selection is disturbed — the scene is untouched.

---

### Edge Cases

- **A scenario registered twice in a row with the same scene**: the second is still one step (an overwrite),
  and undo returns to the first registration's contents.
- **Registering while playing**: the panel is an editor surface, drawn only while editing, so a registration is
  only ever taken from the frozen (authored) state — a mid-run cue cannot be captured.
- **A scene with a single zone and an empty cue**: still a valid registration; a scenario need not be elaborate.
- **The active place is not the only present one**: every present place is captured regardless of which is
  active, and the captured cue is the screen's, not a zone's.
- **A scenario that is declared but never registered**: it appears in the panel showing no contents, and
  Register is offered for it.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The editor MUST present a **Scenario** panel that lists the game's scenarios and, for each, a
  Register control (and, for a scenario that holds contents, a Clear control).
- **FR-002**: Registering a scenario MUST capture the current game state under it: **every present zone** as the
  author left it (its environment and light, its parts, its placed props, its actors, its waypoints and its
  triggers), **the active place** (the place the author was in when registering), and **the screen's authored
  cue**.
- **FR-012**: A captured zone MUST be recorded free of the origin zone's identity — no entity reference, no
  live environment (and so no epoch), and with waypoint chains and the player designation expressed by index —
  so a stored scenario carries nothing that only the zone it was captured from could use.
- **FR-013**: When a scenario holds contents, the panel MUST show the cast characters it contains as a short tag
  list on the scenario's row, in the shape the Outliner's zone rows use, aggregated across the scenario's zones;
  a scenario with no cast actors shows no tags. The tags are derived from the stored contents, not read from the
  live scene.
- **FR-003**: A scenario's identity MUST come from a closed, declared set, so a scenario that has not been
  declared cannot be registered or named; the panel MUST enumerate that set rather than offer free text.
- **FR-004**: A registration MUST be one step on the one undo history. Undoing it MUST restore the scenario's
  previous contents when it had some, and remove it when it had none; redo MUST restore exactly what was
  registered.
- **FR-005**: Re-registering a scenario MUST replace its contents with the current capture (one step, as
  FR-004).
- **FR-006**: The panel MUST show, per scenario, whether it holds contents.
- **FR-007**: The scenario library MUST belong to the gameplay screen, beside its zones, rather than to
  cross-screen state, because nothing outside the gameplay screen and its editor reads it.
- **FR-008**: Registering MUST NOT run, fork or otherwise alter the scene, the running cue, or any zone: it is a
  capture, and undo of it MUST leave the scene undisturbed.
- **FR-009**: The capture MUST be a pure, device-free operation over the gameplay state so it can be asserted
  without opening a window.
- **FR-010**: This feature MUST NOT add cue vocabulary and MUST NOT fork or run a scenario — running a scenario
  is a later increment.
- **FR-011**: The panel MUST offer a Clear control for a scenario that holds contents, emptying it as one step
  on the one undo history; undoing the Clear MUST restore exactly the contents it held.

### Key Entities *(include if data involved)*

- **Scenario**: one authored unit of narrative, identified by a value from a closed, declared set; not a string.
- **Scenario zone**: the ref-free authored record of one zone — its environment as authored, its light, and its
  actors, placed props, waypoints and triggers as arrays of authored states. Its waypoint chains and its player
  designation are recorded by index, not by entity reference, so nothing names the origin zone's slots.
- **Scenario contents**: the captured material filed under a scenario — the set of scenario zones, each keyed by
  the place it sits at, the active place, and the cue.
- **Registration**: the association of contents with a scenario; a scenario either holds contents or does not.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author can file the current scene under a scenario with a single click, and the panel then
  reports that the scenario holds contents.
- **SC-002**: Registering, undoing and redoing round-trips exactly: after undo the scenario holds precisely what
  it held before, and after redo precisely what was registered.
- **SC-003**: A capture taken from a multi-zone scene with actors, props, waypoints, triggers and a cue records
  every one of them (part for part and prop for prop), together with the active place.
- **SC-004**: The capture can be exercised and asserted with no window opened, and the project builds with no
  warnings and a green suite.
- **SC-005**: Registering never changes the running cue, the active zone, the camera or the selection.
- **SC-006**: A registered scenario's cast actors are visible on its own row, without opening another panel.

## Assumptions

- **One scenario to start.** The declared set opens with a single case, `Intro`; adding scenarios later is an
  edit to the declared set, and nothing else lists them.
- **Forking is deferred.** Running a registered scenario — splicing it into the running cue — is a separate,
  later increment (`docs/ROADMAP.md` phase 11's remainder); this feature only sets a scenario up.
- **The capture is the zones, the active place and the cue.** Adventure flags are project state (spec 055) and
  are not part of a scenario's capture; nor is the cast binding (spec 032), which is configuration rather than a
  scene.
- **The runtime cast is derived, not captured.** Which actor plays which character is already recorded on each
  actor, so a scenario need not store it separately.
- **A scenario zone is its own type.** A captured zone is recorded as a ref-free authored record
  (`world/ScenarioZone.fs`), **not** the `ZoneContents` a delete carries. The delete needs the origin zone's
  entity refs and live environment to reverse exactly (`specs/048` FR-009); a scenario wants neither, because a
  fork instantiates into fresh zones. Reusing `ZoneContents` would push zone-scoped refs (and its `(ref, state)`
  list shape) onto scenarios; the two consumers want different shapes, and `docs/ZONE_MODEL_DECISION.md` records
  why the delete's is not the scenario's.
- **The declared set is not the persisted set.** Persisting the scenario library (`docs/ROADMAP.md` phase 15)
  stores the contents per scenario; the set of scenarios remains declared in code, as the places are.
- **A name shared with the narrative engine.** `narrative/Harness.fs` already defines a `Narrative.Scenario`
  for its headless event-script harness — a different concept. The game's scenario vocabulary is kept distinct
  from it.
