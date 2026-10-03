module Gpu

// SDL_gpu device/pass/buffer/pipeline wrappers for the phase-3 mesh path. The
// device and swapchain are created in `Engine.fs`; this module owns the shared
// mesh pipeline (depth-tested, position/normal/uv) and the depth texture that
// the scene pass attaches.

open System
open System.Diagnostics
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
type Sampler = nativeint
type RenderPass = nativeint

// Maximum joint count for the GPU skinning bone palette. Mirrored as BONES_MAX
// in shaders/mesh.vert; a model exceeding this fails at load.
let BonesMax = 205

// Maximum morph count for the vertex-stage morph weights. Mirrored as MORPHS_MAX in
// shaders/mesh.vert; it is the length of the pushed weights array and the loop's ceiling,
// and an asset exceeding it fails at load (spec 037).
let MorphsMax = 8

// The scene is rendered at this many samples per pixel (spec 042): the multisampled colour and depth
// targets and every scene-pass pipeline share it. The editor overlay draws in its own pass over the
// resolved image and is not multisampled. Fixed — there is no authoring surface (FR-010).
let SampleCount = SDL.GPUSampleCount.SampleCount4

// ── shaders + buffers ──

let loadShader (device: Device) (stage: SDL.GPUShaderStage) (path: string) (uniformBuffers: uint32) (storageBuffers: uint32) (samplers: uint32) : Shader =
    let code = File.ReadAllBytes(path)
    let mutable info = SDL.GPUShaderCreateInfo()
    info.Format <- SDL.GPUShaderFormat.SPIRV
    info.Stage <- stage
    info.NumSamplers <- samplers
    info.NumStorageTextures <- 0u
    info.NumStorageBuffers <- storageBuffers
    info.NumUniformBuffers <- uniformBuffers
    info.Props <- 0u
    SDL.CreateGPUShader(device, &info, ReadOnlySpan<byte>(code), "main")

// ── upload instrumentation (spec 052 FR-008/FR-010) ──
//
// The asset loader resets this before building an asset and reads it after, so the per-load log can
// report where the time goes: `CreateNs`/`Creates` (GPU buffer creation), `StageNs`/`StageBuffers`
// (staging create + map/unmap), `MemcpyNs`/`Copies` (host copies into staging), and
// `SubmitNs`/`Submissions` (command-buffer acquire, copy pass, submit). Process-global and not
// thread-safe: only meaningful while one synchronous load runs.
type UploadStats =
    { mutable CreateNs : int64
      mutable Creates : int
      mutable StageNs : int64
      mutable StageBuffers : int
      mutable MemcpyNs : int64
      mutable Copies : int
      mutable SubmitNs : int64
      mutable Submissions : int }

let mutable private stats =
    { CreateNs = 0L; Creates = 0; StageNs = 0L; StageBuffers = 0; MemcpyNs = 0L; Copies = 0; SubmitNs = 0L; Submissions = 0 }

let resetUploadStats () : unit =
    stats <- { CreateNs = 0L; Creates = 0; StageNs = 0L; StageBuffers = 0; MemcpyNs = 0L; Copies = 0; SubmitNs = 0L; Submissions = 0 }

let uploadStats () : UploadStats = stats
let private tstamp () : int64 = Stopwatch.GetTimestamp()

let createBuffer (device: Device) (usage: SDL.GPUBufferUsageFlags) (byteSize: uint32) : Buffer =
    let mutable info = SDL.GPUBufferCreateInfo()
    info.Usage <- usage
    info.Size <- byteSize
    info.Props <- 0u
    let t0 = tstamp ()
    let buffer = SDL.CreateGPUBuffer(device, &info)
    stats.CreateNs <- stats.CreateNs + (tstamp () - t0)
    stats.Creates <- stats.Creates + 1
    buffer

// Release a GPU buffer, if one was created.
let releaseBuffer (device: Device) (buffer: Buffer) : unit =
    if buffer <> IntPtr.Zero then SDL.ReleaseGPUBuffer(device, buffer)

