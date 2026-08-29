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
open System.IO
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
    { Device : nativeint
      Pipeline : nativeint
      VertexShader : nativeint
      FragmentShader : nativeint
      VertexBuffer : nativeint
      TransferBuffer : nativeint
      Bindings : SDL.GPUBufferBinding array
      Data : float32 array
      StagingBytes : byte array
      UniformBytes : byte array
      Count : int }

// ── GPU resource construction ──

let private loadShader (device: nativeint) (stage: SDL.GPUShaderStage) (path: string) (uniformBuffers: uint32) : nativeint =
    let code = File.ReadAllBytes(path)
    let mutable info = SDL.GPUShaderCreateInfo()
    info.Format <- SDL.GPUShaderFormat.SPIRV
    info.Stage <- stage
    info.NumSamplers <- 0u
    info.NumStorageTextures <- 0u
    info.NumStorageBuffers <- 0u
    info.NumUniformBuffers <- uniformBuffers
    info.Props <- 0u
    SDL.CreateGPUShader(device, &info, ReadOnlySpan<byte>(code), "main")

let create (device: nativeint) (swapchainFormat: SDL.GPUTextureFormat) : DebugDraw =
    let vertexShader = loadShader device SDL.GPUShaderStage.Vertex "shaders/line.vert.spv" 1u
    let fragmentShader = loadShader device SDL.GPUShaderStage.Fragment "shaders/line.frag.spv" 0u

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
    ms.SampleCount <- SDL.GPUSampleCount.SampleCount1
    ms.SampleMask <- 0u

    let mutable colorTarget = SDL.GPUColorTargetDescription()
    colorTarget.Format <- swapchainFormat
    // BlendState stays default: no blending, all channels written.

    let vbufDescs = [| vbufDesc |]
    let attrs = [| attr0; attr1 |]
    let colorTargets = [| colorTarget |]

    let mutable info = SDL.GPUGraphicsPipelineCreateInfo()
    info.VertexShader <- vertexShader
    info.FragmentShader <- fragmentShader
    info.PrimitiveType <- SDL.GPUPrimitiveType.LineList
    info.RasterizerState <- raster
    info.MultisampleState <- ms
    // DepthStencilState default (depth test off).

    let pipeline =
        SDL.CreateGPUGraphicsPipeline(
            device,
            &info,
            ReadOnlySpan<SDL.GPUVertexBufferDescription>(vbufDescs),
            ReadOnlySpan<SDL.GPUVertexAttribute>(attrs),
            ReadOnlySpan<SDL.GPUColorTargetDescription>(colorTargets))

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

let private appendVertex (dd: DebugDraw) (p: Vector3) (c: Color) : DebugDraw =
    let i = dd.Count * floatsPerVertex
    dd.Data.[i] <- p.X
    dd.Data.[i + 1] <- p.Y
    dd.Data.[i + 2] <- p.Z
    dd.Data.[i + 3] <- c.R
    dd.Data.[i + 4] <- c.G
    dd.Data.[i + 5] <- c.B
    dd.Data.[i + 6] <- c.A
    { dd with Count = dd.Count + 1 }

let line (dd: DebugDraw) (a: Vector3) (b: Vector3) (c: Color) : DebugDraw =
    let dd = appendVertex dd a c
    appendVertex dd b c

let drawGrid (dd: DebugDraw) : DebugDraw =
    let minor = { R = 0.4f; G = 0.4f; B = 0.4f; A = 1.0f }
    let major = { R = 0.6f; G = 0.6f; B = 0.6f; A = 1.0f }
    let axisX = { R = 1.0f; G = 0.3f; B = 0.3f; A = 1.0f }
    let axisZ = { R = 0.3f; G = 0.3f; B = 1.0f; A = 1.0f }

    let mutable dd = dd
    for i in -100 .. 100 do
        let v = float32 i
        let color =
            if i = 0 then axisZ
            elif i % 10 = 0 then major
            else minor
        dd <- line dd (Vector3(v, 0.0f, -100.0f)) (Vector3(v, 0.0f, 100.0f)) color
        let color =
            if i = 0 then axisX
            elif i % 10 = 0 then major
            else minor
        dd <- line dd (Vector3(-100.0f, 0.0f, v)) (Vector3(100.0f, 0.0f, v)) color
    dd

// ── GPU upload + draw ──

let private writeMatrix (bytes: byte array) (m: Matrix4x4) : unit =
    // Write the 16 floats straight into the pre-allocated byte buffer (no temp
    // array). .NET Matrix4x4 fields are M11..M44 in memory order.
    let s = MemoryMarshal.Cast<byte, float32>(bytes.AsSpan())
    s.[0] <- m.M11; s.[1] <- m.M12; s.[2] <- m.M13; s.[3] <- m.M14
    s.[4] <- m.M21; s.[5] <- m.M22; s.[6] <- m.M23; s.[7] <- m.M24
    s.[8] <- m.M31; s.[9] <- m.M32; s.[10] <- m.M33; s.[11] <- m.M34
    s.[12] <- m.M41; s.[13] <- m.M42; s.[14] <- m.M43; s.[15] <- m.M44

// Upload accumulated vertices to the GPU vertex buffer (copy pass, before the
// render pass).
let upload (dd: DebugDraw) (cmd: nativeint) : unit =
    if dd.Count <= 0 then () else

    let floatCount = dd.Count * floatsPerVertex
    do Buffer.BlockCopy(dd.Data, 0, dd.StagingBytes, 0, floatCount * 4)
    let mapped = SDL.MapGPUTransferBuffer(dd.Device, dd.TransferBuffer, false)
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
let draw (dd: DebugDraw) (viewProjection: Matrix4x4) (cmd: nativeint) (renderPass: nativeint) : unit =
    if dd.Count <= 0 then () else

    do writeMatrix dd.UniformBytes viewProjection
    do SDL.PushGPUVertexUniformData(cmd, 0u, dd.UniformBytes, 64u)
    do SDL.BindGPUGraphicsPipeline(renderPass, dd.Pipeline)

    let bindings = dd.Bindings
    bindings.[0].Buffer <- dd.VertexBuffer
    bindings.[0].Offset <- 0u

    do SDL.BindGPUVertexBuffers(renderPass, 0u, bindings, 1u)
    do SDL.DrawGPUPrimitives(renderPass, uint32 dd.Count, 1u, 0u, 0u)
