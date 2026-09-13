module Zone

// The world container for one playable area: actors, placed props, an optional
// loaded environment (which owns its own part slot map), and the static-scene batch
// that renders its props and parts. Heap-allocated; its entity collections are
// fixed-size generational slot maps, mutated in place.
//
// The zone does not own the physics world — it is shared via `SharedState` and
// passed in per call; the zone keeps only the body→entity map. Bodies are
// created/destroyed on spawn/remove and recreated on transform change, so
// colliders never drift from the rendered geometry.

open System.Collections.Generic
open System.Numerics
open Entity
open SlotMap

type Zone =
    { Name : string
      Actors : SlotMap<Actor>
      mutable Environment : Environment option
      Props : SlotMap<Prop>
      Batch : EnvironmentBatch.EnvironmentBatch
      mutable NextEnvironmentEpoch : uint32
      BodyToEntity : Dictionary<Physics.BodyId, EntityRef> }

let create () : Zone =
    { Name = ""
      Actors = SlotMap<Actor>(64u)
      Environment = None
      // Placed props only — an environment's imported parts live in its own map.
      Props = SlotMap<Prop>(32u)
      Batch = EnvironmentBatch.create ()
      NextEnvironmentEpoch = 0u
      BodyToEntity = Dictionary<Physics.BodyId, EntityRef>() }

// ── static-scene batch ──

// Gather this zone's static scene through its own batch. The one place that binds a
// zone to its batch (the batch itself does not reference `Zone`). Created without a
// device; the buffer is allocated on first gather.
let gatherStatic (device: Gpu.Device) (zone: Zone) : unit =
    let parts =
        match zone.Environment with
        | Some environment -> ValueSome environment.Parts
        | None -> ValueNone
    EnvironmentBatch.gather device zone.Batch zone.Props parts

// ── Ref resolution ──

// The parts map a Part ref belongs to, but only when that exact environment is the
// loaded one — a ref from a previous environment returns None, so it can neither
// read nor mutate a same-index part in the current environment.
let partsFor (zone: Zone) (ref: EntityRef) : SlotMap<Prop> option =
    match zone.Environment with
    | Some environment when environment.Epoch = ref.Epoch -> Some environment.Parts
    | _ -> None

// Whether the ref addresses the live slot it originally named. Kinds without slot
// storage (environment, waypoint, trigger) are never alive.
let isAlive (zone: Zone) (ref: EntityRef) : bool =
    match ref.Kind with
    | EntityKind.Actor -> zone.Actors.IsAlive(handleOf ref)
    | EntityKind.Prop -> zone.Props.IsAlive(handleOf ref)
    | EntityKind.Part ->
        match partsFor zone ref with
        | Some parts -> parts.IsAlive(handleOf ref)
        | None -> false
    | EntityKind.Environment
    | EntityKind.Waypoint
    | EntityKind.Trigger -> false

// The base entity behind a ref (Actor/Prop/Part), or None for other kinds or a
// stale/absent slot. Returned by value (a cheap struct copy) so callers can't
// write a transform that would bypass the body recreation in `setTransform`.
let tryEntityAt (zone: Zone) (ref: EntityRef) : Entity option =
    match ref.Kind with
    | EntityKind.Actor ->
        if not (zone.Actors.IsAlive(handleOf ref)) then None else
        let actor = &zone.Actors.Get(handleOf ref)
        Some actor.Base
    | EntityKind.Prop ->
        if not (zone.Props.IsAlive(handleOf ref)) then None else
        let prop = &zone.Props.Get(handleOf ref)
        Some prop.Base
    | EntityKind.Part ->
        match partsFor zone ref with
        | None -> None
        | Some parts ->
        if not (parts.IsAlive(handleOf ref)) then None else
        let part = &parts.Get(handleOf ref)
        Some part.Base
    | _ -> None

// ── Collider derivation ──

