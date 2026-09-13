# Implementation Plan: Instanced Static Draws

**Branch**: `018-instanced-draws` | **Date**: 2026-09-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/018-instanced-draws/spec.md`

## Summary

The frame is CPU-bound on draw submission: the ArtRoom's 463 parts issue 521
per-submesh static draws, costing ~20–30 ms in `record` (`docs/RENDER_PERFORMANCE.md`
§4–§6). Geometry is already deduplicated at glTF load, so the 521 draws are
instances of only **191 distinct submeshes**. Batch the static scene by unique
submesh identity — `(asset, submeshIndex)`, not a bare index — and draw one
instanced call per group, collapsing 521 → 191. `model`/`normal` move from the
pushed per-draw uniform into a per-instance vertex-buffer slot; `viewProj` is
pushed once per static block. Animation (`ActorRender`) and debug/gizmo draws are
untouched.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`).

**Primary Dependencies**: `SDL3-CS` 3.4.14.1 (SDL_gpu); `glslc` to recompile the
static vertex shader; `System.Numerics`. No new managed dependency.

**Storage**: N/A — the instance buffer is per-frame GPU state owned by the batch.

**Testing**: xUnit. GPU and asset paths need a device, so the pure grouping
planner (per-identity counts → prefix-sum → group offsets) is unit-tested in
`test/EnvironmentBatchTests.fs`, including the two-asset-same-index case. The
rendered image is validated manually via `quickstart.md`.

**Target Platform**: Linux x64 desktop (native), Vulkan backend.

**Project Type**: native desktop game.

**Performance Goals**: the whole point — `record` for the ArtRoom falls from
~20–30 ms to under 5 ms, and draw calls from 521 to 191. Draw cost scales with
distinct submeshes, not instances (SC-001, SC-002, SC-004).

**Constraints**: F# compile order is the dependency order; `render/` may not
depend on `world/`, so the batching lives in `world/` and only the reusable GPU
buffer lives in `render/`. The static pipeline is extended, not replaced; the
mesh (actor) and line (debug) pipelines are unchanged. The instance buffer is
sized on the first gather to the scene's instance count, then grow-only, and
holds retired buffers until they are no longer in flight; buffers are reused with
no per-frame allocation (FR-010).

**Scale/Scope**: 463 environment parts / 521 submesh instances → 191 draws for
the environment alone; plus one group per distinct submesh across placed props.
No culling, no geometry merge, no freeze-time count cache.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: batch keyed on the already-shared submesh; no geometry
  merge, no culling, no cache-before-profiling. The exception lists (per-identity
  Instances) reuse the existing `StaticMesh` buffers rather than duplicating them.
- **II. Fail Loudly** — PASS: an out-of-range submesh range or a registry miss
  raises rather than silently skipping geometry.
- **III. Spec-First** — PASS: `spec.md` precedes this plan and the tasks.
- **IV. Data-Oriented Architecture** — PASS: the instance data is one contiguous
  reused `byte array`; the registry is flat arrays indexed by a dense identity;
  no per-instance objects.
- **V. Minimise GC Pressure** — PASS: gather/upload reuse grow-only buffers and
  allocate nothing per frame (FR-010).

## Project Structure

### Documentation (this feature)

```text
specs/018-instanced-draws/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 validation guide
├── contracts/
│   └── instanced-static.md # Pipeline + batch contract
├── checklists/
│   └── requirements.md  # /speckit-specify quality gate
└── tasks.md             # /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
shaders/static.vert          # EXTENDED: per-instance model/normal attributes; PerDraw = viewProj
shaders/static.vert.spv      # recompiled with glslc (same filename; CopyShaders unchanged)
render/Gpu.fs                # EXTENDED: createStaticPipeline gains slot 1 (instance, 7 attrs)
render/StaticMesh.fs         # EXTENDED: instanced draw; PerDraw shrinks to viewProj (64 B)
render/InstanceBuffer.fs     # NEW: grow-only instance vertex buffer + transfer + CPU staging
world/EnvironmentRender.fs   # CHANGED: stops issuing draws; stays the per-instance data holder
world/EnvironmentBatch.fs    # NEW: dense submesh registry + gather + one instanced draw per group
screen/GameplayScreen.fs     # EXTENDED: gather/upload before the pass; one batch draw in it
Engine.fs                    # EXTENDED: draw-call / instance counters in the per-frame log
test/EnvironmentBatchTests.fs# NEW: pure grouping planner
fsharp-game.fsproj           # add render/InstanceBuffer.fs, world/EnvironmentBatch.fs
test/fsharp-game.Tests.fsproj# add EnvironmentBatchTests.fs
```

**Structure Decision**: `render/InstanceBuffer.fs` (GPU-only, depends on `Gpu`)
is inserted after `render/StaticMesh.fs`. `world/EnvironmentBatch.fs` walks
`Zone` and the assets' submeshes, so it lives in `world/` (the dependency
direction is `world → render`) and is inserted after `world/Zone.fs`. The batch
keys on the shared submesh identity, which is already deduplicated by
`Gltf.load`/`Assets.loadStaticModel`, so no geometry is re-uploaded or merged.

## Complexity Tracking

No constitution violations to justify.
