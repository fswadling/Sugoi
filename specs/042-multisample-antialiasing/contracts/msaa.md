# Contract: Multisample Anti-Aliasing

The surfaces this feature touches, and what explicitly does not change. Everything is on the **scene
pass** the engine already begins every frame; no shader, screen or world module changes.

## The core invariants

1. **One sample count.** `MsaaTexture`, `DepthTexture` and every scene-pass pipeline use `SampleCount4`.
2. **One colour format.** `MsaaTexture.Format`, the swapchain format and each scene pipeline's
   `ColorTargetDescription.Format` are the same.
3. **The resolve is the only new write.** The scene renders to `MsaaTexture`; the swapchain is the
   resolve destination; the overlay loads the resolved image.
4. **No per-frame allocation.** Both targets are created once and recreated only when the extent changes.
5. **Fail loudly.** An unsupported 4× is a clear initialisation error — never a silent downgrade.

## `render/Gpu.fs` — CHANGED

- **New constant**: `SampleCount = SDL.GPUSampleCount.SampleCount4` (FR-001, FR-010), used by both
  targets and all three pipelines here.
- **`Gpu` record**: add `MsaaTexture : Texture` beside `DepthTexture`/`DepthW`/`DepthH` (data-model.md).
- **`createDepthTexture`** → creates the depth texture at `SampleCount`; a new `createMsaaTexture`
  creates the colour target in `SwapchainFormat`, `ColorTarget` usage, `SampleCount`, `1` layer/level.
- **`ensureDepthTexture`** (rename to reflect both) → recreates **both** `MsaaTexture` and `DepthTexture`
  when the extent changes, releasing the old handles first; a no-op when the extent is unchanged
  (FR-008).
- **`depthTargetInfo`** → `Texture` is the (now-multisampled) depth texture; the target carries no
  resolve (the struct has no resolve fields in this binding) — depth is never resolved.
- **`createActorPipeline`** and **`createStaticPipeline`** → `MultisampleState.SampleCount` becomes
  `SampleCount` (cel, outline and static alike — FR-003). Every other field is unchanged, including the
  outline's depth bias (FR-006).
- **`create`** → before building the pipelines, assert 4× support for the swapchain colour format and the
  depth format via `SDL.GPUTextureSupportsSampleCount`; `failwithf` a clear message if either is
  unsupported, and fail loudly if a scene-target handle is null at creation (FR-009).
- **`destroy`** → release `MsaaTexture` beside `DepthTexture`.

```fsharp
// The multisampled colour target the scene renders into; the engine resolves it to the swapchain.
val createMsaaTexture  : device:Device -> format:SDL.GPUTextureFormat -> w:uint32 -> h:uint32 -> Texture
val ensureSceneTargets : g:byref<Gpu> -> w:uint32 -> h:uint32 -> unit
val depthTargetInfo    : g:Gpu -> SDL.GPUDepthStencilTargetInfo
```

## `render/Ui.fs` — CHANGED

The 2D layer's pipeline `MultisampleState.SampleCount` becomes `SampleCount` (it draws into the scene
pass, so it must match — FR-003). Its blend state, depth state and target format are unchanged, so its
alpha blending and ordering are preserved (spec 042 edge case).

## `render/DebugDraw.fs` — CHANGED

The debug-line pipeline `MultisampleState.SampleCount` becomes `SampleCount` (same reason — FR-003).
Depth state and line primitive type unchanged.

## `Engine.fs` — CHANGED

- Call the scene-targets ensure (both colour and depth) by extent, as the depth texture is ensured today.
- Set the reused `sceneTargets.[0]`'s **store op to `SDL.GPUStoreOp.Resolve`** (once, where its
  `LoadOp`/clear colour are set). This is what performs the resolve: setting `ResolveTexture` alone does
  not, and with a plain `Store` the swapchain is never written and the frame is black (research D3).
- Point `sceneTargets.[0]` at `Gpu.MsaaTexture` and set its resolve destination to the
  acquired swapchain texture:

  ```fsharp
  do Gpu.ensureSceneTargets &shared.Gpu w h
  let mutable depthTarget = Gpu.depthTargetInfo shared.Gpu
  sceneTargets.[0].Texture <- shared.Gpu.MsaaTexture
  sceneTargets.[0].ResolveTexture <- swapchainTexture
  sceneTargets.[0].ResolveMipLevel <- 0u
  sceneTargets.[0].ResolveLayer <- 0u
  do ScreenManager.renderScene &manager { Cmd = cmd; ColorTargets = sceneTargets; DepthTarget = depthTarget }
  ```

- The overlay pass is **unchanged**: it still loads the (now resolved) swapchain texture.

## `state/Screen.fs` — UNCHANGED

`ScenePass` already carries `ColorTargets : SDL.GPUColorTargetInfo array` and `DepthTarget`; the resolve
rides inside the colour target. No signature changes; screens are untouched.

## Explicitly unchanged

- **All shaders** — `mesh.vert`, `cel.frag`, `outline.frag`, `static.*`: MSAA changes target and
  pipeline state, not shader code. Alpha `discard` keeps per-sample coverage (FR-005).
- **Screens** (`GameplayScreen`, `ScreenManager`, `MainMenuScreen`) — they receive the same `ScenePass`;
  the menu's clear-only pass resolves the clear the same way (FR-013).
- **The overlay (`ImGuiSdl3`)** — its pass loads the resolved swapchain and is not multisampled
  (FR-007).
- **`fsharp-game.fsproj`, the test project, `core/`, `world/`** — no new files, no new dependencies, no
  new tests; the suite is unchanged (FR-012).
