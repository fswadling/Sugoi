# Quickstart: Fork a Scenario into the Scene

**Feature**: `specs/078-fork-scenario` | **Date**: 2026-10-11

How to build this feature and prove it works end to end. Everything here runs **headlessly** — no window, no
GPU, no device — as the suite already does (`SC-006`). Contracts and record shapes are in
[`contracts/scenario-fork.md`](./contracts/scenario-fork.md) and [`data-model.md`](./data-model.md); this file
only says how to run and what to expect.

## Prerequisites

- .NET SDK 10 (`net10.0`), as the project targets.
- Repo root: `/home/fswadling/Repos/fsharp-game`.

## Build (must be warning-free)

```sh
dotnet build fsharp-game.fsproj
```

Expected: **0 warnings, 0 errors**. The new file `world/ScenarioFork.fs` must be listed in
`fsharp-game.fsproj` **after `state/GameplayState.fs` and before `world/CueEngine.fs`** — both `CueEngine` (the
op) and `editor/ScenarioPanel.fs` (the control) call it, and it reads `GameplayState`, `Zone`, `ZoneRegistry`,
`Command`, `ScenarioRegistry` and `Cue` from earlier files.

## Test (the suite must be green)

```sh
dotnet test test/fsharp-game.Tests.fsproj
```

Expected: the 531 existing tests still pass, plus the new `test/ScenarioForkTests.fs` cases (listed in
`tasks.md`). No test opens a device.

## What the new test file asserts

`test/ScenarioForkTests.fs` reuses the existing headless fixtures — `GameplayState.create ...` (one
`Place.start` zone), `TestSupport.manager` with a fabricated actor asset (as `ActorNameTests` does), and
`ScenarioPanel.commit`/`capture` to register a scenario. It covers, by user story:

### US1 — the cue op (`SC-001`, `FR-001/003/006/009`)

- **Reached in a sequence.** Build a cue `Sequence(ForkScenario Intro, …)` with `Intro` registered; drive
  `CueEngine.tick shared &state now` (as `CueTests` does). Assert the scenario's entities appear in their
  places **and** `state.Cue` becomes a `Fork`/`Parallel` running the scenario's cue beside the rest.
- **Absent place created, active untouched.** Register a scenario naming `Place.Island` while only `start` is
  present; tick the op; assert `Island` is now in `state.Zones`, its entities added, and `state.ActiveZone`
  is still `Place.start`.
- **Unregistered → diagnostic no-op.** `Sequence(ForkScenario Intro, Print …)` with nothing registered;
  assert nothing is added and the rest of the cue still runs (the `noOp` eprintln path).
- **`Fin` scenario cue → entities added, running cue unchanged.**
- **Runtime effect, not a step.** Assert `state.Editor.Undo.Applied` is unchanged by the op (`FR-006`).

### US2 — the editor fork (`SC-004`, `FR-007/008/011`)

- **One step.** Register; call `ScenarioPanel.fork shared &state Intro`; assert the entities appear, the
  screen's cue forked, and `List.length state.Editor.Undo.Applied = 1` (plus whatever preceded it).
- **Undo/redo round-trip.** Snapshot zone counts, the cue, active place, camera and selection; undo; assert the
  added entities and any created place are gone and the cue is exactly what it was (`FR-004`). Redo; assert all
  return exactly.
- **Disabled when unregistered → `fork` is a no-op** (the button's disabled state is checked by reading
  `tryContents`; the write path itself does nothing).

### US3 — footprint & edges (`SC-002/003/005`, `FR-002/005/010`)

- **Create-absent from the registry template.** With `Island` registered in `ZoneRegistry`, fork a scenario
  naming it; assert the place is present with the template's environment and light; with no template
  registered, assert it is present and empty but for the scenario's entities.
- **Append-present.** Fork a scenario naming `start` (already present); assert only entities are appended and
  `start`'s own environment/light/player are untouched.
- **Non-idempotence.** Fork twice; assert two copies of every entity.
- **Waypoint chain reproduced.** A scenario zone with a chained pair forks to two waypoints whose `Next`
  links the newly added waypoint (`Zone.waypointNext` resolves to the fork's own ref, not the caster's).
- **Capacity no-op.** Fill a target zone to the relevant capacity, then fork; assert `materialise` returns
  `None`, a reason is logged, and **nothing at all** changed — no place created, no entity added, no cue forked
  (`FR-010`).
- **`SlotMap.Capacity`** returns the constructed size; free = `Capacity - Count`.

### Supporting-file coverage

- **`state/History.fs`**: `ZonePresent` apply/undo round-trips a place's presence (present-from-template and
  present-empty); the zone-level runner `invalidOp`s it; `pruneCueSteps` drops the `SetCue` nested in a fork's
  `Batch` while keeping its `ZonePresent`/`*Create` commands (`FR-012`).
- **`editor/CueTree.fs` / `CueEditor.fs`**: `label`/`kindName`/`children`/`placeOf`/`withScenario` for
  `ForkScenario`; the chooser offers every `Scenario.all` case.

## Manual editor check (optional, opens a window)

1. Start the app, open the **Scenario** panel, and Register a scene under `Intro`.
2. Move the author around so the scene differs from what was captured; press **Fork** on `Intro`'s row.
3. The captured entities are added and the captured cue begins beside the screen cue; the viewport does not
   jump to another place.
4. `Ctrl+Z` removes exactly what the fork added and restores the cue; `Ctrl+Y` reapplies it.
5. With `Intro` cleared, its **Fork** button is disabled.

## Docs to refresh (`FR-015`)

- `docs/ARCHITECTURE.md` — "Scenario & Cue": running a scenario is forking it into the scene.
- `docs/EDITOR_DESIGN.md` — Workspace's Scenario row gains **Fork**; the `Fork scenario` cue op is listed.
- `docs/ROADMAP.md` — phase 11's remainder (running a scenario) lands.

## Expected outcome summary

| Check | Expected |
|-------|----------|
| `dotnet build fsharp-game.fsproj` | 0 warnings, 0 errors |
| `dotnet test test/fsharp-game.Tests.fsproj` | all green (531 existing + the new cases) |
| A fork's active place / camera / selection | unchanged (`FR-004`) |
| A refused fork | logs and changes nothing (`FR-010`) |
