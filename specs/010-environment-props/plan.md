# Implementation Plan: Environment + Props

**Branch**: `010-environment-props` | **Date**: 2026-09-05 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/010-environment-props/spec.md`

## Summary

Land roadmap phase 4: static world geometry — a spawned environment and prop
assets — visible in the 3D viewport. This adds a scoped glTF 2.0 parser (static
meshes only), a *static* (unskinned) mesh-draw path reusing the phase-3 buffer/
pipeline machinery, and environment/prop asset + entity wiring. The animated
actor from phase 3 keeps rendering in the same scene pass. Textures and
metallic-roughness (PBR) are explicitly phase 5; collision/BVH is phase 6.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`).

**Primary Dependencies**: `System.Text.Json` (glTF JSON), `System.IO` +
`System.Buffers.Binary` (`.bin` reads, little-endian), `System.Numerics`
(`Vector3`/`Quaternion`/`Matrix4x4`), Prime (`scvalue` for the s-syntax manifests),
SDL3-CS (GPU). No new packages.

**Storage**: on-disk `assets/` — glTF (`.gltf` + external `.bin`) and s-syntax
manifests — scanned and loaded once at startup into `AssetManager`
(id-keyed dictionaries), held on `SharedState`. Environment/prop model files must
be added (copied from odin-game) and the manifests extended with a `Model` path.

**Testing**: xUnit for the pure glTF parser (`test/GltfTests.fs`) plus updated
`test/AssetsTests.fs` for environment/prop loading. GPU/visual results are
validated manually via `quickstart.md`.

**Target Platform**: Linux x64 desktop (native).

**Project Type**: native desktop game.

**Performance Goals**: 60 fps; static vertex/index buffers are uploaded once at
startup. The per-frame cost per submesh is one pushed `PerDraw` uniform + one
indexed draw — no per-frame vertex upload, no per-instance bone palette.

**Constraints**: `.fsproj` compile order is the dependency order (`core → render →
world → state → editor → screen`). glTF is parsed into flat struct-of-arrays
(`float32[]`/`int[]`) that feed SDL_gpu buffers directly, mirroring `core/Iqm.fs`.
The parser and asset loader fail loudly on malformed or unsupported input.

**Scale/Scope**: one environment (`island`) + three props (`beech`/`oak`/`oak2`),
one static pipeline + one static shader, flat/Lambert untextured rendering. The
environment is a single zone field (not slot-mapped); props live in the existing
`props` slot map and share loaded models.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: one scoped glTF parser, one static mesh path, one
  static shader, one environment + one prop asset type. Hierarchy/parts,
  collision, textures, and PBR are explicitly deferred to later phases.
- **II. Fail Loudly** — PASS: unsupported glTF (skins, animations, sparse
  accessors, data-URI buffers, non-`float32` attributes, non-triangle primitives)
  raises rather than silently drawing nothing; an asset whose manifest lacks a
  `Model` path raises at scan time.
- **III. Spec-First** — PASS: this spec/plan/tasks precede implementation.
- **IV. Data-Oriented** — PASS: glTF emits flat struct-of-arrays; the static mesh
  is a `[<Struct>]` of GPU buffer handles; submeshes are contiguous arrays; the
  environment/prop entities live inline in the zone's struct fields/slot map.
- **V. Minimise GC Pressure** — PASS: parsing and buffer upload are load-time
  (cold path); per-frame drawing pushes a reused `PerDraw` buffer and issues
  indexed draws, with no per-frame allocation.

## Project Structure

### Documentation (this feature)

```text
specs/010-environment-props/
├── plan.md              # This file
├── research.md          # Phase 0 decisions
├── data-model.md        # Phase 1 data/behaviour model
├── quickstart.md        # Phase 1 validation guide
├── contracts/
│   └── environment-props.md
├── checklists/
│   └── requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
core/Gltf.fs                # NEW: scoped glTF 2.0 parser (buffers/views/accessors/
                            #      meshes/primitives/materials/nodes, baked world-space,
                            #      flat float32[]/int[] output)
render/Gpu.fs               # EXTENDED: add createStaticPipeline (position/normal/uv)
render/StaticMesh.fs        # NEW: static GPU vertex/index buffers + indexed draw
world/EnvironmentAsset.fs   # NEW: shared static model (id/name/modelPath/
                            #      orientation + StaticMesh submeshes) for env + prop
world/EnvironmentRender.fs  # NEW: per-instance static renderer (draw + release)
world/Assets.fs             # EXTENDED: env/prop assets now load glTF (no longer
                            #      metadata-only); manifest gains Model + OrientationDegrees
world/Entity.fs             # EXTENDED: Prop gains AssetId + Renderer; Environment gains
                            #      AssetId + Renderer + orientation
world/Zone.fs               # EXTENDED: spawn/remove env + props with renderers
editor/AssetBrowser.fs      # EXTENDED: spawn environment and prop assets
screen/GameplayScreen.fs    # EXTENDED: draw environment + props in the scene pass
state/Command.fs            # EXTENDED: prop spawn/delete/duplicate carry AssetId
fsharp-game.fsproj          # reordered compile list (insert Gltf/StaticMesh/Environment*)
shaders/static.vert(.spv)   # NEW: static Lambert vertex shader (no skinning)
shaders/static.frag(.spv)   # NEW: static Lambert fragment shader
assets/environments/*       # ADD glTF + .bin model files (copied from odin-game)
assets/props/*              # ADD glTF + .bin model files; manifests gain Model
test/GltfTests.fs           # NEW: glTF parser tests
test/AssetsTests.fs         # EXTENDED: environment/prop asset loading tests
test/fsharp-game.Tests.fsproj # register GltfTests.fs
```

**Structure Decision**: parsing stays in `core/` (pure, no GPU — like `Iqm.fs`).
The GPU static path splits into `render/StaticMesh.fs` (the buffer/draw primitive,
parallel to `render/Mesh.fs`) and `world/EnvironmentRender.fs` (the per-instance
domain renderer, parallel to `world/ActorRender.fs`). `world/EnvironmentAsset.fs`
holds the shared per-asset static model, parallel to `world/ActorAsset.fs`, so
`EnvironmentRender` can depend on it without a cycle back into `Assets`.
`Environment`/`Prop` gain an inline renderer, so `world/` must follow `render/`
(already true after phase 3's structural split). A single static runtime type
serves both environments and props (a prop is a static model with identity
orientation).

## Complexity Tracking

No constitution violations to justify.
