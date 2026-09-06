# Quickstart: PBR + Lighting

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- Texture image files copied from odin-game under `assets/`, with a layout that
  resolves the glTF `images[].uri` (`textures/…`). `ArtRoom` needs its 87 images;
  `Beech`/`Oak`/`Oak2` their albedo/normal/roughness maps.
- The native `libSDL3_image` available at runtime (SDL3 runtime).

## Build & test

```sh
dotnet build
dotnet test   # GltfTests (material/texture-reference parsing + defaults)
```

## Validation scenarios

1. **Textured PBR** — import `artroom`; walls/floor/furniture show albedo + normal
   detail + roughness response under the sun (not flat Lambert).
2. **Emissive** — the `artroom` lamp glows independently of the sun.
3. **Factor-only material** — `island` (no texture) shades from its factors with
   correct defaults.
4. **Props** — spawn a `beech`; its bark/leaves show their albedo/normal/roughness
   maps.
5. **Actor** — spawn Akane; it is lit by the same sun/ambient with a default
   dielectric material and keeps skinning.
6. **Non-uniform scale** — scale a prop non-uniformly; normals/highlights stay
   correct (no stretched shading).
7. **Fail loudly** — point a material's texture at a missing file and confirm a
   loud failure (not a silent gray surface).

## Expected outcome

- Static geometry, props, and the actor all shade with metallic-roughness PBR
  under one directional sun + ambient.
- Albedo/normal/roughness/emissive maps are sampled correctly with sRGB handling.
- Non-uniform scale lights correctly (inverse-transpose normal matrix).
- `dotnet build` is clean; `dotnet test` passes (existing suite + material tests).