let createTransferBuffer (device: Device) (byteSize: uint32) : TransferBuffer =
    let mutable info = SDL.GPUTransferBufferCreateInfo()
    info.Usage <- SDL.GPUTransferBufferUsage.Upload
    info.Size <- byteSize
    info.Props <- 0u
    SDL.CreateGPUTransferBuffer(device, &info)

// ── batched uploads (spec 052) ──

// The maximum staging bound. Copies are packed into a staging buffer and flushed (submitted) when
// it fills, so peak staging memory is bounded and submissions scale with flushes, not resources
// (FR-001/FR-004). The first buffer is sized to the workload (up to `InitialStaging`) so a small
// asset does not allocate the maximum; once an asset has flushed, later buffers use this.
[<Literal>]
let StagingCapacity = 256 * 1024 * 1024

// The first staging buffer's size: a small asset stays small (SC-005); a large asset grows to
// `StagingCapacity` after its first flush.
[<Literal>]
let InitialStaging = 4 * 1024 * 1024

// The current staging transfer buffer: mapped once and filled with packed copies until full.
type Staging =
    { Transfer : TransferBuffer
      Capacity : int
      mutable Filled : int
      mutable Mapped : nativeint }

// One recorded copy: its byte offset in the current staging, and its destination.
type Copy =
    | BufferCopy of SrcOffset : int * Dest : Buffer * DestOffset : uint32 * Size : uint32
    | TextureCopy of SrcOffset : int * Tex : Texture * W : uint32 * Y : uint32 * H : uint32

// One asset's uploads. Recording packs copies into the current staging buffer and flushes it (one
// submit) when it fills; the final flush also generates mips. Peak staging is one buffer, not the
// asset's size (research D2, amended).
type UploadBatch =
    { Device : Device
      mutable Staging : Staging option
      Copies : ResizeArray<Copy>
      MipTextures : ResizeArray<Texture>
      mutable StagingAllocs : int }

let beginUpload (device: Device) : UploadBatch =
    { Device = device
      Staging = None
      Copies = ResizeArray()
      MipTextures = ResizeArray()
      StagingAllocs = 0 }

// Copy offsets must be aligned to the format's texel size (4 bytes for RGBA8); pad each reservation.
let private alignUp (n: int) : int = (n + 3) &&& ~~~3

// Release the current staging buffer, if any. Clears `Staging` first so a throwing release cannot
// leave it to be released twice.
let private releaseStaging (batch: UploadBatch) : unit =
    match batch.Staging with
    | None -> ()
    | Some staging ->
        batch.Staging <- None
        let t0 = tstamp ()
        if staging.Mapped <> IntPtr.Zero then
            SDL.UnmapGPUTransferBuffer(batch.Device, staging.Transfer)
        SDL.ReleaseGPUTransferBuffer(batch.Device, staging.Transfer)
        stats.StageNs <- stats.StageNs + (tstamp () - t0)

