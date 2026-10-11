# Feature Specification: Trim Scenario Scope to Narrative Only

**Feature Branch**: `076-scenario-narrative-only`

**Created**: 2026-10-11

**Status**: Draft

**Input**: User description: "Scenarios currently have too much stuff in them. I don't think scenarios should have environment, light, player, or active zone in them. Before we start the fork work, let's remove those from scenarios."

## Origin

A scenario (`specs/070-scenario-library`) is the game's unit of authored narrative: a set of zones, each as the
author left it, together with the cue to run. `world/ScenarioZone.fs` records a zone as a ref-free authored value,
and `world/ScenarioRegistry.fs` files those under a scenario keyed by the place each sits at.

As it stands, that record is broader than the narrative it represents. A captured zone carries the zone's
**environment** (asset, base, orientation and parts), its **light**, and its **player designation**; the scenario's
contents carry the **active place**. None of those are narrative:

- The environment and its parts belong to the **place**. The Zone Registry's template (`world/ZoneTemplate.fs`,
  `specs/034`, `specs/035`) is where a place's environment is authored, and a runtime zone's environment comes from
  there; a scenario storing a second copy is a second source of truth for the same room.
- The light is the same kind of thing: a per-zone authored setting (`specs/043`), owned by the place.
- The player designation is per-zone state (`Zone.Player`, `specs/045`); "who the player is" is not part of a
  narrative unit, and a fork should not restage it.
- The active place is runtime state on the gameplay screen (`specs/061`); it records where the author happened to be
  standing, not what the scenario is.

