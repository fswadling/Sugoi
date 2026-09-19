module ZoneTests

// Zone-level `EntityRef` semantics: each kind's entities live in their own slot map
// (actors, placed props, the current environment's parts), so a ref names a specific
// slot incarnation — a deleted entity is rejected even after another reuses its
// index, and undo revives the original ref. A part ref carries its environment's
// epoch, so a ref from a previous environment cannot resolve against a same-index
// part in the current one.
//
// Early tests drive the slot maps directly; the environment-lifecycle tests at the
// end drive the real `Zone.loadEnvironment`/`setEnvironment` through a headless
// asset manager so body teardown and slot retention are covered for real.

open Xunit
open System
open System.Collections.Generic
open System.Numerics
open Command
open Entity
open EnvironmentAsset
open Gltf
open Physics
open SlotMap
open Zone

let private entity (name: string) : Entity =
    { Name = name
      Transform = transformIdentity
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

let private prop (name: string) : Prop =
    { Base = entity name
      AssetId = Some "crate"
      SubmeshIndex = 0
      SubmeshCount = 0
      Physics = None
      Renderer = Unchecked.defaultof<EnvironmentRender.EnvironmentRender> }

// An environment part: a prop with no asset id, which is how `buildEnvironment`
// populates an environment's own parts map.
let private part (name: string) : Prop =
    { Base = entity name
      AssetId = None
      SubmeshIndex = 0
      SubmeshCount = 0
      Physics = None
      Renderer = Unchecked.defaultof<EnvironmentRender.EnvironmentRender> }

let private actor (name: string) : Actor =
    { Base = entity name
      AssetId = "character"
      Physics = None
      ShowCollider = false
      Renderer = Unchecked.defaultof<ActorRender.ActorRender> }

// An environment owning `parts`, installed directly (loading one needs assets).
// The epoch scopes its part refs; tests assign it explicitly.
let private environment (epoch: uint32) (parts: SlotMap<Prop>) : Environment =
    { Base = entity "room"; AssetId = "room"; Orientation = Quaternion.Identity; Epoch = epoch; Parts = parts }

// ── Headless asset fixture ──
//
// `Zone.loadEnvironment`/`setEnvironment` build real Jolt bodies and per-part
// renderers, but the prop/part render path (`EnvironmentRender.create`) and the
// collider path never touch the GPU device. A hand-built manager with a zero
// `Device` therefore drives the whole lifecycle for real: only the fields the
// code reads are populated (submesh positions/indices for physics, part ranges).

let private bounds (mn: Vector3) (mx: Vector3) : Gltf.Bounds =
    { Min = mn; Max = mx }

let private assetSubmesh (positions: float32 array) (indices: int array) : StaticSubmesh =
    { Mesh = Unchecked.defaultof<StaticMesh.StaticMesh>
      Material = Unchecked.defaultof<Texture.Material>
      Positions = positions
      Indices = indices }

let private assetPart (name: string) (submeshIndex: int) (submeshCount: int) : EnvironmentPart =
    { Name = name
      HierarchyPath = name
      World = Matrix4x4.Identity
      Bounds = bounds Vector3.Zero Vector3.One
      SubmeshIndex = submeshIndex
      SubmeshCount = submeshCount }

let private envAsset (id: string) (submeshes: StaticSubmesh array) (parts: EnvironmentPart array) : EnvironmentAsset.EnvironmentAsset =
    { Id = id
      Name = id
      ModelPath = ""
      Orientation = Quaternion.Identity
      Bounds = bounds Vector3.Zero Vector3.One
      Submeshes = submeshes
      Parts = parts }

// A device-free manager: safe because the prop/part paths never dereference Device.
let private headlessAssets (environments: (string * EnvironmentAsset.EnvironmentAsset) list) : Assets.AssetManager =
    let envs = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    for (id, asset) in environments do envs.[id] <- asset
    { Device = IntPtr.Zero
      Actors = Dictionary()
      Props = Dictionary()
      Environments = envs }

// A one-triangle room whose single part covers submesh 0.
let private roomAssets () : Assets.AssetManager =
    let submeshes = [| assetSubmesh [| 0.0f; 0.0f; 0.0f;  1.0f; 0.0f; 0.0f;  0.0f; 1.0f; 0.0f |] [| 0; 1; 2 |] |]
    headlessAssets [ "room", envAsset "room" submeshes [| assetPart "wall" 0 1 |] ]

// A device-free manager carrying one whole-model prop asset (`crate`), so
// `Zone.spawnProp` can build a mesh collider + body without a GPU.
let private propAssets () : Assets.AssetManager =
    let submeshes = [| assetSubmesh [| 0.0f; 0.0f; 0.0f;  1.0f; 0.0f; 0.0f;  0.0f; 1.0f; 0.0f |] [| 0; 1; 2 |] |]
    let props = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    props.["crate"] <- envAsset "crate" submeshes [||]
    { Device = IntPtr.Zero
      Actors = Dictionary()
      Props = props
      Environments = Dictionary() }

// The body attached to a live prop ref (spawned through the real mesh-body path).
let private propBody (zone: Zone) (ref: EntityRef) : BodyId =
    (zone.Props.Get { Index = ref.Index; Generation = ref.Generation }).Physics.Value.Body

// ── Placed prop refs (through the real remove path) ──

[<Fact>]
let ``a live prop ref resolves to its entity`` () =
    let zone = Zone.create ()
    let handle = zone.Props.Add(prop "crate")
    let ref = propRef handle.Index handle.Generation
    Assert.True(Zone.isAlive zone ref)
    Assert.Equal("crate", (Zone.tryEntityAt zone ref).Value.Name)

[<Fact>]
let ``removing a prop makes its ref stale`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let handle = zone.Props.Add(prop "crate")
    let ref = propRef handle.Index handle.Generation

    Zone.removeEntity zone world ref

    Assert.False(Zone.isAlive zone ref)
    Assert.True(Option.isNone (Zone.tryEntityAt zone ref))

[<Fact>]
let ``a stale prop ref does not alias the prop that reuses its slot`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let handle = zone.Props.Add(prop "first")
    let ref = propRef handle.Index handle.Generation

    Zone.removeEntity zone world ref

    // The freed index is reused, so same index but a fresh generation...
    let handle2 = zone.Props.Add(prop "second")
    Assert.Equal(handle.Index, handle2.Index)
    Assert.NotEqual(handle.Generation, handle2.Generation)

    // ...and the stale ref resolves to neither the old nor the new entity.
    Assert.True(Option.isNone (Zone.tryEntityAt zone ref))
    let ref2 = propRef handle2.Index handle2.Generation
    Assert.Equal("second", (Zone.tryEntityAt zone ref2).Value.Name)

[<Fact>]
let ``undo restores the recorded generation and redo removes it again`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let original = prop "crate"
    let handle = zone.Props.Add(original)
    let ref = propRef handle.Index handle.Generation

    Zone.removeEntity zone world ref
    Assert.True(Option.isNone (Zone.tryEntityAt zone ref))

    // Undo re-occupies the freed slot, restoring the generation the ref recorded.
    zone.Props.Occupy(handleOf ref, original)
    Assert.True(Zone.isAlive zone ref)
    Assert.Equal("crate", (Zone.tryEntityAt zone ref).Value.Name)
    Assert.Equal(1u, zone.Props.Count)

    // Redo deletes it once more; the ref goes stale again.
    Zone.removeEntity zone world ref
    Assert.True(Option.isNone (Zone.tryEntityAt zone ref))

[<Fact>]
let ``setTransform updates a placed prop in place and keeps its ref valid`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let handle = zone.Props.Add(prop "crate")
    let ref = propRef handle.Index handle.Generation

    let moved = { transformIdentity with Position = Vector3(1.0f, 2.0f, 3.0f) }
    Zone.setTransform zone world ref moved

    Assert.True(Zone.isAlive zone ref)
    Assert.Equal(moved.Position, (Zone.tryEntityAt zone ref).Value.Transform.Position)

// ── Waypoint refs ──

[<Fact>]
let ``a spawned waypoint resolves to its base entity's transform`` () =
    let zone = Zone.create ()
    let transform =
        { transformIdentity with
            Position = Vector3(4.0f, 5.0f, 6.0f)
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.5f) }
    let ref = Zone.spawnWaypoint zone { entity "start" with Transform = transform }

    Assert.True(Zone.isAlive zone ref)
    Assert.Equal(transform, (Zone.tryEntityAt zone ref).Value.Transform)