// Submit the copies recorded so far — and, on the final flush, generate mips — then release the
// staging so the next recording starts fresh. A failure cancels the command buffer.
let private submit (batch: UploadBatch) (final: bool) : unit =
    let hasCopies = batch.Copies.Count > 0
    let hasMips = final && batch.MipTextures.Count > 0
    if hasCopies || hasMips then
        match batch.Staging with
        | Some staging when staging.Mapped <> IntPtr.Zero ->
            SDL.UnmapGPUTransferBuffer(batch.Device, staging.Transfer)
            staging.Mapped <- IntPtr.Zero
        | _ -> ()
        let t0 = tstamp ()
        let cmd = SDL.AcquireGPUCommandBuffer(batch.Device)
        try
            if hasCopies then
                let transfer =
                    match batch.Staging with
                    | Some staging -> staging.Transfer
                    | None -> failwith "gpu: a batch has copies but no staging buffer"
                let copyPass = SDL.BeginGPUCopyPass(cmd)
                for copy in batch.Copies do
                    match copy with
                    | BufferCopy (srcOffset, dst, destOffset, size) ->
                        let mutable loc = SDL.GPUTransferBufferLocation()
                        loc.TransferBuffer <- transfer
                        loc.Offset <- uint32 srcOffset
                        let mutable region = SDL.GPUBufferRegion()
                        region.Buffer <- dst
                        region.Offset <- destOffset
                        region.Size <- size
                        SDL.UploadToGPUBuffer(copyPass, &loc, &region, false)
                    | TextureCopy (srcOffset, tex, w, y, h) ->
                        let mutable transferInfo = SDL.GPUTextureTransferInfo()
                        transferInfo.TransferBuffer <- transfer
                        transferInfo.Offset <- uint32 srcOffset
                        // Read the band as `h` rows of `w` pixels with no padding from `srcOffset`
                        // (explicit rather than relying on the 0-default to the region size).
                        transferInfo.PixelsPerRow <- w
                        transferInfo.RowsPerLayer <- h
                        let mutable region = SDL.GPUTextureRegion()
                        region.Texture <- tex
                        region.MipLevel <- 0u
                        region.Layer <- 0u
                        region.X <- 0u
                        region.Y <- y
                        region.Z <- 0u
                        region.W <- w
                        region.H <- h
                        region.D <- 1u
                        SDL.UploadToGPUTexture(copyPass, &transferInfo, &region, false)
                SDL.EndGPUCopyPass(copyPass)
            if hasMips then
                for tex in batch.MipTextures do SDL.GenerateMipmapsForGPUTexture(cmd, tex)
            SDL.SubmitGPUCommandBuffer(cmd) |> ignore
            stats.SubmitNs <- stats.SubmitNs + (tstamp () - t0)
            stats.Submissions <- stats.Submissions + 1
        with _ ->
            SDL.CancelGPUCommandBuffer(cmd) |> ignore
            reraise ()
    releaseStaging batch
    batch.Copies.Clear()
    if hasMips then batch.MipTextures.Clear()

// Reserve `size` bytes in the current staging buffer, flushing and starting a new one when it does
// not fit. Returns the byte offset. Callers pre-split at `StagingCapacity`, so `need ≤ capacity`
// except for a single texture row larger than the capacity — not reachable with real assets, whose
// row width SDL would reject long before — which is given a buffer of its own (defensive).
let private reserve (batch: UploadBatch) (size: int) : int =
    let need = alignUp size
    let fits =
        match batch.Staging with
        | Some staging -> staging.Filled + need <= staging.Capacity
        | None -> false
    if not fits then
        submit batch false
        // The first buffer is sized to the workload (up to `InitialStaging`) so a small asset stays
        // small; after a flush the asset is known to be large, so later buffers use `StagingCapacity`.
        // A single copy larger than the capacity (a pathological texture row) gets its own buffer.
        let capacity =
            if need > StagingCapacity then need
            elif batch.StagingAllocs = 0 then max need InitialStaging
            else StagingCapacity
        let t0 = tstamp ()
        let transfer = createTransferBuffer batch.Device (uint32 capacity)
        let mapped = SDL.MapGPUTransferBuffer(batch.Device, transfer, false)
        stats.StageNs <- stats.StageNs + (tstamp () - t0)
        stats.StageBuffers <- stats.StageBuffers + 1
        batch.StagingAllocs <- batch.StagingAllocs + 1
        batch.Staging <- Some { Transfer = transfer; Capacity = capacity; Filled = 0; Mapped = mapped }
    let staging = batch.Staging.Value
    let offset = staging.Filled
    staging.Filled <- staging.Filled + need
    offset

// Copy `size` bytes from `data.[dataOffset..]` into the staging at `offset`.
let private write (batch: UploadBatch) (offset: int) (data: byte array) (dataOffset: int) (size: int) : unit =
    let t0 = tstamp ()
    Marshal.Copy(data, dataOffset, IntPtr.Add(batch.Staging.Value.Mapped, offset), size)
    stats.MemcpyNs <- stats.MemcpyNs + (tstamp () - t0)
    stats.Copies <- stats.Copies + 1

// Record a buffer upload, splitting data larger than the staging bound into copies.
let recordBuffer (batch: UploadBatch) (dst: Buffer) (data: byte array) : unit =
    let mutable offset = 0
    while offset < data.Length do
        let size = min StagingCapacity (data.Length - offset)
        let srcOffset = reserve batch size
        write batch srcOffset data offset size
        batch.Copies.Add(BufferCopy(srcOffset, dst, uint32 offset, uint32 size))
        offset <- offset + size

