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
open TestSupport
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
    { State =
        { Base = entity name
          AssetId = Some "crate"
          SubmeshIndex = 0
          SubmeshCount = 0 }
      Physics = None
      Renderer = Unchecked.defaultof<EnvironmentRender.EnvironmentRender> }

// An environment part: a prop with no asset id, which is how `buildEnvironment`
// populates an environment's own parts map.
let private part (name: string) : Prop =
    { State =
        { Base = entity name
          AssetId = None
          SubmeshIndex = 0
          SubmeshCount = 0 }
      Physics = None
      Renderer = Unchecked.defaultof<EnvironmentRender.EnvironmentRender> }

let private actor (name: string) : Actor =
    { State =
        { Base = entity name
          AssetId = "character"
          ShowCollider = false }
      Physics = None
      Renderer = Unchecked.defaultof<ActorRender.ActorRender> }

// An environment owning `parts`, installed directly (loading one needs assets).
// The epoch scopes its part refs; tests assign it explicitly.
let private environment (epoch: uint32) (parts: SlotMap<Prop>) : Environment =
    { Base = entity "room"; AssetId = "room"; Orientation = Quaternion.Identity; Epoch = epoch; Parts = parts; PartsSource = Authored }

// ── Headless asset fixture ──
//
// `Zone.loadEnvironment`/`setEnvironment` build real Jolt bodies and per-part
// renderers, but the prop/part render path (`EnvironmentRender.create`) and the
// collider path never touch the GPU device. A hand-built manager with a zero
// `Device` therefore drives the whole lifecycle for real: only the fields the
// code reads are populated (submesh positions/indices for physics, part ranges).

// A device-free manager: safe because the prop/part paths never dereference Device.
let private headlessAssets (environments: (string * EnvironmentAsset.EnvironmentAsset) list) : Assets.AssetManager =
    let envs = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    for (id, asset) in environments do envs.[id] <- asset
    TestSupport.manager IntPtr.Zero (Dictionary()) (Dictionary()) envs

// A one-triangle room whose single part covers submesh 0.
let private roomAssets () : Assets.AssetManager =
    let submeshes = [| loadedSubmesh [| 0.0f; 0.0f; 0.0f;  1.0f; 0.0f; 0.0f;  0.0f; 1.0f; 0.0f |] [| 0; 1; 2 |] |]
    headlessAssets [ "room", residentAsset "room" submeshes [| TestSupport.part "wall" 0 1 |] ]

// A device-free manager carrying one whole-model prop asset (`crate`), so
// `Zone.spawnProp` can build a mesh collider + body without a GPU.
let private propAssets () : Assets.AssetManager =
    let submeshes = [| loadedSubmesh [| 0.0f; 0.0f; 0.0f;  1.0f; 0.0f; 0.0f;  0.0f; 1.0f; 0.0f |] [| 0; 1; 2 |] |]
    let props = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    props.["crate"] <- residentAsset "crate" submeshes [||]
    TestSupport.manager IntPtr.Zero (Dictionary()) props (Dictionary())

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

    Zone.removeProp zone world ref

    Assert.False(Zone.isAlive zone ref)
    Assert.True(Option.isNone (Zone.tryEntityAt zone ref))

[<Fact>]
let ``a stale prop ref does not alias the prop that reuses its slot`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let handle = zone.Props.Add(prop "first")
    let ref = propRef handle.Index handle.Generation

    Zone.removeProp zone world ref

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

    Zone.removeProp zone world ref
    Assert.True(Option.isNone (Zone.tryEntityAt zone ref))

    // Undo re-occupies the freed slot, restoring the generation the ref recorded.
    zone.Props.Occupy(handleOf ref, original)
    Assert.True(Zone.isAlive zone ref)
    Assert.Equal("crate", (Zone.tryEntityAt zone ref).Value.Name)
    Assert.Equal(1u, zone.Props.Count)

    // Redo deletes it once more; the ref goes stale again.
    Zone.removeProp zone world ref
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

    Zone.removeWaypoint zone ref

    Assert.False(Zone.isAlive zone ref)
    Assert.True(Option.isNone (Zone.tryEntityAt zone ref))

[<Fact>]
let ``a stale waypoint ref does not alias the waypoint that reuses its slot`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let ref = Zone.spawnWaypoint zone (entity "first")

    Zone.removeWaypoint zone ref

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
    zone.EnvironmentState <- Active (environment 1u parts)
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
    Assert.Equal(Some "crate", (Zone.captureProp zone ref).AssetId)
    Assert.True(Zone.isAlive zone ref)

