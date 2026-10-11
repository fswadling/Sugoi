# Research: Trim Scenario Scope to Narrative Only

**Feature**: `specs/076-scenario-narrative-only` | **Date**: 2026-10-11

There are no open `NEEDS CLARIFICATION` items — the spec's Clarifications session settled the only two decisions
(zone structure stays; the player designation is dropped rather than preserved). The notes below record the design
decisions this plan rests on and the alternatives weighed, so `/speckit-tasks` and implementation do not have to
re-derive them.

## Decision: what leaves the scenario record

**Decision.** The scenario record loses four things:

| Removed | From | Where the concept lives instead |
|---|---|---|
| `Environment : ScenarioEnvironment option` | `ScenarioZone` | the place's `ZoneTemplate` (`world/ZoneTemplate.fs`) |
| `Light : Texture.LightSettings` | `ScenarioZone` | the zone/place's authored setting (`specs/043`) |
| `Player : int option` | `ScenarioZone` | the live zone's `Player` (`Zone.Player`, `specs/045`) |
| `Active : Place` | `ScenarioContents` | the gameplay state's `ActiveZone` (`specs/061`) |

A `ScenarioZone` becomes exactly three arrays — actors, waypoints (chains by index) and triggers.

**Rationale.** Each removed field is state the scenario does not own: the environment and light are the place's,
authored in the Zone Registry; the player designation is per-zone; the active place is runtime. Storing them made
the scenario a second source of truth for things that already have one, and forced the capture to read and re-encode
the whole environment (with its parts) on every register.

**Alternatives considered.**

- *Keep the environment but drop the rest.* Rejected: the environment is the same class of mistake as the light, and
  it is the near-duplication of `ZoneTemplate` that `docs/ZONE_MODEL_DECISION.md` already flags. Dropping only some
  of the four leaves the second source of truth half in place.
- *Replace the environment with a registry reference (`Place` → template).* Rejected: the scenario already keys its
  zones by `Place`, so a per-zone environment reference is redundant — the place *is* the registry key. A fork
  resolves the environment from the place.
- *Keep `Player` so a fork can restage the avatar.* Rejected (spec Clarifications): "who the player is" is not part
  of a narrative unit, and a fork should not reassign the avatar; `specs/045` owns the designation on the zone.

## Decision: delete the now-unused type and helpers

**Decision.** Delete `ScenarioEnvironment` outright, and delete the two private helpers that existed only to serve
the removed fields: `captureEnvironmentParts` (built the environment's parts array) and the player-index half of
`captureActors` (which returned the player's index among the actors).

**Rationale.** Minimalism (constitution I). After the trim nothing reads them; leaving them would be just-in-case
code the project's style forbids. The compiler is the checklist: with the fields gone, the helpers stop compiling
and their removal is forced.

**Alternatives considered.**

- *Leave them for a future fork.* Rejected: the fork resolves the environment from the place registry, not from a
  scenario, so nothing will want them. Keeping dead code is the opposite of the project's rule.

## Decision: keep `ScenarioZone` as a named type

**Decision.** Do not collapse the three arrays directly into `ScenarioContents`. A scenario still holds a
`Map<Place, ScenarioZone>`.

**Rationale.** A scenario spans zones and its record is "a zone as a scenario holds it", keyed by the place it sits
at. The named type is the unit the (deferred) fork materialises per place, and it keeps the `capture`/materialise
seam symmetric (`ScenarioZone.capture` reads a live zone; a fork writes one back). It also keeps the record's shape
stated in one place.

**Alternatives considered.**

- *Inline a record of three arrays into `ScenarioContents`.* Rejected: it would put a fourth, near-identical
  "authored zone" shape into the codebase and lose the named seam, for no gain — the type is not heavier than the
  three arrays it groups.

## Decision: `capture` drops the active place

**Decision.** `ScenarioRegistry.capture` currently takes `(zones, active, cue)` and files `Active = active`. It
becomes `(zones, cue)`. `ScenarioZone.capture` keeps its signature (`zone -> ScenarioZone`) and only changes what it
reads.

**Rationale.** The active place is no longer part of the contents, so the parameter has no destination. Removing it
makes the capture's inputs exactly its outputs (zones + cue), which is what FR-007 ("pure and device-free… reads the
zones it is handed and nothing else") asks for.

**Alternatives considered.**

- *Keep the parameter and ignore it.* Rejected: an unused parameter is a lie about the contract; Fail Loudly's spirit
  is that the signature says what the function needs.

## Decision: every present zone stays in the record

**Decision.** A registration still captures **every present zone**, including a place with no actors, waypoints or
triggers (recorded as an empty `ScenarioZone`).

**Rationale.** Confirmed in the spec Clarifications: "Zones are part of a scenario". A scenario's zone map is its
footprint of places, which the deferred fork uses to know which places it spans; recording an empty place keeps that
explicit rather than making the fork infer it. It is also the smallest change — the capture's zone walk is untouched.

**Alternatives considered.**

- *Capture only zones that contain entities.* Rejected (spec Clarifications): it would erase a scenario's footprint
  and change the capture's zone walk for no narrative benefit.

## Decision: the player designation feature is untouched

**Decision.** Removing `Player` from the scenario record does not touch the per-zone player designation or the
Inspector's "Set as Player" (`specs/045`). That remains a zone's own setting and its own `SetPlayer` undo step.

**Rationale.** The feature removes the *scenario's copy* of the designation, not the designation. The user's phrasing
is "remove … from scenarios", and the spec's Key Entities states the concept stays per-zone.

## Decision: no persistence migration

**Decision.** No DTO or stored-value change is needed.

**Rationale.** The scenario library is not persisted (`docs/ROADMAP.md` phase 15); the persistence section of
`docs/ARCHITECTURE.md` names a `Project_Scenario` as future work only. Nothing on disk holds the broader shape.

## Decision: which docs change, and what does not

**Decision.** Update the five guiding docs that describe the current record — `docs/ARCHITECTURE.md` (the
"Scenario & Cue" section), `docs/EDITOR_DESIGN.md` (the Workspace's Scenario row), `docs/ZONE_MODEL_DECISION.md`
(the `ScenarioZone` shape, the four-value table and the convergence prose), `docs/ROADMAP.md` (phase 11's
deliverables), and `docs/PROJECT_STRUCTURE.md` (the `ScenarioZone.fs` line). Leave the prior spec artifacts under
`specs/070-scenario-library/` as the historical record of what was built then.

**Rationale.** The guiding `docs/` are the source of truth (`AGENTS.md`) and must not describe a shape that no
longer exists. Specs are a record of the increment that shipped and are not rewritten in place; this spec supersedes
070's shape.

**Alternatives considered.**

- *Rewrite `specs/070` to the trimmed shape.* Rejected: it would erase the record of what was built and when; the
  project's convention is to supersede with a new spec.

## Note for the deferred fork increment

A fork (ROADMAP phase 11's remainder) will **not** find an environment or light on a scenario zone. It resolves a
place's environment and light from the Zone Registry's template (or leaves an already-present zone's alone), and it
leaves the active place and the player designation as they are. This is recorded here so the fork spec starts from
the right model; it is out of scope for this feature (spec FR-011).
