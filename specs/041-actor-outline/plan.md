# Implementation Plan: Actor Outline

**Branch**: `041-actor-outline` | **Date**: 2026-09-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/041-actor-outline/spec.md`

## Summary

Give actors the drawn silhouette they are missing: a second, **inverted-hull** pass over the same
posed geometry the cel shader (spec 040) already draws — vertices pushed out along a **smoothed
normal**, rendered with **front-face culling** in a flat colour, hidden by the depth buffer
everywhere except the ring past the silhouette. Origins (research.md D1): Wikipedia's cel-shading
"wireframe method", Godot's **Grow**, and `odin-game`'s own deferred plan.

The critical finding is the asset's normals: Akane is **~79% hard-edged** at coincident vertices and
her **hair mesh is ~100% hard-edged** (2 694 of 2 696). Extruding along the authored per-face normal
would tear the outline open at every crease, worst on the hair. So the extrusion direction is a
per-vertex **averaged** normal, welded by position and computed once at load (research D3) — the
feature's central requirement (FR-005, SC-003), not an optional polish.

The engine already has everything else: SDL_gpu exposes front-face culling and depth bias, the actor
draw already binds the bone/morph buffers, and spec 040 left the actor pipeline actor-only.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: **none new.** SDL3-CS (SDL_gpu); `glslc` (present) compiles the one new
fragment shader. `mesh.vert` is extended, not duplicated (research D2).

**Storage**: none. One new derived vertex attribute (the smoothed normal), computed at load from
geometry the parser already reads; no authored format changes and nothing persisted.

**Testing**: `test/IqmTests.fs` (extended) covers the pure part — `Iqm.smoothNormals` (weld coincident
positions, average their normals, normalize; a cube corner averages its three face normals; distinct
positions stay separate). The outline itself needs a GPU and is the interactive walkthrough.

**Target Platform**: Linux x64 desktop, SDL3 + SDL_gpu.

**Project Type**: single-project native desktop game with a docked ImGui editor.

**Performance Goals**: hold 60 FPS. The outline adds one draw per actor submesh (the actor's actor-only
mesh, doubled). The draw path adds no per-frame allocation (FR-011): the smoothed normals are computed
once at load; the outline uniform bytes array is reused. The extrusion is one vertex-shader uniform
branch (`width > 0`) and a world-space add.

**Constraints**: actors only (FR-002); within the existing scene pass, no offscreen targets (FR-003);
follows the skinned/morphed pose (FR-004); **gap-free at creases** via smoothed normals (FR-005);
masked by the material's alpha cutout (FR-006); no bleed-through / correct occlusion (FR-007);
world-space shell (FR-008); colour and width constants (FR-009); cel
shading unchanged (FR-010); no per-frame allocation (FR-011); existing behaviour and tests unchanged
(FR-012, FR-013).

**Scale/Scope**: `core/Iqm.fs` gains `smoothNormals`; `render/Mesh.fs` grows the vertex stride 40 → 52
and pushes the outline uniforms; `shaders/mesh.vert` gains the smoothed-normal input and the
world-space extrusion; `shaders/outline.frag` and `Gpu.createOutlinePipeline` are new; `world/Assets.fs`,
`world/ActorRender.fs` and `screen/GameplayScreen.fs` are wired; one test file grows; `.fsproj`
`CopyShaders` gains `outline.frag.spv`. No new pass, no new target, no new dependency.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — the outline is one extra draw over geometry that already exists; `mesh.vert` is extended rather than duplicated; one new shader. The smoothed normal is derived, not authored. No new pass, target, pipeline count beyond one front-cull pipeline, or dependency. |
| II. Fail Loudly | Pass — no new failure path. A mesh with a single vertex, or one with no coincident vertices, is an ordinary case: `smoothNormals` returns the authored normal; nothing to report. |
| III. Spec-First | Pass — `specs/041-actor-outline/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — the smoothed normal is a per-vertex `float32` triple in the existing `[<Struct>]`-driven, struct-of-arrays mesh; the outline uniform is a reused `byte array`; the extrusion is a `Vector3` value. |
| V. Minimise GC Pressure | Pass — the smoothed normals are computed once at load; the draw reuses buffers and allocates nothing per frame (FR-011). |
| N-V. Testing is the Authoring Tool | Not engaged — narrative subsystem, untouched. |

