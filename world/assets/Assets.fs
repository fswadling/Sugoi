module Assets

// Asset manager: scans the actors/environments/props subdirectories for s-syntax
// manifests and loads each asset into id-keyed collections — mirroring odin-game's
// `Asset_Manager`. Actors load via IQM; environments and props load via the scoped glTF
// parser into shared static submeshes.
//
// Environments are **loaded on demand** (spec 050) and **asynchronously** (spec 053): a request
// enqueues the asset, a worker reads/parses/decodes it off the render thread, and `pump` (once per
// frame, render thread) builds the GPU resources within a budget and installs the loaded unit. The
// whole loaded unit is held under a byte budget by an LRU over asset ids. Props and actors keep
// their eager, synchronous load; only environments are bounded and deferred.

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Numerics
open Prime
open EnvironmentAsset
open AssetLoad

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
      Portraits : ActorAsset.PortraitPaths
      Textures : string
      Sway : Sway.SwayChain array }

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
      Textures : Dictionary<TextureKey, TextureEntry> }

// The resumable render-side build cursor for one decoded payload (one asset). The batch is created
// lazily (only the active cursor holds one, so peak staging is one buffer — research D3), and
// `Created`/`Keys`/the byte counters track exactly what has been made so a failure can roll it back.
type PendingUpload =
    { AssetId : AssetId
      ModelPath : ModelPath
      Payload : DecodedEnvironment
      mutable Batch : Gpu.UploadBatch option
      Created : ResizeArray<StaticMesh.StaticMesh>
      Keys : ResizeArray<TextureKey>
      MaterialCache : Dictionary<int, Texture.Material * TextureKey list * int64>
      Submeshes : ResizeArray<LoadedSubmesh>
      mutable NextSubmesh : int
      mutable SubmeshBytes : int64
      mutable TextureAdded : int64 }

// Cross-screen asset manager, mirroring odin-game's `Asset_Manager`. Lives on
// `SharedState` so every screen reaches the same loaded assets and GPU device.
type AssetManager =
    { Device : Gpu.Device
      Root : string
      Actors : Dictionary<AssetId, ActorAsset.ActorAsset>
      // The actor asset each declared `Name` resolves to, built once at scan (`specs/074`). A portrait
      // line's speaker is a plain string, resolved through this on the per-frame draw path, so no
      // enumeration runs per frame. The lowest `Id` wins a shared name (a name is a label).
      ActorsByName : Dictionary<string, ActorAsset.ActorAsset>
      Props : Dictionary<AssetId, EnvironmentAsset.EnvironmentAsset>
      Environments : Dictionary<AssetId, EnvironmentAsset.EnvironmentAsset>
      // Audio (spec 086): the discovered audio assets, and the mixer context they load/probe through.
      // The context is released in `release`, after every resident sound has been freed (FR-010).
      AudioAssets : Dictionary<AssetId, AudioAsset.AudioAsset>
      Audio : Audio.Context
      // Created on first use, so a headless scan (no assets, or no GPU) never allocates a texture.
      Loader : Lazy<Loader>
      // Residency of environment assets: the LRU order (head = most recently used) and the
      // actual bytes those loaded environments hold (parsed geometry plus GPU data).
      Cache : Lru.Lru
      mutable ResidentBytes : int64
      // Async loading (spec 053): the worker pool (lazy), the in-progress upload cursors, and the
      // durable pin set (never dropped from the pending set, never evicted) that seeds the pool.
      mutable Pool : LoadPool option
      mutable Uploads : Dictionary<AssetId, PendingUpload>
      mutable PinnedIds : HashSet<AssetId>
      // Derived name lists, built on first use and dropped whenever the dictionaries change. The
      // dictionaries remain the source of truth; the lists are immutable, so sharing them is safe.
      mutable ActorNamesCache : string list option
      mutable MusicNamesCache : string list option
      mutable SoundAssetsCache : AudioAsset.AudioAsset list option }

// ── the residency budget ──

// Bound on environment residency. Bounds the *whole loaded unit* — parsed geometry plus GPU
// meshes and textures — so it caps process RAM as well as VRAM. Sized for the measured worst
// case: `ArtRoom` is ~1.68 GB of textures alone; its parsed geometry is ~2 MB.
[<Literal>]
let EnvironmentBudgetBytes : int64 = 2048L * 1024L * 1024L   // 2 GB

// The per-frame GPU upload budget (ms) the pump is allowed (research D3; FR-003).
[<Literal>]
let UploadBudgetMs : float = 4.0

// ── scanning ──

/// Run one asset load; a failure is reported and the asset skipped, so one bad file does not deny the
/// rest (spec 080, US7). `kind` names the report (an asset or an audio asset).
let private loadOrReport (kind: string) (name: string) (load: unit -> 'T) : 'T option =
    try Some (load ())
    with e ->
        do Diagnostics.reportf "%s '%s': %s (skipped)" kind name e.Message
        None

let private readManifest (path: string) : Manifest =
    let stem = Path.GetFileNameWithoutExtension(path)
    let manifest = File.ReadAllText(path) |> scvalue<Manifest>
    let id = if String.IsNullOrEmpty manifest.Id then stem else manifest.Id
    let name = if String.IsNullOrEmpty manifest.Name then id else manifest.Name
    { manifest with Id = id; Name = name }

let private scanDir (root: string) (dir: string) : Manifest array =
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
      Textures = Dictionary<TextureKey, TextureEntry>() }