Carrying them has concrete costs. The capture must read and re-encode the whole environment (with its parts map) on
every register; the record duplicates `ZoneTemplate`'s authored environment, the near-duplication
`docs/ZONE_MODEL_DECISION.md` already flags; and the upcoming **fork** increment (ROADMAP phase 11's remainder) would
have to decide whether a scenario's environment clobbers the place's — a question that only exists because the
scenario should never have carried one.

This feature trims a scenario to its **narrative entities** — its actors, waypoints and triggers — plus the cue. It is a
removal and a simplification, taken **before** the fork work so that increment starts from the right model.

## Clarifications

### Session 2026-10-11

- Q: Should the scenario keep the `Map<Place, ScenarioZone>` of zones (only the *staging* is dropped), or should a
  scenario stop being zone-structured at all? → A: Keep the zone structure — a scenario still spans zones and keys
  its zones by place. Only the active place, the environment, the light and the player designation leave.
- Q: Should the player designation be preserved somewhere for a fork to reproduce? → A: No. It is a per-zone setting
  (`specs/045`) that stays on the zone; a scenario does not record or restage it.
- Q: Does this change anything an author sees in the Scenario panel? → A: No. Register, Clear and the row preview
  behave exactly as they do today; only what a registration captures changes.
- Q: When an author registers a scenario, should the capture record every present zone — including places with
  no narrative entities — or only the zones that contain entities? → A: Every present zone. Zones are part of a
  scenario, so its place-footprint is recorded in full, empty places included (unchanged behaviour).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A scenario records only its narrative entities (Priority: P1)

An author builds a scene — actors, waypoints and triggers across one or more zones, and the cue — and registers it
under a scenario. The scenario records exactly those entities and the cue, and nothing about the zones' staging: no
environment, no light, no player designation, and no active place.

**Why this priority**: it is the whole change. Everything else is a consequence of a scenario's contents being
narrowed to the narrative it represents. It also unblocks the fork increment, which no longer has to reconcile a
scenario's environment with a place's.

**Independent Test**: build a headless scene with actors, waypoints (one chained), triggers and a cue, plus a
non-default light and a player designation on the zone (a loaded environment needs a registered asset, and the
record has no environment field to compare, so its absence is type-enforced); register it and assert the stored zone
equals a hand-built `ScenarioZone` of exactly the actors, the waypoints (chains by index) and the triggers, and that
the contents carry the cue and the zones keyed by place but no active place.

**Acceptance Scenarios**:

1. **Given** a scene with one or more present zones carrying actors, waypoints and triggers, **When** the author
   registers it, **Then** each stored zone holds exactly those actors, waypoints and triggers, and the contents hold
   the screen's cue and the zones keyed by the place each sits at.
2. **Given** a scene whose zone has a non-default light and a player designation (a loaded environment is exercised
   in the manual walkthrough; the headless capture never reads one), **When** the author registers it, **Then** the
   stored zone carries no light and no player, and the recorded type has no environment to carry.
3. **Given** a scene with two present zones and more than one of them non-active, **When** the author registers it,
   **Then** both zones are captured and the contents record no active place.
4. **Given** a zone whose waypoints form a chain, **When** the author registers it, **Then** the chain is recorded as
   indices into the captured waypoints, exactly as it is today.
5. **Given** a registered scenario, **When** the author presses Clear, **Then** it holds no contents; undoing the
   Clear restores exactly the contents it held.

---

### User Story 2 - Zone staging is independent of a scenario (Priority: P2)

Changing a zone's environment, its light, or which of its actors is the player does not change what a registration
records. Two registrations taken around such a change capture identical contents.

**Why this priority**: it is the observable promise of the trim — that a scenario is a narrative unit and not a
snapshot of the room. It is a smaller, second win, but it is what makes the fork increment unambiguous.

**Independent Test**: register a scene; then change the zone's light and set a different player (a loaded
environment can't be staged headlessly, and the record no longer has one); register again and assert the two captures
are equal (the entity arrays and the cue are unchanged).

**Acceptance Scenarios**:

1. **Given** a registered scenario, **When** the author changes the zone's light and sets a new player — with no
   narrative-entity edits — and registers again, **Then** the new contents are equal to the previous contents.
2. **Given** a scene whose only change since the last registration is staging (light or player),
   **When** the author re-registers, **Then** the captured entities and cue are exactly as before.

---

### User Story 3 - The library's record, panel and docs are coherent with the trimmed model (Priority: P3)

The scenario record's surface, the editor's Scenario panel, the existing scenario tests and the guiding documents
all describe and exercise the trimmed scenario — no environment, light, player or active place anywhere in the
scenario's story.

**Why this priority**: leaving a stale field or a stale sentence behind is how the removed concept creeps back. It is
lower priority than the model change itself but is part of finishing it.

**Independent Test**: build the project and run the suite; read the Scenario panel's Register/Clear path and confirm
it captures the trimmed contents; grep the guiding docs and the scenario tests and confirm none still claim a
scenario records its environment, light, player or active place.

**Acceptance Scenarios**:

1. **Given** the trimmed model, **When** the project is built and the suite is run, **Then** it builds with no
   warnings and the suite is green, with the scenario tests asserting the trimmed capture.
2. **Given** the guiding documents that describe a scenario (`docs/ARCHITECTURE.md`, `docs/EDITOR_DESIGN.md`,
   `docs/ZONE_MODEL_DECISION.md`, `docs/ROADMAP.md`, `docs/PROJECT_STRUCTURE.md`), **When** the feature is complete,
   **Then** none of them still says a scenario carries its environment, light, player designation or active place.
3. **Given** the Scenario panel, **When** the author uses Register and Clear, **Then** the row preview and undoing
   and redoing a registration behave exactly as before.

---

### Edge Cases

- **A zone with no environment loaded**: nothing is dropped and nothing fails; the capture is unaffected.
- **A zone with no player designation**: nothing to omit; the capture is unaffected.
- **Re-registering with no changes at all**: still one step on the history (an overwrite of equal contents), exactly
  as today; undo returns to the same value.
- **An empty scenario zone**: a place with no actors, waypoints or triggers is still captured (as an empty zone), so
  the contents still record every present place.
- **The `Narrative.Scenario` name collision**: `narrative/Harness.fs` defines an unrelated `Narrative.Scenario`; this
  change touches only the game's scenario vocabulary and must not affect it.
- **A scenario registered under the previous, broader shape**: none exists on disk — the scenario library is not
  persisted (ROADMAP phase 15) — so no migration is required.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A captured scenario zone MUST record only the narrative entities — its **actors**, **waypoints** and
  **triggers** — as arrays of authored states, with waypoint chains expressed by index.
- **FR-002**: A captured scenario zone MUST NOT record the zone's **environment** in any form (asset, base,
  orientation, or parts).
- **FR-003**: A captured scenario zone MUST NOT record the zone's **light**.
- **FR-004**: A captured scenario zone MUST NOT record the zone's **player designation**.
- **FR-005**: Scenario contents MUST NOT record the **active place**; the contents are the zones keyed by place and
  the cue, and nothing else.
- **FR-006**: Scenario contents MUST still record **every present zone** (each keyed by the place it sits at,
  including a place with no narrative entities, which is recorded as an empty scenario zone) and the **screen's
  authored cue**.
- **FR-007**: The capture MUST remain **pure and device-free**, so it can be asserted without opening a window; it
  MUST read the zones it is handed and nothing else.
- **FR-008**: Registering a scenario MUST remain **one step** on the one undo history; undoing it MUST restore the
  scenario's previous contents (or remove it when it had none) and redo MUST restore exactly what was captured.
