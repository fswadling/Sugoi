# Contract: Animated Character on Screen

Public surfaces added by this feature. Signatures are illustrative F#, matching
the implementation.

## IQM — `core/Iqm.fs`

```fsharp
type VertexFormat = Float | Ubyte
type VertexKind   = Position | Texcoord | Normal | Tangent
                  | BlendIndices | BlendWeights

type VertexArray =
    { Kind : VertexKind; Format : VertexFormat; Size : int; Offset : int }

type Mesh =
    { Name : string; Material : int
      FirstVertex : int; NumVertices : int
      FirstTriangle : int; NumTriangles : int }

type Joint =
    { Name : string; Parent : int
      Translate : Vector3; Rotate : Quaternion; Scale : Vector3 }

type Pose =
    { Parent : int; ChannelMask : int
      ChannelOffset : float32 array; ChannelScale : float32 array }

type Anim =
    { Name : string; FirstFrame : int; NumFrames : int
      FrameRate : float32; Loop : bool }

type Model =
    { Meshes : Mesh array
      Positions : float32 array
      Normals : float32 array option
      Uvs : float32 array option
      Tangents : float32 array option
      BlendIndices : byte array option
      BlendWeights : byte array option
      Triangles : int array
      Joints : Joint array
      Poses : Pose array
      Anims : Anim array
      FrameChannels : int array
      NumFrameChannels : int
      PoseChannelOffsets : int array }

val load : path:string -> Model
```

## Assets — `world/Assets.fs`

```fsharp
type PropAsset = { Id : string; Name : string; ModelPath : string }
type EnvironmentAsset = { Id : string; Name : string; ModelPath : string }

type AssetManager =
    { Device : Gpu.Device
      Actors : Dictionary<string, ActorAsset.ActorAsset>
      Props : Dictionary<string, PropAsset>
      Environments : Dictionary<string, EnvironmentAsset> }

val scan : root:string -> device:Gpu.Device -> AssetManager
val createActorRender : manager:AssetManager -> assetId:string -> ActorRender.ActorRender
```

## Shared actor asset — `world/ActorAsset.fs`

```fsharp
type ActorAsset =
    { Id : string
      Name : string
      ModelPath : string
      Mesh : Mesh.Mesh
      Model : Actor.ActorModel }
```

## Actor (animation sampling) — `world/Actor.fs`

```fsharp
type ActorModel =
    { Model : Iqm.Model
      InverseBind : Matrix4x4 array }

[<Struct>]
type AnimationSlot =
    { AnimIndex : int
      mutable Frame : float32
      PlaybackFps : float32
      Weight : float32
      Loop : bool
      Enabled : bool }

type PoseFrame =
    { Local : Matrix4x4 array
      World : Matrix4x4 array
      Skinning : Matrix4x4 array }

val createModel : Iqm.Model -> ActorModel
val findAnimIndex : ActorModel -> string -> int option
val createSlot : Iqm.Model -> int -> bool -> AnimationSlot
val createPoseFrame : Iqm.Model -> PoseFrame
val advanceInPlace : ActorModel -> byref<AnimationSlot> -> float32 -> unit
val advance : ActorModel -> AnimationSlot -> float32 -> AnimationSlot
val samplePose : ActorModel -> int -> float32 -> bool -> PoseFrame -> unit
```

## GPU mesh path — `render/{Gpu,Mesh}.fs` + `world/ActorRender.fs`

