# Contract: Physics + Picking

Public surface added or changed by this feature. Signatures are illustrative F#,
matching the data model.

## Physics adapter — `physics/Physics.fs`

```fsharp
module Physics

type BodyId = BodyId of uint32

type World
    // Jolt PhysicsSystem + BodyInterface + gravity; created once and stepped per frame

val create : unit -> World
val step : world:World -> dt:float32 -> unit

val createMeshBody : world:World -> position:Vector3 -> rotation:Quaternion -> vertices:Vector3 array -> indices:int array -> BodyId
val createCapsuleBody : world:World -> position:Vector3 -> halfHeight:float32 -> radius:float32 -> BodyId

val destroyBody : world:World -> BodyId -> unit
val setBodyTransform : world:World -> BodyId -> position:Vector3 -> rotation:Quaternion -> unit

val raycast : world:World -> origin:Vector3 -> dir:Vector3 -> (BodyId * float32) option
```

## Rules

- `Physics` is a leaf: it depends on `System.Numerics` and `JoltPhysicsSharp`
  only, and never on `Entity`, `Zone`, or the GPU.
- `World.create` creates the Jolt `PhysicsSystem` with gravity and a body
  interface; `step` advances it by `dt` seconds.
- Mesh bodies are created from the asset's triangles (props/environment);
  capsule bodies from actor bounds. Empty mesh ranges and missing/invalid
  bounds raise (constitution II).
- `setBodyTransform` writes a body's position/rotation in place; the zone does not
  use it for editor transforms, instead destroying and rebuilding the body from
  the cached collider on transform change (a mesh shape cannot be re-scaled on an
  existing body).
- `raycast` returns the nearest hit `BodyId` and distance along the ray, or
  `None`; the caller maps the body back to an entity via the entity↔body map.
- Destroying an entity destroys its body and removes the entity↔body entry (no
  orphan bodies).
