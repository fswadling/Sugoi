module Zone

// The world container for one playable area: actors, placed props, waypoints, triggers,
// an optional loaded environment (which owns its own part slot map), and the static-scene
// batch that renders its props and parts. Heap-allocated; its entity collections are
// fixed-size generational slot maps, mutated in place.
//
// The zone does not own the physics world — it is the gameplay state's and
// passed in per call; the zone keeps only the body→entity map. Bodies are
// created/destroyed on spawn/remove and recreated on transform change, so
// colliders never drift from the rendered geometry.

open System.Collections.Generic
open System.Numerics
open Entity
open SlotMap
open ZoneTemplate

// The zone's environment as one state (spec 053). Only `Active` has attached part resources
// (bodies + renderers) and may be drawn; `Awaiting` is set but its asset is not resident yet, so its
// parts are drawn only once it lands (pop-in). The DU encodes drawability rather than a flag.
type ZoneEnvironment =
    | NoEnvironment
    | Active of Environment
    | Awaiting of Environment

type Zone =
    { Actors : SlotMap<Actor>
      mutable EnvironmentState : ZoneEnvironment
      // The zone's authored light (spec 043): a sun pitch, sun yaw, colour and ambient, resolved
      // into the scene's one sun. A zone-level setting rather than an entity's, and one every
      // zone owns — a zone lights its scene whether or not an environment is loaded.
      mutable Light : Texture.LightSettings
      Props : SlotMap<Prop>
      Waypoints : SlotMap<Waypoint>
      Triggers : SlotMap<Trigger>
      Batch : EnvironmentBatch.EnvironmentBatch
      mutable NextEnvironmentEpoch : uint32
      BodyToEntity : Dictionary<Physics.BodyId, EntityRef>
      // The zone's player designation: which of its actors is the avatar. Only the
      // active zone's designation is live. `ValueNone` means it drives no character.
      mutable Player : Handle voption }

    // The environment as an option (`Active` and `Awaiting` are both `Some`), so every existing read
    // site is unchanged. A member cannot share a record field's name in F# (FS0023), which is why the
    // field is `EnvironmentState`.
    member this.Environment : Environment option =
        match this.EnvironmentState with
        | Active environment | Awaiting environment -> Some environment
        | NoEnvironment -> None

let create () : Zone =
    { Actors = SlotMap<Actor>(64u)
      EnvironmentState = NoEnvironment
      Light = Texture.defaultLight
      Props = SlotMap<Prop>(32u)
      Waypoints = SlotMap<Waypoint>(128u)
      Triggers = SlotMap<Trigger>(32u)
      Batch = EnvironmentBatch.create ()
      NextEnvironmentEpoch = 0u
      BodyToEntity = Dictionary<Physics.BodyId, EntityRef>()
      Player = ValueNone }

// ── static-scene batch ──

// Gather this zone's static scene through its own batch. The one place that binds a
// zone to its batch (the batch itself does not reference `Zone`). Created without a
// device; the buffer is allocated on first gather.
let gatherStatic (device: Gpu.Device) (zone: Zone) : unit =
    let parts =
        match zone.EnvironmentState with
        | Active environment -> ValueSome environment.Parts
        | NoEnvironment | Awaiting _ -> ValueNone
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
// storage (environment, trigger) are never alive.
let isAlive (zone: Zone) (ref: EntityRef) : bool =
    match ref.Kind with
    | EntityKind.Actor -> zone.Actors.IsAlive(handleOf ref)
    | EntityKind.Prop -> zone.Props.IsAlive(handleOf ref)
    | EntityKind.Waypoint -> zone.Waypoints.IsAlive(handleOf ref)
    | EntityKind.Trigger -> zone.Triggers.IsAlive(handleOf ref)
    | EntityKind.Part ->
        match partsFor zone ref with
        | Some parts -> parts.IsAlive(handleOf ref)
        | None -> false
    | EntityKind.Environment -> false

// The base entity behind a ref (Actor/Prop/Part/Waypoint/Trigger), or None for other
// kinds or a stale/absent slot. Returned by value (a cheap struct copy) so callers can't
// write a transform that would bypass the body recreation in `setTransform`.
let tryEntityAt (zone: Zone) (ref: EntityRef) : Entity option =
    match ref.Kind with
    | EntityKind.Actor ->
        if not (zone.Actors.IsAlive(handleOf ref)) then None else
        let actor = &zone.Actors.Get(handleOf ref)
        Some actor.State.Base
    | EntityKind.Prop ->
        if not (zone.Props.IsAlive(handleOf ref)) then None else
        let prop = &zone.Props.Get(handleOf ref)
        Some prop.State.Base
    | EntityKind.Waypoint ->
        if not (zone.Waypoints.IsAlive(handleOf ref)) then None else
        let waypoint = &zone.Waypoints.Get(handleOf ref)
        Some waypoint.Base
    | EntityKind.Trigger ->
        if not (zone.Triggers.IsAlive(handleOf ref)) then None else
        let trigger = &zone.Triggers.Get(handleOf ref)
        Some trigger.State.Base
    | EntityKind.Part ->
        match partsFor zone ref with
        | None -> None
        | Some parts ->
        if not (parts.IsAlive(handleOf ref)) then None else
        let part = &parts.Get(handleOf ref)
        Some part.State.Base
    | EntityKind.Environment -> None

// ── Collider derivation ──

