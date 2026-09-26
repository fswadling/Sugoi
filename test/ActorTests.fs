module ActorTests

open System
open System.Numerics
open Xunit
open Iqm
open Actor
open SlotMap

// Build a minimal model record without going through the binary parser.
let private model (positions: float32 array) (normals: float32 array option) (joints: Joint array) (poses: Pose array) (anims: Anim array) : Iqm.Model =
    { Meshes = [||]
      Positions = positions
      Normals = normals
      Uvs = None
      Tangents = None
      BlendIndices = None
      BlendWeights = None
      Triangles = [||]
      Joints = joints
      Poses = poses
      Anims = anims
      FrameChannels = [||]
      NumFrameChannels = 0
      PoseChannelOffsets = Array.zeroCreate poses.Length }

let private rootJoint =
    { Name = "root"
      Parent = -1
      Translate = Vector3.Zero
      Rotate = Quaternion.Identity
      Scale = Vector3.One }

// A one-joint model with a single animated channel (translate.x): frame 0 = 1,
// frame 1 = 3, so sampling blends 1 -> 3 across the two frames.
let private translationModel () : Iqm.Model =
    let joint =
        { Name = "root"
          Parent = -1
          Translate = Vector3.Zero
          Rotate = Quaternion.Identity
          Scale = Vector3.One }
    let channelScale = Array.zeroCreate 10
    channelScale.[0] <- 1.0f
    let poseData =
        { Parent = -1
          ChannelMask = 1
          ChannelOffset = Array.zeroCreate 10
          ChannelScale = channelScale }
    let anim = { Name = "idle"; FirstFrame = 0; NumFrames = 2; FrameRate = 24.0f; Loop = true }
    { Meshes = [||]
      Positions = [| 0.0f; 0.0f; 0.0f |]
      Normals = None
      Uvs = None
      Tangents = None
      BlendIndices = None
      BlendWeights = None
      Triangles = [||]
      Joints = [| joint |]
      Poses = [| poseData |]
      Anims = [| anim |]
      FrameChannels = [| 1; 3 |]
      NumFrameChannels = 1
      PoseChannelOffsets = [| 0 |] }

[<Fact>]
let ``createModel computes the inverse bind for a root joint`` () =
    let m = model [| 0.0f; 0.0f; 0.0f |] None [| rootJoint |] [||] [||]
    let am = Actor.createModel m
    Assert.Equal(1, am.InverseBind.Length)
    Assert.Equal(Matrix4x4.Identity, am.InverseBind.[0])

[<Fact>]
let ``samplePose with no channels yields the bind-pose local`` () =
    let joint =
        { Name = "root"
          Parent = -1
          Translate = Vector3(1.0f, 2.0f, 3.0f)
          Rotate = Quaternion.Identity
          Scale = Vector3.One }
    let poseData =
        { Parent = -1
          ChannelMask = 0
          ChannelOffset = Array.zeroCreate 10
          ChannelScale = Array.zeroCreate 10 }
    let anim = { Name = "idle"; FirstFrame = 0; NumFrames = 1; FrameRate = 24.0f; Loop = true }
    let m = model [| 0.0f; 0.0f; 0.0f |] None [| joint |] [| poseData |] [| anim |]
    let am = Actor.createModel m
    let pose = Actor.createPoseFrame m
    Actor.samplePose am 0 0.0f true pose
    Assert.Equal(1.0f, pose.Local.[0].M41)
    Assert.Equal(2.0f, pose.Local.[0].M42)
    Assert.Equal(3.0f, pose.Local.[0].M43)

