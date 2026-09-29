# Quickstart: Cel-Shaded Actors

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK and SDL3 for the windowed app.
- `glslc` (or `glslangValidator`) to compile `shaders/cel.frag` → `shaders/cel.frag.spv`. Present on
  this machine (`/usr/bin/glslc`). The `.spv` is committed, like every other shader.
- **Nothing else is new.** No package, no native library. `mesh.vert` is reused, so the vertex stage
  and its skinning/morph buffers are untouched.

## Build & test

```sh
# Recompile the shader whenever cel.frag changes.
glslc shaders/cel.frag -o shaders/cel.frag.spv

dotnet build
dotnet test test/fsharp-game.Tests.fsproj
```

## Automated coverage (headless)

Two pure parts are covered with no GPU, no window and no font (research D9):

1. **Face-mesh name rule** — `Mesh.isFaceMeshName` is `true` for `"Face"` and `false` for `"Body"`,
   `"Hair"`, `"face"` (exact match) and `""` (FR-007).
2. **Face-light direction** — `ActorRender.faceLightDir`:
   - **Identity model** → `(0, 0.25, 1)` after normalization: level `+Z` with the elevation (FR-007).
   - **Quarter turn about `Y`** → level `+X` with the elevation — the facing rotates with the actor.
   - **Well-formed** — a non-degenerate unit-horizontal direction even for a scaled/degenerate model
     (the horizontal is normalized before the elevation is added).

## Interactive walkthrough (SC-001 … SC-006)

Build and run; spawn an actor (Akane) next to an environment (ArtRoom).

1. **The two-tone look (FR-002, SC-001)** — the actor is shaded in two flat bands with a soft edge,
   **not** the smooth PBR gradient the environment beside it shows (SC-001: actor cel, environment
   PBR, side by side).
2. **The bands follow the sun (FR-004, SC-002)** — with the sun fixed in `GameplayScreen`
   (`Direction = normalize(0.4, 0.8, 0.4)`, ambient `0.15`), turn the actor with the editor's rotate
   gizmo: the body's band boundary sweeps across it. Vary the ambient constant and confirm the shadow
   band lightens/darkens rather than going black (FR-003).
3. **The face keeps its banding (FR-007, FR-008, SC-003)** — turn the actor through a full circle: the
   **face's** band boundary stays fixed relative to the face (it does not wash out as the head turns
   away from the sun), while the body's boundary follows the sun. The face's *colours* still track the
   scene light.
4. **Cutouts stay clean (FR-006, SC-004)** — the hair/eye cutout texels are absent, not fringed or
   blended; no sorting artefact appears.
5. **Animation is unchanged (FR-010, SC-005)** — the actor jogs/idles and its morphs blend exactly as
   before (the pose is the same; only the fragment shading changed).
6. **Everything else is unchanged (FR-013, SC-006)** — the environment, props, portraits, UI and
   editor render as before; `dotnet build` is clean and `dotnet test` passes with the existing
   assertions unmodified.

## Automated coverage boundaries

- The **face rule** and the **face-light direction** are pure and fully covered headlessly (above).
- The **shading**, the **cutouts**, the **live sun response** and the **face constant-banding** need a
  GPU device and a window, so they are steps 1–4. This project's tests never open a device (every
  prior rendering spec follows the same split).
- The **skinning/morph vertex path** is spec 023/037's and already covered; this feature does not touch
  it (FR-010).
- **Not verified by automation**: that the cel pipeline and the static pipeline coexist without state
  leaking (both were already separate; the actor path only changes the fragment stage and the sampler
  count). The per-frame draw log (`draws`/`instances`) should be unchanged.

## Expected outcome

- Actors render with the `odin-game` two-band cel look, coloured additively from the scene light, with
  the `Face` mesh lit by an idealized front light so it keeps constant banding.
- Environments and props are unchanged (still PBR); one fragment shader was replaced
  (`mesh.frag` → `cel.frag`), the actor pipeline was retargeted and renamed, and `mesh.vert` was
  untouched.
- `dotnet build` is clean and `dotnet test` passes.
