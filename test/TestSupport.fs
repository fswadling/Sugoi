module TestSupport

// Shared device-free fixtures for the asset residency change (spec 050). An environment asset is
// built in the **loaded** shape (its whole unit present, GPU handles defaulted) so the real
// `Zone`/physics/render paths run headlessly; the GPU device is never dereferenced on those paths.

open System.Collections.Generic
open System.Numerics
open EnvironmentAsset

let private unitBounds : Gltf.Bounds =
    { Min = Vector3.Zero; Max = Vector3.One }

// A submesh with data-only geometry and defaulted GPU handles (never read on these paths).
let loadedSubmesh (positions: float32 array) (indices: int array) : LoadedSubmesh =
    { LoadedSubmesh.Positions = positions
      Normals = Array.zeroCreate positions.Length
      Uvs = Array.zeroCreate (positions.Length / 3 * 2)
      Indices = indices
      MaterialIndex = -1
      Mesh = Unchecked.defaultof<StaticMesh.StaticMesh>
      Material = Unchecked.defaultof<Texture.Material> }

let part (name: string) (submeshIndex: int) (submeshCount: int) : EnvironmentPart =
    { Name = name
      HierarchyPath = name
      World = Matrix4x4.Identity
      Bounds = unitBounds
      SubmeshIndex = submeshIndex
      SubmeshCount = submeshCount }

// An environment/prop asset in the loaded shape: its whole unit present, GPU handles defaulted.
let residentAsset (id: string) (submeshes: LoadedSubmesh array) (parts: EnvironmentPart array) : EnvironmentAsset =
    { Id = id
      Name = id
      ModelPath = ""
      Orientation = Quaternion.Identity
      Loaded =
        Some
            { EnvironmentLoaded.Submeshes = submeshes
              Parts = parts
              Bounds = unitBounds
              Materials = [||]
              SubmeshBytes = 0L
              TextureKeys = [||] } }

// A device-free loader: safe because nothing on these paths dereferences Device.
let loader (device: Gpu.Device) : Assets.Loader =
    { Device = device
      White = Unchecked.defaultof<Texture.Texture>
      DefaultMaterial = Unchecked.defaultof<Texture.Material>
      Textures = Dictionary() }

// A device-free asset manager: safe because the prop/part paths never dereference Device.
let manager
    (device: Gpu.Device)
    (actors: Dictionary<string, ActorAsset.ActorAsset>)
    (props: Dictionary<string, EnvironmentAsset>)
    (environments: Dictionary<string, EnvironmentAsset>) : Assets.AssetManager =
    { Device = device
      Root = ""
      Actors = actors
      Props = props
      Environments = environments
      Loader = lazy (loader device)
      Cache = Lru.create ()
      ResidentBytes = 0L }