// Record a texture level-0 upload, splitting data larger than the staging bound into whole-row
// bands. `mipLevels > 1` schedules mip generation at the final flush.
let recordTexture (batch: UploadBatch) (tex: Texture) (data: byte array) (width: uint32) (height: uint32) (mipLevels: uint32) : unit =
    let rowBytes = int width * 4
    let rowsPerChunk = max 1 (StagingCapacity / max 1 rowBytes)
    let mutable y = 0u
    while y < height do
        let rows = min (uint32 rowsPerChunk) (height - y)
        let start = int y * rowBytes
        let length = int rows * rowBytes
        let srcOffset = reserve batch length
        write batch srcOffset data start length
        batch.Copies.Add(TextureCopy(srcOffset, tex, width, y, rows))
        y <- y + rows
    if mipLevels > 1u then batch.MipTextures.Add tex

// Finish the batch: flush the remaining copies and generate mips, in one submit.
let finishUpload (batch: UploadBatch) : unit =
    submit batch true

// Abandon a batch: release the staging and drop the records. No GPU work is submitted.
let abortUpload (batch: UploadBatch) : unit =
    releaseStaging batch
    batch.Copies.Clear()
    batch.MipTextures.Clear()

// One-time buffer upload (static data like an index buffer): its own transfer buffer, sized to the
// data (not to `StagingCapacity`), submitted once. A failure cancels the command buffer. Its cost
// is attributed to the same aggregate stats as the batch (this path is not part of an asset build,
// so it does not surface in the per-asset line).
let uploadBufferOnce (device: Device) (dst: Buffer) (data: byte array) : unit =
    let t0 = tstamp ()
    let transfer = createTransferBuffer device (uint32 data.Length)
    let mapped = SDL.MapGPUTransferBuffer(device, transfer, false)
    stats.StageNs <- stats.StageNs + (tstamp () - t0)
    stats.StageBuffers <- stats.StageBuffers + 1
    let t1 = tstamp ()
    Marshal.Copy(data, 0, mapped, data.Length)
    stats.MemcpyNs <- stats.MemcpyNs + (tstamp () - t1)
    stats.Copies <- stats.Copies + 1
    SDL.UnmapGPUTransferBuffer(device, transfer)
    let cmd = SDL.AcquireGPUCommandBuffer(device)
    let t2 = tstamp ()
    try
        let copyPass = SDL.BeginGPUCopyPass(cmd)
        let mutable loc = SDL.GPUTransferBufferLocation()
        loc.TransferBuffer <- transfer
        loc.Offset <- 0u
        let mutable region = SDL.GPUBufferRegion()
        region.Buffer <- dst
        region.Offset <- 0u
        region.Size <- uint32 data.Length
        SDL.UploadToGPUBuffer(copyPass, &loc, &region, false)
        SDL.EndGPUCopyPass(copyPass)
        SDL.SubmitGPUCommandBuffer(cmd) |> ignore
        stats.SubmitNs <- stats.SubmitNs + (tstamp () - t2)
        stats.Submissions <- stats.Submissions + 1
    with _ ->
        SDL.CancelGPUCommandBuffer(cmd) |> ignore
        reraise ()
    let t3 = tstamp ()
    SDL.ReleaseGPUTransferBuffer(device, transfer)
    stats.StageNs <- stats.StageNs + (tstamp () - t3)

// Create a static GraphicsStorageRead buffer and upload `data` into it once — the shape a
// shader reads from a storage buffer (the bone palette, the morph offsets).
let createStorageBuffer (device: Device) (data: byte array) : Buffer =
    let buffer = createBuffer device SDL.GPUBufferUsageFlags.GraphicsStorageRead (uint32 data.Length)
    do uploadBufferOnce device buffer data
    buffer

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