```fsharp
// render/Gpu.fs
type Device = nativeint
type Buffer = nativeint
type TransferBuffer = nativeint
type CommandBuffer = nativeint
type Pipeline = nativeint
type Texture = nativeint
type RenderPass = nativeint

let BonesMax = 205

val createBuffer : Device -> SDL.GPUBufferUsageFlags -> uint32 -> Buffer
val createTransferBuffer : Device -> uint32 -> TransferBuffer
val uploadBufferOnce : Device -> Buffer -> byte array -> unit
val writeMatrix : byte array -> int -> Matrix4x4 -> unit
val writeVector4 : byte array -> int -> Vector4 -> unit
val createMeshPipeline : Device -> SDL.GPUTextureFormat -> SDL.GPUTextureFormat -> Pipeline

// render/Mesh.fs
[<Struct>]
type Mesh =
    { Device : Gpu.Device
      VertexBuffer : Gpu.Buffer
      IndexBuffer : Gpu.Buffer
      IndexCount : int
      PerDrawUniformBytes : byte array
      VertexBinding : SDL.GPUBufferBinding array
      IndexBinding : SDL.GPUBufferBinding }

val create : device:Gpu.Device -> positions:float32 array -> normals:float32 array
             -> uvs:float32 array -> boneIds:byte array -> boneWeights:byte array
             -> indices:int array -> Mesh
val draw : m:Mesh -> renderPass:Gpu.RenderPass -> cmd:Gpu.CommandBuffer -> pipeline:Gpu.Pipeline
           -> viewProjection:Matrix4x4 -> model:Matrix4x4 -> lightDir:Vector3
           -> baseColor:Vector4 -> unit

// world/ActorRender.fs
[<Struct>]
type ActorRender =
    { Asset : ActorAsset.ActorAsset
      mutable Animation : Actor.AnimationSlot
      Pose : Actor.PoseFrame
      BoneBuffer : Gpu.Buffer
      BoneTransfer : Gpu.TransferBuffer
      BoneBytes : byte array }

val create : device:Gpu.Device -> asset:ActorAsset.ActorAsset -> ActorRender
val advanceInPlace : byref<ActorRender> -> dt:float32 -> unit
val upload : r:ActorRender -> cmd:Gpu.CommandBuffer -> unit
val draw : r:ActorRender -> renderPass:Gpu.RenderPass -> cmd:Gpu.CommandBuffer -> pipeline:Gpu.Pipeline
           -> viewProjection:Matrix4x4 -> model:Matrix4x4 -> lightDir:Vector3
           -> baseColor:Vector4 -> unit
val release : r:ActorRender -> unit
```

## Entity/Zone — `world/{Entity,Zone}.fs`

```fsharp
[<Struct>]
type Actor =
    { Base : Entity
      AssetId : string
      mutable Renderer : ActorRender.ActorRender }

val spawnActor : zone:Zone -> assets:Assets.AssetManager -> entity:Entity -> assetId:string -> EntityRef
val spawnProp : zone:Zone -> entity:Entity -> EntityRef
val setActorWithAsset : zone:Zone -> assets:Assets.AssetManager -> ref:EntityRef -> entity:Entity -> assetId:string -> unit
val removeEntity : zone:Zone -> ref:EntityRef -> unit
val entityAt : zone:Zone -> ref:EntityRef -> Entity option
val assetIdAt : zone:Zone -> ref:EntityRef -> string
val setEntity : zone:Zone -> ref:EntityRef -> entity:Entity -> unit
```

## Rules

- `Iqm.load` raises on a bad magic/version, a missing `POSITION` array, or an
  unsupported vertex kind/format; it never returns a partially-populated model.
- `Assets.scan` raises on an actor asset with no model path, or a model whose
  joint count exceeds `Gpu.BonesMax`.
- `Actor.samplePose` writes into the caller's `PoseFrame`; it does not allocate
  per frame. `advanceInPlace` mutates the slot's `Frame` through a `byref`.
- `ActorRender.create` uploads the static vertex + index buffers once (via the
  shared `ActorAsset`); `upload` copies the sampled bone palette to the storage
  buffer, and `draw` binds it, pushes `PerDraw`, and issues one indexed draw.
- `release` frees the per-instance bone buffers only; the shared `ActorAsset`
  (mesh + model) is manager-owned and shared across instances.
- `Zone.spawnActor`/`setActorWithAsset` store `AssetId` and create the renderer
  eagerly; `removeEntity` releases it. `Command.apply`/`undo` preserve `AssetId`
  across undo/redo.
- The mesh pipeline is depth-tested and draws into the existing scene pass (the
  debug grid/gizmos remain visible).
