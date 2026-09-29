# Phase 0 Research: Multisample Anti-Aliasing

The method is settled by the artefact and by the engine's capabilities; the work is in the details the
engine does not pin down — where the multisampled target lives, which pipelines must change, and how the
resolve reaches the swapchain. Sources are in the spec's Origin.

## Decision 1 — MSAA + resolve, not a post-process filter

- **Decision**: render the 3D scene into a **4× multisampled colour target** (with a multisampled depth
  target), then **resolve** the colour into the acquired swapchain image. No FXAA/TAA, no supersampling.
- **Rationale**: the artefact is **geometry-edge aliasing** — the outline (spec 041) and every silhouette
  in the frame are stair-stepped because the frame is drawn at one sample per pixel. MSAA is exactly the
  tool for that: it takes several coverage/depth samples per pixel at the polygon edges and averages them
  on resolve, smoothing edges — **including depth-tested edges between overlapping surfaces** and the
  alpha-cutout edges of the hair/eye cards — with no blur to the interior and no extra pass.
- **Alternatives considered**: **FXAA** (a full-screen filter: cheap, but blurs texture detail, misses
  sub-pixel geometry, and needs a post-process pass this port does not have); **TAA** (needs a history
  buffer and reprojection infrastructure the port does not have — new scope); **SSAA** (render at 2× and
  downscale: strictly more expensive than MSAA for the same edge result on this scene).

## Decision 2 — The fixed sample count is 4×

- **Decision**: the sample count is a **constant of 4×** (`SDL.GPUSampleCount.SampleCount4`), for both
  the colour and depth targets and every scene-pass pipeline.
- **Rationale**: 4× is the standard quality/cost point: it removes the visible staircase on a
  high-contrast line, is near-universally supported on desktop GPUs, and keeps the target
  memory modest. The spec's Clarifications (Q1) fixed this; FR-010 makes it a constant, so there is no
  settings surface to build or test.
- **Alternatives considered**: **8×** (diminishing visual gain on this line for roughly double the
  multisample memory and fill cost); **2×** (visible residual stair-stepping on high-contrast lines).

## Decision 3 — An offscreen multisampled colour target resolved to the swapchain

- **Decision**: create a **multisampled colour texture** in the swapchain format (`ColorTarget` usage),
  render the scene into it, and set the **acquired swapchain texture as its resolve target**
  (`GPUColorTargetInfo.ResolveTexture`); the **depth target is also 4×** (its resolve is unused).
- **Rationale**: SDL_gpu has **no multisampled swapchain** — the swapchain is single-sample and cannot be
  made multisampled — so multisampling must go through an offscreen target and a resolve, which is
  SDL_gpu's documented pattern ([`SDL_GPUColorTargetInfo`](https://wiki.libsdl.org/SDL3/SDL_GPUColorTargetInfo),
  [`SDL_BeginGPURenderPass`](https://wiki.libsdl.org/SDL3/SDL_BeginGPURenderPass)). The depth target must
  carry the same sample count as the colour target, or the pass is invalid; resolving depth is unnecessary
  because nothing samples it afterwards (FR-004 needs it multisampled, not resolved). SDL3-CS `3.4.14.1`
  exposes the resolve fields (`ResolveTexture`, `ResolveMipLevel`, `ResolveLayer`, `CycleResolveTexture`)
  on both target structs.
- **Alternatives considered**: **per-sample shading / a supersampled swapchain** (not offered by
  SDL_gpu); **resolving depth** (pointless work — the overlay does not sample depth); **a second scene
  pass for AA** (would double the draw calls for nothing MSAA does not already give).
- **The store op performs the resolve.** Setting `resolve_texture` alone is not enough in SDL_gpu: the
  multisampled colour target's `store_op` must be `SDL_GPU_STOREOP_RESOLVE` (or `_RESOLVE_AND_STORE`),
  or the resolve does not happen. `Resolve` writes the resolved colour to the swapchain and discards the
  multisample contents, which is exactly what the pass wants. (With a plain `Store` the frame is black —
  learnt the hard way.)

## Decision 4 — Every pipeline that draws into the scene must share the count

- **Decision**: set the multisample state to **4×** on all four scene-pass pipelines — the cel and
  outline actor pipelines and the static/environment pipeline (`render/Gpu.fs`), the debug-line pipeline
  (`render/DebugDraw.fs`), and the 2D layer's pipeline (`render/Ui.fs`).
- **Rationale**: a graphics pipeline is only valid for a render pass whose colour/depth targets have the
  **same sample count** as the pipeline's `GPUMultisampleState`. All five draw into the one scene pass
  (`DebugDraw`, the gizmo and the 2D `Ui` layer draw inside it, `screen/GameplayScreen.fs`), so all five
  must move to 4× together (FR-003). The overlay (ImGui) does **not**: it draws in its own pass that
  loads the resolved swapchain (research D6).
- **Alternatives considered**: **splitting the 2D layer into its own single-sample pass** (crisper 2D,
  but a second pass, a second target, and a rework of the frame — the spec's Clarifications, Q2, chose to
  let the shared pass smooth them instead); **leaving the static/`DebugDraw`/`Ui` pipelines at 1×**
  (invalid against a 4× target — the pass would fail).

## Decision 5 — Verification is the walkthrough; the headless suite is unchanged

- **Decision**: add **no** new unit tests; keep the existing suite green (`dotnet test`, FR-012), and
  verify the feature by the run walkthrough.
- **Rationale**: MSAA is entirely GPU pipeline/target state — target formats, sample counts, a resolve —
  and the test project cannot open a GPU device (`AGENTS.md`; only pure logic is unit-tested). There is no
  pure function to extract. This mirrors spec 041, whose GPU-observable behaviour was the walkthrough.
- **Alternatives considered**: **asserting the target descriptors in a headless test** (the structs are
  only constructible against a live device / are not the interesting behaviour); **a screenshot test**
  (the project has no such harness, and it would be new infrastructure for one feature).

## Decision 6 — The overlay is composited after the resolve

- **Decision**: leave the editor overlay exactly as it is — it begins its own pass that **loads the
  resolved swapchain image** (`Engine.fs`) and is not multisampled.
- **Rationale**: ImGui draws 2D text/lines with its own rendering; multisampling it would blur its text
  and re-resolve it for no benefit. Drawing after the resolve is already the frame's order, so no change
  is needed (FR-007).
- **Alternatives considered**: **drawing the overlay into the multisampled target** (blurs its text and
  needs the overlay's own pipeline moved to 4× for no gain).

## Decision 7 — Fail loudly when 4× is unavailable; own the targets in `Gpu`

- **Decision**: at initialisation, check `SDL.GPUTextureSupportsSampleCount` for the swapchain colour
  format and the depth format at 4×, and **fail with a clear error** if unsupported (FR-009). Create both
  multisampled targets in `render/Gpu.fs` (the depth texture's existing home), recreated together when the
  render extent changes (FR-008).
- **Rationale**: `AGENTS.md` / Constitution II — fail loudly, no just-in-case fallbacks. 4× is
  near-universal on desktop, so an unsupported device is an honest error, not a case to paper over with a
  silent single-sample path. Co-locating the colour target with the depth texture keeps the engine's
  single ensure-by-size call and adds no per-frame allocation.
- **Alternatives considered**: **silently falling back** to 1×/2× (a silent quality drop the spec's
  Clarifications, Q3, rejected); **probing support but degrading** (the same silent drop with extra
  branches).
