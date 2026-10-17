module EnvironmentAsset

// Shared static asset for environments and props: manifest metadata and, when loaded, its whole
// runtime data. Mirrors odin-game's `Environment_Asset`/`Prop_Asset`, with the port's residency
// model (spec 050): an environment's parsed geometry and its GPU meshes and textures are **one
// loaded unit** — present or absent together — so one byte budget bounds RAM and VRAM, and an
// eviction drops the geometry with the buffers (a reload re-parses it).
//
// One type serves both kinds: a prop asset is a static model with identity orientation and no
// parts, while an environment also carries its parts (one per glTF node with a mesh), which
// `Zone.setEnvironment` turns into props.

open System.Numerics
open StaticMesh
open Texture

// One submesh: its parsed geometry and (while loaded) its uploaded buffers, together. The
// geometry is retained only for the life of the load — physics reads it at collider-build time,
// which is always after the ensure.
[<Struct>]
type LoadedSubmesh =
    { Positions : float32 array
      Normals : float32 array
      Uvs : float32 array
      Indices : int array
      MaterialIndex : int
      Mesh : StaticMesh             // GPU vertex/index buffers
      Material : Texture.Material } // GPU material bindings

// One glTF node with a mesh, ready to become a prop. `World` is the node's accumulated
// transform (applied as the prop's entity transform); `SubmeshIndex`/`SubmeshCount` slice the
// shared submesh array.
type EnvironmentPart =
    { Name : string
      HierarchyPath : string
      World : Matrix4x4
      Bounds : Gltf.Bounds
      SubmeshIndex : int
      SubmeshCount : int }

// Everything that becomes resident when an environment is loaded and is dropped when it is
// evicted: the geometry, the parts, the source materials, the GPU submeshes, the unit's byte
// size, and the shared texture keys it acquired (released on eviction).
type EnvironmentLoaded =
    { Submeshes : LoadedSubmesh array
      Parts : EnvironmentPart array
      Bounds : Gltf.Bounds
      Materials : Gltf.Material array
      SubmeshBytes : int64
      TextureKeys : string array }

// The asset's coarse lifecycle (spec 053), single-writer on the render thread. `Resident` is the one
// state the accessors read; `Loading` is coarse because the fine phases are derived on the manager.
type Residency =
    | NotLoaded
    | Loading
    | Resident of EnvironmentLoaded
    | Failed of Message : string

type EnvironmentAsset =
    { Id : string
      Name : string
      ModelPath : string          // absolute; resolved once at scan so a lazy load needs no root
      Orientation : Quaternion
      mutable Residency : Residency } // the coarse lifecycle (spec 053); `Resident` == on the GPU

// ── loaded accessors ──
//
// The unit is reached only where it must be present; a render or physics path over a not-loaded
// asset is a bug that fails loudly rather than reading a freed handle (spec 050 FR-011).

let isResident (asset: EnvironmentAsset) : bool =
    match asset.Residency with
    | Resident _ -> true
    | _ -> false

let submeshes (asset: EnvironmentAsset) : LoadedSubmesh array =
    match asset.Residency with
    | Resident loaded -> loaded.Submeshes
    | _ -> invalidOp (sprintf "environment asset '%s' is not loaded" asset.Id)

let parts (asset: EnvironmentAsset) : EnvironmentPart array =
    match asset.Residency with
    | Resident loaded -> loaded.Parts
    | _ -> invalidOp (sprintf "environment asset '%s' is not loaded" asset.Id)