[<Fact>]
let ``samplePose writes the skinning matrix as inverseBind * world`` () =
    // Root with a pure +2 X translation: world = T(+2), inverseBind = T(-2),
    // so skinning = T(-2) * T(+2) = identity.
    let joint =
        { Name = "root"
          Parent = -1
          Translate = Vector3(2.0f, 0.0f, 0.0f)
          Rotate = Quaternion.Identity
          Scale = Vector3.One }
    let poseData =
        { Parent = -1
          ChannelMask = 0
          ChannelOffset = Array.zeroCreate 10
          ChannelScale = Array.zeroCreate 10 }
    let anim = { Name = "idle"; FirstFrame = 0; NumFrames = 1; FrameRate = 24.0f; Loop = true }
    let m = model [| 0.0f; 0.0f; 0.0f |] None [| joint |] [| poseData |] [| anim |]
    let am = Actor.createModel m
    let pose = Actor.createPoseFrame m
    Actor.samplePose am 0 0.0f true pose
    Assert.Equal(1.0f, pose.Skinning.[0].M11)
    Assert.Equal(0.0f, pose.Skinning.[0].M41)

[<Fact>]
let ``advance loops a looping animation`` () =
    let anim = { Name = "idle"; FirstFrame = 0; NumFrames = 10; FrameRate = 10.0f; Loop = true }
    let m = model [| 0.0f; 0.0f; 0.0f |] None [||] [||] [| anim |]
    let am = Actor.createModel m
    let slot = { AnimIndex = 0; Frame = 9.5f; PlaybackFps = 10.0f; Weight = 1.0f; Loop = true; Enabled = true }
    let advanced = Actor.advance am slot 0.1f
    Assert.Equal(0.5f, advanced.Frame)

// ── Refactor guards: sampled output and allocation ──

[<Fact>]
let ``samplePose blends an active channel across frames`` () =
    let m = translationModel ()
    let am = Actor.createModel m
    let pose = Actor.createPoseFrame m

    do Actor.samplePose am 0 0.0f true pose
    Assert.Equal(1.0f, pose.Local.[0].M41)
    do Actor.samplePose am 0 0.5f true pose
    Assert.Equal(2.0f, pose.Local.[0].M41)
    do Actor.samplePose am 0 1.0f true pose
    Assert.Equal(3.0f, pose.Local.[0].M41)

    // Root with an identity bind pose: skinning equals the world matrix.
    Assert.Equal(pose.World.[0].M41, pose.Skinning.[0].M41)

[<Fact>]
let ``samplePose holds the last frame for a non-looping clip`` () =
    let m = translationModel ()
    let am = Actor.createModel m
    let pose = Actor.createPoseFrame m
    do Actor.samplePose am 0 1.0f false pose
    Assert.Equal(3.0f, pose.Local.[0].M41)

[<Fact>]
let ``samplePose composes the local transform as scale * rotation * translation`` () =
    let t = Vector3(1.0f, 2.0f, 3.0f)
    let r = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f)
    let s = Vector3(2.0f, 0.5f, 4.0f)
    let joint = { Name = "root"; Parent = -1; Translate = t; Rotate = r; Scale = s }
    let poseData =
        { Parent = -1
          ChannelMask = 0
          ChannelOffset = Array.zeroCreate 10
          ChannelScale = Array.zeroCreate 10 }
    let anim = { Name = "idle"; FirstFrame = 0; NumFrames = 1; FrameRate = 24.0f; Loop = true }
    let m = model [| 0.0f; 0.0f; 0.0f |] None [| joint |] [| poseData |] [| anim |]
    let am = Actor.createModel m
    let pose = Actor.createPoseFrame m
    do Actor.samplePose am 0 0.0f true pose

    // Row-vector S * R * T; the refactor writes T's row directly and must match.
    let expected =
        Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(t)
    Assert.Equal(expected, pose.Local.[0])

[<Fact>]
let ``samplePose is a no-op with no frames and with no joints`` () =
    let poseData =
        { Parent = -1
          ChannelMask = 0
          ChannelOffset = Array.zeroCreate 10
          ChannelScale = Array.zeroCreate 10 }
    let anim = { Name = "idle"; FirstFrame = 0; NumFrames = 0; FrameRate = 24.0f; Loop = true }

    // Zero frames: the sampler returns before writing anything.
    let noFrames = model [| 0.0f; 0.0f; 0.0f |] None [| rootJoint |] [| poseData |] [| anim |]
    let am = Actor.createModel noFrames
    let pose = Actor.createPoseFrame noFrames
    let before = pose.Local.[0]
    do Actor.samplePose am 0 0.0f true pose
    Assert.Equal(before, pose.Local.[0])

    // Zero joints: nothing to iterate.
    let noJoints = model [| 0.0f; 0.0f; 0.0f |] None [||] [||] [| anim |]
    let am2 = Actor.createModel noJoints
    let pose2 = Actor.createPoseFrame noJoints
    do Actor.samplePose am2 0 0.0f true pose2
    Assert.Equal(0, pose2.Local.Length)