[<Fact>]
let ``setTransform moves and rotates a waypoint in place and keeps its ref valid`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let ref = Zone.spawnWaypoint zone (entity "start")
    let moved =
        { transformIdentity with
            Position = Vector3(1.0f, 2.0f, 3.0f)
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f) }

    Zone.setTransform zone world ref moved

    Assert.True(Zone.isAlive zone ref)
    Assert.Equal(moved, (Zone.tryEntityAt zone ref).Value.Transform)

[<Fact>]
let ``removing a waypoint makes its ref stale`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let ref = Zone.spawnWaypoint zone (entity "start")

    Zone.removeEntity zone world ref

    Assert.False(Zone.isAlive zone ref)
    Assert.True(Option.isNone (Zone.tryEntityAt zone ref))

[<Fact>]
let ``a stale waypoint ref does not alias the waypoint that reuses its slot`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let ref = Zone.spawnWaypoint zone (entity "first")

    Zone.removeEntity zone world ref

    // The freed index is reused, so same index but a fresh generation...
    let ref2 = Zone.spawnWaypoint zone (entity "second")
    Assert.Equal(ref.Index, ref2.Index)
    Assert.NotEqual(ref.Generation, ref2.Generation)

    // ...and the stale ref resolves to neither the old nor the new waypoint.
    Assert.True(Option.isNone (Zone.tryEntityAt zone ref))
    Assert.Equal("second", (Zone.tryEntityAt zone ref2).Value.Name)

[<Fact>]
let ``waypoints live in their own slot map, not the prop map`` () =
    let zone = Zone.create ()
    let ref = Zone.spawnWaypoint zone (entity "start")

    Assert.Equal(EntityKind.Waypoint, ref.Kind)
    Assert.Equal(1u, zone.Waypoints.Count)
    Assert.Equal(0u, zone.Props.Count)

// ── Names ──

[<Fact>]
let ``setName renames each stored kind in place`` () =
    let zone = Zone.create ()
    let actorHandle = zone.Actors.Add(actor "hero")
    let actorRef = actorRef actorHandle.Index actorHandle.Generation
    let propHandle = zone.Props.Add(prop "crate")
    let propRef = propRef propHandle.Index propHandle.Generation
    let waypointRef = Zone.spawnWaypoint zone (entity "start")
    let parts = SlotMap<Prop>(4u)
    let partHandle = parts.Add(part "wall")
    zone.Environment <- Some (environment 1u parts)
    let partRef = partRef 1u partHandle.Index partHandle.Generation

    do Zone.setName zone actorRef "villain"
    do Zone.setName zone propRef "loot"
    do Zone.setName zone partRef "floor"
    do Zone.setName zone waypointRef "exit"

    Assert.Equal("villain", (Zone.tryEntityAt zone actorRef).Value.Name)
    Assert.Equal("loot", (Zone.tryEntityAt zone propRef).Value.Name)
    Assert.Equal("floor", (Zone.tryEntityAt zone partRef).Value.Name)
    Assert.Equal("exit", (Zone.tryEntityAt zone waypointRef).Value.Name)

[<Fact>]
let ``a rename leaves the transform, body, and binding untouched`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = propAssets ()
    let ref = Zone.spawnProp zone world assets (entity "crate") "crate"
    let before = (Zone.tryEntityAt zone ref).Value.Transform
    let body = propBody zone ref

    Zone.setName zone ref "loot"

    Assert.Equal("loot", (Zone.tryEntityAt zone ref).Value.Name)
    Assert.Equal(before, (Zone.tryEntityAt zone ref).Value.Transform)
    Assert.Equal<BodyId>(body, propBody zone ref)
    Assert.Equal<EntityRef>(ref, zone.BodyToEntity.[body])
    Assert.Equal(Some "crate", (Zone.bindingAt zone ref).AssetId)
    Assert.True(Zone.isAlive zone ref)

