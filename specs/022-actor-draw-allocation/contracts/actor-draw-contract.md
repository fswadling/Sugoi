# Contract: `ActorRender.draw` (Actor Draw Allocation)

The public signature does not change; only the source of the binding input does.

## Surface — `world/ActorRender.fs`

```fsharp
// Unchanged signature (r by value, as today).
val draw :
    r:ActorRender -> renderPass:Gpu.RenderPass -> cmd:Gpu.CommandBuffer ->
    pipeline:Gpu.Pipeline -> viewProjection:Matrix4x4 -> model:Matrix4x4 ->
    light:Light -> cameraPos:Vector3 -> unit

// Added field (populated in `create`):
//   BoneStorageBuffers : nativeint array   // [| BoneBuffer |]
```

## Behaviour contract

Given an actor renderer:

1. **Binding** — `draw` MUST bind the instance's `BoneBuffer` at vertex storage slot
   0 with exactly one binding.
2. **No allocation** — `draw` MUST NOT allocate any array or list; the binding input
   is the preallocated `BoneStorageBuffers`.
3. **Rendering** — the skinned draw MUST be identical to before (same buffer, slot,
   count, transform, material).
4. **Isolation** — each instance binds its own `BoneBuffer`; one actor's bind MUST
   NOT use another's buffer.

## Unchanged contracts

- `ActorRender.create`/`upload`/`advanceInPlace`/`playClip` signatures and behaviour.
- `Actor.samplePose` / `Actor.advanceInPlace` — write into preallocated pose arrays.
- `Mesh.draw` — already allocates nothing (reuses `VertexBinding`).
- `ActorRender.release` — still frees `BoneBuffer` and `BoneTransfer`; the binding
  array owns no unmanaged resource and needs no explicit release.
- Actor entity/ref model, command/undo, and the zone lifecycle.

## Rules

- The binding array is per instance, created once; a shared mutable array is not
  used.
- The field type is `nativeint array` to match the SDL3-CS binding.
