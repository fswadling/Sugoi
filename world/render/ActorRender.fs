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
      MorphWeightBytes : byte array // the pushed morph weights + count, reused
      Sway : Sway.SwayState         // the actor's sway chains, seeded and stepped per frame (spec 091)
      mutable PosedModel : Matrix4x4 // `upAxisFix * model`, computed once in `simulate`
      mutable NormalMatrix : Matrix4x4 // the posed model's normal matrix, likewise
      mutable LocomotionClip : string // the locomotion clip last switched to, so a repeat is a no-op
      mutable LastMissing : string }        // the last clip name reported absent, so the report is once per event (spec 080, FR-013)

// The animation slot capacity, re-exported for the editor's "at maximum" report
// (FR-017): the Inspector opens `Entity`, which also declares a module `Actor`, so the
// section reads the capacity from here rather than through that shared name.
let animationSlotCapacity = MaxAnimationSlots

// IQM is authored in a Z-up coordinate system (the character's height runs along
// +Z), while the engine's world/camera is Y-up. Convert with a -90° rotation
// about X (+Z -> +Y, +Y -> -Z), matching odin-game's `orientation_degrees.x =
// -90`. Applied in local space (before the entity transform) inside `draw`.
//
// Rot and matrix are the same rotation expressed twice, because the sway needs the actor's world
// *orientation* as a quaternion and must not solve for it out of a scaled matrix (spec 091).
let private upAxisFixRot : Quaternion = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2.0f)
let private upAxisFix : Matrix4x4 = Matrix4x4.CreateFromQuaternion upAxisFixRot

// The idealized face light (spec 040, FR-007): the actor's horizontal facing — the entity model
// matrix applied to local +Z (the project's forward convention, matching `Systems.forwardOf`),
// flattened in Y and normalized — with the reference's fixed elevation added afterwards. The
// shader normalizes the result again.
let faceLightDir (model: Matrix4x4) : Vector3 =
    let f = Vector3.TransformNormal(Vector3.UnitZ, model)
    let h = Vector3(f.X, 0.0f, f.Z)
    let h = if h.LengthSquared() > 0.0f then Vector3.Normalize h else Vector3.UnitZ
    Vector3(h.X, 0.25f, h.Z)

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
        | ValueSome i -> Some i
        | ValueNone ->
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
      MorphWeightBytes = Array.zeroCreate<byte> 48
      Sway = Sway.createState asset.SwayChains
      PosedModel = upAxisFix
      NormalMatrix = Gpu.normalMatrix upAxisFix
      LocomotionClip = ""
      LastMissing = "" }

// The actor's per-frame step, called once per frame in *both* modes: advance the held clips' clocks
// while playing, blend the pose, and — while playing — step and apply the sway (spec 091).
//
// The blend always runs, because an actor must be posed while it is being placed in the editor; the
// clock and the sway are gated on `playing`, which is what makes the sway a play-mode effect (FR-008).
// It runs from the update chain (research D1), where the frame's time and the actor's transform are in
// hand — the copy pass that uploads the palette has neither, and `dt` is what the rope needs.
//
// `model` is the entity's model matrix and `rotation` its rotation, both passed rather than derived
// because `world/Entity.fs` is compiled below this file. The sway is given the same world transform
// `draw` builds, so the rope swings where the character is drawn.
let simulate (r: byref<ActorRender>) (dt: float32) (playing: bool) (model: Matrix4x4) (rotation: Quaternion) : unit =
    if playing then
        do Actor.advanceSlotsInPlace r.Asset.Model r.AnimationSlots.Slots dt

    do Actor.blendPose r.Asset.Model r.AnimationSlots.Slots r.Pose

    // The posed model and its normal matrix are draw inputs, so compute them once here and let
    // `draw` reuse them (F-22).
    let posedModel = upAxisFix * model
    r.PosedModel <- posedModel
    r.NormalMatrix <- Gpu.normalMatrix posedModel

    if playing then
        do Sway.apply r.Asset.Model r.Pose r.Sway dt posedModel (Quaternion.Multiply(rotation, upAxisFixRot))
    else
        do Sway.clear r.Sway