[<Fact>]
let ``samplePose allocates nothing per frame`` () =
    let m = translationModel ()
    let am = Actor.createModel m
    let pose = Actor.createPoseFrame m

    // Warm up the JIT so one-time costs fall outside the measurement.
    for i in 0 .. 99 do Actor.samplePose am 0 (float32 i * 0.01f) true pose
    let before = GC.GetAllocatedBytesForCurrentThread()
    for i in 0 .. 999 do Actor.samplePose am 0 (float32 i * 0.01f) true pose
    let after = GC.GetAllocatedBytesForCurrentThread()
    Assert.Equal(0L, after - before)

// ── Multi-slot blending (spec 036) ──

// Two translation clips on one joint, sharing a frame channel: clip "a" blends 1 -> 3
// across frames 0..1, clip "b" blends 5 -> 7 across frames 2..3.
let private twoClipModel () : Iqm.Model =
    let joint =
        { Name = "root"
          Parent = -1
          Translate = Vector3.Zero
          Rotate = Quaternion.Identity
          Scale = Vector3.One }
    let channelScale = Array.zeroCreate 10
    channelScale.[0] <- 1.0f
    let poseData =
        { Parent = -1
          ChannelMask = 1
          ChannelOffset = Array.zeroCreate 10
          ChannelScale = channelScale }
    let a = { Name = "a"; FirstFrame = 0; NumFrames = 2; FrameRate = 24.0f; Loop = true }
    let b = { Name = "b"; FirstFrame = 2; NumFrames = 2; FrameRate = 24.0f; Loop = true }
    { Meshes = [||]
      Positions = [| 0.0f; 0.0f; 0.0f |]
      Normals = None
      Uvs = None
      Tangents = None
      BlendIndices = None
      BlendWeights = None
      Triangles = [||]
      Joints = [| joint |]
      Poses = [| poseData |]
      Anims = [| a; b |]
      FrameChannels = [| 1; 3; 5; 7 |]
      NumFrameChannels = 1
      PoseChannelOffsets = [| 0 |] }

// The same joint, with `count` clips all referencing frame 0 — for the capacity test.
let private manyClipModel (count: int) : Iqm.Model =
    let baseModel = twoClipModel ()
    { baseModel with
        Anims = Array.init count (fun i -> { Name = sprintf "clip%d" i; FirstFrame = 0; NumFrames = 2; FrameRate = 24.0f; Loop = true }) }

let private slotOf (animIndex: int) (frame: float32) (weight: float32) (fps: float32) : AnimationSlot =
    { AnimIndex = animIndex
      Frame = frame
      PlaybackFps = fps
      Weight = weight
      Loop = true
      Enabled = true }

let private slotMap (slots: AnimationSlot list) : SlotMap<AnimationSlot> =
    let map = SlotMap<AnimationSlot>(8u)
    for s in slots do map.Add s |> ignore
    map

[<Fact>]
let ``blendPose of two clips at equal weight is the per-joint midpoint`` () =
    let m = twoClipModel ()
    let am = Actor.createModel m
    let pose = Actor.createPoseFrame m
    // clip a at frame 0 translates x = 1; clip b at frame 0 translates x = 5; halfway = 3.
    let slots = slotMap [ slotOf 0 0.0f 1.0f 24.0f; slotOf 1 0.0f 1.0f 24.0f ]
    do Actor.blendPose am slots.Slots pose
    Assert.Equal(3.0f, pose.Local.[0].M41)

