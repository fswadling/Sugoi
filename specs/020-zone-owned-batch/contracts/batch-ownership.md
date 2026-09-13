# Contract: Batch Ownership

Surface changed by this feature. The batch moves from the screen's state onto the
zone; the instance buffer becomes device-lazy.

## Zone — `world/Zone.fs`

```fsharp
// Now owns the static-scene batch. Creation still needs no device.
val create : unit -> Zone
val gatherStatic : device:Gpu.Device -> zone:Zone -> unit
```

- `Zone.create ()` allocates no GPU memory.
- `gatherStatic` reads the zone's own `Props` and environment `Parts` and calls the
  batch's gather — the one place that binds a zone to its batch.

## Static batch — `world/EnvironmentBatch.fs`

```fsharp
val create :
    unit -> EnvironmentBatch

val gather :
    device:Gpu.Device ->
    batch:EnvironmentBatch ->
    props:SlotMap<Prop> ->
    parts:SlotMap<Prop> voption -> unit

val upload :
    device:Gpu.Device -> batch:EnvironmentBatch -> cmd:Gpu.CommandBuffer -> unit

val render :
    batch:EnvironmentBatch -> renderPass:Gpu.RenderPass -> cmd:Gpu.CommandBuffer ->
    pipeline:Gpu.Pipeline -> viewProjection:Matrix4x4 ->
    light:Light -> cameraPos:Vector3 -> unit

val release :
    device:Gpu.Device -> batch:EnvironmentBatch -> unit
```

- **Removed** from `gather`: the `Zone` parameter. It now takes the prop/part maps.
- **Added**: `release` (frees the instance buffer's current and retired GPU
  buffers).
- `gather` is the only place the batch allocates (via `ensure`), so a batch that is
  never rendered never touches the device.

## Instance buffer — `render/InstanceBuffer.fs`

```fsharp
// No device at creation and no allocation; Device is passed where GPU memory is used.
val create  : unit -> InstanceBuffer
val ensure  : device:Gpu.Device -> buf:byref<InstanceBuffer> -> instances:int -> unit
val upload  : device:Gpu.Device -> buf:InstanceBuffer -> cmd:Gpu.CommandBuffer -> usedBytes:int -> unit
val reclaim : device:Gpu.Device -> buf:byref<InstanceBuffer> -> unit
val release : device:Gpu.Device -> buf:byref<InstanceBuffer> -> unit
val binding : buf:InstanceBuffer -> start:int -> SDL.GPUBufferBinding
```

- `Buffer`/`Transfer` are `IntPtr.Zero` until the first `ensure`.
- `upload`/`binding` are only reached with a non-empty instance count, i.e. after
  `ensure` allocated the buffers.

## GameplayState — `state/GameplayState.fs`

```fsharp
// No batch field; creation needs no shared state (it only existed for the batch's device).
val create : debugDraw:DebugDraw.DebugDraw -> gizmoDraw:DebugDraw.DebugDraw -> GameplayState
```

## Gameplay screen — `screen/GameplayScreen.fs`

```fsharp
// Gathers and draws the *active zone's* batch.
val renderScene :
    shared:SharedState.SharedState ->
    state:inref<GameplayState.GameplayState> ->
    pass:ScenePass -> unit
```

- Gather: `Zone.gatherStatic shared.Gpu.Device state.Zone`.
- Upload: `EnvironmentBatch.upload shared.Gpu.Device state.Zone.Batch pass.Cmd`.
- Draw: `EnvironmentBatch.render state.Zone.Batch … shared.Gpu.StaticPipeline …`.

## Screen manager — `screen/ScreenManager.fs`

```fsharp
val init : gpu:Gpu.Gpu -> debugDraw:DebugDraw.DebugDraw -> gizmoDraw:DebugDraw.DebugDraw -> ScreenManager
// Builds SharedState around the GPU context and calls GameplayState.create debugDraw gizmoDraw.
```

## Run loop — `Engine.fs`

```fsharp
// log reads the active zone's batch
manager.Gameplay.Zone.Batch.DrawCalls / .InstanceCount

// shutdown, after WaitForGPUIdle and before Gpu.destroy:
//   EnvironmentBatch.release shared.Device manager.Gameplay.Zone.Batch
```

## Rules

- A zone MUST own exactly one batch, and a batch MUST be driven only with its own
  zone's maps (no cross-zone sharing).
- `Zone.create ()` MUST NOT require a device; allocation happens on first render.
- `release` MUST free the current and retired buffers and MUST run before the device
  is destroyed.
- `DebugDraw`, `GizmoDraw`, and `Camera` stay on `GameplayState`.
- No change to physics, commands, or the entity model.
- Compile order: `EnvironmentBatch.fs` before `Zone.fs`; `EnvironmentBatch` must not
  reference `Zone`.