// Mesh collider merging a contiguous submesh range into one flat vertex/index
// buffer (node-local, unscaled). Fails loudly on an empty range — a part with
// no triangles is a content bug, not a collider-less entity.
let meshCollider (submeshes: EnvironmentAsset.LoadedSubmesh array) (first: int) (count: int) : MeshCollider =
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
        let submeshes = EnvironmentAsset.submeshes assets.Props.[assetId]
        Some (meshCollider submeshes 0 submeshes.Length)
    | None ->
    match zone.Environment with
    | None -> failwith "environment part without a loaded environment"
    | Some environment ->
    let asset = assets.Environments.[environment.AssetId]
    let submeshes = EnvironmentAsset.submeshes asset
    match EnvironmentAsset.parts asset |> Array.tryFind (fun p -> p.SubmeshIndex = binding.SubmeshIndex) with
    | None -> None
    | Some part ->
    Some (meshCollider submeshes part.SubmeshIndex part.SubmeshCount)


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
    // `createCapsuleBody` builds at identity rotation, so the transform's facing is written onto the body here:
    // the body is the source of truth while playing (`Systems.syncActors` copies body -> entity each frame), so
    // without this an editor-authored (or `AddActor`-authored) rotation would be lost the moment Play begins. The
    // actor capsule's rotation is locked (`Physics.translationOnly`), so setting it directly is what
    // `Systems.playerInput` does too.
    let body = Physics.createCapsuleBody world (capsuleCenter transform collider) collider.HalfHeight collider.Radius
    do Physics.setBodyRotation world body transform.Rotation
    body

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

// ── Recorded state ──
//
// A command carries a kind's authored value, taken through `capture*`. Each resolves the
// ref and reads the entity's `State`: the properties the editor authors for that kind, and
// nothing an engine rebuilds. There is no clearing step — a payload is a different type
// from the entity it came from, so it cannot hold a body, a renderer, or a runtime flag in
// the first place. Its mirror is the matching `occupy*`, which builds the entity field by
// field and so fails to build until a new field has been classified as authored, derived,
// or runtime.
//
// Each raises for a stale or wrong-kind ref: every caller resolves the ref through
// `tryEntityAt` first, so a miss here is a broken invariant rather than a condition.

let captureActor (zone: Zone) (ref: EntityRef) : ActorState =
    if ref.Kind <> EntityKind.Actor || not (zone.Actors.IsAlive(handleOf ref)) then
        invalidOp (sprintf "captureActor: %A is not a live actor" ref)

    let actor = zone.Actors.Get(handleOf ref)
    actor.State

let captureProp (zone: Zone) (ref: EntityRef) : PropState =
    if ref.Kind <> EntityKind.Prop || not (zone.Props.IsAlive(handleOf ref)) then
        invalidOp (sprintf "captureProp: %A is not a live prop" ref)

    let prop = zone.Props.Get(handleOf ref)
    prop.State

// A part's record is captured through its environment, so a ref whose epoch no longer
// matches the loaded environment is not a part at all.
let capturePart (zone: Zone) (ref: EntityRef) : PropState =
    match partsFor zone ref with
    | Some parts when parts.IsAlive(handleOf ref) ->
        let part = parts.Get(handleOf ref)
        part.State
    | _ -> invalidOp (sprintf "capturePart: %A is not a live part" ref)

let captureWaypoint (zone: Zone) (ref: EntityRef) : Waypoint =
    if ref.Kind <> EntityKind.Waypoint || not (zone.Waypoints.IsAlive(handleOf ref)) then
        invalidOp (sprintf "captureWaypoint: %A is not a live waypoint" ref)

    let waypoint = zone.Waypoints.Get(handleOf ref)
    waypoint

// The runtime `Triggered` flag is not part of a trigger's authored value, so there is
// nothing to clear here: the trigger system recomputes the flag, and a restore never puts
// back the value the entity happened to have when it was recorded.
let captureTrigger (zone: Zone) (ref: EntityRef) : TriggerState =
    if ref.Kind <> EntityKind.Trigger || not (zone.Triggers.IsAlive(handleOf ref)) then
        invalidOp (sprintf "captureTrigger: %A is not a live trigger" ref)

    let trigger = zone.Triggers.Get(handleOf ref)
    trigger.State

// The zone's placed props, each with the ref it stands at: the authored value a registration records,
// and the slot a replacement has to name to put the prop back where it was — at the generation it had,
// so a ref the author held before an instantiation resolves again afterwards.
let captureProps (zone: Zone) : (EntityRef * PropState) array =
    let props = ResizeArray<EntityRef * PropState>(int zone.Props.Count)
    zone.Props.IteriAlive (fun index generation prop -> props.Add(propRef index generation, prop.State))
    props.ToArray()

// The whole record of a zone: the environment it has loaded, its parts, and the props standing in it.
// `None` when no environment is loaded, which is what registering a place requires — the same condition
// the panel's register control is disabled by, expressed once here rather than at each caller.
let captureTemplate (zone: Zone) : ZoneTemplate option =
    match zone.Environment with
    | None -> None
    | Some environment ->

    let props = captureProps zone |> Array.map snd
    Some (ZoneTemplate.ofEnvironment environment props zone.Light)

// The authored value a trigger is created with: a one-metre radius two metres tall, not
// interactable, with no prompt and no cue. It is only ever a new trigger's starting point —
// what a command records is the value the live trigger holds, so a delete or a copy carries the
// trigger's own shape rather than these defaults.
let private defaultTrigger (entity: Entity) : TriggerState =
    { Base = entity
      Radius = 1.0f
      Height = 2.0f
      Interactable = false
      Prompt = ""
      Cue = Cue.Fin }

