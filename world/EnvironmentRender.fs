module EnvironmentRender

// Static (unskinned) renderer for environments and props. `EnvironmentRender`
// is the per-instance state owned inline by an environment or prop entity; the
// shared per-asset data lives on `EnvironmentAsset.EnvironmentAsset`. It wraps
// the asset's static submeshes plus the asset's authored orientation, and has no
// per-instance GPU buffers (the static buffers are shared, manager-owned).

open System.Numerics
open SDL3
open Gpu
open EnvironmentAsset
open StaticMesh

[<Struct>]
type EnvironmentRender =
    { Asset : EnvironmentAsset.EnvironmentAsset
      OrientationMatrix : Matrix4x4 }

let create (asset: EnvironmentAsset.EnvironmentAsset) : EnvironmentRender =
    { Asset = asset
      OrientationMatrix = Matrix4x4.CreateFromQuaternion(asset.Orientation) }

// Compose the asset's authored orientation into the model matrix and draw every
// submesh with its parsed base color.
let draw (r: EnvironmentRender) (renderPass: Gpu.RenderPass) (cmd: Gpu.CommandBuffer) (pipeline: Gpu.Pipeline) (viewProjection: Matrix4x4) (model: Matrix4x4) (lightDir: Vector3) : unit =
    let world = model * r.OrientationMatrix
    for sub in r.Asset.Submeshes do
        do StaticMesh.draw sub.Mesh renderPass cmd pipeline viewProjection world lightDir sub.BaseColor

// No per-instance GPU buffers: the static submesh buffers live on the shared
// `EnvironmentAsset` (manager-owned). Kept as the seam where per-prop buffers
// would free in later phases.
let release (_r: EnvironmentRender) : unit = ()
