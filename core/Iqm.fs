module Iqm

// Scoped IQM (Inter-Quake Model) v2 binary parser, ported from
// ocaml-game/lib/core/iqm.ml. Data loading only: header, meshes, vertex arrays
// (FLOAT/UBYTE), triangles, joints, poses, animations, frame channels. Anything
// unsupported raises (constitution II). Emits flat struct-of-arrays as float32,
// which feeds the CPU skinner and the GPU buffers directly.
//
// Reads are little-endian; the file is read fully into memory and fields are
// read at absolute offsets (mirrors the OCaml parser).

open System
open System.IO
open System.Numerics
open System.Buffers.Binary

type VertexFormat = Float | Ubyte

type VertexKind = Position | Texcoord | Normal | Tangent | BlendIndices | BlendWeights

type VertexArray =
    { Kind : VertexKind
      Format : VertexFormat
      Size : int
      Offset : int }

type Mesh =
    { Name : string
      Material : string
      FirstVertex : int
      NumVertices : int
      FirstTriangle : int
      NumTriangles : int }

type Joint =
    { Name : string
      Parent : int
      Translate : Vector3
      Rotate : Quaternion
      Scale : Vector3 }

type Pose =
    { Parent : int
      ChannelMask : int
      ChannelOffset : float32 array   // length 10
      ChannelScale : float32 array }  // length 10

type Anim =
    { Name : string
      FirstFrame : int
      NumFrames : int
      FrameRate : float32
      Loop : bool }

type Model =
    { Meshes : Mesh array
      Positions : float32 array
      Normals : float32 array option
      Uvs : float32 array option
      Tangents : float32 array option
      BlendIndices : byte array option
      BlendWeights : byte array option
      Triangles : int array
      Joints : Joint array
      Poses : Pose array
      Anims : Anim array
      FrameChannels : int array
      NumFrameChannels : int
      PoseChannelOffsets : int array }

// ── little-endian reads at absolute offsets ──

let private u32 (b: byte array) (off: int) : uint32 =
    BinaryPrimitives.ReadUInt32LittleEndian(ReadOnlySpan<byte>(b, off, 4))

let private i32 (b: byte array) (off: int) : int =
    BinaryPrimitives.ReadInt32LittleEndian(ReadOnlySpan<byte>(b, off, 4))

let private f32 (b: byte array) (off: int) : float32 =
    BitConverter.Int32BitsToSingle(i32 b off)

let private u16 (b: byte array) (off: int) : int =
    int (BinaryPrimitives.ReadUInt16LittleEndian(ReadOnlySpan<byte>(b, off, 2)))

// NUL-terminated string at (ofs_text + nameOffset).
let private cstring (b: byte array) (ofsText: int) (nameOffset: int) : string =
    let start = ofsText + nameOffset
    let mutable len = 0
    while start + len < b.Length && b.[start + len] <> 0uy do
        len <- len + 1
    System.Text.Encoding.ASCII.GetString(b, start, len)

// ── header ──

let private magic = 0x45544E49u // "INTE", little-endian

let private kindOfInt k =
    match k with
    | 0 -> VertexKind.Position
    | 1 -> VertexKind.Texcoord
    | 2 -> VertexKind.Normal
    | 3 -> VertexKind.Tangent
    | 4 -> VertexKind.BlendIndices
    | 5 -> VertexKind.BlendWeights
    | 6 -> failwith "iqm: COLOR vertex array unsupported"
    | _ -> failwithf "iqm: unknown vertex array type %d" k

let private formatOfInt f =
    match f with
    | 1 -> VertexFormat.Ubyte
    | 7 -> VertexFormat.Float
    | _ -> failwithf "iqm: unsupported vertex format %d" f

// ── load ──

