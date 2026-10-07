# Implementation Plan: Scenario Library

**Branch**: `070-scenario-library` | **Date**: 2026-10-07 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/070-scenario-library/spec.md`

## Summary

Give the editor a **Scenario panel** that files the current scene under a named scenario. A scenario is a
declared value of a closed union (`Scenario`, starting with one case, `Intro`) and the store keyed by it holds
the captured **scenario contents**: every present zone as a **ref-free *scenario zone*** (`world/ScenarioZone.fs`
— its environment as authored, and arrays of authored actor/prop/waypoint/trigger states, with waypoint links and
the player designation by index), the active place, and the screen's authored cue. The panel offers **Register**
(capture the current state under the scenario) and **Clear** (empty it); both are single steps on the one undo
history, because the capture is immutable data and the step carries a before/after pair. Registering only
*captures* — it never runs, forks or alters the scene, and it adds no cue vocabulary. Forking a scenario into the
running cue is a later increment.

The shape follows the project's established **vocabulary + sparse store** split (`Place` + `ZoneRegistry`,
`Character` + `Casting`) and the **in-place editor panel** pattern (`CastPanel`, `ZoneRegistryPanel`), with the
step carried as a state-level `Command` like `Advent` (`specs/055`) rather than a direct write.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: `world/Cue.fs` (the cue — the captured root), `world/ScenarioZone.fs` (the ref-free
authored zone the capture produces), `world/Zone.fs` (the live `Zone` it reads), `world/Place.fs` (the place key),
the `world/ZoneRegistry.fs` + `cast/Roster.fs` shapes
being mirrored, `state/GameplayState.fs` (where the library lives), `state/Command.fs` / `state/History.fs` (the
step), `editor/Editor.fs` (panel submission + `panelNames`), `render/ImGui.fs` (the panel's widgets), and
`native/cimgui_sdl3.cpp` (dock layout). All present; no new dependency.

**Storage**: N/A — persistence (`project.json`) is phase 15; the declared set stays in code.

**Testing**: xUnit, headless (`dotnet test`). The capture is pure over a `Dictionary<Place, Zone.Zone>` + place +
cue, so it is asserted without a device (a zone's actors/waypoints are built by hand, as `PlayerTests` and
`GameplayStateTests` already do); the command round-trip is driven through `History` with the existing headless
fixtures.

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: editor-only cold path — register/clear runs on a click, never per frame; the play loop is
untouched.

**Constraints**: no cue vocabulary and no run/fork (spec FR-010); the build is warning-free; the suite is green;
no device opened.

**Scale/Scope**: 4 new source files (`world/Scenario.fs`, `world/ScenarioZone.fs`, `world/ScenarioRegistry.fs`,
`editor/ScenarioPanel.fs`);
5 touched (`state/GameplayState.fs`, `state/Command.fs`, `state/History.fs`, `editor/Editor.fs`,
`native/cimgui_sdl3.cpp`); 1 new test file (`test/ScenarioTests.fs`); `.fsproj` inserts 4 lines.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS (with one deliberate extra type). Three small world modules (a vocabulary union, a
  ref-free zone, a sparse store), one panel, one command case. `world/ScenarioZone.fs` is a **new** type, not a
  reuse of `ZoneContents`: the delete's snapshot carries entity refs and the live environment, which a scenario
  must not (spec FR-012, `docs/ZONE_MODEL_DECISION.md`), so sharing it would push identity onto scenarios. The
  panel is the third in-place surface rather than a new editor.
- **II. Fail Loudly** — PASS. The zone capture de-refs and raises when a waypoint's link names no live waypoint
  (authoring state that cannot occur, a bug not a value to render); an absent scenario entry is an expected
  `None`; and an out-of-place state-level step still `invalidOp`s in the zone-level runner.
- **III. Spec-First** — PASS. This feature is `specs/070-scenario-library`.
- **IV. Data-Oriented Architecture** — PASS. A closed union and records; the captured zones are an immutable
  `Map` (a value an undo entry can hold safely), not an object graph.
- **V. Minimise GC Pressure** — PASS. Editor-only cold path; the captured `Map`/cue are reference graphs built
  on a click, off the play loop.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/070-scenario-library/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── scenario-registry.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
world/Scenario.fs           # NEW: the vocabulary — Scenario (Intro), scenarioName, all
world/ScenarioZone.fs       # NEW: the ref-free authored zone — ScenarioEnvironment/-Waypoint/-Zone + capture
world/ScenarioRegistry.fs   # NEW: ScenarioContents (Map<Place, ScenarioZone>), Registry, create, tryContents,
                            #   setContents, capture
state/GameplayState.fs      # + Scenarios : ScenarioRegistry.Registry (beside ZoneRegistry); create () seeds it
state/Command.fs            # + SetScenario case; it joins the state-level invalidOp group (with SetCue/Advent)
state/History.fs            # + SetScenario in apply/undo (writes state.Scenarios via setContents)
editor/ScenarioPanel.fs     # NEW: one row per scenario — Register + Clear and the cast tags beside it, each one
                            #   History.commit
editor/Editor.fs            # + "Scenario" in panelNames; draw ScenarioPanel with the left-docked panels
native/cimgui_sdl3.cpp      # + DockBuilderDockWindow("Scenario", left)
fsharp-game.fsproj          # + world/Scenario.fs (before world/Cue.fs), world/ScenarioZone.fs (after world/Zone.fs),
                            #   world/ScenarioRegistry.fs (after world/ScenarioZone.fs), editor/ScenarioPanel.fs
test/ScenarioTests.fs       # NEW: the capture, and the register/clear/undo/redo round-trip
```

Docs refreshed in the same change (the roadmap's "Next step" is stale and phase 11's deliverable text predates
this panel):

```text
docs/EDITOR_DESIGN.md       # + the Scenario panel in the workspace table and the sub-editor list (left dock)
docs/PROJECT_STRUCTURE.md   # drop the (planned) markers; name world/ScenarioRegistry.fs and editor/ScenarioPanel.fs
docs/ARCHITECTURE.md        # Scenario_Library moves off Shared_State; the "Scenario & Cue" section
docs/ROADMAP.md             # phase 11 reshaped to this increment (register + panel; fork deferred); refresh "Next step"
docs/ZONE_MODEL_DECISION.md # the note; records why a scenario has its own ref-free zone type (Phase 6)
```

**Structure Decision**: Two files mirror the `Place` (vocabulary) / `ZoneRegistry` (store) split exactly, and
for the same reason: the vocabulary `world/Scenario.fs` is pure and dependency-free so it sits **before**
`world/Cue.fs` — where a later `ForkScenario` op will name a scenario, as `SetZone` names a `Place` — while
`world/ScenarioRegistry.fs` needs the scenario zone and so sits **after** `world/Zone.fs`. `world/ScenarioZone.fs`
holds the ref-free authored zone that `ScenarioContents` files; it sits after `world/Zone.fs` (its `capture`
reads a live `Zone`) and before `world/ScenarioRegistry.fs`. It is deliberately **its own type**, not
`ZoneContents` — the delete's snapshot keeps zone-scoped refs and the live environment, which a scenario must not
(`docs/ZONE_MODEL_DECISION.md`). The panel takes `SharedState` + `byref<GameplayState>` like `ZoneRegistryPanel`,
and writes through `History.commit` so each control is one step on the one history. No module moves.

## Complexity Tracking

> No Constitution Check violations.