[<Fact>]
let ``a rename addressed to a stale or unstored ref changes nothing`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let ref = Zone.spawnWaypoint zone (entity "start")

    Zone.removeEntity zone world ref

    do Zone.setName zone ref "ghost"
    do Zone.setName zone { Kind = EntityKind.Environment; Index = 0u; Generation = 0u; Epoch = 0u } "ghost"
    do Zone.setName zone { Kind = EntityKind.Trigger; Index = 0u; Generation = 0u; Epoch = 0u } "ghost"

    // Nothing is resurrected and nothing is created: the freed slot stays free.
    Assert.Equal(0u, zone.Waypoints.Count)
    Assert.True(Option.isNone (Zone.tryEntityAt zone ref))

[<Fact>]
let ``two entities may share a name and still resolve separately`` () =
    let zone = Zone.create ()
    let first = Zone.spawnWaypoint zone (entity "start")
    let second = Zone.spawnWaypoint zone (entity "finish")

    Zone.setName zone second "start"

    Assert.Equal("start", (Zone.tryEntityAt zone first).Value.Name)
    Assert.Equal("start", (Zone.tryEntityAt zone second).Value.Name)
    Assert.NotEqual<EntityRef>(first, second)

    // Renaming one leaves the other alone, despite the shared name.
    Zone.setName zone first "renamed"
    Assert.Equal("renamed", (Zone.tryEntityAt zone first).Value.Name)
    Assert.Equal("start", (Zone.tryEntityAt zone second).Value.Name)

[<Fact>]
let ``repeated renames add no bodies and no slots`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = propAssets ()
    let ref = Zone.spawnProp zone world assets (entity "crate") "crate"
    let body = propBody zone ref
    let bodies = zone.BodyToEntity.Count

    for i in 0 .. 99 do
        Zone.setName zone ref (sprintf "crate-%d" i)

    // A rename is a label change: no body is created or destroyed, no slot moves, and
    // no renderer is touched (so there is nothing to leak).
    Assert.Equal(bodies, zone.BodyToEntity.Count)
    Assert.Equal(1u, zone.Props.Count)
    Assert.Equal<BodyId>(body, propBody zone ref)
    Assert.Equal("crate-99", (Zone.tryEntityAt zone ref).Value.Name)

