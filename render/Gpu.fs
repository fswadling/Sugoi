module Gpu

// SDL_gpu device/pass/buffer/pipeline wrappers for the phase-3 mesh path. The
// device and swapchain are created in `Engine.fs`; this module owns the shared
// mesh pipeline (depth-tested, position/normal/uv) and the depth texture that
// the scene pass attaches.

open System
open System.IO
open System.Numerics
open System.Runtime.InteropServices
open SDL3

// SDL_gpu object handles. All are opaque native pointers returned by SDL's
// CreateGPU* functions; the aliases make the signatures self-documenting.
type Device = nativeint
type Shader = nativeint
type Buffer = nativeint
type TransferBuffer = nativeint
type CommandBuffer = nativeint
type CopyPass = nativeint
type Pipeline = nativeint
type Texture = nativeint
type RenderPass = nativeint

// Maximum joint count for the GPU skinning bone palette. Mirrored as BONES_MAX
// in shaders/mesh.vert; a model exceeding this fails at load.
let BonesMax = 205

// ── shaders + buffers ──

let loadShader (device: Device) (stage: SDL.GPUShaderStage) (path: string) (uniformBuffers: uint32) (storageBuffers: uint32) : Shader =
    let code = File.ReadAllBytes(path)
    let mutable info = SDL.GPUShaderCreateInfo()
    info.Format <- SDL.GPUShaderFormat.SPIRV
    info.Stage <- stage
    info.NumSamplers <- 0u
    info.NumStorageTextures <- 0u
    info.NumStorageBuffers <- storageBuffers
    info.NumUniformBuffers <- uniformBuffers
    info.Props <- 0u
    SDL.CreateGPUShader(device, &info, ReadOnlySpan<byte>(code), "main")

let createBuffer (device: Device) (usage: SDL.GPUBufferUsageFlags) (byteSize: uint32) : Buffer =
    let mutable info = SDL.GPUBufferCreateInfo()
    info.Usage <- usage
    info.Size <- byteSize
    info.Props <- 0u
    SDL.CreateGPUBuffer(device, &info)

let createTransferBuffer (device: Device) (byteSize: uint32) : TransferBuffer =
    let mutable info = SDL.GPUTransferBufferCreateInfo()
    info.Usage <- SDL.GPUTransferBufferUsage.Upload
    info.Size <- byteSize
    info.Props <- 0u
    SDL.CreateGPUTransferBuffer(device, &info)

// One-time buffer upload (used for static data like an index buffer): acquires a
// temporary command buffer, copies, submits.
let uploadBufferOnce (device: Device) (dst: Buffer) (data: byte array) : unit =
    let byteSize = uint32 data.Length
    let transfer = createTransferBuffer device byteSize
    let mapped = SDL.MapGPUTransferBuffer(device, transfer, false)
    Marshal.Copy(data, 0, mapped, data.Length)
    SDL.UnmapGPUTransferBuffer(device, transfer)

    let cmd : CommandBuffer = SDL.AcquireGPUCommandBuffer(device)
    let copyPass : CopyPass = SDL.BeginGPUCopyPass(cmd)
    let mutable loc = SDL.GPUTransferBufferLocation()
    loc.TransferBuffer <- transfer
    loc.Offset <- 0u
    let mutable region = SDL.GPUBufferRegion()
    region.Buffer <- dst
    region.Offset <- 0u
    region.Size <- byteSize
    SDL.UploadToGPUBuffer(copyPass, &loc, &region, false)
    SDL.EndGPUCopyPass(copyPass)
    SDL.SubmitGPUCommandBuffer(cmd) |> ignore
    SDL.ReleaseGPUTransferBuffer(device, transfer)

// ── uniform helpers (shared by Mesh + ActorRender) ──