## Complexity Tracking

None. Nothing here is a constitution violation or a new dependency; the one added pipeline is inherent
to a second rendering pass and reuses the existing vertex stage.

## Project Structure

### Documentation (this feature)

```text
specs/041-actor-outline/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── outline.md       # Phase 1 output — the shader/pipeline/draw surfaces
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
shaders/
├── mesh.vert            # CHANGED: + aSmoothNormal (location 5); + the outline-params vertex uniform
│                        #   (slot 2); world-space extrusion, clamped on screen, before projection.
├── outline.frag         # NEW: flat outline colour, alpha-cutout discard. 1 sampler, 0 uniforms.
└── outline.frag.spv     # NEW: compiled with glslc (committed).
core/
└── Iqm.fs               # CHANGED: pure `smoothNormals` (weld coincident positions, average normals).
render/
├── Gpu.fs               # CHANGED: cel pipeline vertex layout 52-byte/6-attr + 3 vertex UBOs; new
│                        #   `createOutlinePipeline` (front-cull, depth bias) + `Gpu.OutlinePipeline`.
└── Mesh.fs              # CHANGED: interleave 52-byte stride (adds the smoothed normal); `create`
                         #   gains smoothNormals; `draw` pushes the outline params (width 0) and a
                         #   new `drawOutline` (PerDraw + outline params + 1 sampler, no frag uniform).
world/
├── Assets.fs            # CHANGED: `loadActors` computes + passes the smoothed normals.
└── ActorRender.fs       # CHANGED: `draw` issues the outline pass, then the cel pass.
screen/
└── GameplayScreen.fs    # CHANGED: pass `shared.Gpu.OutlinePipeline`.
fsharp-game.fsproj        # CHANGED: CopyShaders — add `shaders/outline.frag.spv`.
test/
└── IqmTests.fs          # CHANGED: `smoothNormals` (weld/average/normalize).
docs/
└── RENDERING_NOTES.md   # CHANGED: §14 the actor outline pass.
```

**Structure Decision**: everything lives on the **actor render path** introduced by spec 040. The
outline is a second draw over the *same* mesh buffers, using the *same* vertex shader (extended with
one input and one uniform) paired with a new flat fragment shader and a front-cull pipeline, so there
is one source of truth for the skinned pose (research D2). The smoothed normal is a **derived** vertex
attribute computed at load (`Iqm.smoothNormals`) — no authored change, no second asset file — carried
in the mesh buffer at a new stride. Nothing in the environment/prop static path changes.

## Phase 0: Research

See [research.md](./research.md). It settles why the hull is the method (and the post-process family is
excluded); why `mesh.vert` is extended rather than duplicated; why the extrusion direction must be a
position-welded average; how the vertex buffer and attribute layout grow; how the outline's
world-space width is produced; and how the outline shader masks the alpha cutouts.

## Phase 1: Design

- [data-model.md](./data-model.md) — the new vertex attribute and stride, the `smoothNormals`
  derivation, and the outline vertex/fragment uniforms.
- [contracts/outline.md](./contracts/outline.md) — the surfaces that change (shaders, `Gpu`, `Mesh`,
  `Iqm`, `Assets`, `ActorRender`, `GameplayScreen`, `.fsproj`) and what does not.
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite and the walkthrough.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one extra draw, one shader, one extended vertex stage, one derived attribute; no new target/pass/dependency. |
| II. Fail Loudly | Pass — no new failure path; a mesh without creases is ordinary. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — a per-vertex float triple in the existing SoA mesh; a reused uniform array. |
| V. Minimise GC Pressure | Pass — load-time derivation; nothing per frame. |
| N-V. Testing is the Authoring Tool | Not engaged. |
