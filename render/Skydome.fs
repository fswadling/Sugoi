module Skydome

// The zone's authored sky (spec 092): an on/off flag and a zenith and horizon colour, held on the zone
// beside its light and carried by a scenario as part of the zone's staging (`world/ZoneTemplate.fs`).
//
// The renderer is a full sphere drawn behind the scene, centred on the camera. The vertex stage is fed a
// sky-only view-projection — the camera's rotation but no translation — so the dome always surrounds the
// camera and never clips; the pipeline has depth test/write and culling disabled, so the dome neither
// occludes nor is occluded by geometry. The colours are linear RGB (the light colour's space) and are
// gamma-encoded in the shader.
//
// A trimmed port of `odin-game/src/skydome.odin`: the gradient and a simple sun, with the reference's
// procedural clouds and layered sun deliberately deferred.

open System
open System.Numerics
open SDL3
open Gpu
open Camera
open Texture

// ── the authored value ──

[<Struct>]
type Settings =
    { Enabled : bool
      Zenith : Vector3   // linear rgb, overhead
      Horizon : Vector3 } // linear rgb, at and below the horizon

// One sRGB channel (0..255) to linear, matching the shader's output encode.
let private toLinear (channel: float32) : float32 = MathF.Pow(channel / 255.0f, 2.2f)

// A zone's sky before it is authored: off (so an unedited zone is unchanged, spec 043's precedent), with
// `odin-game`'s zenith/horizon colours so enabling it shows a sensible blue.
let defaultSettings : Settings =
    { Enabled = false
      Zenith = Vector3(toLinear 89.0f, toLinear 140.0f, toLinear 217.0f)
      Horizon = Vector3(toLinear 140.0f, toLinear 179.0f, toLinear 217.0f) }

// ── geometry ──

// The dome's radius, in world units. Large enough to sit beyond any scene geometry; the projection's far
// plane is sized from it, so it does not interact with the camera's own near/far.
let private radius = 900.0f

[<Literal>]
let private rings = 16
[<Literal>]
let private segments = 32

// A full UV sphere as positions (12-byte stride, y up) and a triangle index list, the `odin-game` shape.
let private sphereGeometry () : float32 array * uint32 array =
    let fullRings = rings * 2
    let levelCount = fullRings + 1
    let positions = Array.zeroCreate<float32> (levelCount * segments * 3)

    for level in 0 .. levelCount - 1 do
        // phi from -PI/2 (south pole) to +PI/2 (north pole).
        let phi = -MathF.PI * 0.5f + MathF.PI * float32 level / float32 fullRings
        let y = MathF.Sin(phi) * radius
        let r = MathF.Cos(phi) * radius
        for seg in 0 .. segments - 1 do
            let theta = 2.0f * MathF.PI * float32 seg / float32 segments
            let idx = (level * segments + seg) * 3
            positions.[idx] <- r * MathF.Cos(theta)
            positions.[idx + 1] <- y
            positions.[idx + 2] <- r * MathF.Sin(theta)

    let triCount = fullRings * segments * 2
    let indices = Array.zeroCreate<uint32> (triCount * 3)
    let mutable i = 0
    for level in 0 .. fullRings - 1 do
        for seg in 0 .. segments - 1 do
            let nextSeg = (seg + 1) % segments
            let a = uint32 (level * segments + seg)
            let b = uint32 (level * segments + nextSeg)
            let c = uint32 ((level + 1) * segments + seg)
            let d = uint32 ((level + 1) * segments + nextSeg)
            indices.[i] <- a; indices.[i + 1] <- c; indices.[i + 2] <- d
            indices.[i + 3] <- a; indices.[i + 4] <- d; indices.[i + 5] <- b
            i <- i + 6
    positions, indices

// ── the renderer ──

// The shared skydome resource: the sphere's GPU buffers, uploaded once, and the two reused uniform
// buffers the draw fills each frame. The pipeline lives on `Gpu` (device-level, like the others) and is
// passed to `draw`.
[<Struct>]
type Skydome =
    { Device : Device
      VertexBuffer : Buffer
      IndexBuffer : Buffer
      IndexCount : int
      VertexBytes : byte array       // one mat4, the sky-only view-projection
      FragmentBytes : byte array     // three vec4: zenith, horizon, sun direction
      VertexBinding : SDL.GPUBufferBinding array
      IndexBinding : SDL.GPUBufferBinding }

