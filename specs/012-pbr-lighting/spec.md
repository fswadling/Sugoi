# Feature Specification: PBR + Lighting

**Feature Branch**: `012-pbr-lighting`

**Created**: 2026-09-05

**Status**: Draft

**Input**: User description: "stage 5 — replace the flat/Lambert shading with
metallic-roughness PBR (GGX), applied to everything: environments, props, and the
animated actor, with a directional sun + ambient light"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Static geometry renders with PBR materials (Priority: P1)

An environment or prop authored with glTF `pbrMetallicRoughness` data renders with
metallic-roughness shading: albedo, normal, and roughness maps (where present),
times the material's scalar factors (`baseColorFactor`, `roughnessFactor`,
`metallicFactor`), lit by a directional sun and ambient light. Materials without
texture maps fall back to their scalar factors, so an untextured model still
shades correctly.

**Why this priority**: it is the headline result of the phase. Until static
geometry shades with PBR, none of the phase's other work (actors, emissive,
normal-matrix correctness) has a base to stand on.

**Independent Test**: import `ArtRoom`; its walls/floor/furniture show textured
PBR shading (visible albedo + normal detail + roughness response) under the sun,
not flat Lambert.

**Acceptance Scenarios**:

1. **Given** a glTF material with a `baseColorTexture`, **When** it renders,
   **Then** the surface is shaded with that albedo (sRGB-decoded, multiplied by
   `baseColorFactor`).
2. **Given** a glTF material with a `normalTexture`, **When** it renders,
   **Then** surface detail is perturbed via tangent-space normal mapping.
3. **Given** a glTF material with `roughnessFactor` (and/or a
   `metallicRoughnessTexture`), **When** it renders, **Then** the specular
   highlight shape reflects that roughness.
4. **Given** a glTF material with only scalar factors and no textures, **When** it
   renders, **Then** it shades with `baseColorFactor`, `metallicFactor`, and
   `roughnessFactor` (no texture required).

---

### User Story 2 - The animated actor renders under the same PBR light (Priority: P2)

The skinned actor (phase 3) is shaded with the same metallic-roughness BRDF and
the same sun/ambient light as the environment. Each IQM mesh names an albedo
texture (its `Material` string, e.g. `_10.png`); that texture is sampled as the
base color where present, and meshes fall back to a default dielectric material
(metallic 0, roughness ~0.8, a sensible base color) otherwise.

**Why this priority**: the phase applies PBR "to everything"; the actor is the
second pipeline that must share the same lighting model so the scene looks
consistent.

**Independent Test**: spawn Akane alongside a PBR environment; the actor is lit by
the same sun/ambient with a plausible default material, and keeps skinning.

**Acceptance Scenarios**:

1. **Given** the Akane actor, **When** it renders, **Then** it is shaded with the
   metallic-roughness BRDF under the scene's sun/ambient (not the old flat
   Lambert).
2. **Given** the actor, **When** it animates, **Then** skinning continues to work
   and the PBR shading follows the skinned normals.

---

### User Story 3 - Emissive and factor-only materials behave correctly (Priority: P3)

Materials with an emissive texture/factor glow regardless of the sun, and models
whose nodes have non-uniform scale still light correctly (normals transformed by
the inverse-transpose normal matrix, not `mat3(model)`).

