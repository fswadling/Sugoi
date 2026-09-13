# Contract: Instanced Static Draws

Surface added or changed by this feature. The static pipeline gains an instance
input slot; the batch lives in `world/` and draws one instanced call per distinct
submesh.

## Instance buffer — `render/InstanceBuffer.fs`

```fsharp
// One instance record is 112 bytes (four model rows + three normal rows, all Float4).
val stride : int

[<Struct>]
type InstanceBuffer =
    { Device : Gpu.Device
      mutable Buffer : Gpu.Buffer
      mutable Capacity : int      // instances
      mutable Transfer : Gpu.TransferBuffer
      Staging : byte array        // Capacity * 112 bytes
      mutable Retired : (Gpu.Buffer * int) list }  // superseded buffer + frames remaining

// Created with an initial capacity (0 is fine; the first gather sizes it to the
// scene's instance count), after which it only grows.
val create : device:Gpu.Device -> initialInstances:int -> InstanceBuffer

// Grow when needed: allocates a new GPU + transfer buffer and moves the old one
// to Retired. The old buffer is NOT released here (it may still be referenced by
// an in-flight frame).
val ensure : device:Gpu.Device -> buf:byref<InstanceBuffer> -> instances:int -> unit

// Call once per frame: release retired buffers once they have outlived
// MAX_FRAMES_IN_FLIGHT, so a superseded buffer is never freed while in use.
val reclaim : device:Gpu.Device -> buf:byref<InstanceBuffer> -> unit

// Upload `usedBytes` from Staging in a copy pass, before the scene pass.
val upload : buf:InstanceBuffer -> cmd:Gpu.CommandBuffer -> usedBytes:int -> unit

// The slot-1 binding for a group starting at instance `start` (byte offset start*112).
val binding : buf:InstanceBuffer -> start:int -> SDL.GPUBufferBinding
```

- No `world` dependency. Instance stride is 112 bytes (see the data model).

## Static pipeline — `render/Gpu.fs` `createStaticPipeline`

```fsharp
// EXTENDED: vertex slot 0 (32-byte position/normal/uv, InputRate.Vertex, unchanged)
// plus slot 1 (112-byte instance, InputRate.Instance, InstanceStepRate 0) with
// Float4 attributes at locations 3..9. NumVertexBuffers = 2, NumVertexAttributes = 10.
val createStaticPipeline : device:Device -> swapchainFormat:SDL.GPUTextureFormat -> depthFormat:SDL.GPUTextureFormat -> Pipeline
```

## Static draw — `render/StaticMesh.fs`

```fsharp
// CHANGED: PerDraw shrinks to { mat4 viewProj } (64 B). Model/normal come from the
// instance buffer. The static pipeline and viewProj are assumed bound/pushed by the
// caller for the whole block; this binds vertex slot 0, vertex slot 1 (offset),
// index buffer, pushes the material, binds samplers, and issues the instanced draw.
val drawInstanced :
    m:StaticMesh ->
    renderPass:Gpu.RenderPass ->
    cmd:Gpu.CommandBuffer ->
    instanceBinding:SDL.GPUBufferBinding ->
    instanceCount:uint32 ->
    light:Light ->
    cameraPos:Vector3 ->
    material:Material -> unit
```

- `viewProj` is pushed once per static block by the batch, not here.

## Batch — `world/EnvironmentBatch.fs`

```fsharp
// Dense registry: the pure planner is `register` (identity -> slot) plus
// `planOffsets` (per-slot counts -> start offsets, total, non-empty group count);
// both are headless-tested in test/EnvironmentBatchTests.fs. The batch is a
// long-lived reference type (it owns GPU buffers and is mutated through closures).
type EnvironmentBatch =
    { Device : Gpu.Device
      mutable Instances : InstanceBuffer.InstanceBuffer
      Registry : ResizeArray<StaticSubmesh>   // dense; index = identity slot
      Lookup : Dictionary<nativeint, int>      // uploaded vertex buffer -> slot
      mutable Counts : int array
      mutable Starts : int array
      mutable Cursor : int array
      ViewProjBytes : byte array
      mutable DrawCalls : int
      mutable InstanceCount : int }

val create : device:Gpu.Device -> EnvironmentBatch

// Walk Zone.Props + environment.Parts: register any new submesh identity, count,
// `ensure` the instance buffer holds the total instance count (this is
// what sizes it on the first gather), `reclaim` retired buffers, then fill
// model/normal rows into Instances.Staging. Reuses buffers. Raises on an
// out-of-range submesh range or an inconsistent registry.
val gather : batch:EnvironmentBatch -> zone:Zone -> unit

// Copy Staging to the GPU in a copy pass (before the scene pass).
val upload : batch:EnvironmentBatch -> cmd:Gpu.CommandBuffer -> unit

// Bind the static pipeline, push viewProj once, then one instanced draw per group
// with Count > 0. Sets DrawCalls / InstanceCount for the frame's log.
val render :
    batch:EnvironmentBatch ->
    renderPass:Gpu.RenderPass ->
    cmd:Gpu.CommandBuffer ->
    pipeline:Gpu.Pipeline ->
    viewProjection:Matrix4x4 ->
    light:Light ->
    cameraPos:Vector3 -> unit
```

- `render` issues one `DrawGPUIndexedPrimitives` per distinct submesh with
  `Count > 0`; `DrawCalls` equals that count and `InstanceCount` the total.

## World renderer — `world/EnvironmentRender.fs`

```fsharp
// CHANGED: `draw` no longer issues GPU calls. EnvironmentRender is the per-instance
// data holder the batch reads (asset, orientation, submesh range).
```

## Frame log — `Engine.fs`

The per-frame line gains the batch's counters:

```text
... | parts 463 | draws 191 instances 521
```

## Rules

- `DrawCalls` MUST equal the number of distinct submesh identities in the scene,
  not the number of instances (FR-007).
- Two assets sharing a submesh index MUST be distinct registry slots (FR-002).
- The batch reuses `Instances`, `Counts`, `Starts`, and `Cursor`; it allocates
  nothing per frame (FR-010).
- A superseded instance buffer MUST be retained until it can no longer be
  referenced by an in-flight frame (see `ensure`/`reclaim`), never released
  inline on growth.
- The mesh (actor) pipeline, line pipeline, and their draws are unchanged
  (FR-011).
