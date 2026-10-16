module Sway

// Manifest-declared bone chains (hair first) that sway with the actor's motion: a Verlet rope
// anchored to the animated pose, applied as per-joint rotations *after* the pose is blended and
// *before* it becomes the bone palette (spec 091). It is a pose pass, not a shader — `shaders/mesh.vert`
// and the cel/outline passes never learn about it (`FR-014`).
//
// Ported from `odin-game/src/sway.odin` and its plan (`docs/SWAY_BONES_PLAN.md`): the *model*, not the
// arithmetic. The reference's parent frame is inverted (its `matrix_to_quat` returns the conjugate of
// the rotation its matrix encodes), its per-bone delta is derived from the segment *into* the bone
// rather than the one it *drives*, and it never re-derives a parent frame after it has swayed. Here the
// parent frame is the *posed* one — a real quaternion, never extracted from a matrix — and the segment
// is the one the joint drives, so a chain's reconstructed positions equal the simulated ones.
//
// Pure of GPU, screen and zone: it takes a model, a pose and an actor's world transform, and rewrites
// the pose's rotations. `world/render/ActorRender.fs` owns the per-instance plumbing.

open System
open System.Numerics
open Prime
open Iqm
open Actor

// ── authored: one manifest entry ──

/// A sway chain as authored on an actor's manifest: joint names ordered root to tip, and how stiff
/// the chain is. Names are resolved against the actor's own skeleton when its asset loads.
[<SymbolicExpansion>]
type SwayChain =
    { Bones : string array
      Stiffness : float32 }

// ── resolved: shared by every instance of an asset ──

/// A chain resolved against its skeleton: the joint indices the names named, in the same order.
type SwayChainDef =
    { Joints : int array
      Stiffness : float32 }

// ── runtime: one instance's chains ──

/// One simulated point: a joint, where it rests in the current pose, where the rope has it, where it
/// had it last frame (Verlet's implicit velocity), and how far it sits from the previous point at rest.
[<Struct>]
type SwayPoint =
    { Joint : int
      mutable Rest : Vector3
      mutable Position : Vector3
      mutable Previous : Vector3
      mutable RestLength : float32 }

/// One chain's runtime state. `Seeded` is explicit rather than inferred from a position sentinel (the
/// reference tests against `(0,0,0)`, which is also a legal position): it is cleared while the editor
/// is open, so the editor holds the animated pose and Play resumes from rest (FR-008).
[<Struct>]
type SwayChainState =
    { Def : SwayChainDef
      Points : SwayPoint array
      mutable SeedAnchor : Vector3
      mutable Seeded : bool }

/// One actor's sway runtime: one entry per chain it declares, or none at all.
type SwayState =
    { Chains : SwayChainState array }

// ── tuning (this phase's constants) ──

[<Literal>]
let MaxSwayChainsPerActor = 16

[<Literal>]
let MaxSwayJointsPerChain = 16

[<Literal>]
let SwayConstraintIterations = 3

/// The rope's downward pull. Applied in world space, so this is world gravity whatever the actor's frame.
let SwayGravity = 9.81f

/// Verlet velocity retention per frame, at stiffness 0 and at stiffness 1.
let SwayDampingMax = 0.98f
let SwayDampingMin = 0.85f

/// The return pull is `stiffness * this`. This is the global lever on how hard every chain is pulled
/// back to the animation pose — it scales the pull without touching the damping, so raising it makes a
/// chain stiffer *and* calmer, where raising an actor's authored stiffness does the first but loosens
/// the second. The reference's value is 0.3; this port runs at 0.9, tuned against Akane's hair.
/// `stiffness` itself is refused at 0, where the pull would vanish entirely (FR-004).
let SwaySpringScale = 0.9f

/// The bound on how far one segment may turn from its animated direction in a frame (FR-007).
let SwayMaxAngle = 45.0f * MathF.PI / 180.0f

/// An anchor that moves further than this in one frame is a teleport, not travel, and re-seeds (FR-013).
let SwayTeleportDistance = 2.0f

// ── resolution ──