// Mesh collider merging a contiguous submesh range into one flat vertex/index
// buffer (node-local, unscaled). Fails loudly on an empty range — a part with
// no triangles is a content bug, not a collider-less entity.
let meshCollider (submeshes: EnvironmentAsset.StaticSubmesh array) (first: int) (count: int) : MeshCollider =
    if first < 0 || count <= 0 || first + count > submeshes.Length then
        failwithf "mesh collider: invalid submesh range (first %d, count %d, of %d submeshes)" first count submeshes.Length
    let mutable positionFloats = 0
    let mutable indexCount = 0
    for i in first .. first + count - 1 do
        positionFloats <- positionFloats + submeshes.[i].Positions.Length
        indexCount <- indexCount + submeshes.[i].Indices.Length
    if indexCount = 0 then
        failwithf "mesh collider: submesh range (first %d, count %d) has no triangles" first count
    let positions = Array.zeroCreate<float32> positionFloats
    let indices = Array.zeroCreate<int> indexCount
    let mutable p = 0
    let mutable t = 0
    let mutable baseVertex = 0
    for i in first .. first + count - 1 do
        let s = submeshes.[i]
        Array.blit s.Positions 0 positions p s.Positions.Length
        for k in 0 .. s.Indices.Length - 1 do
            indices.[t + k] <- s.Indices.[k] + baseVertex
        p <- p + s.Positions.Length
        t <- t + s.Indices.Length
        baseVertex <- baseVertex + s.Positions.Length / 3
    { Positions = positions; Indices = indices }

// The actor capsule radius, matching odin-game's DEFAULT_ACTOR_COLLIDER_RADIUS.
// Actors always collide as a fixed-width capsule around their torso rather than
// a radius derived from their (often arm-span-dominated) bounds.
let defaultActorColliderRadius = 0.35f

// Capsule from asset bounds. IQM is Z-up, so the vertical (height) extent is Z;
// the capsule spans that full height (feet at the entity position) with a fixed
// radius.
let capsuleCollider (bounds: Gltf.Bounds) : CapsuleCollider =
    let height = bounds.Max.Z - bounds.Min.Z
    let halfHeight = max 0.0f (height / 2.0f - defaultActorColliderRadius)
    { Radius = defaultActorColliderRadius; HalfHeight = halfHeight }

// A prop's collider from its binding: a placed prop asset (`Some id`) meshes its
// whole model; an environment part (`None`) meshes that part's submesh range.
// Unknown asset ids throw via the dictionary indexer.
let private propCollider (zone: Zone) (assets: Assets.AssetManager) (binding: AssetBinding) : MeshCollider option =
    match binding.AssetId with
    | Some assetId ->
        let asset = assets.Props.[assetId]
        Some (meshCollider asset.Submeshes 0 asset.Submeshes.Length)
    | None ->
    match zone.Environment with
    | None -> failwith "environment part without a loaded environment"
    | Some environment ->
    let asset = assets.Environments.[environment.AssetId]
    match asset.Parts |> Array.tryFind (fun p -> p.SubmeshIndex = binding.SubmeshIndex) with
    | None -> None
    | Some part ->
    Some (meshCollider asset.Submeshes part.SubmeshIndex part.SubmeshCount)


let private actorCollider (assets: Assets.AssetManager) (assetId: string) : CapsuleCollider =
    capsuleCollider assets.Actors.[assetId].Bounds

// ── Physics attachment ──

// Bodies are built from the entity's collider and the two are stored together
// (`Entity.PhysicsAttachment`), so a body can never exist without its collider.

let private meshBody (world: Physics.World) (collider: MeshCollider) (transform: Transform) : Physics.BodyId =
    // Bake the entity scale into the vertices (a mesh shape cannot be scaled
    // on the body); the body takes position + rotation.
    let scale = transform.Scale
    let vertices =
        Array.init (collider.Positions.Length / 3) (fun i ->
            Vector3(collider.Positions.[i * 3] * scale.X, collider.Positions.[i * 3 + 1] * scale.Y, collider.Positions.[i * 3 + 2] * scale.Z))
    Physics.createMeshBody world transform.Position transform.Rotation vertices collider.Indices

