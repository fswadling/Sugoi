# Quickstart — Scenario Library

How to validate `specs/070-scenario-library`. Two halves: the headless assertions (`dotnet test`) and a
by-hand walkthrough that needs a window.

## Prerequisites

- .NET 10 SDK; run from the repository root (`fsharp-game/`).
- `native/libcimgui.so` present (for the walkthrough only; the tests open no device).

## Headless (automated)

```sh
dotnet build fsharp-game.fsproj           # warning-free (spec SC-004)
dotnet build test/fsharp-game.Tests.fsproj
dotnet test test/fsharp-game.Tests.fsproj
```

`test/ScenarioTests.fs` covers:

1. **The capture** (spec FR-002/FR-009/FR-012, SC-003). Build a `Dictionary<Place, Zone.Zone>` by hand (as
   `PlayerTests`/`GameplayStateTests` do — `Zone.create ()`, `Zone.spawnWaypoint`, `zone.Actors.Add`,
   `zone.Player`), call `ScenarioRegistry.capture zones active cue`, and assert the `Zones` map holds one entry per
   present zone (keyed by place), that each entry equals `ScenarioZone.capture` of the source, that `Active` is the
   active place, and that `Cue` is the cue passed. Then assert the stored zone is **ref-free**: a waypoint chain is
   captured as `Next : int option`, the player as an actor index, and nothing names a live slot.
2. **Capture is exact** (US1 #4). Delete/duplicate a part in a zone first, then assert the capture matches
   part for part. (Placed props are not captured — see `spec.md`'s "Scenarios omit placed props" assumption.)
3. **Register / undo / redo** (US2). Through `History.commit` + `History.undoHistory` + `History.redoHistory`
   over a headless `GameplayState`: register an unregistered scenario → holds contents; undo → unregistered; redo
   → contents back.
4. **Overwrite / undo** (US2 #2). Register, edit the scene, register again, undo → the *previous* contents, not
   empty.
5. **Clear / undo** (US1 #5, FR-011). Clear a registered scenario → unregistered; undo → the same contents.
6. **The scene is untouched** (FR-008, SC-005). Assert the active place, the zone count, the cue, the camera and
   the selection are unchanged across a register and an undo.

No test opens a GPU device; the capture and the step are pure over the gameplay state.

## By hand (a window)

```sh
dotnet run
```

1. Launch, Play, then open the editor (backtick if needed) and find the **Scenario** panel — it is docked in the
   **left** column as a tab beside **Outliner**, **Cast** and **Zone Registry**.
2. The single declared scenario, **Intro**, is listed with **Register** and no contents shown.
3. Author a scene: add a second zone (Zone Registry → Instantiate), drop in actors, lay a waypoint, place a
   trigger, and put something in the Cue panel.
4. Press **Register** on **Intro**. The row now reports the scenario holds contents (its zone count).
5. Press **Ctrl+Z**. The row returns to no contents; the scene is unchanged. **Ctrl+Shift+Z** brings the
   registration back.
6. Re-author the scene, press **Register** again (an overwrite), then **Ctrl+Z** — the *previous* registration
   returns.
7. Press **Clear** (offered now it holds contents). The scenario shows no contents; **Ctrl+Z** restores them.
8. Confirm the rest of the editor is undisturbed throughout: no zone switched, no entity moved, no selection
   changed.

## Out of scope (do not expect)

- There is no way to **run** a registered scenario: forking it into the running cue is the next increment
  (`docs/ROADMAP.md` phase 11's remainder).
- Nothing is **persisted**: the library is in-memory until phase 15.
