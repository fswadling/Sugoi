# Contract: `Zone.setTransform` (In-Place Mesh Transform)

The public signature does not change; only the behaviour of the mesh-backed path.

## Surface — `world/Zone.fs`

```fsharp
// Unchanged signature. Dispatches by kind: actor → capsule body, prop/part → mesh body.
val setTransform :
    zone:Zone -> world:Physics.World -> ref:EntityRef -> transform:Transform -> unit

// Private, changed:
//   setMeshTransform : Zone -> Physics.World -> SlotMap<Prop> -> EntityRef -> Transform -> unit
```

## Behaviour contract

Given a live `Prop`-backed ref (a placed prop or an environment part):

1. **Scale unchanged (move / rotate)** — the existing body MUST be updated in place
   (position + rotation); the body MUST NOT be destroyed or recreated, and the mesh
   shape MUST NOT be rebuilt. The body id is stable and `zone.BodyToEntity` is
   unchanged.
2. **Scale changed** — the body MUST be destroyed and rebuilt from the collider with
   the new scale baked in (uniform or non-uniform); `zone.BodyToEntity` MUST no
   longer contain the old body and MUST contain the new one.
3. **No collider (`Physics = None`)** — the entity's transform MUST still be updated;
   no body work is performed.
4. **Stale/absent ref** — no-op, as today.

Invariants that MUST hold after any of the above:

- `Zone.isAlive zone ref` is true, and `Zone.tryEntityAt zone ref` returns the entity
  with the new transform.
- The body's transform matches the entity (body position = entity position for a
  mesh body; body rotation = entity rotation).
- `zone.BodyToEntity` maps the entity's current body back to `ref`.

## Unchanged contracts

- `Physics.setBodyTransform`, `Physics.createMeshBody`, `Physics.destroyBody` —
  reused as-is, no signature or behaviour change.
- `Zone.meshBody` / `Zone.meshCollider` — still bake scale and still fail loudly on
  empty/invalid submesh ranges.
- The actor (capsule) branch of `setTransform` — unchanged.
- Command/undo, entity refs, and the environment/parts model — unchanged.

## Rules

- The in-place path MUST NOT allocate (no re-baked vertex array, no new shape).
- Scale equality is the rebuild trigger; taking the rebuild path when scales are
  effectively equal is always acceptable (a performance fallback, never a
  correctness failure).
- No new public API; `setMeshTransform` stays private.
