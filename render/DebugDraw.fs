module DebugDraw

// Line-only SDL_gpu renderer: a CPU line accumulator plus the GPU line
// pipeline/buffers. The accumulator is a pre-allocated float array (7 floats per
// vertex: position.xyz + color.rgba), reused every frame to avoid allocation.
//
// The view-projection Matrix4x4 is uploaded without transposing: .NET's
// Matrix4x4 is row-major storage with row-vector math (`v * M`), which is
// exactly the transpose of a column-major GLSL `mat4` doing `u * v`, so the raw
// M11..M44 floats are already correct for `u.viewProj * vec4(aPosition, 1.0)`.

open System
open System.Numerics
open System.Runtime.InteropServices
open SDL3

[<Struct>]
type Color =
    { R : float32
      G : float32
      B : float32
      A : float32 }

// 7 floats per vertex (position.xyz + color.rgba) = 28 bytes.
let private floatsPerVertex = 7
let private capacity = 65536

[<Struct>]
type DebugDraw =
    { Device : Gpu.Device
      Pipeline : Gpu.Pipeline
      VertexShader : Gpu.Shader
      FragmentShader : Gpu.Shader
      VertexBuffer : Gpu.Buffer
      TransferBuffer : Gpu.TransferBuffer
      Bindings : SDL.GPUBufferBinding array
      Data : float32 array
      StagingBytes : byte array
      UniformBytes : byte array
      mutable Count : int }

// ── GPU resource construction ──

// `depthTest` selects a pipeline that depth-tests against the scene (used for
// editor markers/gizmos drawn after meshes, so they are occluded when behind
// geometry). Depth writes stay off so the lines don't pollute the depth buffer.
let create (device: Gpu.Device) (swapchainFormat: SDL.GPUTextureFormat) (depthFormat: SDL.GPUTextureFormat) (depthTest: bool) : DebugDraw =
    let vertexShader = Gpu.loadShader device SDL.GPUShaderStage.Vertex "shaders/line.vert.spv" 1u 0u 0u
    let fragmentShader = Gpu.loadShader device SDL.GPUShaderStage.Fragment "shaders/line.frag.spv" 0u 0u 0u

    // One vertex buffer, 28-byte stride: float3 position + float4 color.
    let mutable vbufDesc = SDL.GPUVertexBufferDescription()
    vbufDesc.Slot <- 0u
    vbufDesc.Pitch <- uint32 (floatsPerVertex * 4)
    vbufDesc.InputRate <- SDL.GPUVertexInputRate.Vertex
    vbufDesc.InstanceStepRate <- 0u

    let mutable attr0 = SDL.GPUVertexAttribute()
    attr0.Location <- 0u
    attr0.BufferSlot <- 0u
    attr0.Format <- SDL.GPUVertexElementFormat.Float3
    attr0.Offset <- 0u

    let mutable attr1 = SDL.GPUVertexAttribute()
    attr1.Location <- 1u
    attr1.BufferSlot <- 0u
    attr1.Format <- SDL.GPUVertexElementFormat.Float4
    attr1.Offset <- 12u

    let mutable raster = SDL.GPURasterizerState()
    raster.FillMode <- SDL.GPUFillMode.Fill
    raster.CullMode <- SDL.GPUCullMode.None
    raster.FrontFace <- SDL.GPUFrontFace.CounterClockwise

    let mutable ms = SDL.GPUMultisampleState()
    ms.SampleCount <- Gpu.SampleCount
    ms.SampleMask <- 0u

    let mutable colorTarget = SDL.GPUColorTargetDescription()
    colorTarget.Format <- swapchainFormat
    // BlendState stays default: no blending, all channels written.

    let vbufDescs = [| vbufDesc |]
    let attrs = [| attr0; attr1 |]
    let colorTargets = [| colorTarget |]

    let depthStencil =
        if depthTest then
            let mutable depth = SDL.GPUDepthStencilState()
            depth.EnableDepthTest <- true
            depth.EnableDepthWrite <- false
            depth.CompareOp <- SDL.GPUCompareOp.LessOrEqual
            ValueSome depth
        else
            ValueNone

    let pipeline =
        Gpu.createGraphicsPipeline device vertexShader fragmentShader SDL.GPUPrimitiveType.LineList vbufDescs attrs colorTargets raster ms depthStencil depthFormat

    let byteSize = uint32 (capacity * floatsPerVertex * 4)

    let mutable bufInfo = SDL.GPUBufferCreateInfo()
    bufInfo.Usage <- SDL.GPUBufferUsageFlags.Vertex
    bufInfo.Size <- byteSize
    bufInfo.Props <- 0u
    let vertexBuffer = SDL.CreateGPUBuffer(device, &bufInfo)

    let mutable tfInfo = SDL.GPUTransferBufferCreateInfo()
    tfInfo.Usage <- SDL.GPUTransferBufferUsage.Upload
    tfInfo.Size <- byteSize
    tfInfo.Props <- 0u
    let transferBuffer = SDL.CreateGPUTransferBuffer(device, &tfInfo)

    { Device = device
      Pipeline = pipeline
      VertexShader = vertexShader
      FragmentShader = fragmentShader
      VertexBuffer = vertexBuffer
      TransferBuffer = transferBuffer
      Bindings = Array.zeroCreate<SDL.GPUBufferBinding> 1
      Data = Array.zeroCreate<float32> (capacity * floatsPerVertex)
      StagingBytes = Array.zeroCreate<byte> (capacity * floatsPerVertex * 4)
      UniformBytes = Array.zeroCreate<byte> 64
      Count = 0 }

