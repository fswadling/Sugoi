# Implementation Plan: In-Place Mesh Transform

**Branch**: `021-in-place-mesh-transform` | **Date**: 2026-09-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/021-in-place-mesh-transform/spec.md`

## Summary

Dragging or nudging a mesh-colliding prop stalls the frame for ~200–280 ms. The
cause is `Zone.setMeshTransform` (`world/Zone.fs`): every transform change destroys
the entity's Jolt body and rebuilds it via `Zone.meshBody`, which re-bakes the
entity scale into a fresh vertex array and constructs a new `MeshShape` (and its
BVH). Because a mesh shape cannot be scaled on the body, scale is the only property
that invalidates the shape — position and rotation live on the body.

The fix is a single guard in `setMeshTransform`: if the scale is unchanged, move the
existing body with `Physics.setBodyTransform` (already present) instead of
destroying and rebuilding; only a scale change takes the rebuild path. No new types,
no public API change.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`).

**Primary Dependencies**: `SDL3-CS` 3.4.14.1 (SDL_gpu) and `JoltPhysicsSharp` (Jolt
Physics). No new dependency.

**Storage**: N/A.

**Testing**: xUnit. The path is headlessly testable: `ZoneTests.fs` already builds a
fake `Assets.AssetManager` (zero device) and a real `Physics.create ()` world, and
reads bodies via `zone.Props.Get … .Physics.Value.Body` and `zone.BodyToEntity`.
A prop asset can be placed in the fake manager's `Props` dictionary, spawned through
`Zone.spawnProp`, and then transformed — all without a GPU.

**Target Platform**: Linux x64 desktop, Vulkan backend.

**Project Type**: native desktop game.

**Performance Goals**: eliminate the per-frame ~200–280 ms `update` stall during a
prop drag/nudge; the move/rotate path must not allocate or rebuild the shape/BVH.

**Constraints**: compile order unchanged (`world/` only); preserve collider fidelity
and the body→entity mapping; actor (capsule) colliders out of scope; no new public
API unless unavoidable.

**Scale/Scope**: one private function (`Zone.setMeshTransform`) plus tests. No schema
or interface change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: one guard clause; reuses the existing
  `Physics.setBodyTransform`. No new abstraction, type, or public function.
- **II. Fail Loudly** — PASS: unchanged. Scale changes still rebuild; collider
  derivation still fails loudly on bad geometry (`meshCollider`).
- **III. Spec-First** — PASS: `spec.md` precedes this plan and the tasks.
- **IV. Data-Oriented Architecture** — PASS: no new allocation or indirection; the
  body is updated in place through struct records already in use.
- **V. Minimise GC Pressure** — PASS: *removes* a per-change allocation (the
  `Array.init` that re-bakes scaled vertices) from the move/rotate path.

Re-checked after Phase 1 design: no new obligations; still PASS.

## Project Structure

### Documentation (this feature)

```text
specs/021-in-place-mesh-transform/
├── plan.md                  # This file (/speckit-plan output)
├── research.md              # Phase 0 output
├── data-model.md            # Phase 1 output
├── quickstart.md            # Phase 1 validation guide
├── contracts/
│   └── transform-contract.md # Behavioural contract for Zone.setTransform
├── checklists/
│   └── requirements.md      # /speckit-specify quality gate
└── tasks.md                 # /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
world/Zone.fs        # CHANGED: setMeshTransform updates the body in place on an
                     #          unchanged scale; rebuilds only when scale changes
world/Entity.fs      # UNCHANGED: Transform / MeshCollider / PhysicsAttachment reused
physics/Physics.fs   # UNCHANGED: Physics.setBodyTransform reused
test/ZoneTests.fs    # CHANGED: add a prop-asset fixture and body-identity tests
                     #          (unchanged scale keeps the body; scale change rebuilds)
```

**Structure Decision**: the fix stays inside the existing private mesh-transform
path in `world/Zone.fs`. `Entity`/`Physics` already expose everything needed
(`Transform.Scale`, `PhysicsAttachment.Body`, `Physics.setBodyTransform`), so nothing
else changes. Tests extend `ZoneTests.fs`, which already owns the headless
asset/physics fixture and the `setTransform` tests.

## Complexity Tracking

No constitution violations to justify.
