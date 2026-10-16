module SwayTests

open System
open System.Numerics
open Xunit
open Prime
open Iqm
open Actor
open SlotMap

// Sway is pure and device-free, so everything it promises is assertable here: a fabricated skeleton
// (the `model` helper pattern of `test/ActorTests.fs`) and no GPU, no window, no clock. What the
// walkthrough in `specs/091-actor-sway/quickstart.md` adds is only that it looks right.

// ── fixtures ──

// A four-joint skeleton: a root, and a three-joint chain hanging off it along the model's +Y. The
// chain's first joint names the root as its parent, which is the ordinary case — the head a hair
// chain hangs from is *not* part of the chain.
let private chainModel () : Iqm.Model =
    let joint name parent (t: Vector3) =
        { Name = name; Parent = parent; Translate = t; Rotate = Quaternion.Identity; Scale = Vector3.One }
    { Meshes = [||]
      Positions = [| 0.0f; 0.0f; 0.0f |]
      Normals = None
      Uvs = None
      Tangents = None
      BlendIndices = None
      BlendWeights = None
      Triangles = [||]
      Joints =
        [| joint "root" -1 Vector3.Zero
           joint "h1" 0 (Vector3(0.0f, 0.2f, 0.0f))
           joint "h2" 1 (Vector3(0.0f, 0.2f, 0.0f))
           joint "h3" 2 (Vector3(0.0f, 0.2f, 0.0f)) |]
      Poses = [||]
      Anims = [||]
      FrameChannels = [||]
      NumFrameChannels = 0
      PoseChannelOffsets = [||] }

let private def (stiffness: float32) : Sway.SwayChainDef =
    { Joints = [| 1; 2; 3 |]; Stiffness = stiffness }

let private chainNames = [| "h1"; "h2"; "h3" |]

let private idle = SlotMap<AnimationSlot>(0u)

let private empty = Sway.createState [||]

// The frame the engine actually uses: the IQM's Z-up is turned to the world's Y-up, then the actor's
// own rotation and position. Mirrors `ActorRender.draw`/`simulate` (which is why the pair is built
// from one rotation here rather than solved out of the matrix).
let private upAxisFixRot : Quaternion = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2.0f)
let private upAxisFix : Matrix4x4 = Matrix4x4.CreateFromQuaternion upAxisFixRot

let private actorRotation (yaw: float32) : Quaternion =
    Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw)

let private actorWorld (position: Vector3) (yaw: float32) : Matrix4x4 =
    Matrix4x4.CreateFromQuaternion upAxisFixRot * Matrix4x4.CreateFromQuaternion(actorRotation yaw) * Matrix4x4.CreateTranslation position

let private actorBodyRot (yaw: float32) : Quaternion =
    Quaternion.Multiply(actorRotation yaw, upAxisFixRot)

let private frame = 1.0f / 60.0f

/// One simulation frame, exactly as `ActorRender.simulate` drives it: re-blend the animated pose, then
/// sway it. The blend first is not a convenience — it is what makes each frame's rest the *animated*
/// pose rather than last frame's sway.
let private step (am: ActorModel) (pose: PoseFrame) (state: Sway.SwayState) (world: Matrix4x4) (bodyRot: Quaternion) (frames: int) : unit =
    for _ in 1 .. frames do
        do Actor.blendPose am idle.Slots pose
        do Sway.apply am pose state frame world bodyRot

/// Where a joint is in the world, from the pose as it stands.
let private worldPosition (pose: PoseFrame) (world: Matrix4x4) (joint: int) : Vector3 =
    Vector3.Transform(pose.World.[joint].Translation, world)

let private angleBetween (a: Vector3) (b: Vector3) : float32 =
    MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize a, Vector3.Normalize b), -1.0f, 1.0f))

// The manifest field, as `world/assets/Assets.fs` declares it — so the authored form is pinned here and not
// only in the one hand-written manifest.
[<SymbolicExpansion>]
type private SwayManifest =
    { Sway : Sway.SwayChain array }

// ── resolution (FR-001, FR-009, FR-010, FR-012) ──