// Write a System.Numerics Matrix4x4's raw floats (M11..M44) at `offset`. The
// raw row-major storage is already the column-major GLSL layout (see DebugDraw).
let writeMatrix (bytes: byte array) (offset: int) (m: Matrix4x4) : unit =
    let s = MemoryMarshal.Cast<byte, float32>(bytes.AsSpan(offset, 64))
    s.[0] <- m.M11; s.[1] <- m.M12; s.[2] <- m.M13; s.[3] <- m.M14
    s.[4] <- m.M21; s.[5] <- m.M22; s.[6] <- m.M23; s.[7] <- m.M24
    s.[8] <- m.M31; s.[9] <- m.M32; s.[10] <- m.M33; s.[11] <- m.M34
    s.[12] <- m.M41; s.[13] <- m.M42; s.[14] <- m.M43; s.[15] <- m.M44

let writeVector4 (bytes: byte array) (offset: int) (v: Vector4) : unit =
    let s = MemoryMarshal.Cast<byte, float32>(bytes.AsSpan(offset, 16))
    s.[0] <- v.X; s.[1] <- v.Y; s.[2] <- v.Z; s.[3] <- v.W

// ── mesh pipeline ──

// Depth-tested triangle pipeline for position/normal/uv/blend vertices with a
// vertex-stage PerDraw uniform block plus the bone-palette storage buffer.
let createMeshPipeline (device: Device) (swapchainFormat: SDL.GPUTextureFormat) (depthFormat: SDL.GPUTextureFormat) : Pipeline =
    let vertexShader = loadShader device SDL.GPUShaderStage.Vertex "shaders/mesh.vert.spv" 1u 1u
    let fragmentShader = loadShader device SDL.GPUShaderStage.Fragment "shaders/mesh.frag.spv" 0u 0u

    let mutable vbufDesc = SDL.GPUVertexBufferDescription()
    vbufDesc.Slot <- 0u
    vbufDesc.Pitch <- 40u
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
    attr1.Format <- SDL.GPUVertexElementFormat.Float3
    attr1.Offset <- 12u

    let mutable attr2 = SDL.GPUVertexAttribute()
    attr2.Location <- 2u
    attr2.BufferSlot <- 0u
    attr2.Format <- SDL.GPUVertexElementFormat.Float2
    attr2.Offset <- 24u

    let mutable attr3 = SDL.GPUVertexAttribute()
    attr3.Location <- 3u
    attr3.BufferSlot <- 0u
    attr3.Format <- SDL.GPUVertexElementFormat.Ubyte4
    attr3.Offset <- 32u

    let mutable attr4 = SDL.GPUVertexAttribute()
    attr4.Location <- 4u
    attr4.BufferSlot <- 0u
    attr4.Format <- SDL.GPUVertexElementFormat.Ubyte4Norm
    attr4.Offset <- 36u

    let vbufDescs = [| vbufDesc |]
    let attrs = [| attr0; attr1; attr2; attr3; attr4 |]

    let mutable raster = SDL.GPURasterizerState()
    raster.FillMode <- SDL.GPUFillMode.Fill
    raster.CullMode <- SDL.GPUCullMode.None
    raster.FrontFace <- SDL.GPUFrontFace.CounterClockwise

    let mutable ms = SDL.GPUMultisampleState()
    ms.SampleCount <- SDL.GPUSampleCount.SampleCount1
    ms.SampleMask <- 0u

    let mutable depth = SDL.GPUDepthStencilState()
    depth.EnableDepthTest <- true
    depth.EnableDepthWrite <- true
    depth.CompareOp <- SDL.GPUCompareOp.LessOrEqual

    let mutable colorTarget = SDL.GPUColorTargetDescription()
    colorTarget.Format <- swapchainFormat
    // BlendState stays default: opaque.

    let colorTargets = [| colorTarget |]

    let mutable info = SDL.GPUGraphicsPipelineCreateInfo()
    info.VertexShader <- vertexShader
    info.FragmentShader <- fragmentShader
    info.PrimitiveType <- SDL.GPUPrimitiveType.TriangleList
    info.RasterizerState <- raster
    info.MultisampleState <- ms
    info.DepthStencilState <- depth

    // Pin the input/description arrays so their native pointers can be stored in
    // the vertex-input/target-info structs (the 2-arg overload is required to
    // set a depth-stencil target).
    let vbufHandle = GCHandle.Alloc(vbufDescs, GCHandleType.Pinned)
    let attrHandle = GCHandle.Alloc(attrs, GCHandleType.Pinned)
    let colorHandle = GCHandle.Alloc(colorTargets, GCHandleType.Pinned)

    let pipeline =
        try
            let mutable vertexInput = SDL.GPUVertexInputState()
            vertexInput.VertexBufferDescriptions <- vbufHandle.AddrOfPinnedObject()
            vertexInput.NumVertexBuffers <- 1u
            vertexInput.VertexAttributes <- attrHandle.AddrOfPinnedObject()
            vertexInput.NumVertexAttributes <- 5u
            info.VertexInputState <- vertexInput

            let mutable targetInfo = SDL.GPUGraphicsPipelineTargetInfo()
            targetInfo.ColorTargetDescriptions <- colorHandle.AddrOfPinnedObject()
            targetInfo.NumColorTargets <- 1u
            targetInfo.DepthStencilFormat <- depthFormat
            targetInfo.HasDepthStencilTarget <- true
            info.TargetInfo <- targetInfo

            SDL.CreateGPUGraphicsPipeline(device, &info)
        finally
            vbufHandle.Free()
            attrHandle.Free()
            colorHandle.Free()

    SDL.ReleaseGPUShader(device, vertexShader)
    SDL.ReleaseGPUShader(device, fragmentShader)
    pipeline

