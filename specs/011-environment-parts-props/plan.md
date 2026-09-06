# Implementation Plan: Environment Parts as Props

**Branch**: `011-environment-parts-props` | **Date**: 2026-09-05 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/011-environment-parts-props/spec.md`

## Summary

Rework the phase-4 environment path to match odin-game's parts-as-props model:
instead of baking a glTF node tree into one world-space environment mesh, the
parser emits local-space submeshes plus one **part** per mesh-bearing node (name,
hierarchy path, world transform, submesh range). Spawning an environment then
creates one `Prop` per part in the zone's props slot map, so importing `ArtRoom`
shows its furniture as individual, selectable, transformable props. Placed prop
assets (`beech`/`oak`/`oak2`) keep rendering as single entities; the animated
actor and editor overlays are unchanged.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`).

**Primary Dependencies**: `System.Text.Json` (glTF JSON), `System.IO` +
`System.Buffers.Binary` (`.bin` reads), `System.Numerics` (`Vector3`/`Quaternion`/
`Matrix4x4` incl. `Matrix4x4.Decompose`), Prime (`scvalue` manifests), SDL3-CS.
No new packages.

**Storage**: on-disk `assets/` — glTF (`.gltf` + `.bin`) + s-syntax manifests —
scanned and loaded once at startup into `AssetManager`. `EnvironmentAsset` gains a
`Parts` list alongside its shared `Submeshes`.

**Testing**: xUnit — extend `test/GltfTests.fs` for part emission/local-space
geometry, and `test/{AssetsTests,EntityTests,CommandTests}.fs` for environment-part
spawn/replace/undo. GPU/visual results validated via `quickstart.md`.

**Target Platform**: Linux x64 desktop (native).

**Project Type**: native desktop game.

**Performance Goals**: 60 fps. Static vertex/index buffers upload once per asset;
each part is one `PerDraw` push + one indexed draw per submesh in its range. No
per-frame allocation; imported parts share the environment's uploaded submeshes.

**Constraints**: `.fsproj` compile order is the dependency order (`core → render →
world → state → editor → screen`). `Gltf` cannot depend on `Entity.Transform`, so
the part's world transform is a `Matrix4x4` in `Gltf` and is decomposed into a
`Transform` at spawn time in `world/`.

**Scale/Scope**: one shared static runtime type still serves both environments
(with parts) and prop assets (whole model). One environment (`island`) exercises
the single-part case; `ArtRoom` is added as the multi-part validation asset.
No collision, textures, or persistence.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: one parser change (local space + parts), one extra
  field on `Prop` (submesh range), one renderer range parameter. No new GPU
  pipeline/shader; the environment reuses the existing static path via its parts.
- **II. Fail Loudly** — PASS: unsupported glTF (`matrix` nodes, sparse accessors,
  skins/animations, non-triangle primitives, missing `POSITION`, non-`float32`
  attributes) raises; a manifest with an empty `Model` raises at scan time.
- **III. Spec-First** — PASS: this spec/plan/tasks precede implementation.
- **IV. Data-Oriented** — PASS: submeshes stay flat struct-of-arrays; parts are a
  contiguous array on the asset; props stay inline in the zone's slot map.
- **V. Minimise GC Pressure** — PASS: parsing/decomposition is load-time (cold
  path); per-frame drawing pushes a reused `PerDraw` buffer and issues indexed
  draws, with no per-frame allocation.

## Project Structure

### Documentation (this feature)

```text
specs/011-environment-parts-props/
├── plan.md              # This file
├── research.md          # Phase 0 decisions
├── data-model.md        # Phase 1 data/behaviour model
├── quickstart.md        # Phase 1 validation guide
├── contracts/
│   └── environment-parts-props.md
├── checklists/
│   └── requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
core/Gltf.fs                # EXTENDED: emit local-space submeshes + Parts (world
                            #           Matrix4x4 + submesh range); drop world bake
world/EnvironmentAsset.fs   # EXTENDED: EnvironmentPart + EnvironmentAsset.Parts
world/EnvironmentRender.fs  # EXTENDED: submesh-range draw + per-instance orientation
world/Assets.fs             # EXTENDED: loadStaticModel gains parts; createPartRender
                            #           for environment parts
world/Entity.fs             # EXTENDED: AssetBinding + Prop gains SubmeshIndex/SubmeshCount;
                            #           Environment drops Renderer, gains Orientation
world/Zone.fs               # EXTENDED: loadEnvironment spawns one prop per part;
                            #           spawnPropBinding/bindingAt; preserves placed props
screen/GameplayScreen.fs    # EXTENDED: drop direct environment draw (parts draw via
                            #           the props slot map)
editor/Selection.fs         # EXTENDED: placed-prop vs part AssetBinding
editor/AssetBrowser.fs      # EXTENDED: sections + undoable environment spawn
state/Command.fs            # EXTENDED: commands carry AssetBinding; add SetEnvironment
editor/Outliner.fs          # EXTENDED: loaded-environment indicator
assets/environments/*       # ADD ArtRoom.gltf + ArtRoom.bin (copy from odin-game)
test/GltfTests.fs           # EXTENDED: part emission + local-space assertions
test/AssetsTests.fs         # EXTENDED: environment-part loading
test/EntityTests.fs         # EXTENDED: Prop submesh-range + env-part spawn
test/CommandTests.fs        # EXTENDED: part undo/redo
```

**Structure Decision**: keep the shared static runtime type (`EnvironmentAsset`)
for both kinds rather than splitting into `PropAsset`/`EnvironmentAsset` (the two
differ only by whether the caller draws the whole model or a submesh range, which
is one parameter on the renderer). `Gltf` stays in `core/` and stores part
transforms as `Matrix4x4`; `world/Zone.fs` decomposes them into `Entity.Transform`
(`Matrix4x4.Decompose`) so the gizmo/picking see the correct per-part position.
The environment entity loses its renderer and becomes a container holding
`AssetId` + `Orientation`; props (placed and imported) live on `Zone.Props`, and
`Zone.Environment` becomes optional. Imported parts have `AssetId = None`.

## Complexity Tracking

No constitution violations to justify.
