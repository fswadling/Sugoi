module Actor

// Animation sampling + CPU skinning for IQM actors. The references delegate this
// to raylib; this is the genuinely new F# work for phase 3. Pure data/math — no
// GPU, no Zone dependency; the slot algebra and the blend operate on `core/SlotMap`,
// which is neither. `world/ActorRender.fs` owns the upload/draw side.

open System
open System.Numerics
open System.Runtime.InteropServices
open Iqm
open SlotMap

// The capacity of an actor's animation slots (spec 036). Eight is what the feature
// asks for: a base motion plus a small number of overlays.
[<Literal>]
let MaxAnimationSlots = 8

// Loaded actor: the parsed model plus the inverse bind-pose world matrices,
// computed once at load (the animation world matrix is composed with these).
type ActorModel =
    { Model : Iqm.Model
      InverseBind : Matrix4x4 array }

// One animation slot: a clip held by an actor, its own playback clock, and the
// weight that decides how much it contributes to the blended pose. `Frame`,
// `Weight` and `Enabled` are written in place — by the per-frame clock and by the
// weight setter — so no slot record is ever rebuilt.
[<Struct>]
type AnimationSlot =
    { AnimIndex : int
      mutable Frame : float32
      PlaybackFps : float32
      mutable Weight : float32
      Loop : bool
      mutable Enabled : bool }

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

/// The index of the animation clip named `name`, or `ValueNone`. A loop rather than `Array.tryFindIndex`:
/// `setClipWeight` calls this per frame, and the closure `tryFindIndex` takes captures `name`, allocating on
/// every call (`specs/074`). A `voption` so the found answer does not allocate either.
let tryFindAnimIndex (m: ActorModel) (name: string) : int voption =
    let anims = m.Model.Anims
    let mutable found = ValueNone
    let mutable i = 0
    while found.IsNone && i < anims.Length do
        if anims.[i].Name = name then found <- ValueSome i
        i <- i + 1
    found

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

// ── animation clocks ──

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

// Advance every held clip on its own clock (FR-009): each alive, enabled slot moves
// by its own rate and loop setting, independently of the others. Scans the slot map's
// span in place, so it allocates nothing. A zero-weight slot still advances — a zero
// weight removes its contribution, not its playback.
let advanceSlotsInPlace (m: ActorModel) (slots: Span<Slot<AnimationSlot>>) (dt: float32) : unit =
    for i in 0 .. slots.Length - 1 do
        let slot = &slots.[i]
        if slot.Alive then advanceInPlace m &slot.Value dt

// ── sampling ──

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

// The integer frame window a clip is sampled at, from a playback position in frames:
// the two frames to interpolate (`f0`, `f1`) and the blend between them. Shared by the
// single-clip sampler and the blend, so both honour a clip's loop setting identically.
let private frameWindow (n: int) (frame: float32) (loop: bool) : struct (int * int * float32) =
    let frameCount = float32 n
    let cur =
        if loop then
            let x = frame % frameCount
            if x < 0.0f then x + frameCount else x
        else
            Math.Clamp(frame, 0.0f, frameCount - 1.0f)
    let f0 = int (MathF.Floor cur)
    let f1 = if f0 + 1 >= n then (if loop then 0 else f0) else f0 + 1
    struct (f0, f1, cur - float32 f0)

// One joint's interpolated local (translation, rotation, scale) for a clip, sampled
// between its two frames. The shared per-joint sampler: `samplePose` calls it once for
// one clip, `blendPose` calls it once per contributing slot.
let private sampleJointTRS (model: Iqm.Model) (anim: Iqm.Anim) (f0: int) (f1: int) (blend: float32) (j: int) : struct (Vector3 * Quaternion * Vector3) =
    let joint = model.Joints.[j]
    let p = model.Poses.[j]
    let chanBase = model.PoseChannelOffsets.[j]
    let chan0 = (anim.FirstFrame + f0) * model.NumFrameChannels + chanBase
    let chan1 = (anim.FirstFrame + f1) * model.NumFrameChannels + chanBase

    let struct (t0, r0, s0) = sampleJoint model p joint chan0
    let struct (t1, r1, s1) = sampleJoint model p joint chan1

    struct (Vector3.Lerp(t0, t1, blend), Quaternion.Slerp(r0, r1, blend), Vector3.Lerp(s0, s1, blend))

