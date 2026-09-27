module Ui

// The game's own screen-space 2D layer: one blended, depth-less pipeline; one grow-only
// indexed quad batch in reused arrays; and text via SDL_ttf's GPU text engine. The
// dialogue panel (spec 038) is its first consumer, but it knows nothing of characters,
// expressions or dialogue — only `Gpu`, `Texture` and the font — so a HUD or an interact
// prompt can reuse it unchanged (research Decision 7).
//
// Vertices are pixels with the origin at the top-left and y downwards; the pushed ortho
// matrix maps them to clip space. 32 bytes per vertex (x, y, u, v, r, g, b, a — colour
// written as `Float4` rather than the design's compact `Ubyte4Norm` for clarity when the
// batch is built by hand each frame; the batch is a few hundred vertices).
//
// Text is laid out through a **small pool of slots** rather than one shared string, so a
// frame that draws two independent strings (a name plate and a line) does not make the
// glyph geometry rebuild every frame: each slot caches by its own source and wrap width.
//
// Nothing allocates per frame: the batch arrays, the staging bytes and the uniform bytes
// are reused, and a slot's glyph geometry is rebuilt only when its line or wrap width
// changes (FR-010, SC-007). SDL_ttf's draw data is a pointer-linked list into the library's
// own memory, so it is walked **once per rebuild**, never per frame.

open System
open System.IO
open System.Numerics
open System.Runtime.InteropServices
open SDL3

/// A top-left-origin pixel rectangle — the shape the panel layout arithmetic produces.
[<Struct>]
type Rect = { X : float32; Y : float32; W : float32; H : float32 }

/// One contiguous indexed range drawn with one texture and sampler. The batch records
/// these and flushes nothing until drawing, so a panel is about three draws.
[<Struct>]
type Run =
    { Texture : Gpu.Texture
      Sampler : Gpu.Sampler
      FirstIndex : int
      IndexCount : int }

/// One font-atlas sequence of a line: which texture, and where its indices live in the
/// copied geometry. The engine can split a line across several atlases, so a line is one
/// or more of these. The vertices are copied whole, so only the index window is needed.
[<Struct>]
type TextRun =
    { Texture : Gpu.Texture
      FirstIndex : int
      IndexCount : int }

/// A line's laid-out glyphs, copied out of SDL_ttf once. `Vertices` is 4 floats per vertex
/// in the engine's own space (pixels, origin at the text's upper-left corner, y upwards);
/// `text` translates it into the batch.
type TextGeometry =
    { Source : string
      WrapWidth : int
      VertexCount : int
      IndexCount : int
      Vertices : float32 array
      Indices : uint32 array
      Runs : TextRun array }

let private emptyGeometry : TextGeometry =
    { Source = ""
      WrapWidth = 0
      VertexCount = 0
      IndexCount = 0
      Vertices = Array.empty
      Indices = Array.empty
      Runs = Array.empty }

// ── capacities ──

let private floatsPerVertex = 8
let private bytesPerVertex = floatsPerVertex * 4
let private vertexCapacity = 16384
let private indexCapacity = 24576
let private runCapacity = 16
/// How many independent strings a single frame can hold without thrashing the glyph cache: a
/// dialogue's name plate, its line, and the interact prompt (spec 039). One slot per kind of string
/// the layer draws — the prompt and a line are mutually exclusive today, but the capacity must not
/// depend on that.
let private textSlots = 3

/// The glyph size for a viewport: a fixed fraction of its height, so the panel — itself a
/// fraction of the viewport — and its text scale together (research Decision 9). The size the
/// font is opened at before the first frame knows the real viewport.
let private fontSizeFor (viewportH: float32) : int =
    max 8 (int (viewportH * 0.033f + 0.5f))
let private initialFontSize = fontSizeFor 720.0f

// Opaque SDL_ttf handles, aliased so the struct's fields and the layer's signatures say what
// they are — the same courtesy `Gpu` pays the SDL_gpu handles. They are `TTF_Font`,
// `TTF_TextEngine` and `TTF_Text` respectively.
type Font = nativeint
type TextEngine = nativeint
type TextObject = nativeint