// Acquire a texture by path: get or create its cache entry, bump the refcount, and return the
// texture with the bytes newly made resident (0 when it was already resident). On a cache miss the
// pixels come from `imageOf` — the decoded payload on the async path, or an inline decode for the
// eager prop/actor paths.
let private acquireTexture (loader: Loader) (batch: Gpu.UploadBatch) (imageOf: TextureKey -> Texture.DecodedImage) (path: TextureKey) : Texture.Texture * int64 =
    match loader.Textures.TryGetValue path with
    | true, entry ->
        entry.Refs <- entry.Refs + 1
        entry.Texture, 0L
    | false, _ ->
    let t = Texture.createFromDecoded batch loader.Device (imageOf path)
    loader.Textures.[path] <- { Texture = t; Refs = 1 }
    t, t.Bytes

// Release a texture: decrement its refcount and free it (returning the bytes freed) at zero.
let private releaseTexture (loader: Loader) (path: TextureKey) : int64 =
    match loader.Textures.TryGetValue path with
    | false, _ -> 0L
    | true, entry ->

    entry.Refs <- entry.Refs - 1
    if entry.Refs > 0 then 0L else
    do loader.Textures.Remove path |> ignore
    do Texture.release entry.Texture
    entry.Texture.Bytes

// The actor path: acquire a texture (decoding inline on a cache miss) and hold it for the process
// (actors are never evicted). It is not part of an asset build, so it runs in its own one-shot batch.
let private loadTexture (loader: Loader) (path: TextureKey) : Texture.Texture =
    let batch = Gpu.beginUpload loader.Device
    try
        let t, _ = acquireTexture loader batch Texture.decode path
        do Gpu.finishUpload batch
        t
    with _ ->
        // The batch owns staging buffers; release them if the load failed.
        do Gpu.abortUpload batch
        reraise ()

// Build a runtime PBR material from a parsed glTF material, resolving its texture URIs against
// the glTF file's directory. Returns the material, the texture keys it acquired (in order) and
// the bytes newly made resident, so an eviction can release exactly what a load acquired.
let private resolveMaterial (loader: Loader) (batch: Gpu.UploadBatch) (imageOf: TextureKey -> Texture.DecodedImage) (gltfDir: string) (m: Gltf.Material) : Texture.Material * TextureKey list * int64 =
    // Track each acquire as it happens so a later failure can undo the partial work; on success the
    // keys/bytes are handed back for the caller's own rollback and eventual release (FR-018).
    let acquired = ResizeArray<TextureKey>()
    let mutable added = 0L
    let tex (r: Gltf.TextureRef option) : Texture.Texture option =
        match r with
        | None -> None
        | Some rr ->

        let path = Path.Combine(gltfDir, rr.Uri)
        let t, delta = acquireTexture loader batch imageOf path
        do acquired.Add path
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

