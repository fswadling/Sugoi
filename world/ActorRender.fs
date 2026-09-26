module ActorRender

// Skinned actor rendering. `ActorRender` is the per-instance state owned by an
// actor entity in the zone (matching odin-game's `Actor_Renderer`); the shared
// per-asset data lives on `ActorAsset.ActorAsset` (matching `Actor_Asset`), loaded
// once at startup in `Assets.loadActors` and reused by every instance. The actor's
// animation slots live here too — the runtime clips it is blending, each with its own
// clock and weight (spec 036) — as do its morph slots (spec 037).
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
open SlotMap

// Per-actor animation + skinning state. A value type so instances live inline in
// the zone's actor slot map; `Asset` points at the shared loaded asset.
[<Struct>]
type ActorRender =
    { Asset : ActorAsset.ActorAsset
      AnimationSlots : SlotMap<Actor.AnimationSlot>
      MorphSlots : SlotMap<Actor.MorphSlot>
      Pose : Actor.PoseFrame
      BoneBuffer : Gpu.Buffer       // GraphicsStorageRead, BonesMax * 64
      BoneStorageBuffers : Gpu.Buffer array // [| BoneBuffer |], reused so draw binds without allocating
      MorphStorageBuffers : Gpu.Buffer array // [| the asset's morph buffer, or BoneBuffer when it has none |]
      BoneTransfer : Gpu.TransferBuffer // upload transfer buffer, same size
      BoneBytes : byte array        // BonesMax * 64, reused
      MorphWeightBytes : byte array } // the pushed morph weights + count, reused

// The animation slot capacity, re-exported for the editor's "at maximum" report
// (FR-017): the Inspector opens `Entity`, which also declares a module `Actor`, so the
// section reads the capacity from here rather than through that shared name.
let animationSlotCapacity = MaxAnimationSlots

// IQM is authored in a Z-up coordinate system (the character's height runs along
// +Z), while the engine's world/camera is Y-up. Convert with a -90° rotation
// about X (+Z -> +Y, +Y -> -Z), matching odin-game's `orientation_degrees.x =
// -90`. Applied in local space (before the entity transform) inside `draw`.
let private upAxisFix : Matrix4x4 = Matrix4x4.CreateRotationX(-MathF.PI / 2.0f)

// Create one instance's animation slots, pose frame, and bone palette for an
// already-loaded shared asset. The slot set starts holding a single clip at full
// weight — "Idle" if the asset defines it, else the first clip — so an actor spawned
// today plays exactly as it did before multi-slot blending (FR-011). An asset with no
// clips holds none, and the actor is posed at its bind pose (FR-008).
let create (device: Gpu.Device) (asset: ActorAsset.ActorAsset) : ActorRender =
    let model = asset.Model

    let slots = SlotMap<Actor.AnimationSlot>(uint Actor.MaxAnimationSlots)
    let initialClip =
        match Actor.tryFindAnimIndex model "Idle" with
        | Some i -> Some i
        | None ->
        if model.Model.Anims.Length > 0 then Some 0 else None
    match initialClip with
    | Some i -> slots.Add(Actor.createSlot model.Model i true) |> ignore
    | None -> ()

    // An actor holds no morphs until one is set, so a rebuilt renderer is the default state.
    let morphSlots = SlotMap<Actor.MorphSlot>(uint Gpu.MorphsMax)

    let boneBuffer =
        let mutable info = SDL.GPUBufferCreateInfo()
        info.Usage <- SDL.GPUBufferUsageFlags.GraphicsStorageRead
        info.Size <- uint32 (Gpu.BonesMax * 64)
        info.Props <- 0u
        SDL.CreateGPUBuffer(device, &info)

    { Asset = asset
      AnimationSlots = slots
      MorphSlots = morphSlots
      Pose = Actor.createPoseFrame model.Model
      BoneBuffer = boneBuffer
      BoneStorageBuffers = [| boneBuffer |]
      MorphStorageBuffers = [| (match asset.Morphs with Some morphs -> morphs.Buffer | None -> boneBuffer) |]
      BoneTransfer = Gpu.createTransferBuffer device (uint32 (Gpu.BonesMax * 64))
      BoneBytes = Array.zeroCreate<byte> (Gpu.BonesMax * 64)
      MorphWeightBytes = Array.zeroCreate<byte> 48 }

// Advance every held clip's own clock in place through the renderer field. The
// per-frame hot path calls this so no ActorRender/Actor records are rebuilt.
let advanceInPlace (r: byref<ActorRender>) (dt: float32) : unit =
    do Actor.advanceSlotsInPlace r.Asset.Model r.AnimationSlots.Slots dt

