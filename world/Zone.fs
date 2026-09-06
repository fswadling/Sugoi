module Zone

// The world container for one playable area: actors, props (placed prop assets
// plus the parts imported from the loaded environment's glTF), and an optional
// loaded environment. Waypoints and triggers land in a later phase. Heap-
// allocated (not a struct); its entity collections are fixed-size inline slot
// maps, mutated in place.
//
// Phase 13: the zone does not own the physics world — the world is shared
// across zones (owned by `SharedState`) and passed in per call. The zone keeps
// only the body→entity map. Bodies are created/destroyed on spawn/remove and
// recreated on transform change, so the colliders never drift from the rendered
// geometry.

open System.Collections.Generic
open System.Numerics
open Entity
open SlotMap

type Zone =
    { Name : string
      mutable Actors : SlotMap<Actor>
      mutable Environment : Environment option
      mutable Props : SlotMap<Prop>
      BodyToEntity : Dictionary<Physics.BodyId, EntityRef> }

let create () : Zone =
    { Name = ""
      Actors = SlotMap.create<Actor> 64u
      Environment = None
      Props = SlotMap.create<Prop> 512u
      BodyToEntity = Dictionary<Physics.BodyId, EntityRef>() }

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
    

// ── Entity kind-dispatch ──
//
// The editor addresses entities by `EntityRef { Kind; Index }` and needs to
// read/write/spawn/remove them without a generation check. These helpers are the
// single seam for that; `state/Command.fs` wraps them in undoable commands.

// The base Entity behind a ref (Actor/Prop), or None for other kinds / absent.
let entityAt (zone: Zone) (ref: EntityRef) : Entity option =
    match ref.Kind with
    | EntityKind.Actor -> Option.map (fun (a: Actor) -> a.Base) (SlotMap.tryGetByIndex zone.Actors ref.Index)
    | EntityKind.Prop -> Option.map (fun (p: Prop) -> p.Base) (SlotMap.tryGetByIndex zone.Props ref.Index)
    | _ -> None

// The asset binding behind a ref (asset id +, for props, the submesh range).
// Raises for absent entities and kinds without a binding — callers guard with
// `entityAt` first.
let bindingAt (zone: Zone) (ref: EntityRef) : AssetBinding =
    match ref.Kind with
    | EntityKind.Actor ->
        let a = SlotMap.getByIndex zone.Actors ref.Index
        { AssetId = Some a.AssetId; SubmeshIndex = 0; SubmeshCount = 0 }
    | EntityKind.Prop ->
        let p = SlotMap.getByIndex zone.Props ref.Index
        { AssetId = p.AssetId; SubmeshIndex = p.SubmeshIndex; SubmeshCount = p.SubmeshCount }
    | _ -> failwithf "bindingAt: kind %A has no asset binding" ref.Kind

// Write a base Entity back at the ref's index (Actor/Prop). No-op for other kinds.
// A transform change recreates the body so it follows the new transform.
let setEntity (zone: Zone) (world: Physics.World) (ref: EntityRef) (entity: Entity) : unit =
    match ref.Kind with
    | EntityKind.Actor ->
        match SlotMap.tryGetByIndex zone.Actors ref.Index with
        | None -> ()
        | Some a ->
        do destroyBody world zone a.Physics
        let attachment =
            match a.Physics with
            | Some ph -> Some { ph with Body = capsuleBody world ph.Collider entity.Transform }
            | None -> None
        do mapBody zone attachment ref
        zone.Actors <- SlotMap.setByIndex zone.Actors ref.Index { a with Base = entity; Physics = attachment }
        
    | EntityKind.Prop ->
        match SlotMap.tryGetByIndex zone.Props ref.Index with
        | None -> ()
        | Some p ->
        do destroyBody world zone p.Physics
        let attachment =
            match p.Physics with
            | Some ph -> Some { ph with Body = meshBody world ph.Collider entity.Transform }
            | None -> None
        do mapBody zone attachment ref
        zone.Props <- SlotMap.setByIndex zone.Props ref.Index { p with Base = entity; Physics = attachment }
        
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

