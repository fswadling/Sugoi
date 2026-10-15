# Contract: Skydome Shaders

The interface between the F# skydome render path (`render/Skydome.fs`, `render/Gpu.fs`) and the two GLSL
shaders. Both are compiled with `glslc` and checked in as `.spv`; the binding numbers follow SDL_gpu's
fixed resource-set convention (`docs/RENDERING_NOTES.md` §4).

## Pipeline resource counts (`Gpu.createSkydomePipeline`)

| Stage | Uniform buffers | Storage buffers | Samplers |
|-------|-----------------|-----------------|----------|
| Vertex | 1 | 0 | 0 |
| Fragment | 1 | 0 | 0 |

- Vertex layout: one buffer, slot 0, `Float3` position at offset 0 (12-byte stride).
- Primitive: `TriangleList`. Culling: **None**.
- Depth-stencil: target present (`HasDepthStencilTarget = true`, format = the scene depth format) but
  **`EnableDepthTest = false`, `EnableDepthWrite = false`** (the pass has a depth target; the pipeline must
  match its format, but the sky neither tests nor writes depth).
- Multisample: the scene sample count (spec 042).
- Colour target: the swapchain format.

## `shaders/skydome.vert`

```glsl
#version 450
layout(location = 0) in vec3 aPosition;
layout(location = 0) out vec3 vLocalPos;
layout(set = 1, binding = 0) uniform PerDraw { mat4 viewProj; } perDraw;
void main() {
    vLocalPos = aPosition;
    gl_Position = perDraw.viewProj * vec4(aPosition, 1.0);
}
```

## `shaders/skydome.frag`

```glsl
#version 450
layout(location = 0) in vec3 vLocalPos;
layout(location = 0) out vec4 outColor;
layout(set = 3, binding = 0) uniform SkyUniform {
    vec4 uZenith;    // linear rgb
    vec4 uHorizon;   // linear rgb
    vec4 uLightDir;  // xyz = direction toward the sun (from the zone's light)
} sky;
```

Behaviour:
- `dir = normalize(vLocalPos)`.
- `color = uHorizon`; if `dir.y > 0`, `color = mix(uHorizon, uZenith, smoothstep(0.0, 0.4, dir.y))`.
- Sun: `d = max(dot(dir, normalize(uLightDir.xyz)), 0.0) * step(0.0, dir.y)`;
  `color = mix(color, sunCol, smoothstep(0.997, 0.999, d))` with `sunCol` a fixed warm white — a single
  simple disc, no rings or glare.
- `outColor = vec4(pow(color, vec3(1.0/2.2)), 1.0)`.

## Uniform payload (F# side)

A single `byte array` (pushed once per frame with `PushGPUFragmentUniformData`, slot 0):

| Offset | Content |
|--------|---------|
| 0 | `Vector4(Zenith, 0)` |
| 16 | `Vector4(Horizon, 0)` |
| 32 | `Vector4(lightDirection, 0)` |

The vertex uniform is the sky-only view-projection matrix (`render/Camera` forward + a projection whose far
plane is just past the dome radius), pushed with `PushGPUVertexUniformData`, slot 0.

## Invariants

- The sphere is drawn **first** in the scene pass, so geometry drawn later overwrites it.
- The sky is drawn only when `zone.Skydome.Enabled` (FR-006); otherwise nothing is drawn and the frame is
  unchanged (FR-005).
- `lightDirection` is the active zone's light direction (`Texture.toLight zone.Light`), so the sun and the
  scene's lighting agree (FR-004).
