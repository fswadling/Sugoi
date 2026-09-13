# Research: Texture Mipmaps

Decisions for completing texture level chains at load. The change is confined to
`render/Texture.fs`; the pipelines, shaders, and sampler are unaffected.

## Decision 1 — Create the texture with a full chain, not a single level

- **Decision**: create each sampled texture with `NumLevels = floor(log2(max(w,
  h))) + 1`, i.e. down to 1×1, whenever the largest dimension exceeds one texel.
- **Rationale**: the sampler is already configured to sample mip levels, so the
  texture must actually have them; a single level leaves minification sampling the
  base level, which aliases and reads far more texels than needed.
- **Alternatives considered**: leaving level 0 only and changing the sampler to
  ignore levels (rejected — that keeps the aliasing and the bandwidth cost);
  authoring mip chains into the asset files (rejected — the spec requires the
  renderer to generate them; source images have no levels).

## Decision 2 — Decode to a software surface and normalise the layout

- **Decision**: decode with SDL_image to a software surface, then normalise to a
  32-bit RGBA byte layout (`R,G,B,A` in memory) before creating and uploading the
  texture. Absent/needed conversion goes through SDL's surface conversion.
- **Rationale**: the convenience decode path (`IMG_LoadGPUTexture`) creates a
  texture with no levels and cannot be extended afterwards. A software surface
  gives access to the pixels, and normalising to one layout keeps every texture on
  the single GPU format the shaders expect. Decoders may return non-RGBA layouts
  (for example RGB24 for JPEGs), which must be converted rather than uploaded as-is.
- **Alternatives considered**: keeping the convenience decode and generating
  levels somehow after creation (rejected — levels cannot be added to an existing
  texture); handling each decoder layout in the shader (rejected — per-format
  branching for no benefit).

## Decision 3 — Compact the surface rows before upload

- **Decision**: when the surface's row stride (pitch) exceeds `width * 4`, copy
  each row into a tightly packed staging buffer before uploading.
- **Rationale**: surfaces are commonly allocated with an aligned pitch; uploading
  the raw buffer with the surface's stride would skew the image.
- **Alternatives considered**: uploading with the padded stride (rejected — the
  texture region is defined in texels, not bytes; it would shear the image).

## Decision 4 — Generate the levels on the GPU, outside any pass

- **Decision**: after uploading level 0 in a copy pass, end the pass and ask SDL to
  generate the remaining levels on the same command buffer, then submit. The
  texture must be created with color-target usage in addition to sampler usage.
- **Rationale**: this is the platform's supported path, avoids writing a
  downsampling compute/blit chain by hand, and runs once at load.
- **Alternatives considered**: CPU downsampling each level and uploading the whole
  chain (rejected — more code and more upload bandwidth for the same result);
  a custom shader/copy chain (rejected — reimplements a platform feature).

## Decision 5 — The sampler is unchanged

- **Decision**: keep the shared sampler's linear min/mag filters and linear
  mipmap mode.
- **Rationale**: sampling bands were already configured to use levels; only the
  levels were missing. No sampler, pipeline, or shader change is required.
- **Alternatives considered**: switching to nearest mipmap selection (rejected —
  visible band transitions for no gain).

## Decision 6 — The 1×1 placeholder needs no chain

- **Decision**: leave the shared 1×1 placeholder with a single level.
- **Rationale**: a full chain for 1×1 is exactly one level; there is nothing to
  generate, so it is already correct.
- **Alternatives considered**: routing the placeholder through the surface path
  (rejected — pointless overhead).

## Decision 7 — Load once, cached by path; fail loudly on any failure

- **Decision**: keep the existing path-keyed texture cache so a shared map is
  prepared once, and raise with the source path if decode, conversion, or texture
  creation fails.
- **Rationale**: FR-008 requires preparation to happen once per texture, not per
  material; the cache already provides this. A silent fallback to a single-level
  texture would reintroduce the aliasing invisibly, so failures must be loud
  (constitution II, SC-005).
- **Alternatives considered**: falling back to a mip-less upload when generation
  is unavailable (rejected — silent quality regression).