// Write an actor or prop (base + binding + collider + body) at the ref's index.
// Used by command apply/undo to restore/create an entity.
let setActorWithAsset (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (ref: EntityRef) (entity: Entity) (binding: AssetBinding) : unit =
    match ref.Kind with
    | EntityKind.Actor ->
        let assetId =
            match binding.AssetId with
            | Some id -> id
            | None -> failwith "actor binding without an asset id"
        let renderer = Assets.createActorRender assets assetId
        let attachment = attachActorPhysics world assets assetId entity.Transform
        do mapBody zone attachment ref
        zone.Actors <- SlotMap.setByIndex zone.Actors ref.Index { Base = entity; AssetId = assetId; Physics = attachment; ShowCollider = false; Renderer = renderer }
    | EntityKind.Prop ->
        let renderer = propRenderer zone assets binding
        let attachment = attachPropPhysics world zone assets binding entity.Transform
        do mapBody zone attachment ref
        zone.Props <-
            SlotMap.setByIndex zone.Props ref.Index
                { Base = entity; AssetId = binding.AssetId; SubmeshIndex = binding.SubmeshIndex; SubmeshCount = binding.SubmeshCount; Physics = attachment; Renderer = renderer }
    | _ -> ()

// Remove the entity at the ref's index (Actor/Prop), destroying its body. No-op
// for other kinds.
let removeEntity (zone: Zone) (world: Physics.World) (ref: EntityRef) : unit =
    match ref.Kind with
    | EntityKind.Actor ->
        match SlotMap.tryGetByIndex zone.Actors ref.Index with
        | Some a -> destroyBody world zone a.Physics
        | None -> ()
        zone.Actors <- SlotMap.removeByIndex zone.Actors ref.Index (Some Actor.releaseRenderer)
    | EntityKind.Prop ->
        match SlotMap.tryGetByIndex zone.Props ref.Index with
        | Some p -> destroyBody world zone p.Physics
        | None -> ()
        zone.Props <- SlotMap.removeByIndex zone.Props ref.Index (Some Prop.releaseRenderer)
    | _ -> ()

// Spawn a new actor and return the new ref (renderer + physics attached).
let spawnActor (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (entity: Entity) (assetId: string) : EntityRef =
    let renderer = Assets.createActorRender assets assetId
    let attachment = attachActorPhysics world assets assetId entity.Transform
    let actors, handle = SlotMap.add zone.Actors { Base = entity; AssetId = assetId; Physics = attachment; ShowCollider = false; Renderer = renderer }
    zone.Actors <- actors
    let ref = { Kind = EntityKind.Actor; Index = handle.Index }
    do mapBody zone attachment ref
    ref

// Spawn a prop from a full binding (placed asset or environment part).
let spawnPropBinding (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (entity: Entity) (binding: AssetBinding) : EntityRef =
    let renderer = propRenderer zone assets binding
    let attachment = attachPropPhysics world zone assets binding entity.Transform
    let props, handle =
        SlotMap.add zone.Props
            { Base = entity; AssetId = binding.AssetId; SubmeshIndex = binding.SubmeshIndex; SubmeshCount = binding.SubmeshCount; Physics = attachment; Renderer = renderer }
    zone.Props <- props
    let ref = { Kind = EntityKind.Prop; Index = handle.Index }
    do mapBody zone attachment ref
    ref

// Spawn a placed prop asset (whole model).
let spawnProp (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (entity: Entity) (assetId: string) : EntityRef =
    spawnPropBinding zone world assets entity { AssetId = Some assetId; SubmeshIndex = 0; SubmeshCount = 0 }

// Destroy every prop's body and rebuild a fresh prop slot map that keeps placed
// props (`AssetId = Some`) while dropping environment parts (`AssetId = None`).
// Placed props' bodies are re-created because their slot indices change during
// the rebuild (otherwise their body→entity mapping would point at stale indices).
let private retainPlacedProps (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) : SlotMap<Prop> =
    SlotMap.iteriAlive zone.Props (fun _ (p: Prop) -> destroyBody world zone p.Physics)
    let mutable props = SlotMap.create<Prop> 512u
    SlotMap.iteriAlive zone.Props (fun _ (p: Prop) ->
        if Option.isSome p.AssetId then
            let binding = { AssetId = p.AssetId; SubmeshIndex = p.SubmeshIndex; SubmeshCount = p.SubmeshCount }
            let attachment = attachPropPhysics world zone assets binding p.Base.Transform
            let (m, handle) = SlotMap.add props { p with Physics = attachment }
            let ref = { Kind = EntityKind.Prop; Index = handle.Index }
            mapBody zone attachment ref
            props <- m)
    props

// Remove the loaded environment (if any): drop its parts, keep placed props,
// and clear the environment slot.
let clearEnvironment (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) : unit =
    zone.Props <- retainPlacedProps zone world assets
    zone.Environment <- None

// Load an environment asset, replacing any previous environment: keep placed
// props, drop the previous environment's parts, then spawn one prop per part.
let loadEnvironment (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (assetId: string) : unit =
    zone.Props <- retainPlacedProps zone world assets

    let asset = assets.Environments.[assetId]
    let baseEntity : Entity =
        { Name = asset.Name
          Transform = transformIdentity
          Enabled = true
          Visible = true
          Selectable = true
          Draggable = true }
    zone.Environment <- Some { Base = baseEntity; AssetId = assetId; Orientation = asset.Orientation }

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
        spawnPropBinding zone world assets entity binding |> ignore
