module Actor

// Animation sampling + CPU skinning for IQM actors. The references delegate this
// to raylib; this is the genuinely new F# work for phase 3. Pure data/math — no
// GPU, no Zone dependency. `render/ActorRender.fs` owns the upload/draw side.

open System
open System.Numerics
open Iqm

// Loaded actor: the parsed model plus the inverse bind-pose world matrices,
// computed once at load (the animation world matrix is composed with these).
type ActorModel =
    { Model : Iqm.Model
      InverseBind : Matrix4x4 array }

// One animation slot. Phase 3 plays a single looping idle clip; the slot shape
// leaves room for future multi-slot blending (walk + upper body, etc.).
[<Struct>]
type AnimationSlot =
    { AnimIndex : int
      mutable Frame : float32
      PlaybackFps : float32
      Weight : float32
      Loop : bool
      Enabled : bool }

// The per-frame pose: per-joint local, world, and skinning matrices. The arrays
// are allocated once and mutated in place each frame.
type PoseFrame =
    { Local : Matrix4x4 array
      World : Matrix4x4 array
      Skinning : Matrix4x4 array }

// ── matrix helpers ──

// Compose a local joint transform. System.Numerics is row-vector (`v * M`), so
// TRS is `S * R * T` (scale, then rotate, then translate), matching IQM's
// "output = (input*scale)*rotation + translation". Applying `T` only sets the
// translation row, so it is written directly instead of composed with a full
// multiply (this runs once per joint per frame).
let private localMatrix (t: Vector3) (r: Quaternion) (s: Vector3) : Matrix4x4 =
    let mutable m = Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r)
    m.M41 <- t.X
    m.M42 <- t.Y
    m.M43 <- t.Z
    m

// ── construction ──

let createModel (model: Iqm.Model) : ActorModel =
    let n = model.Joints.Length
    let world = Array.zeroCreate<Matrix4x4> n
    for j in 0 .. n - 1 do
        let joint = model.Joints.[j]
        let local = localMatrix joint.Translate joint.Rotate joint.Scale
        // joints are assumed parent-before-child (standard IQM export order).
        world.[j] <- if joint.Parent >= 0 then local * world.[joint.Parent] else local

    let inverseBind = Array.zeroCreate<Matrix4x4> n
    for j in 0 .. n - 1 do
        let mutable result = Matrix4x4.Identity
        if not (Matrix4x4.Invert(world.[j], &result)) then
            failwithf "iqm: singular bind-pose matrix for joint %d" j
        inverseBind.[j] <- result

    { Model = model; InverseBind = inverseBind }

let findAnimIndex (m: ActorModel) (name: string) : int option =
    m.Model.Anims |> Array.tryFindIndex (fun a -> a.Name = name)

let createSlot (model: Iqm.Model) (animIndex: int) (loop: bool) : AnimationSlot =
    let anim = model.Anims.[animIndex]
    { AnimIndex = animIndex
      Frame = 0.0f
      PlaybackFps = if anim.FrameRate > 0.0f then anim.FrameRate else 24.0f
      Weight = 1.0f
      Loop = loop
      Enabled = true }

let createPoseFrame (model: Iqm.Model) : PoseFrame =
    let n = model.Joints.Length
    { Local = Array.zeroCreate<Matrix4x4> n
      World = Array.zeroCreate<Matrix4x4> n
      Skinning = Array.zeroCreate<Matrix4x4> n }

// ── animation ──

// Advance the slot's frame clock in place. The per-frame hot path calls this
// through a byref so only the frame float is written, instead of rebuilding the
// whole AnimationSlot (and, transitively, Actor/ActorRender) records.
let advanceInPlace (m: ActorModel) (slot: byref<AnimationSlot>) (dt: float32) : unit =
    if slot.Enabled && slot.PlaybackFps > 0.0f && slot.AnimIndex >= 0 && slot.AnimIndex < m.Model.Anims.Length then
        let anim = m.Model.Anims.[slot.AnimIndex]
        if anim.NumFrames > 0 then
            let frame = slot.Frame + dt * slot.PlaybackFps
            let frameCount = float32 anim.NumFrames
            let frame =
                if slot.Loop then
                    let x = frame % frameCount
                    if x < 0.0f then x + frameCount else x
                else
                    Math.Clamp(frame, 0.0f, frameCount - 1.0f)
            slot.Frame <- frame

