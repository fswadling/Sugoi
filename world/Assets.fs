module Assets

// Asset manager: scans the actors/environments/props subdirectories for s-syntax
// manifests and loads each asset into id-keyed collections — mirroring odin-game's
// `Asset_Manager`. Actors load via IQM; environments and props load via the scoped glTF
// parser into shared static submeshes.
//
// Environments are **loaded on demand** (spec 050): startup reads only their manifests, and
// their whole loaded unit — parsed geometry plus GPU meshes and textures — is built on first
// use and held under a byte budget by an LRU over asset ids. Props and actors keep their eager
// load; only environments are bounded.

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Numerics
open Prime
open EnvironmentAsset

// The per-asset load-timing line is opt-in (`FFS_LOAD_TIMING=1`): one line per asset build.
let private loadTiming =
    match Environment.GetEnvironmentVariable "FFS_LOAD_TIMING" with
    | null -> false
    | v -> v <> "" && v <> "0"

// The on-disk manifest shape (id/name/model, plus an optional morph sidecar path and an
// optional portrait block). Fields are named, so the SymbolicExpansion parser is robust to
// reordering, and an absent block parses to a `PortraitPaths` whose fields are empty strings.
[<SymbolicExpansion>]
type private Manifest =
    { Id : string
      Name : string
      Model : string
      Morphs : string
      Portraits : ActorAsset.PortraitPaths }

// A texture the loader created, with the number of loaded assets currently binding it.
type TextureEntry =
    { Texture : Texture.Texture
      mutable Refs : int }

// Texture decode/upload cache shared across every asset. The white placeholder and default
// material are device-wide; textures are cached by absolute path and reference-counted, so a
// map shared by several submeshes or assets is decoded once and freed only when no loaded asset
// binds it (spec 050 FR-018).
type Loader =
    { Device : Gpu.Device
      White : Texture.Texture
      DefaultMaterial : Texture.Material
      Textures : Dictionary<string, TextureEntry> }

// Cross-screen asset manager, mirroring odin-game's `Asset_Manager`. Lives on
// `SharedState` so every screen reaches the same loaded assets and GPU device.
type AssetManager =
    { Device : Gpu.Device
      Root : string
      Actors : Dictionary<string, ActorAsset.ActorAsset>
      Props : Dictionary<string, EnvironmentAsset.EnvironmentAsset>
      Environments : Dictionary<string, EnvironmentAsset.EnvironmentAsset>
      // Created on first use, so a headless scan (no assets, or no GPU) never allocates a texture.
      // A texture-cache detail, not environment residency (see `scan` and `ensureEnvironmentResident`).
      Loader : Lazy<Loader>
      // Residency of environment assets: the LRU order (head = most recently used) and the
      // actual bytes those loaded environments hold (parsed geometry plus GPU data).
      Cache : Lru.Lru
      mutable ResidentBytes : int64 }

// ── the residency budget ──

// Bound on environment residency. Bounds the *whole loaded unit* — parsed geometry plus GPU
// meshes and textures — so it caps process RAM as well as VRAM. Sized for the measured worst
// case: `ArtRoom` is ~1.68 GB of textures alone; its parsed geometry is ~2 MB. A byte budget
// (not a count) keeps one heavy environment plus many light ones resident and caps growth.
[<Literal>]
let ENVIRONMENT_BUDGET_BYTES : int64 = 2048L * 1024L * 1024L   // 2 GB

// ── scanning ──

let private readManifest (path: string) : string * string * string * string * ActorAsset.PortraitPaths =
    let stem = Path.GetFileNameWithoutExtension(path)
    let manifest = File.ReadAllText(path) |> scvalue<Manifest>
    let id = if String.IsNullOrEmpty manifest.Id then stem else manifest.Id
    let name = if String.IsNullOrEmpty manifest.Name then id else manifest.Name
    (id, name, manifest.Model, manifest.Morphs, manifest.Portraits)

let private scanDir (root: string) (dir: string) : (string * string * string * string * ActorAsset.PortraitPaths) array =
    let full = Path.Combine(root, dir)
    if not (Directory.Exists(full)) then Array.empty else
    Directory.EnumerateFiles(full, "*.manifest", SearchOption.AllDirectories)
    |> Seq.map readManifest
    |> Seq.toArray

// ── texture / material loading ──