[<Fact>]
let ``blendPose of one clip at weight 1 equals samplePose`` () =
    let m = twoClipModel ()
    let am = Actor.createModel m
    let blended = Actor.createPoseFrame m
    let sampled = Actor.createPoseFrame m
    let slots = slotMap [ slotOf 1 0.5f 1.0f 24.0f ]
    do Actor.blendPose am slots.Slots blended
    do Actor.samplePose am 1 0.5f true sampled
    Assert.Equal(sampled.Local.[0], blended.Local.[0])
    Assert.Equal(sampled.World.[0], blended.World.[0])
    Assert.Equal(sampled.Skinning.[0], blended.Skinning.[0])

[<Fact>]
let ``blendPose weights are relative`` () =
    let m = twoClipModel ()
    let am = Actor.createModel m
    let half = Actor.createPoseFrame m
    let full = Actor.createPoseFrame m
    let halfSlots = slotMap [ slotOf 0 0.0f 0.5f 24.0f; slotOf 1 0.0f 0.5f 24.0f ]
    let fullSlots = slotMap [ slotOf 0 0.0f 1.0f 24.0f; slotOf 1 0.0f 1.0f 24.0f ]
    do Actor.blendPose am halfSlots.Slots half
    do Actor.blendPose am fullSlots.Slots full
    Assert.Equal(half.Local.[0].M41, full.Local.[0].M41)

[<Fact>]
let ``blendPose with no contributing clip poses the bind pose`` () =
    let joint =
        { Name = "root"
          Parent = -1
          Translate = Vector3(1.0f, 2.0f, 3.0f)
          Rotate = Quaternion.Identity
          Scale = Vector3.One }
    let poseData =
        { Parent = -1
          ChannelMask = 0
          ChannelOffset = Array.zeroCreate 10
          ChannelScale = Array.zeroCreate 10 }
    let anim = { Name = "idle"; FirstFrame = 0; NumFrames = 1; FrameRate = 24.0f; Loop = true }
    let m = model [| 0.0f; 0.0f; 0.0f |] None [| joint |] [| poseData |] [| anim |]
    let am = Actor.createModel m
    let pose = Actor.createPoseFrame m

    // An empty set.
    let empty = SlotMap<AnimationSlot>(8u)
    do Actor.blendPose am empty.Slots pose
    Assert.Equal(1.0f, pose.Local.[0].M41)
    Assert.Equal(2.0f, pose.Local.[0].M42)
    Assert.Equal(3.0f, pose.Local.[0].M43)

    // A held clip at weight 0 contributes nothing, so the bind pose stands.
    let zeroed = slotMap [ slotOf 0 0.0f 0.0f 24.0f ]
    do Actor.blendPose am zeroed.Slots pose
    Assert.Equal(1.0f, pose.Local.[0].M41)

[<Fact>]
let ``blendPose allocates nothing per frame`` () =
    let m = twoClipModel ()
    let am = Actor.createModel m
    let pose = Actor.createPoseFrame m
    let slots = slotMap [ slotOf 0 0.0f 1.0f 24.0f; slotOf 1 0.0f 1.0f 24.0f ]
    let span = slots.Slots

    for i in 0 .. 99 do Actor.blendPose am span pose
    let before = GC.GetAllocatedBytesForCurrentThread()
    for i in 0 .. 999 do Actor.blendPose am span pose
    let after = GC.GetAllocatedBytesForCurrentThread()
    Assert.Equal(0L, after - before)

// ── Slot set algebra (spec 036, US2) ──

[<Fact>]
let ``setSlotWeight adds a clip and clamps the weight`` () =
    let m = twoClipModel ()
    let am = Actor.createModel m
    let slots = SlotMap<AnimationSlot>(8u)

    do Actor.setSlotWeight am slots 1 2.0f
    Assert.Equal(1u, slots.Count)
    Assert.Equal(1, slots.Slots.[0].Value.AnimIndex)
    Assert.True(slots.Slots.[0].Value.Enabled)
    Assert.Equal(1.0f, slots.Slots.[0].Value.Weight)

    // Setting the same clip again adjusts it rather than adding a second slot.
    do Actor.setSlotWeight am slots 1 -3.0f
    Assert.Equal(1u, slots.Count)
    Assert.Equal(0.0f, slots.Slots.[0].Value.Weight)

