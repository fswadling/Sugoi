module Assets

// Asset manager: scans the actors/environments/props subdirectories for s-syntax
// manifests and returns them as id-keyed collections. Props and environments are
// metadata-only this phase; actor assets are loaded eagerly at startup (scan +
// parse + GPU upload) — mirroring odin-game's `Asset_Manager`.

open System
open System.Collections.Generic
open System.IO
open Prime

// The on-disk manifest shape (id/name/model). Fields are named, so the
// SymbolicExpansion parser is robust to reordering.
[<SymbolicExpansion>]
type private Manifest =
    { Id : string
      Name : string
      Model : string }

// Metadata-only asset kinds for props and environments. These are listed and
// spawnable/selectable this phase, but their models are not loaded yet.
type PropAsset =
    { Id : string
      Name : string
      ModelPath : string }

type EnvironmentAsset =
    { Id : string
      Name : string
      ModelPath : string }

// Cross-screen asset manager, mirroring odin-game's `Asset_Manager`. Lives on
// `SharedState` so every screen reaches the same loaded assets and GPU device.
// Assets are keyed by id: props/environments don't look up by id yet, but will
// once their loading lands.
type AssetManager =
    { Device : Gpu.Device
      Actors : Dictionary<string, ActorAsset.ActorAsset>
      Props : Dictionary<string, PropAsset>
      Environments : Dictionary<string, EnvironmentAsset> }

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

// Scan all asset kinds into the manager: props/environments are metadata-only
// this phase, actors are loaded eagerly via `loadActors`.
let scan (root: string) (device: Gpu.Device) : AssetManager =
    let props = Dictionary<string, PropAsset>() 
    for (id, name, modelPath) in scanDir root "props" do
        props.[id] <- { Id = id; Name = name; ModelPath = modelPath }

    let environments = Dictionary<string, EnvironmentAsset>()
    for (id, name, modelPath) in scanDir root "environments" do
        environments.[id] <- { Id = id; Name = name; ModelPath = modelPath }
        
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
