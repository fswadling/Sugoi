# Research: Physics + Picking

Decisions derived from the roadmap phase 6, the odin-game/ocaml-game reference
architecture, and the Jolt Physics .NET binding landscape.

## Decision 1 — Jolt Physics via `JoltPhysicsSharp`

- **Decision**: use Jolt Physics through the `JoltPhysicsSharp` .NET binding
  (v2.22.0, targeting net9.0/net10.0, MIT, by Amer Koleci), which wraps the Jolt
  C++ engine via `joltc` and ships native binaries in `JoltPhysics.Native`.
- **Rationale**: Jolt is a full rigid-body engine (BVH broadphase, capsule/box/
  hull/mesh/heightfield shapes, ray casts, character controller) and the phase
  needs accurate picking plus a foundation for later locomotion. The binding is
  actively maintained and matches the project's `net10.0` target.
- **Alternatives considered**: `Box3D` (rejected — minimal, already unused);
  Bullet/PhysX (rejected — no maintained .NET binding as convenient as
  `JoltPhysicsSharp`). Note: the `Jolt.Net` NuGet package is an unrelated
  JSON-transformation library and must not be used.

## Decision 2 — Mesh colliders from actual geometry

- **Decision**: props and environment parts get a static mesh (BVH) collider built
  from the asset's actual glTF triangles, kept CPU-side on the asset. The entity
  scale is baked into the vertices at body build (a mesh shape cannot be scaled on
  the body); the body carries position + rotation.
- **Rationale**: a box collider from the AABB encloses a room shell's whole
  interior, so a ray cast from inside hit the shell at fraction 0 and won every
  pick. Mesh colliders make picking match the drawn shape exactly.
- **Alternatives considered**: box from AABB (rejected — the room-shell
  regression); convex hull (rejected — less accurate than mesh, and a hull of a
  concave room is still not correct).

## Decision 3 — Capsule collider for the actor

- **Decision**: the actor gets a capsule collider, with radius/height derived from
  the actor asset bounds; missing/invalid bounds fail loudly.
- **Rationale**: a capsule is the standard character-body shape and maps directly
  to Jolt's `CapsuleShape`.
- **Alternatives considered**: box (rejected — poor fit for a humanoid); sphere
  (rejected — no height).

## Decision 4 — One world for picking and simulation

- **Decision**: a single Jolt `PhysicsSystem` serves both viewport picking and
  (later) play-mode simulation.
- **Rationale**: matches the reference architecture's "one world" decision and
  avoids two divergent sources of truth.
- **Alternatives considered**: separate editor/simulation worlds (rejected —
  duplicated state, more sync).

## Decision 5 — Transform sync through the command path

- **Decision**: collider create/remove/update is wired into the existing spawn,
  remove, and command/undo seams in `Zone` and `Command`, not polled every frame.
- **Rationale**: single seam; colliders never drift from entities; matches the
  reference "reconcile on edit" decision.
- **Alternatives considered**: per-frame reconcile (rejected — needless work and
  GC pressure).

## Decision 6 — Collider raycast for picking

- **Decision**: viewport selection casts the existing camera ray against the Jolt
  world (colliders) and maps the hit body back to an entity, replacing the AABB
  slab test for entity selection.
- **Rationale**: accurate selection matches the drawn shape, including rotated/
  non-uniformly scaled props.
- **Alternatives considered**: keep AABB picking (rejected — the phase exists to
  make picking accurate). Gizmo-handle segment picking stays as-is.

## Decision 7 — Handle-based `Physics` leaf adapter

- **Decision**: `physics/Physics.fs` exposes a `World` and opaque `BodyId` handles
  (`createMeshBody`/`createCapsuleBody`/`destroyBody`/`setBodyTransform`/`step`/
  `raycast`) with no dependency on `Entity` or the GPU.
- **Rationale**: keeps the adapter a leaf and testable headless; the entity↔body
  association lives in the gameplay/zone layer.
- **Alternatives considered**: bodies embedded in entity records with a direct
  Jolt dependency (rejected — couples entities to the physics library).

## Decision 8 — Native library copied next to the apphost

- **Decision**: the Jolt native library from `JoltPhysics.Native` is copied next
  to the apphost via an `AfterTargets="Build"` target, mirroring the existing
  SDL3_image/libjbig copy step.
- **Rationale**: the app dir is the reliable lookup location on Linux for native
  dependencies.
- **Alternatives considered**: rely on the system library path (rejected — not
  portable across machines).
