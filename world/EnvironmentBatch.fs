module EnvironmentBatch

// Gathers a zone's static scene (placed props + the environment's imported parts)
// into a per-frame instance buffer and draws one instanced call per distinct
// submesh. A zone owns its batch, so the gather takes the zone's prop/part maps
// rather than a `Zone` (which would make the compile order cyclic). The grouping
// key is the shared submesh's identity (its uploaded StaticMesh), not a bare submesh
// index: two assets can use index 0 for different geometry, so they must never be
// merged. Owns grow-only GPU/CPU buffers, allocated lazily on first gather;
// allocates nothing per frame.

open System
open System.Collections.Generic
open System.Numerics
open System.Runtime.InteropServices
open SDL3
open Gpu
open Texture
open StaticMesh
open EnvironmentAsset
open Entity
open SlotMap

// ── batch state ──
type EnvironmentBatch =
    { mutable Instances : InstanceBuffer.InstanceBuffer
      Registry : ResizeArray<StaticSubmesh>   // dense; index = identity slot
      Lookup : Dictionary<nativeint, int>      // uploaded vertex buffer -> slot
      mutable Counts : int array
      mutable Starts : int array
      mutable Cursor : int array
      ViewProjBytes : byte array
      mutable DrawCalls : int
      mutable InstanceCount : int }

// No device and no GPU allocation; buffers are created on the first gather.
let create () : EnvironmentBatch =
    { Instances = InstanceBuffer.create ()
      Registry = ResizeArray()
      Lookup = Dictionary()
      Counts = Array.empty
      Starts = Array.empty
      Cursor = Array.empty
      ViewProjBytes = Array.zeroCreate<byte> 64
      DrawCalls = 0
      InstanceCount = 0 }

// ── pure planning (headless-testable) ──

// The dense slot for a submesh identity, adding it on first sight. The key is the
// submesh's uploaded vertex buffer, so two assets' index-0 submeshes are distinct.
let register (lookup: Dictionary<nativeint, int>) (next: int) (key: nativeint) : int =
    match lookup.TryGetValue(key) with
    | true, slot -> slot
    | false, _ ->
    lookup.[key] <- next
    next

// Write each group's start offset into `starts`; return the total instance count
// and the number of non-empty groups (the frame's draw calls).
let planOffsets (counts: int array) (starts: int array) (count: int) : int * int =
    let mutable total = 0
    let mutable groups = 0
    for slot in 0 .. count - 1 do
        starts.[slot] <- total
        if counts.[slot] > 0 then groups <- groups + 1
        total <- total + counts.[slot]
    total, groups

// ── gather ──

let private slotFor (batch: EnvironmentBatch) (sub: StaticSubmesh) : int =
    let next = batch.Registry.Count
    let slot = register batch.Lookup next sub.Mesh.VertexBuffer
    if slot = next then
        batch.Registry.Add(sub)
        if next >= batch.Counts.Length then
            let capacity = max 4 (batch.Counts.Length * 2)
            let counts = Array.zeroCreate capacity
            do Array.blit batch.Counts 0 counts 0 batch.Counts.Length
            batch.Counts <- counts
            batch.Starts <- Array.zeroCreate capacity
            batch.Cursor <- Array.zeroCreate capacity
        batch.Counts.[next] <- 0
    slot

let private countInstance (batch: EnvironmentBatch) (r: EnvironmentRender.EnvironmentRender) : unit =
    let last = r.SubmeshIndex + r.SubmeshCount - 1
    for i in r.SubmeshIndex .. last do
        let slot = slotFor batch r.Asset.Submeshes.[i]
        batch.Counts.[slot] <- batch.Counts.[slot] + 1

// One instance = the model matrix's four rows then the normal matrix's three rows.
let private writeInstance (offset: int) (model: Matrix4x4) (normal: Matrix4x4) (staging: byte array) : unit =
    do writeMatrix staging offset model
    let rows = MemoryMarshal.Cast<byte, float32>(staging.AsSpan(offset + 64, 48))
    rows.[0] <- normal.M11; rows.[1] <- normal.M12; rows.[2] <- normal.M13; rows.[3] <- normal.M14
    rows.[4] <- normal.M21; rows.[5] <- normal.M22; rows.[6] <- normal.M23; rows.[7] <- normal.M24
    rows.[8] <- normal.M31; rows.[9] <- normal.M32; rows.[10] <- normal.M33; rows.[11] <- normal.M34