// Load one actor asset (parse + upload). Raises on a malformed or unsupported asset or a model exceeding
// the GPU bone palette; the caller reports and skips (spec 080, US7). The actor's IQM meshes each name an
// albedo texture (their `Material` string); these become per-submesh PBR materials sharing one combined
// vertex/index buffer. A failure after any resource was acquired releases it before raising, so a skipped
// asset leaves none (F-09).
let private loadActorAsset (loader: Lazy<Loader>) (root: string) (manifest: Manifest) : ActorAsset.ActorAsset =
    let id = manifest.Id
    if String.IsNullOrEmpty manifest.Model then
        failwithf "asset manager: actor asset '%s' has no model path" id

    let loader' = loader.Value
    let actorDir = Path.GetDirectoryName(Path.Combine(root, manifest.Model))
    // A declared texture root is root-relative, like the model and portrait paths; an absent one keeps
    // the model's own directory as the default (F-04).
    let declaredTextures =
        if String.IsNullOrEmpty manifest.Textures then ""
        else Path.Combine(root, manifest.Textures)
    let textureDir = ActorAsset.textureDirectory actorDir declaredTextures
    let fallback = Texture.defaultMaterial loader'.White (Vector4(0.85f, 0.75f, 0.65f, 1.0f))
    let model = Iqm.load (Path.Combine(root, manifest.Model))
    if model.Joints.Length > Gpu.BonesMax then
        failwithf "iqm: model has %d joints, exceeding BonesMax %d" model.Joints.Length Gpu.BonesMax

    let actorModel = Actor.createModel model
    let vertexCount = model.Positions.Length / 3
    let normals = Option.defaultWith (fun () -> Array.zeroCreate<float32> (vertexCount * 3)) model.Normals
    let uvs = Option.defaultWith (fun () -> Array.zeroCreate<float32> (vertexCount * 2)) model.Uvs
    let boneIds = Option.defaultWith (fun () -> Array.zeroCreate<byte> (vertexCount * 4)) model.BlendIndices
    let boneWeights = Option.defaultWith (fun () -> Array.zeroCreate<byte> (vertexCount * 4)) model.BlendWeights

    // ── everything below acquires resources; a failure rolls every acquisition back ──
    let acquiredTextures = ResizeArray<TextureKey>()
    let mutable createdMesh : Mesh.Mesh option = None
    let mutable createdMorphBuffer : Gpu.Buffer option = None
    let loadTracked (path: TextureKey) : Texture.Texture =
        let t = loadTexture loader' path
        do acquiredTextures.Add path
        t
    try

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
                else Some (loadTracked (Path.Combine(textureDir, mesh.Material)))
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
    createdMesh <- Some mesh

    // Morph targets, when the actor manifest names a sidecar: parse the manifest, read its
    // data file, and pack the offsets into the dense per-vertex layout the vertex shader
    // reads. Uploaded once and shared by every instance of the asset.
    let morphs =
        if String.IsNullOrEmpty manifest.Morphs then None else

        let manifestPath = Path.Combine(root, manifest.Morphs)
        let morphManifest = File.ReadAllText manifestPath |> ActorAsset.parseMorphManifest
        let dataPath = Path.Combine(Path.GetDirectoryName manifestPath, morphManifest.Data)
        let bytes = File.ReadAllBytes dataPath
        if bytes.Length % 4 <> 0 then
            failwithf "morph data '%s' is not a multiple of four bytes" dataPath
        else

        let data = Array.init (bytes.Length / 4) (fun i -> BitConverter.ToSingle(bytes, i * 4))
        let usable = ActorAsset.usableMorphs morphManifest
        if usable.Length = 0 then None else

        let deltas = ActorAsset.buildMorphDeltas usable data vertexCount
        let deltaBytes = Array.zeroCreate<byte> (deltas.Length * 4)
        do Buffer.BlockCopy(deltas, 0, deltaBytes, 0, deltaBytes.Length)
        let buffer = Gpu.createStorageBuffer loader'.Device deltaBytes
        createdMorphBuffer <- Some buffer
        let actorMorphs : ActorAsset.ActorMorphs =
            { Names = usable |> Array.map (fun m -> m.Name)
              Count = usable.Length
              Buffer = buffer }
        Some actorMorphs

    // Portraits: a declared set must be complete (a partial set is a broken asset, FR-006);
    // declaring none is legal and simply means the character cannot speak a portrait line.
    // Loaded once through the shared texture cache, so nothing is re-read per line (FR-010).
    let portraits =
        let declared = ActorAsset.portraitValues manifest.Portraits
        let isUnset (_, path: string) = String.IsNullOrEmpty path
        if declared |> List.forall isUnset then None else

        do ActorAsset.requireComplete manifest.Portraits
        let loadPortrait (path: string) = loadTracked (Path.Combine(root, path))
        let loaded : ActorAsset.ActorPortraits =
            { Neutral = loadPortrait manifest.Portraits.Neutral
              Happy = loadPortrait manifest.Portraits.Happy
              Sad = loadPortrait manifest.Portraits.Sad
              Angry = loadPortrait manifest.Portraits.Angry
              Surprised = loadPortrait manifest.Portraits.Surprised }
        Some loaded

    // Sway chains (spec 091): `Sway.resolveAll` holds the policy and words the failures; the actor's
    // id is here, so the reporting is here. A chain that cannot resolve is dropped, never the
    // character (FR-009, FR-012).
    let swayChains =
        let struct (defs, messages) = Sway.resolveAll model manifest.Sway
        for message in messages do
            do Diagnostics.reportf "asset '%s': %s" id message
        defs

    { Id = id
      Name = manifest.Name
      ModelPath = manifest.Model
      Bounds = actorBounds
      Mesh = mesh
      Model = actorModel
      Morphs = morphs
      Portraits = portraits
      SwayChains = swayChains }

    with _ ->
        match createdMorphBuffer with
        | Some buffer -> do Gpu.releaseBuffer loader'.Device buffer
        | None -> ()
        match createdMesh with
        | Some mesh -> do Mesh.release mesh
        | None -> ()
        for key in acquiredTextures do do releaseTexture loader' key |> ignore
        reraise ()

// Scan the actors directory, loading each asset through `loadActorAsset`; a failure is reported and the asset
// skipped, so one bad file does not deny the rest (spec 080, US7).
let private loadActors (loader: Lazy<Loader>) (root: string) : Dictionary<AssetId, ActorAsset.ActorAsset> =
    let actors = Dictionary<AssetId, ActorAsset.ActorAsset>()
    for manifest in scanDir root "actors" do
        match loadOrReport "asset" manifest.Id (fun () -> loadActorAsset loader root manifest) with
        | Some asset -> actors.[asset.Id] <- asset
        | None -> ()
    actors

// The name index (`specs/074`): each declared `Name` to the actor asset that answers it. Built once, so a
// portrait line's speaker resolves on the per-frame draw path without enumerating. Sorted by `Id`, so a
// name two assets share resolves to the lowest `Id` — deterministic regardless of scan order. Public so
// `Assets.scan` and the headless fixture share one rule.
let indexByName (actors: Dictionary<AssetId, ActorAsset.ActorAsset>) : Dictionary<string, ActorAsset.ActorAsset> =
    let byName = Dictionary<string, ActorAsset.ActorAsset>()
    for asset in actors.Values |> Seq.sortBy (fun a -> a.Id) do
        if not (byName.ContainsKey asset.Name) then byName.[asset.Name] <- asset
    byName

/// The discovered actor names, sorted and de-duplicated — what a speaker chooser offers (`specs/074`).
let actorNames (manager: AssetManager) : string list =
    match manager.ActorNamesCache with
    | Some names -> names
    | None ->

    let names = manager.Actors.Values |> Seq.map (fun a -> a.Name) |> Seq.distinct |> Seq.sort |> List.ofSeq
    manager.ActorNamesCache <- Some names
    names