/// Resolve one authored chain against a model. Pure. `Error` is an *expected* manifest failure — the
/// chain is reported and dropped and the actor still loads (FR-009) — never a crash.
let resolve (model: Iqm.Model) (chain: SwayChain) : Result<SwayChainDef, string> =
    let names = chain.Bones
    let named = String.concat "\" \"" names

    if names.Length < 2 then
        Error (sprintf "'%s': a chain needs at least two joints to form a segment" named)
    else

    if names.Length > MaxSwayJointsPerChain then
        Error (sprintf "'%s': %d joints exceeds the chain maximum of %d" named names.Length MaxSwayJointsPerChain)
    else

    if not (chain.Stiffness > 0.0f && chain.Stiffness <= 1.0f) then
        Error (sprintf "'%s': stiffness %g is outside (0, 1]" named chain.Stiffness)
    else

    // Resolve each name to a joint index; the first name the skeleton does not carry is the failure.
    let joints = Array.zeroCreate<int> names.Length
    let mutable missing = ""
    let mutable i = 0
    while missing = "" && i < names.Length do
        let mutable found = -1
        let mutable j = 0
        while found < 0 && j < model.Joints.Length do
            if model.Joints.[j].Name = names.[i] then found <- j
            j <- j + 1
        if found < 0 then missing <- names.[i] else joints.[i] <- found
        i <- i + 1

    if missing <> "" then
        Error (sprintf "'%s': this skeleton has no joint '%s'" named missing)
    else

    // A chain must be a path — each joint's skeleton parent is the previous one — because the segment a
    // joint drives is the one to its child, and only a path has such segments.
    let mutable broken = -1
    for k in 1 .. joints.Length - 1 do
        if broken < 0 && model.Joints.[joints.[k]].Parent <> joints.[k - 1] then broken <- k
    if broken >= 0 then
        Error (sprintf "'%s': '%s' is not a child of '%s' — a chain must be a parent-to-child path" named names.[broken] names.[broken - 1])
    else

    Ok { Joints = joints; Stiffness = chain.Stiffness }

/// Resolve a whole actor's authored chains: the usable definitions, and a message for each chain that
/// was dropped — one that would not resolve, laid out as `resolve` words it, or one past
/// `MaxSwayChainsPerActor` (reported once, however many are over). Pure, and returning the messages
/// rather than reporting them keeps the policy testable: the caller is what knows the actor's id, and
/// reports against it (FR-009, FR-012).
let resolveAll (model: Iqm.Model) (chains: SwayChain array) : struct (SwayChainDef array * string array) =
    let usable = ResizeArray<SwayChainDef>()
    let messages = ResizeArray<string>()
    let last = min (chains.Length - 1) (MaxSwayChainsPerActor - 1)
    for i in 0 .. last do
        match resolve model chains.[i] with
        | Ok def -> usable.Add def
        | Error why -> messages.Add(sprintf "sway chain %d %s" (i + 1) why)
    if chains.Length > MaxSwayChainsPerActor then
        messages.Add(sprintf "%d sway chains exceeds the maximum of %d; the rest are ignored" chains.Length MaxSwayChainsPerActor)
    struct (usable.ToArray(), messages.ToArray())

/// Build one instance's runtime chains from an asset's resolved defs. Allocates here and nowhere else.
let createState (defs: SwayChainDef array) : SwayState =
    { Chains =
        defs
        |> Array.map (fun def ->
            { Def = def
              Points =
                def.Joints
                |> Array.map (fun joint ->
                    { Joint = joint
                      Rest = Vector3.Zero
                      Position = Vector3.Zero
                      Previous = Vector3.Zero
                      RestLength = 0.0f })
              SeedAnchor = Vector3.Zero
              Seeded = false }) }

/// Forget every chain's simulated state, so the next simulation re-seeds at the animated pose. Called
/// while the editor is open: the pose is the blend's either way, but this is what stops a resumed Play
/// from starting mid-swing.
let clear (state: SwayState) : unit =
    for c in 0 .. state.Chains.Length - 1 do
        state.Chains.[c].Seeded <- false

// ── the maths ──

/// The rotation that carries `src` onto `tgt`, as `Vector3.Transform(src, ...)` would apply it. The two
/// near-parallel cases are guarded because the general formula degenerates there.
let private quatFromTwoVectors (src: Vector3) (tgt: Vector3) : Quaternion =
    let f = Vector3.Normalize src
    let t = Vector3.Normalize tgt
    let d = Vector3.Dot(f, t)
    if d > 0.999999f then Quaternion.Identity
    elif d < -0.999999f then
        let axis = if abs f.X > 0.999f then Vector3.UnitY else Vector3.UnitX
        let a = Vector3.Cross(f, axis)
        Quaternion.Normalize(Quaternion(a.X, a.Y, a.Z, 0.0f))
    else
        Quaternion.Normalize(Quaternion(Vector3.Cross(f, t), 1.0f + d))

/// `q`, shortened to turn by no more than `maxAngle`. Bounds how far one segment can leave its animated
/// direction in a frame, which is the folding failure mode (FR-007).
let private clampAngle (q: Quaternion) (maxAngle: float32) : Quaternion =
    let angle = 2.0f * MathF.Acos(Math.Clamp(q.W, -1.0f, 1.0f))
    if angle <= maxAngle then q else
    let half = maxAngle * 0.5f
    let v = Vector3.Normalize(Vector3(q.X, q.Y, q.Z))
    Quaternion(v.X * MathF.Sin half, v.Y * MathF.Sin half, v.Z * MathF.Sin half, MathF.Cos half)

