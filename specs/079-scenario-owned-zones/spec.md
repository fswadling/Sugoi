# Feature Specification: Scenario-Owned Zones

**Feature Branch**: `079-scenario-owned-zones`

**Created**: 2026-10-11

**Status**: Draft

**Input**: User description: "Retire the Zone Registry. Treat the scenario as the primary unit of game authoring: a scenario carries its zones' environment settings — environment, light, placed props — as well as their narrative entities, and forking a scenario loads the whole scenario. Zones are addressed by string name, like actors, waypoints and triggers. When a fork names a zone that already exists, the scenario's environment settings replace the existing zone's."

## Origin

A **scenario** (`specs/070-scenario-library`) is the game's unit of authored narrative: a set of zones plus the
cue to run. `specs/076-scenario-narrative-only` deliberately trimmed it to **narrative entities only** — a zone's
actors, waypoints and triggers — leaving its **staging** (its environment, light and placed props) to the **Zone
Registry** (`specs/034-zone-registry`), a separate `Place → staging recipe` store keyed by the closed `Place`
union (`specs/061-zones-as-places`). Running a scenario by **forking** it into the live scene
(`specs/078-fork-scenario`) has, until now, read a created zone's staging from that registry.

This feature **retires the Zone Registry** and moves staging back into the scenario, making the scenario the
**primary unit of game authoring**: you set up and register a scenario, and forking it loads the *whole* scenario —
its zones (created or restaged, with their environment, light and props) and their entities, and forks its cue.
It also drops the closed `Place` vocabulary: like actors, waypoints and triggers, a zone is addressed by a **name**
(a string id), not a case in a union.

Two consequences of the current split disappear. There is no second "zone as authored" value to keep in step with
the zone (the near-duplicate `ZoneTemplate` ↔ `ZoneContents` overlap recorded in `docs/ZONE_MODEL_DECISION.md`),
and a zone brought in by a fork is described **in one place** (the scenario) rather than partly by the registry and
partly by the scenario.

## Clarifications

### Session 2026-10-11

- Q: With the Zone Registry gone, where does a cue op that makes a zone present **outside** a scenario (the spec
  062 `AddZone`/`PreloadZone`/`RemoveZone`) get its staging from? → A: **Retire the staging-dependent zone cue ops.**
  `AddZone` and `PreloadZone` are superseded by scenario forks — a cue that brings a zone into the scene does so by
  forking a scenario, which carries that zone's staging (FR-012). `SetZone` (switch to a present zone) and
  `RemoveZone` (make a present zone absent) need no staging and are unchanged.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A scenario is the unit of authoring; forking loads the whole scenario (Priority: P1)

An author sets up a scene — zones with their environments and light, their actors, waypoints and triggers — and
**registers** it as a scenario. Later, in a fresh or different scene, they **fork** that scenario: every zone it
names comes up **fully** — its environment, its light and its placed props as well as its entities — and its cue
begins to run beside whatever is running. No separate "zone registry" step is involved.

**Why this priority**: it is the feature's reason to exist and the smallest complete slice: one authored unit that
carries everything a scene needs, loadable in one gesture. It is what makes a scenario a *scene*.

**Independent Test**: register a scenario whose zones carry distinct environments, lights and placed props; fork it
into a world that does not contain them; assert each zone is present with its environment, light, placed props and
entities, and that its cue runs in parallel. No Zone Registry control is touched.

**Acceptance Scenarios**:

1. **Given** a registered scenario with a zone that has a distinct environment and light, **When** the scenario is
   forked into a world without that zone, **Then** the zone is present carrying that environment, light, props and
   entities.
2. **Given** a scenario registered from a multi-zone scene, **When** it is forked, **Then** every recorded zone is
   brought up with its own staging and entities.
3. **Given** a scenario whose zones are forked, **When** the fork completes, **Then** the scenario's cue runs beside
   the running one, and the active place, camera and selection are unchanged.

---

### User Story 2 - Forking into an existing world restages the zones it names (Priority: P2)

An author forks a scenario into a world where a zone it names is **already present**. Rather than creating a second
zone of the same name, the fork **reuses** that zone: the scenario's environment settings **replace** the zone's
current environment, light and placed props, and the scenario's entities are appended. Zones are never duplicated;
entities may be.

**Why this priority**: it defines what "fork here" means when the world already holds the zone, and it is the
decision that makes a scenario a *whole scene* rather than a partial overlay.

**Independent Test**: present a zone with one environment; fork a scenario naming that zone with a different
environment and its own entities; assert the zone count did not grow, the zone now carries the scenario's
environment/light/props, and its entities were appended.

**Acceptance Scenarios**:

1. **Given** a present zone, **When** a scenario naming that zone is forked, **Then** the zone's environment, light
   and placed props become the scenario's, and its entities are appended — the zone is **not** duplicated.