// ── Mesh-body transforms: in place on a move, rebuilt only on a scale change ──

[<Fact>]
let ``moving a prop updates its body in place instead of rebuilding`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = propAssets ()
    let ref = Zone.spawnProp zone world assets (entity "crate") "crate"
    let body = propBody zone ref

    let moved = { transformIdentity with Position = Vector3(1.0f, 2.0f, 3.0f) }
    Zone.setTransform zone world ref moved

    // Same body (never destroyed/rebuilt), now at the new position.
    Assert.Equal<BodyId>(body, propBody zone ref)
    let (position, _) = Physics.getBodyTransform world body
    Assert.Equal(moved.Position, position)
    Assert.Equal<EntityRef>(ref, zone.BodyToEntity.[body])
    Assert.Equal(moved.Position, (Zone.tryEntityAt zone ref).Value.Transform.Position)

[<Fact>]
let ``rotating a prop updates its body in place instead of rebuilding`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = propAssets ()
    let ref = Zone.spawnProp zone world assets (entity "crate") "crate"
    let body = propBody zone ref

    let rotated = { transformIdentity with Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.0f) }
    Zone.setTransform zone world ref rotated

    Assert.Equal<BodyId>(body, propBody zone ref)
    let (_, rotation) = Physics.getBodyTransform world body
    Assert.True(MathF.Abs(Quaternion.Dot(rotated.Rotation, rotation)) > 0.9999f, sprintf "expected %A, got %A" rotated.Rotation rotation)