let create (device: Device) : Skydome =
    let positions, indices = sphereGeometry ()

    let vertexBytes = Array.zeroCreate<byte> (positions.Length * 4)
    do Buffer.BlockCopy(positions, 0, vertexBytes, 0, vertexBytes.Length)
    let indexBytes = Array.zeroCreate<byte> (indices.Length * 4)
    do Buffer.BlockCopy(indices, 0, indexBytes, 0, indexBytes.Length)

    let vertexBuffer = createBuffer device SDL.GPUBufferUsageFlags.Vertex (uint32 vertexBytes.Length)
    let indexBuffer = createBuffer device SDL.GPUBufferUsageFlags.Index (uint32 indexBytes.Length)

    // One batched upload for both buffers, submitted once (the loader's pattern).
    let batch = beginUpload device
    do recordBuffer batch vertexBuffer vertexBytes
    do recordBuffer batch indexBuffer indexBytes
    do finishUpload batch

    let mutable indexBinding = SDL.GPUBufferBinding()
    indexBinding.Buffer <- indexBuffer
    indexBinding.Offset <- 0u

    { Device = device
      VertexBuffer = vertexBuffer
      IndexBuffer = indexBuffer
      IndexCount = indices.Length
      VertexBytes = Array.zeroCreate<byte> 64
      FragmentBytes = Array.zeroCreate<byte> 48
      VertexBinding = Array.zeroCreate<SDL.GPUBufferBinding> 1
      IndexBinding = indexBinding }

// Free the sphere's GPU buffers. Called at shutdown, before the device is destroyed.
let release (sky: Skydome) : unit =
    do releaseBuffer sky.Device sky.VertexBuffer
    do releaseBuffer sky.Device sky.IndexBuffer

// Draw the dome behind the scene (spec 092). `settings` is the active zone's sky and `light` its resolved
// sun (so the sky's sun and the scene's lighting agree). Assumes the caller has checked `settings.Enabled`
// and passes the skydome pipeline; draws first in the scene pass so geometry overwrites it.
let draw (sky: Skydome) (renderPass: RenderPass) (cmd: CommandBuffer) (pipeline: Pipeline) (camera: Camera) (settings: Settings) (light: Light) : unit =
    // The dome is camera-centred: a view from the origin looking along the camera's forward, and a
    // projection whose far plane sits just past the dome, so it always surrounds the camera.
    let view = Matrix4x4.CreateLookAt(Vector3.Zero, Camera.forward camera, camera.Up)
    let proj = Matrix4x4.CreatePerspectiveFieldOfView(camera.Fov, camera.Aspect, 0.1f, radius * 1.5f)
    do writeMatrix sky.VertexBytes 0 (view * proj)

    do writeVector4 sky.FragmentBytes 0 (Vector4(settings.Zenith, 0.0f))
    do writeVector4 sky.FragmentBytes 16 (Vector4(settings.Horizon, 0.0f))
    do writeVector4 sky.FragmentBytes 32 (Vector4(light.Direction, 0.0f))

    do SDL.PushGPUVertexUniformData(cmd, 0u, sky.VertexBytes, 64u)
    do SDL.PushGPUFragmentUniformData(cmd, 0u, sky.FragmentBytes, 48u)
    do SDL.BindGPUGraphicsPipeline(renderPass, pipeline)

    let bindings = sky.VertexBinding
    bindings.[0].Buffer <- sky.VertexBuffer
    bindings.[0].Offset <- 0u
    do SDL.BindGPUVertexBuffers(renderPass, 0u, bindings, 1u)

    let mutable indexBinding = sky.IndexBinding
    do SDL.BindGPUIndexBuffer(renderPass, &indexBinding, SDL.GPUIndexElementSize.IndexElementSize32Bit)
    do SDL.DrawGPUIndexedPrimitives(renderPass, uint32 sky.IndexCount, 1u, 0u, 0, 0u)
