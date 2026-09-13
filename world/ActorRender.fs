module ActorRender

// Skinned actor rendering. `ActorRender` is the per-instance state owned by an
// actor entity in the zone (matching odin-game's `Actor_Renderer`); the shared
// per-asset data lives on `ActorAsset.ActorAsset` (matching `Actor_Asset`), loaded
// once at startup in `Assets.loadActors` and reused by every instance.
//
// The bone palette must NOT be a pushed uniform: SDL_gpu's Vulkan backend binds
// each pushed uniform slot with a 4 KiB range (MAX_UBO_SECTION_SIZE), so any bone
// past index 63 reads as zero on the GPU — vertices weighted to those bones
// collapse to a point (degenerate spikes, missing geometry).

open System
open System.Numerics
open System.Runtime.InteropServices
open SDL3
open Actor
open Texture
open Mesh

// Per-actor animation + skinning state. A value type so instances live inline in
// the zone's actor slot map; `Asset` points at the shared loaded asset.
[<Struct>]
type ActorRender =
    { Asset : ActorAsset.ActorAsset
      mutable Animation : Actor.AnimationSlot
      Pose : Actor.PoseFrame
      BoneBuffer : Gpu.Buffer       // GraphicsStorageRead, BonesMax * 64
      BoneStorageBuffers : nativeint array // [| BoneBuffer |], reused so draw binds without allocating
      BoneTransfer : Gpu.TransferBuffer // upload transfer buffer, same size
      BoneBytes : byte array }    // BonesMax * 64, reused

// IQM is authored in a Z-up coordinate system (the character's height runs along
// +Z), while the engine's world/camera is Y-up. Convert with a -90° rotation
// about X (+Z -> +Y, +Y -> -Z), matching odin-game's `orientation_degrees.x =
// -90`. Applied in local space (before the entity transform) inside `draw`.
let private upAxisFix : Matrix4x4 = Matrix4x4.CreateRotationX(-MathF.PI / 2.0f)

// Create one instance's animation slot, pose frame, and bone palette for an
// already-loaded shared asset.
let create (device: Gpu.Device) (asset: ActorAsset.ActorAsset) : ActorRender =
    let model = asset.Model

    let animation =
        match Actor.findAnimIndex model "Idle" with
        | Some i -> Actor.createSlot model.Model i true
        | None ->
        if model.Model.Anims.Length > 0 then
            Actor.createSlot model.Model 0 true
        else
        { AnimIndex = 0
          Frame = 0.0f
          PlaybackFps = 0.0f
          Weight = 1.0f
          Loop = true
          Enabled = false }

    let boneBuffer =
        let mutable info = SDL.GPUBufferCreateInfo()
        info.Usage <- SDL.GPUBufferUsageFlags.GraphicsStorageRead
        info.Size <- uint32 (Gpu.BonesMax * 64)
        info.Props <- 0u
        SDL.CreateGPUBuffer(device, &info)

    { Asset = asset
      Animation = animation
      Pose = Actor.createPoseFrame model.Model
      BoneBuffer = boneBuffer
      BoneStorageBuffers = [| boneBuffer |]
      BoneTransfer = Gpu.createTransferBuffer device (uint32 (Gpu.BonesMax * 64))
      BoneBytes = Array.zeroCreate<byte> (Gpu.BonesMax * 64) }

// Advance the actor's animation clock in place through the renderer field. The
// per-frame hot path calls this so no ActorRender/Actor records are rebuilt.
let advanceInPlace (r: byref<ActorRender>) (dt: float32) : unit =
    do Actor.advanceInPlace r.Asset.Model &r.Animation dt

// Switch this instance to the named animation clip, if it is not already playing it.
// A clip the asset does not define is a content bug and raises.
let playClip (r: byref<ActorRender>) (name: string) : unit =
    let model = r.Asset.Model
    match Actor.findAnimIndex model name with
    | None -> failwithf "actor asset '%s' has no animation clip '%s'" r.Asset.Id name
    | Some index ->

    if r.Animation.AnimIndex <> index then
        r.Animation <- Actor.createSlot model.Model index true


// Sample the pose and upload the bone palette to this instance's GPU storage
// buffer. Runs in the copy-pass phase, before the render pass (like DebugDraw).
let upload (r: ActorRender) (cmd: Gpu.CommandBuffer) : unit =
    let mesh = r.Asset.Mesh
    let model = r.Asset.Model

    do Actor.samplePose model r.Animation.AnimIndex r.Animation.Frame r.Animation.Loop r.Pose

    let byteCount = model.Model.Joints.Length * 64
    for j in 0 .. model.Model.Joints.Length - 1 do
        do Gpu.writeMatrix r.BoneBytes (j * 64) r.Pose.Skinning.[j]

    let mapped = SDL.MapGPUTransferBuffer(mesh.Device, r.BoneTransfer, false)
    do Marshal.Copy(r.BoneBytes, 0, mapped, byteCount)
    do SDL.UnmapGPUTransferBuffer(mesh.Device, r.BoneTransfer)

    let copyPass = SDL.BeginGPUCopyPass(cmd)
    let mutable loc = SDL.GPUTransferBufferLocation()
    loc.TransferBuffer <- r.BoneTransfer
    loc.Offset <- 0u
    let mutable region = SDL.GPUBufferRegion()
    region.Buffer <- r.BoneBuffer
    region.Offset <- 0u
    region.Size <- uint32 byteCount
    do SDL.UploadToGPUBuffer(copyPass, &loc, &region, false)
    do SDL.EndGPUCopyPass(copyPass)

// Bind this instance's bone storage buffer (vertex storage slot 0) and draw.
// Runs inside the render pass, so the binding array is preallocated per instance
// (`BoneStorageBuffers`) rather than rebuilt each frame.
let draw (r: ActorRender) (renderPass: Gpu.RenderPass) (cmd: Gpu.CommandBuffer) (pipeline: Gpu.Pipeline) (viewProjection: Matrix4x4) (model: Matrix4x4) (light: Light) (cameraPos: Vector3) : unit =
    do SDL.BindGPUVertexStorageBuffers(renderPass, 0u, r.BoneStorageBuffers, 1u)
    do Mesh.draw r.Asset.Mesh renderPass cmd pipeline viewProjection (upAxisFix * model) light cameraPos

// Free this instance's GPU buffers (bone storage + upload transfer). Called when
// an actor is removed from its zone. The shared `ActorAsset` (mesh + model) is
// not freed here — it's owned by the asset manager and shared across instances.
let release (r: ActorRender) : unit =
    let device = r.Asset.Mesh.Device
    if r.BoneBuffer <> IntPtr.Zero then SDL.ReleaseGPUBuffer(device, r.BoneBuffer)
    if r.BoneTransfer <> IntPtr.Zero then SDL.ReleaseGPUTransferBuffer(device, r.BoneTransfer)