// Set the named clip's weight, clamped to 0..1, adding the clip to the actor's set when
// it is not already held (FR-005, FR-006, FR-007). A clip the asset does not define
// raises (FR-010). This is one path the Inspector calls as well as the gameplay caller
// (FR-020), so both drive the blend the same way.
let setClipWeight (r: byref<ActorRender>) (name: string) (weight: float32) : unit =
    let model = r.Asset.Model
    match Actor.tryFindAnimIndex model name with
    | ValueNone -> failwithf "actor asset '%s' has no animation clip '%s'" r.Asset.Id name
    | ValueSome index ->
    do Actor.setSlotWeight model r.AnimationSlots index weight

// Remove the named clip from the actor's set. A clip the asset does not define raises,
// since only clips the asset defines are ever held.
let removeClip (r: byref<ActorRender>) (name: string) : unit =
    let model = r.Asset.Model
    match Actor.tryFindAnimIndex model name with
    | ValueNone -> failwithf "actor asset '%s' has no animation clip '%s'" r.Asset.Id name
    | ValueSome index ->
    do Actor.removeSlot r.AnimationSlots index

// The index of the morph named `name` in an asset's morph list, or `ValueNone` — the morph counterpart of
// `Actor.tryFindAnimIndex`, shared by the three morph lookups below. A loop rather than `Array.tryFindIndex`,
// which captures `name` in a closure and allocates on every call — `setMorphWeight` runs per frame from a morph
// blend (`specs/074`).
let private tryMorphIndex (morphs: ActorAsset.ActorMorphs) (name: string) : int voption =
    let names = morphs.Names
    let mutable found = ValueNone
    let mutable i = 0
    while found.IsNone && i < names.Length do
        if names.[i] = name then found <- ValueSome i
        i <- i + 1
    found

// Set the named morph's weight, clamped to 0..1, adding the morph to the actor's set when it is
// not already held (FR-005, FR-007). A morph the asset does not carry raises (FR-007).
let setMorphWeight (r: byref<ActorRender>) (name: string) (weight: float32) : unit =
    match r.Asset.Morphs with
    | None -> failwithf "actor asset '%s' has no morphs" r.Asset.Id
    | Some morphs ->
    match tryMorphIndex morphs name with
    | ValueNone -> failwithf "actor asset '%s' has no morph '%s'" r.Asset.Id name
    | ValueSome index ->
    do Actor.setMorphWeight r.MorphSlots morphs.Count index weight

// Remove the named morph from the actor's set.
let removeMorph (r: byref<ActorRender>) (name: string) : unit =
    match r.Asset.Morphs with
    | None -> failwithf "actor asset '%s' has no morphs" r.Asset.Id
    | Some morphs ->
    match tryMorphIndex morphs name with
    | ValueNone -> failwithf "actor asset '%s' has no morph '%s'" r.Asset.Id name
    | ValueSome index ->
    do Actor.removeMorph r.MorphSlots index

// The named clip's current weight, or `None` when the asset does not define it. A clip the asset defines but
// the instance does not yet hold reads 0 — so a blend captures where it is and can begin a clip it is not
// already playing. This is the guard a cue's blend uses to no-op on a name the asset lacks, rather than
// reaching the setter's raise.
let tryClipWeight (r: byref<ActorRender>) (name: string) : float32 option =
    let model = r.Asset.Model
    match Actor.tryFindAnimIndex model name with
    | ValueNone -> None
    | ValueSome index ->
    let slots = r.AnimationSlots.Slots
    let mutable weight = 0.0f
    for i in 0 .. slots.Length - 1 do
        let slot = slots.[i]
        if slot.Alive && slot.Value.AnimIndex = index then weight <- slot.Value.Weight
    Some weight

// The named morph's current weight, or `None` when the asset carries no such morph (or none at all); a morph
// the asset carries but the instance does not yet hold reads 0. The morph counterpart of `tryClipWeight`.
let tryMorphWeight (r: byref<ActorRender>) (name: string) : float32 option =
    match r.Asset.Morphs with
    | None -> None
    | Some morphs ->
    match tryMorphIndex morphs name with
    | ValueNone -> None
    | ValueSome index ->
    let slots = r.MorphSlots.Slots
    let mutable weight = 0.0f
    for i in 0 .. slots.Length - 1 do
        let slot = slots.[i]
        if slot.Alive && slot.Value.MorphIndex = index then weight <- slot.Value.Weight
    Some weight

