module Assets

// Asset manager: scans the actors/environments/props subdirectories for s-syntax
// manifests and eagerly loads each asset (parse + GPU upload) into id-keyed
// collections — mirroring odin-game's `Asset_Manager`. Actors load via IQM;
// environments and props load via the scoped glTF parser into shared static
// submeshes.

open System
open System.Collections.Generic
open System.IO
open System.Numerics
open Prime

// The on-disk manifest shape (id/name/model). Fields are named, so the
// SymbolicExpansion parser is robust to reordering.
[<SymbolicExpansion>]
type private Manifest =
    { Id : string
      Name : string
      Model : string }

// Cross-screen asset manager, mirroring odin-game's `Asset_Manager`. Lives on
// `SharedState` so every screen reaches the same loaded assets and GPU device.
// Environments and props share the `EnvironmentAsset` runtime type (a static
// model); a prop is an environment-like model with identity orientation.
type AssetManager =
    { Device : Gpu.Device
      Actors : Dictionary<string, ActorAsset.ActorAsset>
      Props : Dictionary<string, EnvironmentAsset.EnvironmentAsset>
      Environments : Dictionary<string, EnvironmentAsset.EnvironmentAsset> }

// ── scanning ──

let private readManifest (path: string) : string * string * string =
    let stem = Path.GetFileNameWithoutExtension(path)
    let manifest = File.ReadAllText(path) |> scvalue<Manifest>
    let id = if String.IsNullOrEmpty manifest.Id then stem else manifest.Id
    let name = if String.IsNullOrEmpty manifest.Name then id else manifest.Name
    (id, name, manifest.Model)

let private scanDir (root: string) (dir: string) : (string * string * string) array =
    let full = Path.Combine(root, dir)
    if Directory.Exists(full) then
        Directory.EnumerateFiles(full, "*.manifest")
        |> Seq.map readManifest
        |> Seq.toArray
    else
        Array.empty

// ── actor asset loading / resolution ──

// Scan the actors directory and eagerly load (parse + upload) each asset, keyed
// by id. Raises on an actor asset with no model path, or a model exceeding the
// GPU bone palette.
let private loadActors (root: string) (device: Gpu.Device) : Dictionary<string, ActorAsset.ActorAsset> =
    let actors = Dictionary<string, ActorAsset.ActorAsset>()
    for (id, name, modelPath) in scanDir root "actors" do
        if String.IsNullOrEmpty modelPath then
            failwithf "asset manager: actor asset '%s' has no model path" id

        let model = Iqm.load (Path.Combine(root, modelPath))
        if model.Joints.Length > Gpu.BonesMax then
            failwithf "iqm: model has %d joints, exceeding BonesMax %d" model.Joints.Length Gpu.BonesMax

        let actorModel = Actor.createModel model
        let vertexCount = model.Positions.Length / 3
        let normals = Option.defaultWith (fun () -> Array.zeroCreate<float32> (vertexCount * 3)) model.Normals
        let uvs = Option.defaultWith (fun () -> Array.zeroCreate<float32> (vertexCount * 2)) model.Uvs
        let boneIds = Option.defaultWith (fun () -> Array.zeroCreate<byte> (vertexCount * 4)) model.BlendIndices
        let boneWeights = Option.defaultWith (fun () -> Array.zeroCreate<byte> (vertexCount * 4)) model.BlendWeights

        let mesh = Mesh.create device model.Positions normals uvs boneIds boneWeights model.Triangles
        actors.[id] <- { Id = id; Name = name; ModelPath = modelPath; Mesh = mesh; Model = actorModel }
    actors

// ── static (environment/prop) asset loading ──

// Parse a glTF model and upload each submesh as a static GPU buffer, producing a
// shared `EnvironmentAsset`. Environments also carry their parsed parts (one per
// glTF node with a mesh); prop assets render the whole model, so their parts list
// stays empty.
let private loadStaticModel (root: string) (device: Gpu.Device) (id: string) (name: string) (modelPath: string) (includeParts: bool) : EnvironmentAsset.EnvironmentAsset =
    let model = Gltf.load (Path.Combine(root, modelPath))
    let submeshes =
        model.Submeshes
        |> Array.map (fun s -> { EnvironmentAsset.Mesh = StaticMesh.create device s.Positions s.Normals s.Uvs s.Indices; EnvironmentAsset.BaseColor = s.BaseColor })
    let parts =
        if includeParts then
            model.Parts
            |> Array.map (fun p ->
                { EnvironmentAsset.Name = p.Name
                  EnvironmentAsset.HierarchyPath = p.HierarchyPath
                  EnvironmentAsset.World = p.World
                  EnvironmentAsset.SubmeshIndex = p.SubmeshIndex
                  EnvironmentAsset.SubmeshCount = p.SubmeshCount })
        else
            Array.empty
    { Id = id
      Name = name
      ModelPath = modelPath
      Orientation = Quaternion.Identity
      Submeshes = submeshes
      Parts = parts }

// Scan all asset kinds into the manager. Actors load via IQM; environments and
// props load via glTF. A missing `Model` path raises for every kind.
let scan (root: string) (device: Gpu.Device) : AssetManager =
    let props = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    for (id, name, modelPath) in scanDir root "props" do
        if String.IsNullOrEmpty modelPath then
            failwithf "asset manager: prop asset '%s' has no model path" id
        props.[id] <- loadStaticModel root device id name modelPath false

    let environments = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    for (id, name, modelPath) in scanDir root "environments" do
        if String.IsNullOrEmpty modelPath then
            failwithf "asset manager: environment asset '%s' has no model path" id
        environments.[id] <- loadStaticModel root device id name modelPath true

    let actors = loadActors root device

    { Device = device
      Actors = actors
      Props = props
      Environments = environments }

// Create the per-instance renderer for an actor asset id (the asset is already
// loaded at startup).
let createActorRender (manager: AssetManager) (assetId: string) : ActorRender.ActorRender =
    let asset = manager.Actors.[assetId]
    ActorRender.create manager.Device asset

// Create the per-instance renderer for a placed prop asset id (already loaded at
// startup): the whole model, identity orientation.
let createPropRender (manager: AssetManager) (assetId: string) : EnvironmentRender.EnvironmentRender =
    let asset = manager.Props.[assetId]
    EnvironmentRender.create asset Quaternion.Identity 0 asset.Submeshes.Length

// Create the per-instance renderer for one environment part: the part's submesh
// range, with the environment's authored orientation.
let createPartRender (manager: AssetManager) (environmentAssetId: string) (submeshIndex: int) (submeshCount: int) : EnvironmentRender.EnvironmentRender =
    let asset = manager.Environments.[environmentAssetId]
    EnvironmentRender.create asset asset.Orientation submeshIndex submeshCount
