# Implementation Plan: Zone-Owned Static Batch

**Branch**: `020-zone-owned-batch` | **Date**: 2026-09-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/020-zone-owned-batch/spec.md`

## Summary

Move the static-scene render batch (`EnvironmentBatch`) off `GameplayState` and
onto `Zone`. Three consequences drive the design:

1. **Compile cycle.** `EnvironmentBatch.gather` walks `Zone`; if `Zone` owns the
   batch, `Zone` needs `EnvironmentBatch` to be compiled first. Break it by moving
   `world/EnvironmentBatch.fs` to just before `world/Zone.fs` and having `gather`
   read the zone's prop/part maps instead of the `Zone` type. `Zone` then exposes a
   thin `gatherStatic` helper for callers.

2. **GPU-free zone creation.** Many headless tests call `Zone.create ()` and must
   keep working. Making `Zone.create` allocate GPU buffers would break them, so
   buffer allocation becomes **lazy**: `EnvironmentBatch`/`InstanceBuffer` are
   created without a device, and the device is supplied when the batch is first
   gathered/uploaded for rendering (the render path already has shared state).

3. **Lifetime.** The batch gains a `release` that frees its GPU buffers; the live
   zone's batch is released at shutdown before the device is destroyed. Per-zone
   unload release is wired when zone unload exists (the zone map), later.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`).

**Primary Dependencies**: `SDL3-CS` 3.4.14.1 (SDL_gpu). No new dependency.

**Storage**: N/A.

**Testing**: xUnit. The headless suite is the main guard this feature must not
break: `ZoneTests`, `PlayerTests`, and `SpawningTests` all call `Zone.create ()`
with no device. `SpawningTests` also hand-builds a `GameplayState`.

**Target Platform**: Linux x64 desktop (native), Vulkan backend.

**Project Type**: native desktop game.

**Performance Goals**: none added — allocation stays off the per-frame path and now
happens lazily on first use/growth instead of at zone creation.

**Constraints**: compile order must stay acyclic. `world/` may not depend on
`state/`; `render/` may not depend on `world/`. `EnvironmentBatch` holds
`StaticSubmesh` (a `world/EnvironmentAsset` type), so it stays in `world/`, ordered
after `world/Entity.fs` (for `Prop`/`Entity.modelMatrix`) and after
`world/EnvironmentRender.fs`, and before `world/Zone.fs`.

**Scale/Scope**: one zone today (single active, single drawn); the change makes the
batch per-zone for the coming zone map. No change to rendering behaviour, the
entity model, physics, or commands.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: the batch moves and its gather loses the `Zone`
  parameter; no new abstraction. Lazy allocation removes an eager cost.
- **II. Fail Loudly** — PASS: unchanged; render only runs with a valid device and
  a non-empty instance count.
- **III. Spec-First** — PASS: `spec.md` precedes this plan and the tasks.
- **IV. Data-Oriented Architecture** — PASS: the batch stays a reference type of
  reused buffers; ownership moves onto the zone it describes.
- **V. Minimise GC Pressure** — PASS: still no per-frame allocation; allocation is
  once-per-growth and now deferred.

## Project Structure

### Documentation (this feature)

```text
specs/020-zone-owned-batch/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 validation guide
├── contracts/
│   └── batch-ownership.md # Batch + zone + screen contract
├── checklists/
│   └── requirements.md  # /speckit-specify quality gate
└── tasks.md             # /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
world/EnvironmentBatch.fs   # MOVED before Zone.fs; device-less create; gather takes prop/part maps; upload takes device; add release
world/Zone.fs               # EXTENDED: owns `Batch`; create sets it; `gatherStatic` helper
render/InstanceBuffer.fs    # CHANGED: device-less create; ensure/upload/reclaim/release take the device; lazy alloc
state/GameplayState.fs      # CHANGED: drop `Batch`; create no longer needs shared state
screen/ScreenManager.fs     # CHANGED: GameplayState.create takes only the debug draws
screen/GameplayScreen.fs    # CHANGED: gather/upload/render the active zone's batch
Engine.fs                   # CHANGED: log reads the zone's batch; release it at shutdown
test/SpawningTests.fs       # CHANGED: hand-built GameplayState drops the batch field
fsharp-game.fsproj          # MOVED: EnvironmentBatch.fs before Zone.fs
```

**Structure Decision**: the batch is the zone's renderer for its static scene, so
it moves onto `Zone`. To keep the dependency graph acyclic it must be compiled
before `Zone.fs`, and the zone-walking loop becomes a function of the zone's maps
(`SlotMap<Prop>` + optional parts map) rather than of the `Zone` type; `Zone`
provides `gatherStatic` so call sites stay clean. GPU buffers become lazy so
`Zone.create ()` stays device-free. `DebugDraw`/`GizmoDraw`/`Camera` remain screen
state — they are view overlays, not zone content.

## Complexity Tracking

No constitution violations to justify.
