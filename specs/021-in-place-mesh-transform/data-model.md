# Data Model: In-Place Mesh Transform

No new types. The feature changes the *update path* of existing records; the
invariant it relies on is that an entity's scale is baked into its mesh collider
geometry, so only a scale change invalidates the collider shape.

## Existing types (unchanged)

### `Transform` — `world/Entity.fs`

```fsharp
type Transform =
    { mutable Position : Vector3
      mutable Rotation : Quaternion
      mutable Scale : Vector3 }
```

Position and rotation live on the physics body; **scale does not** — a Jolt mesh
shape cannot be scaled on the body.

### `MeshCollider` — `world/Entity.fs`

```fsharp
type MeshCollider =
    { Positions : float32 array   // node-local, unscaled
      Indices : int array }
```

### `PhysicsAttachment<'Collider>` — `world/Entity.fs`

```fsharp
type PhysicsAttachment<'Collider> =
    { Collider : 'Collider
      Body : Physics.BodyId }
```

`Prop.Physics : PhysicsAttachment<MeshCollider> option` — `None` for a part from
which no collider can be derived (no body work on transform).

## The invariant

> A body is valid for its entity's geometry **iff** the entity's current scale equals
> the scale the body was built from.

`Zone.meshBody` bakes `transform.Scale` into the vertices it feeds to
`Physics.createMeshBody`; the body carries position + rotation only. Therefore:

- move / rotate (scale unchanged) → body remains valid; update its transform in
  place.
- scale changed → body shape is stale; destroy and rebuild with newly baked vertices.

## State transitions for `Zone.setMeshTransform`

| Input state | Transform change | Action | Body id | `BodyToEntity` |
|---|---|---|---|---|
| `Physics = Some`, scale unchanged | move/rotate | `Physics.setBodyTransform` in place | unchanged | unchanged |
| `Physics = Some`, scale changed | any axis, uniform or not | `destroyBody` + `meshBody` | new | old removed, new mapped |
| `Physics = None` | any | update transform only | n/a | n/a |
| stale/absent ref | any | no-op | n/a | n/a |

## Validation rules

- The collider MUST match the transformed geometry after any change (FR-003):
  position/rotation via the body, scale via a rebuild.
- The body→entity mapping MUST resolve the same entity after an in-place update
  (FR-004): the body id does not change, so the existing entry stays correct.
- The in-place path MUST NOT allocate or rebuild the shape (FR-007/FR-008).
- A `Physics = None` entity MUST still receive the transform update (FR-005).

## Relationships

- `Prop` —(has one)→ `PhysicsAttachment<MeshCollider>` —(Body)→ Jolt body;
  `Zone.BodyToEntity` maps the body id back to the entity ref.
- `Zone.setTransform` → `setMeshTransform` (placed props via `zone.Props`, parts via
  the environment's parts map).
- Unchanged: physics world (`SharedState`), asset manager, entity/ref model.