[<Struct>]
type Ui =
    { Device : Gpu.Device
      Pipeline : Gpu.Pipeline
      VertexShader : Gpu.Shader
      FragmentShader : Gpu.Shader
      VertexBuffer : Gpu.Buffer
      IndexBuffer : Gpu.Buffer
      VertexTransfer : Gpu.TransferBuffer
      IndexTransfer : Gpu.TransferBuffer
      /// Clamp-to-edge, linear: the glyph atlas must not bleed one glyph into its neighbour.
      Sampler : Gpu.Sampler
      /// The 1x1 white placeholder the panel and the name plate are drawn with.
      White : Texture.Texture
      Font : Font
      TextEngine : TextEngine
      /// The size in pixels the font is currently set to; a viewport change re-sizes it only
      /// when the derived size actually differs, which also drops the slots' cached geometry.
      mutable FontSize : int
      TextObjects : TextObject array
      TextGeoms : TextGeometry array
      TextW : int array
      TextH : int array
      Vertices : float32 array
      Indices : uint32 array
      VertexBytes : byte array
      IndexBytes : byte array
      Runs : Run array
      mutable RunCount : int
      mutable VertexCount : int
      mutable IndexCount : int
      UniformBytes : byte array
      VertexBindings : SDL.GPUBufferBinding array
      SamplerBindings : SDL.GPUTextureSamplerBinding array }

// ── construction ──

let private loadShader (device: Gpu.Device) (stage: SDL.GPUShaderStage) (path: string) (uniformBuffers: uint32) (samplers: uint32) : Gpu.Shader =
    let code = File.ReadAllBytes(path)
    let mutable info = SDL.GPUShaderCreateInfo()
    info.Format <- SDL.GPUShaderFormat.SPIRV
    info.Stage <- stage
    info.NumSamplers <- samplers
    info.NumStorageTextures <- 0u
    info.NumStorageBuffers <- 0u
    info.NumUniformBuffers <- uniformBuffers
    info.Props <- 0u
    SDL.CreateGPUShader(device, &info, ReadOnlySpan<byte>(code), "main")

let private createSampler (device: Gpu.Device) : Gpu.Sampler =
    let mutable info = SDL.GPUSamplerCreateInfo()
    info.MinFilter <- SDL.GPUFilter.Linear
    info.MagFilter <- SDL.GPUFilter.Linear
    info.MipmapMode <- SDL.GPUSamplerMipmapMode.Linear
    info.AddressModeU <- SDL.GPUSamplerAddressMode.ClampToEdge
    info.AddressModeV <- SDL.GPUSamplerAddressMode.ClampToEdge
    info.AddressModeW <- SDL.GPUSamplerAddressMode.ClampToEdge
    SDL.CreateGPUSampler(device, &info)

