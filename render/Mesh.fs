module Mesh

// Skinned actor GPU buffers + draw. The vertex buffer (position + normal + uv +
// boneIds + weights) and the index buffer are uploaded once at create; skinning
// happens in the vertex shader. An actor is one combined buffer with one or more
// submeshes (IQM mesh ranges), each carrying its own albedo material; `draw` issues
// one indexed draw per submesh through the cel pipeline (spec 040). The bone storage
// buffer is bound by ActorRender before draw.

open System
open System.IO
open System.Numerics
open SDL3
open Gpu
open Texture

// One IQM mesh range into the shared index buffer, with its material.

// The IQM mesh name that marks a face mesh — the reference's `iqm_mesh_indices(model_path, "Face")`,
// whose match is exact. A face mesh takes the idealized front light (spec 040, FR-007).
let isFaceMeshName (name: string) : bool = name = "Face"

[<Struct>]
type Submesh =
    { FirstIndex : int
      IndexCount : int
      Material : Texture.Material
      IsFace : bool }   // the IQM mesh is named "Face" — lit by the idealized front light

[<Struct>]
type Mesh =
    { Device : Gpu.Device
      VertexBuffer : Gpu.Buffer
      IndexBuffer : Gpu.Buffer
      Submeshes : Submesh array
      PerDrawUniformBytes : byte array
      CelUniformBytes : byte array
      OutlineUniformBytes : byte array   // the vertex-stage outline params pushed per pass (spec 041)
      VertexBinding : SDL.GPUBufferBinding array
      IndexBinding : SDL.GPUBufferBinding }

// Interleave the static vertex data into a 52-byte stride buffer: float3 position + float3 normal +
// float2 uv + ubyte4 boneIds + ubyte4Norm weights + float3 smoothNormal (the outline's extrusion
// direction, spec 041).
let private interleave (positions: float32 array) (normals: float32 array) (uvs: float32 array) (boneIds: byte array) (boneWeights: byte array) (smoothNormals: float32 array) : byte array =
    let vertexCount = positions.Length / 3
    use ms = new MemoryStream(vertexCount * 52)
    use bw = new BinaryWriter(ms)
    for v in 0 .. vertexCount - 1 do
        let p = v * 3
        let u = v * 2
        let b = v * 4
        do bw.Write(positions.[p]); bw.Write(positions.[p + 1]); bw.Write(positions.[p + 2])
        do bw.Write(normals.[p]); bw.Write(normals.[p + 1]); bw.Write(normals.[p + 2])
        do bw.Write(uvs.[u]); bw.Write(uvs.[u + 1])
        do bw.Write(boneIds.[b]); bw.Write(boneIds.[b + 1]); bw.Write(boneIds.[b + 2]); bw.Write(boneIds.[b + 3])
        do bw.Write(boneWeights.[b]); bw.Write(boneWeights.[b + 1]); bw.Write(boneWeights.[b + 2]); bw.Write(boneWeights.[b + 3])
        do bw.Write(smoothNormals.[p]); bw.Write(smoothNormals.[p + 1]); bw.Write(smoothNormals.[p + 2])
    ms.ToArray()

let create (device: Gpu.Device) (positions: float32 array) (normals: float32 array) (uvs: float32 array) (boneIds: byte array) (boneWeights: byte array) (smoothNormals: float32 array) (indices: int array) (submeshes: Submesh array) : Mesh =
    let vertexBytes = interleave positions normals uvs boneIds boneWeights smoothNormals

    let vertexBuffer = createBuffer device SDL.GPUBufferUsageFlags.Vertex (uint32 vertexBytes.Length)
    let indexBuffer = createBuffer device SDL.GPUBufferUsageFlags.Index (uint32 (indices.Length * 4))

    // Upload the static vertex + index buffers once.
    do uploadBufferOnce device vertexBuffer vertexBytes
    let indexBytes = Array.zeroCreate<byte> (indices.Length * 4)
    do Buffer.BlockCopy(indices, 0, indexBytes, 0, indices.Length * 4)
    do uploadBufferOnce device indexBuffer indexBytes

    let mutable indexBinding = SDL.GPUBufferBinding()
    indexBinding.Buffer <- indexBuffer
    indexBinding.Offset <- 0u

    { Device = device
      VertexBuffer = vertexBuffer
      IndexBuffer = indexBuffer
      Submeshes = submeshes
      PerDrawUniformBytes = Array.zeroCreate<byte> 192
      CelUniformBytes = Array.zeroCreate<byte> 80
      OutlineUniformBytes = Array.zeroCreate<byte> 16
      VertexBinding = Array.zeroCreate<SDL.GPUBufferBinding> 1
      IndexBinding = indexBinding }

// Release the mesh's vertex and index buffers.
let release (m: Mesh) : unit =
    releaseBuffer m.Device m.VertexBuffer
    releaseBuffer m.Device m.IndexBuffer