// Inverse-transpose of the model's upper 3x3 (the normal matrix), returned as a
// mat4 so the shader can read `mat3(normalMatrix)`. Falls back to identity when
// the upper 3x3 is singular (degenerate scale).
let normalMatrix (model: Matrix4x4) : Matrix4x4 =
    let m3 =
        Matrix4x4(
            model.M11, model.M12, model.M13, 0.0f,
            model.M21, model.M22, model.M23, 0.0f,
            model.M31, model.M32, model.M33, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f)
    let mutable inv = Matrix4x4.Identity
    if Matrix4x4.Invert(m3, &inv) then Matrix4x4.Transpose(inv) else Matrix4x4.Identity

// ── actor pipelines (cel surface + outline, specs 040/041) ──

// Shared builder for the two actor pipelines: the same 6-attribute, 52-byte vertex layout and the
// `mesh.vert` stage (skinning + morphs + the outline extrusion), differing only in the fragment
// shader, the cull mode and whether depth bias is enabled.
let private createActorPipeline (device: Device) (swapchainFormat: SDL.GPUTextureFormat) (depthFormat: SDL.GPUTextureFormat) (fragmentPath: string) (fragmentUniformBuffers: uint32) (fragmentSamplers: uint32) (cullMode: SDL.GPUCullMode) (useDepthBias: bool) : Pipeline =
    let vertexShader = loadShader device SDL.GPUShaderStage.Vertex "shaders/mesh.vert.spv" 3u 2u 0u
    let fragmentShader = loadShader device SDL.GPUShaderStage.Fragment fragmentPath fragmentUniformBuffers 0u fragmentSamplers

    let mutable vbufDesc = SDL.GPUVertexBufferDescription()
    vbufDesc.Slot <- 0u
    vbufDesc.Pitch <- 52u
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

    // The outline's smoothed extrusion normal (spec 041).
    let mutable attr5 = SDL.GPUVertexAttribute()
    attr5.Location <- 5u
    attr5.BufferSlot <- 0u
    attr5.Format <- SDL.GPUVertexElementFormat.Float3
    attr5.Offset <- 40u

    let vbufDescs = [| vbufDesc |]
    let attrs = [| attr0; attr1; attr2; attr3; attr4; attr5 |]

    let mutable raster = SDL.GPURasterizerState()
    raster.FillMode <- SDL.GPUFillMode.Fill
    raster.CullMode <- cullMode
    // The IQM actor mesh is clockwise-wound: its triangle cross-products oppose the vertex normals
    // (verified on Akane.iqm), so its outward faces are front-facing only under Clockwise. The cel
    // surface does not cull (CullMode.None), so this only steers the outline hull's front-face cull.
    raster.FrontFace <- SDL.GPUFrontFace.Clockwise
    // The outline hull biases its depth back so it cannot bleed through thin/concave geometry
    // (spec 041, FR-007). The cel surface leaves bias off.
    raster.EnableDepthBias <- useDepthBias
    if useDepthBias then
        raster.DepthBiasConstantFactor <- 1.0f
        raster.DepthBiasSlopeFactor <- 1.0f
        raster.DepthBiasClamp <- 0.0f

    let mutable ms = SDL.GPUMultisampleState()
    ms.SampleCount <- SampleCount
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
            vertexInput.NumVertexAttributes <- 6u
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

// The cel-shaded surface: double-sided (the model relies on it), no depth bias.
let createCelPipeline (device: Device) (swapchainFormat: SDL.GPUTextureFormat) (depthFormat: SDL.GPUTextureFormat) : Pipeline =
    createActorPipeline device swapchainFormat depthFormat "shaders/cel.frag.spv" 1u 1u SDL.GPUCullMode.None false

// The outline hull: front faces culled so only the extruded back faces show, depth-biased back.
let createOutlinePipeline (device: Device) (swapchainFormat: SDL.GPUTextureFormat) (depthFormat: SDL.GPUTextureFormat) : Pipeline =
    createActorPipeline device swapchainFormat depthFormat "shaders/outline.frag.spv" 0u 1u SDL.GPUCullMode.Front true

