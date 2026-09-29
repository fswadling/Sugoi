# Contract: Actor Outline

The interfaces this feature touches, and what explicitly does not change. Everything is on the actor
render path (spec 040); the environment/prop static path is untouched.

## `shaders/mesh.vert` — CHANGED (shared by both pipelines)

Adds one vertex input and one vertex uniform, and applies the extrusion. Resource counts go from
(2 UBO, 2 storage, 0 samplers) to **(3 UBO, 2 storage, 0 samplers)**.

```glsl
layout(location = 5) in vec3 aSmoothNormal;              // NEW — the derived extrusion direction

layout(set = 1, binding = 2) uniform OutlineParams {      // NEW — vertex uniform slot 2
    vec4 uOutline;   // x = world-space width (0 = off), yzw unused
} outline;
```

After the existing skinning/morphs, the smoothed normal is transformed like the surface normal
(bone blend + `normalMatrix`, plus the same morph normal deltas), and it offsets the world position
before projection by a constant world distance:

```glsl
vec4 clip = perDraw.viewProj * world;
if (outline.uOutline.x > 0.0) {
    vec3 worldDir = normalize(mat3(perDraw.normalMatrix) * mat3(boneBlended) * smoothNormal);
    world.xyz += worldDir * outline.uOutline.x;
    clip = perDraw.viewProj * world;
}
gl_Position = clip;
```

The surface pass pushes `uOutline.x = 0`, so the branch is inert for it (research D5).

## `shaders/outline.frag` — NEW

```glsl
#version 450
layout(location = 0) in vec3 vWorldPos;   // unused; kept to match mesh.vert's outputs
layout(location = 1) in vec3 vWorldNormal;
layout(location = 2) in vec2 vUV;
layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform sampler2D uAlbedoMap;   // 1 sampler

const vec3  OUTLINE_COLOR = vec3(0.05, 0.05, 0.06);
const float ALPHA_CUTOFF  = 0.5;
```

Body: `discard` when `texture(uAlbedoMap, vUV).a < ALPHA_CUTOFF` (the white placeholder means an
opaque material never discards), then `outColor = vec4(OUTLINE_COLOR, 1.0)`. **0 uniform buffers, 0
storage buffers, 1 sampler** (research D6).

## `render/Gpu.fs` — CHANGED

- `createCelPipeline`: vertex input becomes **6 attributes / 52-byte stride** (adds location 5,
  offset 40); `mesh.vert` is loaded with **(3, 2, 0)**. Rasteriser, depth and colour target unchanged.
- `createOutlinePipeline` — **NEW**:
  - `mesh.vert` **(3, 2, 0)** + `outline.frag.spv` **(0, 0, 1)**;
  - same 6-attribute / 52-byte vertex layout;
  - `CullMode = Front`, `FrontFace = CounterClockwise`;
  - `EnableDepthTest = true`, `EnableDepthWrite = true`, `CompareOp = LessOrEqual`;
  - **depth bias enabled**: `EnableDepthBias = true`, a small `DepthBiasConstantFactor` and
    `DepthBiasSlopeFactor` (guards against hull bleed-through, FR-007);
  - opaque (default blend).
- `Gpu` record: add `OutlinePipeline : Pipeline`; `create` builds it; `destroy` releases it.

## `render/Mesh.fs` — CHANGED

- `interleave` writes **52-byte** vertices (adds the smoothed normal at offset 40).
- `create` gains a `smoothNormals : float32 array` argument, interleaved with the rest.
- `draw` (cel surface): signature unchanged, but it now also pushes vertex uniform slot 2 with
  `uOutline = (0, 0, 0, 0)`.
- `drawOutline` — **NEW**:

  ```fsharp
  val drawOutline :
      m:Mesh -> renderPass:Gpu.RenderPass -> cmd:Gpu.CommandBuffer -> pipeline:Gpu.Pipeline ->
      viewProjection:Matrix4x4 -> model:Matrix4x4 -> widthWorld:float32 -> unit
  ```

  Pushes `PerDraw` (slot 0) and `uOutline` (slot 2: the world width in x), binds
  the vertex buffer, the index buffer and **one** sampler (`sub.Material.Bindings.[0]`), and issues the
  indexed draws. It pushes **no** fragment uniform (the outline shader has none).

## `core/Iqm.fs` — CHANGED

New pure function (see [data-model.md](../data-model.md)):

```fsharp
val smoothNormals : positions: float32 array -> normals: float32 array -> float32 array
```

## `world/Assets.fs` — CHANGED

`loadActors` computes `Iqm.smoothNormals model.Positions normals` and passes it to `Mesh.create`. The
`normals` array it already defaults when absent is reused for the smoothing.

## `world/ActorRender.fs` — CHANGED

`draw` gains the outline pipeline and issues **two** passes per actor after the shared bone/morph
bindings (research D8):

```fsharp
val draw :
    r:ActorRender -> renderPass:Gpu.RenderPass -> cmd:Gpu.CommandBuffer ->
    pipeline:Gpu.Pipeline -> outlinePipeline:Gpu.Pipeline ->
    viewProjection:Matrix4x4 -> model:Matrix4x4 -> light:Light -> unit
```

1. `Mesh.drawOutline … outlinePipeline (upAxisFix * model) outlineWidth`;
2. `Mesh.draw … pipeline (upAxisFix * model) light (faceLightDir model)` (unchanged surface pass).

The outline width is a module constant (`outlineWidth`, world units).

## `screen/GameplayScreen.fs` — CHANGED

The actor draw passes `shared.Gpu.OutlinePipeline` in addition to the cel pipeline. One call site.

## `fsharp-game.fsproj` — CHANGED

`CopyShaders`: add `shaders/outline.frag.spv`.

## Explicitly unchanged

- **Environments and props**: `static.vert`/`static.frag`, `StaticMesh`, `InstanceBuffer`,
  `EnvironmentBatch` — the static path is never outlined (FR-002).
- **`shaders/cel.frag`** and the cel shading it produces (FR-010) — the outline is a separate pass that
  only adds geometry behind the actor.
- **`Texture.Material`** and the white placeholder — the outline reads `Bindings.[0]` like cel.
- **The actor/entity/zone records and all authored formats** — the smoothed normal is derived, not
  authored.
- **The UI / 2D layer, portraits, the editor** — untouched.
- **The scene pass structure** — still one render pass; the outline adds draws, not a pass or a target.
