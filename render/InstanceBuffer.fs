module InstanceBuffer

// Grow-only per-frame instance vertex buffer for the static instancing batch.
// One instance is 112 bytes: four model rows then three normal-matrix rows, all
// Float4, bound at vertex slot 1 with InputRate.Instance (see shaders/static.vert
// and docs/RENDERING_NOTES.md §7).
//
// Buffers are allocated lazily on the first `ensure` and the device is passed where
// memory is used, so an unrendered buffer (e.g. a zone created headlessly) never
// needs a device. Growth retires the old GPU/transfer buffers instead of releasing
// them inline, because SDL_gpu keeps several frames in flight.

open System
open System.Runtime.InteropServices
open SDL3
open Gpu

// ── layout ──
let stride = 112

// Frames a superseded buffer must outlive before it can be released
// (SDL_gpu keeps MAX_FRAMES_IN_FLIGHT = 3 frames in flight).
let private retireFrames = 3

// ── buffer state ──
[<Struct>]
type InstanceBuffer =
    { mutable Buffer : Gpu.Buffer
      mutable Transfer : Gpu.TransferBuffer
      mutable Capacity : int
      mutable Staging : byte array
      mutable Retired : (Gpu.Buffer * Gpu.TransferBuffer * int) list }

// No device and no allocation; the buffers are created on the first `ensure`.
let create () : InstanceBuffer =
    { Buffer = IntPtr.Zero
      Transfer = IntPtr.Zero
      Capacity = 0
      Staging = Array.empty<byte>
      Retired = [] }

// ── growth + retirement ──

// Ensure room for `instances`; on growth allocate a new buffer and retire the old
// one (released later by `reclaim`, never inline).
let ensure (device: Gpu.Device) (buf: byref<InstanceBuffer>) (instances: int) : unit =
    if instances <= buf.Capacity then () else

    if buf.Buffer <> IntPtr.Zero then
        buf.Retired <- (buf.Buffer, buf.Transfer, retireFrames) :: buf.Retired
    let bytes = uint32 (max 1 instances * stride)
    buf.Buffer <- createBuffer device SDL.GPUBufferUsageFlags.Vertex bytes
    buf.Transfer <- createTransferBuffer device bytes
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

// Free this buffer's GPU memory, including any retired buffers. Called before the
// device is destroyed, or when the owning zone is released.
let release (device: Gpu.Device) (buf: byref<InstanceBuffer>) : unit =
    if buf.Buffer <> IntPtr.Zero then
        do SDL.ReleaseGPUBuffer(device, buf.Buffer)
        do SDL.ReleaseGPUTransferBuffer(device, buf.Transfer)
        buf.Buffer <- IntPtr.Zero
        buf.Transfer <- IntPtr.Zero
        buf.Capacity <- 0
        buf.Staging <- Array.empty<byte>
    for (buffer, transfer, _) in buf.Retired do
        do SDL.ReleaseGPUBuffer(device, buffer)
        do SDL.ReleaseGPUTransferBuffer(device, transfer)
    buf.Retired <- []

// ── upload + binding ──

// Copy `usedBytes` from Staging to the GPU buffer in a copy pass (before the scene pass).
let upload (device: Gpu.Device) (buf: InstanceBuffer) (cmd: Gpu.CommandBuffer) (usedBytes: int) : unit =
    if usedBytes <= 0 || buf.Buffer = IntPtr.Zero then () else

    let mapped = SDL.MapGPUTransferBuffer(device, buf.Transfer, false)
    do Marshal.Copy(buf.Staging, 0, mapped, usedBytes)
    do SDL.UnmapGPUTransferBuffer(device, buf.Transfer)

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
