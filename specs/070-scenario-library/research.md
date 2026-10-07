# Phase 0 — Research: Scenario Library

The spec left no `NEEDS CLARIFICATION` markers (three questions were resolved in `/speckit-clarify`). This
document records the decisions the plan rests on, each with the alternative it rejected.

## D1 · Where the scenario library lives

**Decision**: On `GameplayState`, beside `Zones` and `ZoneRegistry`, as
`Scenarios : ScenarioRegistry.Registry`.

**Rationale**: The readership test (spec 070 FR-007, and `specs/069-state-ownership-boundary`'s own rule) —
nothing outside the gameplay screen and its editor reads scenarios. `ZoneRegistry` already moved here for the
same reason and sits beside it.

**Alternatives considered**: `SharedState` (the pre-069 arrangement, and what `docs/ARCHITECTURE.md`'s
`Shared_State → Scenario_Library` diagram still shows). Rejected: no other screen reads the scenario library, so
it would be cross-screen state by convention only. The diagram is corrected in this change.

## D2 · How a scenario is identified

**Decision**: A closed union `Scenario` (`world/Scenario.fs`), opened with the single case `Intro`, enumerated
via `Microsoft.FSharp.Reflection` as `Place.all`/`Roster.characters` are.

**Rationale**: Spec FR-003 — a scenario is a declared value, not a string, so a scenario that is not declared
cannot be registered or named, and matching over the set is exhaustive. This is the same shape `Place` gives a
zone and `Roster.Character` gives a cast member.

**Alternatives considered**: a `Dictionary<string, …>` keyed by a free-text name. Rejected: misspellable, and
the project has deliberately fixed identity to closed unions for places, characters and advent flags. A single
file holding both the vocabulary and the store was also considered; rejected because the store needs
`ScenarioZone` (defined in `world/ScenarioZone.fs`), which would drag the vocabulary's compile point after
`Zone.fs`
and block a later cue op from naming a scenario — the reason `Place` is its own file too.

## D3 · What a scenario's contents are

**Decision**: `ScenarioContents = { Zones : Map<Place, ScenarioZone>; Active : Place; Cue : Cue.Cue }`, where each
zone is the ref-free value `ScenarioZone.capture` produces.

**Rationale**: Spec FR-002 (zones, active place, cue) and FR-012 (a captured zone is ref-free). The active place
is a single value, not a whole-zone concept, so it is a field of its own rather than folded into the map.

**Alternatives considered**: **reuse `ZoneContents` (the delete's snapshot) — chosen first, then reversed** by
070's Phase 6. It was the only existing type with the breadth a scenario needs (actors/waypoints/triggers, which
`ZoneTemplate` lacks), but it carries exactly what a scenario must not: entity refs and the live `Environment`
(epoch and parts). Those are dead weight for a fork — which instantiates into fresh zones and re-mints every
index — and carrying them invites the wrong implementation; the two consumers want different shapes
(`docs/ZONE_MODEL_DECISION.md`, D8). A mutable `Dictionary<Place, …>` was also rejected: the contents are an undo
*snapshot*, so a value a later registration cannot mutate is required — an immutable `Map` gives that for free,
as `Set<Advent>` does for the flag step.

## D4 · Where the capture function lives

**Decision**: `ScenarioRegistry.capture (zones: Dictionary<Place, Zone.Zone>) (active: Place) (cue: Cue.Cue) :
ScenarioContents`, in `world/ScenarioRegistry.fs`.

**Rationale**: Spec FR-009 — the capture must be pure and device-free so it can be asserted headlessly. Taking
the three pieces rather than the `GameplayState` keeps it in `world/` (which cannot see `state/`) and mirrors
`ZoneTemplate.ofEnvironment` / `Zone.captureTemplate`, which likewise separate the record from the live zone.

**Alternatives considered**: a method on `GameplayState` (rejected: would put domain logic in `state/`, and
`state/` already only *composes* `world/`); a helper inside the panel (rejected: not reachable by a headless
test).

## D5 · The undo step

**Decision**: A new state-level `Command` case
`SetScenario of Name : Scenario * Previous : ScenarioContents option * Next : ScenarioContents option`, handled in
`state/History.fs` (`setContents name next/previous`), and refused by the zone-level `Command.apply`/`undo` like
`SetCue`/`Advent`.

**Rationale**: Spec FR-004/FR-005/FR-011 — a registration, a re-registration and a clear are all one step with
total undo. A before/after option pair makes all three the same shape and makes undo of a first registration
correctly return the scenario to *unregistered*. It is not a zone edit (a scenario spans zones), so it belongs on
the state-level runner beside `SetCue` (`specs/049`) and `Advent` (`specs/055`), not on a zone.

**Alternatives considered**: a direct write to `state.Scenarios` as the Cast/Zone Registry panels do
(rejected: the spec makes registration undoable, and the Zone Registry's non-undoable write is precisely the
thing being deviated from here); two cases, one for register and one for clear (rejected: the `option` pair
already expresses both, so a second case is redundant).

## D6 · The panel and its controls

**Decision**: `editor/ScenarioPanel.fs`, a flat list — one row per scenario, showing whether it holds contents,
with **Register** (always) and **Clear** (only when it holds contents). Both call `History.commit`.

**Rationale**: Spec FR-001/FR-006/FR-011. A scenario declares no variants, so the row is flat where the Zone
Registry's tree is nested. Clear closes the otherwise-unreachable "unregistered again" state (the clarify
answer), and the `option` payload makes it the same step shape as Register. The dock is the **left** column,
per `docs/EDITOR_DESIGN.md`'s workspace table, which already places Cast and the Zone Registry there.