[<Fact>]
let ``a rename addressed to a stale or unstored ref changes nothing`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let ref = Zone.spawnWaypoint zone (entity "start")

    Zone.removeWaypoint zone ref

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

// ── Chain links ──

[<Fact>]
let ``a link resolves to its target and clears when broken`` () =
    let zone = Zone.create ()
    let a = Zone.spawnWaypoint zone (entity "a")
    let b = Zone.spawnWaypoint zone (entity "b")

    do Zone.setWaypointNext zone a (Some b)
    Assert.Equal(Some b, Zone.waypointNext zone a)
    Assert.True(Option.isNone (Zone.waypointNext zone b))

    do Zone.setWaypointNext zone a None
    Assert.True(Option.isNone (Zone.waypointNext zone a))

[<Fact>]
let ``a chain reads in the order it was linked`` () =
    let zone = Zone.create ()
    let a = Zone.spawnWaypoint zone (entity "a")
    let b = Zone.spawnWaypoint zone (entity "b")
    let c = Zone.spawnWaypoint zone (entity "c")

    do Zone.setWaypointNext zone a (Some b)
    do Zone.setWaypointNext zone b (Some c)

    // Walk the links. Nothing in the app walks a chain yet, so the walk lives here.
    let rec chain (ref: EntityRef) (acc: EntityRef list) =
        match Zone.waypointNext zone ref with
        | Some next -> chain next (next :: acc)
        | None -> List.rev acc

    Assert.Equal<EntityRef list>([ b; c ], chain a [])

[<Fact>]
let ``a waypoint cannot be its own next`` () =
    let zone = Zone.create ()
    let a = Zone.spawnWaypoint zone (entity "a")
    let b = Zone.spawnWaypoint zone (entity "b")
    do Zone.setWaypointNext zone a (Some b)

    let mutable message = ""
    try
        Zone.setWaypointNext zone a (Some a)
    with ex ->
        message <- ex.Message

    Assert.Contains("cannot be its own next", message)
    Assert.Equal(Some b, Zone.waypointNext zone a)

[<Fact>]
let ``a link to a deleted waypoint stops the chain`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let a = Zone.spawnWaypoint zone (entity "a")
    let b = Zone.spawnWaypoint zone (entity "b")
    let c = Zone.spawnWaypoint zone (entity "c")
    do Zone.setWaypointNext zone a (Some b)
    do Zone.setWaypointNext zone b (Some c)

    Zone.removeWaypoint zone b

    // The chain stops at the link that named b; b's own link is inert, not dangling.
    Assert.True(Option.isNone (Zone.waypointNext zone a))
    Assert.True(Option.isNone (Zone.waypointNext zone b))
    Assert.True(Zone.isAlive zone c)

[<Fact>]
let ``a waypoint reusing a deleted waypoint's slot is not aliased`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let a = Zone.spawnWaypoint zone (entity "a")
    let b = Zone.spawnWaypoint zone (entity "b")
    let c = Zone.spawnWaypoint zone (entity "c")
    do Zone.setWaypointNext zone a (Some b)
    do Zone.setWaypointNext zone b (Some c)

    Zone.removeWaypoint zone b

    // The freed index is reused, so same index but a fresh generation...
    let newcomer = Zone.spawnWaypoint zone (entity "newcomer")
    Assert.Equal(b.Index, newcomer.Index)
    Assert.NotEqual(b.Generation, newcomer.Generation)

    // ...and neither stale link resolves, not even to the replacement, which ends its
    // own chain.
    Assert.True(Option.isNone (Zone.waypointNext zone a))
    Assert.True(Option.isNone (Zone.waypointNext zone newcomer))

[<Fact>]
let ``chains live with their zone`` () =
    let first = Zone.create ()
    let second = Zone.create ()
    let a = Zone.spawnWaypoint first (entity "a")
    let b = Zone.spawnWaypoint first (entity "b")
    do Zone.setWaypointNext first a (Some b)

    Assert.Equal(Some b, Zone.waypointNext first a)
    // The other zone holds no waypoints at all, so none can resolve there.
    Assert.True(Option.isNone (Zone.waypointNext second a))
    Assert.Equal(0u, second.Waypoints.Count)