/// Drop the derived name lists after the dictionaries change. The next accessor rebuilds once.
let invalidateNameCaches (manager: AssetManager) : unit =
    manager.ActorNamesCache <- None
    manager.MusicNamesCache <- None
    manager.SoundAssetsCache <- None

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

// The pixels for a texture path: the payload's decoded image, or a loud failure if the worker did
// not produce it (a bug, since `decodeEnvironment` decodes every path any material references).
let private imageProvider (payload: DecodedEnvironment) (path: TextureKey) : Texture.DecodedImage =
    match payload.Images.TryGetValue path with
    | true, image -> image
    | false, _ -> failwithf "environment '%s': decoded image for '%s' is missing" payload.AssetId path

// Resolve the material at `index` once per cursor (`MaterialCache`), so a material shared by several
// submeshes is resolved — and its textures acquired — once (spec 050 FR-018).
let private materialFor (loader: Loader) (cursor: PendingUpload) (batch: Gpu.UploadBatch) (imageOf: TextureKey -> Texture.DecodedImage) (gltfDir: string) (index: int) : Texture.Material * TextureKey list * int64 =
    match cursor.MaterialCache.TryGetValue index with
    | true, (material, _, _) -> material, [], 0L
    | false, _ ->
    let material, keys, added =
        if index < 0 || index >= cursor.Payload.Model.Materials.Length then loader.DefaultMaterial, [], 0L
        else resolveMaterial loader batch imageOf gltfDir cursor.Payload.Model.Materials.[index]
    cursor.MaterialCache.[index] <- (material, keys, added)
    material, keys, added

// Advance a cursor: build the GPU mesh for the next submesh and resolve its material, until the
// whole asset is built or the deadline passes. Returns the loaded unit when complete (`finishUpload`
// has then run), else None so the caller keeps the cursor for the next frame.
let private advanceCursor (loader: Loader) (device: Gpu.Device) (cursor: PendingUpload) (includeParts: bool) (deadline: int64) : EnvironmentLoaded option =
    let model = cursor.Payload.Model
    let gltfDir = Path.GetDirectoryName cursor.ModelPath
    let imageOf = imageProvider cursor.Payload
    let batch =
        match cursor.Batch with
        | Some b -> b
        | None ->
        let b = Gpu.beginUpload device
        cursor.Batch <- Some b
        b
    while cursor.NextSubmesh < model.Submeshes.Length && Stopwatch.GetTimestamp() < deadline do
        let s = model.Submeshes.[cursor.NextSubmesh]
        let mesh = StaticMesh.create batch device s.Positions s.Normals s.Uvs s.Indices
        do cursor.Created.Add mesh
        let vertexCount = s.Positions.Length / 3
        // The unit's bytes: the retained geometry (RAM) plus the uploaded buffers (VRAM).
        let cpuBytes = int64 (s.Positions.Length + s.Normals.Length + s.Uvs.Length + s.Indices.Length) * 4L
        let gpuBytes = int64 (vertexCount * 32) + int64 (s.Indices.Length * 4)
        cursor.SubmeshBytes <- cursor.SubmeshBytes + cpuBytes + gpuBytes
        let material, ks, added = materialFor loader cursor batch imageOf gltfDir s.Material
        cursor.TextureAdded <- cursor.TextureAdded + added
        for k in ks do cursor.Keys.Add k
        do cursor.Submeshes.Add
            { LoadedSubmesh.Positions = s.Positions
              Normals = s.Normals
              Uvs = s.Uvs
              Indices = s.Indices
              MaterialIndex = s.Material
              Mesh = mesh
              Material = material }
        cursor.NextSubmesh <- cursor.NextSubmesh + 1
    if cursor.NextSubmesh < model.Submeshes.Length then None else

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
    Gpu.finishUpload batch
    Some
        { EnvironmentLoaded.Submeshes = cursor.Submeshes.ToArray()
          Parts = parts
          Bounds = model.Bounds
          Materials = model.Materials
          SubmeshBytes = cursor.SubmeshBytes
          TextureKeys = cursor.Keys.ToArray() }

let private newCursor (assetId: AssetId) (modelPath: ModelPath) (payload: DecodedEnvironment) : PendingUpload =
    if loadTiming then
        do Gpu.resetUploadStats()
        do Texture.resetLoadStats()
    { AssetId = assetId
      ModelPath = modelPath
      Payload = payload
      Batch = None
      Created = ResizeArray()
      Keys = ResizeArray()
      MaterialCache = Dictionary()
      Submeshes = ResizeArray()
      NextSubmesh = 0
      SubmeshBytes = 0L
      TextureAdded = 0L }

// The per-asset load line (opt-in via `FFS_LOAD_TIMING=1`): the worker's queue-wait/parse/decode and
// the render-side creation/staging/copy/submit. The GPU figures come from the process-global upload
// stats reset when the cursor was created, so a concurrent per-frame upload can inflate them — a
// diagnostic, not a benchmark.
let private printLoad (name: string) (payload: DecodedEnvironment) : unit =
    if loadTiming then
        let ms (t: int64) = float t / float Stopwatch.Frequency * 1000.0
        let t = Texture.loadStats()
        let g = Gpu.uploadStats()
        do printfn "load %-12s | queue-wait %6.1f | parse %6.1f | decode %7.1f | tex-create %6.1fms x%d | stage %6.1fms x%d | memcpy %6.1fms x%d | submit %6.1fms x%d"
            name (ms payload.QueueNs) (ms payload.ParseNs) (ms payload.DecodeNs)
            (ms t.CreateNs) t.Creates (ms g.StageNs) g.StageBuffers (ms g.MemcpyNs) g.Copies (ms g.SubmitNs) g.Submissions

