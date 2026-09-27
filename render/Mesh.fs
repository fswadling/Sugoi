module Mesh

// Skinned actor GPU buffers + draw. The vertex buffer (position + normal + uv +
// boneIds + weights) and the index buffer are uploaded once at create; skinning
// happens in the vertex shader. An actor is one combined buffer with one or more
// submeshes (IQM mesh ranges), each carrying its own PBR material; `draw` issues
// one indexed draw per submesh. The bone storage buffer is bound by ActorRender
// before draw.

open System
open System.IO
open System.Numerics
open SDL3
open Gpu
open Texture

// One IQM mesh range into the shared index buffer, with its material.
[<Struct>]
type Submesh =
    { FirstIndex : int
      IndexCount : int
      Material : Texture.Material }

[<Struct>]
type Mesh =
    { Device : Gpu.Device
      VertexBuffer : Gpu.Buffer
      IndexBuffer : Gpu.Buffer
      Submeshes : Submesh array
      PerDrawUniformBytes : byte array
      MaterialUniformBytes : byte array
      VertexBinding : SDL.GPUBufferBinding array
      IndexBinding : SDL.GPUBufferBinding }

// Interleave the static vertex data into a 40-byte stride buffer:
// float3 position + float3 normal + float2 uv + ubyte4 boneIds + ubyte4Norm weights.
let private interleave (positions: float32 array) (normals: float32 array) (uvs: float32 array) (boneIds: byte array) (boneWeights: byte array) : byte array =
    let vertexCount = positions.Length / 3
    use ms = new MemoryStream(vertexCount * 40)
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
    ms.ToArray()

let create (device: Gpu.Device) (positions: float32 array) (normals: float32 array) (uvs: float32 array) (boneIds: byte array) (boneWeights: byte array) (indices: int array) (submeshes: Submesh array) : Mesh =
    let vertexBytes = interleave positions normals uvs boneIds boneWeights

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
      MaterialUniformBytes = Array.zeroCreate<byte> 128
      VertexBinding = Array.zeroCreate<SDL.GPUBufferBinding> 1
      IndexBinding = indexBinding }

// Release the mesh's vertex and index buffers.
let release (m: Mesh) : unit =
    releaseBuffer m.Device m.VertexBuffer
    releaseBuffer m.Device m.IndexBuffer

// Bind the mesh pipeline, push the vertex + fragment uniforms, bind each
// submesh's samplers, and issue one indexed draw per submesh. The bone storage
// buffer is bound by ActorRender first.
let draw (m: Mesh) (renderPass: Gpu.RenderPass) (cmd: Gpu.CommandBuffer) (pipeline: Gpu.Pipeline) (viewProjection: Matrix4x4) (model: Matrix4x4) (light: Light) (cameraPos: Vector3) : unit =
    do writeMatrix m.PerDrawUniformBytes 0 viewProjection
    do writeMatrix m.PerDrawUniformBytes 64 model
    do writeMatrix m.PerDrawUniformBytes 128 (normalMatrix model)

    do SDL.PushGPUVertexUniformData(cmd, 0u, m.PerDrawUniformBytes, 192u)
    do SDL.BindGPUGraphicsPipeline(renderPass, pipeline)

    let bindings = m.VertexBinding
    bindings.[0].Buffer <- m.VertexBuffer
    bindings.[0].Offset <- 0u
    do SDL.BindGPUVertexBuffers(renderPass, 0u, bindings, 1u)

    let mutable indexBinding = m.IndexBinding
    do SDL.BindGPUIndexBuffer(renderPass, &indexBinding, SDL.GPUIndexElementSize.IndexElementSize32Bit)

    for sub in m.Submeshes do
        let material = sub.Material
        do writeVector4 m.MaterialUniformBytes 0 material.BaseColor
        do writeVector4 m.MaterialUniformBytes 16 (Vector4(material.Metallic, material.Roughness, 0.0f, 0.0f))
        do writeVector4 m.MaterialUniformBytes 32 (Vector4(material.Emissive, 0.0f))
        let hasAlbedo = if material.HasAlbedo then 1.0f else 0.0f
        let hasNormal = if material.HasNormal then 1.0f else 0.0f
        let hasMR = if material.HasMetallicRoughness then 1.0f else 0.0f
        let hasEmissive = if material.HasEmissive then 1.0f else 0.0f
        do writeVector4 m.MaterialUniformBytes 48 (Vector4(hasAlbedo, hasNormal, hasMR, hasEmissive))
        do writeVector4 m.MaterialUniformBytes 64 (Vector4(light.Direction, 0.0f))
        do writeVector4 m.MaterialUniformBytes 80 (Vector4(light.Color, 0.0f))
        do writeVector4 m.MaterialUniformBytes 96 (Vector4(light.Ambient, 0.0f))
        do writeVector4 m.MaterialUniformBytes 112 (Vector4(cameraPos, 0.0f))

        do SDL.PushGPUFragmentUniformData(cmd, 0u, m.MaterialUniformBytes, 128u)
        do SDL.BindGPUFragmentSamplers(renderPass, 0u, material.Bindings, uint32 material.Bindings.Length)
        do SDL.DrawGPUIndexedPrimitives(renderPass, uint32 sub.IndexCount, 1u, uint32 sub.FirstIndex, 0, 0u)
