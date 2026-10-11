# Feature Specification: Fork a Scenario into the Scene

**Feature Branch**: `078-fork-scenario`

**Created**: 2026-10-11

**Status**: Draft

**Input**: User description: "Forking a scenario should just add all the entities in the scenario to the scene, and fork its cue in parallel to the current running cue. We want to fork scenarios both via the editor and via cues."

## Origin

A scenario (`specs/070-scenario-library`) is the game's unit of authored narrative: a set of zones — each a
`ScenarioZone` of **narrative entities** (actors, waypoints with their chains by index, and triggers) — together
with the cue to run. Since `specs/076-scenario-narrative-only` a scenario carries **only** those entities and the cue:
no environment, light, player designation or active place. Registering a scenario captures the current scene; what a
registered scenario is *for* — **running** it by **forking** it into the live scene — was deferred
(`docs/ROADMAP.md` phase 11's remainder). This feature is that remainder.

A **fork** appends a scenario into the scene as it stands and runs its cue beside the running one:

- **Materialise**: add every recorded zone's entities into that place's zone — creating the place first when it is
  absent — without switching the active place.
- **Fork the cue**: run the scenario's cue in parallel to whatever is already running (`Cue.forkInto`, the mechanism
  triggers already use, `specs/058-trigger-cue`).

It is **non-idempotent**: a fork adds copies, and a second fork adds a second copy — the author is responsible for
keeping scenarios from clashing.

This feature is the first consumer of two recent increments. `specs/076-scenario-narrative-only` means a fork has no
staging to reconcile (a created place takes its environment and light from the Zone Registry's template; the active
place and the player are untouched). `specs/077-commands-carry-zone` means a fork that spans several places is an
ordinary `Batch` on the one undo history — no special command, and no switching the active place.

Two entry points are wanted, and both are in scope: a **`ForkScenario` cue op** (a runtime effect, like every other
cue op) and an **editor control** (one undoable step). Odin's other scenario ops — `InlineScenario`, `ClearScenario` —
and the camera-follow / interaction toggles are **not** in scope.

## Clarifications

### Session 2026-10-11

- Q: When a fork cannot place all of the scenario's entities because a target zone lacks room, does the whole fork do
  nothing, or does it skip the full zone and fork the rest? → A: All-or-nothing, checked up front — the capacity of
  every target zone is checked before any entity is added, so a fork either fully applies or nothing at all comes of
  it (FR-010).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A cue forks a scenario into the running scene (Priority: P1)

While the game is running, the cue reaches a `ForkScenario` op naming a registered scenario. Every entity of the
scenario's zones is added to the scene (its places created if absent), and the scenario's cue begins to run **beside**
whatever was already running — nothing in flight is disturbed.

**Why this priority**: it is the feature's reason to exist — turning an authored scenario into live story at run time.
It is the smallest complete slice: one op, one materialisation, one fork.

**Independent Test**: register a scenario whose zones carry actors, a chained waypoint pair and triggers; run a cue
whose sequence reaches `ForkScenario`; assert the entities appear in their places and the scenario's cue runs in
parallel with the rest of the running cue. Unregistered: assert the op is a no-op with a diagnostic.

**Acceptance Scenarios**:

1. **Given** a registered scenario with entities across present places, **When** a running cue reaches
   `ForkScenario`, **Then** every entity is added to its place's zone and the scenario's cue runs beside the rest.
2. **Given** the same scenario names a place that is not present, **When** the op runs, **Then** the place is made
   present and its entities added, and the active place is unchanged.
3. **Given** a scenario that is not registered, **When** a cue reaches `ForkScenario`, **Then** the op performs
   nothing and reports a diagnostic; the running cue is otherwise unaffected.
4. **Given** a fork whose scenario cue is `Fin`, **When** the op runs, **Then** the entities are still added and the
   running cue is unchanged.

---

### User Story 2 - The editor forks a scenario as one undoable step (Priority: P2)

An author, editing, presses **Fork** on a scenario's row. Every entity is added to the scene and the scenario's cue
is forked into the screen's cue, all as **one** step on the one undo history: one Ctrl+Z removes exactly what the
fork added and restores the cue as it was; one Ctrl+Y re-applies it.

**Why this priority**: it is the authoring-time counterpart of US1, and the place the author tests a scenario without
running the game. It is a smaller win than the runtime op, but it is in scope.

