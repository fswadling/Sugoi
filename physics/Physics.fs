module Physics

// Jolt Physics adapter — a leaf over `JoltPhysicsSharp` + `System.Numerics`, with
// no dependency on `Entity`, `Zone`, or the GPU. Bodies are created/destroyed on
// editor edits (cold) and stepped once per frame. A `BodyId` is the Jolt body
// index + sequence number, exposed opaquely so the rest of the code never touches
// Jolt directly.

open System
open System.Numerics
open JoltPhysicsSharp

// Opaque body handle (Jolt's body index + sequence number).
[<Struct>]
type BodyId = BodyId of uint32

// Two object layers, mapped 1:1 to two broad-phase layers: static geometry
// (props/environment) and the moving actor capsule.
let private layerNonMoving = ObjectLayer(0u)
let private layerMoving = ObjectLayer(1u)

type World =
    { System : PhysicsSystem
      JobSystem : JobSystemThreadPool
      // Jolt's PhysicsSystem keeps raw pointers to the filters below (and reads its
      // settings), so they must outlive it. If the GC finalises these managed wrappers
      // the native objects are freed, the filter pointers dangle, and collision
      // silently stops working — actors fall straight through the world.
      Settings : PhysicsSystemSettings
      PairFilter : ObjectLayerPairFilterTable
      BroadPhase : BroadPhaseLayerInterfaceTable
      ObjectVsBroadPhase : ObjectVsBroadPhaseLayerFilterTable }

let create () : World =
    do Foundation.Init(false) |> ignore

    let pairFilter = new ObjectLayerPairFilterTable(2u)
    pairFilter.EnableCollision(layerNonMoving, layerMoving)
    pairFilter.EnableCollision(layerMoving, layerMoving)

    let broadPhase = new BroadPhaseLayerInterfaceTable(2u, 2u)
    broadPhase.MapObjectToBroadPhaseLayer(layerNonMoving, BroadPhaseLayer(0uy))
    broadPhase.MapObjectToBroadPhaseLayer(layerMoving, BroadPhaseLayer(1uy))

    let objectVsBroadPhase = new ObjectVsBroadPhaseLayerFilterTable(broadPhase, 2u, pairFilter, 2u)

    let mutable settings = new PhysicsSystemSettings()
    settings.MaxBodies <- 65536
    settings.MaxBodyPairs <- 65536
    settings.MaxContactConstraints <- 65536
    settings.NumBodyMutexes <- 0
    settings.ObjectLayerPairFilter <- pairFilter
    settings.BroadPhaseLayerInterface <- broadPhase
    settings.ObjectVsBroadPhaseLayerFilter <- objectVsBroadPhase

    let system = new PhysicsSystem(settings)
    system.Gravity <- Vector3(0.0f, -9.81f, 0.0f)
    { System = system
      JobSystem = new JobSystemThreadPool()
      Settings = settings
      PairFilter = pairFilter
      BroadPhase = broadPhase
      ObjectVsBroadPhase = objectVsBroadPhase }

let step (world: World) (dt: float32) : unit =
    do world.System.Update(dt, 1, world.JobSystem) |> ignore

let private createBody (world: World) (shape: Shape) (position: Vector3) (rotation: Quaternion) (motionType: MotionType) (allowedDOFs: AllowedDOFs) (allowSleeping: bool) (layer: ObjectLayer) : BodyId =
    let mutable pos = position
    let mutable rot = rotation
    use settings = new BodyCreationSettings(shape, &pos, &rot, motionType, layer)
    settings.AllowedDOFs <- allowedDOFs
    settings.AllowSleeping <- allowSleeping
    let body = world.System.BodyInterface.CreateBody(settings)
    let mutable bodyId = body.ID
    do world.System.BodyInterface.AddBody(&bodyId, Activation.Activate)
    BodyId body.ID.ID

// Static bodies never move, so their allowed degrees of freedom are irrelevant.
let private allDOFs : AllowedDOFs = AllowedDOFs.All

