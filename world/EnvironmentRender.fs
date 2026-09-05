module EnvironmentRender

// Static (unskinned) renderer for environments and props. `EnvironmentRender`
// is the per-instance state owned inline by an environment part or prop entity;
// the shared per-asset data lives on `EnvironmentAsset.EnvironmentAsset`. It
// draws a contiguous range of the asset's static submeshes (a whole prop asset,
// or one environment part's range) with the instance's orientation.

open System.Numerics
open SDL3
open Gpu
open EnvironmentAsset
open StaticMesh

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

// Compose the asset's authored orientation into the model matrix and draw the
// instance's submesh range, each submesh with its parsed base color.
let draw (r: EnvironmentRender) (renderPass: Gpu.RenderPass) (cmd: Gpu.CommandBuffer) (pipeline: Gpu.Pipeline) (viewProjection: Matrix4x4) (model: Matrix4x4) (lightDir: Vector3) : unit =
    let world = model * r.OrientationMatrix
    let last = r.SubmeshIndex + r.SubmeshCount - 1
    for i in r.SubmeshIndex .. last do
        let sub = r.Asset.Submeshes.[i]
        do StaticMesh.draw sub.Mesh renderPass cmd pipeline viewProjection world lightDir sub.BaseColor

// No per-instance GPU buffers: the static submesh buffers live on the shared
// `EnvironmentAsset` (manager-owned). Kept as the seam where per-prop buffers
// would free in later phases.
let release (_r: EnvironmentRender) : unit = ()