[<Fact>]
let ``a zero weight keeps the slot but removes its contribution`` () =
    let m = twoClipModel ()
    let am = Actor.createModel m
    let pose = Actor.createPoseFrame m
    let slots = slotMap [ slotOf 0 0.0f 1.0f 24.0f; slotOf 1 0.0f 0.0f 24.0f ]
    do Actor.blendPose am slots.Slots pose
    Assert.Equal(1.0f, pose.Local.[0].M41)
    Assert.Equal(2u, slots.Count)

[<Fact>]
let ``removeSlot frees the clip's slot and is a no-op when absent`` () =
    let m = twoClipModel ()
    let am = Actor.createModel m
    let slots = SlotMap<AnimationSlot>(8u)
    do Actor.setSlotWeight am slots 0 1.0f
    do Actor.setSlotWeight am slots 1 1.0f
    Assert.Equal(2u, slots.Count)
    do Actor.removeSlot slots 0
    Assert.Equal(1u, slots.Count)
    do Actor.removeSlot slots 0
    Assert.Equal(1u, slots.Count)

[<Fact>]
let ``setSlotWeight raises for a clip index outside the asset`` () =
    let m = twoClipModel ()
    let am = Actor.createModel m
    let slots = SlotMap<AnimationSlot>(8u)
    Assert.Throws<System.Exception>(fun () -> Actor.setSlotWeight am slots 9 1.0f) |> ignore

[<Fact>]
let ``setSlotWeight raises when the set is full`` () =
    let m = manyClipModel 9
    let am = Actor.createModel m
    let slots = SlotMap<AnimationSlot>(8u)
    for i in 0 .. 7 do Actor.setSlotWeight am slots i 1.0f
    let mutable raised = false
    try
        Actor.setSlotWeight am slots 8 1.0f
    with :? System.InvalidOperationException ->
        raised <- true
    Assert.True(raised)

// ── Per-slot clocks (spec 036, US4) ──

[<Fact>]
let ``advanceSlotsInPlace advances each enabled slot by its own rate`` () =
    let m = twoClipModel ()
    let am = Actor.createModel m
    let disabled = { slotOf 0 0.0f 1.0f 10.0f with Enabled = false }
    let slots = slotMap [ slotOf 0 0.0f 1.0f 10.0f; slotOf 1 0.0f 1.0f 5.0f; disabled ]
    do Actor.advanceSlotsInPlace am slots.Slots 0.1f
    Assert.Equal(1.0f, slots.Slots.[0].Value.Frame)
    Assert.Equal(0.5f, slots.Slots.[1].Value.Frame)
    Assert.Equal(0.0f, slots.Slots.[2].Value.Frame)

[<Fact>]
let ``advanceSlotsInPlace advances a zero-weight slot`` () =
    let m = twoClipModel ()
    let am = Actor.createModel m
    let slots = slotMap [ slotOf 0 0.0f 0.0f 10.0f ]
    do Actor.advanceSlotsInPlace am slots.Slots 0.1f
    Assert.Equal(1.0f, slots.Slots.[0].Value.Frame)

[<Fact>]
let ``advanceSlotsInPlace wraps a looping slot and holds a non-looping one`` () =
    let m = twoClipModel ()
    let am = Actor.createModel m
    let holding = { slotOf 1 1.5f 1.0f 10.0f with Loop = false }
    let slots = slotMap [ slotOf 0 1.5f 1.0f 10.0f; holding ]
    do Actor.advanceSlotsInPlace am slots.Slots 0.1f
    Assert.Equal(0.5f, slots.Slots.[0].Value.Frame)
    Assert.Equal(1.0f, slots.Slots.[1].Value.Frame)

// ── Scale and rotation midpoints (spec 036, SC-001 / FR-003) ──

