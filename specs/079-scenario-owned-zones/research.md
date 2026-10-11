# Research: Scenario-Owned Zones

**Feature**: `specs/079-scenario-owned-zones` | **Date**: 2026-10-11

No open `NEEDS CLARIFICATION` items — the spec settled the one decision (retire the staging-dependent zone cue ops).
The notes below record the design decisions this plan rests on so `/speckit-tasks` and implementation do not
re-derive them.

## Decision: a zone id is a `string`; the closed `Place` vocabulary retires

**Decision.** `world/Place.fs` is deleted. Everywhere a zone is named — `GameplayState.Zones : Dictionary<string,
Zone>`, `ActiveZone : string`, the first field of every content command, the cue ops, `ScenarioContents.Zones` — the
id is a plain `string`. The start zone becomes a module constant (`GameplayState.startZone = "start"`).

**Rationale.** A zone is named as an actor, waypoint or trigger is (`specs/072`/`075`), so the model is uniform and a
zone the code does not declare can exist. The closed union made a zone unrepresentable unless declared, which is the
layer this feature removes. A `string` id is exactly the trade 072 accepted for actors.

**Alternatives considered.**

- *A newtype `ZoneId = ZoneId of string`.* Rejected: F# type abbreviations are transparent and a single-case
  wrapper adds ceremony without the safety the codebase does not pay for actor/waypoint/trigger names either.
- *Keep `Place`, retire only the registry.* Rejected by the spec: zones are named, and the union's product
  enumeration (`Place.all`) is what the panels walk — with names, the "known zones" set is the present zones.

## Decision: `ZoneTemplate` **is** a zone's staging; the scenario zone carries it

**Decision.** `ZoneTemplate` (environment asset + base + orientation + parts, light, placed props) is kept and is the
one authored-zone staging value. `ScenarioZone` gains a field, `Staging : ZoneTemplate option` (`None` = the zone has
no environment — an empty zone with the default light and no props). `ScenarioZone.capture` becomes
`{ Staging = Zone.captureTemplate zone; Actors; Waypoints; Triggers }`.

**Rationale.** `specs/076` trimmed a scenario to narrative entities and left staging to the registry; with the
registry gone, the scenario is the home for staging. Reusing `ZoneTemplate` introduces no fourth shape and takes the
`ZoneTemplate` ↔ `ZoneContents` convergence `docs/ZONE_MODEL_DECISION.md` predicted (its option 3: promote one
authored-zone recipe). `Zone.captureTemplate` already produces exactly this value from a live zone.

**Alternatives considered.**

- *Inline the staging fields into `ScenarioZone`.* Rejected: it re-derives `ZoneTemplate` and gives its
  `ofEnvironment`/`loadEnvironmentFrom` bridge a second caller to keep in step.
- *Represent an environment-less zone's light/props.* Rejected: today's registry cannot either (`captureTemplate`
  is `None` without an environment); a zone with props/light but no environment is out of scope and rare.

## Decision: a fork **ensures**, **restages**, **appends**

**Decision.** `ScenarioFork.materialise` walks `contents.Zones`:
- **absent** → `GameplayState.presentZone shared &state id staging` (create empty, then load the template — which
  streams the environment and detaches the bodies when the zone is not active), recording `ZonePresent(id, staging)`;
- **present** → if the zone's current staging (`Zone.captureTemplate zone`) differs from the scenario's,
  `GameplayState.stageZone shared &state id staging` replaces it, recording `ZoneStage(id, previous, staging)`; if it
  already matches, the staging step is **skipped**;
- then append the zone's actors, waypoints (chains re-linked) and triggers, recording the usual create commands.

**Rationale.** A zone's name is its identity, so "add a zone" can only mean *ensure by name* — a `Dictionary` cannot
hold two zones with one name. This is already the cue layer's rule (the retired `AddZone` was a no-op on a present
place). Entities append because an entity's name is a label. Skipping an already-matching restage avoids releasing and
reloading an identical environment on a re-fork.

**Alternatives considered.**

- *Duplicate a zone as entities are duplicated.* Rejected: structurally impossible (the map is keyed by name) and
  meaningless.
- *Always restage, even when equal.* Rejected: it frees and rebuilds GPU/physics for no change.
- *Leave a present zone's staging alone (078's rule).* Rejected by the spec: a scenario is a whole scene and its
  look wins (FR-004).

## Decision: a new state-level command, `ZoneStage`

**Decision.** Add `ZoneStage of Name : string * Previous : ZoneTemplate option * Next : ZoneTemplate option` to
`state/Command.fs`. It is **state-level** (`placeOf` answers `None`). `History.apply` → `GameplayState.stageZone …
Next`; `History.undo` → `… Previous`.

**Rationale.** Restaging an arbitrary (possibly **inactive**) zone must keep the codebase's body invariant on **both**
apply and redo, and it must reverse symmetrically. Only the state-level runner knows `state.ActiveZone`, so it is the
one place that can detach an inactive zone after the swap. Encoding the restage as a `Batch` of the existing
`SetEnvironment` + `SetLight` + `PropDelete`/`PropCreate` (what `Spawning.instantiateZone` did) would re-add bodies on
replay with no detach — a correctness bug for an inactive zone. One command, symmetric, detach-aware.