// ── Triggers ──

[<Fact>]
let ``a spawned trigger resolves with the default shape and no binding`` () =
    let zone = Zone.create ()
    let ref = Zone.spawnTrigger zone (entity "area")

    Assert.Equal(EntityKind.Trigger, ref.Kind)
    Assert.True(Zone.isAlive zone ref)
    Assert.Equal("area", (Zone.tryEntityAt zone ref).Value.Name)
    let trigger = (Zone.tryTriggerAt zone ref).Value
    Assert.Equal(1.0f, trigger.State.Radius)
    Assert.Equal(2.0f, trigger.State.Height)
    Assert.False(trigger.State.Interactable)
    Assert.False(trigger.Triggered)

[<Fact>]
let ``tryTriggerByName finds the first live trigger with a matching name`` () =
    let zone = Zone.create ()
    let first = Zone.spawnTrigger zone (entity "area")
    let second = Zone.spawnTrigger zone (entity "area")
    let other = Zone.spawnTrigger zone (entity "hall")

    Assert.Equal<EntityRef option>(Some first, Zone.tryTriggerByName zone "area") // first match wins
    Assert.Equal<EntityRef option>(Some other, Zone.tryTriggerByName zone "hall")
    Assert.Equal<EntityRef option>(None, Zone.tryTriggerByName zone "ghost")

    // a removed trigger is no longer found; the next with that name is
    Zone.removeTrigger zone first
    Assert.Equal<EntityRef option>(Some second, Zone.tryTriggerByName zone "area")

[<Fact>]
let ``setTrigger, setName and setTransform all work on a trigger`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let ref = Zone.spawnTrigger zone (entity "area")
    let payload = Zone.captureTrigger zone ref

    do Zone.setTrigger zone ref { payload with Radius = 3.5f; Prompt = "Open"; Interactable = true }
    do Zone.setName zone ref "hallway"
    do Zone.setTransform zone world ref { transformIdentity with Position = Vector3(1.0f, 2.0f, 3.0f) }

    let updated = (Zone.tryTriggerAt zone ref).Value
    Assert.Equal(3.5f, updated.State.Radius)
    Assert.Equal("Open", updated.State.Prompt)
    Assert.True(updated.State.Interactable)
    Assert.Equal("hallway", updated.State.Base.Name)
    Assert.Equal(Vector3(1.0f, 2.0f, 3.0f), updated.State.Base.Transform.Position)
    Assert.True(Zone.isAlive zone ref)

[<Fact>]
let ``setTrigger refuses a ref of another kind`` () =
    let zone = Zone.create ()
    let waypoint = Zone.spawnWaypoint zone (entity "start")
    let payload =
        { Base = entity "area"
          Radius = 1.0f
          Height = 1.0f
          Interactable = false
          Prompt = ""
          Cue = Cue.Fin }

    let mutable message = ""
    try
        Zone.setTrigger zone waypoint payload
    with ex ->
        message <- ex.Message

    Assert.Contains("is not a trigger", message)

[<Fact>]
let ``removing a trigger makes its ref stale and a reused slot is not aliased`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let ref = Zone.spawnTrigger zone (entity "first")

    Zone.removeTrigger zone ref
    Assert.True(ValueOption.isNone (Zone.tryTriggerAt zone ref))

    // The freed index is reused, so same index but a fresh generation...
    let second = Zone.spawnTrigger zone (entity "second")
    Assert.Equal(ref.Index, second.Index)
    Assert.NotEqual(ref.Generation, second.Generation)

    // ...and the stale ref resolves to neither the old nor the new trigger.
    Assert.True(ValueOption.isNone (Zone.tryTriggerAt zone ref))
    Assert.Equal("second", (Zone.tryTriggerAt zone second).Value.State.Base.Name)

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
    let struct (position, _) = Physics.getBodyTransform world body
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
    let struct (_, rotation) = Physics.getBodyTransform world body
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
    zone.EnvironmentState <- Active (environment 1u parts)

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
    zone.EnvironmentState <- Active (environment 1u partsA)
    let refA = partRef 1u handleA.Index handleA.Generation

    // Swap in a different environment whose part occupies the same index with the
    // same slot generation. The ref from the first must not alias it.
    let partsB = SlotMap<Prop>(8u)
    let handleB = partsB.Add(part "wall-b")
    zone.EnvironmentState <- Active (environment 2u partsB)
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
    zone.EnvironmentState <- Active (environment 1u parts)
    let wallRef = partRef 1u partHandle.Index partHandle.Generation

    Zone.removePart zone world wallRef

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
    zone.EnvironmentState <- Active (environment 1u partsA)
    let refA = partRef 1u handleA.Index handleA.Generation

    let partsB = SlotMap<Prop>(8u)
    let handleB = partsB.Add(part "wall-b")
    zone.EnvironmentState <- Active (environment 2u partsB)
    let refB = partRef 2u handleB.Index handleB.Generation

    // A ref naming environment A is a no-op while B is loaded, even though B has a
    // live part at the same index/generation.
    Zone.removePart zone world refA

    Assert.True(Zone.isAlive zone refB)
    Assert.Equal(1u, partsB.Count)

