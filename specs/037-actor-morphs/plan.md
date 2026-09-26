# Implementation Plan: Actor Morph Targets

**Branch**: `037-actor-morphs` | **Date**: 2026-09-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/037-actor-morphs/spec.md`

## Summary

An actor asset gains morph targets: named sets of per-vertex offsets loaded once from an s-expression manifest and a binary of 32-bit floats. An actor weighs up to eight of them, and its rendered shape is the base mesh displaced by `Σ weight × offset` — applied to the vertex **before the bone blend**, which is the one place this port cannot copy odin.

odin morphs a CPU vertex array and lets raylib re-upload and skin from it. This port skins on the GPU: the actor's vertex buffer is static and uploaded once, and the bone palette is a vertex storage buffer. Morphing on the CPU here would mean a dynamic vertex buffer and ~800 KB re-uploaded per actor per frame — the cost `023` spent a feature removing. So the offsets go to the vertex stage: a **per-asset storage buffer** of deltas read by `mesh.vert`, and a small **per-actor weight push**, with the displacement applied before the existing skinning. That is the same `reset, then add delta * weight` odin computes, expressed where this pipeline can compute it.

The data is authored: a `Morphs` field on the actor manifest names an s-expression morph manifest, which names its binary; the manifest's targets are absolute vertex ranges into the actor's combined vertex buffer (odin's are per source mesh, because raylib keeps one array per mesh). The parse and the delta packing are pure and headless-testable; `Assets` does the file IO and the one upload.

No new module and no new managed dependency: one field on the asset, one slot map and two setters on the renderer, the slot algebra beside the animation slots, the manifest types and their pure builders in `ActorAsset.fs`, the loader in `Assets.fs`, a Morphs section in the Inspector, and a recompiled `mesh.vert`.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: none new. `Prime` (already referenced) parses the s-expression manifest, exactly as the actor/prop/environment manifests are parsed today; `System.Numerics` and the existing `Gpu`/`Mesh`/`SDL3-CS` path for the GPU side; **`glslc`** to recompile `shaders/mesh.vert` (the project's existing shader step). No JSON parser is added.

**Storage**: two sidecar files per morph-carrying asset — a manifest (s-expression) and a data file (float32). Nothing is persisted at runtime; morph weights are actor runtime state.

**Testing**: `test/ActorTests.fs` is extended — no new test file, no `.fsproj` change. The parts that are pure are the manifest parse (string → records) and the delta packing (bytes + targets + counts → the per-vertex buffer), so both are reachable headlessly, as is the weight algebra over `SlotMap<MorphSlot>`:
- the parse rejects a bad version, a missing data file name, and a target whose range exceeds the mesh;
- the packing places each target's offsets at the right per-vertex index and zeroes the rest;
- a weight clamps to 0..1, adds when absent, adjusts when held, and a ninth morph raises;
- with no morphs held the displacement is zero.

What no headless test reaches is the shader itself and the Inspector section — the GPU path needs a device and the section needs a frame, so both are [quickstart.md](./quickstart.md), as `036` left its Inspector section.

**Target Platform**: Linux x64 desktop, SDL3 + SDL_gpu.

**Project Type**: single-project native desktop game with a docked ImGui editor.

**Performance Goals**: hold 60 FPS. The morph cost is bounded by the morph count (≤ 8): at most eight `vec4`-pair reads and adds per vertex, gated on a count that is zero for the actors that carry no morphs. The offsets are uploaded once per asset at load; only the weights (48 bytes) are pushed per actor per frame, from a reused byte array, so the path adds no per-frame allocation.

**Constraints**: an actor holds at most eight morphs and a ninth fails loudly (FR-006); the displacement happens before posing (FR-004); an actor with no morph data, or every weight at 0, renders as it does today and the existing animation/skinning suites are unchanged (FR-008, SC-002); a malformed sidecar fails at load (FR-003, SC-004); morph edits are runtime state with no command and no persistence (FR-011); no per-frame allocation (FR-012).

**Scale/Scope**: one field on `ActorAsset`; morph types, the parse and the packing in `world/ActorAsset.fs`; the load in `world/Assets.fs`; `MorphSlot` + the weight algebra in `world/Actor.fs`; the slot map, two setters and the draw binding in `world/ActorRender.fs`; `MorphsMax` and two pipeline descriptor counts in `render/Gpu.fs`; a Morphs section in `editor/Inspector.fs`; `shaders/mesh.vert` (+ its `.spv`); tests extended. No new file, no `.fsproj` change, no command, no editor-state field.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one storage buffer, one pushed uniform, one asset field, one slot map, two setters, one Inspector section. No new module, no new managed dependency, and the Inspector reuses the shape `036` landed. |
| II. Fail Loudly | Pass — an unparsable manifest, a data file whose size disagrees with the manifest, a target range outside the mesh, an asset declaring more than eight morphs, an unknown morph name, and a ninth morph on one actor all raise rather than degrading. |
| III. Spec-First | Pass — `specs/037-actor-morphs/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — the offsets are one contiguous per-asset buffer in a vectorised per-vertex layout, and the actor's weights are a `SlotMap` of `[<Struct>]` slots scanned as a `Span`, as the animation slots are. |
| V. Minimise GC Pressure | Pass — the deltas are uploaded once; the per-frame work writes the weights into a reused 48-byte array and pushes it, so the play path allocates nothing. |
| N-V. Testing is the Authoring Tool | Not engaged — it governs the narrative subsystem, which is untouched. |

