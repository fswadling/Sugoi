# Quickstart: Batched asset uploads

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Contracts**: [contracts/batched-upload.md](./contracts/batched-upload.md)

Runnable validation for the batched upload. Two parts: the headless suite (always), and a
GPU walkthrough that records the before/after numbers (the upload path needs a device the
test suite cannot open).

## 0. Prerequisites

- The reference machine and assets (`assets/environments/ArtRoom`, `assets/environments/Island`).
- Run with `FFS_LOAD_TIMING=1` to enable the per-asset timing line, which reports:
  `parse | buf-create | tex-create | stage | memcpy | submit | decode` (FR-008/FR-010).

## 1. Headless validation (no GPU required)

```sh
dotnet build fsharp-game.fsproj
dotnet test test/fsharp-game.Tests.fsproj
```

- Both projects build with **0 warnings**; the suite passes **412 tests** unchanged.
- This is the regression gate for FR-002/FR-007: the batch changes no pure logic, so any
  changed test result is a real regression.

## 2. Batching holds — submission count (FR-001, SC-002)

Run with `FFS_LOAD_TIMING=1`, then click **ArtRoom** in the Asset Browser's Environments list and
read the instrumentation line.

- **Before (baseline)**: `submits 469` (382 geometry + 87 texture).
- **After**: `submits` is a **small handful (single digits)** that scales with staging flushes, not
  with ArtRoom's resource count.
- Click **Island** too: it was `submits 2`; after, `submits 1`. Small assets still work and pay no
  extra cost (SC-005).

## 3. Upload cost and peak staging (FR-001, FR-004, SC-001, SC-004)

Same run, same lines. Measured on ArtRoom:

- **Before**: 469 submissions; `buffer-upload ~666 ms x382` + `tex-upload ~554 ms x87`
  (~0.82 s of that was pure submission overhead); peak staging ~one resource.
- **After**: a few submissions, `submit` a few ms (overhead well under SC-001's ~5%); `stage`
  (create/map/unmap) and `memcpy` (host copy) split out; **peak staging bounded by
  `StagingCapacity` (256 MiB) — one staging buffer, not the asset's size** (SC-004).
- **Not submission overhead, not removed**: the host `memcpy` (~160 ms) and per-texture creation
  (`tex-create`, ~220 ms). Aggregate upload work falls ~3×; these floors are why SC-001 no longer
  names a sub-150 ms aggregate. See spec SC-001 and the Assumptions.
- **Not expected to change**: `decode` (~4.3 s for ArtRoom) — out of scope (spec Assumptions).

Record both lines (before from the baseline run, after from this one) as the evidence.

## 4. Identical rendering (FR-002, SC-003)

- Load ArtRoom and Island and confirm the scene looks exactly as before: same geometry, same
  textures, same materials.
- Confirm the frame log's `draws`/`instances`/`parts` are unchanged (ArtRoom: `draws 191`,
  `instances 521`, `parts 463`).
- Walk a couple of zones and confirm residency behaves as in spec 050 (the readout, the byte
  budget, eviction) — nothing here should be observable.

## 5. Failure rollback (FR-006)

- Point an environment entry at a **missing or corrupt texture** (or model) and load it.
- Confirm: the load fails loudly as before, the app keeps running, **no partial asset** is
  left resident, and there is no leak. An abort makes no further submission; if the asset had
  already flushed, the rollback releases those copies' target resources. The zone/asset returns
  to its prior state.

## 6. Large-resource bound (FR-004, US3)

- If any single texture or buffer exceeds the staging capacity, confirm it uploads correctly (its
  copies all land) and peak staging stays under the fixed bound regardless of the asset's total
  size — a large asset is uploaded in a few bounded flushes, not held whole in memory.

## 7. Report

Record: the before/after `submits` and upload-ms lines for ArtRoom and Island, confirmation
that `draws`/`instances`/`parts` and the rendered image are unchanged, and the failure-path
outcome. This is the SC-001/SC-002/SC-003/SC-005 evidence for the feature.
