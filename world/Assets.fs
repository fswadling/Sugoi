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
        Directory.EnumerateFiles(full, "*.manifest", SearchOption.AllDirectories)
        |> Seq.map readManifest
        |> Seq.toArray
    else
        Array.empty

// ── texture / material loading ──

// Texture decode/upload cache shared across all assets scanned in one pass. The
// white placeholder and default material are device-wide; textures are cached by
// absolute path so a map shared by multiple submeshes/assets is decoded once.
type private Loader =
    { Device : Gpu.Device
      White : Texture.Texture
      DefaultMaterial : Texture.Material
      Textures : Dictionary<string, Texture.Texture> }

let private createLoader (device: Gpu.Device) : Loader =
    let white = Texture.solid device 255uy 255uy 255uy 255uy
    { Device = device
      White = white
      DefaultMaterial = Texture.defaultMaterial white (Vector4(0.9f, 0.9f, 0.9f, 1.0f))
      Textures = Dictionary<string, Texture.Texture>() }

let private loadTexture (loader: Loader) (path: string) : Texture.Texture =
    match loader.Textures.TryGetValue(path) with
    | true, t -> t
    | false, _ ->
    let t = Texture.load loader.Device path
    loader.Textures.[path] <- t
    t

// Build a runtime PBR material from a parsed glTF material, resolving its texture
// URIs against the glTF file's directory.
let private resolveMaterial (loader: Loader) (gltfDir: string) (m: Gltf.Material) : Texture.Material =
    let tex (r: Gltf.TextureRef option) : Texture.Texture option =
        r |> Option.map (fun rr -> loadTexture loader (Path.Combine(gltfDir, rr.Uri)))
    Texture.makeMaterial
        loader.White
        m.BaseColor
        m.Metallic
        m.Roughness
        m.Emissive
        (tex m.AlbedoTexture)
        (tex m.NormalTexture)
        (tex m.MetallicRoughnessTexture)
        (tex m.EmissiveTexture)

// ── actor asset loading / resolution ──

// Scan the actors directory and eagerly load (parse + upload) each asset, keyed
// by id. Raises on an actor asset with no model path, or a model exceeding the
// GPU bone palette. The actor's IQM meshes each name an albedo texture (their
// `Material` string); these become per-submesh PBR materials sharing one combined
// vertex/index buffer. The loader is lazy so a headless scan (no actors) never
// touches the GPU.
let private loadActors (loader: Lazy<Loader>) (root: string) : Dictionary<string, ActorAsset.ActorAsset> =
    let actors = Dictionary<string, ActorAsset.ActorAsset>()
    for (id, name, modelPath) in scanDir root "actors" do
        if String.IsNullOrEmpty modelPath then
            failwithf "asset manager: actor asset '%s' has no model path" id

        let loader' = loader.Value
        let actorDir = Path.GetDirectoryName(Path.Combine(root, modelPath))
        let fallback = Texture.defaultMaterial loader'.White (Vector4(0.85f, 0.75f, 0.65f, 1.0f))
        let model = Iqm.load (Path.Combine(root, modelPath))
        if model.Joints.Length > Gpu.BonesMax then
            failwithf "iqm: model has %d joints, exceeding BonesMax %d" model.Joints.Length Gpu.BonesMax

        let actorModel = Actor.createModel model
        let vertexCount = model.Positions.Length / 3
        let normals = Option.defaultWith (fun () -> Array.zeroCreate<float32> (vertexCount * 3)) model.Normals
        let uvs = Option.defaultWith (fun () -> Array.zeroCreate<float32> (vertexCount * 2)) model.Uvs
        let boneIds = Option.defaultWith (fun () -> Array.zeroCreate<byte> (vertexCount * 4)) model.BlendIndices
        let boneWeights = Option.defaultWith (fun () -> Array.zeroCreate<byte> (vertexCount * 4)) model.BlendWeights

        let submeshes : Mesh.Submesh array =
            if model.Meshes.Length = 0 then
                [| { FirstIndex = 0
                     IndexCount = model.Triangles.Length
                     Material = fallback } |]
            else

            model.Meshes
            |> Array.map (fun mesh ->
                let albedo =
                    if String.IsNullOrEmpty mesh.Material then None
                    else Some (loadTexture loader' (Path.Combine(actorDir, "akane", mesh.Material)))
                { FirstIndex = mesh.FirstTriangle * 3
                  IndexCount = mesh.NumTriangles * 3
                  Material = Texture.makeMaterial loader'.White (Vector4(1.0f, 1.0f, 1.0f, 1.0f)) 0.0f 0.8f Vector3.Zero albedo None None None })

        let mesh = Mesh.create loader'.Device model.Positions normals uvs boneIds boneWeights model.Triangles submeshes
        actors.[id] <- { Id = id; Name = name; ModelPath = modelPath; Mesh = mesh; Model = actorModel }
    actors

// ── static (environment/prop) asset loading ──

// Parse a glTF model and upload each submesh as a static GPU buffer, producing a
// shared `EnvironmentAsset`. Each submesh resolves its glTF material index to a
// runtime PBR material (default dielectric when the primitive has none).
// Environments also carry their parsed parts (one per glTF node with a mesh);
// prop assets render the whole model, so their parts list stays empty.
let private loadStaticModel (loader: Loader) (root: string) (id: string) (name: string) (modelPath: string) (includeParts: bool) : EnvironmentAsset.EnvironmentAsset =
    let model = Gltf.load (Path.Combine(root, modelPath))
    let gltfDir = Path.GetDirectoryName(Path.Combine(root, modelPath))
    let materialCache = Dictionary<int, Texture.Material>()
    let materialFor (submesh: Gltf.Submesh) : Texture.Material =
        let idx = submesh.Material
        match materialCache.TryGetValue(idx) with
        | true, m -> m
        | false, _ ->
        let m =
            if idx < 0 || idx >= model.Materials.Length then loader.DefaultMaterial
            else resolveMaterial loader gltfDir model.Materials.[idx]
        materialCache.[idx] <- m
        m
    let submeshes =
        model.Submeshes
        |> Array.map (fun s ->
            { EnvironmentAsset.Mesh = StaticMesh.create loader.Device s.Positions s.Normals s.Uvs s.Indices
              EnvironmentAsset.Material = materialFor s })
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
    // Create the GPU loader lazily: a headless scan (no assets, or a null test
    // device) never allocates a placeholder texture.
    let loader = lazy (createLoader device)

    let props = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    for (id, name, modelPath) in scanDir root "props" do
        if String.IsNullOrEmpty modelPath then
            failwithf "asset manager: prop asset '%s' has no model path" id
        props.[id] <- loadStaticModel loader.Value root id name modelPath false

    let environments = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    for (id, name, modelPath) in scanDir root "environments" do
        if String.IsNullOrEmpty modelPath then
            failwithf "asset manager: environment asset '%s' has no model path" id
        environments.[id] <- loadStaticModel loader.Value root id name modelPath true

    let actors = loadActors loader root

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
