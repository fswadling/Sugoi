module EnvironmentRender

// Static (unskinned) renderer data for environments and props. `EnvironmentRender`
// is the per-instance state owned inline by an environment part or prop entity; the
// shared per-asset data lives on `EnvironmentAsset.EnvironmentAsset`. It records
// which contiguous range of the asset's static submeshes this instance uses and its
// orientation; `world/EnvironmentBatch.fs` reads these fields to batch one instanced
// draw per distinct submesh.

open System.Numerics
open EnvironmentAsset

[<Struct>]
type EnvironmentRender =
    { Asset : EnvironmentAsset.EnvironmentAsset
      OrientationMatrix : Matrix4x4
      SubmeshIndex : int
      SubmeshCount : int }

let create (asset: EnvironmentAsset.EnvironmentAsset) (orientation: Quaternion) (submeshIndex: int) (submeshCount: int) : EnvironmentRender =
    { Asset = asset
      OrientationMatrix = Matrix4x4.CreateFromQuaternion(orientation)
      SubmeshIndex = submeshIndex
      SubmeshCount = submeshCount }

// No per-instance GPU buffers: the static submesh buffers live on the shared
// `EnvironmentAsset` (manager-owned). Kept as the seam where per-prop buffers
// would free in later phases.
let release (_r: EnvironmentRender) : unit = ()