[<Fact>]
let ``setTransform updates an environment part in place`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let parts = SlotMap<Prop>(8u)
    let handle = parts.Add(part "wall")
    zone.EnvironmentState <- Active (environment 1u parts)
    let ref = partRef 1u handle.Index handle.Generation

    let moved = { transformIdentity with Position = Vector3(1.0f, 2.0f, 3.0f) }
    Zone.setTransform zone world ref moved

    Assert.True(Zone.isAlive zone ref)
    Assert.Equal(moved.Position, (Zone.tryEntityAt zone ref).Value.Transform.Position)

[<Fact>]
let ``a captured part carries its submesh range`` () =
    let zone = Zone.create ()
    let parts = SlotMap<Prop>(8u)
    let wall = part "wall"
    let handle = parts.Add({ wall with State.SubmeshIndex = 2; State.SubmeshCount = 3 })
    zone.EnvironmentState <- Active (environment 1u parts)
    let ref = partRef 1u handle.Index handle.Generation

    let record = Zone.capturePart zone ref

    Assert.Equal(None, record.AssetId)
    Assert.Equal(2, record.SubmeshIndex)
    Assert.Equal(3, record.SubmeshCount)

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
    Assert.Equal(Vector3.Zero, (second.Parts.Get { Index = 0u; Generation = 0u }).State.Base.Transform.Position)
    // ...and the stale ref never reaches its own (inactive) environment's part.
    Zone.setEnvironment zone world assets (Some first)
    Assert.Equal(Vector3.Zero, (first.Parts.Get { Index = 0u; Generation = 0u }).State.Base.Transform.Position)

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

// ── Deferred part materialisation (spec 053: `FromAsset` vs `Authored`, VR-010) ──

[<Fact>]
let ``an asset-authored environment materialises the asset's parts`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = roomAssets ()
    let environment = Zone.loadEnvironment zone world assets "room"
    // `FromAsset`: the parts are materialised from the asset once it is resident (the fixture has one).
    Assert.Equal(1u, environment.Parts.Count)
    Assert.True(Zone.isAlive zone (partRef environment.Epoch 0u 0u))

[<Fact>]
let ``a registered template's parts are never filled from the asset`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = roomAssets ()
    // A bare entry: an environment with no parts of its own (`Authored`). Loading it must keep it
    // empty and never materialise the asset's parts, which a `FromAsset` value would (spec 034).
    let template : ZoneTemplate.ZoneTemplate =
        { AssetId = "room"
          Base = entity "room"
          Orientation = Quaternion.Identity
          Light = Texture.defaultLight
          Parts = [||]
          Props = [||] }
    let environment = Zone.loadEnvironmentFrom zone world assets template
    Assert.Equal(0u, environment.Parts.Count)

// ── Recorded state: what a command carries ──

[<Fact>]
let ``an authored value cannot expose a resource or a runtime flag`` () =
    // SC-001, read off the types rather than a value: a payload that cannot name a body, a
    // renderer, or a runtime flag cannot carry one, whatever a given value happens to hold.
    let forbidden = [ "Physics"; "Renderer"; "Triggered"; "Attachment"; "Body"; "Collider" ]
    for state in [ typeof<ActorState>; typeof<PropState>; typeof<TriggerState> ] do
        for member' in state.GetMembers() do
            Assert.False(List.contains member'.Name forbidden, sprintf "%s exposes %s" state.Name member'.Name)