let private createLoader (device: Gpu.Device) : Loader =
    let white = Texture.solid device 255uy 255uy 255uy 255uy
    { Device = device
      White = white
      DefaultMaterial = Texture.defaultMaterial white (Vector4(0.9f, 0.9f, 0.9f, 1.0f))
      Textures = Dictionary<string, TextureEntry>() }

// Acquire a texture by path: get or create its cache entry, bump the refcount, and return the
// texture with the bytes newly made resident (0 when it was already resident).
let private acquireTexture (loader: Loader) (batch: Gpu.UploadBatch) (path: string) : Texture.Texture * int64 =
    match loader.Textures.TryGetValue path with
    | true, entry ->
        entry.Refs <- entry.Refs + 1
        entry.Texture, 0L
    | false, _ ->
    let t = Texture.load batch loader.Device path
    loader.Textures.[path] <- { Texture = t; Refs = 1 }
    t, t.Bytes

// Release a texture: decrement its refcount and free it (returning the bytes freed) at zero.
let private releaseTexture (loader: Loader) (path: string) : int64 =
    match loader.Textures.TryGetValue path with
    | false, _ -> 0L
    | true, entry ->

    entry.Refs <- entry.Refs - 1
    if entry.Refs > 0 then 0L else
    do loader.Textures.Remove path |> ignore
    do Texture.release entry.Texture
    entry.Texture.Bytes

// The actor path: acquire a texture and hold it for the process (actors are never evicted). It is
// not part of an asset build, so it runs in its own one-shot batch.
let private loadTexture (loader: Loader) (path: string) : Texture.Texture =
    let batch = Gpu.beginUpload loader.Device
    try
        let t, _ = acquireTexture loader batch path
        do Gpu.finishUpload batch
        t
    with _ ->
        // The batch owns staging buffers; release them if the load failed.
        do Gpu.abortUpload batch
        reraise ()

// Build a runtime PBR material from a parsed glTF material, resolving its texture URIs against
// the glTF file's directory. Returns the material, the texture keys it acquired (in order) and
// the bytes newly made resident, so an eviction can release exactly what a load acquired.
let private resolveMaterial (loader: Loader) (batch: Gpu.UploadBatch) (gltfDir: string) (m: Gltf.Material) : Texture.Material * string list * int64 =
    // Track each acquire as it happens so a later failure can undo the partial work; on success the
    // keys/bytes are handed back for the caller's own rollback and eventual release (FR-018).
    let acquired = ResizeArray<string>()
    let mutable added = 0L
    let tex (r: Gltf.TextureRef option) : Texture.Texture option =
        match r with
        | None -> None
        | Some rr ->

        let path = Path.Combine(gltfDir, rr.Uri)
        let t, delta = acquireTexture loader batch path
        acquired.Add path
        added <- added + delta
        Some t

    try
        let albedo = tex m.AlbedoTexture
        let normal = tex m.NormalTexture
        let metallicRoughness = tex m.MetallicRoughnessTexture
        let emissive = tex m.EmissiveTexture
        let material =
            Texture.makeMaterial loader.White m.BaseColor m.Metallic m.Roughness m.Emissive albedo normal metallicRoughness emissive
        material, List.ofSeq acquired, added
    with _ ->
        // A missing/corrupt texture partway through, or a failed material build, must not leak the
        // acquires already made: release them (per occurrence, matching the acquires) and reraise.
        for key in acquired do releaseTexture loader key |> ignore
        reraise ()

// ── actor asset loading / resolution ──