**Independent Test**: register a scenario; press Fork; assert the entities appear, the screen's cue became the fork,
and exactly one step is on the history; undo and assert the entities are gone, created places are gone, and the cue
is exactly what it was; redo and assert it all returns.

**Acceptance Scenarios**:

1. **Given** a registered scenario, **When** the author presses Fork, **Then** its entities are added, its cue is
   forked into the screen's cue, and the whole is one step.
2. **Given** a fork on the history, **When** the author undoes it, **Then** the added entities and any created places
   are removed, the cue returns to what it was before, and the active place, camera and selection are unchanged.
3. **Given** an undone fork, **When** the author redoes it, **Then** the entities and the cue fork return exactly.
4. **Given** a scenario that is not registered, **When** the panel draws, **Then** its Fork control is disabled.

---

### User Story 3 - A fork reproduces the scenario's footprint, and clashes are handled (Priority: P3)

Forking a scenario brings its **places** into the scene, not just its entities: a recorded place that is absent is
made present; one that is already present is appended to. Duplicates are allowed, and a scenario that would overflow
a zone's capacity is logged and does nothing rather than raising.

**Why this priority**: it defines what "fork" means for the scene as a whole and how it behaves at its edges. It is
lower priority than the two entry points but is part of finishing the feature.

**Independent Test**: fork a scenario that names an absent place and an already-present one; assert the absent place
is created (from its registered template, or empty) and the present one is appended to; fork twice and assert a
second copy; force a capacity overflow in one target zone and assert a logged no-op that adds nothing anywhere
rather than a raise.

**Acceptance Scenarios**:

1. **Given** a scenario naming an absent place with a registered template, **When** it is forked, **Then** the place
   is present with that template's environment and light, its entities added, and the active place unchanged.
2. **Given** a scenario naming an absent place with **no** registered template, **When** it is forked, **Then** the
   place is present and empty but for the scenario's entities.
3. **Given** a registered scenario, **When** the author forks it twice, **Then** the scene holds two copies of every
   entity (a name is a label, not an identity).
4. **Given** a target zone whose slot map cannot take the scenario's entities, **When** it is forked, **Then** the
   fork logs the reason and adds nothing at all — no place created, no entity added, no cue forked — rather than
   raising or applying partially.

---

### Edge Cases

- **An empty scenario zone**: the place is made present and nothing is added.
- **A scenario zone whose place is already present**: only its entities are appended; the zone's own environment,
  light and player designation are left as they are.
