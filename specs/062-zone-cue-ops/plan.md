# Implementation Plan: Zone Cue Ops

**Branch**: `062-zone-cue-ops` | **Date**: 2026-10-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/062-zone-cue-ops/spec.md`

## Summary

Give the cue the rest of the zone vocabulary: **`AddZone`**, **`RemoveZone`** and **`PreloadZone`** beside the
existing **`SetZone`**, each naming a `Place`. An add makes a declared place present and streams in its
registered template without entering it (`SetZone` enters); a remove makes a present, inactive place absent,
reusing the editor's release-and-unload path; a preload starts a present zone's environment loading
asynchronously so the later switch is instant. The cue guards each op so a redundant or contradictory one
performs nothing — including a cue's zone op on a place that is not present, which `specs/061` deferred here —
while the editor's own functions keep failing loudly. A preload is best-effort and never unloads the active
zone's environment. No new module: the ops go in `world/Cue.fs`, their effects and guards in
`world/CueEngine.fs`, a new presence primitive in `state/GameplayState.fs`, and a small template-population
helper in `world/Zone.fs`.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: `Place` (the closed zone vocabulary), `Assets.requestEnvironment` (the existing
async loader), `ZoneRegistry.tryTemplate` (config lookup), `Physics`/`Zone` (bodies), `Prime` — all present

**Storage**: N/A (nothing persisted; the cue and the runtime zone set stay in memory, roadmap phase 15)

**Testing**: xUnit, headless (`dotnet test`); no device

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: none new — each op runs once when the walk reaches it; the only extra work is the
per-op guard (`ContainsKey`) and, on an add, one zone's population

**Constraints**: zone ops name only declared places; the build is warning-free; the suite is green

**Scale/Scope**: 4 source files (`world/Cue.fs`, `world/CueEngine.fs`, `state/GameplayState.fs`,
`world/Zone.fs`), 1 editor file (`editor/CueTree.fs`); 3 test files; no `.fsproj` change

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. Three leaf ops, one presence primitive, one template helper; no new module, no new
  subsystem, no new state. A preload is a one-line reuse of the existing loader.
- **II. Fail Loudly** — PASS, with a recorded nuance. The *editor’s* functions
  (`GameplayState.addZone`/`switchZone`/`deleteZone`) keep raising on a precondition they are handed wrongly;
  only the **cue layer** turns a redundant or contradictory op into a no-op, because a cue is runtime data that
  may legitimately run twice (spec FR-007/FR-008, Q3 = B). The guard lives in `CueEngine`, so the loud
  behaviour is not diluted at its source.
- **III. Spec-First** — PASS. This feature is `specs/062-zone-cue-ops`.
- **IV. Data-Oriented Architecture** — PASS. The ops are union cases carrying a closed-union value; no new
  heap structure is introduced. `AddZone` populates a zone through the same `Zone.spawn*`/`loadEnvironmentFrom`
  paths everything else uses.
- **V. Minimise GC Pressure** — PASS. The ops are no-progress leaves (an idle cue still allocates nothing); the
  guards are dictionary lookups; a preload enqueues off the frame.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/062-zone-cue-ops/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── zone-cue-ops.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
world/Cue.fs              # add AddZone / RemoveZone / PreloadZone (leaves carrying a Place)
world/CueEngine.fs        # their effects and guards; SetZone's absent-place behaviour becomes a no-op
world/Zone.fs             # loadTemplate: populate a fresh zone from a registered template
state/GameplayState.fs    # presentZone: make an absent place present (streaming) without entering it
editor/CueTree.fs         # the four zone ops share one place chooser; kinds, labels, withPlace
test/CueTests.fs          # the ops' effects and no-ops, through CueEngine.tick
test/CuePanelTests.fs     # labels, kinds, withPlace for the new ops
test/GameplayStateTests.fs# presentZone makes a place present with its template, active place unchanged
```

**Structure Decision**: No module moves and no compile-order change. `Place` (`world/Place.fs`) already
precedes `Cue`; `Zone`, `Assets` and `ZoneRegistry` already precede `GameplayState` and `CueEngine`. The one
new dependency edge — `CueEngine → ZoneRegistry` for the template lookup — is exactly why the lookup belongs
at the cue layer: `GameplayState` takes the template as data and stays registry-free (research D4), preserving
`specs/061`'s vocabulary/config split. The presence primitive is a state-level operation (it touches the zone
map and `Zone`), and the template-population helper is a `Zone` operation (it spawns props and loads the
environment), so each lands in the module that owns the data it touches.

## Complexity Tracking

> No Constitution Check violations.
