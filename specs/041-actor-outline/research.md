# Phase 0 Research: Actor Outline

The method is settled by the reference and by the engine's capabilities; the work is in the details
the reference does not pin down — the extrusion direction on a hard-edged mesh, how the vertex buffer
grows, and how the world-space width is produced. Sources are in the spec's Origin.

## Decision 1 — The inverted hull, not a post-process edge filter

- **Decision**: outline actors with a second pass over their own posed mesh — front-face culled,
  vertices pushed out, flat-coloured, composited by the depth buffer. No offscreen render targets.
- **Rationale**: it is what `odin-game`'s plan names ("inverted-hull second pass — scaled back-face
  shells"), what Godot documents as **Grow**, and what the engine can do today (SDL_gpu exposes
  front-face culling and depth bias). The alternative family — Sobel/derivative edge detection over a
  depth + world-normal buffer ([Lettier — Outlining](https://lettier.github.io/3d-game-shaders-for-beginners/outlining.html))
  — needs offscreen depth and normal targets and a full-screen pass, none of which this port has
  (`render/SceneTarget.fs` is still planned). FR-003 forbids requiring one.
- **Alternatives considered**: **post-process Sobel** (gives interior crease lines and intersections,
  but needs a G-buffer and a new pass — out of scope and new infrastructure); **stencil-buffer
  outline** (Godot 4.5+; needs a stencil-capable depth format, but the engine creates `D32Float` —
  a depth-format change for no gain here).

## Decision 2 — Extend `mesh.vert`; one vertex-stage source of truth

- **Decision**: the outline pipeline reuses `shaders/mesh.vert` (extended with the smoothed-normal
  input and the extrusion) paired with a new `shaders/outline.frag`. It does **not** get its own vertex
  shader.
- **Rationale**: the hull must be posed by the *same* skinning and morphs as the surface, or it will
  not follow the actor (FR-004). Splitting the skinning into a second `outline.vert` would make two
  copies that must stay in step — a latent divergence bug. `glslc` has no `#include` by default, so a
  shared file is not available. Extending the one vertex shader keeps the pose computed once. The only
  cost is that the surface pass also pushes the (zero-width) outline uniform.
- **Alternatives considered**: **a separate `outline.vert`** with duplicated skinning (rejected — two
  sources of truth for the pose); **enabling the `GL_GOOGLE_include_directive` extension** so both
  shaders include a shared skinning block (rejected — a new build flag and generated-SPIR-V
  complexity for one shared block).

## Decision 3 — The extrusion direction is a position-welded average

- **Decision**: the direction a vertex is pushed along is a per-vertex normal **averaged across all
  vertices that share its position**, computed once at load (`Iqm.smoothNormals`), not the authored
  per-face normal.
- **Rationale**: inverted-hull gaps come from split vertices whose normals differ — exactly Godot's
  warning ("*the mesh must have connected faces with shared vertices… If the mesh has… flat shading,
  the mesh will appear to have gaps*"). Measured from the port's own asset, Akane is **79.4%
  hard-edged** at coincident vertices and her **hair mesh is 2 694 of 2 696 hard-edged** (spec
  Origin). Extruding along the authored normal would tear the outline open at every crease, worst on
  the hair. Averaging by position closes the seam because coincident vertices then share one
  direction. The average is a pure function of the positions and normals the IQM parser already reads.
- **Alternatives considered**: **authored normals** (gaps — the whole problem); **a second, authored
  smoothed-normal asset** (a new authored format and authoring surface for data that is derivable);
  **extruding along the position from the bone origin** (cheap but wrong for non-convex shapes — arms
  and hair get a global scale, not a silhouette).

## Decision 4 — The vertex grows from 40 to 52 bytes (attribute location 5)

- **Decision**: add a third `Float3` — the smoothed normal — at attribute **location 5, offset 40**;
  the interleaved stride becomes **52 bytes**. Both the cel and outline pipelines declare all six
  attributes.
- **Rationale**: the smoothed normal is per-vertex data consumed in the vertex shader, so a vertex
  attribute is the natural home (the alternative — a storage buffer indexed by `gl_VertexIndex` — is
  more machinery for the same bytes). 40 is `Float3`-aligned, so offsets are clean: position 0, normal
  12, uv 24, boneIds 32, weights 36, smoothed normal 40. Declaring all six on both pipelines keeps one
  layout shared by the two pipelines over the same buffer.
- **Alternatives considered**: **a storage buffer** (rejected — extra bind + indexing for data the
  vertex stage reads directly); **packing the smoothed normal into a spare channel** (no spare: the
  stride is exactly full); **a subset layout per pipeline** (nearly free, but two layouts to maintain
  for one buffer — the stride is what must match, and one shared six-attribute layout is simpler).

## Decision 5 — A fixed world-space shell

- **Decision**: the outline offsets each vertex in world space along its smoothed normal by one constant
  world distance (`outlineWidth`). The single value arrives in the third vertex uniform block (slot 2,
  `set = 1, binding = 2`) as `x`; `yzw` are unused.
- **Rationale**: FR-008 wants a line that shrinks with distance exactly like the actor — a physical ink
  ribbon — so the offset is a plain world-space constant with no on-screen projection. (The width was
  built several ways after seeing it run: a constant on-screen (clip-space) width did not recede; a shell
  clamped on screen, and then a clamped shell with a distance fade, still read as wrong; the plain
  world-space width is the final model — see the spec's Clarifications.)
- **Alternatives considered**: **constant on-screen (clip-space) width** (no recession — rejected);
  **a shell clamped on screen** (a near plateau that stops shrinking — rejected); **a clamped shell with
  a distance fade** (the line vanished rather than shrinking — rejected).

## Decision 6 — `outline.frag`: flat colour, alpha-cutout masked

- **Decision**: a new fragment shader that samples the material's albedo (the one sampler) and
  `discard`s texels below the alpha cutoff, then outputs a constant outline colour. It has **0 uniform
  buffers**.
- **Rationale**: FR-006 — hair and eye meshes are alpha cutouts, and the outline must follow the
  visible shape, so the outline masks by the same cutout as the cel surface (spec 040). Colour and
  cutoff are constants (FR-009), so no uniform block is needed. The shared opaque-white placeholder
  means a factor-only material's alpha is 1 and never discards.
- **Alternatives considered**: **no texture in the outline** (would box the hair/eye cards — the
  rejected option in the clarify session); **declaring the cel uniform block but leaving it unused**
  (risks the SPIR-V compiler stripping it, so the declared count would not match — a separate, uniformless
  block is exact).

## Decision 7 — A front-cull, depth-biased outline pipeline

- **Decision**: `Gpu.createOutlinePipeline` = `mesh.vert` (3 UBO, 2 storage) + `outline.frag`
  (0 UBO, 0 storage, 1 sampler), `CullMode.Front`, depth test `LessOrEqual` with depth write, **depth
  bias enabled** (a small constant + slope factor), opaque.
- **Rationale**: front-culling renders only the hull's back faces, so it is invisible except where it
  pokes past the silhouette (the whole trick). Depth bias is the standard guard against the hull
  bleeding through thin or concave geometry (FR-007) — SDL_gpu exposes
  `enable_depth_bias`/`depth_bias_constant_factor`/`depth_bias_slope_factor`
  ([`SDL_GPURasterizerState`](https://wiki.libsdl.org/SDL3/SDL_GPURasterizerState)), matching Vulkan's
  [`vkCmdSetDepthBias`](https://registry.khronos.org/vulkan/specs/1.3-extensions/man/html/vkCmdSetDepthBias.html).
- **Alternatives considered**: **`CullMode.None`** (would draw the hull's front faces too — a coloured
  shell over the actor); **no depth bias** (bleed-through on thin parts); **depth-pass with no write** (the
  hull must write depth so the surface composites over it cleanly).

## Decision 8 — Outline first, then the cel surface

- **Decision**: in `ActorRender.draw`, issue the outline hull **before** the cel surface, both
  depth-tested and depth-writing, after the shared bone/morph bindings.
- **Rationale**: this is the canonical order (Wikipedia: "*After drawing the outline, back-face culling
  is set back to normal to draw the shading…*") and it means the surface simply draws over the hull.
  The bone/morph buffers are bound once per actor, so both passes share them.
- **Alternatives considered**: **surface first, hull second** (works with `LessOrEqual`, but the
  canonical order is easier to reason about and matches the reference description).

## Decision 9 — Parameters are constants

- **Decision**: the outline colour, world-space width and alpha cutoff are shader/port constants (a dark
  near-black, `0.001` world units, `0.5`).
- **Rationale**: FR-009 fixes the parameters this phase, matching spec 040's policy. There is no
  per-frame derivation — the width is a single world distance, not a pixel scale.
- **Alternatives considered**: **per-actor or per-material outline authoring** (deferred, as in 040).

## Decision 10 — Test the one pure part

- **Decision**: extend `test/IqmTests.fs` with `Iqm.smoothNormals`: a cube corner (three coincident
  vertices with the three face normals) averages to the corner diagonal; well-separated vertices are
  returned unchanged; a single vertex is returned as authored. The outline itself is the walkthrough.
- **Rationale**: GPU draws cannot run headless in this port, so only the pure derivation is testable —
  and it is exactly where a subtle bug (a bad weld tolerance, a forgotten normalize) would silently
  break the outline. `PROJECT_STRUCTURE.md` colocates `Iqm`'s tests in `IqmTests.fs`.
- **Alternatives considered**: **golden-image tests** (no GPU in the host); **test nothing** (leaves the
  weld/normalize logic — the part most likely to regress — uncovered).