[<Fact>]
let ``scaling a prop rebuilds its body to match`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = propAssets ()
    let ref = Zone.spawnProp zone world assets (entity "crate") "crate"
    let before = propBody zone ref

    // Non-uniform, so a change on any axis must still invalidate the shape.
    let scaled = { transformIdentity with Scale = Vector3(2.0f, 3.0f, 4.0f) }
    Zone.setTransform zone world ref scaled

    let after = propBody zone ref
    Assert.NotEqual<BodyId>(before, after)
    Assert.False(zone.BodyToEntity.ContainsKey(before))
    Assert.Equal<EntityRef>(ref, zone.BodyToEntity.[after])
    Assert.Equal(scaled.Scale, (Zone.tryEntityAt zone ref).Value.Transform.Scale)

[<Fact>]
let ``a move, scale, move sequence keeps the body mapping consistent`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = propAssets ()
    let ref = Zone.spawnProp zone world assets (entity "crate") "crate"

    // Move: same body, still one mapping.
    let moved = { transformIdentity with Position = Vector3(1.0f, 0.0f, 0.0f) }
    Zone.setTransform zone world ref moved
    let afterMove = propBody zone ref
    Assert.Equal(1, zone.BodyToEntity.Count)
    Assert.Equal<EntityRef>(ref, zone.BodyToEntity.[afterMove])

    // Scale: a fresh body replaces it; the old body's mapping is gone.
    let scaled = { moved with Scale = Vector3(2.0f, 2.0f, 2.0f) }
    Zone.setTransform zone world ref scaled
    let afterScale = propBody zone ref
    Assert.NotEqual<BodyId>(afterMove, afterScale)
    Assert.False(zone.BodyToEntity.ContainsKey(afterMove))
    Assert.Equal(1, zone.BodyToEntity.Count)
    Assert.Equal<EntityRef>(ref, zone.BodyToEntity.[afterScale])

    // Move again: the rebuilt body is updated in place.
    let movedAgain = { scaled with Position = Vector3(2.0f, 0.0f, 2.0f) }
    Zone.setTransform zone world ref movedAgain
    Assert.Equal<BodyId>(afterScale, propBody zone ref)
    Assert.Equal(1, zone.BodyToEntity.Count)

// ── Environment parts (their own map, scoped by epoch) ──

[<Fact>]
let ``environment parts use their own slot map, not the placed-prop map`` () =
    let zone = Zone.create ()
    let placedHandle = zone.Props.Add(prop "crate")          // index 0 in Props
    let parts = SlotMap<Prop>(8u)
    let partHandle = parts.Add(part "wall")                  // index 0 in Parts too
    zone.Environment <- Some (environment 1u parts)

    // The same index in two maps; both refs are live and resolve independently, so
    // an environment load can never take a placed prop's slot.
    Assert.Equal(placedHandle.Index, partHandle.Index)
    let placedRef = propRef placedHandle.Index placedHandle.Generation
    let wallRef = partRef 1u partHandle.Index partHandle.Generation
    Assert.True(Zone.isAlive zone placedRef)
    Assert.True(Zone.isAlive zone wallRef)
    Assert.Equal("crate", (Zone.tryEntityAt zone placedRef).Value.Name)
    Assert.Equal("wall", (Zone.tryEntityAt zone wallRef).Value.Name)

