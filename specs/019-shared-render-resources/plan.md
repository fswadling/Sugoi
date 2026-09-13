# Implementation Plan: Shared Render Resources

**Branch**: `019-shared-render-resources` | **Date**: 2026-09-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/019-shared-render-resources/spec.md`

## Summary

Move the device-level render resources out of per-screen state and into shared
app state. Today `Engine.run` creates a `Gpu.Gpu` context (device, swapchain and
depth formats, mesh/static pipelines, depth target) and then destructures
`gpu.MeshPipeline`/`gpu.StaticPipeline` into `GameplayState` — so the pipelines
live twice and screen state carries raw hardware handles. This feature gives the
shared state the `Gpu` context (the single owner), drops the two pipeline fields
from `GameplayState`, and threads shared state into `GameplayScreen.renderScene`
so screens read device resources from their one home. Per-screen render buffers
(`DebugDraw`, `GizmoDraw`, `EnvironmentBatch`) and the camera stay on the screen.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`).

**Primary Dependencies**: `SDL3-CS` 3.4.14.1 (SDL_gpu). No new dependency.

**Storage**: N/A.

**Testing**: xUnit. This is an ownership/location change with no new pure logic;
correctness is validated by the existing suite plus the app's own draw-count log.
`test/SpawningTests.fs` constructs a `GameplayState` by hand and will need its
constructor/`Batch` handling updated.

**Target Platform**: Linux x64 desktop (native), Vulkan backend.

**Project Type**: native desktop game.

**Performance Goals**: none — no per-frame work or allocation is added (FR-010);
the render path is unchanged.

**Constraints**: F# compile order is the dependency order; `state/SharedState.fs`
precedes `state/GameplayState.fs`, so screen state may read shared state and not
the reverse. The `Gpu.Gpu` context is a struct today; wherever it is held must be
mutable so the depth target can be recreated on resize. `GameplayScreen.renderScene`
currently receives only the screen state, so shared state must be threaded in.

**Scale/Scope**: one shared GPU context, two screens plus the manager that owns
their state, and the gameplay screen's state. No change to `world/`, `render/`,
physics, or the command/undo model.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: this removes a duplication (the pipelines) and narrows
  screen state; it adds no abstraction.
- **II. Fail Loudly** — PASS: unchanged; no new failure paths.
- **III. Spec-First** — PASS: `spec.md` precedes this plan and the tasks.
- **IV. Data-Oriented Architecture** — PASS: `SharedState` is already a reference
  type holding device-derived state (the asset manager); adding the GPU context
  is consistent. No new object graph.
- **V. Minimise GC Pressure** — PASS: no new per-frame allocation; the context is
  a struct held once.

## Project Structure

### Documentation (this feature)

```text
specs/019-shared-render-resources/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 validation guide
├── contracts/
│   └── state-ownership.md # State + screen contract
├── checklists/
│   └── requirements.md  # /speckit-specify quality gate
└── tasks.md             # /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
state/SharedState.fs        # EXTENDED: owns the Gpu context (device-derived render resources)
state/GameplayState.fs      # CHANGED: drop MeshPipeline/StaticPipeline; create from shared state
screen/ScreenManager.fs     # CHANGED: build/receive SharedState with the Gpu; pass shared to renderScene
screen/GameplayScreen.fs    # CHANGED: renderScene takes shared state; read pipelines from shared.Gpu
Engine.fs                   # CHANGED: hand the Gpu context to shared state; use shared.Gpu for depth/resize/destroy
test/SpawningTests.fs       # CHANGED: minimal GameplayState construction drops the pipeline fields
```

**Structure Decision**: the `Gpu.Gpu` context becomes a field of `SharedState`
(created once, after the device, released before it). Screen state keeps only the
screen's entities, view, per-screen render buffers, and editor/play state; the
two pipeline fields are deleted. `GameplayScreen.renderScene` gains a shared-state
parameter so it can read `shared.Gpu.MeshPipeline` / `shared.Gpu.StaticPipeline`
instead of holding copies. Nothing in `world/` or `render/` changes: the batch,
debug draws, and camera stay where they are.

## Complexity Tracking

No constitution violations to justify.