// ── the simulation ──

/// Step every chain and write the resulting joint rotations into `pose`, then recompose it. The caller
/// has already blended this frame's pose (an animated pose, not last frame's sway) and, while playing
/// only, calls this. A no-op when the actor declares no chain or the frame has no time.
///
/// `world` is the actor's world matrix (`upAxisFix * modelMatrix`, the same one `draw` uses) and
/// `bodyRot` its world orientation (`transform.Rotation * upAxisFix`, in that order). The rotation the
/// sim produces for a joint is `d * (bodyRot * animatedWorldRotation)`, and the local rotation written is
/// that target pulled back through the parent — so `bodyRot` cancels exactly, and the sway is correct in
/// world space at any actor yaw (FR-005). The parent frame is the *posed* one and the delta is the
/// segment the joint *drives*: `k` runs to `n-2`, so the tip joint, which drives no segment, is never
/// rotated (its world orientation still turns, carried by its parent) — FR-006.
let apply (model: ActorModel) (pose: PoseFrame) (state: SwayState) (dt: float32) (world: Matrix4x4) (bodyRot: Quaternion) : unit =
    if state.Chains.Length = 0 || dt <= 0.0f then () else

    let joints = model.Model.Joints
    let invBody = Quaternion.Conjugate bodyRot
    let gravity = Vector3(0.0f, -SwayGravity * dt * dt, 0.0f)

    for c in 0 .. state.Chains.Length - 1 do
        let chain = state.Chains.[c]
        let points = chain.Points
        let n = points.Length
        if n < 2 then () else

        // ── the animated pose this frame: where each point rests ──
        for k in 0 .. n - 1 do
            let point = &points.[k]
            point.Rest <- Vector3.Transform(pose.World.[point.Joint].Translation, world)
            if k > 0 then point.RestLength <- Vector3.Distance(points.[k - 1].Rest, point.Rest)

        // ── seed, or re-seed after a teleport ──
        let anchor = points.[0].Rest
        if not chain.Seeded || Vector3.Distance(anchor, chain.SeedAnchor) > SwayTeleportDistance then
            for k in 0 .. n - 1 do
                let point = &points.[k]
                point.Position <- point.Rest
                point.Previous <- point.Rest
            state.Chains.[c].Seeded <- true
            state.Chains.[c].SeedAnchor <- anchor

        // ── the rope: the root follows the pose, every other point swings ──
        let stiffness = chain.Def.Stiffness
        let damping = SwayDampingMax + (SwayDampingMin - SwayDampingMax) * stiffness
        let springK = stiffness * SwaySpringScale

        points.[0].Position <- points.[0].Rest
        points.[0].Previous <- points.[0].Rest

        for k in 1 .. n - 1 do
            let point = &points.[k]
            let velocity = (point.Position - point.Previous) * damping
            point.Previous <- point.Position
            let mutable next = point.Position + velocity + gravity
            next <- next + (point.Rest - next) * springK
            point.Position <- next

        // ── hold each segment's rest length, then put the anchor back ──
        for _ in 1 .. SwayConstraintIterations do
            for k in 1 .. n - 1 do
                let parent = &points.[k - 1]
                let child = &points.[k]
                let dir = child.Position - parent.Position
                let distance = dir.Length()
                if distance > 0.000001f then
                    let correction = dir * ((1.0f - child.RestLength / distance) * 0.5f)
                    parent.Position <- parent.Position + correction
                    child.Position <- child.Position - correction
            points.[0].Position <- points.[0].Rest

        // ── turn each joint so the segment it drives points where the rope put it ──
        let first = points.[0].Joint
        let firstParent = joints.[first].Parent
        let mutable parentPoseRot = if firstParent >= 0 then pose.WorldRotation.[firstParent] else Quaternion.Identity

        for k in 0 .. n - 2 do
            let restDelta = points.[k + 1].Rest - points.[k].Rest
            let swayDelta = points.[k + 1].Position - points.[k].Position
            let restLength = restDelta.Length()
            let swayLength = swayDelta.Length()
            if restLength <= 0.000001f || swayLength <= 0.000001f then () else

            let joint = points.[k].Joint
            let delta = clampAngle (quatFromTwoVectors (restDelta / restLength) (swayDelta / swayLength)) SwayMaxAngle
            let targetWorld = Quaternion.Normalize(Quaternion.Multiply(delta, Quaternion.Multiply(bodyRot, pose.WorldRotation.[joint])))
            let targetPose = Quaternion.Normalize(Quaternion.Multiply(invBody, targetWorld))
            pose.Rotation.[joint] <- Quaternion.Normalize(Quaternion.Multiply(Quaternion.Conjugate parentPoseRot, targetPose))
            parentPoseRot <- targetPose

    do Actor.composeAll model pose
