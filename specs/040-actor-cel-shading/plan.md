# Implementation Plan: Cel-Shaded Actors

**Branch**: `040-actor-cel-shading` | **Date**: 2026-09-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/040-actor-cel-shading/spec.md`

## Summary

Give actors the `odin-game` cel look: a two-band soft ramp over the wrapped half-Lambert
(`dot(N,L)*0.5+0.5`), coloured additively (`albedo * (ambient + sunColour * band)`), with the
actor's **`Face` mesh lit by an idealized front light** so it keeps constant banding as the actor
turns (FR-002, FR-003, FR-007).

The key structural fact is that actors already draw through a **dedicated** pipeline: the shared
skinned-mesh pipeline (`shaders/mesh.vert` + `mesh.frag`, `render/Mesh.fs`) is used **only** by
actors — environments and props go through the separate instanced static path
(`shaders/static.vert`/`static.frag`, `render/StaticMesh.fs`, `world/EnvironmentBatch.fs`). So this
feature is not "add a second pipeline next to PBR"; it is **turn the actor pipeline into the cel
pipeline**: the fragment shader becomes cel (a lean uniform, one sampler), `mesh.vert` (skinning +
morphs) is reused unchanged, and the `Face` flag is carried from the IQM mesh name onto the actor's
submeshes. Nothing in the environment/prop path changes.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: **none new.** SDL3-CS (SDL_gpu); `glslc`/`glslangValidator` (already on
the machine) to compile the one new GLSL fragment shader. The existing `mesh.vert` is reused, so no
vertex-stage change and no new native library.

**Storage**: none. One new runtime mesh flag (`Mesh.Submesh.IsFace`) and one shader; no authored
format changes and nothing persisted.

**Testing**: `test/ActorTests.fs` (extended) covers the two **pure** parts — the face-mesh name rule
and the face-light direction helper. The shading, the cutouts and the live sun response are the
interactive walkthrough (a GPU, a window and a person). GPU paths cannot run headless here.

**Target Platform**: Linux x64 desktop, SDL3 + SDL_gpu.

**Project Type**: single-project native desktop game with a docked ImGui editor.

**Performance Goals**: hold 60 FPS. The cel fragment shader is **cheaper** than the PBR one it
replaces (no GGX, no derivative tangent frame, no normal/metallic-roughness/emissive sampling), and
the draw pushes fewer fragment-uniform bytes (80 vs 128) and binds fewer samplers (1 vs 4). The draw
path adds no per-frame allocation (FR-011, SC-005): the `Face` flag is a struct field set once at
load, and the uniform bytes array is reused.

**Constraints**: actors only (FR-001); two bands, threshold 0.5 / softness 0.05 (FR-002); additive
colour, shadow band never black (FR-003); body bands follow the scene sun/ambient (FR-004); albedo
from the material texture × base colour (FR-005); alpha-test cutouts (FR-006); `Face` meshes take the
idealized front light (direction only — colours still from the scene) (FR-007, FR-008, FR-009);
skinning/morphs unchanged (FR-010); constants, no authoring surface (FR-012); existing behaviour and
tests unchanged (FR-013, FR-014).

**Scale/Scope**: one fragment shader replaced (`mesh.frag` → `cel.frag`), the actor pipeline
retargeted and renamed (`createMeshPipeline`/`MeshPipeline` → `createCelPipeline`/`CelPipeline`),
`Mesh.Submesh` gains `IsFace`, `Assets.loadActors` sets it, `ActorRender.draw` computes the face
light, `Mesh.draw` pushes the cel uniform and binds one sampler, the `.fsproj` shader-copy list
swaps `mesh.frag.spv` → `cel.frag.spv`, docs updated, tests added. No new pass, no new pipeline
count, no new native dependency.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — the actor pipeline already exists; the change **retargets** it rather than adding a second one, and deletes the now-unused PBR fragment shader instead of leaving it dead. One shader, one struct field, one flag. No new pipeline, pass, dependency or native file. |
| II. Fail Loudly | Pass — no new failure path. A model with no `Face` mesh is an ordinary state (no face light applies); the cutout discard is a test, not an error; a missing shader file fails at `loadShader` as every other shader does. |
| III. Spec-First | Pass — `specs/040-actor-cel-shading/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — `IsFace` is a `[<Struct>]` field on the existing `Mesh.Submesh` struct, set once at load; the cel uniform is a reused `byte array` written in the hot path; the face light is a `Vector3` value. No allocation, no object graph. |
| V. Minimise GC Pressure | Pass — the draw path reuses `Mesh`'s uniform byte array and pushes raw bytes; the face-light direction is a value; nothing per-frame allocates (FR-011). |
| N-V. Testing is the Authoring Tool | Not engaged — it governs the narrative subsystem, which is untouched. |