// Compose one joint's local/world/skinning matrices from its (translation, rotation,
// scale) and write them into `pose`. Joints are parent-before-child, so a child's
// parent world matrix is already written when it is composed.
let private composeJoint (m: ActorModel) (model: Iqm.Model) (j: int) (t: Vector3) (r: Quaternion) (s: Vector3) (pose: PoseFrame) : unit =
    let joint = model.Joints.[j]
    let local = localMatrix t r s
    let world = if joint.Parent >= 0 then local * pose.World.[joint.Parent] else local
    pose.Local.[j] <- local
    pose.World.[j] <- world
    pose.Skinning.[j] <- m.InverseBind.[j] * world

// Sample the pose of a single clip at a local animation frame (0 .. NumFrames-1) and
// write the per-joint local/world/skinning matrices into `pose`. Kept as the tested
// single-clip path (and `blendPose`'s reference for one slot at weight 1).
let samplePose (m: ActorModel) (animIndex: int) (frame: float32) (loop: bool) (pose: PoseFrame) : unit =
    if animIndex < 0 || animIndex >= m.Model.Anims.Length then () else
    let model = m.Model
    let anim = model.Anims.[animIndex]
    let n = anim.NumFrames
    if n <= 0 then () else

    let struct (f0, f1, blend) = frameWindow n frame loop
    for j in 0 .. model.Joints.Length - 1 do
        let struct (t, r, s) = sampleJointTRS model anim f0 f1 blend j
        composeJoint m model j t r s pose

// The weighted average of every contributing clip, composed into `pose` (FR-003).
// Per joint: translations and scales are summed by weight and divided by the total,
// and rotations use an incremental slerp running average — the four components of two
// rotations cannot be averaged as plain numbers. A joint that no clip contributes to
// falls back to its bind-pose local, so an actor holding nothing stands in its rest
// pose (FR-008). Weights are relative, so only their proportions matter (FR-004).
// Accumulates in per-joint locals, so it allocates nothing (FR-012). Takes the slot map's
// span (a `Span`, as `Slots` yields) and only reads it.
let blendPose (m: ActorModel) (slots: Span<Slot<AnimationSlot>>) (pose: PoseFrame) : unit =
    let model = m.Model
    for j in 0 .. model.Joints.Length - 1 do
        let mutable translationSum = Vector3.Zero
        let mutable scaleSum = Vector3.Zero
        let mutable rotation = Quaternion.Identity
        let mutable rotationWeight = 0.0f
        let mutable active = false

        for i in 0 .. slots.Length - 1 do
            let slot = slots.[i]
            if slot.Alive then
                let s = slot.Value
                if s.Enabled && s.Weight > 0.0f && s.AnimIndex >= 0 && s.AnimIndex < model.Anims.Length then
                    let anim = model.Anims.[s.AnimIndex]
                    if anim.NumFrames > 0 then
                        let struct (f0, f1, blend) = frameWindow anim.NumFrames s.Frame s.Loop
                        let struct (t, r, sc) = sampleJointTRS model anim f0 f1 blend j
                        let w = s.Weight
                        translationSum <- translationSum + t * w
                        scaleSum <- scaleSum + sc * w
                        if not active then
                            rotation <- r
                            rotationWeight <- w
                            active <- true
                        else
                            let nextWeight = rotationWeight + w
                            rotation <- Quaternion.Slerp(rotation, r, w / nextWeight)
                            rotationWeight <- nextWeight

        if active then
            let invWeight = 1.0f / rotationWeight
            composeJoint m model j (translationSum * invWeight) rotation (scaleSum * invWeight) pose
        else
            let joint = model.Joints.[j]
            composeJoint m model j joint.Translate joint.Rotate joint.Scale pose

// ── slot set algebra ──

// The array index of the alive slot holding a clip, or -1 when the actor holds none.
let private slotIndexOf (slots: SlotMap<AnimationSlot>) (animIndex: int) : int =
    let span = slots.Slots
    let mutable found = -1
    for i in 0 .. span.Length - 1 do
        if found < 0 then
            let slot = span.[i]
            if slot.Alive && slot.Value.AnimIndex = animIndex then found <- i
    found