/// Create the layer: the pipeline, the batch buffers, the glyph-atlas sampler, and the font
/// and text engine (opened once). Fails loudly if the font or the text engine will not
/// initialize (research Decision 9).
let create (device: Gpu.Device) (swapchainFormat: SDL.GPUTextureFormat) (depthFormat: SDL.GPUTextureFormat) (fontPath: string) : Ui =
    // ── SDL_ttf ──
    if not (TTF.Init()) then
        failwithf "ui: TTF_Init failed: %s" (SDL.GetError())

    // ── shaders ──
    let vertexShader = loadShader device SDL.GPUShaderStage.Vertex "shaders/ui.vert.spv" 1u 0u
    let fragmentShader = loadShader device SDL.GPUShaderStage.Fragment "shaders/ui.frag.spv" 0u 1u

    // ── vertex input layout ──
    let mutable vertexBufferDescription = SDL.GPUVertexBufferDescription()
    vertexBufferDescription.Slot <- 0u
    vertexBufferDescription.Pitch <- uint32 bytesPerVertex
    vertexBufferDescription.InputRate <- SDL.GPUVertexInputRate.Vertex
    vertexBufferDescription.InstanceStepRate <- 0u

    let mutable positionAttribute = SDL.GPUVertexAttribute()
    positionAttribute.Location <- 0u
    positionAttribute.BufferSlot <- 0u
    positionAttribute.Format <- SDL.GPUVertexElementFormat.Float2
    positionAttribute.Offset <- 0u

    let mutable uvAttribute = SDL.GPUVertexAttribute()
    uvAttribute.Location <- 1u
    uvAttribute.BufferSlot <- 0u
    uvAttribute.Format <- SDL.GPUVertexElementFormat.Float2
    uvAttribute.Offset <- 8u

    let mutable colourAttribute = SDL.GPUVertexAttribute()
    colourAttribute.Location <- 2u
    colourAttribute.BufferSlot <- 0u
    colourAttribute.Format <- SDL.GPUVertexElementFormat.Float4
    colourAttribute.Offset <- 16u

    // ── pipeline state ──
    let mutable raster = SDL.GPURasterizerState()
    raster.FillMode <- SDL.GPUFillMode.Fill
    raster.CullMode <- SDL.GPUCullMode.None
    raster.FrontFace <- SDL.GPUFrontFace.CounterClockwise

    let mutable multisample = SDL.GPUMultisampleState()
    multisample.SampleCount <- SDL.GPUSampleCount.SampleCount1
    multisample.SampleMask <- 0u

    // The one genuinely new pipeline state for the game's UI: alpha blending. Depth test is
    // off, so the layer is never occluded by scene geometry (FR-011).
    let mutable blend = SDL.GPUColorTargetBlendState()
    blend.EnableBlend <- true
    blend.SrcColorBlendFactor <- SDL.GPUBlendFactor.SrcAlpha
    blend.DstColorBlendFactor <- SDL.GPUBlendFactor.OneMinusSrcAlpha
    blend.ColorBlendOp <- SDL.GPUBlendOp.Add
    blend.SrcAlphaBlendFactor <- SDL.GPUBlendFactor.SrcAlpha
    blend.DstAlphaBlendFactor <- SDL.GPUBlendFactor.OneMinusSrcAlpha
    blend.AlphaBlendOp <- SDL.GPUBlendOp.Add

    let mutable colorTarget = SDL.GPUColorTargetDescription()
    colorTarget.Format <- swapchainFormat
    colorTarget.BlendState <- blend

    // ── the graphics pipeline ──
    let vertexBufferDescriptions = [| vertexBufferDescription |]
    let attributes = [| positionAttribute; uvAttribute; colourAttribute |]
    let colorTargets = [| colorTarget |]

    let mutable info = SDL.GPUGraphicsPipelineCreateInfo()
    info.VertexShader <- vertexShader
    info.FragmentShader <- fragmentShader
    info.PrimitiveType <- SDL.GPUPrimitiveType.TriangleList
    info.RasterizerState <- raster
    info.MultisampleState <- multisample

    let vertexBufferHandle = GCHandle.Alloc(vertexBufferDescriptions, GCHandleType.Pinned)
    let attributeHandle = GCHandle.Alloc(attributes, GCHandleType.Pinned)
    let colorHandle = GCHandle.Alloc(colorTargets, GCHandleType.Pinned)

    let pipeline =
        try
            let mutable vertexInput = SDL.GPUVertexInputState()
            vertexInput.VertexBufferDescriptions <- vertexBufferHandle.AddrOfPinnedObject()
            vertexInput.NumVertexBuffers <- 1u
            vertexInput.VertexAttributes <- attributeHandle.AddrOfPinnedObject()
            vertexInput.NumVertexAttributes <- 3u
            info.VertexInputState <- vertexInput

            let mutable targetInfo = SDL.GPUGraphicsPipelineTargetInfo()
            targetInfo.ColorTargetDescriptions <- colorHandle.AddrOfPinnedObject()
            targetInfo.NumColorTargets <- 1u
            targetInfo.DepthStencilFormat <- depthFormat
            targetInfo.HasDepthStencilTarget <- false
            info.TargetInfo <- targetInfo

            SDL.CreateGPUGraphicsPipeline(device, &info)
        finally
            vertexBufferHandle.Free()
            attributeHandle.Free()
            colorHandle.Free()

    if pipeline = IntPtr.Zero then
        failwithf "ui: failed to create the 2D pipeline: %s" (SDL.GetError())

    // ── the batch buffers ──
    let vertexBuffer = Gpu.createBuffer device SDL.GPUBufferUsageFlags.Vertex (uint32 (vertexCapacity * bytesPerVertex))
    let indexBuffer = Gpu.createBuffer device SDL.GPUBufferUsageFlags.Index (uint32 (indexCapacity * 4))
    let vertexTransfer = Gpu.createTransferBuffer device (uint32 (vertexCapacity * bytesPerVertex))
    let indexTransfer = Gpu.createTransferBuffer device (uint32 (indexCapacity * 4))

    // ── font, text engine and text slots ──
    let font = TTF.OpenFont(fontPath, float32 initialFontSize)
    if font = IntPtr.Zero then
        failwithf "ui: failed to open the font '%s': %s" fontPath (SDL.GetError())

    let textEngine = TTF.CreateGPUTextEngine(device)
    if textEngine = IntPtr.Zero then
        failwithf "ui: failed to create the GPU text engine: %s" (SDL.GetError())

    let textObjects =
        Array.init textSlots (fun _ ->
            let textObject = TTF.CreateText(textEngine, font, "", UIntPtr.Zero)
            if textObject = IntPtr.Zero then failwithf "ui: failed to create a text object: %s" (SDL.GetError())
            textObject)

    { Device = device
      Pipeline = pipeline
      VertexShader = vertexShader
      FragmentShader = fragmentShader
      VertexBuffer = vertexBuffer
      IndexBuffer = indexBuffer
      VertexTransfer = vertexTransfer
      IndexTransfer = indexTransfer
      Sampler = createSampler device
      White = Texture.solid device 255uy 255uy 255uy 255uy
      Font = font
      TextEngine = textEngine
      FontSize = initialFontSize
      TextObjects = textObjects
      TextGeoms = Array.create textSlots emptyGeometry
      TextW = Array.zeroCreate textSlots
      TextH = Array.zeroCreate textSlots
      Vertices = Array.zeroCreate<float32> (vertexCapacity * floatsPerVertex)
      Indices = Array.zeroCreate<uint32> indexCapacity
      VertexBytes = Array.zeroCreate<byte> (vertexCapacity * bytesPerVertex)
      IndexBytes = Array.zeroCreate<byte> (indexCapacity * 4)
      Runs = Array.zeroCreate<Run> runCapacity
      RunCount = 0
      VertexCount = 0
      IndexCount = 0
      UniformBytes = Array.zeroCreate<byte> 64
      VertexBindings = Array.zeroCreate<SDL.GPUBufferBinding> 1
      SamplerBindings = Array.zeroCreate<SDL.GPUTextureSamplerBinding> 1 }