// ── CPU accumulator ──

let clear (dd: DebugDraw) : DebugDraw =
    { dd with Count = 0 }

// Append one vertex through a byref: a primitive that emits many vertices must not copy the
// whole record for every vertex. `Count` is the only mutable field; `Data` is a shared array.
let private appendVertex (dd: byref<DebugDraw>) (p: Vector3) (c: Color) : unit =
    let i = dd.Count * floatsPerVertex
    dd.Data.[i] <- p.X
    dd.Data.[i + 1] <- p.Y
    dd.Data.[i + 2] <- p.Z
    dd.Data.[i + 3] <- c.R
    dd.Data.[i + 4] <- c.G
    dd.Data.[i + 5] <- c.B
    dd.Data.[i + 6] <- c.A
    dd.Count <- dd.Count + 1

let line (dd: byref<DebugDraw>) (a: Vector3) (b: Vector3) (c: Color) : unit =
    appendVertex &dd a c
    appendVertex &dd b c

// 12-edge wireframe box from a center + half-extent (markers, scale gizmo tips).
let wireframeBox (dd: byref<DebugDraw>) (center: Vector3) (half: Vector3) (c: Color) : unit =
    let x0 = center.X - half.X
    let x1 = center.X + half.X
    let y0 = center.Y - half.Y
    let y1 = center.Y + half.Y
    let z0 = center.Z - half.Z
    let z1 = center.Z + half.Z
    let v x y z = Vector3(x, y, z)
    line &dd (v x0 y0 z0) (v x1 y0 z0) c
    line &dd (v x1 y0 z0) (v x1 y0 z1) c
    line &dd (v x1 y0 z1) (v x0 y0 z1) c
    line &dd (v x0 y0 z1) (v x0 y0 z0) c
    line &dd (v x0 y1 z0) (v x1 y1 z0) c
    line &dd (v x1 y1 z0) (v x1 y1 z1) c
    line &dd (v x1 y1 z1) (v x0 y1 z1) c
    line &dd (v x0 y1 z1) (v x0 y1 z0) c
    line &dd (v x0 y0 z0) (v x0 y1 z0) c
    line &dd (v x1 y0 z0) (v x1 y1 z0) c
    line &dd (v x1 y0 z1) (v x1 y1 z1) c
    line &dd (v x0 y0 z1) (v x0 y1 z1) c

// Sphere outline as three orthogonal great circles (selection highlight, move
// gizmo tips).
let sphere (dd: byref<DebugDraw>) (center: Vector3) (radius: float32) (c: Color) : unit =
    let segments = 24
    for plane in 0 .. 2 do
        let mutable prev = None
        for i in 0 .. segments do
            let a = 2.0f * MathF.PI * float32 i / float32 segments
            let ca = MathF.Cos a
            let sa = MathF.Sin a
            let p =
                match plane with
                | 0 -> center + Vector3(radius * ca, radius * sa, 0.0f)
                | 1 -> center + Vector3(radius * ca, 0.0f, radius * sa)
                | _ -> center + Vector3(0.0f, radius * ca, radius * sa)
            match prev with
            | Some q -> line &dd q p c
            | None -> ()
            prev <- Some p

