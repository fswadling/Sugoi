# Quickstart: Scenario-Owned Zones

**Feature**: `specs/079-scenario-owned-zones` | **Date**: 2026-10-11

How to build this feature and prove it works end to end. Everything runs **headlessly** — no window, no GPU, no
device — as the suite already does (`SC-006`). Contracts and record shapes are in
[`contracts/scenario-owned-zones.md`](./contracts/scenario-owned-zones.md) and [`data-model.md`](./data-model.md);
this file only says how to run and what to expect.

## Prerequisites

- .NET SDK 10 (`net10.0`), as the project targets.
- Repo root: `/home/fswadling/Repos/fsharp-game`.

## Build (must be warning-free)

```sh
dotnet build fsharp-game.fsproj
```

Expected: **0 warnings, 0 errors**. `world/Place.fs`, `world/ZoneRegistry.fs` and `editor/ZoneRegistryPanel.fs` must
be **removed** from `fsharp-game.fsproj`; the remaining files keep their order. `GameplayState.stageZone` lives in
`state/GameplayState.fs` (after `world/Zone.fs`/`ZoneTemplate.fs`); `Command.ZoneStage`/`ZonePresent` carry a
`ZoneStaging`, so `world/ZoneTemplate.fs` stays above `state/Command.fs`.

## Test (the suite must be green)

```sh
dotnet test test/fsharp-game.Tests.fsproj
```

Expected: the suite passes with the `Place` → `string` migration applied across the existing tests and the new
coverage below. No test opens a device. `test/ZoneRegistryTests.fs` is retired with its subject; `SpawningTests`'
`instantiateZone` cases move to `stageZone`/the fork.

## What the new/updated coverage asserts

### US1 — a scenario is the unit of authoring; forking loads it (`SC-001`, `FR-001/002/004`)

- **Staging round-trip.** Build a zone with a distinct environment asset and light; `ScenarioZone.capture` it; register;
  fork it into a world without that zone; assert the zone is present carrying that environment (`zone.Environment`
  with the asset id) and light.
- **Multi-zone.** Register a scenario over two zones with different environments; fork; assert every zone comes up
  with its own staging and entities, and the scenario's cue runs beside the rest.
- **Cue op vs editor.** The `ForkScenario` op materialises during `CueEngine.tick`; `ScenarioPanel.fork` does the same
  as one undoable step.

### US2 — fork into an existing world restages it (`SC-002`, `FR-003/004/005`)

- **Reuse, not duplicate.** Present a zone with environment A; fork a scenario naming it with environment B and its own
  entities; assert `state.Zones.Count` did not grow, the zone now carries B's environment/light/props, and its entities
  were appended.
- **Non-idempotence.** Fork twice; assert the staging matches the scenario both times and its entities are duplicated.
- **Active-zone restage.** Restage the active zone; assert the active place, camera and selection are unchanged.
- **Skip-if-equal.** Re-fork when the zone already has the scenario's staging; assert no environment is released/reloaded
  (e.g. the epoch/template is unchanged) and only entities are appended.

### US3 — zones are named; the registry retires (`SC-003`, `FR-010/011/012`)

- **Undeclared id.** Fork a scenario naming a zone id no code declares; assert it is created with the scenario's staging.
- **Retired surfaces.** Assert the Zone Registry and the closed `Place` vocabulary are gone (no `Place` type, no
  `ZoneRegistry`, no panel), and that `AddZone`/`PreloadZone` no longer exist in the cue vocabulary while
  `SetZone`/`RemoveZone` do.

### Supporting-file coverage

- **`state/History.fs`**: `ZonePresent` apply/undo round-trips a created-and-staged zone; `ZoneStage` apply/undo
  round-trips a restage on a **present, inactive** zone and leaves its bodies detached; the zone-level runner
  `invalidOp`s both.
- **`state/GameplayState.fs`**: `stageZone` replaces environment/light/props; detaches when the zone is not active;
  leaves `Zone.Player` alone.
- **Capacity** (`FR-009`, `SC-005`): a target with room for the entities but **not** the props refuses the whole fork;
  `materialise` returns `None`, logs, and changes nothing anywhere (no zone created/restaged, no entity/prop added).
- **Editor** (`FR-008`, `SC-004`): the editor fork is one `Batch` on the history that round-trips exactly (created
  zones removed, restaged zones restored, entities de-appended, cue restored); `pruneCueSteps` still drops the nested
  cue step.

## Manual editor check (optional, opens a window)

1. Start the app; in the **Asset Browser** pick an environment for the active zone, then tune the light in the
   **Inspector**; place a couple of actors and a waypoint; **Register** the scene as a scenario.
2. Delete the zone (or start a fresh run) so the scene does not hold it; press **Fork** on the scenario's row.
3. The zone comes up with that environment, light and its entities; the cue runs; `Ctrl+Z` removes exactly what the
   fork did; `Ctrl+Y` brings it back.
4. Confirm there is **no Zone Registry panel** and that the Outliner's add-place is a typed name.

## Docs to refresh (`FR-015`)

- `docs/ARCHITECTURE.md` — zones are named; a scenario owns its zones' staging; the registry is retired.
- `docs/EDITOR_DESIGN.md` — D-023's panel retired; the module map; the phasing.
- `docs/ZONE_MODEL_DECISION.md` — the `ZoneTemplate`↔scenario convergence is taken; `ZoneTemplate` is the staging.
- `docs/ROADMAP.md` — phase 12 (the registry) retired; phase 11 folds staging into the scenario.

## Expected outcome summary

| Check | Expected |
|-------|----------|
| `dotnet build fsharp-game.fsproj` | 0 warnings, 0 errors |
| `dotnet test test/fsharp-game.Tests.fsproj` | all green |
| Forking a scenario | zones ensured + restaged, entities appended, cue forked |
| A present zone named by a fork | restaged, not duplicated |
| Active place / camera / selection | unchanged |
| A refused fork | logs and changes nothing (`FR-009`) |
