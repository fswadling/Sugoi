# Contract: `Actor.samplePose` (Actor Skinning Hot Path)

The public surface does not change; the sampler is refactored to allocate nothing
and do less work, producing identical output.

## Surface — `world/Actor.fs`

```fsharp
// Unchanged signature.
val samplePose :
    m:ActorModel -> animIndex:int -> frame:float32 -> loop:bool ->
    pose:PoseFrame -> unit

// Private, changed return type:
//   sampleJoint : Iqm.Model -> Iqm.Pose -> Iqm.Joint -> int
//                 -> struct (Vector3 * Quaternion * Vector3)
//
// Private, changed body (same signature):
//   localMatrix : Vector3 -> Quaternion -> Vector3 -> Matrix4x4
```

## Behaviour contract

1. **Output identity** — after `samplePose`, for every joint `j`, `pose.Local.[j]`,
   `pose.World.[j]`, and `pose.Skinning.[j]` MUST equal the pre-change values
   (`Skinning = InverseBind * World`; `World = Local * parentWorld` or `Local` for a
   root).
2. **No allocation** — the sample path MUST allocate no tuple, array, or list per
   frame; `sampleJoint`'s result MUST be a value type.
3. **Local transform** — `localMatrix t r s` MUST equal
   `Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) *
   Matrix4x4.CreateTranslation(t)` in row-vector order, while using one fewer
   multiply.
4. **No-ops** — a non-positive anim index, an out-of-range index, zero frames, or
   zero joints MUST leave the sampler a no-op, as today.

## Unchanged contracts

- `createModel`, `createPoseFrame`, `createSlot`, `findAnimIndex`,
  `advance`/`advanceInPlace` — signatures and behaviour.
- The channel-sampling logic (`sampleJoint`'s mask loop and `match`) — unchanged.
- `ActorRender.upload`/`draw` — already allocation-free (022); the sampler's outputs
  are consumed exactly as before.
- `PoseFrame` shape (three matrix arrays) and the actor/zone lifecycle.

## Rules

- This is a pure refactor of the sampler: same inputs, same outputs, less work.
- No new types, no new fields, no public API change.
- Correctness is guarded by golden tests over a model with active channels.
