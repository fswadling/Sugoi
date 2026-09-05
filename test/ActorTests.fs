module ActorTests

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
