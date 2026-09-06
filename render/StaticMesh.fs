module StaticMesh

// Static GPU vertex/index buffers + draw for unskinned geometry (environments
// and props). The vertex buffer is position + normal + uv (32-byte stride),
// uploaded once at create; no blend attributes, no bone storage buffer. Draw
// pushes a vertex PerDraw uniform (viewProj/model/normalMatrix) and a fragment
// material+light uniform, binds the material's samplers, and issues an indexed
// draw.

open System
open System.IO
open System.Numerics
open SDL3
open Gpu
open Texture

[<Struct>]
type StaticMesh =
    { Device : Gpu.Device
      VertexBuffer : Gpu.Buffer
      IndexBuffer : Gpu.Buffer
      IndexCount : int
      PerDrawUniformBytes : byte array
      MaterialUniformBytes : byte array
      VertexBinding : SDL.GPUBufferBinding array
      IndexBinding : SDL.GPUBufferBinding }

// Interleave the static vertex data into a 32-byte stride buffer:
// float3 position (12) + float3 normal (12) + float2 uv (8).
let private interleave (positions: float32 array) (normals: float32 array) (uvs: float32 array) : byte array =
    let vertexCount = positions.Length / 3
    use ms = new MemoryStream(vertexCount * 32)
    use bw = new BinaryWriter(ms)
    for v in 0 .. vertexCount - 1 do
        let p = v * 3
        let u = v * 2
        do bw.Write(positions.[p]); bw.Write(positions.[p + 1]); bw.Write(positions.[p + 2])
        do bw.Write(normals.[p]); bw.Write(normals.[p + 1]); bw.Write(normals.[p + 2])
        do bw.Write(uvs.[u]); bw.Write(uvs.[u + 1])
    ms.ToArray()

let create (device: Gpu.Device) (positions: float32 array) (normals: float32 array) (uvs: float32 array) (indices: int array) : StaticMesh =
    let vertexBytes = interleave positions normals uvs

    let vertexBuffer = createBuffer device SDL.GPUBufferUsageFlags.Vertex (uint32 vertexBytes.Length)
    let indexBuffer = createBuffer device SDL.GPUBufferUsageFlags.Index (uint32 (indices.Length * 4))

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
      PerDrawUniformBytes = Array.zeroCreate<byte> 192   // viewProj + model + normalMatrix
      MaterialUniformBytes = Array.zeroCreate<byte> 128  // 8 x vec4 material/light
      VertexBinding = Array.zeroCreate<SDL.GPUBufferBinding> 1
      IndexBinding = indexBinding }

// Bind the static pipeline, push the vertex + fragment uniforms, bind the
// material's samplers, and issue an indexed draw.
let draw (m: StaticMesh) (renderPass: Gpu.RenderPass) (cmd: Gpu.CommandBuffer) (pipeline: Gpu.Pipeline) (viewProjection: Matrix4x4) (model: Matrix4x4) (light: Light) (cameraPos: Vector3) (material: Material) : unit =
    do writeMatrix m.PerDrawUniformBytes 0 viewProjection
    do writeMatrix m.PerDrawUniformBytes 64 model
    do writeMatrix m.PerDrawUniformBytes 128 (normalMatrix model)

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

    do SDL.PushGPUVertexUniformData(cmd, 0u, m.PerDrawUniformBytes, 192u)
    do SDL.PushGPUFragmentUniformData(cmd, 0u, m.MaterialUniformBytes, 128u)
    do SDL.BindGPUGraphicsPipeline(renderPass, pipeline)

    let bindings = m.VertexBinding
    bindings.[0].Buffer <- m.VertexBuffer
    bindings.[0].Offset <- 0u
    do SDL.BindGPUVertexBuffers(renderPass, 0u, bindings, 1u)

    do SDL.BindGPUFragmentSamplers(renderPass, 0u, material.Bindings, uint32 material.Bindings.Length)

    let mutable indexBinding = m.IndexBinding
    do SDL.BindGPUIndexBuffer(renderPass, &indexBinding, SDL.GPUIndexElementSize.IndexElementSize32Bit)
    do SDL.DrawGPUIndexedPrimitives(renderPass, uint32 m.IndexCount, 1u, 0u, 0, 0u)