// Switch this instance to the named animation clip as a hard switch: bring the clip to
// full weight and zero every other slot's weight, leaving the other clips held.
// `Systems.animate` drives the player through this, unchanged; the idle↔walk cross-fade
// that would replace it is deferred (FR-013).
//
// A clip the asset does not define is not fatal (spec 080, US1): the current pose is held
// and the substitution is reported once per request, not once per frame (FR-013), because
// `Systems.animate` calls this every play frame with `"Idle"`/`"Jog"`.
let playClip (r: byref<ActorRender>) (name: string) : unit =
    // The locomotion caller repeats the same name every play frame; an unchanged clip is already at
    // full weight, so skip the resolve and the slot rewrite entirely (F-29).
    if r.LocomotionClip = name then () else

    let model = r.Asset.Model

    match Actor.tryFindAnimIndex model name with
    | ValueNone ->
        if r.LastMissing <> name then
            r.LastMissing <- name
            do Diagnostics.reportf "animation: actor asset '%s' has no clip '%s'; holding the pose" r.Asset.Id name
    | ValueSome index ->

    r.LastMissing <- ""
    r.LocomotionClip <- name
    let slots = r.AnimationSlots
    let span = slots.Slots
    for i in 0 .. span.Length - 1 do
        let slot = &span.[i]
        if slot.Alive then slot.Value.Weight <- 0.0f

    do Actor.setSlotWeight model slots index 1.0f

// Upload the pose's bone palette to this instance's GPU storage buffer. Runs in the copy-pass phase,
// before the render pass (like DebugDraw). The pose itself is built a phase earlier by `simulate`;
// this is only the transfer, which is what its name says.
let upload (r: ActorRender) (cmd: Gpu.CommandBuffer) : unit =
    let mesh = r.Asset.Mesh
    let jointCount = r.Asset.Model.Model.Joints.Length
    let byteCount = jointCount * 64
    let source = MemoryMarshal.AsBytes(r.Pose.Skinning.AsSpan(0, jointCount))

    // A buffer rewritten every frame is mapped cycled, matching the frames in flight (F-02); the
    // palette bytes copy straight from the pose (F-22).
    let mapped = SDL.MapGPUTransferBuffer(mesh.Device, r.BoneTransfer, true)
    source.CopyTo(Span<byte>(mapped.ToPointer(), byteCount))
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

// The outline shell: a fixed world-space width, so it shrinks with distance exactly like the actor
// itself (perspective) — a physical ink line, not a constant-pixel overlay. There is no on-screen cap
// and no distance fade: the line is thickest when the actor is closest, as any world-space feature is.
let outlineWidth = 0.001f

// Bind this instance's bone storage buffer (vertex storage slot 0) and draw. Runs inside the render
// pass, so the binding array is preallocated per instance (`BoneStorageBuffers`) rather than rebuilt
// each frame. Two passes share the posed mesh: the outline hull first, then the cel surface over it.
let draw (r: ActorRender) (renderPass: Gpu.RenderPass) (cmd: Gpu.CommandBuffer) (pipeline: Gpu.Pipeline) (outlinePipeline: Gpu.Pipeline) (viewProjection: Matrix4x4) (model: Matrix4x4) (light: Light) : unit =
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

    let posedModel = r.PosedModel
    do Mesh.drawOutline r.Asset.Mesh renderPass cmd outlinePipeline viewProjection posedModel r.NormalMatrix outlineWidth
    do Mesh.draw r.Asset.Mesh renderPass cmd pipeline viewProjection posedModel r.NormalMatrix light (faceLightDir model)

// Free this instance's GPU buffers (bone storage + upload transfer). Called when
// an actor is removed from its zone. The shared `ActorAsset` (mesh + model) is
// not freed here — it's owned by the asset manager and shared across instances. The
// animation slots hold no GPU resource, so there is nothing to release for them.
let release (r: ActorRender) : unit =
    let device = r.Asset.Mesh.Device
    if r.BoneBuffer <> IntPtr.Zero then SDL.ReleaseGPUBuffer(device, r.BoneBuffer)
    if r.BoneTransfer <> IntPtr.Zero then SDL.ReleaseGPUTransferBuffer(device, r.BoneTransfer)