// Depth-tested triangle pipeline for static position/normal/uv geometry (no
// blend attributes, no bone storage buffer). Used by environments and props,
// which must not pay the skinning cost of the actor mesh pipeline.
let createStaticPipeline (device: Device) (swapchainFormat: SDL.GPUTextureFormat) (depthFormat: SDL.GPUTextureFormat) : Pipeline =
    let vertexShader = loadShader device SDL.GPUShaderStage.Vertex "shaders/static.vert.spv" 1u 0u 0u
    let fragmentShader = loadShader device SDL.GPUShaderStage.Fragment "shaders/static.frag.spv" 1u 0u 4u

    let mutable vbufDesc = SDL.GPUVertexBufferDescription()
    vbufDesc.Slot <- 0u
    vbufDesc.Pitch <- 32u
    vbufDesc.InputRate <- SDL.GPUVertexInputRate.Vertex
    vbufDesc.InstanceStepRate <- 0u

    // Instance transform stream (slot 1): one 112-byte record per instance — four
    // model rows then three normal-matrix rows, all Float4 (see shaders/static.vert
    // and render/InstanceBuffer.fs). InputRate.Instance advances one record per
    // instance; SDL_gpu requires instance_step_rate to be 0.
    let mutable instanceDesc = SDL.GPUVertexBufferDescription()
    instanceDesc.Slot <- 1u
    instanceDesc.Pitch <- 112u
    instanceDesc.InputRate <- SDL.GPUVertexInputRate.Instance
    instanceDesc.InstanceStepRate <- 0u

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

    let instanceAttr (location: uint32) (offset: uint32) : SDL.GPUVertexAttribute =
        let mutable a = SDL.GPUVertexAttribute()
        a.Location <- location
        a.BufferSlot <- 1u
        a.Format <- SDL.GPUVertexElementFormat.Float4
        a.Offset <- offset
        a

    let vbufDescs = [| vbufDesc; instanceDesc |]
    let attrs =
        [| attr0; attr1; attr2
           instanceAttr 3u 0u; instanceAttr 4u 16u; instanceAttr 5u 32u
           instanceAttr 6u 48u; instanceAttr 7u 64u; instanceAttr 8u 80u
           instanceAttr 9u 96u |]

    let mutable raster = SDL.GPURasterizerState()
    raster.FillMode <- SDL.GPUFillMode.Fill
    raster.CullMode <- SDL.GPUCullMode.None
    raster.FrontFace <- SDL.GPUFrontFace.CounterClockwise

    let mutable ms = SDL.GPUMultisampleState()
    ms.SampleCount <- SampleCount
    ms.SampleMask <- 0u

    let mutable depth = SDL.GPUDepthStencilState()
    depth.EnableDepthTest <- true
    depth.EnableDepthWrite <- true
    depth.CompareOp <- SDL.GPUCompareOp.LessOrEqual

    let mutable colorTarget = SDL.GPUColorTargetDescription()
    colorTarget.Format <- swapchainFormat

    let colorTargets = [| colorTarget |]

    let mutable info = SDL.GPUGraphicsPipelineCreateInfo()
    info.VertexShader <- vertexShader
    info.FragmentShader <- fragmentShader
    info.PrimitiveType <- SDL.GPUPrimitiveType.TriangleList
    info.RasterizerState <- raster
    info.MultisampleState <- ms
    info.DepthStencilState <- depth

    let vbufHandle = GCHandle.Alloc(vbufDescs, GCHandleType.Pinned)
    let attrHandle = GCHandle.Alloc(attrs, GCHandleType.Pinned)
    let colorHandle = GCHandle.Alloc(colorTargets, GCHandleType.Pinned)

    let pipeline =
        try
            let mutable vertexInput = SDL.GPUVertexInputState()
            vertexInput.VertexBufferDescriptions <- vbufHandle.AddrOfPinnedObject()
            vertexInput.NumVertexBuffers <- 2u
            vertexInput.VertexAttributes <- attrHandle.AddrOfPinnedObject()
            vertexInput.NumVertexAttributes <- 10u
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

    do SDL.ReleaseGPUShader(device, vertexShader)
    do SDL.ReleaseGPUShader(device, fragmentShader)
    pipeline

// ── GPU context (pipeline + depth texture) ──