[<Fact>]
let ``a captured prop carries its authored fields and no body`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = propAssets ()
    let ref = Zone.spawnProp zone world assets (entity "crate") "crate"

    // Live, the prop has a body. What a command records is the authored value, a type that
    // cannot hold one: the body and renderer are the restore's to rebuild.
    Assert.True(Option.isSome (zone.Props.Get(handleOf ref)).Physics)
    let captured = Zone.captureProp zone ref
    Assert.Equal(Some "crate", captured.AssetId)
    Assert.Equal("crate", captured.Base.Name)

[<Fact>]
let ``a captured trigger carries its shape and never its runtime flag`` () =
    let zone = Zone.create ()
    let ref = Zone.spawnTrigger zone (entity "area")
    let payload = Zone.captureTrigger zone ref
    do Zone.setTrigger zone ref { payload with Radius = 4.0f; Prompt = "Open"; Interactable = true }
    // The flag is the trigger system's; nothing authors it through a command, so it is set
    // here straight on the slot.
    let slot = &zone.Triggers.Get(handleOf ref)
    slot.Triggered <- true

    let captured = Zone.captureTrigger zone ref

    Assert.Equal(4.0f, captured.Radius)
    Assert.Equal("Open", captured.Prompt)
    Assert.True(captured.Interactable)
    // Recording reads the authored value, so the runtime flag is left exactly where it was.
    Assert.True(slot.Triggered)

[<Fact>]
let ``a captured waypoint carries its link`` () =
    let zone = Zone.create ()
    let a = Zone.spawnWaypoint zone (entity "a")
    let b = Zone.spawnWaypoint zone (entity "b")
    do Zone.setWaypointNext zone b (Some a)

    // The link is authored state, so the record carries it - and a fresh waypoint's
    // absence of one is carried just as faithfully.
    Assert.Equal(Some a, (Zone.captureWaypoint zone b).Next)
    Assert.True(Option.isNone (Zone.captureWaypoint zone a).Next)

[<Fact>]
let ``delete and undo restore a prop exactly`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = propAssets ()
    let ref = Zone.spawnProp zone world assets (entity "crate") "crate"
    let live = (zone.Props.Get(handleOf ref)).State
    let recorded = Zone.captureProp zone ref

    // Recording is a read: what a command carries is the live authored value, whole.
    Assert.Equal(live, recorded)

    let cmd = PropDelete (ref, recorded)

    do apply zone world assets cmd
    Assert.False(Zone.isAlive zone ref)

    let stack = UndoStack.undo (UndoStack.push (UndoStack.create ()) cmd) zone world assets
    Assert.True(Zone.isAlive zone ref)
    Assert.Equal(live, (zone.Props.Get(handleOf ref)).State)
    Assert.Equal("crate", (Zone.tryEntityAt zone ref).Value.Name)
    Assert.Equal(Some "crate", (zone.Props.Get(handleOf ref)).State.AssetId)
    // The body is rebuilt rather than reused: the recorded one was destroyed.
    Assert.True(Option.isSome (zone.Props.Get(handleOf ref)).Physics)

    let stack = UndoStack.redo stack zone world assets
    Assert.False(Zone.isAlive zone ref)
    ignore stack

[<Fact>]
let ``delete and undo restore an environment part exactly`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = roomAssets ()
    let environment = Zone.loadEnvironment zone world assets "room"
    let ref = partRef environment.Epoch 0u 0u
    let live = (environment.Parts.Get(handleOf ref)).State
    let recorded = Zone.capturePart zone ref
    Assert.Equal(live, recorded)

    let cmd = PartDelete (ref, recorded)

    do apply zone world assets cmd
    Assert.False(Zone.isAlive zone ref)

    let stack = UndoStack.undo (UndoStack.push (UndoStack.create ()) cmd) zone world assets
    Assert.True(Zone.isAlive zone ref)
    Assert.Equal(live, (environment.Parts.Get(handleOf ref)).State)
    Assert.Equal("wall", (Zone.tryEntityAt zone ref).Value.Name)
    Assert.True(Option.isSome (environment.Parts.Get(handleOf ref)).Physics)
    ignore stack

[<Fact>]
let ``delete and undo restore a trigger exactly, without its runtime flag`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = propAssets ()
    let ref = Zone.spawnTrigger zone (entity "area")
    do Zone.setTrigger zone ref { (Zone.captureTrigger zone ref) with Radius = 2.5f; Height = 3.0f; Interactable = true; Prompt = "Open" }
    let live = (zone.Triggers.Get(handleOf ref)).State
    let recorded = Zone.captureTrigger zone ref
    Assert.Equal(live, recorded)

    let cmd = TriggerDelete (ref, recorded)

    do apply zone world assets cmd
    Assert.False(Zone.isAlive zone ref)

    let stack = UndoStack.undo (UndoStack.push (UndoStack.create ()) cmd) zone world assets
    Assert.True(Zone.isAlive zone ref)
    Assert.Equal(live, (zone.Triggers.Get(handleOf ref)).State)
    // The runtime flag is the trigger system's to recompute, never a restore's to put back.
    Assert.False((zone.Triggers.Get(handleOf ref)).Triggered)
    ignore stack