## Complexity Tracking

None. The one structural addition — a storage buffer and a pushed uniform for the vertex stage — is the smallest way to apply the offsets where this pipeline applies a vertex, and it reuses the binding pattern the bone palette already established.

## Project Structure

### Documentation (this feature)

```text
specs/037-actor-morphs/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── actor-morphs.md  # Phase 1 output
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
shaders/
└── mesh.vert(.spv)     # CHANGED: a `Morphs` storage buffer (set 0, binding 1) and a
                        #   `MorphWeights` vertex uniform (set 1, binding 1); displace
                        #   aPosition/aNormal by Σ weight·delta before the bone blend.
                        #   Recompiled with `glslc`; the `.fsproj` copy target is unchanged.
render/
└── Gpu.fs              # CHANGED: `+ MorphsMax = 8` beside `BonesMax` (the shader mirrors it);
                        #   `createMeshPipeline`'s vertex shader declares 2 storage buffers and
                        #   2 uniform buffers, not 1 and 1.
world/
├── Actor.fs            # CHANGED: `+ MorphSlot` (morph index + mutable weight) and the pure
│                       #   weight algebra beside the animation one — `setMorphWeight`,
│                       #   `removeMorph`, a slot lookup — all int-indexed, so Actor.fs stays
│                       #   GPU-free.
├── ActorAsset.fs       # CHANGED: the manifest types (`[<SymbolicExpansion>]`), the pure
│                       #   manifest parse and delta packing, the runtime `ActorMorphs`
│                       #   (names, count, GPU buffer), and `Morphs : ActorMorphs option` on the
│                       #   asset.
├── Assets.fs           # CHANGED: `loadActors` reads an actor's `Morphs` sidecar (file IO), builds
│                       #   its delta buffer through the pure packer, and uploads it once.
└── ActorRender.fs      # CHANGED: `MorphSlots : SlotMap<MorphSlot>`; `setMorphWeight`/`removeMorph`
                        #   (name-resolving); `draw` binds the asset's delta buffer at vertex storage
                        #   slot 1 (or the bone buffer when there is none) and pushes the weights.
editor/
└── Inspector.fs        # CHANGED: a `Morphs` collapsing header in the actor block, mirroring the
                        #   Animations section — a row per held morph (name, weight drag, remove) and
                        #   a button per unheld morph; the degenerate cases as text.
test/
└── ActorTests.fs       # EXTENDED: the manifest parse, the delta packing, the weight algebra.
docs/
├── PROJECT_STRUCTURE.md # already describes `world/Actor.fs` as "IQM model + morphs"; no change.
└── ROADMAP.md          # CHANGED (small): phase 3's deferral of morphs is now built.
assets/
└── actors/…            # ADD: one actor asset carrying a morph sidecar (manifest + binary), so the
                        #   feature has something to render and a test has a fixture.
```

**Structure Decision**: the offsets belong to the **asset** (`ActorAsset.fs` holds their types, the pure parse and the packing), and the weights belong to the **actor** (`Actor.fs`'s slot algebra and `ActorRender.fs`'s slot map), which is the split the data itself has. `ActorAsset.fs` is compiled after `Actor.fs`, so the weight algebra cannot depend on the asset's types — it takes the morph **count** as an argument instead, which keeps `Actor.fs` free of both the asset and the GPU. The loader goes in `Assets.fs` because that is where every sidecar is read and where the one upload belongs. Nothing is added to `Mesh.fs`: the vertex layout is unchanged, the draw already binds storage buffers before it, and the weight push is the renderer's per-actor concern, not the mesh's. No new module, and no new file beyond the fixture asset the feature needs.

## Phase 0: Research

See [research.md](./research.md). It settles why the offsets are applied in the vertex stage rather than on the CPU, the per-asset buffer's dense per-vertex layout and why it is not odin's per-mesh one, where the weights travel, why `MorphsMax` lives in `Gpu.fs` while `MaxAnimationSlots` lives in `Actor.fs`, the manifest's shape and its extension, and the split that keeps the loader testable.

## Phase 1: Design

- [data-model.md](./data-model.md) — `MorphSlot`, the asset's morph data and manifest records, the delta buffer's layout, the weight uniform's layout, and the authored-versus-runtime boundary.
- [contracts/actor-morphs.md](./contracts/actor-morphs.md) — the surfaces that change: `Gpu`, `Actor`, `ActorAsset`, `Assets`, `ActorRender`, `mesh.vert`, the Inspector section, and what explicitly does not change.
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite, the shader rebuild, and the interactive walkthrough.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — as checked: no new module, file, dependency, or editor-state field; one buffer, one push, one section. |
| II. Fail Loudly | Pass — every malformed-input path and the nine-morph over-limit raise. |
| III. Spec-First | Pass. |
| IV. Data-Oriented | Pass — a contiguous per-asset buffer in a vectorised layout, and struct slots scanned as a `Span`. |
| V. Minimise GC | Pass — deltas uploaded once; weights pushed from a reused array; nothing per frame. |