// ── line measurement + glyph geometry (rebuilt once per line change, per slot) ──

let private rebuild (ui: byref<Ui>) (slot: int) (textObject: TextObject) (text: string) (wrapWidth: int) : unit =
    do TTF.SetTextString(textObject, text, UIntPtr.Zero) |> ignore
    do TTF.SetTextWrapWidth(textObject, wrapWidth) |> ignore
    do TTF.SetTextPosition(textObject, 0, 0) |> ignore

    let mutable width = 0
    let mutable height = 0
    if not (TTF.GetTextSize(textObject, &width, &height)) then
        failwithf "ui: failed to measure text: %s" (SDL.GetError())

    // First walk: count the non-empty sequences and their totals.
    let mutable totalVertices = 0
    let mutable totalIndices = 0
    let mutable sequenceCount = 0
    let mutable node = TTF.GetGPUTextDrawData(textObject)
    while node <> IntPtr.Zero do
        let sequence = Marshal.PtrToStructure<TTF.GPUAtlasDrawSequence>(node)
        if sequence.NumVertices > 0 then
            totalVertices <- totalVertices + sequence.NumVertices
            totalIndices <- totalIndices + sequence.NumIndices
            sequenceCount <- sequenceCount + 1
        node <- sequence.Next

    let vertices = Array.zeroCreate<float32> (totalVertices * 4)
    let indices = Array.zeroCreate<uint32> totalIndices
    let runs = Array.zeroCreate<TextRun> sequenceCount

    // Second walk: fill. The engine returns xy/uv interleaved per sequence and indices
    // 0-based within it; both are copied into the layer's own contiguous arrays.
    let mutable runIndex = 0
    let mutable vertexBase = 0
    let mutable indexBase = 0
    let mutable node = TTF.GetGPUTextDrawData(textObject)
    while node <> IntPtr.Zero do
        let sequence = Marshal.PtrToStructure<TTF.GPUAtlasDrawSequence>(node)
        if sequence.NumVertices > 0 then
            let count = sequence.NumVertices
            let xy = Array.zeroCreate<float32> (count * 2)
            let uv = Array.zeroCreate<float32> (count * 2)
            do Marshal.Copy(sequence.XY, xy, 0, count * 2)
            do Marshal.Copy(sequence.UV, uv, 0, count * 2)
            for vertex in 0 .. count - 1 do
                vertices.[(vertexBase + vertex) * 4] <- xy.[vertex * 2]
                vertices.[(vertexBase + vertex) * 4 + 1] <- xy.[vertex * 2 + 1]
                vertices.[(vertexBase + vertex) * 4 + 2] <- uv.[vertex * 2]
                vertices.[(vertexBase + vertex) * 4 + 3] <- uv.[vertex * 2 + 1]
            let sequenceIndices = Array.zeroCreate<int> sequence.NumIndices
            do Marshal.Copy(sequence.Indices, sequenceIndices, 0, sequence.NumIndices)
            for index in 0 .. sequence.NumIndices - 1 do
                indices.[indexBase + index] <- uint32 (sequenceIndices.[index] + vertexBase)
            runs.[runIndex] <- { Texture = sequence.AtlasTexture; FirstIndex = indexBase; IndexCount = sequence.NumIndices }
            runIndex <- runIndex + 1
            vertexBase <- vertexBase + count
            indexBase <- indexBase + sequence.NumIndices
        node <- sequence.Next

    ui.TextGeoms.[slot] <- { Source = text; WrapWidth = wrapWidth; VertexCount = totalVertices; IndexCount = totalIndices; Vertices = vertices; Indices = indices; Runs = runs }
    ui.TextW.[slot] <- width
    ui.TextH.[slot] <- height