// Bind the cel pipeline, push the vertex + cel-fragment uniforms, bind each submesh's albedo
// sampler, and issue one indexed draw per submesh. The bone storage buffer is bound by
// ActorRender first. `faceDir` is the actor's idealized face-light direction (world space).
//
// Parameter order: viewProjection, model, normalMatrix, light, faceDir. The caller computes
// `normalMatrix` once (Gpu.normalMatrix of the model) and passes it to both `draw` and
// `drawOutline`, so one inversion serves the surface and the outline pass.
let draw (m: Mesh) (renderPass: Gpu.RenderPass) (cmd: Gpu.CommandBuffer) (pipeline: Gpu.Pipeline) (viewProjection: Matrix4x4) (model: Matrix4x4) (normalMatrix: Matrix4x4) (light: Light) (faceDir: Vector3) : unit =
    do writeMatrix m.PerDrawUniformBytes 0 viewProjection
    do writeMatrix m.PerDrawUniformBytes 64 model
    do writeMatrix m.PerDrawUniformBytes 128 normalMatrix

    do SDL.PushGPUVertexUniformData(cmd, 0u, m.PerDrawUniformBytes, 192u)
    // The cel surface is inert to the outline extrusion: push a zero-width outline uniform.
    do writeVector4 m.OutlineUniformBytes 0 Vector4.Zero
    do SDL.PushGPUVertexUniformData(cmd, 2u, m.OutlineUniformBytes, 16u)
    do SDL.BindGPUGraphicsPipeline(renderPass, pipeline)

    let bindings = m.VertexBinding
    bindings.[0].Buffer <- m.VertexBuffer
    bindings.[0].Offset <- 0u
    do SDL.BindGPUVertexBuffers(renderPass, 0u, bindings, 1u)

    let mutable indexBinding = m.IndexBinding
    do SDL.BindGPUIndexBuffer(renderPass, &indexBinding, SDL.GPUIndexElementSize.IndexElementSize32Bit)

    for sub in m.Submeshes do
        let material = sub.Material
        do writeVector4 m.CelUniformBytes 0 material.BaseColor
        do writeVector4 m.CelUniformBytes 16 (Vector4(light.Direction, 0.0f))
        do writeVector4 m.CelUniformBytes 32 (Vector4(light.Color, 0.0f))
        do writeVector4 m.CelUniformBytes 48 (Vector4(light.Ambient, 0.0f))
        let isFace = if sub.IsFace then 1.0f else 0.0f
        do writeVector4 m.CelUniformBytes 64 (Vector4(faceDir, isFace))

        do SDL.PushGPUFragmentUniformData(cmd, 0u, m.CelUniformBytes, 80u)
        do SDL.BindGPUFragmentSamplers(renderPass, 0u, material.Bindings, 1u)
        do SDL.DrawGPUIndexedPrimitives(renderPass, uint32 sub.IndexCount, 1u, uint32 sub.FirstIndex, 0, 0u)

// The outline pass (spec 041): bind the front-cull pipeline, push the PerDraw and outline uniforms,
// bind each submesh's albedo sampler (for the cutout mask), and issue one indexed draw per submesh.
// No fragment uniform is pushed — outline.frag declares none. The bone storage buffer is bound by
// ActorRender first, as for the surface. `widthWorld` is the shell's world-space width, so the line
// shrinks with distance like the geometry (spec 041, FR-008).
//
// Parameter order: viewProjection, model, normalMatrix, widthWorld. See `draw`.
let drawOutline (m: Mesh) (renderPass: Gpu.RenderPass) (cmd: Gpu.CommandBuffer) (pipeline: Gpu.Pipeline) (viewProjection: Matrix4x4) (model: Matrix4x4) (normalMatrix: Matrix4x4) (widthWorld: float32) : unit =
    do writeMatrix m.PerDrawUniformBytes 0 viewProjection
    do writeMatrix m.PerDrawUniformBytes 64 model
    do writeMatrix m.PerDrawUniformBytes 128 normalMatrix
    do writeVector4 m.OutlineUniformBytes 0 (Vector4(widthWorld, 0.0f, 0.0f, 0.0f))

    do SDL.PushGPUVertexUniformData(cmd, 0u, m.PerDrawUniformBytes, 192u)
    do SDL.PushGPUVertexUniformData(cmd, 2u, m.OutlineUniformBytes, 16u)
    do SDL.BindGPUGraphicsPipeline(renderPass, pipeline)

    let bindings = m.VertexBinding
    bindings.[0].Buffer <- m.VertexBuffer
    bindings.[0].Offset <- 0u
    do SDL.BindGPUVertexBuffers(renderPass, 0u, bindings, 1u)

    let mutable indexBinding = m.IndexBinding
    do SDL.BindGPUIndexBuffer(renderPass, &indexBinding, SDL.GPUIndexElementSize.IndexElementSize32Bit)

    for sub in m.Submeshes do
        do SDL.BindGPUFragmentSamplers(renderPass, 0u, sub.Material.Bindings, 1u)
        do SDL.DrawGPUIndexedPrimitives(renderPass, uint32 sub.IndexCount, 1u, uint32 sub.FirstIndex, 0, 0u)