// The world-space centre of an actor's capsule: the entity position is the feet,
// so the capsule (cylinder half-height + hemisphere radius) sits above it.
let capsuleCenter (transform: Transform) (collider: CapsuleCollider) : Vector3 =
    transform.Position + Vector3(0.0f, collider.HalfHeight + collider.Radius, 0.0f)

let private capsuleBody (world: Physics.World) (collider: CapsuleCollider) (transform: Transform) : Physics.BodyId =
    Physics.createCapsuleBody world (capsuleCenter transform collider) collider.HalfHeight collider.Radius

// Attach physics to an entity: derive its collider and build the body in the
// supplied world; for props, the attachment is None when the part binding is
// stale and no collider can be derived.
let private attachActorPhysics (world: Physics.World) (assets: Assets.AssetManager) (assetId: string) (transform: Transform) : PhysicsAttachment<CapsuleCollider> option =
    let collider = actorCollider assets assetId
    Some { Collider = collider; Body = capsuleBody world collider transform }

let private attachPropPhysics (world: Physics.World) (zone: Zone) (assets: Assets.AssetManager) (binding: AssetBinding) (transform: Transform) : PhysicsAttachment<MeshCollider> option =
    match propCollider zone assets binding with
    | None -> None
    | Some collider ->
    Some { Collider = collider; Body = meshBody world collider transform }