/// Lay out `text` in `slot`, wrapped to `wrapWidth` pixels, and cache its glyph geometry.
/// A no-op when that slot's line and wrap width are unchanged, so its geometry is rebuilt
/// only on a line change. Returns the measured (width, height) at that wrap width — the one
/// impure step the panel layout needs.
let setLine (ui: byref<Ui>) (slot: int) (text: string) (wrapWidth: int) : int * int =
    if ui.TextGeoms.[slot].Source <> text || ui.TextGeoms.[slot].WrapWidth <> wrapWidth then
        rebuild &ui slot ui.TextObjects.[slot] text wrapWidth
    (ui.TextW.[slot], ui.TextH.[slot])

// ── CPU batch ──

let beginBatch (ui: byref<Ui>) (viewportW: float32) (viewportH: float32) : unit =
    ui.VertexCount <- 0
    ui.IndexCount <- 0
    ui.RunCount <- 0

    // ── the font size follows the viewport, so text scales with the panel (Decision 9) ──
    // `TTF_SetFontSize` keeps the one open font but re-rasterises its glyphs, so a change
    // invalidates every slot's cached geometry — as well as the atlas handles in its runs —
    // and the slots must be rebuilt at the new size. Guarded on the viewport having a height,
    // so a minimised window does not churn the font.
    let desired = fontSizeFor viewportH
    if viewportH > 0.0f && desired <> ui.FontSize then
        if not (TTF.SetFontSize(ui.Font, float32 desired)) then
            failwithf "ui: failed to set the font to %d px: %s" desired (SDL.GetError())
        ui.FontSize <- desired
        for slot in 0 .. textSlots - 1 do
            ui.TextGeoms.[slot] <- { ui.TextGeoms.[slot] with Source = null }

    // Pixels (origin top-left, y down) -> clip space. Raw row-major floats are already the
    // column-major GLSL layout for `u.ortho * v` (see `DebugDraw`).
    let ortho =
        Matrix4x4(
            2.0f / viewportW, 0.0f, 0.0f, 0.0f,
            0.0f, -2.0f / viewportH, 0.0f, 0.0f,
            0.0f, 0.0f, 1.0f, 0.0f,
            -1.0f, 1.0f, 0.0f, 1.0f)
    do Gpu.writeMatrix ui.UniformBytes 0 ortho

