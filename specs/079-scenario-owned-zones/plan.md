# Implementation Plan: Scenario-Owned Zones

**Branch**: `079-scenario-owned-zones` | **Date**: 2026-10-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/079-scenario-owned-zones/spec.md`

## Summary

Make the **scenario the primary unit of authoring**: it carries its zones' **staging** (environment, light, placed
props) as well as their narrative entities, so **forking a scenario loads the whole scene**. Two layers retire: the
**Zone Registry** (a separate `Place → staging recipe` store and its panel) and the **closed `Place` vocabulary**
(zones are addressed by a **string name**, as actors, waypoints and triggers are). A fork **ensures** each zone by
name (create if absent, reuse if present), **restages** it from the scenario, **appends** its entities, and **forks**
its cue. Two recent increments shrink the work: `specs/077` means commands already carry their zone, so a
multi-zone fork is an ordinary `Batch`; `specs/078` means the materialiser already exists — this feature changes its
zone handling from *create-absent-from-registry* to *ensure-and-restage-from-scenario*.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: `world/Place.fs` (**retired**), `world/ZoneRegistry.fs` + `editor/ZoneRegistryPanel.fs`
(**retired**), `world/ZoneTemplate.fs` (kept — becomes the scenario zone's staging), `world/ScenarioZone.fs` (grows
staging), `world/ScenarioRegistry.fs` (capture grows staging), `world/ScenarioFork.fs` (the ensure/restage/append
materialiser), `world/Cue.fs` + `world/CueEngine.fs` (retire `AddZone`/`PreloadZone`), `state/Command.fs` /
`state/GameplayState.fs` / `state/History.fs` (a zone id is a `string`; `ZonePresent` carries its staging),
`editor/Outliner.fs` (present zones by name; add by typed name), `editor/CueTree.fs` / `editor/CueEditor.fs` (a
typed zone name), `editor/Spawning.fs` (its `instantiateZone` retires; staging moves to the fork). No new dependency.

**Storage**: N/A — the scenario library is not persisted (`docs/ROADMAP.md` phase 15). A zone id is a name, not a
stored key.

**Testing**: xUnit, headless. Staging capture, the ensure/restage/append fork, the capacity refusal and the retired
surfaces are all pure over the gameplay state plus the asset manager, so both are asserted with no device; the cue
op drives `CueEngine.tick` as `CueTests` does.

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: a fork runs on a click or when a cue reaches the op — never per frame. The materialisation is
O(entities + zones forked); a restage releases/reloads an environment, which is a heavier op-time cost, and it is
skipped when the zone's staging already matches the scenario's.

**Constraints**: the `Command` type stays above `GameplayState`; `ZonePresent` carries its staging so a created zone
is replayable without the registry; a fork never switches the active zone or moves the player; capacity is logged and
no-ops (see Complexity Tracking); build warning-free; suite green; no device opened.

**Scale/Scope**: 3 files retired (`world/Place.fs`, `world/ZoneRegistry.fs`, `editor/ZoneRegistryPanel.fs`); ~18
touched (`world/ZoneTemplate.fs`, `world/ScenarioZone.fs`, `world/ScenarioRegistry.fs`, `world/ScenarioFork.fs`,
`world/Cue.fs`, `world/CueEngine.fs`, `state/Command.fs`, `state/GameplayState.fs`, `state/History.fs`,
`state/EditorState.fs`, `editor/Spawning.fs`, `editor/Outliner.fs`, `editor/CueTree.fs`, `editor/CueEditor.fs`,
`editor/Editor.fs`, `screen/GameplayScreen.fs`, plus `fsharp-game.fsproj`); ~12 test files; 4 docs
(`docs/ARCHITECTURE.md`, `docs/EDITOR_DESIGN.md`, `docs/ZONE_MODEL_DECISION.md`, `docs/ROADMAP.md`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. The feature **removes** two layers (the closed `Place` union, the Zone Registry and its
  panel) and adds no new store: staging reuses `ZoneTemplate` and rides the existing scenario. `instantiateZone` and
  the registry panel are deleted, not duplicated.
- **II. Fail Loudly** — **justified deviation** (unchanged from 078, see Complexity Tracking): a fork that lacks room
  **logs and no-ops** rather than raising, because the cue op runs mid-play. Every other unexpected state still
  raises (an unknown zone id in a command is a bug; a restage failure is a bug).
- **III. Spec-First** — PASS. This feature is `specs/079-scenario-owned-zones`.
- **IV. Data-Oriented Architecture** — PASS. A scenario's zones are authored records (staging + entity arrays); the
  fork walks them into the fixed slot maps. No object graph; no inheritance.
- **V. Minimise GC Pressure** — PASS. Editor/op-time only; the runtime fork is a runtime effect with no per-frame work.

The **Narrative Subsystem** principles do not apply (`world/`/`state/`/`editor/`, not `narrative/`).

## Project Structure

### Documentation (this feature)

```text
specs/079-scenario-owned-zones/
├── spec.md
├── plan.md                    # this file
├── research.md                # Phase 0 output
├── data-model.md              # Phase 1 output
├── quickstart.md              # Phase 1 output
├── contracts/
│   └── scenario-owned-zones.md
├── checklists/
│   └── requirements.md
└── tasks.md                   # /speckit-tasks
```

### Source Code (repository root)

```text
world/Place.fs              # RETIRED (the closed place union; a zone id is a string now)
world/ZoneRegistry.fs       # RETIRED (the staging store)
editor/ZoneRegistryPanel.fs # RETIRED (its panel)

