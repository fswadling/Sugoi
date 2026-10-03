# Quickstart: Async asset loading

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Contracts**: [contracts/environment-loading.md](./contracts/environment-loading.md)

Runnable validation for async environment loading. Two parts: the headless suite (always), and a GPU
walkthrough that shows the frame loop stays live through an ArtRoom load (the load path needs a
device the test suite cannot open).

## 0. Prerequisites

- The reference machine and assets (`assets/environments/ArtRoom`, `assets/environments/Island`, and
  a couple of light zones).
- Run with `FFS_LOAD_TIMING=1` to enable the per-asset timing line, which now separates the worker's
  parse/decode from the render thread's staging/memcpy/submit and reports the queue wait.

## 1. Headless validation (no GPU required)

```sh
dotnet build fsharp-game.fsproj
dotnet test test/fsharp-game.Tests.fsproj
```

- Both projects build with **0 warnings**; the suite passes **412 tests** unchanged plus the new
  queue tests.
- New headless coverage: the pending set is de-duplicated by id; a burst beyond the capacity drops
  the least-recently-requested; a pinned (active) id is never dropped; a repeat request moves an id
  to the most-recent end.

## 2. The frame loop stays live while ArtRoom loads (FR-001, SC-001, US1)

Run with `FFS_LOAD_TIMING=1`; watch the once-per-second frame log and switch to the zone whose
environment is **ArtRoom**.

- **Before (baseline)**: one frame shows `ui ~5000 ms` (the decode + upload inside `buildLoaded`) and
  the loop is dead for ~5.1 s.
- **After**: no frame blocks for the load — `ui`/`acquire`/`submit` stay in their normal range
  (the `pump` slice is bounded by `UploadBudgetMs`, 4 ms), the loop keeps running, and the
  environment **pops in** when its uploads complete (a few seconds later, dominated by the unchanged
  decode wall-clock).
- The `load ArtRoom` line still reports the true decode cost, now attributed to the worker.

## 3. Zone switches complete in the requesting frame (FR-004, SC-002, US1)

- Switch to a non-resident zone and back, and click through the Outliner: the active zone changes
  immediately every time; there is no stall waiting for an environment.
- The residency glyph for the incoming zone reads `[~]` (loading) and becomes `[+]` (resident) when
  it lands; a zone with no environment still reads `[-]` (FR-007, US4).

## 4. A burst is bounded and most-recent-first (FR-005, SC-004, US3)

- Hold undo (or click rapidly through many zones) to fire more distinct requests than the queue
  capacity.
- Confirm: the app stays responsive; only the most recently requested environments load; the active
  zone's environment always loads; requests the user has moved past do not all get loaded.

## 5. Identical rendering and residency accounting (FR-010, SC-003, SC-005)

- Let ArtRoom land fully and confirm the scene is pixel-identical to the synchronous load: same
  geometry, textures, materials, and the frame log's `draws`/`instances`/`parts` unchanged
  (ArtRoom: `draws 191`, `instances 521`, `parts 463`).
- Walk zones and confirm residency behaves exactly as in spec 050: the byte budget, the eviction
  order, and the readout. A pending (not-yet-resident) environment contributes nothing to the
  budget.
- Repeatedly load and unload: resident + staging memory returns to steady state after the first
  cycle (SC-006).

## 6. Failure is surfaced, not half-applied (FR-008, SC-007, US5)

- Point an environment entry at a **missing or corrupt** model or texture and activate its zone.
- Confirm: the error is logged, the zone's glyph reads `[x]` (failed), the zone is **not** stuck
  loading, no partial environment is resident, and the app keeps running. A later request retries.
- Shutdown (close the window) after a load has started: the workers are stopped and joined, and
  shutdown is clean (FR-011).

## 7. Report

Record: the before/after frame-log lines for the ArtRoom load (a ~5 s `ui` stall vs. bounded
frames), the residency glyph transitions, the burst outcome, and the failure/shutdown outcome. This
is the SC-001/SC-002/SC-004/SC-007 evidence for the feature.