[<Fact>]
let ``the manifest form parses into chains`` () =
    // Pins the authored shape used by `assets/actors/Akane/akane.manifest`: a list of record-shaped
    // elements, each a `[Bones [names]] [Stiffness …]` pair, with the joint names as **bare atoms**.
    // Prime's canonical rendering of a string array is unquoted, and quoted members parse as nothing
    // at all — so this assertion is what stops the manifest being rewritten into a form that silently
    // yields no chains. (Learned the hard way: it did.)
    let value =
        [| { Sway.Bones = [| "h1"; "h2"; "h3" |]; Sway.Stiffness = 0.15f }
           { Sway.Bones = [| "h1"; "h2" |]; Sway.Stiffness = 0.5f } |]
    let text = "[[Sway " + scstring value + "]]"
    Assert.Equal("[[Sway [[[Bones [h1 h2 h3]] [Stiffness 0.15]] [[Bones [h1 h2]] [Stiffness 0.5]]]]]", text)
    let parsed = scvalue<SwayManifest> text
    Assert.Equal(2, parsed.Sway.Length)
    Assert.Equal<string array>([| "h1"; "h2"; "h3" |], parsed.Sway.[0].Bones)
    Assert.Equal(0.15f, parsed.Sway.[0].Stiffness)
    Assert.Equal<string array>([| "h1"; "h2" |], parsed.Sway.[1].Bones)
    // And the shapes of name Akane actually uses survive unquoted.
    let akane = [| { Sway.Bones = [| "J_Bip_C_Head"; "J_Sec_Hair1_01"; "J_Sec_Hair4_12_end" |]; Sway.Stiffness = 0.15f } |]
    let reparsed = scvalue<SwayManifest> ("[[Sway " + scstring akane + "]]")
    Assert.Equal<string array>(akane.[0].Bones, reparsed.Sway.[0].Bones)

[<Fact>]
let ``resolve accepts a chain that is a parent-to-child path`` () =
    match Sway.resolve (chainModel ()) { Bones = chainNames; Stiffness = 0.15f } with
    | Ok resolved -> Assert.Equal<int array>([| 1; 2; 3 |], resolved.Joints)
    | Error why -> Assert.Fail why

[<Fact>]
let ``resolve refuses a chain that is not a path`` () =
    // h3 hangs off h2, not off the root, so [h3, h1] is not a chain.
    match Sway.resolve (chainModel ()) { Bones = [| "h3"; "h1" |]; Stiffness = 0.15f } with
    | Ok _ -> Assert.Fail "a chain that is not a parent-to-child path resolved"
    | Error why -> Assert.Contains("parent-to-child", why)

[<Fact>]
let ``resolve refuses a joint name the skeleton does not carry`` () =
    match Sway.resolve (chainModel ()) { Bones = [| "h1"; "nope" |]; Stiffness = 0.15f } with
    | Ok _ -> Assert.Fail "an unknown joint name resolved"
    | Error why -> Assert.Contains("nope", why)

[<Fact>]
let ``resolve refuses a chain too short to have a segment`` () =
    match Sway.resolve (chainModel ()) { Bones = [| "h1" |]; Stiffness = 0.15f } with
    | Ok _ -> Assert.Fail "a one-joint chain resolved"
    | Error why -> Assert.Contains("at least two", why)

[<Fact>]
let ``resolve refuses a stiffness at 0 or above 1`` () =
    for stiffness in [ 0.0f; -0.1f; 1.5f ] do
        match Sway.resolve (chainModel ()) { Bones = chainNames; Stiffness = stiffness } with
        | Ok _ -> Assert.Fail (sprintf "stiffness %g resolved" stiffness)
        | Error why -> Assert.Contains("stiffness", why)

[<Fact>]
let ``resolve refuses a chain longer than the maximum`` () =
    let names = Array.init (Sway.MaxSwayJointsPerChain + 1) (fun i -> sprintf "h%d" i)
    match Sway.resolve (chainModel ()) { Bones = names; Stiffness = 0.15f } with
    | Ok _ -> Assert.Fail "an over-long chain resolved"
    | Error why -> Assert.Contains("maximum", why)

