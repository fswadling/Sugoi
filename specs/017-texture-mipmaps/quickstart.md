# Quickstart: Texture Mipmaps

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- SDL3 ≥ 3.2 (level generation) and the `SDL3-CS` package restored; the vendored
  SDL_image native library and its `libjbig` dependency copied next to the apphost
  by the build.
- A scene with a detailed, textured environment (e.g. the ArtRoom) whose materials
  use the full range of map types.

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj   # includes the level-count arithmetic, if exposed
```

## Validation scenarios

1. **No shimmer at distance (SC-001)** — load the textured environment and move the
   camera so surfaces fall into the distance: their appearance is stable; there is
   no crawling high-frequency noise. Compare against a build before this feature to
   confirm the difference.
2. **Grazing angles (SC-001)** — view a surface nearly edge-on (e.g. the floor far
   ahead): it resolves to a stable averaged tone rather than aliased noise.
3. **Close-up unchanged (SC-003)** — view a texture up close (magnified): its
   base-level detail matches the pre-change build; no softening.
4. **Non-RGBA source (FR-004)** — load a material whose source image has no alpha
   (typically a JPEG): colours are correct and the surface is opaque, not misordered
   or transparent.
5. **Non-square / non-power-of-two (FR-005)** — load an image whose dimensions are
   not powers of two: it renders undistorted and still reduces cleanly with distance.
6. **Shared map prepared once (FR-008)** — a texture referenced by several materials
   loads once; the scene's load time does not grow with the number of references.
7. **Coverage (SC-002, SC-005)** — every textured material (albedo, normal,
   metallic-roughness, emissive) reduces smoothly; no map silently samples a single
   level. An absent map still renders via the 1×1 placeholder.
8. **Failure is loud (FR-007)** — point a material at a corrupt or missing image:
   loading fails with a message naming the path, rather than rendering a single-level
   texture.
9. **No per-frame regression (SC-004)** — the per-phase frame log shows no added
   per-frame work (`record`/`update` unchanged); texture preparation happens during
   load.

## Expected outcome

- Textured surfaces are stable under minification across distances and grazing
  angles, with close-up detail unchanged.
- Every sampled texture has a complete chain to 1×1; the 1×1 placeholder is
  unaffected.
- Preparation is a one-time load cost with no per-frame work added.
- `dotnet build` is clean; `dotnet test` passes.
