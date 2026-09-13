# Quickstart: Instanced Static Draws

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- `glslc` (to recompile `shaders/static.vert.spv`) and SDL3 for the app.
- The ArtRoom environment asset and at least two prop assets that use different
  geometry (ideally both with submesh index 0, to exercise the key).

## Build & test

```sh
glslc shaders/static.vert -o shaders/static.vert.spv
dotnet build
dotnet test test/fsharp-game.Tests.fsproj   # includes EnvironmentBatchTests
```

## Validation scenarios

0. **Baseline (SC-002)** — before the change, instrument the current per-submesh
   path and record the ArtRoom baseline: `draws 521` and `record` ~20–30 ms.
   Without this the post-change reduction cannot be compared.
1. **Draw calls collapse (SC-002, SC-005)** — load the ArtRoom and play. The
   per-frame log reports `draws 191` (its distinct submeshes) against
   `instances 521`, versus 521 draws before. With no props, `draws` equals the
   environment's distinct-submesh count.
2. **Frame budget (SC-001)** — in the same run, `record` falls from the previous
   ~20–30 ms into the frame budget (target under 5 ms), and the frame holds the
   target rate.
3. **Image unchanged (SC-003)** — compare the scene against a build before this
   feature: geometry, orientation, scale, materials, and lighting are identical.
   Check a rotated/unevenly scaled prop, and a normal-mapped surface.
4. **Key correctness (FR-002, edge case)** — place two props whose assets both
   use submesh index 0; each must render its own geometry and material (no mixed
   up or collapsed geometry). The frame's `draws` increases by the props' distinct
   submeshes, not by their count.
5. **Extra copies add no draws (SC-004)** — place several more copies of the same
   prop; `draws` is unchanged while `instances` grows.
6. **Spawn/delete while editing** — add and remove a prop in the editor; the next
   frame's `draws`/`instances` reflect it, with no stale group and no crash.
7. **Paused editor** — with the world not simulating, the scene still renders
   correctly through the batch.
8. **Empty scene** — no environment and no props: no static draw calls, no error;
   actors and gizmos still render.
9. **No per-frame allocation / no regression (SC-006)** — the frame log shows no
   added per-frame work beyond the batch; actor and debug/gizmo rendering is
   unchanged.

## Expected outcome

- The ArtRoom draws 191 instanced calls instead of 521, and `record` drops into
  the frame budget.
- The rendered image is unchanged, including two assets that share a submesh
  index.
- Draw cost scales with distinct submeshes; extra copies add no draw calls.
- `dotnet build` is clean; `dotnet test` passes (existing suite +
  `EnvironmentBatchTests`).