// Change a mesh-backed entity's transform in `map` (a placed prop or an
// environment part), keeping its body in step. A mesh shape bakes the entity
// scale into its vertices, so a move or rotate updates the existing body in
// place; only a scale change rebuilds the shape and its BVH. This keeps dragging
// off the collider-rebuild path.
let private setMeshTransform (zone: Zone) (world: Physics.World) (map: SlotMap<Prop>) (ref: EntityRef) (transform: Transform) : unit =
    if not (map.IsAlive(handleOf ref)) then () else

    let p = &map.Get(handleOf ref)
    let attachment =
        match p.Physics with
        | Some ph when p.State.Base.Transform.Scale = transform.Scale ->
            do Physics.setBodyTransform world ph.Body transform.Position transform.Rotation
            Some ph
        | Some ph ->
            do destroyBody world zone p.Physics
            Some { ph with Body = meshBody world ph.Collider transform }
        | None -> None
    do mapBody zone attachment ref
    p <- { p with State.Base.Transform = transform; Physics = attachment }

// Change an existing entity's transform at the ref (Actor/Prop/Part/Waypoint/Trigger),
// recreating its body so it follows. No-op for other kinds or a stale/absent ref.
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
        a <- { a with State.Base.Transform = transform; Physics = attachment }

    | EntityKind.Prop ->
        do setMeshTransform zone world zone.Props ref transform

    | EntityKind.Waypoint ->
        if zone.Waypoints.IsAlive(handleOf ref) then
            let waypoint = &zone.Waypoints.Get(handleOf ref)
            waypoint <- { waypoint with Base = { waypoint.Base with Transform = transform } }

    | EntityKind.Trigger ->
        if zone.Triggers.IsAlive(handleOf ref) then
            let trigger = &zone.Triggers.Get(handleOf ref)
            trigger <- { trigger with State.Base.Transform = transform }

    | EntityKind.Part ->
        match partsFor zone ref with
        | None -> ()
        | Some parts ->
        do setMeshTransform zone world parts ref transform

    | EntityKind.Environment -> ()

// Change an existing entity's name at the ref (Actor/Prop/Part/Waypoint/Trigger). No-op
// for other kinds or a stale/absent ref. A name is a label rather than an identity — refs
// address entities — so the same name may be given to more than one.
let setName (zone: Zone) (ref: EntityRef) (name: string) : unit =
    match ref.Kind with
    | EntityKind.Actor ->
        if zone.Actors.IsAlive(handleOf ref) then
            let actor = &zone.Actors.Get(handleOf ref)
            actor <- { actor with State.Base.Name = name }

    | EntityKind.Prop ->
        if zone.Props.IsAlive(handleOf ref) then
            let p = &zone.Props.Get(handleOf ref)
            p <- { p with State.Base.Name = name }

    | EntityKind.Waypoint ->
        if zone.Waypoints.IsAlive(handleOf ref) then
            let waypoint = &zone.Waypoints.Get(handleOf ref)
            waypoint <- { waypoint with Base = { waypoint.Base with Name = name } }

    | EntityKind.Trigger ->
        if zone.Triggers.IsAlive(handleOf ref) then
            let trigger = &zone.Triggers.Get(handleOf ref)
            trigger <- { trigger with State.Base.Name = name }

    | EntityKind.Part ->
        match partsFor zone ref with
        | None -> ()
        | Some parts ->
        if parts.IsAlive(handleOf ref) then
            let p = &parts.Get(handleOf ref)
            p <- { p with State.Base.Name = name }

    | EntityKind.Environment -> ()

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

// Place an authored trigger value at a ref's slot: the restore path for a deleted or
// copied trigger. The runtime flag starts clear — it is the trigger system's to recompute.
// Raises when the slot is not free.
let occupyTrigger (zone: Zone) (ref: EntityRef) (state: TriggerState) : unit =
    do ensureSlotFree zone.Triggers ref
    do zone.Triggers.Occupy(handleOf ref, { State = state; Triggered = false })

// ── Restoring a recorded record ──
//
// The mirror of `capture*`: each builds its kind's record field by field from the payload
// and rebuilds what a command does not carry — the body and the renderer — so a field
// added to a kind fails to build here until its author says whether it is authored (copied
// from the payload), derived (rebuilt), or runtime (left blank).
//
// The recorded slot is always free when a restore runs: entities are only ever added to
// their own kind's map by `Spawning`, which pairs every add with the command that reverses
// it. `ensureSlotFree` therefore turns a slot stolen by an untracked add into a loud
// failure, and does so before any GPU or physics resource is built.