[<Struct>]
type Gpu =
    { Device : Device
      SwapchainFormat : SDL.GPUTextureFormat
      DepthFormat : SDL.GPUTextureFormat
      CelPipeline : Pipeline
      OutlinePipeline : Pipeline
      StaticPipeline : Pipeline
      mutable MsaaTexture : Texture
      mutable DepthTexture : Texture
      mutable DepthW : int
      mutable DepthH : int }

let create (device: Device) (swapchainFormat: SDL.GPUTextureFormat) : Gpu =
    let depthFormat = SDL.GPUTextureFormat.D32Float
    // Fail loudly if the scene cannot be multisampled (spec 042, FR-009): both the colour and the depth
    // format must support the fixed sample count. There is no silent single-sample fallback.
    if not (SDL.GPUTextureSupportsSampleCount(device, swapchainFormat, SampleCount)) then
        failwithf "gpu: swapchain format %A does not support %A multisampling (spec 042)" swapchainFormat SampleCount
    if not (SDL.GPUTextureSupportsSampleCount(device, depthFormat, SampleCount)) then
        failwithf "gpu: depth format %A does not support %A multisampling (spec 042)" depthFormat SampleCount
    { Device = device
      SwapchainFormat = swapchainFormat
      DepthFormat = depthFormat
      CelPipeline = createCelPipeline device swapchainFormat depthFormat
      OutlinePipeline = createOutlinePipeline device swapchainFormat depthFormat
      StaticPipeline = createStaticPipeline device swapchainFormat depthFormat
      MsaaTexture = IntPtr.Zero
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
    info.SampleCount <- SampleCount
    SDL.CreateGPUTexture(device, &info)

// The multisampled colour target the scene renders into (spec 042); the engine resolves it into the
// acquired swapchain texture. Same format as the swapchain, so the resolve is a plain sample-average.
let private createMsaaTexture (device: Device) (format: SDL.GPUTextureFormat) (w: uint32) (h: uint32) : Texture =
    let mutable info = SDL.GPUTextureCreateInfo()
    info.Type <- SDL.GPUTextureType.TextureType2D
    info.Format <- format
    info.Usage <- SDL.GPUTextureUsageFlags.ColorTarget
    info.Width <- w
    info.Height <- h
    info.LayerCountOrDepth <- 1u
    info.NumLevels <- 1u
    info.SampleCount <- SampleCount
    SDL.CreateGPUTexture(device, &info)

// Recreate the multisampled colour and depth targets when the swapchain size changes (resize). Mutates
// the context's mutable fields in place through a byref (runs every frame). Both are `SampleCount`×.
let ensureSceneTargets (g: byref<Gpu>) (w: uint32) (h: uint32) : unit =
    if g.MsaaTexture <> IntPtr.Zero && g.DepthW = int w && g.DepthH = int h then
        ()
    else

    if g.MsaaTexture <> IntPtr.Zero then
        do SDL.ReleaseGPUTexture(g.Device, g.MsaaTexture)
    if g.DepthTexture <> IntPtr.Zero then
        do SDL.ReleaseGPUTexture(g.Device, g.DepthTexture)
    g.MsaaTexture <- createMsaaTexture g.Device g.SwapchainFormat w h
    g.DepthTexture <- createDepthTexture g.Device w h
    if g.MsaaTexture = IntPtr.Zero || g.DepthTexture = IntPtr.Zero then
        failwithf "gpu: failed to create the %A scene targets at %ux%u (spec 042)" SampleCount w h
    g.DepthW <- int w
    g.DepthH <- int h

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
    if g.CelPipeline <> IntPtr.Zero then
        do SDL.ReleaseGPUGraphicsPipeline(g.Device, g.CelPipeline)
    if g.OutlinePipeline <> IntPtr.Zero then
        do SDL.ReleaseGPUGraphicsPipeline(g.Device, g.OutlinePipeline)
    if g.StaticPipeline <> IntPtr.Zero then
        do SDL.ReleaseGPUGraphicsPipeline(g.Device, g.StaticPipeline)
    if g.MsaaTexture <> IntPtr.Zero then
        do SDL.ReleaseGPUTexture(g.Device, g.MsaaTexture)
    if g.DepthTexture <> IntPtr.Zero then
        do SDL.ReleaseGPUTexture(g.Device, g.DepthTexture)
