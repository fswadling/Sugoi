module Mesh

// Static GPU vertex/index buffers + draw for a skinned actor mesh. The vertex
// buffer (position + normal + uv + boneIds + weights) and the index buffer are
// uploaded once at create; skinning happens in the vertex shader, so there is no
// per-frame vertex upload.

open System
open System.IO
open System.Numerics
open SDL3
open Gpu

[<Struct>]
type Mesh =
    { Device : Gpu.Device
      VertexBuffer : Gpu.Buffer
      IndexBuffer : Gpu.Buffer
      IndexCount : int
      PerDrawUniformBytes : byte array
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

let create (device: Gpu.Device) (positions: float32 array) (normals: float32 array) (uvs: float32 array) (boneIds: byte array) (boneWeights: byte array) (indices: int array) : Mesh =
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
      IndexCount = indices.Length
      PerDrawUniformBytes = Array.zeroCreate<byte> 160
      VertexBinding = Array.zeroCreate<SDL.GPUBufferBinding> 1
      IndexBinding = indexBinding }

// Bind the mesh pipeline, push the PerDraw uniform (slot 0), and issue an
// indexed draw. The bone storage buffer is bound by ActorRender first.
let draw (m: Mesh) (renderPass: Gpu.RenderPass) (cmd: Gpu.CommandBuffer) (pipeline: Gpu.Pipeline) (viewProjection: Matrix4x4) (model: Matrix4x4) (lightDir: Vector3) (baseColor: Vector4) : unit =
    do writeMatrix m.PerDrawUniformBytes 0 viewProjection
    do writeMatrix m.PerDrawUniformBytes 64 model
    do writeVector4 m.PerDrawUniformBytes 128 (Vector4(lightDir, 0.0f))
    do writeVector4 m.PerDrawUniformBytes 144 baseColor

    do SDL.PushGPUVertexUniformData(cmd, 0u, m.PerDrawUniformBytes, 160u)
    do SDL.BindGPUGraphicsPipeline(renderPass, pipeline)

    let bindings = m.VertexBinding
    bindings.[0].Buffer <- m.VertexBuffer
    bindings.[0].Offset <- 0u
    do SDL.BindGPUVertexBuffers(renderPass, 0u, bindings, 1u)

    let mutable indexBinding = m.IndexBinding
    do SDL.BindGPUIndexBuffer(renderPass, &indexBinding, SDL.GPUIndexElementSize.IndexElementSize32Bit)
    do SDL.DrawGPUIndexedPrimitives(renderPass, uint32 m.IndexCount, 1u, 0u, 0, 0u)
