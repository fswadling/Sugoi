# Contract: Environment Parts as Props

Public surfaces added or changed by this feature. Signatures are illustrative F#,
matching the data model.

## glTF — `core/Gltf.fs`

```fsharp
type Part =
    { Name : string
      HierarchyPath : string
      World : Matrix4x4
      SubmeshIndex : int
      SubmeshCount : int }

type Model =
    { Submeshes : Submesh array   // local space
      Parts : Part array
      Bounds : Bounds }

val load : path:string -> Model
```

## Shared static asset — `world/EnvironmentAsset.fs`

```fsharp
type EnvironmentPart =
    { Name : string
      HierarchyPath : string
      World : Matrix4x4
      SubmeshIndex : int
      SubmeshCount : int }

type EnvironmentAsset =
    { Id : string
      Name : string
      ModelPath : string
      Orientation : Quaternion
      Submeshes : StaticSubmesh array
      Parts : EnvironmentPart array }
```

## Static rendering — `world/EnvironmentRender.fs`

```fsharp
[<Struct>]
type EnvironmentRender =
    { Asset : EnvironmentAsset.EnvironmentAsset
      OrientationMatrix : Matrix4x4
      SubmeshIndex : int
      SubmeshCount : int }

val create : asset:EnvironmentAsset.EnvironmentAsset -> orientation:Quaternion
             -> submeshIndex:int -> submeshCount:int -> EnvironmentRender
val draw : r:EnvironmentRender -> renderPass:Gpu.RenderPass -> cmd:Gpu.CommandBuffer
           -> pipeline:Gpu.Pipeline -> viewProjection:Matrix4x4 -> model:Matrix4x4
           -> lightDir:Vector3 -> unit
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
val createPropRender : manager:AssetManager -> assetId:string -> EnvironmentRender.EnvironmentRender
val createPartRender : manager:AssetManager -> environmentAssetId:string -> submeshIndex:int
                       -> submeshCount:int -> EnvironmentRender.EnvironmentRender
```

## Entity/Zone — `world/{Entity,Zone}.fs`

```fsharp
[<Struct>]
type AssetBinding =
    { AssetId : string
      SubmeshIndex : int
      SubmeshCount : int }

[<Struct>]
type Prop =
    { Base : Entity
      AssetId : string
      SubmeshIndex : int
      SubmeshCount : int
      mutable Renderer : EnvironmentRender.EnvironmentRender }

[<Struct>]
type Environment =
    { Base : Entity
      AssetId : string
      Orientation : Quaternion
      mutable Props : SlotMap<Prop> }

val spawnProp : zone:Zone -> assets:Assets.AssetManager -> entity:Entity -> assetId:string -> EntityRef
val spawnPropBinding : zone:Zone -> assets:Assets.AssetManager -> entity:Entity -> binding:AssetBinding -> EntityRef
val setEnvironment : zone:Zone -> assets:Assets.AssetManager -> assetId:string -> unit
val setActorWithAsset : zone:Zone -> assets:Assets.AssetManager -> ref:EntityRef -> entity:Entity -> binding:AssetBinding -> unit
val removeEntity : zone:Zone -> ref:EntityRef -> unit
val entityAt : zone:Zone -> ref:EntityRef -> Entity option
val bindingAt : zone:Zone -> ref:EntityRef -> AssetBinding
val assetIdAt : zone:Zone -> ref:EntityRef -> string
val setEntity : zone:Zone -> ref:EntityRef -> entity:Entity -> unit
```

`state/Command.fs` gains:

```fsharp
type Command =
    | Spawn of EntityRef * Entity * AssetBinding
    | Delete of EntityRef * Entity * AssetBinding
    | Duplicate of EntityRef * Entity * AssetBinding
    | Transform of EntityRef * Transform * Transform
    | SetEnvironment of PreviousAssetId : string * NewAssetId : string
```

## Rules

- `Gltf.load` emits local-space `Submeshes` and one `Part` per mesh-bearing node;
  a `Part.World` is the accumulated `S * R * T` and `SubmeshCount` is the node
  mesh's primitive count. It raises on `matrix` nodes, sparse accessors,
  skins/animations, non-triangle primitives, a missing `POSITION`, a non-`float32`
  attribute, or an unsupported index component type.
- `Zone.setEnvironment` decomposes each `Part.World` into an `Entity.Transform`
  (`Matrix4x4.Decompose`) and spawns a prop with `AssetId = ""`, the part's
  submesh range, and that transform; it keeps placed props (non-empty `AssetId`)
  and drops the previous environment's parts (empty `AssetId`). An empty asset id
  clears the environment.
- A placed prop (`AssetId` non-empty) draws the whole asset
  (`SubmeshIndex = 0`, `SubmeshCount = asset.Submeshes.Length`) with identity
  orientation; an environment part draws its range with the environment's
  orientation.
- `EnvironmentRender.draw` iterates only its `SubmeshIndex`/`SubmeshCount` range
  and composes `model * OrientationMatrix`; it issues one indexed draw per submesh
  with that submesh's `BaseColor`.
- The environment entity renders no geometry directly; `GameplayScreen.renderScene`
  draws only the props slot map (placed props + imported parts).