// One joint whose clip overrides scale.x only: clip "a" scales x = 1, clip "b" x = 3.
let private scaleModel () : Iqm.Model =
    let joint =
        { Name = "root"
          Parent = -1
          Translate = Vector3.Zero
          Rotate = Quaternion.Identity
          Scale = Vector3.One }
    let channelScale = Array.zeroCreate 10
    channelScale.[7] <- 1.0f / 16384.0f            // sx
    let poseData =
        { Parent = -1
          ChannelMask = 1 <<< 7
          ChannelOffset = Array.zeroCreate 10
          ChannelScale = channelScale }
    let a = { Name = "a"; FirstFrame = 0; NumFrames = 1; FrameRate = 24.0f; Loop = true }
    let b = { Name = "b"; FirstFrame = 1; NumFrames = 1; FrameRate = 24.0f; Loop = true }
    { Meshes = [||]
      Positions = [| 0.0f; 0.0f; 0.0f |]
      Normals = None
      Uvs = None
      Tangents = None
      BlendIndices = None
      BlendWeights = None
      Triangles = [||]
      Joints = [| joint |]
      Poses = [| poseData |]
      Anims = [| a; b |]
      FrameChannels = [| 16384; 49152 |]
      NumFrameChannels = 1
      PoseChannelOffsets = [| 0 |] }

// One joint whose clip overrides the Y rotation only: clip "a" is the identity rotation,
// clip "b" is a pi rotation about Y — their equal-weight blend is a pi/2 rotation about Y.
let private rotationModel () : Iqm.Model =
    let joint =
        { Name = "root"
          Parent = -1
          Translate = Vector3.Zero
          Rotate = Quaternion.Identity
          Scale = Vector3.One }
    let channelScale = Array.zeroCreate 10
    channelScale.[4] <- 1.0f / 16384.0f            // ry
    channelScale.[6] <- 1.0f / 16384.0f            // rw
    let poseData =
        { Parent = -1
          ChannelMask = (1 <<< 4) ||| (1 <<< 6)
          ChannelOffset = Array.zeroCreate 10
          ChannelScale = channelScale }
    let a = { Name = "a"; FirstFrame = 0; NumFrames = 1; FrameRate = 24.0f; Loop = true }
    let b = { Name = "b"; FirstFrame = 1; NumFrames = 1; FrameRate = 24.0f; Loop = true }
    { Meshes = [||]
      Positions = [| 0.0f; 0.0f; 0.0f |]
      Normals = None
      Uvs = None
      Tangents = None
      BlendIndices = None
      BlendWeights = None
      Triangles = [||]
      Joints = [| joint |]
      Poses = [| poseData |]
      Anims = [| a; b |]
      FrameChannels = [| 0; 16384; 16384; 0 |]
      NumFrameChannels = 2
      PoseChannelOffsets = [| 0 |] }

[<Fact>]
let ``blendPose of two clips at equal weight is the scale midpoint`` () =
    let m = scaleModel ()
    let am = Actor.createModel m
    let pose = Actor.createPoseFrame m
    // clip a scales x = 1, clip b x = 3; translation 0 and rotation identity throughout.
    let slots = slotMap [ slotOf 0 0.0f 1.0f 24.0f; slotOf 1 0.0f 1.0f 24.0f ]
    do Actor.blendPose am slots.Slots pose
    Assert.Equal(Matrix4x4.CreateScale(Vector3(2.0f, 1.0f, 1.0f)), pose.Local.[0])

[<Fact>]
let ``blendPose of two clips at equal weight is the rotation midpoint`` () =
    let m = rotationModel ()
    let am = Actor.createModel m
    let pose = Actor.createPoseFrame m
    // clip a is the identity rotation, clip b a pi rotation about Y; the midpoint is pi/2 about Y.
    let slots = slotMap [ slotOf 0 0.0f 1.0f 24.0f; slotOf 1 0.0f 1.0f 24.0f ]
    do Actor.blendPose am slots.Slots pose
    let expectedRotation = Quaternion.Slerp(Quaternion.Identity, Quaternion(0.0f, 1.0f, 0.0f, 0.0f), 0.5f)
    let expected = Matrix4x4.CreateScale(Vector3.One) * Matrix4x4.CreateFromQuaternion(expectedRotation)
    Assert.Equal(expected, pose.Local.[0])
