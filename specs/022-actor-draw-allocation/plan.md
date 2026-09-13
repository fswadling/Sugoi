# Implementation Plan: Actor Draw Allocation

**Branch**: `022-actor-draw-allocation` | **Date**: 2026-09-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/022-actor-draw-allocation/spec.md`

## Summary

`ActorRender.draw` (`world/ActorRender.fs`) binds each actor's bone storage buffer
with a freshly allocated one-element array:

```fsharp
do SDL.BindGPUVertexStorageBuffers(renderPass, 0u, [| r.BoneBuffer |], 1u)
```

The array literal allocates on every draw, for every actor, every frame. Replace it
with a per-instance array created once in `ActorRender.create`, following the
pattern the mesh, static-mesh, and debug-draw paths already use for their binding
arrays. No behaviour, signature, or lifecycle change.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`).

**Primary Dependencies**: `SDL3-CS` 3.4.14.1 (SDL_gpu). No new dependency.

**Storage**: N/A.

**Testing**: xUnit. GPU draw behaviour can't be unit-tested headlessly, so
correctness is covered by `dotnet build` + the existing 103-test suite, plus a
runtime check that an animated actor renders identically. The allocation removal
itself is verified by inspection (the preallocated array is the only binding input).

**Target Platform**: Linux x64 desktop, Vulkan backend.

**Project Type**: native desktop game.

**Performance Goals**: zero per-frame allocation on the actor draw path (currently
one small array per actor per frame).

**Constraints**: SDL's `BindGPUVertexStorageBuffers` takes `nativeint array` (or
`ReadOnlySpan<nativeint>`), not a named `GPUBuffer` type. Compile order unchanged
(`world/` only). Priority principle V (Minimise GC Pressure).

**Scale/Scope**: one field on `ActorRender` plus its use in `draw`; no schema,
interface, or lifecycle change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: one field, one call-site change; reuses the existing
  binding-array pattern.
- **II. Fail Loudly** — PASS: unaffected; no new failure path.
- **III. Spec-First** — PASS: `spec.md` precedes this plan and the tasks (recorded
  retroactively for a change already made).
- **IV. Data-Oriented Architecture** — PASS: the field is a plain array reference on
  an existing `[<Struct>]`; the renderer stays a value type.
- **V. Minimise GC Pressure** — PASS: this is the change; it removes the only
  per-frame allocation on the actor path.

Re-checked after Phase 1 design: no new obligations; still PASS.

## Project Structure

### Documentation (this feature)

```text
specs/022-actor-draw-allocation/
├── plan.md                  # This file (/speckit-plan output)
├── research.md              # Phase 0 output
├── data-model.md            # Phase 1 output
├── quickstart.md            # Phase 1 validation guide
├── contracts/
│   └── actor-draw-contract.md # Behavioural contract for ActorRender.draw
├── checklists/
│   └── requirements.md      # /speckit-specify quality gate
└── tasks.md                 # /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
world/ActorRender.fs   # CHANGED: add a per-instance bone-storage binding array
                       #          (created in `create`), use it in `draw`
world/Actor.fs         # UNCHANGED: samplePose / advanceInPlace already write in place
render/Mesh.fs         # UNCHANGED: already reuses its VertexBinding array
```

**Structure Decision**: the fix stays inside `world/ActorRender.fs`. `Mesh.draw`
already reuses a binding array, and `Actor.samplePose`/`advanceInPlace` write into
preallocated pose arrays, so this one array is the last per-frame allocation on the
actor path. No other file changes.

## Complexity Tracking

No constitution violations to justify.