[<Fact>]
let ``repeated delete and undo cycles leave no body behind`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = propAssets ()
    let ref = Zone.spawnProp zone world assets (entity "crate") "crate"
    let baseline = zone.BodyToEntity.Count

    for _ in 1 .. 50 do
        let cmd = PropDelete (ref, Zone.captureProp zone ref)
        do apply zone world assets cmd
        // Undo rebuilds the body. A leaked one would still be attached to the world and
        // would keep its entry in the body map.
        UndoStack.undo (UndoStack.push (UndoStack.create ()) cmd) zone world assets |> ignore

    Assert.Equal(baseline, zone.BodyToEntity.Count)
    Assert.Equal(1u, zone.Props.Count)
    Assert.True(Zone.isAlive zone ref)

// ── A registered zone, loaded back (spec 034) ──
//
// The record of an environment is taken from the live parts map and loaded through the same path an
// asset takes, so these assert the claim the registry rests on: what a registration records is enough
// to put the zone back.

// A three-part hall, so a record can be taken after one part has moved, one has been renamed, one has
// been deleted and one has been duplicated — the arrangements a registration exists to keep.
let private hallAssets () : Assets.AssetManager =
    let submesh = loadedSubmesh [| 0.0f; 0.0f; 0.0f;  1.0f; 0.0f; 0.0f;  0.0f; 1.0f; 0.0f |] [| 0; 1; 2 |]
    let partAt (name: string) (index: int) (x: float32) : EnvironmentPart =
        { TestSupport.part name index 1 with World = Matrix4x4.CreateTranslation(Vector3(x, 0.0f, 0.0f)) }
    headlessAssets
        [ "hall",
          residentAsset "hall" [| submesh; submesh; submesh |] [| partAt "north" 0 0.0f; partAt "east" 1 1.0f; partAt "south" 2 2.0f |] ]

[<Fact>]
let ``a registered zone loads back part for part, deletions and duplicates included`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = hallAssets ()

    // The zone as an author leaves it.
    let loaded = Zone.loadEnvironment zone world assets "hall"
    Assert.Equal(3u, loaded.Parts.Count)
    let north = partRef loaded.Epoch 0u 0u
    let east = partRef loaded.Epoch 1u 0u
    let south = partRef loaded.Epoch 2u 0u
    do Zone.setTransform zone world north { transformIdentity with Position = Vector3(5.0f, 0.0f, 0.0f) }
    do Zone.setName zone east "east wing"
    do apply zone world assets (PartDelete (south, Zone.capturePart zone south))

    // And one duplicated, through the real part path — what `Spawning.duplicate` does for a part. The
    // copy is a live part of its own, so the record has to carry it and its source rather than
    // collapsing the two.
    let source = (loaded.Parts.Get(handleOf north)).State
    let binding : AssetBinding = { AssetId = source.AssetId; SubmeshIndex = source.SubmeshIndex; SubmeshCount = source.SubmeshCount }
    let copied = { entity "north copy" with Transform = { transformIdentity with Position = Vector3(9.0f, 0.0f, 0.0f) } }
    do Zone.spawnPartBinding zone world assets copied binding |> ignore
    Assert.Equal(3u, loaded.Parts.Count)

    // The record of that arrangement, taken from the live zone: the deleted part is simply not in it,
    // and the duplicate is in it as a part of its own.
    let template = (Zone.captureTemplate zone).Value
    Assert.Equal("hall", template.AssetId)
    Assert.Equal("hall", template.Base.Name)
    Assert.Equal(3, template.Parts.Length)

    // Loaded into a fresh zone it comes back exactly — the asset, the settings, and the parts in the
    // state they were left in.
    let fresh = Zone.create ()
    let reinstated = Zone.loadEnvironmentFrom fresh world assets template
    Assert.Equal(template.AssetId, reinstated.AssetId)
    Assert.Equal(template.Base, reinstated.Base)
    Assert.Equal(template.Orientation, reinstated.Orientation)
    Assert.Equal(template, (Zone.captureTemplate fresh).Value)
    // Order is not what the record is for, so the names are compared sorted: what matters is that the
    // moved, the renamed and the duplicated part are all there, and the deleted one is not.
    Assert.Equal<string list>(
        [ "east wing"; "north"; "north copy" ],
        template.Parts |> Array.map (fun (part: PropState) -> part.Base.Name) |> Array.toList |> List.sort)
    let northPart : PropState = template.Parts.[0]
    Assert.Equal(Vector3(5.0f, 0.0f, 0.0f), northPart.Base.Transform.Position)
    // The copy draws the same piece of the asset as its source, at the transform it was given.
    let copy : PropState = Array.find (fun (part: PropState) -> part.Base.Name = "north copy") template.Parts
    Assert.Equal(Vector3(9.0f, 0.0f, 0.0f), copy.Base.Transform.Position)
    Assert.Equal(northPart.SubmeshIndex, copy.SubmeshIndex)
    Assert.Equal(northPart.SubmeshCount, copy.SubmeshCount)
    // The deleted part is gone rather than reinstated from the asset it came from.
    Assert.DoesNotContain("south", template.Parts |> Array.map (fun part -> part.Base.Name))

    // And what came back is a live environment, not just a record: its parts resolve and carry bodies,
    // which is what the activation path attaches.
    Assert.True(Option.isSome fresh.Environment)
    Assert.Equal(3u, reinstated.Parts.Count)
    for i in 0 .. template.Parts.Length - 1 do
        let handle : Handle = { Index = uint32 i; Generation = 0u }
        Assert.True((reinstated.Parts.Get handle).Physics.IsSome)
        Assert.True(Zone.isAlive fresh (partRef reinstated.Epoch handle.Index handle.Generation))