// A circle centred at `o` in the plane spanned by `u`/`v`.
let private circleInto (dd: byref<DebugDraw>) (o: Vector3) (u: Vector3) (v: Vector3) (radius: float32) (segments: int) (c: Color) : unit =
    let mutable prev = None
    for i in 0 .. segments do
        let a = 2.0f * MathF.PI * float32 i / float32 segments
        let p = o + u * (radius * MathF.Cos a) + v * (radius * MathF.Sin a)
        match prev with
        | Some q -> line &dd q p c
        | None -> ()
        prev <- Some p

// A half-circle through `pole`, started at `o + u * radius`.
let private arcInto (dd: byref<DebugDraw>) (o: Vector3) (u: Vector3) (pole: Vector3) (radius: float32) (segments: int) (c: Color) : unit =
    let mutable prev = None
    for i in 0 .. segments do
        let a = MathF.PI * float32 i / float32 segments
        let p = o + u * (radius * MathF.Cos a) + pole * (radius * MathF.Sin a)
        match prev with
        | Some q -> line &dd q p c
        | None -> ()
        prev <- Some p

// A horizontal ring of `segments` edges centred at `o`.
let private ringInto (dd: byref<DebugDraw>) (o: Vector3) (radius: float32) (segments: int) (c: Color) : unit =
    let mutable prev = None
    for i in 0 .. segments do
        let a = 2.0f * MathF.PI * float32 i / float32 segments
        let p = o + Vector3(radius * MathF.Cos a, 0.0f, radius * MathF.Sin a)
        match prev with
        | Some q -> line &dd q p c
        | None -> ()
        prev <- Some p

// Capsule wireframe: a cylinder silhouette, the two hemisphere equator circles,
// and the hemisphere silhouette arcs (through the poles).
let capsule (dd: byref<DebugDraw>) (center: Vector3) (halfHeight: float32) (radius: float32) (c: Color) : unit =
    let segments = 24
    let top = center + Vector3(0.0f, halfHeight, 0.0f)
    let bottom = center - Vector3(0.0f, halfHeight, 0.0f)

    // Cylinder silhouette (four vertical lines between the equators).
    line &dd (top + Vector3(radius, 0.0f, 0.0f)) (bottom + Vector3(radius, 0.0f, 0.0f)) c
    line &dd (top - Vector3(radius, 0.0f, 0.0f)) (bottom - Vector3(radius, 0.0f, 0.0f)) c
    line &dd (top + Vector3(0.0f, 0.0f, radius)) (bottom + Vector3(0.0f, 0.0f, radius)) c
    line &dd (top - Vector3(0.0f, 0.0f, radius)) (bottom - Vector3(0.0f, 0.0f, radius)) c

    // Hemisphere equator circles (horizontal, in the XZ plane).
    circleInto &dd top Vector3.UnitX Vector3.UnitZ radius segments c
    circleInto &dd bottom Vector3.UnitX Vector3.UnitZ radius segments c

    // Hemisphere silhouette arcs (half-circles through the pole `p`).
    arcInto &dd top Vector3.UnitX Vector3.UnitY radius segments c
    arcInto &dd top Vector3.UnitZ Vector3.UnitY radius segments c
    arcInto &dd bottom Vector3.UnitX -Vector3.UnitY radius segments c
    arcInto &dd bottom Vector3.UnitZ -Vector3.UnitY radius segments c

// Cylinder wireframe: the two end rings and a vertical line at each segment angle. The
// centre and half-height form matches `capsule`, so a caller that has a base and a height
// passes `base + height / 2` and `height / 2`.
let cylinder (dd: byref<DebugDraw>) (center: Vector3) (halfHeight: float32) (radius: float32) (c: Color) : unit =
    let segments = 16
    let top = center + Vector3(0.0f, halfHeight, 0.0f)
    let bottom = center - Vector3(0.0f, halfHeight, 0.0f)

    ringInto &dd top radius segments c
    ringInto &dd bottom radius segments c

    // One vertical edge per segment, at the same angles as the rings' vertices.
    for i in 0 .. segments - 1 do
        let a = 2.0f * MathF.PI * float32 i / float32 segments
        let offset = Vector3(radius * MathF.Cos a, 0.0f, radius * MathF.Sin a)
        line &dd (bottom + offset) (top + offset) c

