# Research: PBR + Lighting

Decisions derived from `odin-game/src/environment_render.odin`,
`odin-game/assets/shaders/environment/env.frag`, and `odin-game/docs/PBR_PLAN.md`.

## Decision 1 — Cook-Torrance GGX with metallic-roughness (matching odin's env.frag)

- **Decision**: implement the standard direct-lit metallic-roughness BRDF —
  `D_GGX`, `F_Schlick`, `G_Smith` (Schlick-GGX with the `k = (r+1)²/8` remap) —
  with `F0 = mix(vec3(0.04), albedo, metallic)` and
  `direct = (kd·albedo/π + specular) · lightColor · NdotL`.
- **Rationale**: it's the same BRDF odin ships in `env.frag`, it's the glTF
  `pbrMetallicRoughness` model, and it's small enough to hand-write in GLSL.
- **Alternatives considered**: Lambert + Blinn-Phong (rejected — not the glTF
  model, no energy-consistent specular); a full IBL/filament BRDF (rejected —
  phase scope; odin uses flat ambient too).

## Decision 2 — sRGB decode/encode inside the shader

- **Decision**: albedo and emissive textures are sRGB → decode with `pow(c, 2.2)`
  before lighting; output is gamma-encoded once with `pow(c, 1/2.2)`. Normal and
  roughness maps are linear data, sampled raw.
- **Rationale**: matches odin and keeps color handling local to the shader; no
  framebuffer/format changes.
- **Alternatives considered**: sRGB GPU texture formats (rejected — odin does
  manual decode, and it keeps the shader self-contained).

## Decision 3 — SDL_image for texture decode, no managed image library

- **Decision**: use `SDL3.Image.LoadGPUTexture` (`IMG_LoadGPUTexture`), already
  bound by the referenced `SDL3-CS` package, to decode PNG/JPEG straight into an
  `R8G8B8A8Unorm` SDL_gpu texture. A thin `render/Texture.fs` wraps it plus
  `CreateGPUSampler`.
- **Rationale**: the BCL has no cross-platform image decoder, but the project is
  already on SDL3 whose companion SDL_image does it natively — no managed NuGet
  dependency.
- **Alternatives considered**: StbImageSharp/ImageSharp (rejected — new managed
  dependency when SDL_image is already there); embedding stb (rejected —
  duplicates SDL_image).

## Decision 4 — Material factors come from our own glTF parse

- **Decision**: parse `metallicFactor`, `roughnessFactor`, `baseColorFactor`,
  `emissiveFactor` and the texture refs directly from the glTF JSON (defaults:
  metallic 1.0, roughness 1.0, baseColor white, emissive black).
- **Rationale**: the phase-4 parser already reads glTF JSON; a model-loader
  abstraction might drop scalar factors (raylib drops them when no MR texture
  exists, per PBR_PLAN). Reading our own JSON is the only reliable source.
- **Alternatives considered**: trust a model loader's material values (rejected —
  the raylib lesson shows factors can be dropped).

## Decision 5 — Per-draw inverse-transpose normal matrix

- **Decision**: compute `mat3(transpose(inverse(model)))` (or `inverse` of the
  upper 3×3) per draw and pass it to the shader, replacing `mat3(model)`.
- **Rationale**: phase 11 made submeshes local-space and applies the full TRS via
  the model matrix, so non-uniform scale is now possible; `mat3(model)` is only
  correct for uniform scale.
- **Alternatives considered**: in-shader `inverse(transpose(mat3(model)))`
  (rejected — per-vertex/fragment cost and precision; CPU is one 3×3 inverse per
  draw).

## Decision 6 — Derivative-based tangent frame for normal maps

- **Decision**: reconstruct the tangent basis from screen-space derivatives of
  world position and UV (`dFdx/dFdy`), matching odin, because the glTF assets
  carry no `TANGENT` attribute.
- **Rationale**: avoids adding a tangent attribute to the vertex format and to the
  parser; the derivative approach is already proven in odin's `env.frag`.
- **Alternatives considered**: add `TANGENT` to the vertex pipeline (rejected —
  parser + format churn for a value derivable in-shader).

## Decision 7 — Actor shades dielectric with per-mesh albedo textures

- **Decision**: the skinned shader shades with the same BRDF on a dielectric
  material (metallic 0, roughness ~0.8). Each IQM mesh names an albedo texture via
  its material string (e.g. `_10.png`); that texture is sampled as the base color
  where present, otherwise the mesh falls back to a default dielectric material.
- **Rationale**: "PBR on everything" means consistent lighting; the IQM material
  string is the only authored texture reference, so we use it for albedo while
  keeping a single dielectric fallback for the remaining factors.
- **Alternatives considered**: a single constant material with no textures
  (rejected — the IQM material string already names the actor's albedo textures);
  keep the actor on Lambert (rejected — inconsistent lighting).

## Decision 8 — Opaque only; no blend pass

- **Decision**: no transparency/`alphaMode`/`alphaCutoff`/blend this phase; all
  materials are opaque.
- **Rationale**: odin's glass requires a separate blend pass and sorting; that's
  phase-14 scope. The current assets render acceptably opaque.
- **Alternatives considered**: alpha-cutoff discard (rejected — not needed by the
  assets and adds a uniform/branch).

## Decision 9 — Metallic-roughness samples the packed texture's `.g` and `.b`

- **Decision**: sample `metallicRoughnessTexture`'s `.g` channel for roughness and
  `.b` channel for metallic, multiplied by `roughnessFactor` and `metallicFactor`
  respectively. The texture is linear data — sampled raw, no sRGB decode.
- **Rationale**: this is the glTF `metallicRoughnessTexture` layout (B = metallic,
  G = roughness), and it is the complete, correct implementation rather than a
  dielectric-only shortcut. Current assets are dielectric (`metallicFactor: 0`),
  so metallic evaluates to 0 in practice, but the channel is honored.
- **Alternatives considered**: scalar-only metallic, ignoring `.b` (rejected — the
  packed channel is free to sample and future-proofs metals); a separate metallic
  texture (rejected — glTF packs both into one texture).

## Decision 10 — Texture assets copied + layout reconciled

- **Decision**: copy each asset's `textures/` tree from odin-game and switch to
  odin's per-asset subdirectory layout (`environments/<Name>/` and
  `props/<Name>/`, each with its own `textures/`), so the glTF `images[].uri`
  (`textures/…`) resolves relative to the `.gltf`. Manifests live next to their
  model with a `Model` path like `environments/ArtRoom/ArtRoom.gltf`, and
  `Assets.scanDir` recurses to find them.
- **Rationale**: the phase-4 assets copied only `.gltf`/`.bin`; the texture files
  are required now.
- **Alternatives considered**: embed textures in the manifests (rejected — glTF
  owns the references; we just make the files available).
