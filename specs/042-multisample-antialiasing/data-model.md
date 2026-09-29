# Data Model: Multisample Anti-Aliasing

The feature adds no authored data and no pure logic. It adds **two transient GPU render targets** (a
multisampled colour target beside the existing depth target, which becomes multisampled), one **sample-
count constant**, and the **resolve wiring** that writes the multisampled colour down to the swapchain.
Everything is runtime GPU state owned by `render/Gpu.fs`.

## The `Gpu` record grows by one target

The depth texture already lives here (`DepthTexture`/`DepthW`/`DepthH`), recreated by extent. The
multisampled colour target joins it, and the depth texture becomes 4×. The extent fields remain the
shared scene extent.

| Field | Type | Change | Meaning |
|---|---|---|---|
| `DepthTexture` | `Texture` | **now 4×** | the multisampled depth target of the scene pass |
| `DepthW` / `DepthH` | `int` | unchanged | the scene extent both targets are recreated at |
| `MsaaTexture` | `Texture` | **NEW** | the 4× multisampled colour target the scene renders into |

`ensureDepthTexture` becomes the scene-targets ensure: it recreates **both** `MsaaTexture` and
`DepthTexture` when the extent changes (and only then — FR-008). `createDepthTexture` gains a sibling
`createMsaaTexture` in the swapchain format.

## The sample-count constant

```fsharp
// The scene is rendered at this many samples per pixel (spec 042, FR-001/FR-010).
let SampleCount = SDL.GPUSampleCount.SampleCount4
```

One constant, used by every scene-pass pipeline and both targets (FR-010). No settings surface.

## The multisampled colour target

| Property | Value |
|---|---|
| Type | `TextureType2D` |
| Format | `SwapchainFormat` (the colour target format must match the resolve destination) |
| Usage | `ColorTarget` |
| Width / Height | the scene extent |
| `LayerCountOrDepth` / `NumLevels` | `1` / `1` |
| `SampleCount` | `SampleCount4` |

Created once per extent; released on resize and at shutdown, beside the depth texture.

## The multisampled depth target

| Property | Value | Change |
|---|---|---|
| Format | `D32Float` | unchanged |
| Usage | `DepthStencilTarget` | unchanged |
| `SampleCount` | `SampleCount4` | **was `SampleCount1`** |

Nothing samples depth after the pass, so its resolve is left unused.

## The scene colour target descriptor (the resolve)

`Engine.fs` fills the reused `sceneTargets.[0]` each frame. The multisampled texture becomes the colour
target and the acquired swapchain becomes its **resolve destination**:

| Field | Value | Change |
|---|---|---|
| `Texture` | `Gpu.MsaaTexture` | **was the swapchain** |
| `ResolveTexture` | the acquired swapchain texture | **NEW** |
| `ResolveMipLevel` / `ResolveLayer` | `0` / `0` | NEW |
| `LoadOp` / `StoreOp` | `Clear` / **`Resolve`** | the resolve is driven by the store op: with the multisampled colour target, `Resolve` writes the resolved colour to `ResolveTexture` (FR-002) |
| `ClearColor` | the existing scene clear | unchanged |

The resolve happens automatically when the pass ends; the swapchain then holds the resolved single-sample
image, which the overlay pass loads (FR-002, FR-007). `state/Screen.fs`'s `ScenePass` is unchanged — it
already carries the `ColorTargets`/`DepthTarget` these descriptors live in.

## The scene depth target descriptor

| Field | Value | Change |
|---|---|---|
| `Texture` | `Gpu.DepthTexture` (now 4×) | unchanged field, new sample count |
| `MipLevel` / `Layer` | `0` / `0` | unchanged |

The depth target is **not** resolved — SDL3-CS `3.4.14.1`'s `GPUDepthStencilTargetInfo` has no resolve
fields, and nothing samples depth after the pass (FR-004 needs it multisampled, not resolved).

## Pipelines: every scene-pass pipeline shares the sample count

A graphics pipeline is only valid for a pass whose targets have the **same** sample count, so all of
these move to `SampleCount4` together (FR-003). They all already draw into the one scene pass.

| Pipeline | Location | Change |
|---|---|---|
| cel (actor surface) | `render/Gpu.fs` `createActorPipeline` | `SampleCount1` → `SampleCount4` |
| outline (actor hull) | `render/Gpu.fs` `createActorPipeline` | `SampleCount1` → `SampleCount4` |
| static (env/props) | `render/Gpu.fs` `createStaticPipeline` | `SampleCount1` → `SampleCount4` |
| debug lines | `render/DebugDraw.fs` | `SampleCount1` → `SampleCount4` |
| 2D layer | `render/Ui.fs` | `SampleCount1` → `SampleCount4` |
| overlay (ImGui) | `ImGuiSdl3` backend | **unchanged** — it loads the resolved swapchain, not a multisampled target |

## Invariants

- **Sample counts agree**: `MsaaTexture.SampleCount` = `DepthTexture.SampleCount` = every scene-pass
  pipeline's `MultisampleState.SampleCount` = 4×. A mismatch is an invalid pass.
- **Formats agree**: `MsaaTexture.Format` = `SwapchainFormat` = each scene pipeline's
  `ColorTargetDescription.Format`; the resolve destination shares the format and size.
- **Single-sample edges stay single-sample**: the overlay is never multisampled, so its text is crisp
  (FR-007).
- **Per-sample cutouts**: the cel/outline fragment `discard` (specs 040/041) keeps per-sample coverage;
  the cut shape is unchanged and its edge is smoothed (FR-005).
- **Bias preserved**: the outline pipeline's depth bias (spec 041, FR-007) is untouched in value
  (FR-006).
- **No per-frame allocation**: both targets are created once and recreated only on extent change
  (FR-008).
- **Fail loudly**: `SDL.GPUTextureSupportsSampleCount` is checked for both formats at init, and a null
target handle (from a failed creation or resize) is a loud failure — an unsupported 4× or a failed
creation is a clear error, never a silent single-sample frame (FR-009).