// Set the named clip's weight, clamped to 0..1, adding the clip to the actor's set when
// it is not already held (FR-005, FR-006, FR-007). A clip the asset does not define
// raises (FR-010). This is one path the Inspector calls as well as the gameplay caller
// (FR-020), so both drive the blend the same way.
let setClipWeight (r: byref<ActorRender>) (name: string) (weight: float32) : unit =
    let model = r.Asset.Model
    match Actor.tryFindAnimIndex model name with
    | None -> failwithf "actor asset '%s' has no animation clip '%s'" r.Asset.Id name
    | Some index ->
    do Actor.setSlotWeight model r.AnimationSlots index weight

// Remove the named clip from the actor's set. A clip the asset does not define raises,
// since only clips the asset defines are ever held.
let removeClip (r: byref<ActorRender>) (name: string) : unit =
    let model = r.Asset.Model
    match Actor.tryFindAnimIndex model name with
    | None -> failwithf "actor asset '%s' has no animation clip '%s'" r.Asset.Id name
    | Some index ->
    do Actor.removeSlot r.AnimationSlots index

// Set the named morph's weight, clamped to 0..1, adding the morph to the actor's set when it is
// not already held (FR-005, FR-007). A morph the asset does not carry raises (FR-007).
let setMorphWeight (r: byref<ActorRender>) (name: string) (weight: float32) : unit =
    match r.Asset.Morphs with
    | None -> failwithf "actor asset '%s' has no morphs" r.Asset.Id
    | Some morphs ->
    let index = Array.tryFindIndex (fun n -> n = name) morphs.Names
    match index with
    | Some i -> do Actor.setMorphWeight r.MorphSlots morphs.Count i weight
    | None -> failwithf "actor asset '%s' has no morph '%s'" r.Asset.Id name

// Remove the named morph from the actor's set.
let removeMorph (r: byref<ActorRender>) (name: string) : unit =
    match r.Asset.Morphs with
    | None -> failwithf "actor asset '%s' has no morphs" r.Asset.Id
    | Some morphs ->
    let index = Array.tryFindIndex (fun n -> n = name) morphs.Names
    match index with
    | Some i -> do Actor.removeMorph r.MorphSlots i
    | None -> failwithf "actor asset '%s' has no morph '%s'" r.Asset.Id name

// Switch this instance to the named animation clip as a hard switch: bring the clip to
// full weight and zero every other slot's weight, leaving the other clips held.
// `Systems.animate` drives the player through this, unchanged; the idle↔walk cross-fade
// that would replace it is deferred (FR-013).
let playClip (r: byref<ActorRender>) (name: string) : unit =
    let model = r.Asset.Model

    match Actor.tryFindAnimIndex model name with
    | None -> failwithf "actor asset '%s' has no animation clip '%s'" r.Asset.Id name
    | Some index ->

    let slots = r.AnimationSlots
    let span = slots.Slots
    for i in 0 .. span.Length - 1 do
        let slot = span.[i]
        if slot.Alive then span.[i] <- { slot with Value = { slot.Value with Weight = 0.0f } }

    do Actor.setSlotWeight model slots index 1.0f

// Sample the blended pose and upload the bone palette to this instance's GPU storage
// buffer. Runs in the copy-pass phase, before the render pass (like DebugDraw).
let upload (r: ActorRender) (cmd: Gpu.CommandBuffer) : unit =
    let mesh = r.Asset.Mesh
    let model = r.Asset.Model

    do Actor.blendPose model r.AnimationSlots.Slots r.Pose

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

    // Morphs: bind the asset's offset buffer at storage slot 1 (or the bone buffer when the
    // asset has none, so the binding is always satisfied and never read at a count of 0), and
    // push this actor's weights at uniform slot 1.
    let morphCount =
        match r.Asset.Morphs with
        | Some morphs -> morphs.Count
        | None -> 0
    do SDL.BindGPUVertexStorageBuffers(renderPass, 1u, r.MorphStorageBuffers, 1u)
    do Actor.packMorphWeights r.MorphSlots.Slots morphCount Gpu.MorphsMax r.MorphWeightBytes
    do SDL.PushGPUVertexUniformData(cmd, 1u, r.MorphWeightBytes, 48u)

    do Mesh.draw r.Asset.Mesh renderPass cmd pipeline viewProjection (upAxisFix * model) light cameraPos

// Free this instance's GPU buffers (bone storage + upload transfer). Called when
// an actor is removed from its zone. The shared `ActorAsset` (mesh + model) is
// not freed here — it's owned by the asset manager and shared across instances. The
// animation slots hold no GPU resource, so there is nothing to release for them.
let release (r: ActorRender) : unit =
    let device = r.Asset.Mesh.Device
    if r.BoneBuffer <> IntPtr.Zero then SDL.ReleaseGPUBuffer(device, r.BoneBuffer)
    if r.BoneTransfer <> IntPtr.Zero then SDL.ReleaseGPUTransferBuffer(device, r.BoneTransfer)