// Set a clip's weight, adding the clip to the actor's set when it is not already held
// (FR-005, FR-007). The weight is clamped to 0..1 (FR-006) and the slot enabled; adding
// past `MaxAnimationSlots` raises through `SlotMap.Add` (FR-002), and a clip index the
// asset does not define raises.
let setSlotWeight (m: ActorModel) (slots: SlotMap<AnimationSlot>) (animIndex: int) (weight: float32) : unit =
    if animIndex < 0 || animIndex >= m.Model.Anims.Length then
        failwithf "animation slot: clip index %d is out of range (the asset has %d clips)" animIndex m.Model.Anims.Length

    let clamped = Math.Clamp(weight, 0.0f, 1.0f)
    let index = slotIndexOf slots animIndex
    if index >= 0 then
        let span = slots.Slots
        let slot = span.[index]
        span.[index] <- { slot with Value = { slot.Value with Weight = clamped; Enabled = true } }
    else
        slots.Add({ createSlot m.Model animIndex true with Weight = clamped }) |> ignore

// Remove the clip's slot from the actor's set; a no-op when it holds none for it (FR-015).
let removeSlot (slots: SlotMap<AnimationSlot>) (animIndex: int) : unit =
    let index = slotIndexOf slots animIndex
    if index >= 0 then
        let slot = slots.Slots.[index]
        slots.Free { Index = uint32 index; Generation = slot.Generation }

// ── morph set algebra (spec 037) ──

// One morph slot: a morph the actor weighs, with a mutable weight. There is no `Enabled`
// flag — a weight of 0 already means "contributes nothing", and a morph has no clock to gate.
[<Struct>]
type MorphSlot =
    { MorphIndex : int
      mutable Weight : float32 }

// The array index of the alive slot holding a morph, or -1 when the actor holds none.
let morphSlotIndexOf (slots: SlotMap<MorphSlot>) (morphIndex: int) : int =
    let span = slots.Slots
    let mutable found = -1
    for i in 0 .. span.Length - 1 do
        if found < 0 then
            let slot = span.[i]
            if slot.Alive && slot.Value.MorphIndex = morphIndex then found <- i
    found

// Set a morph's weight, adding the morph to the actor's set when it is not already held
// (FR-005, FR-007). The weight is clamped to 0..1; a morph index outside the asset's morphs
// raises, and adding past the maximum raises through `SlotMap.Add` (FR-006).
let setMorphWeight (slots: SlotMap<MorphSlot>) (morphCount: int) (morphIndex: int) (weight: float32) : unit =
    if morphIndex < 0 || morphIndex >= morphCount then
        failwithf "morph slot: morph index %d is out of range (the asset has %d morphs)" morphIndex morphCount

    let clamped = Math.Clamp(weight, 0.0f, 1.0f)
    let index = morphSlotIndexOf slots morphIndex
    if index >= 0 then
        let span = slots.Slots
        let slot = span.[index]
        span.[index] <- { slot with Value = { slot.Value with Weight = clamped } }
    else
        slots.Add({ MorphIndex = morphIndex; Weight = clamped }) |> ignore

// Remove the morph's slot from the actor's set; a no-op when it holds none for it (FR-009).
let removeMorph (slots: SlotMap<MorphSlot>) (morphIndex: int) : unit =
    let index = morphSlotIndexOf slots morphIndex
    if index >= 0 then
        let slot = slots.Slots.[index]
        slots.Free { Index = uint32 index; Generation = slot.Generation }

// Pack an actor's morph weights into the pushed vertex-uniform payload: the weight of each
// morph by index (0 for a morph not held), then the morph count at float index `capacity`.
// Indexing by morph index — not by slot order — is what makes the sum order-independent
// (FR-005). Reuses `bytes`, so the per-frame path allocates nothing (FR-012).
let packMorphWeights (slots: Span<Slot<MorphSlot>>) (morphCount: int) (capacity: int) (bytes: byte array) : unit =
    Array.Clear(bytes, 0, bytes.Length)
    let floats = MemoryMarshal.Cast<byte, float32>(bytes.AsSpan())
    for i in 0 .. slots.Length - 1 do
        let slot = slots.[i]
        if slot.Alive && slot.Value.MorphIndex >= 0 && slot.Value.MorphIndex < morphCount then
            floats.[slot.Value.MorphIndex] <- slot.Value.Weight
    floats.[capacity] <- float32 morphCount
