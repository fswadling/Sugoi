# Implementation Plan: Animated Character on Screen

**Branch**: `009-animated-character` | **Date**: 2026-09-01 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/009-animated-character/spec.md`

## Summary

Land roadmap phase 3: the first real 3D result — an animated actor in the 3D
viewport. This builds the mesh-draw path SDL_gpu does not provide out of the box
(vertex/index buffers, one graphics pipeline, one shader), an IQM binary parser,
and GPU skinning + animation sampling, then wires a spawned Akane actor to render
and loop its `Idle` animation through the asset manifest. **Retrospective** — the
code already landed in the working tree; these artifacts document it.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`).

**Primary Dependencies**: `System.Numerics` (`Vector3`/`Quaternion`/`Matrix4x4`),
`System.IO` + `System.Buffers.Binary` (little-endian binary reads), SDL3-CS (GPU),
Prime (`scvalue` for the manifest). No new packages.

**Storage**: on-disk `assets/` (IQM + s-syntax manifests), scanned and loaded once
at startup into an in-memory `AssetManager` (id-keyed dictionaries) held on
`SharedState`.

**Testing**: xUnit for the pure logic — IQM parsing (`test/IqmTests.fs`) and pose
sampling (`test/ActorTests.fs`), plus `test/AssetsTests.fs` for the asset manager.
GPU/visual results are validated manually via `quickstart.md`. `dotnet test`
passes (54 tests).

**Target Platform**: Linux x64 desktop (native).

**Project Type**: native desktop game.

**Performance Goals**: 60 fps; per-frame cost is one bone-palette storage-buffer
upload (166 bones × 64 B ≈ 10.4 KiB) + one indexed draw. The IQM is parsed and the
static vertex/index buffers are uploaded once (startup), never per frame.

**Constraints**: `.fsproj` compile order is the dependency order. The phase-3
modules land in `core → render → world → state → editor → screen` position, and
the bone palette is a vertex storage buffer (not a pushed uniform) because
SDL_gpu caps pushed uniforms at 4 KiB. The parser/sampler/asset loader fail loudly
on malformed or unsupported input.

**Scale/Scope**: one character (Akane), one shader/pipeline, one placeholder
directional-Lambert shader; the model carries two animation clips and the `Idle`
clip is played.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: one parser module, one animation-sampling module, one
  mesh/gpu path, one placeholder shader. Textures, PBR, morphs, and locomotion
  are explicitly deferred to later phases.
- **II. Fail Loudly** — PASS: malformed/unsupported IQM (bad magic/version,
  missing `POSITION`, `COLOR`/unknown kinds, out-of-range joint counts) raises
  rather than silently drawing nothing; an actor asset without a model path
  raises at scan time.
- **III. Spec-First** — PASS (retroactively): this spec backfills the work.
- **IV. Data-Oriented** — PASS: IQM emits flat struct-of-arrays (`float32[]` +
  raw `byte[]` blend data); `ActorRender`/`AnimationSlot` are `[<Struct>]` and
  live inline in the actor slot map; skinning runs on the GPU.
- **V. Minimise GC Pressure** — PASS: parsing and model/mesh construction are
  load-time (cold path); the per-frame bone upload reuses a pre-allocated
  `BoneBytes` buffer and the animation clock is advanced through a `byref` so no
  `Actor`/`ActorRender` records are rebuilt per frame.

## Project Structure

### Documentation (this feature)

```text
specs/009-animated-character/
├── plan.md              # This file
├── research.md          # Phase 0 decisions
├── data-model.md        # Phase 1 data/behaviour model
├── quickstart.md        # Phase 1 validation guide
├── contracts/
│   └── animated-character.md
├── checklists/
│   └── requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
core/Iqm.fs              # NEW: IQM v2 binary parser (header/meshes/arrays/
                         #      triangles/joints/poses/anims/frames + PoseChannelOffsets)
render/Gpu.fs            # NEW: device/pass/buffer/pipeline wrappers, BonesMax,
                         #      mesh pipeline + depth texture
render/Mesh.fs           # NEW: static GPU vertex/index buffers + indexed draw
world/Actor.fs           # NEW: ActorModel + AnimationSlot + pose sampling
world/ActorAsset.fs      # NEW: shared per-asset runtime data (manifest meta + Mesh + ActorModel)
world/ActorRender.fs     # NEW: per-instance renderer (bone upload + draw + release)
world/Assets.fs          # MOVED+REWORKED from core/Assets.fs: AssetManager, eager actor load
world/Entity.fs          # MOVED+REWORKED from core/Entity.fs: Actor gains AssetId + Renderer
world/Zone.fs            # MOVED+REWORKED from core/Zone.fs: asset-aware spawn/CRUD
state/Screen.fs          # MOVED from screen/Screen.fs
state/SharedState.fs     # MOVED from screen/SharedState.fs: holds AssetManager
state/Command.fs         # MOVED from editor/Command.fs: Spawn/Delete/Duplicate carry AssetId
state/EditorState.fs     # MOVED from editor/EditorState.fs
state/GameplayState.fs   # MOVED from screen/GameplayState.fs: holds Zone + MeshPipeline
editor/AssetBrowser.fs   # spawns actors with the selected asset's id; props metadata-only
editor/Selection.fs      # delete/duplicate preserve the actor AssetId
screen/GameplayScreen.fs # advance actor animations; renderScene draws meshes after the grid
Engine.fs                # creates the mesh pipeline + depth texture, drives the scene pass
fsharp-game.fsproj       # reordered compile list (core → render → world → state → editor → screen)
shaders/mesh.vert(.spv)  # NEW: directional-Lambert vertex shader + GPU skinning
shaders/mesh.frag(.spv)  # NEW: placeholder directional-Lambert fragment shader
test/IqmTests.fs         # NEW: IQM parser tests
test/ActorTests.fs       # NEW: pose-sampling tests
test/AssetsTests.fs      # updated for AssetManager (id-keyed actors/props/environments)
test/CommandTests.fs     # updated for asset-aware commands
test/EntityTests.fs      # updated for Actor.AssetId/Renderer
```

**Structure Decision**: parsing and pose sampling stay in `core/`/`world/` (no
GPU, no `Zone` dependency). The GPU path (`render/{Gpu,Mesh}.fs`) sits below the
world model, and `world/ActorRender.fs` bridges the two: it owns the per-instance
renderer while `world/ActorAsset.fs` owns the shared per-asset mesh + parsed
model, avoiding a cycle (ActorRender → ActorAsset → Mesh/Actor). `Entity.Actor`
owns its `Renderer` inline, so `world/` must follow `render/`. The phase-3 work
therefore landed alongside a structural split of the old flat clusters into
`world/` (entity/world + actor runtime data) and `state/` (per-screen/shared +
editor command state); see `research.md` Decision 14.

## Complexity Tracking

No constitution violations to justify.