let private ensureRun (ui: byref<Ui>) (texture: Gpu.Texture) (sampler: Gpu.Sampler) (firstIndex: int) (count: int) : unit =
    if count > 0 then
        if ui.RunCount > 0 && ui.Runs.[ui.RunCount - 1].Texture = texture && ui.Runs.[ui.RunCount - 1].Sampler = sampler then
            ui.Runs.[ui.RunCount - 1] <- { ui.Runs.[ui.RunCount - 1] with IndexCount = ui.Runs.[ui.RunCount - 1].IndexCount + count }
        else
            if ui.RunCount >= runCapacity then invalidOp "Ui: too many texture runs in one frame"
            ui.Runs.[ui.RunCount] <- { Texture = texture; Sampler = sampler; FirstIndex = firstIndex; IndexCount = count }
            ui.RunCount <- ui.RunCount + 1

let private addQuad (ui: byref<Ui>) (rect: Rect) (texture: Gpu.Texture) (sampler: Gpu.Sampler) (colour: Vector4) : unit =
    if ui.VertexCount + 4 > vertexCapacity then invalidOp "Ui: vertex capacity exceeded" else
    let baseVertex = ui.VertexCount
    let baseIndex = ui.IndexCount
    let vertices = ui.Vertices
    let putVertex (corner: int) (x: float32) (y: float32) (texU: float32) (texV: float32) : unit =
        let offset = (baseVertex + corner) * floatsPerVertex
        vertices.[offset] <- x
        vertices.[offset + 1] <- y
        vertices.[offset + 2] <- texU
        vertices.[offset + 3] <- texV
        vertices.[offset + 4] <- colour.X
        vertices.[offset + 5] <- colour.Y
        vertices.[offset + 6] <- colour.Z
        vertices.[offset + 7] <- colour.W
    let left = rect.X
    let top = rect.Y
    let right = rect.X + rect.W
    let bottom = rect.Y + rect.H
    do putVertex 0 left top 0.0f 0.0f
    do putVertex 1 right top 1.0f 0.0f
    do putVertex 2 right bottom 1.0f 1.0f
    do putVertex 3 left bottom 0.0f 1.0f
    ui.Indices.[baseIndex] <- uint32 baseVertex
    ui.Indices.[baseIndex + 1] <- uint32 (baseVertex + 1)
    ui.Indices.[baseIndex + 2] <- uint32 (baseVertex + 2)
    ui.Indices.[baseIndex + 3] <- uint32 baseVertex
    ui.Indices.[baseIndex + 4] <- uint32 (baseVertex + 2)
    ui.Indices.[baseIndex + 5] <- uint32 (baseVertex + 3)
    ui.VertexCount <- baseVertex + 4
    ui.IndexCount <- baseIndex + 6
    ensureRun &ui texture sampler baseIndex 6

/// A solid (or tinted) rectangle, drawn with the shared white placeholder.
let rect (ui: byref<Ui>) (rect: Rect) (colour: Vector4) : unit =
    addQuad &ui rect ui.White.Handle ui.White.Sampler colour

