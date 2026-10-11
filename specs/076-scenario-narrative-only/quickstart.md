# Quickstart: Trim Scenario Scope to Narrative Only

**Feature**: `specs/076-scenario-narrative-only` | **Date**: 2026-10-11

Validation guide for the trimmed scenario record. It proves the record carries only narrative entities plus the cue,
and that nothing else changed. See `data-model.md` for the shapes and `contracts/scenario-capture.md` for the API.

## Prerequisites

- .NET 10 SDK, at the repository root.
- Headless validation needs no window, GPU or asset files.
- The manual walkthrough needs a desktop (SDL3/ImGui) and the usual `assets/`.

## Headless validation (primary)

```sh
dotnet build          # expect: no warnings
dotnet test           # expect: green, including ScenarioTests
```

What the scenario tests must show (updated by this feature):

1. **The record is narrative-only.** Register a scene whose zone has actors (including a chained pair of waypoints)
   and triggers; assert each captured zone holds exactly those arrays with chains as indices, and that
   `ScenarioContents` exposes no active place and the `ScenarioZone` type has no environment, light or player field.
2. **Staging is independent.** Register; then change only the zone's **light** and **player** (a loaded environment
   can't be staged headlessly, and the record has no environment to compare) and register again; assert the two
   captures are equal (no entity edits between them).
3. **Every present place is recorded.** Register a multi-zone scene where one zone is empty; assert both places
   appear in `contents.Zones`, the empty one as an empty `ScenarioZone`.
4. **The step round-trips.** Register, undo, redo and clear through `History`, asserting the library holds the
   expected contents at each point — one step each.

These run with no device (the capture is pure over the zones it is handed).

## Manual editor walkthrough

1. `dotnet run`; in the editor, ensure **editing** is on.
2. In the Outliner: **Add** a second place; in the active zone, place an actor, two waypoints (chain them), and a
   trigger; in the Inspector, load an environment, change the sun's pitch, and **Set as Player** on the actor.
3. Open the **Scenario** panel and press **Register** on `Intro`. The row shows `2 zone(s)`.
4. Change only the environment/light/player (not the entities). Press **Register** again. The contents are
   unchanged from step 3 (same narrative entities).
5. Press **Ctrl+Z**: the previous registration is restored (an overwrite undoes to what it held before).
   **Ctrl+Y** re-applies it.
6. Press **Clear**: the row shows `(none)`; **Ctrl+Z** restores the contents.
7. Confirm the scene was never disturbed: the active place, camera, and selection are as before the registration,
   and the running cue is untouched.

## Expected outcomes

- A captured zone holds only actors, waypoints (chains by index) and triggers.
- Contents hold the zones keyed by place and the cue — no active place.
- Registering changes environment/light/player between captures, yet the two captures are equal.
- Build is warning-free; the suite is green; no device is opened for the headless checks.

## Out of scope for this feature

Forking a scenario into the running scene (ROADMAP phase 11's remainder). A future fork takes a place's environment
and light from the Zone Registry's template, and leaves the active place and the player designation as they are —
see `research.md`'s note for the fork increment.
