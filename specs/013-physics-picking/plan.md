# Implementation Plan: Physics + Picking

**Branch**: `013-physics-picking` | **Date**: 2026-09-06 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/013-physics-picking/spec.md`

## Summary

Add a Jolt Physics world and give the scene real colliders: static mesh colliders
for props/environment parts (from actual geometry), a capsule collider for the actor,
accurate collider raycast picking (replacing the phase-2 AABB raycast), and
collider↔render transform sync through the existing editor/command paths. This
replaces the previously-referenced-but-unused `Box3D` package with
`JoltPhysicsSharp`.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`).

**Primary Dependencies**: `JoltPhysicsSharp` (2.22.0) + its `JoltPhysics.Native`
runtime package (replaces the unused `Box3D` reference), `SDL3-CS`,
`System.Numerics`. No other new managed dependency.

**Storage**: N/A — in-memory physics world. Collider shapes are derived at spawn
time from asset bounds; nothing is persisted.

**Testing**: xUnit — extend the test project with `test/PhysicsTests.fs` for
collider derivation (bounds → capsule, submesh-range merge) and the ray/picking
math. Headless: the Jolt world can be created and queried without a GPU device.

**Target Platform**: Linux x64 desktop (native).

**Project Type**: native desktop game.

**Performance Goals**: 60 fps. The world steps once per frame; colliders are
created/synced on editor edits (cold); picking raycasts only on click (not every
frame).

**Constraints**: F# compile order is the dependency order. `physics/Physics.fs`
stays a leaf over `JoltPhysicsSharp` + `System.Numerics` (no `Entity`/GPU
dependencies) so it can be inserted after `core/` and before `render/`. The Jolt
native library must be copied next to the apphost, matching the existing
`CopyImGuiBackend`/`CopySdl3ImageLibraries` pattern.

**Scale/Scope**: one physics world; one capsule (actor) plus mesh colliders for
props/environment parts (hundreds at most). Mesh + capsule shapes only this phase.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: one `Physics` adapter, mesh + capsule shapes only, no
  speculative abstractions; hull/heightfield colliders and locomotion are deferred.
- **II. Fail Loudly** — PASS: missing/invalid asset bounds raise; no silent
  zero-size or placeholder colliders.
- **III. Spec-First** — PASS: this spec/plan/tasks precede implementation.
- **IV. Data-Oriented Architecture** — PASS: bodies are handle-keyed; the
  entity↔body association is a flat map; collider data derives from existing asset
  bounds.
- **V. Minimise GC Pressure** — PASS: bodies are created on edit (cold); the
  per-frame step reuses the world; raycasts run only on click.

## Project Structure

### Documentation (this feature)

```text
specs/013-physics-picking/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 decisions
├── data-model.md        # Phase 1 data/behaviour model
├── quickstart.md        # Phase 1 validation guide
├── contracts/
│   └── physics.md       # Physics adapter interface
├── checklists/
│   └── requirements.md  # /speckit-specify quality gate
└── tasks.md             # /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
physics/Physics.fs          # NEW: Jolt adapter — world, mesh/capsule bodies, step, raycast
world/Entity.fs             # EXTENDED: MeshCollider/CapsuleCollider + PhysicsAttachment; Actor/Prop
                            #           carry one; Environment drops Props; AssetBinding.AssetId is option
world/Zone.fs               # EXTENDED: derive collider shape + create/destroy/rebuild bodies;
                            #           Props move to Zone.Props; Environment becomes option; BodyToEntity map
state/SharedState.fs        # EXTENDED: owns the Physics world
state/Command.fs            # EXTENDED: thread the world through spawn/remove/transform/undo
editor/Viewport.fs          # EXTENDED: route click-select through the physics raycast
screen/GameplayScreen.fs    # EXTENDED: step the world each frame
fsharp-game.fsproj          # add physics/Physics.fs; swap Box3D -> JoltPhysicsSharp; copy Jolt native lib
test/PhysicsTests.fs        # NEW: collider derivation + picking raycast tests
```

**Structure Decision**: `physics/Physics.fs` is a handle-based leaf adapter over
Jolt (`World`, `BodyId`, `createMeshBody`/`createCapsuleBody`, `step`,
`setBodyTransform`, `raycast`) with no dependency on `Entity` or the GPU. The
world lives on `SharedState` (one per game); each `Zone` owns the body→entity
reverse map, populated by `Zone` on spawn/remove and kept in sync by `Command`.
Picking routes the existing camera ray through `Physics.raycast` instead of the
AABB slab test; transform changes destroy and rebuild the body.

## Complexity Tracking

No constitution violations to justify.
