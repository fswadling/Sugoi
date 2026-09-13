# Data Model: Actor Skinning Hot Path

No new types. The change touches how existing values are produced, not their shape.

## `ActorModel` — `world/Actor.fs` (unchanged)

```fsharp
type ActorModel =
    { Model : Iqm.Model
      InverseBind : Matrix4x4 array }   // computed once at load
```

Reads per frame: `Model.Joints`, `Model.Poses`, `Model.PoseChannelOffsets`,
`Model.Anims`, `Model.FrameChannels`, `Model.NumFrameChannels`, `InverseBind`.

## `PoseFrame` — `world/Actor.fs` (unchanged)

```fsharp
type PoseFrame =
    { Local : Matrix4x4 array
      World : Matrix4x4 array
      Skinning : Matrix4x4 array }
```

Allocated once in `createPoseFrame` and mutated in place. `World` is read for the
parent chain; `Skinning` is uploaded; `Local` is retained (tests / structure).

## `AnimationSlot` — `world/Actor.fs` (unchanged)

The current clip index, mutable frame, playback rate, and loop flag.

## The invariant

> `samplePose` is a pure function of (model, clip, frame, loop); its outputs are the
> per-joint `Local`/`World`/`Skinning` matrices. The refactor must produce the same
> matrices and allocate nothing.

- `sampleJoint` returns the joint's sampled `(translation, rotation, scale)` as a
  **value** (`struct` tuple) — it must not escape as a reference tuple.
- `localMatrix` must produce a matrix equal to `S * R * T` (row-vector order).

## Validation rules

- `Local`/`World`/`Skinning` MUST equal the pre-change values (FR-003).
- The sample path MUST allocate nothing per frame (FR-001).
- `sampleJoint`'s return type MUST be a value type (FR-001).
- `localMatrix` MUST equal `CreateScale(s) * CreateFromQuaternion(r) *
  CreateTranslation(t)` (FR-002).
- Zero joints / zero frames / non-positive anim index MUST remain no-ops (FR-006).