**Alternatives considered**: the roadmap's `editor/ScenarioEditor.fs` with Cue/Scene Inspector tabs (rejected as
this increment's shape: that is the fork/run surface, which is deferred); Register-only with no Clear (rejected
in clarify).

## D7 · Naming against the narrative engine

**Decision**: The game's module is `Scenario` and its type is `Scenario`; nothing here `open`s `Narrative`, whose
`Harness.Scenario` (a headless event-script) is a different concept.

**Rationale**: Spec assumption "A name shared with the narrative engine". The modules are flat-namespaced and
distinct, so the two coexist; only an `open Narrative` in a scenario file would be a hazard, and none is needed.
The type is referenced as `Scenario.Scenario` / `ScenarioRegistry.ScenarioContents` where `open`s would be
ambiguous.

## D8 · A scenario zone is its own type, not `ZoneContents`

**Decision**: `world/ScenarioZone.fs` — a ref-free authored zone: `ScenarioEnvironment` (asset, base, orientation,
a parts array), `ScenarioWaypoint` (`Base` with `Next : int option`), and `ScenarioZone` (arrays of `ActorState`
and `TriggerState`, the light, and `Player : int option`). Placed props are deliberately omitted — they are
environment decoration, not narrative, and a scenario-prop separation is a later question.

**Rationale**: Spec FR-012. `ZoneContents` has one purpose — reverse a zone *delete* exactly — so it keeps the
origin zone's `EntityRef`s and its live `Environment`. A scenario needs neither: it is authored state a fork
materialises in fresh zones. The four ref carriers (`(ref, state)` pairs, the environment's epoch,
`Waypoint.Next`, `Player`) are each re-expressed, so nothing in a stored scenario names a slot in the zone it came
from. Arrays (not lists) are the shape a bulk load walks, and the de-ref is a pure function, so it is asserted
headlessly. `docs/ZONE_MODEL_DECISION.md` records why the delete's snapshot is not the scenario's shape.

**Alternatives considered**: reuse `ZoneContents` (reversed — see D3); convert only its lists to arrays and keep
the refs (rejected: the refs are the actual problem, not list-node overhead). The inverse, `occupy`, is
deliberately not written — loading a scenario into fresh zones (the fork) is the later increment and needs the
renderer/physics paths a headless test cannot exercise.