[<Fact>]
let ``resolveAll keeps what resolved and reports each chain that did not`` () =
    // The whole-actor policy: a typo costs the effect and its siblings survive (FR-009).
    let model = chainModel ()
    let good = { Sway.Bones = chainNames; Sway.Stiffness = 0.15f }
    let badJoint = { Sway.Bones = [| "h1"; "nope" |]; Sway.Stiffness = 0.15f }
    let badStiffness = { Sway.Bones = [| "h1"; "h2" |]; Sway.Stiffness = 0.0f }
    let struct (defs, messages) = Sway.resolveAll model [| good; badJoint; badStiffness; good |]
    Assert.Equal(2, defs.Length)
    Assert.Equal<int array>([| 1; 2; 3 |], defs.[0].Joints)
    Assert.Equal<int array>([| 1; 2; 3 |], defs.[1].Joints)
    Assert.Equal(2, messages.Length)
    Assert.Contains("chain 2", messages.[0])
    Assert.Contains("nope", messages.[0])
    Assert.Contains("chain 3", messages.[1])
    Assert.Contains("stiffness", messages.[1])

[<Fact>]
let ``resolveAll ignores past the chain cap and says so once`` () =
    let model = chainModel ()
    let chain = { Sway.Bones = chainNames; Sway.Stiffness = 0.15f }
    let many = Array.create (Sway.MaxSwayChainsPerActor + 3) chain
    let struct (defs, messages) = Sway.resolveAll model many
    Assert.Equal(Sway.MaxSwayChainsPerActor, defs.Length)
    Assert.Equal(1, messages.Length)
    Assert.Contains("maximum", messages.[0])

// ── the applied rotation (FR-005, FR-006, SC-003, SC-004) ──

[<Fact>]
let ``every segment turns onto the direction the rope reached, at any facing`` () =
    let model = chainModel ()
    let am = Actor.createModel model
    for yaw in [ 0.0f; 0.7f; -2.1f; 3.0f ] do
        let pose = Actor.createPoseFrame model
        let state = Sway.createState [| def 0.15f |]
        let world = actorWorld Vector3.Zero yaw
        do step am pose state world (actorBodyRot yaw) 90
        let points = state.Chains.[0].Points
        for k in 0 .. points.Length - 2 do
            let posed = Vector3.Normalize(worldPosition pose world points.[k + 1].Joint - worldPosition pose world points.[k].Joint)
            let rope = Vector3.Normalize(points.[k + 1].Position - points.[k].Position)
            // `> 0.999` is ~2.6°: the slack the rope's constraint solver leaves, not the arithmetic —
            // the applied rotation puts the segment on the rope's direction exactly.
            Assert.True(Vector3.Dot(posed, rope) > 0.999f, sprintf "yaw %f segment %d: posed %A rope %A" yaw k posed rope)

[<Fact>]
let ``the same motion bends the same way relative to the actor, at any facing`` () =
    // Turning the actor and its whole world by θ about the *gravity* axis leaves the body's view of
    // gravity unchanged, so the rope — and therefore every local rotation the sway writes — must come
    // out identical. That is the property the reference's inverted parent frame breaks: its bend mirrors
    // as soon as the parent is turned (spec Origin, defect 1; SC-003).
    let model = chainModel ()
    let am = Actor.createModel model
    let runs = ResizeArray<Quaternion array>()
    for yaw in [ 0.0f; 0.9f; -2.1f ] do
        let pose = Actor.createPoseFrame model
        let state = Sway.createState [| def 0.15f |]
        let turn = Matrix4x4.CreateFromQuaternion(actorRotation yaw)
        for i in 1 .. 60 do
            do Actor.blendPose am idle.Slots pose
            // The actor walks along its own forward, so the whole run is the un-yawed one turned about
            // the world origin by the yaw — and gravity points down that axis, so it is unchanged too.
            let walked = Vector3.Transform(Vector3(float32 i * 0.03f, 0.0f, 0.0f), turn)
            do Sway.apply am pose state frame (actorWorld walked yaw) (actorBodyRot yaw)
        runs.Add(Array.init 3 (fun k -> pose.Rotation.[k + 1]))
    let reference = runs.[0]
    for i in 1 .. runs.Count - 1 do
        for k in 0 .. 2 do
            Assert.True(abs (Quaternion.Dot(runs.[i].[k], reference.[k])) > 0.99999f, sprintf "yaw %d joint %d bent differently" i k)

// ── motion (FR-003, FR-004, SC-001, SC-002) ──

