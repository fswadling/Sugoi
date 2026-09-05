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

type Submesh =
    { Name : string
      Material : int
      BaseColor : Vector4
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

    let materialBaseColors =
        optArray root "materials"
        |> Array.map (fun m ->
            match m.TryGetProperty("pbrMetallicRoughness") with
            | true, pbr ->
                match pbr.TryGetProperty("baseColorFactor") with
                | true, f ->
                    let c = f.EnumerateArray() |> Seq.map (fun x -> x.GetSingle()) |> Seq.toArray
                    if c.Length <> 4 then failwith "gltf: baseColorFactor must have 4 components"
                    Vector4(c.[0], c.[1], c.[2], c.[3])
                | false, _ -> Vector4(1.0f, 1.0f, 1.0f, 1.0f)
            | false, _ -> Vector4(1.0f, 1.0f, 1.0f, 1.0f))

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
        let baseColor =
            if material >= 0 && material < materialBaseColors.Length then materialBaseColors.[material]
            else Vector4(1.0f, 1.0f, 1.0f, 1.0f)

        let name = if primCount = 1 then meshName else sprintf "%s.%d" meshName primIndex
        { Name = name
          Material = material
          BaseColor = baseColor
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
    // transformable prop.
    let parts = ResizeArray<Part>()

    let rec bake (nodeIndex: int) (parentWorld: Matrix4x4) (parentPath: string) : unit =
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
            bake child world path

    let sceneIndex = defaultArg (optInt root "scene") 0
    let scenes = optArray root "scenes"
    let roots =
        if sceneIndex >= 0 && sceneIndex < scenes.Length then
            optArray scenes.[sceneIndex] "nodes" |> Array.map (fun n -> n.GetInt32())
        else
            [||]

    for r in roots do
        bake r Matrix4x4.Identity ""

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
      Bounds = { Min = Vector3(minX, minY, minZ); Max = Vector3(maxX, maxY, maxZ) } }