world/ZoneTemplate.fs       # KEPT: it *is* a zone's staging (env asset + placement + parts + light + placed props)
world/ScenarioZone.fs       # CHANGED: + `Staging : ZoneTemplate option`; `capture` gathers staging too
world/ScenarioRegistry.fs   # CHANGED: zones keyed by `string`; capture fills staging via `Zone.captureTemplate`
world/ScenarioFork.fs       # CHANGED: ensure-by-name, restage from the scenario, append; `ZonePresent` carries staging
world/Cue.fs                # CHANGED: retire `AddZone`/`PreloadZone`; zone ops take `string`; keep `SetZone`/`RemoveZone`
world/CueEngine.fs          # CHANGED: drop the retired arms; the rest take `string`
state/Command.fs            # CHANGED: zone ids are `string`; `ZonePresent of Name : string * Staging : ZoneTemplate option`
state/GameplayState.fs      # CHANGED: `Zones : Dictionary<string, Zone>`, `ActiveZone : string`; a `startZone` const
state/History.fs            # CHANGED: the string-keyed arms; `ZonePresent` applies present-from-staging; `ZoneStage`
editor/Spawning.fs          # CHANGED: `instantiateZone` retired (its sole caller, the registry panel, is gone)
editor/Outliner.fs          # CHANGED: list present zones by name; "Add place" becomes a typed name
editor/CueTree.fs           # CHANGED: the zone chooser is a typed name; the retired kinds leave `kinds`
editor/CueEditor.fs         # CHANGED: as above
editor/Editor.fs            # CHANGED: stop drawing the retired Zone Registry panel
world/Scenario.fs           # CHANGED: doc comments only (the `Place`/`ZoneRegistry` analogy it cites has gone)

fsharp-game.fsproj          # - Place.fs, - ZoneRegistry.fs, - ZoneRegistryPanel.fs
test/*.fs                   # CHANGED: Place → string; registry tests retired; staging/fork coverage added
test/fsharp-game.Tests.fsproj

docs/ARCHITECTURE.md        # zones are named; a scenario owns its zones' staging; the registry is retired
docs/EDITOR_DESIGN.md       # D-023 (Zone Registry panel) retired; the module map; the phasing
docs/ZONE_MODEL_DECISION.md # the registry↔scenario convergence taken; ZoneTemplate is the staging
docs/ROADMAP.md             # phase 12 (Zone Registry) retired; phase 11 folds staging into the scenario
```

**Structure Decision**: The scenario zone grows staging (`Staging : ZoneTemplate option`), and the materialiser
(`world/ScenarioFork.fs`) becomes the **one** place a zone's staging is replaced — absorbing `Spawning.instantiateZone`
(whose only caller, the Zone Registry panel, retires). `world/Place.fs` and `world/ZoneRegistry.fs` are removed from the
compile list; a zone id is a `string` throughout, exactly as an actor name is. The `Command` type stays above
`GameplayState`; `ZonePresent` (a `state/Command.fs` case) carries its `ZoneTemplate option` so a created zone is
reversible and replayable without a registry.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Fail Loudly (II): a fork that lacks capacity **logs and does nothing** instead of raising | The cue-op path runs mid-play; a raise there would crash the game on authored content and a fixed-capacity slot map. The author-facing rule ("no op, not a crash") was chosen in `specs/078` and carries over. | Pre-check, so nothing is applied partially. Rejected: raise everywhere (crashes play); silence the editor too (an author would get no signal). Both entry points log the reason. |
| Fail Loudly (II): a restage of an **unloaded** environment is best-effort, not a raise | An absent environment asset streams in asynchronously (spec 050/053); the fork completes and the look resolves when the load lands, exactly as the cue's zone ops do today. | Raise on "not resident yet" — rejected: the environment may legitimately still be loading; the codebase's residency model is designed for this. |