// Actor capsules are dynamic so the engine resolves gravity and collisions, but
// rotation is locked about every axis so the character stays upright (its facing is
// written directly). Sleeping is disabled so the body keeps responding to input.
let private translationOnly : AllowedDOFs =
    LanguagePrimitives.EnumOfValue(int AllowedDOFs.TranslationX ||| int AllowedDOFs.TranslationY ||| int AllowedDOFs.TranslationZ)

// Static body from a triangle mesh (Jolt `MeshShape`, with its BVH). A mesh
// shape cannot be scaled on the body, so the caller bakes any entity scale into
// the vertices; the body carries position + rotation only.
let createMeshBody (world: World) (position: Vector3) (rotation: Quaternion) (vertices: Vector3 array) (indices: int array) : BodyId =
    let triangles = Array.zeroCreate<IndexedTriangle> (indices.Length / 3)
    for i in 0 .. triangles.Length - 1 do
        let mutable i1 = indices.[i * 3]
        let mutable i2 = indices.[i * 3 + 1]
        let mutable i3 = indices.[i * 3 + 2]
        triangles.[i] <- IndexedTriangle(&i1, &i2, &i3, 0u, 0u)
    use settings = new MeshShapeSettings(vertices, triangles)
    let shape = settings.Create()
    createBody world shape position rotation MotionType.Static allDOFs false layerNonMoving

let createCapsuleBody (world: World) (position: Vector3) (halfHeight: float32) (radius: float32) : BodyId =
    let shape = new CapsuleShape(halfHeight, radius)
    createBody world shape position Quaternion.Identity MotionType.Dynamic translationOnly false layerMoving

let destroyBody (world: World) (BodyId id) : unit =
    let mutable bodyId = BodyID(id)
    do world.System.BodyInterface.RemoveAndDestroyBody(&bodyId)

// ── Dynamic actor body access ──
//
// The locomotion systems read and write the actor's body directly: they set the
// horizontal velocity and facing, then read the resolved transform back after the
// step. The vertical velocity is left to gravity.

let getBodyVelocity (world: World) (BodyId id) : Vector3 =
    let mutable bodyId = BodyID(id)
    world.System.BodyInterface.GetLinearVelocity(&bodyId)

let setBodyVelocity (world: World) (BodyId id) (velocity: Vector3) : unit =
    let mutable bodyId = BodyID(id)
    let mutable v = velocity
    do world.System.BodyInterface.SetLinearVelocity(&bodyId, &v)

let setBodyRotation (world: World) (BodyId id) (rotation: Quaternion) : unit =
    let mutable bodyId = BodyID(id)
    let mutable r = rotation
    do world.System.BodyInterface.SetRotation(&bodyId, &r, Activation.Activate)

// The body's position (capsule centre) and rotation, read after a step.
let getBodyTransform (world: World) (BodyId id) : Vector3 * Quaternion =
    let mutable bodyId = BodyID(id)
    let position = world.System.BodyInterface.GetPosition(&bodyId)
    let rotation = world.System.BodyInterface.GetRotation(&bodyId)
    position, rotation

let setBodyTransform (world: World) (BodyId id) (position: Vector3) (rotation: Quaternion) : unit =
    let mutable bodyId = BodyID(id)
    let mutable pos = position
    let mutable rot = rotation
    do world.System.BodyInterface.SetPositionAndRotation(&bodyId, &pos, &rot, Activation.DontActivate)

let raycast (world: World) (origin: Vector3) (dir: Vector3) : (BodyId * float32) option =
    let mutable o = origin
    let mutable d = dir
    let mutable ray = Ray(&o, &d)
    let mutable hit = RayCastResult.Default
    let query = world.System.NarrowPhaseQuery
    if query.CastRay(&ray, &hit) then
        Some (BodyId hit.BodyID.ID, hit.Fraction)
    else
        None