- **An empty cue (`Fin`)**: the entities are added and the running cue is untouched.
- **Forking twice**: a second copy is added; nothing is de-duplicated.
- **A target zone at capacity**: the fork logs and adds nothing at all — it never raises and never applies partially.
- **A waypoint chain inside the scenario**: its links are reproduced among the appended waypoints (an index into the
  scenario's own array maps to the newly added waypoint).
- **Forking in the editor while playing**: not offered — the Scenario panel is an editing-time surface.
- **A scenario whose place count exceeds available zones**: only declared places exist, so a scenario can only name
  declared ones; a place already present is appended to.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A fork MUST add every entity of every recorded scenario zone into that place's zone: its actors, its
  waypoints (with their chains re-linked among the added waypoints) and its triggers.
- **FR-002**: A fork MUST make a scenario's place present when it is absent — from its Zone Registry template if one
  is registered, else empty — **without** changing the active place.
- **FR-003**: A fork MUST fork the scenario's cue **in parallel** to the running cue, so nothing in flight is
  disturbed and the scenario's cue runs beside what is already running.
- **FR-004**: A fork MUST NOT change the active place, the camera or the selection; it appends, it does not switch.
- **FR-005**: A fork is **not idempotent**: forking twice adds a second copy of every entity; a duplicate entity name
  is a legitimate scene (a name is a label, not an identity).
- **FR-006**: The **cue op** (`ForkScenario`, naming a scenario) MUST materialise the scenario and fork its cue where
  the walk reaches it; it is a **runtime effect**, not a step on the undo history, like every other cue op.
- **FR-007**: The **editor** fork MUST be exactly **one step** on the one undo history: undoing it removes the added
  entities and any places it created and restores the cue as it was; redo re-applies all of it.
- **FR-008**: The editor MUST offer a **Fork** control per scenario, **disabled** when the scenario is not registered.
- **FR-009**: A fork of an **unregistered** scenario MUST do nothing: the editor control is disabled, and the cue op
  is a no-op that reports a diagnostic (the convention the actor and trigger cue ops use).
- **FR-010**: A fork MUST check, before adding anything, that **every** target zone has room for the scenario's
  entities; if any does not, the fork MUST **log** the reason and **do nothing at all** — no place created, no entity
  added, no cue forked — rather than raising or applying partially.
- **FR-011**: The cue op MUST be authored through the Cue editor's existing controls: it takes a scenario chosen from
  the closed, declared set (no free text), and it runs the scenario's cue in the `Fork` position (beside the rest).
- **FR-012**: Play-pruning (`History.pruneCueSteps`) MUST NOT leave the fork's cue effect on the history to be
  applied against an advanced cue — it is pruned on the way into play exactly as a `SetCue` step is.
- **FR-013**: This feature MUST NOT add Odin's other scenario ops (`InlineScenario`, `ClearScenario`) and MUST NOT add
  the camera-follow or interaction toggles.
- **FR-014**: The tests MUST cover the cue op, the editor step and its undo/redo, the place-footprint rules
  (create-absent / append-present), non-idempotence, the capacity no-op, and the unregistered no-op — all headlessly.
- **FR-015**: The guiding documents that describe running a scenario (`docs/ARCHITECTURE.md` "Scenario & Cue",
  `docs/EDITOR_DESIGN.md` Workspace, `docs/ROADMAP.md` phase 11) MUST be updated to say a scenario is forked into the
  scene and the cue.

### Key Entities

- **Scenario**: the closed vocabulary (`world/Scenario.fs`); a `ForkScenario` op names one, and the editor Fork
  control is offered per case.
- **Scenario contents**: the registered material forked — `Map<Place, ScenarioZone>` plus the cue
  (`world/ScenarioRegistry.fs`).
- **Scenario zone**: the ref-free record forked into a place's zone — actors, waypoints (chains by index), triggers.
- **Fork**: the act itself — materialise the scenario's zones into the scene (creating absent places) and fork its
  cue. In the editor it is one `Command` (a `Batch`); at runtime it is a cue op's effect.
- **Presence step**: making a place present **without** entering it, the one new reversible step the editor fork
  needs (the existing "add a place" step also activates it).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A cue that reaches a `ForkScenario` op adds every entity of the named scenario to the scene and its cue
  runs in parallel with the cue that reached it.
- **SC-002**: A fork makes each recorded place present, appending into places already present without changing the
  active place, camera or selection.
- **SC-003**: Forking a scenario twice yields two copies of its entities — a fork is not idempotent.
- **SC-004**: In the editor, a fork (entities and cue together) is a single undo step that round-trips exactly.
- **SC-005**: An unregistered scenario and an over-capacity target are both no-ops that add nothing at all and do not
  raise (the first reports a diagnostic; the second logs the reason).
- **SC-006**: The capture and the fork can be exercised and asserted with no window opened; the project builds with no
  warnings and the suite is green.

## Assumptions

- **Zones are part of a scenario** (`specs/076`): every recorded place is materialised — empty ones included — and
  keyed by the place it sits at.
- **A scenario carries no staging** (`specs/076`): a created place takes its environment and light from the Zone
  Registry's template, or is empty when none is registered. An already-present place keeps its own.
- **The active place and the player are untouched.** A fork is additive; neither a scenario nor a fork moves the
  player — the cue's own zone ops are how the author does that.
- **A multi-place fork is one `Batch`** (`specs/077`): commands carry their place, so the editor fork needs no
  special command, and the runtime fork is a direct effect with no history.
- **Capacity is logged, not raised** — a deliberate deviation from the project's fail-loud rule, chosen so a run-time
  cue op cannot crash the game. The check is **all-or-nothing and up front**: a fork verifies every target zone has
  room before adding anything, and does nothing at all when any lacks it.
- **`Fork` is the parallel construct** (`world/Cue.fs`): a cue op runs the scenario's cue beside the rest of the run,
  which is only meaningful where `Fork` is (a sequence's current op).
- **The cue op is a runtime effect**: like every cue op it is not on the undo history, so it is not undoable by
  design, while the editor fork is.
- **Out of scope**: `InlineScenario`/`ClearScenario` and the camera-follow/interaction toggles
  (`docs/ROADMAP.md` phase 11 names them; they are separate increments).
- **Prior specs are historical**: `specs/070-scenario-library` described the registration half and is left as the
  record of what was built then; this feature supersedes its "fork deferred" note.
