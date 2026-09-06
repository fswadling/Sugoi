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
      JobSystem : JobSystemThreadPool }

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
    { System = system; JobSystem = new JobSystemThreadPool() }

let step (world: World) (dt: float32) : unit =
    do world.System.Update(dt, 1, world.JobSystem) |> ignore

let private createBody (world: World) (shape: Shape) (position: Vector3) (rotation: Quaternion) (motionType: MotionType) (layer: ObjectLayer) : BodyId =
    let mutable pos = position
    let mutable rot = rotation
    use settings = new BodyCreationSettings(shape, &pos, &rot, motionType, layer)
    let body = world.System.BodyInterface.CreateBody(settings)
    let mutable bodyId = body.ID
    do world.System.BodyInterface.AddBody(&bodyId, Activation.DontActivate)
    BodyId body.ID.ID

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
    createBody world shape position rotation MotionType.Static layerNonMoving

let createCapsuleBody (world: World) (position: Vector3) (halfHeight: float32) (radius: float32) : BodyId =
    let shape = new CapsuleShape(halfHeight, radius)
    createBody world shape position Quaternion.Identity MotionType.Kinematic layerMoving

let destroyBody (world: World) (BodyId id) : unit =
    let mutable bodyId = BodyID(id)
    do world.System.BodyInterface.RemoveAndDestroyBody(&bodyId)

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