// Roll a cursor back: abort its batch and release everything it made. Used on failure and on a drop.
let private releaseCursor (loader: Loader) (cursor: PendingUpload) : unit =
    match cursor.Batch with
    | Some batch -> do Gpu.abortUpload batch
    | None -> ()
    cursor.Batch <- None
    for mesh in cursor.Created do StaticMesh.release mesh
    for key in cursor.Keys do releaseTexture loader key |> ignore
    do cursor.Created.Clear()
    do cursor.Keys.Clear()

// The eager (props/actors) build: decode on the render thread and advance the cursor to completion.
// A failure part-way rolls the cursor back before raising, so a skipped asset leaves no mesh, texture
// or staging buffer behind (F-09).
let private buildSync (loader: Loader) (device: Gpu.Device) (modelPath: ModelPath) (includeParts: bool) : EnvironmentLoaded * int64 =
    match AssetLoad.decodeEnvironment "" modelPath with
    | LoadFailed (_, msg) -> failwithf "asset '%s': %s" modelPath msg
    | Decoded payload ->
    let cursor = newCursor "" modelPath payload
    try

    match advanceCursor loader device cursor includeParts Int64.MaxValue with
    | None -> failwithf "asset '%s': the build did not complete" modelPath
    | Some loaded ->
    do printLoad (Path.GetFileNameWithoutExtension modelPath) payload
    loaded, cursor.TextureAdded

    with _ ->
        do releaseCursor loader cursor
        reraise ()


// The ids evictable this pass, in eviction order: the LRU order walked from the tail with the
// protected ids removed. Pure, so the policy (tail-first, the environment in use exempt) is
// asserted headlessly; the caller releases them and stops at the budget on the actual freed bytes.
let evictionCandidates (order: AssetId list) (protectedIds: AssetId list) : AssetId list =
    [ for id in List.rev order do
        if not (List.contains id protectedIds) then yield id ]

// A fresh, unloaded environment asset: manifest metadata only, so startup reads no model file.
let private metadataOnly (id: string) (name: string) (modelPath: string) : EnvironmentAsset.EnvironmentAsset =
    { Id = id
      Name = name
      ModelPath = modelPath
      Orientation = Quaternion.Identity
      Residency = NotLoaded }

// ── residency ──

// Unload an asset: release its meshes and acquired textures (freeing each texture at refcount
// zero), subtract its bytes and drop the whole unit (its geometry becomes collectable), then
// remove it from the cache. A not-loaded asset is a no-op.
let releaseEnvironment (manager: AssetManager) (assetId: AssetId) : unit =
    let asset = manager.Environments.[assetId]
    match asset.Residency with
    | NotLoaded | Loading | Failed _ -> ()
    | Resident loaded ->

    for sub in loaded.Submeshes do StaticMesh.release sub.Mesh
    let mutable freed = 0L
    for key in loaded.TextureKeys do freed <- freed + releaseTexture manager.Loader.Value key
    manager.ResidentBytes <- manager.ResidentBytes - loaded.SubmeshBytes - freed
    asset.Residency <- NotLoaded
    do Lru.remove manager.Cache assetId


let private ensurePool (manager: AssetManager) : LoadPool =
    match manager.Pool with
    | Some pool -> pool
    | None ->
    let pool = AssetLoad.createPool AssetLoad.loadWorkers AssetLoad.LoadQueueCapacity AssetLoad.decodeEnvironment
    do AssetLoad.setPinned pool.Queue (manager.PinnedIds |> Seq.toList)
    manager.Pool <- Some pool
    pool

// Request that an environment become resident. De-duplicated and bounded; returns immediately — never
// loads on the calling thread (FR-001/FR-004). The pin set is owned by the gameplay layer
// (`setPinned`, narrowed to the active environment every frame), so a request never adds to it.
let requestEnvironment (manager: AssetManager) (assetId: AssetId) : unit =
    let asset = manager.Environments.[assetId]
    match asset.Residency with
    | Resident _ -> Lru.touch manager.Cache assetId
    | _ ->
    let pool = ensurePool manager
    let dropped = AssetLoad.request pool.Queue assetId asset.ModelPath
    asset.Residency <- Loading
    // A dropped request may never start (or its payload is discarded); its residency must not stay
    // `Loading` (VR-011).
    for droppedId in dropped do
        match manager.Environments.TryGetValue droppedId with
        | true, a when a.Residency = Loading -> a.Residency <- NotLoaded
        | _ -> ()

// Replace the pin set (the durable home is the manager; the pool mirrors it). An unchanged set is a
// no-op, so the per-update caller neither allocates nor takes the pool's lock (F-15).
let setPinned (manager: AssetManager) (pinnedIds: AssetId list) : unit =
    let unchanged =
        manager.PinnedIds.Count = List.length pinnedIds
        && List.forall (fun id -> manager.PinnedIds.Contains id) pinnedIds
    if unchanged then () else

    do manager.PinnedIds.Clear()
    for p in pinnedIds do manager.PinnedIds.Add p |> ignore
    match manager.Pool with
    | None -> ()
    | Some pool ->
    do AssetLoad.setPinned pool.Queue pinnedIds