- **FR-009**: Changing a zone's environment, light or player designation MUST NOT change what a registration records
  (FR-002–FR-004); a registration taken after only such changes equals the earlier one.
- **FR-010**: The scenario **vocabulary and panel behaviour** MUST be unchanged from the author's point of view:
  the closed declared set, the per-scenario row, the Register and Clear controls, the row's contents preview, and
  the one-history step all behave as they do today.
- **FR-011**: This feature MUST NOT change any zone, entity, environment, light, player or cue in the scene, and MUST
  NOT add any cue vocabulary; recording a scenario is a capture, not a scene edit.
- **FR-012**: The command that records a scenario registration MUST keep its present shape — a state-level step
  holding the contents before and after the edit — so undo and redo of a registration are unchanged.
- **FR-013**: The guiding documents that describe a scenario (`docs/ARCHITECTURE.md`, `docs/EDITOR_DESIGN.md`,
  `docs/ZONE_MODEL_DECISION.md`, `docs/ROADMAP.md`, `docs/PROJECT_STRUCTURE.md`) MUST be updated to the trimmed
  model so none still records a scenario's environment, light, player designation or active place.
- **FR-014**: The scenario tests MUST be updated to assert the trimmed capture (the narrative entities, the chains by
  index, the cue, and the zones by place), and MUST NOT assert an environment, light, player or active place.

### Key Entities

- **Scenario**: one authored unit of narrative, identified by a value from a closed, declared set; unchanged.
- **Scenario zone**: the ref-free authored record of one zone — now **only** its actors, waypoints and triggers as
  arrays of authored states, with waypoint chains by index. It no longer holds an environment, a light or a player
  designation.
- **Scenario contents**: the material filed under a scenario — the set of scenario zones keyed by the place each
  sits at, and the cue. It no longer holds an active place.
- **Removed concepts**: the authored environment a scenario zone used to carry; a scenario zone's light; a scenario
  zone's player designation; and the active place on scenario contents. The environment remains the property of the
  **place** (the Zone Registry's template), the light remains a per-zone authored setting, the player designation
  remains per-zone state, and the active place remains runtime state on the gameplay screen.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A scenario captured from a scene with actors, chained waypoints, triggers and a cue records exactly
  those entities and the cue — with no environment, light, player designation or active place anywhere in the
  captured value.
- **SC-002**: Two registrations taken around a change to the zone's environment, light and player produce equal
  contents (given no narrative-entity edits between them).
- **SC-003**: Register, clear, undo and redo round-trip exactly as before: after undo the scenario holds precisely
  what it held, and after redo precisely what was registered.
- **SC-004**: The capture can be exercised and asserted with no window opened; the project builds with no warnings
  and the suite is green.
- **SC-005**: No guiding document still states that a scenario records its environment, light, player designation or
  active place.
- **SC-006**: An author's use of the Scenario panel is unchanged — Register and Clear, the row preview, and the
  single undoable step all behave as they do today.

## Assumptions

- **Zone structure stays.** A scenario still spans zones and keys them by place; only the active place and the
  zone-scoped staging (environment, light, player) leave. The set of zones in a scenario is its narrative footprint,
  not staging.
- **The place owns its staging.** A place's environment and light are authored in the Zone Registry
  (`world/ZoneTemplate.fs`); a runtime zone gets them from there. Scenario contents no longer duplicate them, which
  also removes the near-duplication of `ZoneTemplate` that `docs/ZONE_MODEL_DECISION.md` records.
- **The player designation stays per-zone.** It is set on the active zone (`specs/045`) and is not part of a
  narrative unit; a scenario does not record or restage it.
- **The active place stays on the gameplay state.** It is runtime state (`specs/061`); a scenario does not record it.
- **Forking remains deferred.** Running a registered scenario into the running cue is a separate, later increment
  (ROADMAP phase 11's remainder). This feature only trims the model first; it adds no cue vocabulary and no fork.
- **Placed props remain out.** A placed prop is environment decoration and is already not captured; this feature does
  not reopen that question.
- **No migration is needed.** The scenario library is not persisted yet (ROADMAP phase 15), so no stored value holds
  the broader shape.
- **Prior spec artifacts are historical.** `specs/070-scenario-library` described the broader record and is left as
  the record of what was built then; this spec supersedes its shape, and the guiding `docs/` are updated instead.
- **The declared set is unchanged.** The scenario vocabulary (`world/Scenario.fs`) still opens with a single case,
  `Intro`; this feature does not add or remove scenarios.