let occupyActor (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (ref: EntityRef) (state: ActorState) : unit =
    do ensureSlotFree zone.Actors ref
    let renderer = Assets.createActorRender assets state.AssetId
    let attachment = attachActorPhysics world assets state.AssetId state.Base.Transform
    do zone.Actors.Occupy(handleOf ref, { State = state
                                          Physics = attachment
                                          Renderer = renderer })
    do mapBody zone attachment ref

let occupyProp (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (ref: EntityRef) (state: PropState) : unit =
    do ensureSlotFree zone.Props ref
    let binding = { AssetId = state.AssetId; SubmeshIndex = state.SubmeshIndex; SubmeshCount = state.SubmeshCount }
    let renderer = propRenderer zone assets binding
    let attachment = attachPropPhysics world zone assets binding state.Base.Transform
    do zone.Props.Occupy(handleOf ref, { State = state
                                         Physics = attachment
                                         Renderer = renderer })
    do mapBody zone attachment ref

// A part restore only runs while the environment it names is loaded: swaps are commands
// and the stack is LIFO, so a swap is undone before the part commands that followed it.
// An epoch mismatch is therefore a broken invariant, not a no-op.
let occupyPart (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (ref: EntityRef) (state: PropState) : unit =
    match partsFor zone ref with
    | None -> failwithf "occupyPart: part ref %A does not belong to the loaded environment" ref
    | Some parts ->
    do ensureSlotFree parts ref
    let binding = { AssetId = state.AssetId; SubmeshIndex = state.SubmeshIndex; SubmeshCount = state.SubmeshCount }
    let renderer = propRenderer zone assets binding
    let attachment = attachPropPhysics world zone assets binding state.Base.Transform
    do parts.Occupy(handleOf ref, { State = state
                                    Physics = attachment
                                    Renderer = renderer })
    do mapBody zone attachment ref

// Toggle an actor's collider overlay. A direct write rather than a command: the overlay
// is an editor visualisation preference, not a scene edit, so toggling it is not an undo
// step. Its value is still carried by whatever command records the actor, so a delete or
// a copy takes it along.
let setShowCollider (zone: Zone) (ref: EntityRef) (show: bool) : unit =
    if zone.Actors.IsAlive(handleOf ref) then
        let actor = &zone.Actors.Get(handleOf ref)
        actor <- { actor with State.ShowCollider = show }

let occupyWaypoint (zone: Zone) (ref: EntityRef) (waypoint: Waypoint) : unit =
    do ensureSlotFree zone.Waypoints ref
    do zone.Waypoints.Occupy(handleOf ref, { Base = waypoint.Base; Next = waypoint.Next })

// ── Removing an entity ──
//
// The teardown half of a delete: free the slot and release what the kind owns. Each is a
// no-op for a stale ref, so a command that runs against an entity the editor has already
// removed does nothing rather than raising.

// Remove a mesh-backed entity (placed prop or environment part) from `map`,
// destroying its body. No-op for a stale ref.
let private removeMeshEntity (zone: Zone) (world: Physics.World) (map: SlotMap<Prop>) (ref: EntityRef) : unit =
    if map.IsAlive(handleOf ref) then
        // Copy the attachment out first: `Remove` mutates the slot.
        let physics = (map.Get(handleOf ref)).Physics
        do destroyBody world zone physics
        do map.Remove(handleOf ref, Some Prop.releaseRenderer)

let removeActor (zone: Zone) (world: Physics.World) (ref: EntityRef) : unit =
    if zone.Actors.IsAlive(handleOf ref) then
        let physics = (zone.Actors.Get(handleOf ref)).Physics
        do destroyBody world zone physics
        do zone.Actors.Remove(handleOf ref, Some Actor.releaseRenderer)

let removeProp (zone: Zone) (world: Physics.World) (ref: EntityRef) : unit =
    do removeMeshEntity zone world zone.Props ref

let removePart (zone: Zone) (world: Physics.World) (ref: EntityRef) : unit =
    match partsFor zone ref with
    | Some parts -> do removeMeshEntity zone world parts ref
    | None -> ()

let removeWaypoint (zone: Zone) (ref: EntityRef) : unit =
    if zone.Waypoints.IsAlive(handleOf ref) then
        do zone.Waypoints.Free(handleOf ref)

let removeTrigger (zone: Zone) (ref: EntityRef) : unit =
    if zone.Triggers.IsAlive(handleOf ref) then
        do zone.Triggers.Free(handleOf ref)

// Spawn a new actor and return the new ref (renderer + physics attached).
let spawnActor (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (entity: Entity) (assetId: string) : EntityRef =
    let renderer = Assets.createActorRender assets assetId
    let attachment = attachActorPhysics world assets assetId entity.Transform
    let state = { Base = entity; AssetId = assetId; ShowCollider = false }
    let handle = zone.Actors.Add({ State = state; Physics = attachment; Renderer = renderer })
    let ref = actorRef handle.Index handle.Generation
    do mapBody zone attachment ref
    ref

// The first live actor named `name`, in slot order, or None when there is none. Names are not required to be
// unique: the first match wins, deterministically. Used by the cue's actor ops to resolve the actor an author
// names (spec 072).
let tryActorByName (zone: Zone) (name: string) : EntityRef option =
    let slots = zone.Actors.Slots
    let mutable found = None
    for i in 0 .. slots.Length - 1 do
        if found.IsNone then
            let slot = &slots.[i]
            if slot.Alive && slot.Value.State.Base.Name = name then
                found <- Some (actorRef (uint32 i) slot.Generation)
    found

// Build a mesh-backed prop value from an entity + binding, with no resources yet
// (used while building an environment's parts map; activation attaches them).
let private meshProp (entity: Entity) (binding: AssetBinding) : Prop =
    let state = { Base = entity; AssetId = binding.AssetId; SubmeshIndex = binding.SubmeshIndex; SubmeshCount = binding.SubmeshCount }
    { State = state
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

// ── Waypoints ──

// Spawn a waypoint from its base entity and return its ref. Waypoints carry no
// renderer or body, so no world/assets are needed.
let spawnWaypoint (zone: Zone) (entity: Entity) : EntityRef =
    let handle = zone.Waypoints.Add({ Base = entity; Next = None })
    waypointRef handle.Index handle.Generation

// The waypoint a waypoint's chain continues at, if that link still resolves: the source
// must be a live waypoint, the link must exist, and its target must resolve as a live
// waypoint. Pure: an unresolvable link is left where it is, because it becomes live
// again if undo restores the waypoint it names.
// The waypoint a waypoint's chain continues at, if that link still resolves: the source
// must be a live waypoint and the target must resolve as a live waypoint. Otherwise
// there is no next. Pure: a link whose target is gone is left exactly as it is and simply
// stops resolving, so it becomes live again if undo restores the waypoint it names.
let waypointNext (zone: Zone) (ref: EntityRef) : EntityRef option =
    if ref.Kind <> EntityKind.Waypoint then None else
    if not (zone.Waypoints.IsAlive(handleOf ref)) then None else

    match (zone.Waypoints.Get(handleOf ref)).Next with
    | Some target when isAlive zone target -> Some target
    | _ -> None

// Point a waypoint at another (`Some target`), or end its chain (`None`). A waypoint
// cannot be its own next and a link is only ever between waypoints; both are
// preconditions the editor's disabled actions keep unreachable. The target is not
// required to be alive: undoing a delete can restore a link before the waypoint it
// names is back, and such a link simply does not resolve.
// Point a waypoint at another (`Some target`), or end its chain (`None`). A waypoint
// cannot be its own next and a link is only ever between waypoints; both are
// preconditions the editor's disabled actions keep unreachable. The target is not
// required to be alive: undoing a delete can restore a link while the waypoint it names
// is momentarily gone, and such a link simply does not resolve.
let setWaypointNext (zone: Zone) (ref: EntityRef) (next: EntityRef option) : unit =
    if ref.Kind <> EntityKind.Waypoint then invalidOp "setWaypointNext: the source is not a waypoint" else
    if next = Some ref then invalidOp "setWaypointNext: a waypoint cannot be its own next" else
    if not (zone.Waypoints.IsAlive(handleOf ref)) then () else

    let target =
        match next with
        | None -> None
        | Some t ->
        if t.Kind <> EntityKind.Waypoint then invalidOp "setWaypointNext: the target is not a waypoint" else
        Some t

    let waypoint = &zone.Waypoints.Get(handleOf ref)
    waypoint <- { waypoint with Next = target }

// The first live waypoint named `name`, in slot order, or None when there is none. Names are not
// required to be unique: the first match wins, deterministically. Used by the cue's camera ops to
// resolve the waypoint an author typed into a cue (`world/CueEngine.fs`).
let tryWaypointByName (zone: Zone) (name: string) : EntityRef option =
    let slots = zone.Waypoints.Slots
    let mutable found = None
    for i in 0 .. slots.Length - 1 do
        if found.IsNone then
            let slot = &slots.[i]
            if slot.Alive && slot.Value.Base.Name = name then
                found <- Some (waypointRef (uint32 i) slot.Generation)
    found

// The chain starting at `start`, following `Next` links to the end, `start` first. Bounded by the
// zone's live waypoint count, so a chain that loops back on itself terminates rather than running
// forever. The caller resolves `start` with `tryWaypointByName`.
let waypointChain (zone: Zone) (start: EntityRef) : EntityRef list =
    let rec go (ref: EntityRef) (acc: EntityRef list) (remaining: int) =
        if remaining <= 0 then List.rev acc else
        match waypointNext zone ref with
        | Some next -> go next (next :: acc) (remaining - 1)
        | None -> List.rev acc
    go start [ start ] (int zone.Waypoints.Count)

// ── Triggers ──

// Spawn a trigger from its base entity and return its ref, with the default shape.
// Triggers carry no renderer or body, so no world/assets are needed.
let spawnTrigger (zone: Zone) (entity: Entity) : EntityRef =
    let handle = zone.Triggers.Add({ State = defaultTrigger entity; Triggered = false })
    triggerRef handle.Index handle.Generation

// The trigger behind a ref, or ValueNone for another kind or a stale/absent slot. The
// authored payload (shape, flags, prompt), which the shared editor paths cannot reach
// through `tryEntityAt` because that answers the base entity alone. A `voption` because
// the play-mode overlay reads it every frame, and `Trigger` is a struct.
let tryTriggerAt (zone: Zone) (ref: EntityRef) : Trigger voption =
    if ref.Kind <> EntityKind.Trigger then ValueNone else
    if not (zone.Triggers.IsAlive(handleOf ref)) then ValueNone else

    ValueSome (zone.Triggers.Get(handleOf ref))

// Replace a trigger's authored value, leaving its runtime flag alone. Raises for a ref of
// another kind; no-op for a stale or absent one.
let setTrigger (zone: Zone) (ref: EntityRef) (state: TriggerState) : unit =
    if ref.Kind <> EntityKind.Trigger then invalidOp "setTrigger: the ref is not a trigger" else
    if not (zone.Triggers.IsAlive(handleOf ref)) then () else

    let slot = &zone.Triggers.Get(handleOf ref)
    slot <- { slot with State = state }

// The first live trigger named `name`, in slot order, or None. Names are not required to be unique: the
// first match wins, deterministically. Used by the cue's trigger ops to resolve the trigger an author names
// (`world/CueEngine.fs`).
let tryTriggerByName (zone: Zone) (name: string) : EntityRef option =
    let slots = zone.Triggers.Slots
    let mutable found = None
    for i in 0 .. slots.Length - 1 do
        if found.IsNone then
            let slot = &slots.[i]
            if slot.Alive && slot.Value.State.Base.Name = name then
                found <- Some (triggerRef (uint32 i) slot.Generation)
    found

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
    let transform = p.State.Base.Transform
    let binding = { AssetId = p.State.AssetId; SubmeshIndex = p.State.SubmeshIndex; SubmeshCount = p.State.SubmeshCount }
    let renderer = propRenderer zone assets binding
    let attachment = attachPropPhysics world zone assets binding transform
    p.Renderer <- renderer
    p.Physics <- attachment
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

// Parts carry headroom beyond the count an environment starts with: `spawnPartBinding` adds to this
// map too, so one sized to its starting count would fail its `Add` the moment the author duplicates a
// part.
let private partsCapacity = 512u

// The parts map an authored part list describes, with no resources yet (the caller activates to
// attach them).
let private partsOf (states: PropState array) : SlotMap<Prop> =
    let parts = SlotMap<Prop>(max partsCapacity (uint32 states.Length))
    for state in states do
        let binding = { AssetId = state.AssetId; SubmeshIndex = state.SubmeshIndex; SubmeshCount = state.SubmeshCount }
        do parts.Add(meshProp state.Base binding) |> ignore
    parts

// The authored part states an environment asset starts with: one per mesh-bearing node, with the
// node's accumulated transform decomposed onto the part's entity.
let private assetPartStates (asset: EnvironmentAsset.EnvironmentAsset) : PropState array =
    EnvironmentAsset.parts asset
    |> Array.map (fun part ->
        let mutable scale = Vector3.One
        let mutable rotation = Quaternion.Identity
        let mutable translation = Vector3.Zero
        do Matrix4x4.Decompose(part.World, &scale, &rotation, &translation) |> ignore
        { Base =
            { Name = if part.Name = "" then part.HierarchyPath else part.Name
              Transform = { Position = translation; Rotation = rotation; Scale = scale }
              Enabled = true
              Visible = true
              Selectable = true
              Draggable = true }
          AssetId = None
          SubmeshIndex = part.SubmeshIndex
          SubmeshCount = part.SubmeshCount })

// Build a fresh environment value from an authored record: an asset's own part states, or a
// registered template's. One builder for both, so an instantiated zone and a loaded asset cannot
// diverge in how their parts are built, activated or stamped with an epoch.
let private buildEnvironment (assetId: string) (baseEntity: Entity) (orientation: Quaternion) (states: PropState array) (source: PartsSource) (epoch: uint32) : Environment =
    { Base = baseEntity
      AssetId = assetId
      Orientation = orientation
      Epoch = epoch
      Parts = partsOf states
      PartsSource = source }

// Drop every part's live attachments (its body and renderer) without touching its authored state. Used
// when an environment is torn down, and before a not-yet-resident one is stored `Awaiting`: a captured
// value may still name bodies a delete already destroyed, and a later release must not touch them.
let private clearPartAttachments (environment: Environment) : unit =
    let slots = environment.Parts.Slots
    for i in 0 .. slots.Length - 1 do
        let slot = &slots.[i]
        if slot.Alive then
            let p = slot.Value
            slot.Value <- { p with Physics = None; Renderer = Unchecked.defaultof<EnvironmentRender.EnvironmentRender> }

// Tear down the current environment's part resources (bodies + renderers) but keep
// its slot map, so its slots survive the swap and rebuild on the way back.
let private deactivateEnvironment (zone: Zone) (world: Physics.World) : unit =
    match zone.EnvironmentState with
    | NoEnvironment -> ()
    | Active environment | Awaiting environment ->
    let parts = environment.Parts
    let slots = parts.Slots
    for i in 0 .. slots.Length - 1 do
        let slot = &slots.[i]
        if slot.Alive then
            let p = slot.Value
            do destroyBody world zone p.Physics
            do Prop.releaseRenderer p
    do clearPartAttachments environment
    zone.EnvironmentState <- NoEnvironment

// Rebuild an environment's part resources and make it the current environment.
let private activateEnvironment (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (environment: Environment) : unit =
    zone.EnvironmentState <- Active environment
    let parts = environment.Parts
    let slots = parts.Slots
    for i in 0 .. slots.Length - 1 do
        let slot = &slots.[i]
        if slot.Alive then
            do attachPart zone world assets environment { Index = uint32 i; Generation = slot.Generation }

// Materialise an asset-authored environment's parts when the asset lands: only `FromAsset` with an
// empty map, so a registered template (which may deliberately hold a subset) is never filled from the
// asset, and a redo of an already-materialised value is not filled twice (FR-004; VR-010).
let private materialiseParts (assets: Assets.AssetManager) (environment: Environment) : unit =
    if environment.PartsSource = FromAsset && environment.Parts.Count = 0u then
        for state in assetPartStates assets.Environments.[environment.AssetId] do
            let binding = { AssetId = state.AssetId; SubmeshIndex = state.SubmeshIndex; SubmeshCount = state.SubmeshCount }
            do environment.Parts.Add(meshProp state.Base binding) |> ignore

// Swap the current environment for `environment` (None clears it): request residency, tear down the
// outgoing parts, then make the incoming one current — activating it now if its asset is already
// resident, else leaving it `Awaiting` for `reconcileEnvironment` to finish when it lands.
let setEnvironment (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (environment: Environment option) : unit =
    match environment with
    | Some incoming -> do Assets.requestEnvironment assets incoming.AssetId
    | None -> ()
    do deactivateEnvironment zone world
    match environment with
    | None -> ()
    | Some incoming ->
    if Assets.isEnvironmentResident assets incoming.AssetId then
        do materialiseParts assets incoming
        do activateEnvironment zone world assets incoming
    else
        do clearPartAttachments incoming
        zone.EnvironmentState <- Awaiting incoming

// Finish an `Awaiting` environment once its asset is resident (called per frame after `Assets.pump`).
let reconcileEnvironment (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) : unit =
    match zone.EnvironmentState with
    | Awaiting environment when Assets.isEnvironmentResident assets environment.AssetId ->
        do materialiseParts assets environment
        do activateEnvironment zone world assets environment
    | _ -> ()

// ── A zone's authored contents, recorded and put back ──
//
// The whole of a zone's authored state as one value, for the one caller that needs it: the delete step (spec 048).
// It lives here rather than beside the command that carries it, because it is a zone's record and nothing else is:
// every other command payload is defined where the thing it describes is (`ActorState`, `PropState`, `Environment`
// in `Entity.fs`; `LightSettings` in `Texture.fs`), and `ZoneTemplate` — the same kind of composite, recorded for
// a different purpose — is in `world/` too.
//
// `ZoneContents` is a snapshot *with identity* — it keeps each entity's ref and the live environment, so a restore
// lands in the same slots — which is why it overlaps both `Zone` and `ZoneTemplate`. `docs/ZONE_MODEL_DECISION.md`
// records how the three zone-shaped values relate and recommends when to converge them.

/// The authored contents of one zone: what a delete keeps so the zone can be put back, and nothing a body, a
/// renderer or a batch holds. Each entity is its ref paired with the same authored value a `*Create` carries, so a
/// restore rebuilds it through the one path that builds live entities at all (`occupy*` below).
type ZoneContents =
    { Actors : (EntityRef * ActorState) list
      Props : (EntityRef * PropState) list
      Waypoints : (EntityRef * Waypoint) list
      Triggers : (EntityRef * TriggerState) list
      // The environment carries its own parts (and their refs), so a part is not listed separately: loading the
      // value back restores them, the property `SetEnvironment`'s undo already relies on.
      Environment : Environment option
      Light : Texture.LightSettings
      Player : Handle voption }

/// Take a zone's contents. Authored state only: the same maps the per-kind `capture*` readers above answer from.
let captureZone (zone: Zone) : ZoneContents =
    let actors = ResizeArray<EntityRef * ActorState>(int zone.Actors.Count)
    zone.Actors.IteriAlive (fun index generation actor -> actors.Add(actorRef index generation, actor.State))
    let props = ResizeArray<EntityRef * PropState>(int zone.Props.Count)
    zone.Props.IteriAlive (fun index generation prop -> props.Add(propRef index generation, prop.State))
    let waypoints = ResizeArray<EntityRef * Waypoint>(int zone.Waypoints.Count)
    zone.Waypoints.IteriAlive (fun index generation waypoint -> waypoints.Add(waypointRef index generation, waypoint))
    let triggers = ResizeArray<EntityRef * TriggerState>(int zone.Triggers.Count)
    zone.Triggers.IteriAlive (fun index generation trigger -> triggers.Add(triggerRef index generation, trigger.State))
    { Actors = List.ofSeq actors
      Props = List.ofSeq props
      Waypoints = List.ofSeq waypoints
      Triggers = List.ofSeq triggers
      Environment = zone.Environment
      Light = zone.Light
      Player = zone.Player }

/// Put recorded contents into a zone, through the same calls an entity create makes, so a restored zone is built by
/// the one path that builds live entities. The zone must be **active**: `occupy*` creates a body and the body joins
/// the simulation as it is created, so only the active zone's bodies may be built — the caller makes the zone active
/// first (`History.undo`).
let occupyZone (zone: Zone) (physics: Physics.World) (assets: Assets.AssetManager) (contents: ZoneContents) : unit =
    do setEnvironment zone physics assets contents.Environment
    for ref, actor in contents.Actors do occupyActor zone physics assets ref actor
    for ref, prop in contents.Props do occupyProp zone physics assets ref prop
    for ref, waypoint in contents.Waypoints do occupyWaypoint zone ref waypoint
    for ref, trigger in contents.Triggers do occupyTrigger zone ref trigger
    zone.Light <- contents.Light
    zone.Player <- contents.Player

/// The contents of a fresh, empty zone — what a reset installs as the new `start` place (spec 084).
let emptyContents : ZoneContents =
    { Actors = []; Props = []; Waypoints = []; Triggers = []
      Environment = None; Light = Texture.defaultLight; Player = ValueNone }

// Load an environment asset, replacing any previous one, and return the built value
// so the caller's `SetEnvironment` command can retain it. Each load takes a fresh
// epoch, so refs from a previously loaded environment never resolve against this one.
let loadEnvironment (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (assetId: string) : Environment =
    let asset = assets.Environments.[assetId]
    // Request residency; the pin set is owned by the gameplay layer, not the load.
    do Assets.requestEnvironment assets assetId
    let epoch = zone.NextEnvironmentEpoch + 1u
    zone.NextEnvironmentEpoch <- epoch
    let baseEntity : Entity =
        { Name = asset.Name
          Transform = transformIdentity
          Enabled = true
          Visible = true
          Selectable = true
          Draggable = true }
    // Asset-authored: an empty parts map, materialised when the asset lands (`FromAsset`).
    let environment = buildEnvironment assetId baseEntity asset.Orientation [||] FromAsset epoch
    do setEnvironment zone world assets (Some environment)
    environment

// Load a registered zone — the record an author filed away — in place of the asset's own part list.
// The same path as `loadEnvironment`, a fresh epoch included, so the two cannot diverge; what differs
// is only where the settings and the parts come from, which is what makes a registration reproducible
// (spec 034 FR-012).
let loadEnvironmentFrom (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (template: ZoneTemplate) : Environment =
    do Assets.requestEnvironment assets template.AssetId
    let epoch = zone.NextEnvironmentEpoch + 1u
    zone.NextEnvironmentEpoch <- epoch
    // A registered template's parts are final (`Authored`), never filled from the asset (spec 034).
    let environment = buildEnvironment template.AssetId template.Base template.Orientation template.Parts Authored epoch
    do setEnvironment zone world assets (Some environment)
    environment

// Populate a **fresh** zone from a registered template: its environment, its light and its placed props.
// The content half of `editor/Spawning.instantiateZone`, without that caller's replace-and-undo machinery —
// a runtime add (spec 062) builds a zone that did not exist, so there is nothing to replace. The zone is
// expected to be empty; the caller detaches every body built here when the zone is left inactive.
let loadTemplate (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager) (template: ZoneTemplate) : unit =
    do ignore (loadEnvironmentFrom zone world assets template)
    zone.Light <- template.Light
    for state in template.Props do
        let assetId =
            match state.AssetId with
            | Some assetId -> assetId
            | None -> failwith "loadTemplate: a recorded prop must name an asset"
        let binding = { AssetId = Some assetId; SubmeshIndex = state.SubmeshIndex; SubmeshCount = state.SubmeshCount }
        do ignore (spawnPropBinding zone world assets state.Base binding)

// ── Zone-level physics lifecycle ──
//
// A switch detaches the outgoing zone's bodies from the simulation and re-attaches
// the incoming zone's, without destroying bodies or shapes (Jolt's RemoveBody/
// AddBody). Deletion is the only place bodies are destroyed.

let deactivatePhysics (zone: Zone) (world: Physics.World) : unit =
    let detach (attachment: PhysicsAttachment<'T> option) : unit =
        match attachment with
        | Some ph -> Physics.removeBody world ph.Body
        | None -> ()

    let actorSlots = zone.Actors.Slots
    for i in 0 .. actorSlots.Length - 1 do
        let slot = &actorSlots.[i]
        if slot.Alive then
            detach slot.Value.Physics

    let propSlots = zone.Props.Slots
    for i in 0 .. propSlots.Length - 1 do
        let slot = &propSlots.[i]
        if slot.Alive then
            detach slot.Value.Physics

    match zone.Environment with
    | Some environment ->
        let partSlots = environment.Parts.Slots
        for i in 0 .. partSlots.Length - 1 do
            let slot = &partSlots.[i]
            if slot.Alive then
                detach slot.Value.Physics
    | None -> ()

let activatePhysics (zone: Zone) (world: Physics.World) : unit =
    let attach (attachment: PhysicsAttachment<'T> option) : unit =
        match attachment with
        | Some ph -> Physics.addBody world ph.Body
        | None -> ()

    let actorSlots = zone.Actors.Slots
    for i in 0 .. actorSlots.Length - 1 do
        let slot = &actorSlots.[i]
        if slot.Alive then
            attach slot.Value.Physics

    let propSlots = zone.Props.Slots
    for i in 0 .. propSlots.Length - 1 do
        let slot = &propSlots.[i]
        if slot.Alive then
            attach slot.Value.Physics

    match zone.Environment with
    | Some environment ->
        let partSlots = environment.Parts.Slots
        for i in 0 .. partSlots.Length - 1 do
            let slot = &partSlots.[i]
            if slot.Alive then
                attach slot.Value.Physics
    | None -> ()

    Physics.optimizeBroadPhase world

// Delete-time cleanup: destroy every body the zone owns (it is inactive, so the
// bodies are detached), release every renderer and the zone's static-scene batch,
// and drop the body map. Nothing here is undoable.
let release (zone: Zone) (world: Physics.World) (device: Gpu.Device) : unit =
    let actorSlots = zone.Actors.Slots
    for i in 0 .. actorSlots.Length - 1 do
        let slot = &actorSlots.[i]
        if slot.Alive then
            let a = &slot.Value
            match a.Physics with
            | Some ph -> do Physics.releaseBody world ph.Body
            | None -> ()
            do Actor.releaseRenderer a

    let propSlots = zone.Props.Slots
    for i in 0 .. propSlots.Length - 1 do
        let slot = &propSlots.[i]
        if slot.Alive then
            let p = &slot.Value
            match p.Physics with
            | Some ph -> do Physics.releaseBody world ph.Body
            | None -> ()
            do Prop.releaseRenderer p

    match zone.Environment with
    | Some environment ->
        let partSlots = environment.Parts.Slots
        for i in 0 .. partSlots.Length - 1 do
            let slot = &partSlots.[i]
            if slot.Alive then
                let p = &slot.Value
                match p.Physics with
                | Some ph -> do Physics.releaseBody world ph.Body
                | None -> ()
                do Prop.releaseRenderer p
    | None -> ()

    do EnvironmentBatch.release device zone.Batch
    do zone.BodyToEntity.Clear()
