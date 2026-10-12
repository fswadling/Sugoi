module Gltf

// Scoped glTF 2.0 parser for static environments/props, ported from
// ocaml-game/lib/core/gltf.ml. Supports external `.bin` buffers, buffer views,
// accessors (float32 POSITION/NORMAL/TEXCOORD_0, u8/u16/u32 indices), materials
// (per-primitive baseColorFactor), and node transforms kept as per-node parts
// (local-space geometry + a world transform per part, so each node can become an
// independently-transformable prop). Anything else raises (constitution II).
// Emits flat struct-of-arrays (float32[]/int[]) that feed the static GPU buffers
// directly.

open System
open System.IO
open System.Numerics
open System.Buffers.Binary
open System.Text.Json

type Bounds = { Min : Vector3; Max : Vector3 }

type TextureRef = { Uri : string }

type Material =
    { BaseColor : Vector4
      Metallic : float32
      Roughness : float32
      Emissive : Vector3
      AlbedoTexture : TextureRef option
      MetallicRoughnessTexture : TextureRef option
      NormalTexture : TextureRef option
      EmissiveTexture : TextureRef option }

type Submesh =
    { Name : string
      Material : int
      Positions : float32 array
      Normals : float32 array
      Uvs : float32 array
      Indices : int array }

type Part =
    { Name : string
      HierarchyPath : string
      World : Matrix4x4
      SubmeshIndex : int
      SubmeshCount : int }

type Model =
    { Submeshes : Submesh array
      Parts : Part array
      Materials : Material array
      Bounds : Bounds }

// ── glTF component-type / accessor tables ──

let private ctByte = 5120
let private ctUByte = 5121
let private ctUShort = 5123
let private ctUInt = 5125
let private ctFloat = 5126

let private componentSize ct =
    match ct with
    | 5120 | 5121 -> 1
    | 5122 | 5123 -> 2
    | 5125 | 5126 -> 4
    | _ -> failwithf "gltf: unsupported componentType %d" ct

let private typeComponents t =
    match t with
    | "SCALAR" -> 1
    | "VEC2" -> 2
    | "VEC3" -> 3
    | "VEC4" -> 4
    | _ -> failwithf "gltf: unsupported accessor type %s" t

// ── little-endian binary reads ──

let private f32At (buf: byte array) (off: int) : float32 =
    BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(ReadOnlySpan<byte>(buf, off, 4)))

let private indexAt (buf: byte array) (ct: int) (off: int) : int =
    match ct with
    | 5121 -> int buf.[off]
    | 5123 -> int (BinaryPrimitives.ReadUInt16LittleEndian(ReadOnlySpan<byte>(buf, off, 2)))
    | 5125 -> int (BinaryPrimitives.ReadUInt32LittleEndian(ReadOnlySpan<byte>(buf, off, 4)))
    | _ -> failwithf "gltf: unsupported index componentType %d" ct

// ── JSON helpers ──

let private optArray (e: JsonElement) (name: string) : JsonElement array =
    match e.TryGetProperty(name) with
    | true, v -> v.EnumerateArray() |> Seq.toArray
    | false, _ -> [||]

let private req (e: JsonElement) (name: string) : JsonElement =
    match e.TryGetProperty(name) with
    | true, v -> v
    | false, _ -> failwithf "gltf: missing '%s'" name

let private reqInt (e: JsonElement) (name: string) : int =
    (req e name).GetInt32()

let private optInt (e: JsonElement) (name: string) : int option =
    match e.TryGetProperty(name) with
    | true, v -> Some (v.GetInt32())
    | false, _ -> None

let private optString (e: JsonElement) (name: string) : string =
    match e.TryGetProperty(name) with
    | true, v -> v.GetString()
    | false, _ -> ""

// ── parsed descriptor records ──

type private BufferView = { Buffer : int; ByteOffset : int; ByteStride : int option }

type private Accessor =
    { BufferView : int
      ByteOffset : int
      ComponentType : int
      Count : int
      Components : int }

type private Node =
    { Name : string
      Children : int array
      Translation : Vector3
      Rotation : Quaternion
      Scale : Vector3
      Mesh : int option }

// ── attribute/index extraction ──