// Install a completed build: mark it resident, account its bytes, touch the LRU, leave the pending
// set, then evict LRU tails over budget using the bytes each release actually frees.
let private settle (manager: AssetManager) (pool: LoadPool) (assetId: AssetId) (loaded: EnvironmentLoaded) (textureAdded: int64) : unit =
    let asset = manager.Environments.[assetId]
    asset.Residency <- Resident loaded
    manager.ResidentBytes <- manager.ResidentBytes + loaded.SubmeshBytes + textureAdded
    do Lru.touch manager.Cache assetId
    do AssetLoad.endUpload pool.Queue assetId
    do AssetLoad.makeResident pool.Queue assetId
    do manager.Uploads.Remove assetId |> ignore
    do AssetLoad.releaseSlot pool   // the cursor is done: its payload is now resident
    let pins = manager.PinnedIds |> Seq.toList
    for victim in evictionCandidates (Lru.keys manager.Cache) pins do
        if manager.ResidentBytes > EnvironmentBudgetBytes then releaseEnvironment manager victim

let private resetIfLoading (manager: AssetManager) (assetId: AssetId) : unit =
    match manager.Environments.TryGetValue assetId with
    | true, asset when asset.Residency = Loading -> asset.Residency <- NotLoaded
    | _ -> ()

let private failAsset (manager: AssetManager) (assetId: AssetId) (message: string) : unit =
    match manager.Environments.TryGetValue assetId with
    | true, asset -> asset.Residency <- Failed message
    | false, _ -> ()

// Render thread, once per frame. Drain decoded payloads, advance the in-progress builds within the
// budget (sequentially), and settle what lands. Discards payloads for assets no longer wanted.
let pump (manager: AssetManager) (budgetMs: float) : unit =
    match manager.Pool with
    | None -> ()
    | Some pool ->

    let mutable draining = true
    while draining do
        match AssetLoad.tryTakeReady pool with
        | None -> draining <- false
        | Some outcome ->
        match outcome with
        | LoadFailed (assetId, message) ->
            if AssetLoad.isPending pool.Queue assetId then
                do Diagnostics.reportf "environment load failed: %s: %s" assetId message
                do failAsset manager assetId message
            // The claim is released here (the id leaves the wanted set whether it failed or was
            // dropped). `makeResident` clears the claim, `Pending` and `Uploading`.
            do AssetLoad.makeResident pool.Queue assetId
            do AssetLoad.releaseSlot pool   // the decode's slot is no longer needed

        | Decoded payload ->

        if AssetLoad.isPending pool.Queue payload.AssetId then
            if manager.Uploads.ContainsKey payload.AssetId then
                // Defensive: a second payload for an id already uploading. The claim is held
                // from claim until resident, so this cannot normally happen; discard it rather
                // than overwrite (and leak) the cursor in progress.
                do Diagnostics.reportf "environment load: duplicate payload for '%s' discarded" payload.AssetId
                do AssetLoad.releaseSlot pool
            else
                // The payload becomes a cursor, which holds its slot until it settles; the
                // claim is held through the upload, so this id is never decoded a second time.
                manager.Uploads.[payload.AssetId] <- newCursor payload.AssetId manager.Environments.[payload.AssetId].ModelPath payload
        else
            // No longer wanted: discard the decoded payload, release the claim and the slot.
            do resetIfLoading manager payload.AssetId
            do AssetLoad.makeResident pool.Queue payload.AssetId
            do AssetLoad.releaseSlot pool

    if manager.Uploads.Count > 0 then
        let deadline = Stopwatch.GetTimestamp() + int64 (budgetMs / 1000.0 * float Stopwatch.Frequency)
        // Advance any cursor already holding a batch first, so one staging buffer at a time is
        // enforced regardless of dictionary enumeration order (VR-008).
        let ids =
            manager.Uploads
            |> Seq.map (fun kv -> kv.Key, (match kv.Value.Batch with Some _ -> 0 | None -> 1))
            |> Seq.sortBy snd
            |> Seq.map fst
            |> Seq.toArray
        for assetId in ids do
            if manager.Uploads.ContainsKey assetId then
                if not (AssetLoad.isPending pool.Queue assetId) then
                    // Dropped while queued/decoding: discard the cursor, releasing what it made.
                    do releaseCursor manager.Loader.Value manager.Uploads.[assetId]
                    do manager.Uploads.Remove assetId |> ignore
                    do AssetLoad.makeResident pool.Queue assetId
                    do AssetLoad.releaseSlot pool
                    do resetIfLoading manager assetId
                elif Stopwatch.GetTimestamp() < deadline then
                    do AssetLoad.beginUpload pool.Queue assetId
                    let cursor = manager.Uploads.[assetId]
                    try
                        match advanceCursor manager.Loader.Value manager.Device cursor true deadline with
                        | None -> ()
                        | Some loaded ->
                        do settle manager pool assetId loaded cursor.TextureAdded
                        do printLoad (Path.GetFileNameWithoutExtension cursor.ModelPath) cursor.Payload

                    with e ->
                        do releaseCursor manager.Loader.Value cursor
                        do manager.Uploads.Remove assetId |> ignore
                        do AssetLoad.makeResident pool.Queue assetId
                        do AssetLoad.releaseSlot pool
                        do failAsset manager assetId e.Message

