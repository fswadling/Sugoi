# Data Model: Physics + Picking

Phase 13 introduces a Jolt physics world, collider bodies for actors/props, and
accurate raycast picking. The physics adapter is a handle-based leaf; entities
gain a physics attachment (collider + body), `SharedState` owns the world, and
each `Zone` owns the body→entity association.

```mermaid
graph TD
    A[Asset geometry / bounds] --> C[Zone: derive shape]
    C --> P[Physics: create body]
    P --> W[Physics World (Jolt)]
    E[Entity] --> H[Body handle]
    H --> W
    W --> R[Physics.raycast]
    R --> M[Entity<->body map]
    M --> S[Selection]
```

## Physics adapter — `physics/Physics.fs`

```fsharp
type BodyId = BodyId of uint32

type World
    // Jolt PhysicsSystem + BodyInterface + gravity

val create : unit -> World
val step : world:World -> dt:float32 -> unit
val createMeshBody : world:World -> position:Vector3 -> rotation:Quaternion -> vertices:Vector3 array -> indices:int array -> BodyId
val createCapsuleBody : world:World -> position:Vector3 -> halfHeight:float32 -> radius:float32 -> BodyId
val destroyBody : world:World -> BodyId -> unit
val setBodyTransform : world:World -> BodyId -> position:Vector3 -> rotation:Quaternion -> unit
val raycast : world:World -> origin:Vector3 -> dir:Vector3 -> (BodyId * float32) option
```

- `World` owns the Jolt `PhysicsSystem`, its `BodyInterface`, and gravity.
- Bodies are created as static (props/environment) or kinematic (actor capsule)
  this phase; no gravity-driven dynamics are authored yet.
- `raycast` returns the nearest hit body and distance, or `None`.

## Entity extension — `world/Entity.fs`

```fsharp
// Collider shape data cached in the attachment so a transform change can
// rebuild the body without re-reading the asset. Props/parts are always mesh,
// actors always capsule — each entity attaches only its own shape.
type MeshCollider = { Positions : float32 array; Indices : int array }  // node-local, unscaled
type CapsuleCollider = { Radius : float32; HalfHeight : float32 }

// Collider + the body built from it, held together so a body can never exist
// without its collider. None = no collider (e.g. a stale environment-part binding).
type PhysicsAttachment<'Collider> = { Collider : 'Collider; Body : Physics.BodyId }

type Actor =
    { Base : Entity
      AssetId : string
      Physics : PhysicsAttachment<CapsuleCollider> option
      mutable Renderer : ActorRender.ActorRender }

type Prop =
    { Base : Entity
      AssetId : string option             // None = environment part
      SubmeshIndex : int
      SubmeshCount : int
      Physics : PhysicsAttachment<MeshCollider> option
      mutable Renderer : EnvironmentRender.EnvironmentRender }
```

- `Physics = None` means "no collider" (e.g. an environment part whose binding no
  longer resolves to a part); when attached, collider and body are created,
  destroyed, and recreated (on transform change) together.

## Entity↔body association — `state/SharedState.fs` + `world/Zone.fs`

```fsharp
// One physics world for the whole game, owned by SharedState.
type SharedState =
    { Assets : Assets.AssetManager
      Physics : Physics.World }

// Each zone owns the reverse lookup from body handle to entity ref (for picking).
type Zone =
    { ...
      BodyToEntity : Dictionary<Physics.BodyId, EntityRef> }
```

- The `Physics.World` is shared across zones on `SharedState`; each `Zone` keeps
  only its `BodyToEntity` reverse map.
- On spawn, `Zone` derives the shape from the asset, creates the body, and
  records `BodyId → EntityRef`.
- On remove, the body is destroyed and the map entry removed.
- On transform change, the body is destroyed and rebuilt from the cached collider
  (a mesh shape cannot be re-scaled on an existing body).

## Collider derivation

- **Prop/environment part (mesh)**: the submesh range's triangles are merged
  into one `MeshCollider` (node-local, unscaled); empty ranges fail loudly. The
  entity scale is baked into the vertices at body build (a mesh shape cannot be
  scaled on the body); position/rotation go on the body.
- **Actor (capsule)**: asset bounds → `radius` (half the horizontal extent) and
  `halfHeight` (half the vertical extent minus `radius`). Missing/invalid bounds
  fail loudly.

## Relationships

- `Physics` → `System.Numerics` + `JoltPhysicsSharp` only (leaf; no `Entity`/GPU).
- `Zone` → `Physics` + `Assets` + `Entity` (derive shape, create/destroy/rebuild bodies).
- `SharedState` → `Assets` + `Physics` (owns the world); `Zone` owns the
  body→entity map.
- `Viewport` → `Physics` (collider raycast); `Selection` consumes the result.
- Compile order: `physics/Physics.fs` is inserted after `core/Input.fs` and before
  `render/`, keeping `core → render → world` one-directional (physics is a leaf).