/// A textured rectangle — a portrait. Tinted by `colour` (white draws it unchanged).
let image (ui: byref<Ui>) (rect: Rect) (texture: Texture.Texture) (colour: Vector4) : unit =
    addQuad &ui rect texture.Handle texture.Sampler colour

/// Append a laid-out line's glyphs, translated so the text's upper-left corner sits at
/// `rect.X, rect.Y`. The engine's y axis points up, so it is flipped here.
let text (ui: byref<Ui>) (rect: Rect) (geom: TextGeometry) (colour: Vector4) : unit =
    if geom.IndexCount = 0 then () else
    if ui.VertexCount + geom.VertexCount > vertexCapacity || ui.IndexCount + geom.IndexCount > indexCapacity then
        invalidOp "Ui: vertex capacity exceeded by the text geometry"
    else

    let baseVertex = ui.VertexCount
    for vertex in 0 .. geom.VertexCount - 1 do
        let offset = (baseVertex + vertex) * floatsPerVertex
        let srcOffset = vertex * 4
        ui.Vertices.[offset] <- rect.X + geom.Vertices.[srcOffset]
        ui.Vertices.[offset + 1] <- rect.Y - geom.Vertices.[srcOffset + 1]
        ui.Vertices.[offset + 2] <- geom.Vertices.[srcOffset + 2]
        ui.Vertices.[offset + 3] <- geom.Vertices.[srcOffset + 3]
        ui.Vertices.[offset + 4] <- colour.X
        ui.Vertices.[offset + 5] <- colour.Y
        ui.Vertices.[offset + 6] <- colour.Z
        ui.Vertices.[offset + 7] <- colour.W

    let baseIndex = ui.IndexCount
    for index in 0 .. geom.IndexCount - 1 do
        ui.Indices.[baseIndex + index] <- uint32 baseVertex + geom.Indices.[index]

    ui.VertexCount <- baseVertex + geom.VertexCount
    ui.IndexCount <- baseIndex + geom.IndexCount
    for run in geom.Runs do
        ensureRun &ui run.Texture ui.Sampler (baseIndex + run.FirstIndex) run.IndexCount

// ── upload + draw ──

/// Copy the batch into the vertex and index buffers (copy pass, before the render pass).
/// A no-op when nothing was batched.
let upload (ui: Ui) (cmd: Gpu.CommandBuffer) : unit =
    if ui.VertexCount <= 0 then () else

    // ── stage the vertex range into the transfer buffer ──
    let vertexBytes = ui.VertexCount * bytesPerVertex
    do Buffer.BlockCopy(ui.Vertices, 0, ui.VertexBytes, 0, vertexBytes)
    let mappedVertices = SDL.MapGPUTransferBuffer(ui.Device, ui.VertexTransfer, false)
    do Marshal.Copy(ui.VertexBytes, 0, mappedVertices, vertexBytes)
    do SDL.UnmapGPUTransferBuffer(ui.Device, ui.VertexTransfer)

    // ── stage the index range into the transfer buffer ──
    let indexBytes = ui.IndexCount * 4
    do Buffer.BlockCopy(ui.Indices, 0, ui.IndexBytes, 0, indexBytes)
    let mappedIndices = SDL.MapGPUTransferBuffer(ui.Device, ui.IndexTransfer, false)
    do Marshal.Copy(ui.IndexBytes, 0, mappedIndices, indexBytes)
    do SDL.UnmapGPUTransferBuffer(ui.Device, ui.IndexTransfer)

    // ── one copy pass: transfer buffers -> device buffers ──
    let copyPass = SDL.BeginGPUCopyPass(cmd)

    let mutable vertexLocation = SDL.GPUTransferBufferLocation()
    vertexLocation.TransferBuffer <- ui.VertexTransfer
    vertexLocation.Offset <- 0u
    let mutable vertexRegion = SDL.GPUBufferRegion()
    vertexRegion.Buffer <- ui.VertexBuffer
    vertexRegion.Offset <- 0u
    vertexRegion.Size <- uint32 vertexBytes
    do SDL.UploadToGPUBuffer(copyPass, &vertexLocation, &vertexRegion, false)

    let mutable indexLocation = SDL.GPUTransferBufferLocation()
    indexLocation.TransferBuffer <- ui.IndexTransfer
    indexLocation.Offset <- 0u
    let mutable indexRegion = SDL.GPUBufferRegion()
    indexRegion.Buffer <- ui.IndexBuffer
    indexRegion.Offset <- 0u
    indexRegion.Size <- uint32 indexBytes
    do SDL.UploadToGPUBuffer(copyPass, &indexLocation, &indexRegion, false)

    do SDL.EndGPUCopyPass(copyPass)

