# Data Model: Actor Draw Allocation

No new types. One field is added to an existing renderer struct.

## `ActorRender` — `world/ActorRender.fs`

```fsharp
[<Struct>]
type ActorRender =
    { Asset : ActorAsset.ActorAsset
      mutable Animation : Actor.AnimationSlot
      Pose : Actor.PoseFrame
      BoneBuffer : Gpu.Buffer                 // GraphicsStorageRead, BonesMax * 64
      BoneStorageBuffers : nativeint array    // added: [| BoneBuffer |], reused by draw
      BoneTransfer : Gpu.TransferBuffer       // upload transfer buffer, same size
      BoneBytes : byte array }                // BonesMax * 64, reused
```

## The invariant

> Every per-frame binding input the actor draw needs is owned by the renderer
> instance and created once, so `draw` allocates nothing.

- `BoneBuffer` and `BoneStorageBuffers` are created together in `create`.
- `draw` passes `BoneStorageBuffers` to the storage-buffer bind; it never builds a
  new array.
- `Pose` (allocated in `createPoseFrame`) is written in place by `samplePose`;
  `BoneBytes` is reused by `upload`.

## Lifecycle

| Event | `BoneStorageBuffers` |
|---|---|
| `create` | allocated as `[| BoneBuffer |]` |
| `draw` (per frame) | read only — no allocation |
| `release` (actor removed) | GC'd with the instance; no GPU resource of its own |
| struct copy (draw takes `r` by value) | copies the array reference, not the array |

## Validation rules

- The draw path MUST reference only preallocated state (FR-001, FR-005).
- Exactly one storage buffer MUST be bound at slot 0 (FR-002).
- `release` behaviour and resource ownership are unchanged (FR-006): the array needs
  no explicit release because it owns no unmanaged resource; `BoneBuffer` and
  `BoneTransfer` are still released as before.