let drawGrid (dd: byref<DebugDraw>) : unit =
    let minor = { R = 0.4f; G = 0.4f; B = 0.4f; A = 1.0f }
    let major = { R = 0.6f; G = 0.6f; B = 0.6f; A = 1.0f }
    let axisX = { R = 1.0f; G = 0.3f; B = 0.3f; A = 1.0f }
    let axisZ = { R = 0.3f; G = 0.3f; B = 1.0f; A = 1.0f }

    for i in -100 .. 100 do
        let v = float32 i
        let color =
            if i = 0 then axisZ
            elif i % 10 = 0 then major
            else minor
        line &dd (Vector3(v, 0.0f, -100.0f)) (Vector3(v, 0.0f, 100.0f)) color
        let color =
            if i = 0 then axisX
            elif i % 10 = 0 then major
            else minor
        line &dd (Vector3(-100.0f, 0.0f, v)) (Vector3(100.0f, 0.0f, v)) color

// ── GPU upload + draw ──

// Upload accumulated vertices to the GPU vertex buffer (copy pass, before the
// render pass).
let upload (dd: DebugDraw) (cmd: Gpu.CommandBuffer) : unit =
    if dd.Count <= 0 then () else

    let floatCount = dd.Count * floatsPerVertex
    do Buffer.BlockCopy(dd.Data, 0, dd.StagingBytes, 0, floatCount * 4)
    // Recurring per-frame map: cycle so an earlier in-flight frame still reading this staging
    // buffer does not race this frame's write (spec 098, F-02).
    let mapped = SDL.MapGPUTransferBuffer(dd.Device, dd.TransferBuffer, true)
    do Marshal.Copy(dd.StagingBytes, 0, mapped, floatCount * 4)
    do SDL.UnmapGPUTransferBuffer(dd.Device, dd.TransferBuffer)

    let copyPass = SDL.BeginGPUCopyPass(cmd)
    let mutable loc = SDL.GPUTransferBufferLocation()
    loc.TransferBuffer <- dd.TransferBuffer
    loc.Offset <- 0u
    let mutable region = SDL.GPUBufferRegion()
    region.Buffer <- dd.VertexBuffer
    region.Offset <- 0u
    region.Size <- uint32 (floatCount * 4)
    do SDL.UploadToGPUBuffer(copyPass, &loc, &region, false)
    do SDL.EndGPUCopyPass(copyPass)

// Bind the line pipeline, push the view-projection uniform, and draw the
// accumulated vertices. Called inside the render pass; the uniform push targets
// the command buffer.
let draw (dd: DebugDraw) (viewProjection: Matrix4x4) (cmd: Gpu.CommandBuffer) (renderPass: Gpu.RenderPass) : unit =
    if dd.Count <= 0 then () else

    do Gpu.writeMatrix dd.UniformBytes 0 viewProjection
    do SDL.PushGPUVertexUniformData(cmd, 0u, dd.UniformBytes, 64u)
    do SDL.BindGPUGraphicsPipeline(renderPass, dd.Pipeline)

    let bindings = dd.Bindings
    bindings.[0].Buffer <- dd.VertexBuffer
    bindings.[0].Offset <- 0u

    do SDL.BindGPUVertexBuffers(renderPass, 0u, bindings, 1u)
    do SDL.DrawGPUPrimitives(renderPass, uint32 dd.Count, 1u, 0u, 0u)

// Free the owned GPU resources (pipeline, shaders, buffers).
let release (dd: DebugDraw) : unit =
    if dd.Pipeline <> IntPtr.Zero then SDL.ReleaseGPUGraphicsPipeline(dd.Device, dd.Pipeline)
    if dd.VertexShader <> IntPtr.Zero then SDL.ReleaseGPUShader(dd.Device, dd.VertexShader)
    if dd.FragmentShader <> IntPtr.Zero then SDL.ReleaseGPUShader(dd.Device, dd.FragmentShader)
    if dd.VertexBuffer <> IntPtr.Zero then SDL.ReleaseGPUBuffer(dd.Device, dd.VertexBuffer)
    if dd.TransferBuffer <> IntPtr.Zero then SDL.ReleaseGPUTransferBuffer(dd.Device, dd.TransferBuffer)
