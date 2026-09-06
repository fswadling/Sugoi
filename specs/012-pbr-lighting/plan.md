# Implementation Plan: PBR + Lighting

**Branch**: `012-pbr-lighting` | **Date**: 2026-09-05 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/012-pbr-lighting/spec.md`

## Summary

Replace the flat Lambert shading in both render paths with metallic-roughness
Cook-Torrance GGX. The glTF parser gains full `pbrMetallicRoughness` + emissive +
texture-reference parsing; textures are decoded/uploaded via SDL_image
(`IMG_LoadGPUTexture`, no managed dependency); the static and skinned fragment
shaders become GGX PBR; normals switch to a per-draw inverse-transpose normal
matrix; and the scene supplies a directional sun + ambient + camera position. The
actor shades dielectric, sampling per-mesh albedo textures named by each IQM
mesh's material string and falling back to a default dielectric material where
none is named.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`).

**Primary Dependencies**: `System.Text.Json` (glTF JSON), `System.Buffers.Binary`
(`.bin`), `System.Numerics` (incl. `Matrix4x4.Decompose`/`Invert`/`Transpose`),
Prime (`scvalue` manifests), SDL3-CS (GPU + `SDL3.Image.LoadGPUTexture`). No new
managed packages.

**Storage**: on-disk `assets/` — glTF (`.gltf` + `.bin`) + external image files
(`textures/*.png|jpg`) + s-syntax manifests. Textures are copied from odin-game and
the layout reconciled so glTF image URIs resolve.

**Testing**: xUnit — extend `test/GltfTests.fs` for material/texture-reference
parsing and defaults. GPU/visual results validated via `quickstart.md`.

**Target Platform**: Linux x64 desktop (native).

**Project Type**: native desktop game.

**Performance Goals**: 60 fps. Textures upload once at asset load; per draw is one
material-uniform push + sampler binds + one indexed draw. The normal matrix is
computed per draw on the CPU (cheap 3×3) or in-shader; no per-frame allocation.

**Constraints**: `.fsproj` compile order is the dependency order (`core → render →
world → state → editor → screen`). `Gltf` stays in `core/` (no GPU handles) and
emits texture *references* (image URIs); the GPU texture upload lives in `render/`.
Fail loudly on unsupported/missing inputs.

**Scale/Scope**: one PBR shader pair for static geometry, one for skinned; one
material record; four texture kinds (albedo/normal/roughness/emissive). Opaque
only. Environment + props + actor all use the same light.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: reuse the existing static/skinned pipelines and add one
  material record + one texture loader; GGX BRDF is shared by both shaders. No
  shadow/IBL/blend pass this phase.
- **II. Fail Loudly** — PASS: missing/unsupported/embedded images raise; a
  texture-less material falls back to factors (valid glTF, not an error).
- **III. Spec-First** — PASS: this spec/plan/tasks precede implementation.
- **IV. Data-Oriented** — PASS: materials are a flat array indexed per submesh;
  textures are cached by URI; per-draw data is a pushed uniform block + sampler
  handles (value types).
- **V. Minimise GC Pressure** — PASS: texture/material upload is load-time (cold);
  per-frame pushes reuse preallocated uniform buffers with no allocation.

## Project Structure

### Documentation (this feature)

```text
specs/012-pbr-lighting/
├── plan.md              # This file
├── research.md          # Phase 0 decisions
├── data-model.md        # Phase 1 data/behaviour model
├── quickstart.md        # Phase 1 validation guide
├── contracts/
│   └── pbr-lighting.md
├── checklists/
│   └── requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
core/Gltf.fs                # EXTENDED: parse pbrMetallicRoughness (metallic/roughness
                            #           factors + baseColorTexture/metallicRoughnessTexture),
                            #           emissive (factor + texture), normalTexture,
                            #           textures/images; Material array + texture refs;
                            #           reject embedded images (bufferView and data:)
core/Iqm.fs                 # EXTENDED: Mesh.Material int -> string (names albedo texture)
render/Texture.fs           # NEW: SDL_image LoadGPUTexture wrapper + sampler + cache;
                            #      also defines runtime Material + Light records
render/Gpu.fs               # EXTENDED: Sampler type, loadShader sampler count, normalMatrix
shaders/static.vert(.spv)   # EXTENDED: world pos/normal, pass UV, normal matrix
shaders/static.frag(.spv)   # REWRITTEN: GGX PBR + albedo/normal/roughness/emissive
shaders/mesh.vert(.spv)     # EXTENDED: skinned PBR (world pos/normal + UV)
shaders/mesh.frag(.spv)     # REWRITTEN: same GGX PBR + skinning
world/EnvironmentAsset.fs   # EXTENDED: StaticSubmesh carries a runtime Material (Texture.Material)
render/StaticMesh.fs        # EXTENDED: draw pushes material uniform + binds samplers
render/Mesh.fs              # EXTENDED: per-submesh draw + material uniform + sampler binds
world/EnvironmentRender.fs  # EXTENDED: pass light + material; bind textures
world/ActorRender.fs        # EXTENDED: pass light + material
world/Assets.fs             # EXTENDED: load glTF materials + upload textures (via Texture.fs);
                            #           build per-submesh actor materials from IQM material names
screen/GameplayScreen.fs    # EXTENDED: construct Light value + camera pos; pass to renderers
assets/{environments,props} # ADD texture image files (copied from odin-game)
test/GltfTests.fs           # EXTENDED: material factor/texture-reference parsing + defaults
test/IqmTests.fs            # EXTENDED: IQM material string assertions
fsharp-game.fsproj          # insert render/Texture.fs; update CopyShaders for new shaders
```

**Structure Decision**: `Gltf` (in `core/`) emits `Material` descriptors with
texture *URIs* (no GPU handles); a new `render/Texture.fs` owns SDL_image decode +
SDL_gpu texture/sampler creation, the runtime `Material` and `Light` records, and
a URI-keyed cache (used by `Assets`). `world/Assets.fs` resolves URIs to loaded
textures and builds the runtime material per submesh — glTF materials for
environments/props, and per-IQM-mesh albedo materials for actors. The static and
skinned shaders share the GGX BRDF but keep separate vertex stages (skinning only
in the actor shader). `GameplayScreen` constructs the `Light` value and threads it
into both renderers.

## Complexity Tracking

No constitution violations to justify.