[<Fact>]
let ``a chain trails behind the direction of travel`` () =
    let model = chainModel ()
    let am = Actor.createModel model
    let pose = Actor.createPoseFrame model
    let state = Sway.createState [| def 0.15f |]
    do step am pose state (actorWorld Vector3.Zero 0.0f) (actorBodyRot 0.0f) 60
    let points = state.Chains.[0].Points
    let tip = points.Length - 1
    let tipJoint = points.[tip].Joint
    // Walk +X, and each frame measure how far the tip's rope position sits behind where the animated
    // pose (travelling with the actor) puts it. `points.[tip]` is read fresh every time: a `SwayPoint`
    // is a struct, so a binding taken once would freeze at its value here.
    let mutable mostTrailing = 0.0f
    let mutable mostLeading = 0.0f
    let mutable sum = 0.0f
    let mutable worstTurn = 0.0f
    for i in 1 .. 60 do
        do Actor.blendPose am idle.Slots pose
        let travelled = Vector3(float32 i * 0.06f, 0.0f, 0.0f)
        do Sway.apply am pose state frame (actorWorld travelled 0.0f) (actorBodyRot 0.0f)
        do Actor.blendPose am idle.Slots pose
        let trail = Vector3.Dot(points.[tip].Position - worldPosition pose (actorWorld travelled 0.0f) tipJoint, Vector3.UnitX)
        mostTrailing <- min mostTrailing trail
        mostLeading <- max mostLeading trail
        sum <- sum + trail
        // SC-001's second clause: a steady walk never drives a segment to the bound, so nothing is
        // clamped — the clamp is there for a hitch, not for walking (FR-007).
        for k in 0 .. points.Length - 2 do
            let turn = angleBetween (points.[k + 1].Rest - points.[k].Rest) (points.[k + 1].Position - points.[k].Position)
            worstTurn <- max worstTurn turn
    Assert.True(worstTurn < Sway.SwayMaxAngle, sprintf "a steady walk drove a segment to the bound: %f of %f rad" worstTurn Sway.SwayMaxAngle)
    // A rope dragged by a moving anchor swings: it trails on the response and then overshoots forward,
    // as hair does. So the promise is inertia (it goes behind first, and sits behind on average) and a
    // bound (it never runs away ahead of the pose), not that it is never ahead at all (FR-003).
    Assert.True(mostTrailing < -0.005f, sprintf "the chain never trailed: %f" mostTrailing)
    Assert.True(sum < 0.0f, sprintf "the chain sat ahead of the pose on average: %f" sum)
    Assert.True(mostLeading < 0.1f, sprintf "the chain ran away ahead of the motion: %f" mostLeading)

[<Fact>]
let ``with no motion a chain settles and holds the animated pose`` () =
    let model = chainModel ()
    let am = Actor.createModel model
    let pose = Actor.createPoseFrame model
    let state = Sway.createState [| def 0.9f |]
    let world = actorWorld Vector3.Zero 0.0f
    let bodyRot = actorBodyRot 0.0f
    // How far the rope sits from the pose it is anchored to.
    let deviation () =
        let points = state.Chains.[0].Points
        let mutable worst = 0.0f
        for k in 0 .. points.Length - 2 do
            worst <- max worst (angleBetween (points.[k + 1].Rest - points.[k].Rest) (points.[k + 1].Position - points.[k].Position))
        worst
    do step am pose state world bodyRot 60
    let early = deviation ()
    do step am pose state world bodyRot 120
    let settled = deviation ()
    // Gravity is always on, so a chain settles to the pose *plus* the droop the spring balances: the
    // promise is that it stops there, not that the offset is zero (FR-004).
    Assert.True(settled < 0.15f, sprintf "the chain did not settle back to the pose: %f rad" settled)
    Assert.True(abs (settled - early) < 0.01f, sprintf "the chain was still moving: %f then %f" early settled)

// ── bounds, teleports and inertness (FR-007, FR-012, FR-013, FR-015) ──