[<Fact>]
let ``a part ref from one environment does not resolve in another`` () =
    let zone = Zone.create ()
    let partsA = SlotMap<Prop>(8u)
    let handleA = partsA.Add(part "wall-a")
    zone.Environment <- Some (environment 1u partsA)
    let refA = partRef 1u handleA.Index handleA.Generation

    // Swap in a different environment whose part occupies the same index with the
    // same slot generation. The ref from the first must not alias it.
    let partsB = SlotMap<Prop>(8u)
    let handleB = partsB.Add(part "wall-b")
    zone.Environment <- Some (environment 2u partsB)
    Assert.Equal(handleA.Index, handleB.Index)
    Assert.Equal(handleA.Generation, handleB.Generation)

    Assert.False(Zone.isAlive zone refA)
    Assert.True(Option.isNone (Zone.tryEntityAt zone refA))
    let refB = partRef 2u handleB.Index handleB.Generation
    Assert.Equal("wall-b", (Zone.tryEntityAt zone refB).Value.Name)

[<Fact>]
let ``a part ref is not alive when no environment is loaded`` () =
    let zone = Zone.create ()
    let ref = partRef 1u 0u 0u
    Assert.False(Zone.isAlive zone ref)
    Assert.True(Option.isNone (Zone.tryEntityAt zone ref))

[<Fact>]
let ``removing a part frees its slot without disturbing placed props`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let placedHandle = zone.Props.Add(prop "placed")
    let parts = SlotMap<Prop>(8u)
    let partHandle = parts.Add(part "wall")
    zone.Environment <- Some (environment 1u parts)
    let wallRef = partRef 1u partHandle.Index partHandle.Generation

    Zone.removeEntity zone world wallRef

    Assert.False(Zone.isAlive zone wallRef)
    // The placed prop and its map are untouched.
    let placedRef = propRef placedHandle.Index placedHandle.Generation
    Assert.True(Zone.isAlive zone placedRef)
    Assert.Equal(1u, zone.Props.Count)

[<Fact>]
let ``removing a foreign part ref leaves the current environment's part intact`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let partsA = SlotMap<Prop>(8u)
    let handleA = partsA.Add(part "wall-a")
    zone.Environment <- Some (environment 1u partsA)
    let refA = partRef 1u handleA.Index handleA.Generation

    let partsB = SlotMap<Prop>(8u)
    let handleB = partsB.Add(part "wall-b")
    zone.Environment <- Some (environment 2u partsB)
    let refB = partRef 2u handleB.Index handleB.Generation

    // A ref naming environment A is a no-op while B is loaded, even though B has a
    // live part at the same index/generation.
    Zone.removeEntity zone world refA

    Assert.True(Zone.isAlive zone refB)
    Assert.Equal(1u, partsB.Count)

[<Fact>]
let ``setTransform updates an environment part in place`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let parts = SlotMap<Prop>(8u)
    let handle = parts.Add(part "wall")
    zone.Environment <- Some (environment 1u parts)
    let ref = partRef 1u handle.Index handle.Generation

    let moved = { transformIdentity with Position = Vector3(1.0f, 2.0f, 3.0f) }
    Zone.setTransform zone world ref moved

    Assert.True(Zone.isAlive zone ref)
    Assert.Equal(moved.Position, (Zone.tryEntityAt zone ref).Value.Transform.Position)

[<Fact>]
let ``bindingAt resolves an environment part's submesh range`` () =
    let zone = Zone.create ()
    let parts = SlotMap<Prop>(8u)
    let handle = parts.Add({ part "wall" with SubmeshIndex = 2; SubmeshCount = 3 })
    zone.Environment <- Some (environment 1u parts)
    let ref = partRef 1u handle.Index handle.Generation

    let binding = Zone.bindingAt zone ref

    Assert.Equal(None, binding.AssetId)
    Assert.Equal(2, binding.SubmeshIndex)
    Assert.Equal(3, binding.SubmeshCount)

// ── Actor refs (slot map, to avoid the renderer release) ──

[<Fact>]
let ``a stale actor ref does not alias the actor that reuses its slot`` () =
    let zone = Zone.create ()
    let handle = zone.Actors.Add(actor "first")
    let ref = actorRef handle.Index handle.Generation

    zone.Actors.Free(handle)

    let handle2 = zone.Actors.Add(actor "second")
    Assert.Equal(handle.Index, handle2.Index)
    Assert.True(Option.isNone (Zone.tryEntityAt zone ref))
    let ref2 = actorRef handle2.Index handle2.Generation
    Assert.Equal("second", (Zone.tryEntityAt zone ref2).Value.Name)

// ── Environment lifecycle (headless assets, real bodies) ──

[<Fact>]
let ``loading an environment attaches a body per part mapped to its part ref`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = roomAssets ()

    let environment = Zone.loadEnvironment zone world assets "room"

    Assert.Equal(1u, environment.Parts.Count)
    Assert.True((environment.Parts.Get { Index = 0u; Generation = 0u }).Physics.IsSome)
    let body = (environment.Parts.Get { Index = 0u; Generation = 0u }).Physics.Value.Body
    Assert.True(zone.BodyToEntity.ContainsKey(body))
    Assert.Equal<EntityRef>(partRef environment.Epoch 0u 0u, zone.BodyToEntity.[body])