// ── What a record takes from a zone's props (spec 035) ──

// The hall's environments with the crate's props: what a test that places props in a registered zone
// needs, composed from the two fixtures already here rather than a third copy of their shape.
let private hallAndCrateAssets () : Assets.AssetManager =
    { hallAssets () with Props = (propAssets ()).Props }

[<Fact>]
let ``a record carries the zone's props as the author left them`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = hallAndCrateAssets ()
    do Zone.loadEnvironment zone world assets "hall" |> ignore

    // Four props and the arrangements the spec names: one moved, one renamed, one deleted, one
    // duplicated — the duplicate through the real prop path, which is what `Spawning.duplicate` makes.
    let lectern = Zone.spawnProp zone world assets (entity "lectern") "crate"
    let books = Zone.spawnProp zone world assets (entity "books") "crate"
    let chair = Zone.spawnProp zone world assets (entity "chair") "crate"
    do Zone.setTransform zone world lectern { transformIdentity with Position = Vector3(1.0f, 2.0f, 3.0f) }
    do Zone.setName zone books "book stack"
    do apply zone world assets (PropDelete (chair, Zone.captureProp zone chair))
    do Zone.spawnPropBinding zone world assets (entity "lectern copy") { AssetId = Some "crate"; SubmeshIndex = 0; SubmeshCount = 0 } |> ignore

    let template = (Zone.captureTemplate zone).Value
    Assert.Equal(3, template.Props.Length)
    Assert.Equal<string list>(
        [ "book stack"; "lectern"; "lectern copy" ],
        template.Props |> Array.map (fun (prop: PropState) -> prop.Base.Name) |> Array.toList |> List.sort)
    let carried : PropState = Array.find (fun (prop: PropState) -> prop.Base.Name = "lectern") template.Props
    Assert.Equal(Vector3(1.0f, 2.0f, 3.0f), carried.Base.Transform.Position)
    // The deleted one is gone rather than being taken from the asset it was placed from.
    Assert.DoesNotContain("chair", template.Props |> Array.map (fun (prop: PropState) -> prop.Base.Name))

    // A prop names the asset it stands from and a part names a piece of the environment, which is the
    // whole of what tells them apart and what stops one being drawn as the other.
    for prop in template.Props do
        Assert.True(prop.AssetId.IsSome, sprintf "prop '%s' names no asset" prop.Base.Name)
    for part in template.Parts do
        Assert.True(part.AssetId.IsNone)