**Why this priority**: emissive is a material feature the assets already author
(ArtRoom's lamp), and the normal-matrix fix is a correctness requirement the
local-space parts introduced in phase 11.

**Independent Test**: import `ArtRoom` and observe the lamp is visibly emissive;
scale a prop non-uniformly and confirm its normals/highlights stay correct.

**Acceptance Scenarios**:

1. **Given** a material with `emissiveFactor`/`emissiveTexture`, **When** it
   renders, **Then** it adds emissive light independent of the sun.
2. **Given** a part with non-uniform scale, **When** it renders, **Then** its
   lighting uses the correct normal matrix (no stretched/incorrect highlights).

---

### Edge Cases

- A glTF material with no `pbrMetallicRoughness` (defaults: metallic 1.0,
  roughness 1.0, white base color).
- A referenced texture image file that is missing or an unsupported format — must
  fail loudly (a missing texture is malformed; a material with *no* texture
  reference is valid and falls back to factors).
- An image with a `data:` URI or `bufferView` source (embedded) — unsupported this
  phase, fail loudly.
- A normal map with no `TANGENT` attribute (derivative-based tangent frame must be
  used).
- An albedo/emissive map in sRGB space vs. normal/roughness maps in linear space —
  the shader must decode only the color maps.
- A sampler with non-default wrap/filter (honor it, or default to repeat/linear).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The glTF parser MUST parse, per material, the
  `pbrMetallicRoughness` factors (`baseColorFactor`, `metallicFactor`,
  `roughnessFactor`), the `emissiveFactor`, and the texture references
  (`baseColorTexture`, `metallicRoughnessTexture`, `normalTexture`,
  `emissiveTexture`), with glTF spec defaults when absent.
- **FR-002**: The parser MUST resolve texture references through the glTF
  `textures`/`images` arrays to image URIs, and MUST fail loudly on embedded
  (`data:`/`bufferView`) images.
- **FR-003**: The system MUST decode external PNG/JPEG images and upload them as
  sampled GPU textures (via SDL_image's `IMG_LoadGPUTexture`), with a sampler per
  texture.
- **FR-004**: The static (environment/prop) fragment shader MUST implement
  metallic-roughness Cook-Torrance GGX shading: `D_GGX`, `F_Schlick`, `G_Smith`,
  with a directional sun, ambient light, and camera position.
- **FR-005**: The skinned (actor) fragment shader MUST implement the same
  metallic-roughness shading. IQM meshes name an albedo texture via their
  `Material` string; that texture is sampled where present, otherwise the mesh
  falls back to a default dielectric material (metallic 0, roughness ~0.8).
- **FR-006**: Shading MUST be sRGB-correct: albedo and emissive are decoded from
  sRGB to linear before lighting and gamma-encoded once at output; normal and
  roughness maps are sampled as linear data.
- **FR-007**: Normals MUST be transformed by the inverse-transpose of the model's
  upper 3×3 (a per-draw normal matrix), replacing the phase-4/11 `mat3(model)`
  approximation, so non-uniform scale lights correctly.
- **FR-008**: A material without a texture map MUST fall back to its scalar factor
  (e.g. `baseColorFactor`, `roughnessFactor`), and a missing `emissiveTexture`/
  `emissiveFactor` MUST contribute no emissive light.
- **FR-009**: The scene MUST supply a single directional sun (direction + color)
  and an ambient term, plus the camera position for specular; these are shared by
  both the static and skinned pipelines.
- **FR-010**: The system MUST fail loudly on a missing or unsupported referenced
  image (constitution II), and on any glTF feature already rejected by the phase-4
  parser.

### Key Entities *(include if feature involves data)*

- **Material** (parsed + runtime): `baseColorFactor`, `metallicFactor`,
  `roughnessFactor`, `emissiveFactor`, and optional albedo/normal/roughness/
  emissive textures plus `has*` flags.
- **Texture**: a decoded image uploaded as an SDL_gpu sampled texture + sampler.
- **Light**: a directional sun (direction, color) + ambient color; the scene-level
  light shared by both pipelines.
- **Static submesh / actor mesh**: existing geometry plus its per-submesh material.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: `ArtRoom` renders with PBR — albedo, normal, and roughness maps
  visibly affect shading, and the emissive lamp glows.
- **SC-002**: A factor-only material (no textures) shades correctly from its
  scalar factors.
- **SC-003**: The Akane actor renders under the same PBR sun/ambient and keeps
  skinning.
- **SC-004**: A prop scaled non-uniformly lights correctly (normal matrix fix).
- **SC-005**: A missing or unsupported referenced image fails loudly (no silent
  fallback), while a texture-less material does not fail.
- **SC-006**: `dotnet build` is clean and `dotnet test` passes (parser + material
  tests added).

## Assumptions

- **Opaque only.** Transparency, alpha blending, and the odin blend pass for glass
  are deferred (roadmap phase 14). `alphaMode`/`alphaCutoff` are not implemented
  this phase.
- **No shadows, IBL, or tone mapping.** Shadows are phase 14; ambient is a flat
  irradiance approximation (matching odin's v1 PBR), and output is simple gamma.
- **Metallic + roughness come from the packed `metallicRoughnessTexture`.** That
  texture is linear and packed: `.g` is roughness, `.b` is metallic (glTF layout).
  The shader multiplies `roughnessFactor × texture.g` and
  `metallicFactor × texture.b`. Current assets are dielectric (`metallicFactor:
  0`), so metallic evaluates to 0 in practice, but the channel is honored rather
  than ignored.
- **External images only.** Textures are external `.png`/`.jpg` files relative to
  the glTF; embedded images are unsupported and fail loudly.
- **Texture assets must be copied** from odin-game, using odin's per-asset
  subdirectory layout (`environments/<Name>/` and `props/<Name>/`, each with its
  own `textures/`), so glTF image URIs (`textures/…`) resolve relative to the
  `.gltf`.
- **No new managed dependency.** Image decoding uses SDL_image (already bound by
  `SDL3-CS` via `IMG_LoadGPUTexture`). The native `libSDL3_image.so` is vendored
  into `native/` (from `ppy.sdl3_image-cs`) and copied next to the apphost; its
  `libjbig.so.0` dependency is also vendored and preloaded in `Texture.fs` before
  the first decode (the app dir is not in the linker's default search path).
- **Lighting is scene-level constants this phase.** A small `Light` record (sun
  direction/color, ambient) is used rather than a zone-authoring UI (zone light
  editing arrives with zones/phase 8).