[<Fact>]
let ``swapping environments tears down the outgoing bodies, retains slots, and rebuilds on return`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = roomAssets ()

    let first = Zone.loadEnvironment zone world assets "room"
    let firstRef = partRef first.Epoch 0u 0u
    let firstBody = (first.Parts.Get { Index = 0u; Generation = 0u }).Physics.Value.Body
    Assert.True(Zone.isAlive zone firstRef)

    // Loading again takes a fresh epoch and deactivates `first` in place.
    let second = Zone.loadEnvironment zone world assets "room"
    Assert.NotEqual<uint32>(first.Epoch, second.Epoch)
    Assert.False(zone.BodyToEntity.ContainsKey(firstBody))       // outgoing bodies destroyed
    Assert.True((first.Parts.Get { Index = 0u; Generation = 0u }).Physics.IsNone)        // part cleared, slot kept
    Assert.False(Zone.isAlive zone firstRef)                     // epoch scoping

    // Swapping back (what undoing the second load does) revives the same slot.
    Zone.setEnvironment zone world assets (Some first)
    Assert.True(Zone.isAlive zone firstRef)
    let revivedBody = (first.Parts.Get { Index = 0u; Generation = 0u }).Physics.Value.Body
    Assert.True(zone.BodyToEntity.ContainsKey(revivedBody))
    Assert.Equal<EntityRef>(firstRef, zone.BodyToEntity.[revivedBody])

[<Fact>]
let ``a part transform is inert while a different environment is loaded`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = roomAssets ()

    let first = Zone.loadEnvironment zone world assets "room"
    let firstRef = partRef first.Epoch 0u 0u
    let second = Zone.loadEnvironment zone world assets "room"

    let moved = { transformIdentity with Position = Vector3(1.0f, 2.0f, 3.0f) }
    Zone.setTransform zone world firstRef moved

    // The current environment's same-index part is untouched...
    Assert.Equal(Vector3.Zero, (second.Parts.Get { Index = 0u; Generation = 0u }).Base.Transform.Position)
    // ...and the stale ref never reaches its own (inactive) environment's part.
    Zone.setEnvironment zone world assets (Some first)
    Assert.Equal(Vector3.Zero, (first.Parts.Get { Index = 0u; Generation = 0u }).Base.Transform.Position)

[<Fact>]
let ``undo and redo of an environment load route through the real lifecycle`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = roomAssets ()

    let previous = zone.Environment
    let environment = Zone.loadEnvironment zone world assets "room"
    let reference = partRef environment.Epoch 0u 0u
    let stack = UndoStack.push (UndoStack.create ()) (SetEnvironment (previous, Some environment))
    Assert.True(Zone.isAlive zone reference)

    let stack = UndoStack.undo stack zone world assets
    Assert.True(Option.isNone zone.Environment)
    Assert.False(Zone.isAlive zone reference)

    let stack = UndoStack.redo stack zone world assets
    Assert.True(Option.isSome zone.Environment)
    Assert.True(Zone.isAlive zone reference)
