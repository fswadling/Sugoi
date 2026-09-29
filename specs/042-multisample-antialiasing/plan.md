# Implementation Plan: Multisample Anti-Aliasing

**Branch**: `042-multisample-antialiasing` | **Date**: 2026-09-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/042-multisample-antialiasing/spec.md`

## Summary

The renderer draws every frame with **no multisampling anywhere**: each graphics pipeline and every
render target is `SampleCount1`, and the scene pass renders **straight into the acquired swapchain
texture** (`Engine.fs`). So the actor outline (spec 041) and every geometry edge in the frame is
stair-stepped. The inner boundary of the outline — a near-black line against the bright cel character —
shows it most.

Give the scene pass **4× MSAA**: render the whole scene into a **multisampled colour texture** and a
**multisampled depth texture**, then **resolve** the colour into the acquired swapchain image
`GPUColorTargetInfo.ResolveTexture`). SDL_gpu has no multisampled swapchain, so the offscreen target +
resolve is the way (research D1). The scene pass already exists; the work is the two targets, the
sample count on every scene-pass pipeline, and the resolve wiring in the engine. The editor overlay
(ImGui) is drawn in its own pass over the already-resolved image and is untouched (FR-007).

Because the change is entirely GPU state, there is **no new pure logic** and the headless suite is
unchanged; the feature is verified by the run walkthrough.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: **none new.** SDL3-CS `3.4.14.1` already exposes everything required:
`SDL.GPUSampleCount` (`SampleCount1/2/4/8`), the resolve fields on `GPUColorTargetInfo`
(`ResolveTexture`, `ResolveMipLevel`, `ResolveLayer`, `CycleResolveTexture`) and
`GPUDepthStencilTargetInfo`, and `SDL.GPUTextureSupportsSampleCount` for the fail-loudly check
(FR-009). No shader changes.

**Storage**: none. Two transient GPU render targets (colour + depth), created once and recreated only
when the render extent changes; nothing persisted, no authored format changes.

**Testing**: no new pure logic — MSAA is GPU pipeline/target state, so the headless suite is unchanged
and must stay green (`dotnet test`, FR-012). Verification is the interactive walkthrough (research D5):
outline boundaries smooth, overlapping-surface edges smooth, cutout edges clean, overlay unaffected.

**Target Platform**: Linux x64 desktop, SDL3 + SDL_gpu (Vulkan backend).

**Project Type**: single-project native desktop game with a docked ImGui editor.

**Performance Goals**: hold the port's existing 60 FPS cadence. MSAA adds GPU fill cost and target
memory (4 samples on colour and depth) but **no per-frame CPU allocation** (FR-008) — the targets are
created once and reused. No explicit frame-rate budget is set this phase (spec Clarifications, Q4).

**Constraints**: fixed **4×** sample count (FR-001, FR-010); resolve the colour into the swapchain
(FR-002); every scene-pass pipeline at 4× — cel, outline, static, `Ui`, `DebugDraw` (FR-003); the depth
target multisampled so overlapping-surface edges are smoothed (FR-004); alpha-cutout `discard` still
per-sample (FR-005); the outline's depth bias preserved (FR-006); the overlay composited over the
**resolved** image and not multisampled (FR-007); no per-frame allocation (FR-008); **fail loudly** if
the device cannot provide 4× (FR-009); specs 040/041 appearance and all existing behaviour/tests
unchanged (FR-011, FR-012, FR-013).

**Scale/Scope**: `render/Gpu.fs` gains the multisampled colour texture (beside the depth texture) and
its recreate-on-resize path, renames `ensureDepthTexture` to `ensureSceneTargets`, makes the depth
texture 4×, sets the sample count on the cel/outline/static
pipelines, and checks sample-count support at init; `render/Ui.fs` and `render/DebugDraw.fs` set their
pipelines to 4×; `Engine.fs` points the scene colour target at the multisampled texture and sets the
swapchain as its resolve target; `docs/RENDERING_NOTES.md` gains a section. No new pass, no new shader,
no new dependency.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one more offscreen colour target and a sample-count change on pipelines that already exist. No new pass, no new shader, no new dependency, no settings surface (the count is a constant, FR-010). The resolve reuses SDL_gpu's built-in `ResolveTexture`. |
| II. Fail Loudly | Pass — the feature's one failure path is deliberate and required: if the device cannot provide 4×, initialisation fails with a clear error (FR-009) rather than silently rendering without AA. No fallback branch is added. |
| III. Spec-First | Pass — `specs/042-multisample-antialiasing/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — the added state is two GPU handles + an extent on the existing `Gpu` record (the depth texture's pattern); no object graph, no per-frame allocation. |
| V. Minimise GC Pressure | Pass — the targets are created once and recreated only on resize; the frame allocates nothing new (FR-008). |
| N-V. Testing is the Authoring Tool | Not engaged — narrative subsystem, untouched. |

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

None. A second offscreen target is inherent to multisampling (the depth target already exists at the
same place); there is no new abstraction to justify.

## Project Structure

### Documentation (this feature)

```text
specs/042-multisample-antialiasing/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── msaa.md          # Phase 1 output — the targets, sample counts and resolve wiring
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
render/
├── Gpu.fs               # CHANGED: add the 4× multisampled colour texture beside the depth texture
│                        #   (now also 4×); recreate both on resize; set SampleCount4 on the cel,
│                        #   outline and static pipelines; check `SDL.GPUTextureSupportsSampleCount`
│                        #   at create and fail loudly (FR-009).
├── Ui.fs                # CHANGED: the 2D pipeline's multisample state → SampleCount4.
└── DebugDraw.fs         # CHANGED: the debug-line pipeline's multisample state → SampleCount4.
Engine.fs                # CHANGED: point the scene colour target at the multisampled texture and set
                         #   the acquired swapchain as its resolve target; ensure both targets by size.
state/
└── Screen.fs            # UNCHANGED: `ScenePass` already carries the colour/depth targets; the resolve
                         #   rides in the `GPUColorTargetInfo`.
docs/
└── RENDERING_NOTES.md   # CHANGED: a new section on the multisampled scene pass and the resolve.
```

**Structure Decision**: the change lives on the **scene pass** the engine already begins every frame
(`state/Screen.fs` `ScenePass` → `screen/*Screen.fs` → `SDL.BeginGPURenderPass`). The multisampled
targets are owned by `render/Gpu.fs` exactly as the depth texture is today, so the engine keeps its
single `ensure`-by-size call and screens keep receiving plain `ColorTargets`/`DepthTarget`. No screen,
shader or world module changes.

## Phase 0: Research

See [research.md](./research.md). It settles that MSAA + resolve is the method (and FXAA/TAA/SSAA are
excluded); that SDL_gpu has no multisampled swapchain so the offscreen target is required; the fixed 4×
count; that **every** pipeline rendering into the scene must share the count; the depth-must-be-
multisampled point; the overlay-after-resolve ordering; and the fail-loudly support check.

## Phase 1: Design

- [data-model.md](./data-model.md) — the new `Gpu` fields (the multisampled colour texture), the fixed
  sample count, and the colour/depth target descriptors with the resolve.
- [contracts/msaa.md](./contracts/msaa.md) — the surfaces that change (`Gpu`, `Ui`, `DebugDraw`,
  `Engine`) and what does not (shaders, screens, `ScenePass`), and the invariants they must hold.
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite and the run walkthrough
  (outline smoothness, overlapping-surface edges, cutout edges, overlay, resize).

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — two targets, a sample count on existing pipelines, one resolve; no new pass/shader/dependency/setting. |
| II. Fail Loudly | Pass — an unsupported 4× is a clear init error (FR-009); no fallback. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — two handles + an extent on the existing `Gpu` record; nothing per frame. |
| V. Minimise GC Pressure | Pass — created once, recreated only on resize (FR-008). |
| N-V. Testing is the Authoring Tool | Not engaged. |
