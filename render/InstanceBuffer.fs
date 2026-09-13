module InstanceBuffer

// Grow-only per-frame instance vertex buffer for the static instancing batch.
// One instance is 112 bytes: four model rows then three normal-matrix rows, all
// Float4, bound at vertex slot 1 with InputRate.Instance (see shaders/static.vert
// and docs/RENDERING_NOTES.md §7). Growth retires the old GPU/transfer buffers
// instead of releasing them, because SDL_gpu keeps several frames in flight — a
// buffer bound by a submitted frame may still be in use.

open System
open System.Runtime.InteropServices
open SDL3
open Gpu

let stride = 112

// Frames a superseded buffer must outlive before it can be released
// (SDL_gpu keeps MAX_FRAMES_IN_FLIGHT = 3 frames in flight).
let private retireFrames = 3

[<Struct>]
type InstanceBuffer =
    { Device : Gpu.Device
      mutable Buffer : Gpu.Buffer
      mutable Transfer : Gpu.TransferBuffer
      mutable Capacity : int
      mutable Staging : byte array
      mutable Retired : (Gpu.Buffer * Gpu.TransferBuffer * int) list }

let private backing (device: Gpu.Device) (instances: int) : Gpu.Buffer * Gpu.TransferBuffer =
    let bytes = uint32 (max 1 instances * stride)
    createBuffer device SDL.GPUBufferUsageFlags.Vertex bytes, createTransferBuffer device bytes

// Initial capacity 0 is fine: the first gather sizes the buffer to the scene.
let create (device: Gpu.Device) (initialInstances: int) : InstanceBuffer =
    let buffer, transfer = backing device initialInstances
    { Device = device
      Buffer = buffer
      Transfer = transfer
      Capacity = initialInstances
      Staging = Array.zeroCreate<byte> (max 1 initialInstances * stride)
      Retired = [] }

// Ensure room for `instances`; on growth allocate a new buffer and retire the old
// one (released later by `reclaim`, never inline).
let ensure (device: Gpu.Device) (buf: byref<InstanceBuffer>) (instances: int) : unit =
    if instances <= buf.Capacity then () else

    let buffer, transfer = backing device instances
    buf.Retired <- (buf.Buffer, buf.Transfer, retireFrames) :: buf.Retired
    buf.Buffer <- buffer
    buf.Transfer <- transfer
    buf.Capacity <- instances
    buf.Staging <- Array.zeroCreate<byte> (instances * stride)

// Release retired buffers that have outlived every in-flight frame. Call once per frame.
let reclaim (device: Gpu.Device) (buf: byref<InstanceBuffer>) : unit =
    let mutable keep = []
    for (buffer, transfer, frames) in buf.Retired do
        if frames <= 1 then
            do SDL.ReleaseGPUBuffer(device, buffer)
            do SDL.ReleaseGPUTransferBuffer(device, transfer)
        else
            keep <- (buffer, transfer, frames - 1) :: keep
    buf.Retired <- keep

// Copy `usedBytes` from Staging to the GPU buffer in a copy pass (before the scene pass).
let upload (buf: InstanceBuffer) (cmd: Gpu.CommandBuffer) (usedBytes: int) : unit =
    if usedBytes <= 0 then () else

    let mapped = SDL.MapGPUTransferBuffer(buf.Device, buf.Transfer, false)
    do Marshal.Copy(buf.Staging, 0, mapped, usedBytes)
    do SDL.UnmapGPUTransferBuffer(buf.Device, buf.Transfer)

    let copyPass = SDL.BeginGPUCopyPass(cmd)
    let mutable loc = SDL.GPUTransferBufferLocation()
    loc.TransferBuffer <- buf.Transfer
    loc.Offset <- 0u
    let mutable region = SDL.GPUBufferRegion()
    region.Buffer <- buf.Buffer
    region.Offset <- 0u
    region.Size <- uint32 usedBytes
    do SDL.UploadToGPUBuffer(copyPass, &loc, &region, false)
    do SDL.EndGPUCopyPass(copyPass)

// The slot-1 binding for a group starting at instance `start` (byte offset start*112).
let binding (buf: InstanceBuffer) (start: int) : SDL.GPUBufferBinding =
    let mutable b = SDL.GPUBufferBinding()
    b.Buffer <- buf.Buffer
    b.Offset <- uint32 (start * stride)
    b