// Scan the actors directory and eagerly load (parse + upload) each asset, keyed by id. Raises
// on an actor asset with no model path, or a model exceeding the GPU bone palette. The actor's
// IQM meshes each name an albedo texture (their `Material` string); these become per-submesh
// PBR materials sharing one combined vertex/index buffer. The loader is lazy so a headless
// scan (no actors) never touches the GPU.
let private loadActors (loader: Lazy<Loader>) (root: string) : Dictionary<string, ActorAsset.ActorAsset> =
    let actors = Dictionary<string, ActorAsset.ActorAsset>()
    for (id, name, modelPath, morphsPath, portraitPaths) in scanDir root "actors" do
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
                     Material = fallback
                     IsFace = false } |]
            else

            model.Meshes
            |> Array.map (fun mesh ->
                let albedo =
                    if String.IsNullOrEmpty mesh.Material then None
                    else Some (loadTexture loader' (Path.Combine(actorDir, "akane", mesh.Material)))
                { FirstIndex = mesh.FirstTriangle * 3
                  IndexCount = mesh.NumTriangles * 3
                  Material = Texture.makeMaterial loader'.White (Vector4(1.0f, 1.0f, 1.0f, 1.0f)) 0.0f 0.8f Vector3.Zero albedo None None None
                  IsFace = Mesh.isFaceMeshName mesh.Name })

        // Actor bounds from the model's positions, for the capsule collider.
        if model.Positions.Length = 0 then failwithf "iqm: actor '%s' has no positions" id
        let mutable minV = Vector3(Single.PositiveInfinity, Single.PositiveInfinity, Single.PositiveInfinity)
        let mutable maxV = Vector3(Single.NegativeInfinity, Single.NegativeInfinity, Single.NegativeInfinity)
        for i in 0 .. model.Positions.Length / 3 - 1 do
            let x = model.Positions.[i * 3]
            let y = model.Positions.[i * 3 + 1]
            let z = model.Positions.[i * 3 + 2]
            minV <- Vector3(min minV.X x, min minV.Y y, min minV.Z z)
            maxV <- Vector3(max maxV.X x, max maxV.Y y, max maxV.Z z)
        let actorBounds : Gltf.Bounds = { Min = minV; Max = maxV }

        // The outline's averaged extrusion directions, derived once from the geometry (spec 041).
        let smoothNormals = Iqm.smoothNormals model.Positions normals
        let mesh = Mesh.create loader'.Device model.Positions normals uvs boneIds boneWeights smoothNormals model.Triangles submeshes

        // Morph targets, when the actor manifest names a sidecar: parse the manifest, read its
        // data file, and pack the offsets into the dense per-vertex layout the vertex shader
        // reads. Uploaded once and shared by every instance of the asset.
        let morphs =
            if String.IsNullOrEmpty morphsPath then None else

            let manifestPath = Path.Combine(root, morphsPath)
            let manifest = File.ReadAllText manifestPath |> ActorAsset.parseMorphManifest
            let dataPath = Path.Combine(Path.GetDirectoryName manifestPath, manifest.Data)
            let bytes = File.ReadAllBytes dataPath
            if bytes.Length % 4 <> 0 then
                failwithf "morph data '%s' is not a multiple of four bytes" dataPath
            else

            let data = Array.init (bytes.Length / 4) (fun i -> BitConverter.ToSingle(bytes, i * 4))
            let usable = ActorAsset.usableMorphs manifest
            if usable.Length = 0 then None else

            let deltas = ActorAsset.buildMorphDeltas usable data vertexCount
            let deltaBytes = Array.zeroCreate<byte> (deltas.Length * 4)
            do Buffer.BlockCopy(deltas, 0, deltaBytes, 0, deltaBytes.Length)
            let buffer = Gpu.createStorageBuffer loader'.Device deltaBytes
            let actorMorphs : ActorAsset.ActorMorphs =
                { Names = usable |> Array.map (fun m -> m.Name)
                  Count = usable.Length
                  Buffer = buffer }
            Some actorMorphs

        // Portraits: a declared set must be complete (a partial set is a broken asset, FR-006);
        // declaring none is legal and simply means the character cannot speak a portrait line.
        // Loaded once through the shared texture cache, so nothing is re-read per line (FR-010).
        let portraits =
            let declared = ActorAsset.portraitValues portraitPaths
            let isUnset (_, path: string) = String.IsNullOrEmpty path
            if declared |> List.forall isUnset then None else

            do ActorAsset.requireComplete portraitPaths
            let loadPortrait (path: string) = loadTexture loader' (Path.Combine(root, path))
            let loaded : ActorAsset.ActorPortraits =
                { Neutral = loadPortrait portraitPaths.Neutral
                  Happy = loadPortrait portraitPaths.Happy
                  Sad = loadPortrait portraitPaths.Sad
                  Angry = loadPortrait portraitPaths.Angry
                  Surprised = loadPortrait portraitPaths.Surprised }
            Some loaded

        actors.[id] <- { Id = id; Name = name; ModelPath = modelPath; Bounds = actorBounds; Mesh = mesh; Model = actorModel; Morphs = morphs; Portraits = portraits }
    actors

// ── static (environment/prop) asset loading ──

// Bounds over a contiguous submesh range (one part's geometry).
let private submeshRangeBounds (submeshes: Gltf.Submesh array) (first: int) (count: int) : Gltf.Bounds =
    let mutable minV = Vector3(Single.PositiveInfinity, Single.PositiveInfinity, Single.PositiveInfinity)
    let mutable maxV = Vector3(Single.NegativeInfinity, Single.NegativeInfinity, Single.NegativeInfinity)
    for i in first .. first + count - 1 do
        let positions = submeshes.[i].Positions
        for v in 0 .. positions.Length / 3 - 1 do
            let x = positions.[v * 3]
            let y = positions.[v * 3 + 1]
            let z = positions.[v * 3 + 2]
            minV <- Vector3(min minV.X x, min minV.Y y, min minV.Z z)
            maxV <- Vector3(max maxV.X x, max maxV.Y y, max maxV.Z z)
    { Min = minV; Max = maxV }

// Build an asset's whole loaded unit in one pass: parse the glTF, upload each submesh, resolve
// its material (acquiring the material's textures), and measure the unit. Returns the unit and
// the bytes newly made resident by the textures it acquired (the submesh bytes are on the unit).
let private buildLoaded (loader: Loader) (modelPath: string) (includeParts: bool) : EnvironmentLoaded * int64 =
    // One batch for the whole asset: every buffer and texture upload is recorded into it, packed into
    // a bounded staging buffer and flushed as it fills, then finished here (spec 052).
    Gpu.resetUploadStats()
    Texture.resetLoadStats()
    let totalStart = Stopwatch.GetTimestamp()
    let batch = Gpu.beginUpload loader.Device
    // Track what is built so a failure partway (e.g. a corrupt submesh) rolls back the GPU
    // resources already made, keeping the loader's refcounts balanced.
    let created = ResizeArray<StaticMesh.StaticMesh>()
    let keys = ResizeArray<string>()
    let mutable parseTicks = 0L
    // Only the build rolls back; the timing print below is outside the `try`, so a logging failure
    // cannot undo a successful upload.
    let loaded, textureAdded =
        try
            let t0 = Stopwatch.GetTimestamp()
            let model = Gltf.load modelPath
            parseTicks <- Stopwatch.GetTimestamp() - t0
            let gltfDir = Path.GetDirectoryName modelPath
            // A material is resolved — and its textures acquired — once per distinct material index.
            // A cache hit must contribute no keys and no bytes, or a material shared by several
            // submeshes would double-count its textures and over-release them (FR-018).
            let materialCache = Dictionary<int, Texture.Material>()
            let materialFor (index: int) : Texture.Material * string list * int64 =
                match materialCache.TryGetValue index with
                | true, material -> material, [], 0L
                | false, _ ->
                let material, keys, added =
                    if index < 0 || index >= model.Materials.Length then loader.DefaultMaterial, [], 0L
                    else resolveMaterial loader batch gltfDir model.Materials.[index]
                materialCache.[index] <- material
                material, keys, added
            let mutable submeshBytes = 0L
            let mutable textureAdded = 0L
            let submeshes =
                model.Submeshes
                |> Array.map (fun s ->
                    let mesh = StaticMesh.create batch loader.Device s.Positions s.Normals s.Uvs s.Indices
                    created.Add mesh
                    let vertexCount = s.Positions.Length / 3
                    // The unit's bytes: the retained geometry (RAM) plus the uploaded buffers (VRAM).
                    let cpuBytes = int64 (s.Positions.Length + s.Normals.Length + s.Uvs.Length + s.Indices.Length) * 4L
                    let gpuBytes = int64 (vertexCount * 32) + int64 (s.Indices.Length * 4)
                    submeshBytes <- submeshBytes + cpuBytes + gpuBytes
                    let material, ks, added = materialFor s.Material
                    textureAdded <- textureAdded + added
                    for k in ks do keys.Add k
                    { LoadedSubmesh.Positions = s.Positions
                      Normals = s.Normals
                      Uvs = s.Uvs
                      Indices = s.Indices
                      MaterialIndex = s.Material
                      Mesh = mesh
                      Material = material })
            let parts =
                if not includeParts then Array.empty else
                model.Parts
                |> Array.map (fun p ->
                    { EnvironmentPart.Name = p.Name
                      HierarchyPath = p.HierarchyPath
                      World = p.World
                      Bounds = submeshRangeBounds model.Submeshes p.SubmeshIndex p.SubmeshCount
                      SubmeshIndex = p.SubmeshIndex
                      SubmeshCount = p.SubmeshCount })
            // Flush and finish the whole asset before it is made resident.
            Gpu.finishUpload batch
            let unit =
                { EnvironmentLoaded.Submeshes = submeshes
                  Parts = parts
                  Bounds = model.Bounds
                  Materials = model.Materials
                  SubmeshBytes = submeshBytes
                  TextureKeys = keys.ToArray() }
            unit, textureAdded
        with _ ->
            do Gpu.abortUpload batch
            for mesh in created do StaticMesh.release mesh
            for key in keys do releaseTexture loader key |> ignore
            reraise ()
    // Per-asset instrumentation (FR-008/FR-010): one line per asset build, opt-in.
    if loadTiming then
        let ms (t: int64) = float t / float Stopwatch.Frequency * 1000.0
        let g = Gpu.uploadStats()
        let d = Texture.loadStats()
        do printfn "load %-12s total %7.1fms | parse %6.1f | buf-create %5.1fms x%d | tex-create %6.1fms x%d | stage %6.1fms x%d | memcpy %6.1fms x%d | submit %6.1fms x%d | decode %7.1fms x%d"
            (Path.GetFileNameWithoutExtension modelPath)
            (ms (Stopwatch.GetTimestamp() - totalStart))
            (ms parseTicks)
            (ms g.CreateNs) g.Creates
            (ms d.CreateNs) d.Decoded
            (ms g.StageNs) g.StageBuffers
            (ms g.MemcpyNs) g.Copies
            (ms g.SubmitNs) g.Submissions
            (ms d.DecodeNs) d.Decoded
    loaded, textureAdded

// The ids evictable this pass, in eviction order: the LRU order walked from the tail with the
// protected ids removed. Pure, so the policy (tail-first, the environment in use exempt) is
// asserted headlessly; the caller releases them and stops at the budget on the actual freed
// bytes.
let evictionCandidates (order: string list) (protectedIds: string list) : string list =
    [ for id in List.rev order do
        if not (List.contains id protectedIds) then yield id ]

// A fresh, unloaded environment asset: manifest metadata only, so startup reads no model file.
let private metadataOnly (id: string) (name: string) (modelPath: string) : EnvironmentAsset.EnvironmentAsset =
    { Id = id
      Name = name
      ModelPath = modelPath
      Orientation = Quaternion.Identity
      Loaded = None }

// ── residency ──

// Unload an asset: release its meshes and acquired textures (freeing each texture at refcount
// zero), subtract its bytes and drop the whole unit (its geometry becomes collectable), then
// remove it from the cache. A not-loaded asset is a no-op. Nothing of a zone is touched
// (spec 050 FR-006).
let releaseEnvironment (manager: AssetManager) (assetId: string) : unit =
    let asset = manager.Environments.[assetId]
    match asset.Loaded with
    | None -> ()
    | Some loaded ->

    for sub in loaded.Submeshes do StaticMesh.release sub.Mesh
    let mutable freed = 0L
    for key in loaded.TextureKeys do freed <- freed + releaseTexture manager.Loader.Value key
    manager.ResidentBytes <- manager.ResidentBytes - loaded.SubmeshBytes - freed
    asset.Loaded <- None
    do Lru.remove manager.Cache assetId

// Make an environment asset loaded: build its whole unit (parse + upload + acquire textures),
// insert it at the LRU head, then evict tails while over budget — skipping every id in
// `protectedIds` (the caller passes the asset being loaded, which is the environment becoming
// active, so the environment in use is never evicted). A cache hit only touches. Raises if the
// asset or its model file is missing or corrupt, before any state changes, so a failed load
// cancels cleanly.
let ensureEnvironmentResident (manager: AssetManager) (assetId: string) (protectedIds: string list) : unit =
    let asset = manager.Environments.[assetId]
    match asset.Loaded with
    | Some _ -> Lru.touch manager.Cache assetId
    | None ->
    let loaded, textureAdded = buildLoaded manager.Loader.Value asset.ModelPath true
    asset.Loaded <- Some loaded
    manager.ResidentBytes <- manager.ResidentBytes + loaded.SubmeshBytes + textureAdded
    do Lru.touch manager.Cache assetId
    // Evict LRU tails until within budget, using the bytes each release actually frees
    // (`releaseEnvironment` updates `ResidentBytes`), never a protected id. If only protected
    // environments remain the loop ends and the budget is allowed to be exceeded (FR-005).
    for victim in evictionCandidates (Lru.keys manager.Cache) protectedIds do
        if manager.ResidentBytes > ENVIRONMENT_BUDGET_BYTES then
            releaseEnvironment manager victim

let isEnvironmentResident (manager: AssetManager) (assetId: string) : bool =
    match manager.Environments.TryGetValue assetId with
    | true, asset -> asset.Loaded.IsSome
    | false, _ -> false

// Resident accessor by id (asserts presence; FR-011).
let submeshes (manager: AssetManager) (assetId: string) : EnvironmentAsset.LoadedSubmesh array =
    EnvironmentAsset.submeshes manager.Environments.[assetId]

// Scan all asset kinds into the manager. Actors and props load eagerly; environments are
// recorded from their manifests only, their models loaded on first use (FR-010).
let scan (root: string) (device: Gpu.Device) : AssetManager =
    // This laziness is a texture-cache detail, NOT environment residency. Environments are loaded
    // on demand through the `Loaded = None` state every `metadataOnly` asset starts in (see
    // `ensureEnvironmentResident`). This `Lazy` only defers the device-wide texture cache/white
    // placeholder, so a scan that loads no texture never touches the GPU. A real project has props,
    // whose eager upload below forces it at scan anyway, so in production it is effectively a no-op;
    // its only real effect is keeping device-free scans and tests off the GPU.
    let loader = lazy (createLoader device)

    let props = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    for (id, name, modelPath, _, _) in scanDir root "props" do
        if String.IsNullOrEmpty modelPath then
            failwithf "asset manager: prop asset '%s' has no model path" id
        let asset = metadataOnly id name (Path.Combine(root, modelPath))
        let loaded, _ = buildLoaded loader.Value asset.ModelPath false
        asset.Loaded <- Some loaded
        props.[id] <- asset

    let environments = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    for (id, name, modelPath, _, _) in scanDir root "environments" do
        if String.IsNullOrEmpty modelPath then
            failwithf "asset manager: environment asset '%s' has no model path" id
        environments.[id] <- metadataOnly id name (Path.Combine(root, modelPath))

    let actors = loadActors loader root

    { Device = device
      Root = root
      Actors = actors
      Props = props
      Environments = environments
      Loader = loader
      Cache = Lru.create ()
      ResidentBytes = 0L }

// Release the GPU resources the manager loaded: every loaded environment and prop, the textures
// (materials and portraits hold only references to them), each actor's mesh and morph storage
// buffer, and the placeholder.
let release (manager: AssetManager) : unit =
    let releaseAsset (asset: EnvironmentAsset.EnvironmentAsset) : unit =
        match asset.Loaded with
        | None -> ()
        | Some loaded ->

        for sub in loaded.Submeshes do StaticMesh.release sub.Mesh
        asset.Loaded <- None

    for KeyValue (_, asset) in manager.Environments do releaseAsset asset
    for KeyValue (_, asset) in manager.Props do releaseAsset asset
    // The loader is created on first use; a manager that never loaded a texture has none to free.
    if manager.Loader.IsValueCreated then
        for KeyValue (_, entry) in manager.Loader.Value.Textures do Texture.release entry.Texture
        do manager.Loader.Value.Textures.Clear()
        do Texture.release manager.Loader.Value.White
    for KeyValue (_, actor) in manager.Actors do
        do Mesh.release actor.Mesh
        match actor.Morphs with
        | None -> ()
        | Some morphs ->
        do Gpu.releaseBuffer actor.Mesh.Device morphs.Buffer

// Create the per-instance renderer for an actor asset id (the asset is already loaded at
// startup).
let createActorRender (manager: AssetManager) (assetId: string) : ActorRender.ActorRender =
    let asset = manager.Actors.[assetId]
    ActorRender.create manager.Device asset

// Create the per-instance renderer for a placed prop asset id (already loaded): the whole model,
// identity orientation.
let createPropRender (manager: AssetManager) (assetId: string) : EnvironmentRender.EnvironmentRender =
    let asset = manager.Props.[assetId]
    EnvironmentRender.create asset Quaternion.Identity 0 (EnvironmentAsset.submeshes asset).Length

// Create the per-instance renderer for one environment part: the part's submesh range, with the
// environment's authored orientation.
let createPartRender (manager: AssetManager) (environmentAssetId: string) (submeshIndex: int) (submeshCount: int) : EnvironmentRender.EnvironmentRender =
    let asset = manager.Environments.[environmentAssetId]
    EnvironmentRender.create asset asset.Orientation submeshIndex submeshCount