let private destroyBody (world: Physics.World) (zone: Zone) (attachment: PhysicsAttachment<'T> option) : unit =
    match attachment with
    | None -> ()
    | Some ph ->
    do Physics.destroyBody world ph.Body
    do zone.BodyToEntity.Remove(ph.Body) |> ignore


let private mapBody (zone: Zone) (attachment: PhysicsAttachment<'T> option) (ref: EntityRef) : unit =
    match attachment with
    | None -> ()
    | Some ph ->
    do zone.BodyToEntity.[ph.Body] <- ref

// The asset binding behind a ref (asset id +, for props/parts, the submesh range).
// Raises for a stale or absent entity and for kinds without a binding.
let bindingAt (zone: Zone) (ref: EntityRef) : AssetBinding =
    match ref.Kind with
    | EntityKind.Actor ->
        let a = &zone.Actors.Get(handleOf ref)
        { AssetId = Some a.AssetId; SubmeshIndex = 0; SubmeshCount = 0 }
    | EntityKind.Prop ->
        let p = &zone.Props.Get(handleOf ref)
        { AssetId = p.AssetId; SubmeshIndex = p.SubmeshIndex; SubmeshCount = p.SubmeshCount }
    | EntityKind.Part ->
        match partsFor zone ref with
        | None -> failwithf "bindingAt: part ref %A does not belong to the loaded environment" ref
        | Some parts ->
        let p = &parts.Get(handleOf ref)
        { AssetId = p.AssetId; SubmeshIndex = p.SubmeshIndex; SubmeshCount = p.SubmeshCount }
    | _ -> failwithf "bindingAt: kind %A has no asset binding" ref.Kind

// Change a mesh-backed entity's transform in `map` (a placed prop or an
// environment part), recreating its body so the collider follows.
let private setMeshTransform (zone: Zone) (world: Physics.World) (map: SlotMap<Prop>) (ref: EntityRef) (transform: Transform) : unit =
    if not (map.IsAlive(handleOf ref)) then () else

    let p = &map.Get(handleOf ref)
    do destroyBody world zone p.Physics
    let attachment =
        match p.Physics with
        | Some ph -> Some { ph with Body = meshBody world ph.Collider transform }
        | None -> None
    do mapBody zone attachment ref
    p <- { p with Base = { p.Base with Transform = transform }; Physics = attachment }

// Change an existing entity's transform at the ref (Actor/Prop/Part), recreating
// its body so it follows. No-op for other kinds or a stale/absent ref.
let setTransform (zone: Zone) (world: Physics.World) (ref: EntityRef) (transform: Transform) : unit =
    match ref.Kind with
    | EntityKind.Actor ->
        if not (isAlive zone ref) then () else

        let a = &zone.Actors.Get(handleOf ref)
        do destroyBody world zone a.Physics
        let attachment =
            match a.Physics with
            | Some ph -> Some { ph with Body = capsuleBody world ph.Collider transform }
            | None -> None
        do mapBody zone attachment ref
        a <- { a with Base = { a.Base with Transform = transform }; Physics = attachment }

    | EntityKind.Prop ->
        do setMeshTransform zone world zone.Props ref transform

    | EntityKind.Part ->
        match partsFor zone ref with
        | None -> ()
        | Some parts ->
        do setMeshTransform zone world parts ref transform

    | _ -> ()

// Build a prop renderer from a binding: a placed prop asset (`Some id`) or an
// environment part (`None`, the part's submesh range in the zone's environment).
let private propRenderer (zone: Zone) (assets: Assets.AssetManager) (binding: AssetBinding) : EnvironmentRender.EnvironmentRender =
    match binding.AssetId with
    | Some assetId -> Assets.createPropRender assets assetId
    | None ->
    match zone.Environment with
    | Some environment -> Assets.createPartRender assets environment.AssetId binding.SubmeshIndex binding.SubmeshCount
    | None -> failwith "environment part without a loaded environment"

// `Occupy` requires the recorded slot to be free. Check before building any GPU or
// physics resources, so a slot stolen by an untracked `Add` fails cheaply rather
// than after allocating a renderer and a body that the raise would leak.
let private ensureSlotFree (map: SlotMap<'T>) (ref: EntityRef) : unit =
    if map.IsOccupied(ref.Index) then
        invalidOp (sprintf "slot %d of ref %A is occupied — a recorded slot was reused by an untracked Add" ref.Index ref)

// Write an entity (base + binding + collider + body) back at the ref, for command
// apply/undo to re-create one that was removed. The recorded slot is always free
// here — entities are only ever added to their own kind's map by `Spawning`, which
// pairs every `Add` with a command — so a taken slot means an untracked `Add` and
// `Occupy` raises rather than clobbering the occupant.
let setActorWithAsset (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (ref: EntityRef) (entity: Entity) (binding: AssetBinding) : unit =
    match ref.Kind with
    | EntityKind.Actor ->
        do ensureSlotFree zone.Actors ref
        let assetId =
            match binding.AssetId with
            | Some id -> id
            | None -> failwith "actor binding without an asset id"
        let renderer = Assets.createActorRender assets assetId
        let attachment = attachActorPhysics world assets assetId entity.Transform
        let actor = { Base = entity; AssetId = assetId; Physics = attachment; ShowCollider = false; Renderer = renderer }
        zone.Actors.Occupy(handleOf ref, actor)
        do mapBody zone attachment ref
    | EntityKind.Prop ->
        do ensureSlotFree zone.Props ref
        let renderer = propRenderer zone assets binding
        let attachment = attachPropPhysics world zone assets binding entity.Transform
        let prop =
            { Base = entity; AssetId = binding.AssetId; SubmeshIndex = binding.SubmeshIndex; SubmeshCount = binding.SubmeshCount; Physics = attachment; Renderer = renderer }
        zone.Props.Occupy(handleOf ref, prop)
        do mapBody zone attachment ref
    | EntityKind.Part ->
        // A part restore only runs while the environment it names is loaded: swaps
        // are commands and the stack is LIFO, so a swap is undone before the part
        // commands that followed it. An epoch mismatch is therefore a broken
        // invariant, not a no-op.
        match partsFor zone ref with
        | Some parts ->
            do ensureSlotFree parts ref
            let renderer = propRenderer zone assets binding
            let attachment = attachPropPhysics world zone assets binding entity.Transform
            let prop =
                { Base = entity; AssetId = binding.AssetId; SubmeshIndex = binding.SubmeshIndex; SubmeshCount = binding.SubmeshCount; Physics = attachment; Renderer = renderer }
            parts.Occupy(handleOf ref, prop)
            do mapBody zone attachment ref
        | None -> failwithf "setActorWithAsset: part ref %A does not belong to the loaded environment" ref
    | _ -> ()

// Remove a mesh-backed entity (placed prop or environment part) from `map`,
// destroying its body. No-op for a stale ref.
let private removeMeshEntity (zone: Zone) (world: Physics.World) (map: SlotMap<Prop>) (ref: EntityRef) : unit =
    if map.IsAlive(handleOf ref) then
        // Copy the attachment out first: `Remove` mutates the slot.
        let physics = (map.Get(handleOf ref)).Physics
        do destroyBody world zone physics
        map.Remove(handleOf ref, Some Prop.releaseRenderer)

// Remove the entity at the ref (Actor/Prop/Part), destroying its body. No-op for
// other kinds or a stale ref (its generation no longer matches the slot).
let removeEntity (zone: Zone) (world: Physics.World) (ref: EntityRef) : unit =
    match ref.Kind with
    | EntityKind.Actor ->
        if isAlive zone ref then
            let physics = (zone.Actors.Get(handleOf ref)).Physics
            do destroyBody world zone physics
            zone.Actors.Remove(handleOf ref, Some Actor.releaseRenderer)
    | EntityKind.Prop ->
        do removeMeshEntity zone world zone.Props ref
    | EntityKind.Part ->
        match partsFor zone ref with
        | Some parts -> do removeMeshEntity zone world parts ref
        | None -> ()
    | _ -> ()

// Spawn a new actor and return the new ref (renderer + physics attached).
let spawnActor (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (entity: Entity) (assetId: string) : EntityRef =
    let renderer = Assets.createActorRender assets assetId
    let attachment = attachActorPhysics world assets assetId entity.Transform
    let handle = zone.Actors.Add({ Base = entity; AssetId = assetId; Physics = attachment; ShowCollider = false; Renderer = renderer })
    let ref = actorRef handle.Index handle.Generation
    do mapBody zone attachment ref
    ref

// Build a mesh-backed prop value from an entity + binding, with no resources yet
// (used while building an environment's parts map; activation attaches them).
let private meshProp (entity: Entity) (binding: AssetBinding) : Prop =
    { Base = entity
      AssetId = binding.AssetId
      SubmeshIndex = binding.SubmeshIndex
      SubmeshCount = binding.SubmeshCount
      Physics = None
      Renderer = Unchecked.defaultof<EnvironmentRender.EnvironmentRender> }

// Spawn a placed prop from a full binding (a prop asset, whole model).
let spawnPropBinding (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (entity: Entity) (binding: AssetBinding) : EntityRef =
    let renderer = propRenderer zone assets binding
    let attachment = attachPropPhysics world zone assets binding entity.Transform
    let prop = { meshProp entity binding with Physics = attachment; Renderer = renderer }
    let handle = zone.Props.Add(prop)
    let ref = propRef handle.Index handle.Generation
    do mapBody zone attachment ref
    ref

// Spawn a placed prop asset (whole model).
let spawnProp (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (entity: Entity) (assetId: string) : EntityRef =
    spawnPropBinding zone world assets entity { AssetId = Some assetId; SubmeshIndex = 0; SubmeshCount = 0 }

// ── Environment parts ──
//
// A swap tears down the outgoing parts' bodies/renderers and rebuilds the incoming
// ones but retains the slot map, so slots (and generations) survive — a part ref
// held by the undo stack resolves to the same part afterwards, and no respawn can
// steal a reintroduction's slot.

// Build a renderer + body for a part, mapping the body back to a `Part` ref stamped
// with the environment's epoch. `zone.Environment` must already be `environment`
// (the part renderer resolves its asset through it).
let private attachPart (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (environment: Environment) (handle: Handle) : unit =
    let parts = environment.Parts
    let p = &parts.Get(handle)
    let transform = p.Base.Transform
    let binding = { AssetId = p.AssetId; SubmeshIndex = p.SubmeshIndex; SubmeshCount = p.SubmeshCount }
    let renderer = propRenderer zone assets binding
    let attachment = attachPropPhysics world zone assets binding transform
    p <- { p with Renderer = renderer; Physics = attachment }
    do mapBody zone attachment (partRef environment.Epoch handle.Index handle.Generation)

// Spawn a duplicate of an environment part into the current environment.
let spawnPartBinding (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (entity: Entity) (binding: AssetBinding) : EntityRef =
    match zone.Environment with
    | None -> failwith "environment part without a loaded environment"
    | Some environment ->

    let handle = environment.Parts.Add(meshProp entity binding)
    let ref = partRef environment.Epoch handle.Index handle.Generation
    do attachPart zone world assets environment handle
    ref

// Build a fresh environment value from an asset: one part per mesh-bearing node,
// with no resources yet (the caller activates to attach them).
let private buildEnvironment (assets: Assets.AssetManager) (assetId: string) (epoch: uint32) : Environment =
    let asset = assets.Environments.[assetId]
    let baseEntity : Entity =
        { Name = asset.Name
          Transform = transformIdentity
          Enabled = true
          Visible = true
          Selectable = true
          Draggable = true }
    let parts = SlotMap<Prop>(512u)
    for part in asset.Parts do
        let mutable scale = Vector3.One
        let mutable rotation = Quaternion.Identity
        let mutable translation = Vector3.Zero
        do Matrix4x4.Decompose(part.World, &scale, &rotation, &translation) |> ignore
        let entity : Entity =
            { Name = if part.Name = "" then part.HierarchyPath else part.Name
              Transform = { Position = translation; Rotation = rotation; Scale = scale }
              Enabled = true
              Visible = true
              Selectable = true
              Draggable = true }
        let binding = { AssetId = None; SubmeshIndex = part.SubmeshIndex; SubmeshCount = part.SubmeshCount }
        parts.Add(meshProp entity binding) |> ignore
    { Base = baseEntity; AssetId = assetId; Orientation = asset.Orientation; Epoch = epoch; Parts = parts }

// Tear down the current environment's part resources (bodies + renderers) but keep
// its slot map, so its slots survive the swap and rebuild on the way back.
let private deactivateEnvironment (zone: Zone) (world: Physics.World) : unit =
    match zone.Environment with
    | None -> ()
    | Some environment ->
    let parts = environment.Parts
    let slots = parts.Slots
    for i in 0 .. slots.Length - 1 do
        let slot = &slots.[i]
        if slot.Alive then
            let p = slot.Value
            do destroyBody world zone p.Physics
            do Prop.releaseRenderer p
            parts.Get { Index = uint32 i; Generation = slot.Generation } <- { p with Physics = None; Renderer = Unchecked.defaultof<EnvironmentRender.EnvironmentRender> }
    zone.Environment <- None

// Rebuild an environment's part resources and make it the current environment.
let private activateEnvironment (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (environment: Environment) : unit =
    zone.Environment <- Some environment
    let parts = environment.Parts
    let slots = parts.Slots
    for i in 0 .. slots.Length - 1 do
        let slot = &slots.[i]
        if slot.Alive then
            do attachPart zone world assets environment { Index = uint32 i; Generation = slot.Generation }

// Swap the current environment for `environment` (None clears it): tear down the
// outgoing parts and rebuild the incoming ones.
let setEnvironment (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (environment: Environment option) : unit =
    do deactivateEnvironment zone world
    match environment with
    | Some incoming -> do activateEnvironment zone world assets incoming
    | None -> ()

// Load an environment asset, replacing any previous one, and return the built value
// so the caller's `SetEnvironment` command can retain it. Each load takes a fresh
// epoch, so refs from a previously loaded environment never resolve against this one.
let loadEnvironment (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (assetId: string) : Environment =
    let epoch = zone.NextEnvironmentEpoch + 1u
    zone.NextEnvironmentEpoch <- epoch
    let environment = buildEnvironment assets assetId epoch
    do setEnvironment zone world assets (Some environment)
    environment