let isEnvironmentResident (manager: AssetManager) (assetId: AssetId) : bool =
    match manager.Environments.TryGetValue assetId with
    | true, asset -> EnvironmentAsset.isResident asset
    | false, _ -> false

// The readout predicates (FR-007/FR-008). Allocation-free: they read `Residency` only.
let isLoading (manager: AssetManager) (assetId: AssetId) : bool =
    match manager.Environments.TryGetValue assetId with
    | true, asset -> (match asset.Residency with Loading -> true | _ -> false)
    | false, _ -> false

let hasFailed (manager: AssetManager) (assetId: AssetId) : bool =
    match manager.Environments.TryGetValue assetId with
    | true, asset -> (match asset.Residency with Failed _ -> true | _ -> false)
    | false, _ -> false

// ── audio (spec 086) ──

// The opt-in per-asset line (`FFS_AUDIO_DEBUG=1`), mirroring `FFS_LOAD_TIMING` (spec 053).
let private audioDebug =
    match Environment.GetEnvironmentVariable "FFS_AUDIO_DEBUG" with
    | null -> false
    | v -> v <> "" && v <> "0"

let private audioManifestPaths (root: string) : string array =
    let dir = Path.Combine(root, "audio")
    if not (Directory.Exists dir) then Array.empty
    else Directory.EnumerateFiles(dir, "*.manifest", SearchOption.AllDirectories) |> Seq.toArray

// Load or probe one audio asset from its manifest. A malformed manifest, a missing source file or an
// unknown class is an **authoring error** and raises, so the caller reports and skips (the asset is
// not added). A source that cannot be decoded is a runtime failure: it is caught here, reported, and
// the asset is built `Failed` so it still appears (FR-008).
let private loadAudioAsset (audio: Audio.Context) (root: string) (manifestPath: string) : AudioAsset.AudioAsset =
    let stem = Path.GetFileNameWithoutExtension manifestPath
    let manifest = File.ReadAllText manifestPath |> scvalue<AudioAsset.Manifest>
    let id = if String.IsNullOrEmpty manifest.Id then stem else manifest.Id
    let name = if String.IsNullOrEmpty manifest.Name then id else manifest.Name
    if String.IsNullOrEmpty manifest.File then
        failwithf "audio asset '%s' names no source file" id
    let classOf =
        match manifest.Kind with
        | "Music" -> AudioAsset.Music
        | "Sound" -> AudioAsset.Sound
        | other -> failwithf "audio asset '%s' has an invalid class '%s' (expected Music or Sound)" id other
    let sourcePath = Path.GetFullPath(Path.Combine(root, manifest.File))
    let residency =
        try
            match classOf with
            | AudioAsset.Sound -> AudioAsset.Resident (audio.LoadSound sourcePath)
            | AudioAsset.Music ->
                do audio.Probe sourcePath
                AudioAsset.Reference
        with e ->
            do Diagnostics.reportf "audio asset '%s': %s" id e.Message
            AudioAsset.Failed e.Message
    { Id = id; Name = name; Path = sourcePath; Class = classOf; Residency = residency }

// One opt-in line per audio asset: id, class, resulting residency.
let private reportAudio (asset: AudioAsset.AudioAsset) : unit =
    let classText = match asset.Class with AudioAsset.Music -> "Music" | AudioAsset.Sound -> "Sound"
    let residencyText =
        match asset.Residency with
        | AudioAsset.Resident _ -> "resident"
        | AudioAsset.Reference -> "reference"
        | AudioAsset.Failed message -> "failed: " + message
    do printfn "audio %-14s | %-5s | %s" asset.Id classText residencyText

// ── audio readouts (spec 086) ──

/// The audio asset for an id (asserts presence).
let audioAsset (manager: AssetManager) (assetId: AssetId) : AudioAsset.AudioAsset =
    manager.AudioAssets.[assetId]

/// Whether the asset's sound is decoded into memory (`Resident`). Absent ids are false.
let isAudioResident (manager: AssetManager) (assetId: AssetId) : bool =
    match manager.AudioAssets.TryGetValue assetId with
    | true, asset -> AudioAsset.isResident asset
    | false, _ -> false

/// Whether the asset's load/probe failed.
let hasAudioFailed (manager: AssetManager) (assetId: AssetId) : bool =
    match manager.AudioAssets.TryGetValue assetId with
    | true, asset -> (match asset.Residency with AudioAsset.Failed _ -> true | _ -> false)
    | false, _ -> false

/// The discovered music assets' ids, sorted — the song panel's picker (spec 087): `Kind = Music`
/// (spec 086). A failed music asset is still listed: it is a row the author may fix later.
let musicNames (manager: AssetManager) : string list =
    match manager.MusicNamesCache with
    | Some names -> names
    | None ->

    let names =
        [ for KeyValue (id, asset) in manager.AudioAssets do if asset.Class = AudioAsset.Music then id ]
        |> List.sort
    manager.MusicNamesCache <- Some names
    names

/// The discovered sound assets (spec 089), sorted by name — the sound panel's rows: `Kind = Sound`
/// (spec 086). Each carries its id (to play), its name (to show) and its residency (resident =
/// playable, failed = listed and marked). A failed sound is still listed: it is a row the author may
/// fix later.
let soundAssets (manager: AssetManager) : AudioAsset.AudioAsset list =
    match manager.SoundAssetsCache with
    | Some assets -> assets
    | None ->

    let assets =
        [ for KeyValue (_, asset) in manager.AudioAssets do if asset.Class = AudioAsset.Sound then asset ]
        |> List.sortBy (fun asset -> asset.Name)
    manager.SoundAssetsCache <- Some assets
    assets