let private fillInstance (batch: EnvironmentBatch) (model: Matrix4x4) (r: EnvironmentRender.EnvironmentRender) : unit =
    let world = model * r.OrientationMatrix
    let normal = normalMatrix world
    let last = r.SubmeshIndex + r.SubmeshCount - 1
    for i in r.SubmeshIndex .. last do
        let slot = slotFor batch r.Asset.Submeshes.[i]
        let index = batch.Cursor.[slot]
        batch.Cursor.[slot] <- index + 1
        writeInstance (index * InstanceBuffer.stride) world normal batch.Instances.Staging

// Walk the given prop/part maps twice: count instances per submesh identity
// (registering new ones), then fill their transforms. Takes the maps rather than a
// `Zone` so the batch type can be compiled before the zone that owns it. The device
// is needed only to size the instance buffer, allocated on the first gather. Parts
// arrive as a `voption` so an absent environment allocates nothing on the hot path.
let gather (device: Gpu.Device) (batch: EnvironmentBatch) (props: SlotMap<Prop>) (parts: SlotMap<Prop> voption) : unit =
    for slot in 0 .. batch.Registry.Count - 1 do
        batch.Counts.[slot] <- 0

    do props.IteriAlive(fun _ _ p -> countInstance batch p.Renderer)
    match parts with
    | ValueSome ps -> ps.IteriAlive(fun _ _ p -> countInstance batch p.Renderer)
    | ValueNone -> ()

    let total, groups = planOffsets batch.Counts batch.Starts batch.Registry.Count
    do Array.blit batch.Starts 0 batch.Cursor 0 batch.Registry.Count
    batch.InstanceCount <- total
    batch.DrawCalls <- groups

    do InstanceBuffer.ensure device &batch.Instances total
    do InstanceBuffer.reclaim device &batch.Instances

    do props.IteriAlive(fun _ _ p -> fillInstance batch (Entity.modelMatrix p.State.Base.Transform) p.Renderer)
    match parts with
    | ValueSome ps -> ps.IteriAlive(fun _ _ p -> fillInstance batch (Entity.modelMatrix p.State.Base.Transform) p.Renderer)
    | ValueNone -> ()

// ── upload ──

let upload (device: Gpu.Device) (batch: EnvironmentBatch) (cmd: Gpu.CommandBuffer) : unit =
    InstanceBuffer.upload device batch.Instances cmd (batch.InstanceCount * InstanceBuffer.stride)

// Free this batch's instance-buffer GPU memory. Called when the owning zone is
// released (or at shutdown), before the device is destroyed.
let release (device: Gpu.Device) (batch: EnvironmentBatch) : unit =
    InstanceBuffer.release device &batch.Instances

// ── draw ──

// Bind the static pipeline, push the shared viewProj once, then one instanced draw
// per non-empty group.
let render (batch: EnvironmentBatch) (renderPass: Gpu.RenderPass) (cmd: Gpu.CommandBuffer) (pipeline: Gpu.Pipeline) (viewProjection: Matrix4x4) (light: Light) (cameraPos: Vector3) : unit =
    if batch.InstanceCount = 0 then () else

    do writeMatrix batch.ViewProjBytes 0 viewProjection
    do SDL.PushGPUVertexUniformData(cmd, 0u, batch.ViewProjBytes, 64u)
    do SDL.BindGPUGraphicsPipeline(renderPass, pipeline)

    for slot in 0 .. batch.Registry.Count - 1 do
        let count = batch.Counts.[slot]
        if count > 0 then
            let sub = batch.Registry.[slot]
            let binding = InstanceBuffer.binding batch.Instances batch.Starts.[slot]
            do StaticMesh.drawInstanced sub.Mesh renderPass cmd binding (uint32 count) light cameraPos sub.Material
