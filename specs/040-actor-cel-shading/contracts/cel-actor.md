# Contract: Cel Actor Rendering

The interfaces this feature touches, and what explicitly does not change. Everything here is on the
**actor render path** — the shader pair only actors use (`StaticPipeline` serves environments/props).

## `shaders/cel.frag` — NEW (replaces `shaders/mesh.frag`)

The actor fragment shader. Paired with the **existing** `shaders/mesh.vert.spv` (skinning + morphs,
unchanged): a vertex-stage change would be needed only if the cel shader required new vertex outputs,
and it does not — it reads `vWorldNormal` and `vUV`.

```glsl
#version 450

// Locations match mesh.vert's outputs exactly (0 vWorldPos, 1 vWorldNormal, 2 vUV);
// vWorldPos is unused by cel but kept for a matching interface.
layout(location = 0) in vec3 vWorldPos;
layout(location = 1) in vec3 vWorldNormal;
layout(location = 2) in vec2 vUV;
layout(location = 0) out vec4 outColor;

layout(set = 2, binding = 0) uniform sampler2D uAlbedoMap;      // FR-005

layout(set = 3, binding = 0) uniform CelUniform {                // 80 bytes, std140
    vec4 uBaseColor;    // rgb + alpha
    vec4 uLightDir;     // xyz toward the scene sun               (FR-004)
    vec4 uLightColor;   // xyz
    vec4 uAmbient;      // xyz
    vec4 uFace;         // xyz = idealized face light dir; w = isFace (FR-007)
} cel;

const float RAMP_THRESHOLD = 0.5;   // FR-002, FR-012
const float RAMP_SOFTNESS  = 0.05;  // FR-002, FR-012
const float ALPHA_CUTOFF   = 0.5;   // FR-006
```

Shading (the contract, not literal code):

- `albedo = srgbToLinear(texture(uAlbedoMap, vUV).rgb) * uBaseColor.rgb` (FR-005).
- `discard` when `texture(...).a * uBaseColor.a < ALPHA_CUTOFF` (FR-006).
- **Band direction** `L = normalize(uFace.w > 0.5 ? uFace.xyz : uLightDir.xyz)` — face meshes take the
  idealized light, everything else the scene sun (FR-007, FR-009).
- `band = smoothstep(RAMP_THRESHOLD - RAMP_SOFTNESS, RAMP_THRESHOLD + RAMP_SOFTNESS, dot(N,L)*0.5 + 0.5)`
  (FR-002).
- `color = albedo * (uAmbient.rgb + uLightColor.rgb * band)` — **additive** (FR-003; the clarification
  of 2026-09-29).
- Output gamma-encoded once: `outColor = vec4(pow(color, vec3(1.0/2.2)), 1.0)`.

Sampler / storage counts, as declared to `loadShader`:

| Stage | uniform buffers | storage buffers | samplers |
|---|---|---|---|
| `mesh.vert.spv` (unchanged) | 2 | 2 | 0 |
| `cel.frag.spv` (new) | 1 | 0 | **1** |

## `render/Gpu.fs` — CHANGED

- `createMeshPipeline` → **`createCelPipeline`**: loads `mesh.vert.spv` (2, 2, 0) and
  **`cel.frag.spv` (1, 0, 1)**; the vertex input layout, rasteriser (cull none), depth state and
  colour target are **unchanged** from the current mesh pipeline.
- The `Gpu` record field `MeshPipeline : Pipeline` → **`CelPipeline : Pipeline`**; `create` and
  `destroy` updated to match.

## `render/Mesh.fs` — CHANGED

- `Submesh` gains `IsFace : bool` (see [data-model.md](../data-model.md)).
- New pure predicate `isFaceMeshName : string -> bool`.
- `draw` signature: **drops** `cameraPos`, **gains** `faceDir`:

  ```fsharp
  val draw :
      m:Mesh -> renderPass:Gpu.RenderPass -> cmd:Gpu.CommandBuffer -> pipeline:Gpu.Pipeline ->
      viewProjection:Matrix4x4 -> model:Matrix4x4 -> light:Light -> faceDir:Vector3 -> unit
  ```

- Per submesh it now: writes the 80-byte cel uniform (base colour, light dir/colour/ambient, and
  `(faceDir.xyz, if sub.IsFace then 1 else 0)`), pushes **80** bytes at fragment slot 0, and binds
  **one** sampler (`sub.Material.Bindings.[0]`). The vertex `PerDraw` push (192 bytes) and the index
  draw are unchanged.

## `world/ActorRender.fs` — CHANGED

- New pure helper `faceLightDir : Matrix4x4 -> Vector3` (see [data-model.md](../data-model.md)).
- `draw` computes `faceDir = faceLightDir model` (the raw entity matrix, **before** the existing
  `upAxisFix` multiply) and passes it to `Mesh.draw`. Its `cameraPos` parameter is dropped (unused now).

## `world/Assets.fs` — CHANGED

- `loadActors` sets `IsFace = Mesh.isFaceMeshName mesh.Name` on each submesh; the no-meshes fallback
  submesh sets `IsFace = false`. No other change.

## `screen/GameplayScreen.fs` — CHANGED

- The actor draw passes `shared.Gpu.CelPipeline` (was `shared.Gpu.MeshPipeline`). One identifier.

## `fsharp-game.fsproj` — CHANGED

- `CopyShaders`: `shaders/mesh.frag.spv` → **`shaders/cel.frag.spv`**. (No compile-order change — no
  new module — unless a separate `test/CelTests.fs` is added, in which case the test project gains its
  entry.)

## `shaders/mesh.frag`, `shaders/mesh.frag.spv` — DELETED

The actor-only PBR fragment shader, unused after the swap (research D1). `shaders/mesh.vert(.spv)`
stays. The new `shaders/cel.frag.spv` is compiled with `glslc` and committed, like every other `.spv`.

## Explicitly unchanged

- **Environments and props**: `shaders/static.vert`/`static.frag`, `render/StaticMesh.fs`,
  `render/InstanceBuffer.fs`, `world/EnvironmentBatch.fs` — the PBR static path is untouched (FR-013).
- **`shaders/mesh.vert`** and the skinning/morph vertex stage (specs 023, 037) — FR-010.
- **`render/Texture.fs`** (`Material`, `Light`, the white placeholder) — unchanged; cel reads a subset
  of the same data.
- **The actor, entity and zone records**, the systems, and all authored formats — unchanged; the face
  flag is derived from the IQM mesh name at load, and the face light from the existing transform.
- **The UI / 2D layer, portraits, the editor** — untouched.
- **The draw-call count and pass structure**: the actor is still one indexed draw per submesh, inside
  the same scene pass; no pass or pipeline is added.
