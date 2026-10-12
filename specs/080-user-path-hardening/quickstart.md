# Quickstart: Graceful Failure on User-Facing Paths

**Feature**: `specs/080-user-path-hardening` | **Date**: 2026-10-12

How to build this feature and prove it works end to end. Most of it runs **headlessly** — the cue-engine,
history, cue-editor and capture behaviours are unit-tested without a window or device, as the suite
already is (`SC-006`). The renderer, asset-scan and frame-loop cases are checked by the run scenarios at
the end. Record shapes and the per-action guarantees are in [`data-model.md`](./data-model.md) and
[`contracts/user-path-hardening.md`](./contracts/user-path-hardening.md); this file only says how to run
and what to expect.

## Prerequisites

- .NET SDK 10 (`net10.0`), as the project targets.
- Repo root: `/home/fswadling/Repos/fsharp-game`.
- No new dependency. Every touched file already exists and keeps its place in the compile order, so the
  `.fsproj` is unchanged.

## Build (must be warning-free)

```sh
dotnet build fsharp-game.fsproj
```

Expected: **0 warnings, 0 errors**.

## Test (the suite must be green)

```sh
dotnet test test/fsharp-game.Tests.fsproj
```

Expected: the existing suite passes unchanged (the fail-fast / invariant tests still pass — `SC-004`,
`FR-012`), plus the new coverage below. No new test opens a device.

### What the new/updated coverage asserts

- **US1 — clip fallback.** With a device-free actor-renderer fixture, requesting a clip the asset lacks
  returns a defined fallback rather than raising; a clip the asset has is unaffected (`SC-002`).
- **US2 — camera ops and a bare fork.** In `CueTests`, each camera op on a missing place / missing
  waypoint / short path / non-positive speed leaves the camera untouched and the cue proceeds to `Fin`;
  a cue that is a bare `ForkScenario` runs to completion. The existing "a Fork outside a sequence raises"
  case is rewritten to assert the new defined behaviour.
- **US3 — the conditional branch.** In `CuePanelTests`, `withAt`/`withoutAt`/`withAppendedChild` on an
  `If` branch rebuild the node correctly (both arms; a single remaining branch preserved).
- **US4 — skip-and-report.** In `GameplayStateTests`, build a history whose step names a place, remove
  that place out-of-band (as a cue would), then undo and redo the history to both ends: no throw, the
  stale step is skipped and reported, and surviving steps still apply (`SC-001`).
- **US5 — capacity.** In `SpawningTests`, fill a kind's map to `Capacity`, attempt one more create: no
  entity is added and a reason is reported.
- **US6 — dangling link capture.** In `ScenarioTests`/`ScenarioForkTests`, delete a waypoint's successor,
  capture the zone, and assert the link is recorded absent and capture succeeds.

## Run scenarios (renderer, assets, boundary)

These need a window/GPU (a display) and are validated by running the app, not by the headless suite.

### US1 — play with a missing locomotion clip

1. In the editor, set as player an actor whose asset defines neither `"Idle"` nor `"Jog"`.
2. Press **Backtick** to Play and hold a movement key.
3. Expected: the app keeps running; the actor holds a defined pose; the console shows one fallback line.

### US7 — a bad asset is skipped

1. Put a malformed `.iqm` (or `.gltf`) in an actor/prop asset directory, or corrupt one.
2. Run the app.
3. Expected: `asset '<id>': …` is reported once; the app starts and runs without that asset.

### US7c — a missing font

1. Temporarily rename `assets/fonts/DejaVuSans.ttf`.
2. Run the app.
3. Expected: `ui: failed to open the font …; text disabled` is reported and the app runs (with no text).

### US8 — a forced unexpected failure

1. Temporarily insert `failwith "boom"` into a per-frame system (say, at the top of `ScreenManager.update`).
2. Run the app.
3. Expected: `frame failed in phase '…': …` is reported and the app suppresses the window and exits
   cleanly through teardown (`SDL3 + ImGui (SDL_gpu): clean shutdown`) — no unexplained abort.

## Gate

The feature is done when `dotnet build` is warning-free, the headless suite is green including the new
cases, and each run scenario above behaves as described.
