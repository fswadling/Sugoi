# Quickstart: Actor Outline

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK and SDL3 for the windowed app.
- `glslc` (present at `/usr/bin/glslc`) to compile the shaders. **Two** shaders are compiled and
  committed this time: the changed `shaders/mesh.vert` and the new `shaders/outline.frag`.
- **Nothing else is new.** No package, no native library, no render target.

## Build & test

```sh
# Recompile the vertex shader (changed) and the new outline fragment shader.
glslc shaders/mesh.vert   -o shaders/mesh.vert.spv
glslc shaders/outline.frag -o shaders/outline.frag.spv

dotnet build
dotnet test test/fsharp-game.Tests.fsproj
```

## Automated coverage (headless)

`test/IqmTests.fs` grows cases for `Iqm.smoothNormals` (the one pure part, research D10):

1. **A cube corner** — three coincident vertices carrying the three face normals average to the
   corner's diagonal direction; all three get the same direction (this is the gap-closing property).
2. **Separated vertices** — vertices at distinct positions keep their own normalized normal.
3. **A single vertex** — returned as its own normalized normal.
4. **Well-formed output** — same length as the input; every output is a unit vector (no zero
   direction, even for opposing normals).

## Interactive walkthrough (SC-001 … SC-006)

Build and run; spawn Akane beside the ArtRoom environment.

1. **The outline exists (FR-001, SC-001)** — Akane is drawn with a dark edge tracing her silhouette;
   the environment and props are un-outlined.
2. **It follows the pose (FR-004, SC-002)** — play the jog and idle clips and blend a morph: the
   outline tracks the limbs and face; the cel shading underneath is unchanged (no double-shading).
3. **It closes at creases (FR-005, SC-003)** — turn Akane slowly through a full circle and watch the
   **hair** and the body creases: the outline is continuous, with no gaps between faces that meet at a
   sharp angle (this is the point of the averaged normals).
4. **Cutouts stay clean (FR-006, SC-004)** — the outline follows the visible hair/eye shapes; there is
   no outline boxing the transparent parts of those textures.
5. **Scale with distance, no leak (FR-007, FR-008, SC-005)** — zoom in and out: the line scales with
   distance like the geometry, thinning as the actor recedes. Look at a thin part (an arm against the
   torso, a hair strand): the outline does not bleed across the shaded surface.
6. **No regression (FR-010, FR-012, SC-006)** — the cel shading, the environment, props, portraits,
   UI and editor are unchanged; `dotnet build` is clean and `dotnet test` passes with the existing
   assertions unmodified.

## Automated coverage boundaries

- **`smoothNormals`** is pure and fully covered headlessly (above).
- The **outline pass**, the **extrusion width**, the **gap-free result**, the **cutout masking** and
  the **bleed-through** behaviour need a GPU device and a window, so they are steps 1–5. Every prior
  rendering spec follows the same split.
- The **pose** is spec 040's and is unchanged (the outline shares the mesh buffers); step 2 confirms it.
- **Not verified by automation**: the exact pixel width of the line, and that the outline does not
  increase the frame's per-frame allocation (structural — the smoothed normals are load-time and the
  uniform array is reused). The per-frame draw log shows the expected extra draws per actor.

## Expected outcome

- Actors are drawn with a fixed world-space, gap-free silhouette outline that follows their animation and
  respects their alpha cutouts, over the cel shading of spec 040.
- Environments and props are unchanged and un-outlined; one vertex shader was extended, one fragment
  shader and one front-cull pipeline were added, and `mesh.vert.spv` was recompiled.
- `dotnet build` is clean and `dotnet test` passes.