## Complexity Tracking

None. Nothing here is a constitution violation or a new dependency. The feature *reduces* code (a
simpler fragment shader) and removes an unused one.

## Project Structure

### Documentation (this feature)

```text
specs/040-actor-cel-shading/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── cel-actor.md     # Phase 1 output — the shader/pipeline/draw surfaces
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
shaders/
├── cel.frag             # NEW: the cel actor fragment shader (replaces mesh.frag). Two-band soft
│                        #   ramp, additive colour, idealized face light, alpha-test cutout.
├── cel.frag.spv         # NEW: compiled with glslc (committed, like every other .spv).
└── mesh.frag(.spv)      # DELETED: the PBR actor fragment shader this replaces (actor-only, unseen
                         #   after the swap). mesh.vert(.spv) is kept — the skinning vertex stage.

render/
├── Gpu.fs               # CHANGED: `createMeshPipeline` -> `createCelPipeline` (loads cel.frag.spv
│                        #   with 1 sampler); `Gpu.MeshPipeline` -> `Gpu.CelPipeline`; create/destroy.
└── Mesh.fs              # CHANGED: `Submesh` gains `IsFace : bool`; `draw` drops `cameraPos`, gains
                         #   `faceDir`, writes the 80-byte cel uniform and binds 1 sampler.

world/
├── Assets.fs            # CHANGED: `loadActors` sets each submesh's `IsFace` from the IQM mesh name.
└── ActorRender.fs       # CHANGED: compute the actor's idealized face-light direction from its model
                         #   matrix (a pure helper) and pass it to `Mesh.draw`.

screen/
└── GameplayScreen.fs    # CHANGED: pass `shared.Gpu.CelPipeline` to `ActorRender.draw`.

fsharp-game.fsproj        # CHANGED: CopyShaders — `mesh.frag.spv` out, `cel.frag.spv` in.
test/
├── ActorTests.fs        # CHANGED: the pure face-mesh name rule and face-light direction.
└── (or test/CelTests.fs + its .fsproj entry, if the additions do not fit ActorTests)
docs/
├── RENDERING_NOTES.md   # CHANGED: the actor pipeline is now the cel pipeline.
└── PROJECT_STRUCTURE.md # (only if a new module is added.)
```

**Structure Decision**: the whole change lives on the **actor render path** — the shader pair that
only actors use. The fragment shader is replaced in place (`mesh.frag` → `cel.frag`), so the pipeline
count is unchanged and the environment/prop path (`static.*`, `StaticMesh`, `EnvironmentBatch`) is
untouched. The `Face` flag is a `[<Struct>]` field carried on `Mesh.Submesh` from the IQM mesh name
(the parser already reads names) — no re-read of the model at draw time and no new authoring. The
face-light direction is computed in `ActorRender.draw` from the same model matrix it already
receives, using the **existing** project convention (`Vector3.Transform(UnitZ, rotation)` flattened
and elevated) that `Systems.forwardOf`/`yawOf` already encode, so it needs no new state and no change
to the actor or entity records.

## Phase 0: Research

See [research.md](./research.md). It settles why the actor pipeline is retargeted rather than
duplicated; why the cel uniform is lean (dropping normal/metallic-roughness/emissive/camera and the
`hasAlbedo` gate, given the shared white placeholder); why ramp parameters are shader constants; how
the `Face` flag is identified and carried; and how the idealized face light is derived to match the
reference.

## Phase 1: Design

- [data-model.md](./data-model.md) — the one data change (`Mesh.Submesh.IsFace`), the cel fragment
  uniform block and its byte layout, and the face-light direction.
- [contracts/cel-actor.md](./contracts/cel-actor.md) — the surfaces that change (shader, `Gpu`,
  `Mesh`, `ActorRender`, `Assets`, `GameplayScreen`, `.fsproj`) and what explicitly does not.
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite and the interactive
  walkthrough.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one shader replaced, one field, one flag; a deleted shader; no new pipeline/pass/dependency. |
| II. Fail Loudly | Pass — no new failure path; a model with no `Face` mesh is a normal state. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — a struct field, a reused byte array, a `Vector3`. |
| V. Minimise GC Pressure | Pass — nothing allocates per frame. |
| N-V. Testing is the Authoring Tool | Not engaged. |