[<Fact>]
let ``a segment cannot turn further than the bound`` () =
    let model = chainModel ()
    let am = Actor.createModel model
    let pose = Actor.createPoseFrame model
    let state = Sway.createState [| def 0.9f |]
    let world = actorWorld Vector3.Zero 0.0f
    let bodyRot = actorBodyRot 0.0f
    do step am pose state world bodyRot 1
    let points = state.Chains.[0].Points
    // Displace the rope rigidly, a quarter turn off the chain, lengths untouched.
    let rotation = Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2.0f))
    let mutable placed = points.[0].Rest
    let anchor = &points.[0]
    anchor.Position <- placed
    anchor.Previous <- placed
    for k in 1 .. points.Length - 1 do
        placed <- placed + Vector3.Transform(points.[k].Rest - points.[k - 1].Rest, rotation)
        let point = &points.[k]
        point.Position <- placed
        point.Previous <- placed
    do Actor.blendPose am idle.Slots pose
    do Sway.apply am pose state frame world bodyRot
    for k in 0 .. points.Length - 2 do
        let rest = Vector3.Normalize(points.[k + 1].Rest - points.[k].Rest)
        let posed = Vector3.Normalize(worldPosition pose world points.[k + 1].Joint - worldPosition pose world points.[k].Joint)
        let turn = angleBetween rest posed
        Assert.True(turn <= Sway.SwayMaxAngle + 0.02f, sprintf "segment %d turned %f rad, over the %f bound" k turn Sway.SwayMaxAngle)

[<Fact>]
let ``a teleport re-seeds the chain instead of yanking it`` () =
    let model = chainModel ()
    let am = Actor.createModel model
    let pose = Actor.createPoseFrame model
    let state = Sway.createState [| def 0.15f |]
    // How far the rope sits from its rest, over the chain.
    let deviation () =
        let points = state.Chains.[0].Points
        let mutable worst = 0.0f
        for k in 0 .. points.Length - 1 do
            worst <- max worst (Vector3.Distance(points.[k].Position, points.[k].Rest))
        worst
    do step am pose state (actorWorld Vector3.Zero 0.0f) (actorBodyRot 0.0f) 60
    Assert.True(deviation () > 0.001f, "the fixture is not swaying, so a re-seed would not show")
    // A small step is travel: the rope lags and is not re-seeded.
    do Actor.blendPose am idle.Slots pose
    do Sway.apply am pose state frame (actorWorld (Vector3(0.1f, 0.0f, 0.0f)) 0.0f) (actorBodyRot 0.0f)
    Assert.True(deviation () > 0.001f, "ordinary travel re-seeded the chain")
    // A jump of ten units is a teleport: the chain comes back to the pose rather than snapping back.
    do Actor.blendPose am idle.Slots pose
    do Sway.apply am pose state frame (actorWorld (Vector3(10.0f, 0.0f, 0.0f)) 0.0f) (actorBodyRot 0.0f)
    Assert.True(deviation () < 0.01f, sprintf "the teleport left the chain displaced: %f" (deviation ()))

[<Fact>]
let ``an actor that declares nothing, and a frame with no time, are not written to`` () =
    let model = chainModel ()
    let am = Actor.createModel model
    let pose = Actor.createPoseFrame model
    do Actor.blendPose am idle.Slots pose
    let before = Array.copy pose.Rotation
    do Sway.apply am pose empty frame (actorWorld Vector3.Zero 0.0f) (actorBodyRot 0.0f)
    Assert.Equal<Quaternion array>(before, pose.Rotation)
    do Sway.apply am pose (Sway.createState [| def 0.15f |]) 0.0f (actorWorld Vector3.Zero 0.0f) (actorBodyRot 0.0f)
    Assert.Equal<Quaternion array>(before, pose.Rotation)

[<Fact>]
let ``two chains on one actor keep their own rope`` () =
    let state = Sway.createState [| def 0.15f; { Joints = [| 1; 2; 3 |]; Stiffness = 0.5f } |]
    Assert.Equal(2, state.Chains.Length)
    Assert.False(Object.ReferenceEquals(state.Chains.[0].Points, state.Chains.[1].Points))

[<Fact>]
let ``stepping a chain allocates nothing per frame`` () =
    let model = chainModel ()
    let am = Actor.createModel model
    let pose = Actor.createPoseFrame model
    let state = Sway.createState [| def 0.15f |]
    let world = actorWorld Vector3.Zero 0.3f
    let bodyRot = actorBodyRot 0.3f
    do step am pose state world bodyRot 100
    let before = GC.GetAllocatedBytesForCurrentThread()
    do step am pose state world bodyRot 500
    let after = GC.GetAllocatedBytesForCurrentThread()
    Assert.Equal(before, after)