2. **Given** a scenario forked twice, **When** both forks run, **Then** the zone's staging matches the scenario (the
   same both times) and its entities are duplicated (two copies), so the whole is not idempotent.
3. **Given** a fork whose scenario restages the **active** zone, **When** the fork completes, **Then** the active
   place, camera and selection are unchanged (the world's contents changed, not the view).

---

### User Story 3 - Zones are addressed by name; the closed vocabulary and the registry retire (Priority: P3)

Zones are named the way actors, waypoints and triggers are: a **string**. There is no closed list of places a zone
must be declared in, and no Zone Registry panel or staging store. A zone that is named but not yet present is
brought into existence by naming it (via a scenario fork), exactly as a named actor is brought in by an actor op.

**Why this priority**: it is the structural change that removes the two layers this feature exists to remove. It is
lower priority than the fork behaviour but is what makes the model uniform.

**Independent Test**: fork a scenario naming a zone that no code declares; assert it comes up. Assert the codebase
no longer routes a zone through a `Place`-keyed registry or a closed vocabulary, and the Registry panel is gone.

**Acceptance Scenarios**:

1. **Given** a scenario naming a zone by a name the code does not declare, **When** it is forked, **Then** the zone
   is created with the scenario's staging (no declaration step required).
2. **Given** the editor, **When** the author authors or forks a scenario, **Then** zones are named as strings (typed
   or chosen from the scenario's own zone names) and there is no Zone Registry surface.

---

### Edge Cases

- **A scenario naming a zone absent from the world**: it is created with the scenario's staging.
- **A scenario naming a zone already present**: it is restaged (staging replaced) and appended to — never duplicated.
- **A zone whose current environment is the same asset as the scenario's**: the restage is still correct (the zone
  ends carrying the scenario's staging); it need not special-case the no-change.
- **The active zone is restaged by a fork**: its environment and light change, but it is not switched away from, and
  the camera and selection are untouched.
- **A target zone (present or created) that lacks room for the entities or props**: the fork logs the reason and
  adds **nothing at all**, applying to staging as well as entities (all-or-nothing, checked up front).
- **Two scenarios naming the same zone with different staging**: the later fork wins; keeping scenarios from clashing
  is the author's responsibility, as it already is for entities (this just makes the clash visible in the look too).
- **An empty scenario zone**: the zone is made present (or restaged) and nothing is appended.
- **A scenario whose cue is `Fin`**: the zones are still brought up; the running cue is untouched.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A scenario MUST record, for each zone it holds, that zone's **staging** — its environment (asset and
  placement and parts), its placed props, and its light — **in addition to** its narrative entities (actors,
  waypoints with their chains, and triggers). *(This reverses `specs/076-scenario-narrative-only`.)*
- **FR-002**: **Registering** a scenario MUST capture the current scene's zones with their staging and entities, and
  the screen's cue, as one authored value.
- **FR-003**: A fork MUST **ensure** every recorded zone exists by **name**: create it when absent, reuse it when
  present. It MUST never create a second zone with a name already present. A zone's name is its identity (as against
  an entity's name, which is a label).
- **FR-004**: A fork MUST apply the scenario's staging to **every** recorded zone: for a **created** zone by loading
  that environment and light and placing those props; for a **present** zone by **replacing** its environment, light
  and placed props with the scenario's.
- **FR-005**: A fork MUST **append** the scenario's narrative entities into each zone's zone — actors, waypoints
  (chains re-linked among the appended waypoints) and triggers. A fork is **not idempotent**: forking twice adds a
  second copy of every entity, and a duplicate entity name is a legitimate scene.
- **FR-006**: A fork MUST fork the scenario's cue **in parallel** to the running cue, disturbing nothing in flight.
- **FR-007**: A fork MUST NOT change the active zone, the camera or the selection — including when it restages the
  active zone.
- **FR-008**: The **cue op** (`ForkScenario`) MUST remain a **runtime effect**, not a step on the undo history; the
  **editor** fork MUST remain exactly **one** step that undoes and redoes the whole (the zones' staging, the appended
  entities and the cue fork).
- **FR-009**: A fork MUST check, before changing anything, that **every** target zone has room for the entities **and
  props** to be added; if any lacks room, the fork MUST **log** the reason and **do nothing at all** — no zone
  created or restaged, no entity or prop added, no cue forked — rather than raising or applying partially.
- **FR-010**: Zones MUST be identified by **name** (a string), not by a closed vocabulary: a zone that is not
  declared by the code can be brought into existence by naming it (via a fork). No type-level list of zones MUST
  gate whether a zone can exist.
- **FR-011**: The **Zone Registry** — the separate staging store and its editor panel — MUST be retired. A zone's
  staging is carried by the scenario(s) that define it, and there is no other store to keep in step.
- **FR-012**: The **staging-dependent zone cue ops** (`AddZone`, `PreloadZone`; `specs/062`) MUST be retired: a cue
  that brings a zone into the scene does so by **forking a scenario**, which carries that zone's staging. The
  **`SetZone`** op (switch to a present zone) and the **`RemoveZone`** op (make a present zone absent) need no staging
  and MUST remain.
- **FR-013**: A zone's **player designation** MUST NOT be part of a scenario's staging (it is per-zone runtime state,
  as in `specs/076`) and MUST NOT be changed by a fork.
- **FR-014**: The tests MUST cover: the staging round-trip (register then fork restores environment/light/props),
  ensure-and-restage into a present zone (no duplicate), entity appension and non-idempotence, the all-or-nothing
  capacity refusal (entities **and** props), the cue op as a runtime effect, the editor fork as one undoable step,
  and the absence of the retired registry — all headlessly.
- **FR-015**: The guiding documents that describe zones, staging and the registry (`docs/ARCHITECTURE.md`,
  `docs/EDITOR_DESIGN.md`, `docs/ZONE_MODEL_DECISION.md`, `docs/ROADMAP.md`) MUST be updated: zones are named, a
  scenario carries its zones' staging, and the Zone Registry is retired. `specs/061`, `specs/062` and `specs/076` are
  left as historical records (superseded).

### Key Entities

- **Scenario**: the primary authored unit — a set of zones, each carrying its **staging** and its narrative entities,
  together with the cue to run. Registering captures the scene; forking loads it.
- **Zone**: a named runtime container (`Zone`); it is identified by a **string name** and holds its environment,
  light, placed props and narrative entities while it is present.
- **Zone staging**: a zone's environment (asset, placement and parts), its placed props, and its light — the authored
  "look" a scenario carries for each of its zones (today's `ZoneTemplate`).
- **Fork**: the act of loading a scenario into the scene — **ensure** each zone by name, **restage** it, **append**
  its entities, and **fork** its cue.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Registering a scenario and forking it into a world without its zones brings up every zone **fully** —
  its environment, light, placed props and entities — with no Zone Registry interaction at all.
- **SC-002**: Forking a scenario naming a zone that is **already present** replaces that zone's environment, light
  and placed props with the scenario's and appends its entities, and the number of zones does not grow.
- **SC-003**: No Zone Registry store or panel and no closed vocabulary of zones remain in the codebase; a zone is
  named, and a zone the code does not declare can be brought up by a fork.
- **SC-004**: In the editor, a fork — staging replacement, appended entities and the cue fork together — is a single
  undo step that round-trips exactly; at run time the cue op is a runtime effect with no history step.
- **SC-005**: A fork whose target lacks room (for entities or props) logs the reason and changes nothing at all, and
  does not raise.
- **SC-006**: The staging capture and the fork can be exercised and asserted with no window opened; the project
  builds with no warnings and the suite is green.

## Assumptions

- **The scenario is the primary unit of authoring.** Setting up and registering a scenario is the way a scene is
  authored; forking a scenario loads the whole scene. (Established in conversation, 2026-10-11.)
- **Zones are named like entities.** A zone id is a string, as an actor, waypoint or trigger name is; typos are the
  author's to avoid, and a chooser offers the scenario's own zone names. This is the same trade `specs/072` made for
  actors.
- **"Staging" = environment + light + placed props** (today's `ZoneTemplate`). The **player designation** is not
  staging (it is per-zone runtime state) and is not captured or forked (`specs/076`).
- **Zones are ensured, entities are appended.** A zone's name is its identity, so a fork never duplicates a zone; an
  entity's name is a label, so a fork may duplicate an entity (FR-003 vs FR-005). This asymmetry is intended.
- **A present zone is restaged.** When a fork names a present zone, the scenario's staging replaces the zone's
  (chosen 2026-10-11), because a scenario is a whole scene.
- **Boot is unchanged.** The runtime world still starts with the start zone present and empty
  (`GameplayState.create`); establishing the initial scene is done by forking a scenario (or authoring the zone),
  and a boot-by-scenario convenience is **not** in this feature's scope.
- **Prior specs are historical.** `specs/061-zones-as-places` (the closed vocabulary) and
  `specs/076-scenario-narrative-only` (narrative-only scenarios) are superseded; `specs/062-zone-cue-ops`'s
  staging-dependent ops (`AddZone`/`PreloadZone`) are retired (FR-012) while its `SetZone`/`RemoveZone` remain;
  `specs/077` and `specs/078` are edited to carry a zone **name** instead of a place.
- **Out of scope**: odin's `InlineScenario`/`ClearScenario` and the camera-follow/interaction toggles (unchanged);
  persistence of the scenario library (ROADMAP phase 15); and any boot/loading-screen scene assembly.
