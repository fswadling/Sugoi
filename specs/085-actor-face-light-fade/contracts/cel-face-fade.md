# Contract: Cel Face Light Fade

The one interface this feature changes is the actor's fragment shading body. Everything else on the
actor render path — and the whole environment/prop/UI path — is explicitly unchanged. The parent
spec is `specs/040`; this contract refines its face behaviour without altering its band contract.

## `shaders/cel.frag` — CHANGED (body only)

The uniforms, the existing ramp constants (`RAMP_THRESHOLD`, `RAMP_SOFTNESS`, `ALPHA_CUTOFF`), the
albedo read, the alpha-test discard and the non-face path are **unchanged**. Only the face path
gains a switch between computing `band` and composing the colour, plus one new named constant
(`FACE_FADE_SOFTNESS`).

Contract (the shape, not literal required code):

- `isFace = cel.uFace.w > 0.5` (unchanged gate).
- `band = smoothstep(RAMP_THRESHOLD - RAMP_SOFTNESS, RAMP_THRESHOLD + RAMP_SOFTNESS, dot(N, L)*0.5 + 0.5)`
  is computed exactly as before (FR-001; `specs/040` FR-002).
- **New, face only** — switch `band` by the sun's azimuth (FR-002, FR-003):

  ```glsl
  const float FACE_FADE_SOFTNESS = 0.05;   // half-width of the switch, in the azimuthal dot

  if (isFace) {
      vec3 faceH = vec3(cel.uFace.x, 0.0, cel.uFace.z);
      vec3 sunH  = vec3(cel.uLightDir.x, 0.0, cel.uLightDir.z);
      float faceLen = length(faceH);
      float sunLen  = length(sunH);
      float toward  = (faceLen > 1e-4 && sunLen > 1e-4)
          ? dot(faceH, sunH) / (faceLen * sunLen)   // 1 = facing the sun, -1 = facing away
          : 1.0;                                    // no azimuth to compare: full front light
      band *= smoothstep(-FACE_FADE_SOFTNESS, FACE_FADE_SOFTNESS, toward);
  }
  ```

- `color = albedo * (cel.uAmbient.rgb + cel.uLightColor.rgb * band)` is then composed **unchanged**
  (FR-004). Because `band` is scaled by a step, the lit band is the full front light (`factor = 1`)
  while the face points more toward the sun than away, and the shadow band's ambient-only tone
  (`factor = 0`) once it points past square — a two-tone switch eased by the small `smoothstep` at the
  90° crossing, with the shadow band itself unaffected.

Behavioural guarantees:

- The band *direction* and the ramp are untouched — the boundary stays fixed relative to the face
  whatever the heading and sun (FR-001).
- The band *colours* still come from the scene sun's colour and the ambient term (FR-004;
  `specs/040` FR-008).
- Only `Face` meshes are affected; every other mesh keeps `L = normalize(cel.uLightDir.xyz)` and the
  plain band (FR-005; `specs/040` FR-004, FR-009).
- The fade is a fixed expression — no uniform, no authored parameter (FR-006).

## `shaders/cel.frag.spv` — RECOMPILED

Recompiled from the edited `cel.frag` with `glslc` (the same plain invocation used for every other
`.spv`, byte-reproducible — verified before the change), and committed alongside the source.

## Explicitly unchanged

- **The cel uniform block** (`set = 3, binding = 0`, 80 bytes) — no member added; the fade uses
  existing data (FR-006/FR-007).
- **`render/Mesh.fs`** — `Mesh.draw`'s uniform write, the fragment push and the one-sampler bind are
  untouched; no new parameter.
- **`world/ActorRender.fs`** — `faceLightDir` and `ActorRender.draw` are untouched.
- **`shaders/mesh.vert(.spv)`** — the vertex stage (skinning, morphs, outline extrusion) is untouched.
- **`render/Gpu.fs`** — the same `Gpu.CelPipeline` serves the edited shader; pipeline creation,
  cull/depth state and the `CopyShaders` list (`cel.frag.spv` already listed) are unchanged.
- **`screen/GameplayScreen.fs`** — the actor draw call is unchanged.
- **`specs/040-actor-cel-shading/`** — referenced, not edited.
- **`docs/RENDERING_NOTES.md` §13** — CHANGED: gains the fade bullet (documentation of this change).