/// Bind the pipeline, push the ortho uniform and draw each run. Called inside the scene
/// pass, after the actors, so the layer sits on top of the scene (FR-011).
let draw (ui: Ui) (cmd: Gpu.CommandBuffer) (renderPass: Gpu.RenderPass) : unit =
    if ui.IndexCount <= 0 then () else

    // ── push the ortho uniform and bind the pipeline ──
    do SDL.PushGPUVertexUniformData(cmd, 0u, ui.UniformBytes, 64u)
    do SDL.BindGPUGraphicsPipeline(renderPass, ui.Pipeline)

    // ── bind the batch's vertex and index buffers ──
    let vertexBindings = ui.VertexBindings
    vertexBindings.[0].Buffer <- ui.VertexBuffer
    vertexBindings.[0].Offset <- 0u
    do SDL.BindGPUVertexBuffers(renderPass, 0u, vertexBindings, 1u)

    let mutable indexBinding = SDL.GPUBufferBinding()
    indexBinding.Buffer <- ui.IndexBuffer
    indexBinding.Offset <- 0u
    do SDL.BindGPUIndexBuffer(renderPass, &indexBinding, SDL.GPUIndexElementSize.IndexElementSize32Bit)

    // ── one indexed draw per texture run ──
    for runIndex in 0 .. ui.RunCount - 1 do
        let run = ui.Runs.[runIndex]
        ui.SamplerBindings.[0].Texture <- run.Texture
        ui.SamplerBindings.[0].Sampler <- run.Sampler
        do SDL.BindGPUFragmentSamplers(renderPass, 0u, ui.SamplerBindings, 1u)
        do SDL.DrawGPUIndexedPrimitives(renderPass, uint32 run.IndexCount, 1u, uint32 run.FirstIndex, 0, 0u)

/// Free the owned GPU resources and the font/engine.
let release (ui: Ui) : unit =
    for textObject in ui.TextObjects do
        if textObject <> IntPtr.Zero then TTF.DestroyText(textObject)
    if ui.TextEngine <> IntPtr.Zero then TTF.DestroyGPUTextEngine(ui.TextEngine)
    if ui.Font <> IntPtr.Zero then TTF.CloseFont(ui.Font)
    do TTF.Quit()
    if ui.Pipeline <> IntPtr.Zero then SDL.ReleaseGPUGraphicsPipeline(ui.Device, ui.Pipeline)
    if ui.VertexShader <> IntPtr.Zero then SDL.ReleaseGPUShader(ui.Device, ui.VertexShader)
    if ui.FragmentShader <> IntPtr.Zero then SDL.ReleaseGPUShader(ui.Device, ui.FragmentShader)
    if ui.VertexBuffer <> IntPtr.Zero then SDL.ReleaseGPUBuffer(ui.Device, ui.VertexBuffer)
    if ui.IndexBuffer <> IntPtr.Zero then SDL.ReleaseGPUBuffer(ui.Device, ui.IndexBuffer)
    if ui.VertexTransfer <> IntPtr.Zero then SDL.ReleaseGPUTransferBuffer(ui.Device, ui.VertexTransfer)
    if ui.IndexTransfer <> IntPtr.Zero then SDL.ReleaseGPUTransferBuffer(ui.Device, ui.IndexTransfer)
    if ui.Sampler <> IntPtr.Zero then SDL.ReleaseGPUSampler(ui.Device, ui.Sampler)
    Texture.release ui.White