let load (path: string) : Model =
    let b = File.ReadAllBytes(path)

    if b.Length < 124 then failwith "iqm: file too short"
    if u32 b 0 <> magic then failwith "iqm: not an IQM file"
    if u32 b 16 <> 2u then failwith "iqm: unsupported IQM version"

    let numText = int (u32 b 28)
    let ofsText = int (u32 b 32)
    let numMeshes = int (u32 b 36)
    let ofsMeshes = int (u32 b 40)
    let numVertexArrays = int (u32 b 44)
    let numVertexes = int (u32 b 48)
    let ofsVertexArrays = int (u32 b 52)
    let numTriangles = int (u32 b 56)
    let ofsTriangles = int (u32 b 60)
    let numJoints = int (u32 b 68)
    let ofsJoints = int (u32 b 72)
    let numPoses = int (u32 b 76)
    let ofsPoses = int (u32 b 80)
    let numAnims = int (u32 b 84)
    let ofsAnims = int (u32 b 88)
    let numFrames = int (u32 b 92)
    let numFrameChannels = int (u32 b 96)
    let ofsFrames = int (u32 b 100)

    // meshes (24 bytes each)
    let meshes =
        Array.init numMeshes (fun i ->
            let off = ofsMeshes + i * 24
            { Name = cstring b ofsText (int (u32 b off))
              Material = cstring b ofsText (int (u32 b (off + 4)))
              FirstVertex = int (u32 b (off + 8))
              NumVertices = int (u32 b (off + 12))
              FirstTriangle = int (u32 b (off + 16))
              NumTriangles = int (u32 b (off + 20)) })

    // triangles (3 x u32 each)
    let triangles =
        Array.init (numTriangles * 3) (fun i -> int (u32 b (ofsTriangles + i * 4)))

    // joints (48 bytes each)
    let joints =
        Array.init numJoints (fun i ->
            let off = ofsJoints + i * 48
            { Name = cstring b ofsText (int (u32 b off))
              Parent = i32 b (off + 4)
              Translate = Vector3(f32 b (off + 8), f32 b (off + 12), f32 b (off + 16))
              Rotate = Quaternion(f32 b (off + 20), f32 b (off + 24), f32 b (off + 28), f32 b (off + 32))
              Scale = Vector3(f32 b (off + 36), f32 b (off + 40), f32 b (off + 44)) })

    // poses (88 bytes each): parent, channelmask, channeloffset[10], channelscale[10]
    let poses =
        Array.init numPoses (fun i ->
            let off = ofsPoses + i * 88
            { Parent = i32 b off
              ChannelMask = int (u32 b (off + 4))
              ChannelOffset = Array.init 10 (fun j -> f32 b (off + 8 + j * 4))
              ChannelScale = Array.init 10 (fun j -> f32 b (off + 48 + j * 4)) })

    // anims (20 bytes each): name, first_frame, num_frames, framerate, flags
    let anims =
        Array.init numAnims (fun i ->
            let off = ofsAnims + i * 20
            { Name = cstring b ofsText (int (u32 b off))
              FirstFrame = int (u32 b (off + 4))
              NumFrames = int (u32 b (off + 8))
              FrameRate = f32 b (off + 12)
              Loop = (u32 b (off + 16) &&& 1u) <> 0u })

    // frame channels: uint16 array, num_frames * num_framechannels entries
    let frameChannels =
        Array.init (numFrames * numFrameChannels) (fun i -> u16 b (ofsFrames + i * 2))

    // vertex arrays (20 bytes each): type, flags, format, size, offset
    let vertexArrays =
        Array.init numVertexArrays (fun i ->
            let off = ofsVertexArrays + i * 20
            { Kind = kindOfInt (int (u32 b off))
              Format = formatOfInt (int (u32 b (off + 8)))
              Size = int (u32 b (off + 12))
              Offset = int (u32 b (off + 16)) })

    // Decode each vertex array into its flat float32 array.
    let mutable positions = None
    let mutable normals = None
    let mutable uvs = None
    let mutable tangents = None
    let mutable blendIndices = None
    let mutable blendWeights = None

    for va in vertexArrays do
        let count = numVertexes * va.Size
        match va.Format with
        | VertexFormat.Float ->
            let data = Array.init count (fun i -> f32 b (va.Offset + i * 4))
            match va.Kind with
            | Position -> positions <- Some data
            | Texcoord -> uvs <- Some data
            | Normal -> normals <- Some data
            | Tangent -> tangents <- Some data
            | _ -> failwith "iqm: unexpected float vertex kind"
        | VertexFormat.Ubyte ->
            let data = Array.init count (fun i -> b.[va.Offset + i])
            match va.Kind with
            | BlendWeights -> blendWeights <- Some data
            | BlendIndices -> blendIndices <- Some data
            | _ -> failwith "iqm: unexpected ubyte vertex kind"

    // Per-pose channel offset within a frame (packed by the set bits of each
    // pose's channelmask, in increasing channel order).
    let poseChannelOffsets =
        let offsets = Array.zeroCreate<int> numPoses
        let mutable acc = 0
        for i in 0 .. numPoses - 1 do
            offsets.[i] <- acc
            acc <- acc + System.Numerics.BitOperations.PopCount(uint32 poses.[i].ChannelMask)
        offsets

    { Meshes = meshes
      Positions = match positions with Some p -> p | None -> failwith "iqm: missing POSITION vertex array"
      Normals = normals
      Uvs = uvs
      Tangents = tangents
      BlendIndices = blendIndices
      BlendWeights = blendWeights
      Triangles = triangles
      Joints = joints
      Poses = poses
      Anims = anims
      FrameChannels = frameChannels
      NumFrameChannels = numFrameChannels
      PoseChannelOffsets = poseChannelOffsets }