let private readFloats (buf: byte array) (bv: BufferView) (acc: Accessor) : float32 array =
    let stride = match bv.ByteStride with Some s -> s | None -> acc.Components * 4
    Array.init (acc.Count * acc.Components) (fun i ->
        let elem = i / acc.Components
        let comp = i % acc.Components
        f32At buf (bv.ByteOffset + acc.ByteOffset + elem * stride + comp * 4))

let private readIndices (buf: byte array) (bv: BufferView) (acc: Accessor) : int array =
    let size = componentSize acc.ComponentType
    let stride = match bv.ByteStride with Some s -> s | None -> size
    Array.init acc.Count (fun i -> indexAt buf acc.ComponentType (bv.ByteOffset + acc.ByteOffset + i * stride))

// ── load ──

let load (path: string) : Model =
    let dir = Path.GetDirectoryName(path)
    let json = File.ReadAllText(path)
    use doc = JsonDocument.Parse(json)
    let root = doc.RootElement

    if root.TryGetProperty("skins") |> fst then failwith "gltf: skins unsupported"
    if root.TryGetProperty("animations") |> fst then failwith "gltf: animations unsupported"

    // external `.bin` buffers, resolved relative to the .gltf directory
    let buffers =
        optArray root "buffers"
        |> Array.map (fun b ->
            let uri =
                match b.TryGetProperty("uri") with
                | true, u -> u.GetString()
                | false, _ -> failwith "gltf: buffer without uri unsupported"
            if uri.StartsWith("data:") then failwith "gltf: embedded data-URI buffers unsupported"
            File.ReadAllBytes(Path.Combine(dir, uri)))

    let bufferViews =
        optArray root "bufferViews"
        |> Array.map (fun bv ->
            { Buffer = reqInt bv "buffer"
              ByteOffset = defaultArg (optInt bv "byteOffset") 0
              ByteStride = optInt bv "byteStride" })

    let accessors =
        optArray root "accessors"
        |> Array.map (fun a ->
            if a.TryGetProperty("sparse") |> fst then failwith "gltf: sparse accessors unsupported"
            { BufferView = reqInt a "bufferView"
              ByteOffset = defaultArg (optInt a "byteOffset") 0
              ComponentType = reqInt a "componentType"
              Count = reqInt a "count"
              Components = typeComponents ((req a "type").GetString()) })

    // images: external uri only; embedded (bufferView/data:) raise.
    let images =
        optArray root "images"
        |> Array.map (fun img ->
            match img.TryGetProperty("uri") with
            | true, u ->
                let uri = u.GetString()
                if uri.StartsWith("data:") then failwith "gltf: embedded data-URI images unsupported"
                uri
            | false, _ ->
                if img.TryGetProperty("bufferView") |> fst then failwith "gltf: embedded images unsupported"
                else failwith "gltf: image without uri unsupported")

    // textures: source -> image index
    let textures = optArray root "textures" |> Array.map (fun t -> reqInt t "source")

    let textureRef (index: int) : TextureRef =
        if index < 0 || index >= textures.Length then failwith "gltf: texture index out of range"
        let imageIndex = textures.[index]
        if imageIndex < 0 || imageIndex >= images.Length then failwith "gltf: image index out of range"
        { Uri = images.[imageIndex] }

    let floatList (e: JsonElement) (name: string) (n: int) (def: float32 array) : float32 array =
        match e.TryGetProperty(name) with
        | false, _ -> def
        | true, v ->
        let c = v.EnumerateArray() |> Seq.map _.GetSingle() |> Seq.toArray
        if c.Length <> n then failwithf "gltf: %s must have %d components" name n
        c

    let materials =
        optArray root "materials"
        |> Array.map (fun m ->
            let pbr = match m.TryGetProperty("pbrMetallicRoughness") with | true, p -> p | false, _ -> m
            let baseColor = floatList pbr "baseColorFactor" 4 [| 1.0f; 1.0f; 1.0f; 1.0f |]
            let metallic = match pbr.TryGetProperty("metallicFactor") with | true, f -> f.GetSingle() | false, _ -> 1.0f
            let roughness = match pbr.TryGetProperty("roughnessFactor") with | true, f -> f.GetSingle() | false, _ -> 1.0f
            let emissive = floatList m "emissiveFactor" 3 [| 0.0f; 0.0f; 0.0f |]
            let texRef (block: JsonElement) (name: string) : TextureRef option =
                match block.TryGetProperty(name) with
                | true, t -> Some (textureRef (reqInt t "index"))
                | false, _ -> None
            { BaseColor = Vector4(baseColor.[0], baseColor.[1], baseColor.[2], baseColor.[3])
              Metallic = metallic
              Roughness = roughness
              Emissive = Vector3(emissive.[0], emissive.[1], emissive.[2])
              AlbedoTexture = texRef pbr "baseColorTexture"
              MetallicRoughnessTexture = texRef pbr "metallicRoughnessTexture"
              NormalTexture = texRef m "normalTexture"
              EmissiveTexture = texRef m "emissiveTexture" })

    let nodes =
        optArray root "nodes"
        |> Array.map (fun n ->
            if n.TryGetProperty("matrix") |> fst then failwith "gltf: node matrix unsupported (use TRS)"
            let vec3 (name: string) (def: Vector3) : Vector3 =
                match n.TryGetProperty(name) with
                | true, v ->
                    let c = v.EnumerateArray() |> Seq.map (fun x -> x.GetSingle()) |> Seq.toArray
                    if c.Length <> 3 then failwithf "gltf: %s must have 3 components" name
                    Vector3(c.[0], c.[1], c.[2])
                | false, _ -> def
            let rotation =
                match n.TryGetProperty("rotation") with
                | true, v ->
                    let c = v.EnumerateArray() |> Seq.map (fun x -> x.GetSingle()) |> Seq.toArray
                    if c.Length <> 4 then failwith "gltf: rotation must have 4 components"
                    Quaternion(c.[0], c.[1], c.[2], c.[3])
                | false, _ -> Quaternion.Identity
            { Name = optString n "name"
              Children = optArray n "children" |> Array.map (fun c -> c.GetInt32())
              Translation = vec3 "translation" Vector3.Zero
              Rotation = rotation
              Scale = vec3 "scale" Vector3.One
              Mesh = optInt n "mesh" })

    let meshes = optArray root "meshes"

    // Read one primitive's geometry (kept in node-local space; the node's world
    // transform is carried on the part, not baked into the vertices).
    let readPrimitive (meshName: string) (primCount: int) (primIndex: int) (prim: JsonElement) : Submesh =
        let mode = defaultArg (optInt prim "mode") 4
        if mode <> 4 then failwithf "gltf: non-triangle primitive mode %d" mode

        let attrs =
            match prim.TryGetProperty("attributes") with
            | true, a -> a
            | false, _ -> failwith "gltf: primitive missing attributes"

        let attrAccessor (name: string) : int option =
            match attrs.TryGetProperty(name) with
            | true, v -> Some (v.GetInt32())
            | false, _ -> None

        let readFloatAttr (name: string) : float32 array option =
            match attrAccessor name with
            | None -> None
            | Some ai ->
                let acc = accessors.[ai]
                if acc.ComponentType <> ctFloat then failwithf "gltf: non-float32 vertex attribute %s" name
                Some (readFloats buffers.[bufferViews.[acc.BufferView].Buffer] bufferViews.[acc.BufferView] acc)

        let positions =
            match readFloatAttr "POSITION" with
            | Some p -> p
            | None -> failwith "gltf: primitive missing POSITION"

        let vertexCount = positions.Length / 3
        let normals = match readFloatAttr "NORMAL" with Some n -> n | None -> Array.zeroCreate<float32> (vertexCount * 3)
        let uvs = match readFloatAttr "TEXCOORD_0" with Some u -> u | None -> Array.zeroCreate<float32> (vertexCount * 2)

        let indices =
            match prim.TryGetProperty("indices") with
            | true, idx ->
                let acc = accessors.[idx.GetInt32()]
                readIndices buffers.[bufferViews.[acc.BufferView].Buffer] bufferViews.[acc.BufferView] acc
            | false, _ -> Array.init vertexCount id

        let material = defaultArg (optInt prim "material") -1

        let name = if primCount = 1 then meshName else sprintf "%s.%d" meshName primIndex
        { Name = name
          Material = material
          Positions = positions
          Normals = normals
          Uvs = uvs
          Indices = indices }

    // Read every mesh's primitives once (deduplicating instanced meshes) into a
    // mesh-index → submesh-range table, so shared geometry is uploaded once and
    // each part references it by range.
    let submeshes = ResizeArray<Submesh>()
    let meshRanges = Array.zeroCreate<int * int> meshes.Length
    for meshIndex in 0 .. meshes.Length - 1 do
        let prims = optArray meshes.[meshIndex] "primitives"
        let meshName = optString meshes.[meshIndex] "name"
        let first = submeshes.Count
        for primIndex in 0 .. prims.Length - 1 do
            submeshes.Add(readPrimitive meshName prims.Length primIndex prims.[primIndex])
        meshRanges.[meshIndex] <- (first, prims.Length)

    // Walk the scene node tree, emitting one part per node that references a mesh.
    // A part's World is the accumulated node transform (applied at spawn/draw time,
    // not baked into the vertices), so each part can become an independently-
    // transformable prop. `onPath` is the set of ancestors on the current branch, so a node graph that is
    // its own ancestor is reported rather than recursing until the stack overflows (spec 080, US7).
    let parts = ResizeArray<Part>()

    let rec bake (onPath: Set<int>) (nodeIndex: int) (parentWorld: Matrix4x4) (parentPath: string) : unit =
        if Set.contains nodeIndex onPath then
            failwithf "gltf: node %d is its own ancestor (a cycle in the node graph)" nodeIndex
        let onPath = Set.add nodeIndex onPath
        let n = nodes.[nodeIndex]
        let local =
            Matrix4x4.CreateScale(n.Scale) * Matrix4x4.CreateFromQuaternion(n.Rotation) * Matrix4x4.CreateTranslation(n.Translation)
        // Row-vector convention: a point goes local -> parent -> world, so the
        // node's accumulated world matrix is `local * parentWorld` (apply the
        // node's transform, then the ancestors').
        let world = local * parentWorld
        let nodeLabel = if n.Name = "" then sprintf "Node%d" nodeIndex else n.Name
        let path = if parentPath = "" then nodeLabel else sprintf "%s/%s" parentPath nodeLabel

        match n.Mesh with
        | Some meshIndex ->
            let (first, count) = meshRanges.[meshIndex]
            parts.Add(
                { Name = n.Name
                  HierarchyPath = path
                  World = world
                  SubmeshIndex = first
                  SubmeshCount = count })
        | None -> ()

        for child in n.Children do
            bake onPath child world path

    let sceneIndex = defaultArg (optInt root "scene") 0
    let scenes = optArray root "scenes"
    let roots =
        if sceneIndex >= 0 && sceneIndex < scenes.Length then
            optArray scenes.[sceneIndex] "nodes" |> Array.map (fun n -> n.GetInt32())
        else
            [||]

    for r in roots do
        bake Set.empty r Matrix4x4.Identity ""

    // bounds over all local-space positions (informational; drawn via parts).
    let mutable minX = Single.PositiveInfinity
    let mutable minY = Single.PositiveInfinity
    let mutable minZ = Single.PositiveInfinity
    let mutable maxX = Single.NegativeInfinity
    let mutable maxY = Single.NegativeInfinity
    let mutable maxZ = Single.NegativeInfinity
    for sub in submeshes do
        let vcount = sub.Positions.Length / 3
        for v in 0 .. vcount - 1 do
            let x = sub.Positions.[v * 3]
            let y = sub.Positions.[v * 3 + 1]
            let z = sub.Positions.[v * 3 + 2]
            if x < minX then minX <- x
            if y < minY then minY <- y
            if z < minZ then minZ <- z
            if x > maxX then maxX <- x
            if y > maxY then maxY <- y
            if z > maxZ then maxZ <- z

    { Submeshes = submeshes.ToArray()
      Parts = parts.ToArray()
      Materials = materials
      Bounds = { Min = Vector3(minX, minY, minZ); Max = Vector3(maxX, maxY, maxZ) } }