[<Fact>]
let ``capturing a zone is a copy, and a zone with no props is a complete entry`` () =
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = hallAndCrateAssets ()
    do Zone.loadEnvironment zone world assets "hall" |> ignore
    let crate = Zone.spawnProp zone world assets (entity "crate") "crate"

    let taken = (Zone.captureTemplate zone).Value
    // Taking a record does not disturb the zone...
    Assert.Equal(taken, (Zone.captureTemplate zone).Value)

    // ...and the zone moving on afterwards does not reach it.
    do Zone.setName zone crate "crate moved on"
    do apply zone world assets (PropDelete (crate, Zone.captureProp zone crate))
    Assert.Equal(1, taken.Props.Length)
    Assert.Equal("crate", taken.Props.[0].Base.Name)

    // No props at all is a complete entry rather than a partial one, and it is the state every entry
    // registered before props were recorded is in.
    let bare = Zone.create ()
    do Zone.loadEnvironment bare world assets "hall" |> ignore
    Assert.Empty((Zone.captureTemplate bare).Value.Props)

[<Fact>]
let ``the zone's light is set by the command and put back by its undo`` () =
    // The light is zone-level, so the command touches no entity and needs no asset but the argument
    // `apply` takes (spec 043 FR-006).
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = headlessAssets []

    let before = zone.Light
    let after = { before with Pitch = 20.0f; Yaw = 200.0f; Color = Vector3(0.9f, 0.8f, 0.7f); Ambient = 0.9f }
    let cmd = SetLight (before, after)

    do apply zone world assets cmd
    Assert.Equal(after, zone.Light)

    let stack = UndoStack.undo (UndoStack.push (UndoStack.create ()) cmd) zone world assets
    Assert.Equal(before, zone.Light)

    let stack = UndoStack.redo stack zone world assets
    Assert.Equal(after, zone.Light)
    ignore stack

[<Fact>]
let ``the player designation is set by the command and put back by its undo`` () =
    // The designation is zone-level and holds a handle into the zone's own actors, so it needs a zone
    // with actors but no assets beyond the argument `apply` takes (spec 045 FR-001 to FR-003).
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = headlessAssets []

    let b = zone.Actors.Add(actor "b")
    let c = zone.Actors.Add(actor "c")

    // A first designation undoes to no player at all — `ValueNone` is a real state, not a hole.
    let cmd = SetPlayer (ValueNone, ValueSome b)
    do apply zone world assets cmd
    Assert.Equal(ValueSome b, zone.Player)

    let stack = UndoStack.undo (UndoStack.push (UndoStack.create ()) cmd) zone world assets
    Assert.True(ValueOption.isNone zone.Player)

    let stack = UndoStack.redo stack zone world assets
    Assert.Equal(ValueSome b, zone.Player)

    // A re-designation puts the *previous actor* back, which is what the payload's `Previous` carries.
    let recmd = SetPlayer (ValueSome b, ValueSome c)
    do apply zone world assets recmd
    Assert.Equal(ValueSome c, zone.Player)

    let stack = UndoStack.undo (UndoStack.push stack recmd) zone world assets
    Assert.Equal(ValueSome b, zone.Player)
    ignore stack

[<Fact>]
let ``a registered zone records its light`` () =
    // What an entry carries is the live authored value, whole — the zone's light is part of the
    // record a registration is (spec 043 FR-007).
    let zone = Zone.create ()
    let world = Physics.create ()
    let assets = roomAssets ()
    do Zone.loadEnvironment zone world assets "room" |> ignore

    let authored = { Texture.defaultLight with Pitch = 20.0f; Yaw = 200.0f; Ambient = 0.9f }
    zone.Light <- authored
    Assert.Equal(authored, (Zone.captureTemplate zone).Value.Light)