// Pure variant: copy the slot, advance it in place, and return the copy.
let advance (m: ActorModel) (slot: AnimationSlot) (dt: float32) : AnimationSlot =
    let mutable s = slot
    advanceInPlace m &s dt
    s

// Sample one joint's local transform from a pose's frame channels, overriding
// the bind-pose components for whichever channels the pose's channelmask sets.
let private sampleJoint (model: Iqm.Model) (p: Iqm.Pose) (joint: Iqm.Joint) (chanBase: int) : struct (Vector3 * Quaternion * Vector3) =
    let mutable tx = joint.Translate.X
    let mutable ty = joint.Translate.Y
    let mutable tz = joint.Translate.Z
    let mutable rx = joint.Rotate.X
    let mutable ry = joint.Rotate.Y
    let mutable rz = joint.Rotate.Z
    let mutable rw = joint.Rotate.W
    let mutable sx = joint.Scale.X
    let mutable sy = joint.Scale.Y
    let mutable sz = joint.Scale.Z

    let mutable c = 0
    for ch in 0 .. 9 do
        if (p.ChannelMask >>> ch) &&& 1 = 1 then
            let value = p.ChannelOffset.[ch] + float32 model.FrameChannels.[chanBase + c] * p.ChannelScale.[ch]
            match ch with
            | 0 -> tx <- value
            | 1 -> ty <- value
            | 2 -> tz <- value
            | 3 -> rx <- value
            | 4 -> ry <- value
            | 5 -> rz <- value
            | 6 -> rw <- value
            | 7 -> sx <- value
            | 8 -> sy <- value
            | 9 -> sz <- value
            | _ -> ()
            c <- c + 1

    struct (Vector3(tx, ty, tz), Quaternion(rx, ry, rz, rw), Vector3(sx, sy, sz))

// Sample the pose at a local animation frame (0 .. NumFrames-1) and write the
// per-joint local/world/skinning matrices into `pose`.
let samplePose (m: ActorModel) (animIndex: int) (frame: float32) (loop: bool) (pose: PoseFrame) : unit =
    if animIndex < 0 || animIndex >= m.Model.Anims.Length then () else
    let model = m.Model
    let anim = model.Anims.[animIndex]
    let n = anim.NumFrames
    if n <= 0 then () else

    let frameCount = float32 n
    let cur =
        if loop then
            let x = frame % frameCount
            if x < 0.0f then x + frameCount else x
        else
            Math.Clamp(frame, 0.0f, frameCount - 1.0f)

    let f0 = int (MathF.Floor cur)
    let f1 = if f0 + 1 >= n then (if loop then 0 else f0) else f0 + 1
    let blend = cur - float32 f0

    for j in 0 .. model.Joints.Length - 1 do
        let joint = model.Joints.[j]
        let p = model.Poses.[j]
        let chanBase = model.PoseChannelOffsets.[j]
        let chan0 = (anim.FirstFrame + f0) * model.NumFrameChannels + chanBase
        let chan1 = (anim.FirstFrame + f1) * model.NumFrameChannels + chanBase

        let struct (t0, r0, s0) = sampleJoint model p joint chan0
        let struct (t1, r1, s1) = sampleJoint model p joint chan1

        let t = Vector3.Lerp(t0, t1, blend)
        let r = Quaternion.Slerp(r0, r1, blend)
        let s = Vector3.Lerp(s0, s1, blend)

        let local = localMatrix t r s
        let world = if joint.Parent >= 0 then local * pose.World.[joint.Parent] else local
        pose.Local.[j] <- local
        pose.World.[j] <- world
        pose.Skinning.[j] <- m.InverseBind.[j] * world