// Scan all asset kinds into the manager. Actors and props load eagerly; environments are recorded
// from their manifests only, their models loaded on request (spec 053); audio loads eagerly (sounds
// into memory, music as a probed reference, spec 086).
let scan (root: string) (device: Gpu.Device) (audio: Audio.Context) : AssetManager =
    let loader = lazy (createLoader device)

    let props = Dictionary<AssetId, EnvironmentAsset.EnvironmentAsset>()
    for manifest in scanDir root "props" do
        let loaded =
            loadOrReport "asset" manifest.Id (fun () ->
                if String.IsNullOrEmpty manifest.Model then
                    failwithf "asset manager: prop asset '%s' has no model path" manifest.Id
                let asset = metadataOnly manifest.Id manifest.Name (Path.Combine(root, manifest.Model))
                let loaded, _ = buildSync loader.Value device asset.ModelPath false
                asset.Residency <- Resident loaded
                asset)
        match loaded with
        | Some asset -> props.[asset.Id] <- asset
        | None -> ()

    let environments = Dictionary<AssetId, EnvironmentAsset.EnvironmentAsset>()
    for manifest in scanDir root "environments" do
        let loaded =
            loadOrReport "asset" manifest.Id (fun () ->
                if String.IsNullOrEmpty manifest.Model then
                    failwithf "asset manager: environment asset '%s' has no model path" manifest.Id
                metadataOnly manifest.Id manifest.Name (Path.Combine(root, manifest.Model)))
        match loaded with
        | Some asset -> environments.[asset.Id] <- asset
        | None -> ()

    let actors = loadActors loader root
    let actorsByName = indexByName actors

    // Audio (spec 086): sounds decode into memory, music is probed and kept as a path. A malformed
    // manifest or an authoring error is reported and skipped; a decode failure marks the asset failed.
    let audioAssets = Dictionary<AssetId, AudioAsset.AudioAsset>()
    for manifestPath in audioManifestPaths root do
        let name = Path.GetFileNameWithoutExtension manifestPath
        match loadOrReport "audio asset" name (fun () -> loadAudioAsset audio root manifestPath) with
        | Some asset ->
            audioAssets.[asset.Id] <- asset
            if audioDebug then do reportAudio asset
        | None -> ()

    { Device = device
      Root = root
      Actors = actors
      ActorsByName = actorsByName
      Props = props
      Environments = environments
      AudioAssets = audioAssets
      Audio = audio
      Loader = loader
      Cache = Lru.create ()
      ResidentBytes = 0L
      Pool = None
      Uploads = Dictionary<AssetId, PendingUpload>()
      PinnedIds = HashSet<AssetId>()
      ActorNamesCache = None
      MusicNamesCache = None
      SoundAssetsCache = None }

// Release the GPU resources the manager loaded. The pool is stopped first (FR-011), so no worker is
// live while the device is torn down; an in-flight cursor is rolled back with the rest.
let release (manager: AssetManager) : unit =
    match manager.Pool with
    | Some pool ->
        do AssetLoad.stopPool pool
        manager.Pool <- None
    | None -> ()
    for KeyValue (_, cursor) in manager.Uploads do releaseCursor manager.Loader.Value cursor
    do manager.Uploads.Clear()
    do invalidateNameCaches manager

    let releaseAsset (asset: EnvironmentAsset.EnvironmentAsset) : unit =
        match asset.Residency with
        | Resident loaded ->
            for sub in loaded.Submeshes do StaticMesh.release sub.Mesh
            asset.Residency <- NotLoaded
        | NotLoaded | Loading | Failed _ -> ()

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

    // Audio (spec 086): free every resident sound before releasing the mixer it belongs to (FR-010).
    for KeyValue (_, asset) in manager.AudioAssets do
        match asset.Residency with
        | AudioAsset.Resident handle -> manager.Audio.FreeSound handle
        | AudioAsset.Reference | AudioAsset.Failed _ -> ()
    do manager.AudioAssets.Clear()
    do Audio.release manager.Audio

// Create the per-instance renderer for an actor asset id (the asset is already loaded at
// startup).
let createActorRender (manager: AssetManager) (assetId: AssetId) : ActorRender.ActorRender =
    let asset = manager.Actors.[assetId]
    ActorRender.create manager.Device asset

// Create the per-instance renderer for a placed prop asset id (already loaded): the whole model,
// identity orientation.
let createPropRender (manager: AssetManager) (assetId: AssetId) : EnvironmentRender.EnvironmentRender =
    let asset = manager.Props.[assetId]
    EnvironmentRender.create asset Quaternion.Identity 0 (EnvironmentAsset.submeshes asset).Length

// Create the per-instance renderer for one environment part: the part's submesh range, with the
// environment's authored orientation.
let createPartRender (manager: AssetManager) (environmentAssetId: AssetId) (submeshIndex: int) (submeshCount: int) : EnvironmentRender.EnvironmentRender =
    let asset = manager.Environments.[environmentAssetId]
    EnvironmentRender.create asset asset.Orientation submeshIndex submeshCount