// ── GPU context (pipeline + depth texture) ──

[<Struct>]
type Gpu =
    { Device : Device
      SwapchainFormat : SDL.GPUTextureFormat
      DepthFormat : SDL.GPUTextureFormat
      MeshPipeline : Pipeline
      DepthTexture : Texture
      DepthW : int
      DepthH : int }

let create (device: Device) (swapchainFormat: SDL.GPUTextureFormat) : Gpu =
    let depthFormat = SDL.GPUTextureFormat.D32Float
    { Device = device
      SwapchainFormat = swapchainFormat
      DepthFormat = depthFormat
      MeshPipeline = createMeshPipeline device swapchainFormat depthFormat
      DepthTexture = IntPtr.Zero
      DepthW = 0
      DepthH = 0 }

let private createDepthTexture (device: Device) (w: uint32) (h: uint32) : Texture =
    let mutable info = SDL.GPUTextureCreateInfo()
    info.Type <- SDL.GPUTextureType.TextureType2D
    info.Format <- SDL.GPUTextureFormat.D32Float
    info.Usage <- SDL.GPUTextureUsageFlags.DepthStencilTarget
    info.Width <- w
    info.Height <- h
    info.LayerCountOrDepth <- 1u
    info.NumLevels <- 1u
    info.SampleCount <- SDL.GPUSampleCount.SampleCount1
    SDL.CreateGPUTexture(device, &info)

// Recreate the depth texture when the swapchain size changes (resize).
let ensureDepthTexture (g: Gpu) (w: uint32) (h: uint32) : Gpu =
    if g.DepthTexture <> IntPtr.Zero && g.DepthW = int w && g.DepthH = int h then
        g
    else

    if g.DepthTexture <> IntPtr.Zero then
        SDL.ReleaseGPUTexture(g.Device, g.DepthTexture)
    { g with DepthTexture = createDepthTexture g.Device w h; DepthW = int w; DepthH = int h }

let depthTargetInfo (g: Gpu) : SDL.GPUDepthStencilTargetInfo =
    let mutable t = SDL.GPUDepthStencilTargetInfo()
    t.Texture <- g.DepthTexture
    t.ClearDepth <- 1.0f
    t.LoadOp <- SDL.GPULoadOp.Clear
    t.StoreOp <- SDL.GPUStoreOp.Store
    t.StencilLoadOp <- SDL.GPULoadOp.DontCare
    t.StencilStoreOp <- SDL.GPUStoreOp.DontCare
    t.Cycle <- 0uy
    t.ClearStencil <- 0uy
    t.Layer <- 0uy
    t.MipLevel <- 0uy
    t

let destroy (g: Gpu) : unit =
    if g.MeshPipeline <> IntPtr.Zero then
        SDL.ReleaseGPUGraphicsPipeline(g.Device, g.MeshPipeline)
    if g.DepthTexture <> IntPtr.Zero then
        SDL.ReleaseGPUTexture(g.Device, g.DepthTexture)
