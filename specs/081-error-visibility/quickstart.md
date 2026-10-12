# Quickstart: Error Visibility

**Feature**: `specs/081-error-visibility` | **Date**: 2026-10-12

How to build this feature and prove it works. The tally is asserted headlessly; the two display surfaces need a
window and are checked by running the app. The per-requirement guarantees are in
[`contracts/error-visibility.md`](./contracts/error-visibility.md) and the shapes in
[`data-model.md`](./data-model.md).

## Prerequisites

- .NET SDK 10 (`net10.0`), as the project targets.
- Repo root: `/home/fswadling/Repos/fsharp-game`.
- No new dependency and no native rebuild: the two ImGui functions this uses are already exported by the
  vendored `native/libcimgui.so`.

## Build (must be warning-free)

```sh
dotnet build fsharp-game.fsproj
```

Expected: **0 warnings, 0 errors**. `core/Diagnostics.fs` is listed in `fsharp-game.fsproj` immediately after
`core/SlotMap.fs`.

## Test (the suite must be green)

```sh
dotnet test test/fsharp-game.Tests.fsproj
```

Expected: the existing suite passes unchanged (the ~20 call sites that moved from `eprintfn` to the tally
behave identically on the console), plus the new coverage below. No test opens a device.

### What the new coverage asserts (`test/DiagnosticsTests.fs`)

- **Count and last message** — a report raises `errorCount` by one and becomes `lastMessage` (FR-001).
- **De-duplication** — `n` reports of the same text yield **one** entry whose `Count` is `n`, with
  `errorCount = n` (FR-002, SC-002).
- **Ordering** — distinct reports appear most-recent-first (FR-004).
- **Formatting** — `reportf` with a format string produces the same text `eprintfn` would (FR-006).
- **Threshold** — `isLoud()` is false below `loudThreshold` and true at it (FR-005).

The tally is process-global, so each test clears it first; the runner serializes collections
(`test/xunit.runner.json`), so no other class reports between a clear and an assert.

## Run scenarios (the display surfaces)

These need a display. Cause a report, then observe.

### US1 — the count is visible

1. Start the app and enter Play (or Edit).
2. Trigger a handled failure — easiest: author a cue with a `Set camera` op naming a waypoint, delete the
   waypoint, and run the cue (`specs/080`). Or point an actor at an asset with no `"Idle"` clip and walk.
3. Expected: the Debug window shows a red `N error(s): <message>` line and the distinct-report list; the menu
   bar shows `(!) N error(s)`. No terminal is involved (`SC-001`).

### US1 — nothing is shown when clean

1. Start the app and do not trigger any report.
2. Expected: the Debug window shows only its ordinary read-outs; there is no menu-bar item and no red line
   (`FR-003`, `SC-005`).

### US2 — the detail is reachable

1. Collapse or close the Debug window.
2. Cause reports to accumulate past the threshold (repeat the failure ~10 times).
3. Expected: the indicator is still visible in the menu bar; clicking it — or the automatic one-time focus —
   brings the Debug window forward with the de-duplicated detail, one entry per distinct message with its
   count (`FR-004`, `FR-005`).

### The console is unchanged

1. Cause a report with the app's output attached to a terminal.
2. Expected: the same line you would have seen before this feature (`FR-006`).

## Gate

The feature is done when `dotnet build` is warning-free, the headless suite is green including the new cases,
and the run scenarios above behave as described.
