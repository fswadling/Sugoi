module ActorTests

open System
open System.Numerics
open Xunit
open Iqm
open Actor

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
