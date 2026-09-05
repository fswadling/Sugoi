# Contract: Environment + Props

Public surfaces added by this feature. Signatures are illustrative F#, matching
the data model.

## glTF — `core/Gltf.fs`

```fsharp
type Bounds = { Min : Vector3; Max : Vector3 }

type Submesh =
    { Name : string
      Material : int
      BaseColor : Vector4
      Positions : float32 array
      Normals : float32 array
      Uvs : float32 array
      Indices : int array }

type Model = { Submeshes : Submesh array; Bounds : Bounds }

val load : path:string -> Model
```

## Static mesh + pipeline — `render/{Gpu,StaticMesh}.fs`

```fsharp
// render/Gpu.fs (added)
val createStaticPipeline : Device -> SDL.GPUTextureFormat -> SDL.GPUTextureFormat -> Pipeline

// render/StaticMesh.fs
[<Struct>]
type StaticMesh =
    { Device : Gpu.Device
      VertexBuffer : Gpu.Buffer
      IndexBuffer : Gpu.Buffer
      IndexCount : int
      PerDrawUniformBytes : byte array
      VertexBinding : SDL.GPUBufferBinding array
      IndexBinding : SDL.GPUBufferBinding }

val create : device:Gpu.Device -> positions:float32 array -> normals:float32 array
             -> uvs:float32 array -> indices:int array -> StaticMesh
val draw : m:StaticMesh -> renderPass:Gpu.RenderPass -> cmd:Gpu.CommandBuffer -> pipeline:Gpu.Pipeline
           -> viewProjection:Matrix4x4 -> model:Matrix4x4 -> lightDir:Vector3
           -> baseColor:Vector4 -> unit
```

## Shared static asset — `world/EnvironmentAsset.fs`

```fsharp
type EnvironmentAsset =
    { Id : string
      Name : string
      ModelPath : string
      Orientation : Quaternion
      Submeshes : StaticMesh.StaticMesh array }
```

## Static rendering — `world/EnvironmentRender.fs`

```fsharp
[<Struct>]
type EnvironmentRender =
    { Asset : EnvironmentAsset.EnvironmentAsset
      OrientationMatrix : Matrix4x4 }

val create : device:Gpu.Device -> asset:EnvironmentAsset.EnvironmentAsset -> EnvironmentRender
val draw : r:EnvironmentRender -> renderPass:Gpu.RenderPass -> cmd:Gpu.CommandBuffer -> pipeline:Gpu.Pipeline
           -> viewProjection:Matrix4x4 -> model:Matrix4x4 -> lightDir:Vector3 -> unit
val release : r:EnvironmentRender -> unit
```

## Assets — `world/Assets.fs`

```fsharp
type AssetManager =
    { Device : Gpu.Device
      Actors : Dictionary<string, ActorAsset.ActorAsset>
      Props : Dictionary<string, EnvironmentAsset.EnvironmentAsset>
      Environments : Dictionary<string, EnvironmentAsset.EnvironmentAsset> }

val scan : root:string -> device:Gpu.Device -> AssetManager
val createActorRender : manager:AssetManager -> assetId:string -> ActorRender.ActorRender
val createEnvironmentRender : manager:AssetManager -> assetId:string -> EnvironmentRender.EnvironmentRender
```

## Entity/Zone — `world/{Entity,Zone}.fs`

```fsharp
[<Struct>]
type Prop =
    { Base : Entity
      AssetId : string
      mutable Renderer : EnvironmentRender.EnvironmentRender }

[<Struct>]
type Environment =
    { Base : Entity
      AssetId : string
      mutable Renderer : EnvironmentRender.EnvironmentRender
      mutable Props : SlotMap<Prop> }

val spawnActor : zone:Zone -> assets:Assets.AssetManager -> entity:Entity -> assetId:string -> EntityRef
val spawnProp : zone:Zone -> assets:Assets.AssetManager -> entity:Entity -> assetId:string -> EntityRef
val setEnvironment : zone:Zone -> assets:Assets.AssetManager -> assetId:string -> unit
val setActorWithAsset : zone:Zone -> assets:Assets.AssetManager -> ref:EntityRef -> entity:Entity -> assetId:string -> unit
val removeEntity : zone:Zone -> ref:EntityRef -> unit
val entityAt : zone:Zone -> ref:EntityRef -> Entity option
val assetIdAt : zone:Zone -> ref:EntityRef -> string
val setEntity : zone:Zone -> ref:EntityRef -> entity:Entity -> unit
```

## Rules

- `Gltf.load` raises on data-URI buffers, sparse accessors, `skins`/`animations`,
  a non-triangle primitive, a missing `POSITION`, a non-`float32` attribute, or an
  unsupported index component type; it never returns a partially-populated model.
- `Assets.scan` raises on any actor/environment/prop asset with an empty `Model`
  path.
- `StaticMesh.create` uploads the static vertex + index buffers once;
  `StaticMesh.draw` pushes the `PerDraw` uniform and issues one indexed draw with
  no bone storage buffer.
- `EnvironmentRender.draw` composes `model * OrientationMatrix` and draws every
  submesh with its parsed `BaseColor`; `release` frees no shared buffers (the
  `EnvironmentAsset` is manager-owned).
- `Zone.spawnProp`/`setActorWithAsset` store the prop's `AssetId` and create its
  renderer eagerly; `removeEntity` releases it. `Command.apply`/`undo` preserve
  `AssetId` across undo/redo for props.
- `Zone.setEnvironment` unloads any existing environment and installs a fresh
  props slot map; it is not an undoable command in this phase.
- The static pipeline is depth-tested and draws into the existing scene pass; the
  environment and props draw alongside the skinned actor and the debug grid.