**Alternatives considered.**

- *A `Batch` of the existing commands.* Rejected above: replay cannot detach an inactive zone's bodies.
- *Make `SetEnvironment` detach.* Rejected: it is a content command used by the active-zone Inspector (and the Asset
  Browser); the active place is not known to the zone-level runner.

## Decision: `ZonePresent` carries its staging

**Decision.** `ZonePresent of Name : string * Staging : ZoneTemplate option`. `apply` → `GameplayState.presentZone …
Staging`; `undo` → `GameplayState.deleteZone`.

**Rationale.** `specs/078` introduced `ZonePresent` resolving its staging from the registry. With the registry gone,
the staging must ride the command so a **created** zone is replayable (redo present + stage) without any store. The
created zone's staging is expressed by this one command; no separate `PropCreate`/`SetEnvironment` commands are
recorded for it (the template load places its props).

**Alternatives considered.**

- *A bare `ZonePresent Name` plus a separate stage step.* Rejected: two commands for one act, and the create must be
  staged before its entities can occupy slots.

## Decision: `instantiateZone` retires; `GameplayState.stageZone` is the one restager

**Decision.** `editor/Spawning.fs`'s `instantiateZone` is deleted (its only caller, the Zone Registry panel, retires).
Its body — release/load the environment, replace the props, set the light, record the reversal — becomes
`GameplayState.stageZone (shared) (&state) (name) (staging: ZoneTemplate option)`, detaching physics when the zone is
not active.

**Rationale.** One place replaces a zone's staging, used by the fork and by `History`'s `ZoneStage`. Deleting its
duplicate keeps the "mutate and record" logic single-sourced (the project's rule-of-three concern is met: the only
other caller is gone).

**Alternatives considered.**

- *Keep `instantiateZone` for the panel.* Rejected: the panel is retired by this feature.
- *Put `stageZone` in `ScenarioFork`.* Rejected: `History` must call it (for `ZoneStage`), and a `state/` module is
  the natural home beside `presentZone`/`populateZone`; `ScenarioFork` (compiled after `GameplayState`) can still call it.

## Decision: the staging-dependent cue ops retire

**Decision.** `AddZone` and `PreloadZone` leave `world/Cue.fs`. `SetZone` (switch to a present zone) and `RemoveZone`
(make a present zone absent) stay, and every zone op takes a `string`. `ForkScenario` is unchanged in shape.

**Rationale.** `AddZone`/`PreloadZone` existed to bring a zone in *with its staging* outside a scenario; with the
registry gone they have no staging source, and a fork now supplies exactly that. `SetZone`/`RemoveZone` need no
staging and are unaffected. (Chosen in the spec's Clarifications.)

**Alternatives considered.**

- *Make `AddZone` name a scenario's zone.* Rejected by the spec's clarification choice C: the fork is the way to
  bring a staged zone in.

## Decision: capacity is all-or-nothing, and now includes props

**Decision.** The pre-check (078's) now sums the entities **and** the props to be added: for each target zone, the
scenario's prop count must fit the props map (it replaces all props, so it is `newProps ≤ capacity`), and the appended
entities must fit their maps (`SlotMap.Capacity - Count`). If any target lacks room, log and return `None` — no
change at all.

**Rationale.** Staging brings props, which 076 left out; the all-or-nothing rule (078) is unaffected.

## Decision: the editor's zone input is a typed name; the registry panel retires

**Decision.** `editor/Outliner.fs` lists the present zones (the `Zones` keys) with their residency marker, and its
"Add place" control becomes a typed **name** field committing `ZoneAdd (active, name)` (guarded: non-empty, not
already present). `editor/CueTree.fs`'s zone chooser becomes a **typed** name field (as it was before `specs/061`),
because the control deliberately names no state and so cannot offer a live-zone list. `editor/ZoneRegistryPanel.fs`
is deleted and `editor/Editor.fs` stops drawing it.

**Rationale.** With no closed vocabulary there is nothing to enumerate; a name is typed, exactly as an actor's name
is. The active zone's environment is still set in the Asset Browser (`SetEnvironment`, unchanged), and its light in
the Inspector — so retiring the panel loses no authoring affordance.

**Alternatives considered.**

- *A combo of present zone names for the cue place.* Rejected: the shared control (`CueTree`) takes no
  `GameplayState`, which is what lets a trigger's and a scenario's cue both draw it.

## Decision: docs

**Decision.** Update `docs/ARCHITECTURE.md` (zones are named; a scenario owns its zones' staging; the registry
retires), `docs/EDITOR_DESIGN.md` (D-023's panel retires; the module map and phasing), `docs/ZONE_MODEL_DECISION.md`
(the convergence is taken; `ZoneTemplate` is the staging), `docs/ROADMAP.md` (phase 12 retires; phase 11 folds
staging into the scenario). Leave `specs/061`, `specs/062`, `specs/076` as historical (superseded).

## Note: out of scope

Persistence of the scenario library (ROADMAP phase 15) — a zone id would key the persisted zones there, but no
persistence lands here. Odin's `InlineScenario`/`ClearScenario` and the camera-follow/interaction toggles remain
deferred (unchanged). No new cue families.
