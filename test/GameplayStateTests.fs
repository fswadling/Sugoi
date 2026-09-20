module GameplayStateTests

// Zone container behaviour: add/select/delete, name validation, physics
// detach/attach across a switch, per-zone undo, and per-zone player designation.
// Headless: the prop/part render paths never touch the GPU device, so a hand-built
// asset manager drives the real `Zone`/`GameplayState` code without a GPU.

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

let private entity (name: string) : Entity =
    { Name = name
      Transform = transformIdentity
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

// ── Headless fixtures ──

let private bounds (mn: Vector3) (mx: Vector3) : Gltf.Bounds =
    { Min = mn; Max = mx }

let private assetSubmesh (positions: float32 array) (indices: int array) : StaticSubmesh =
    { Mesh = Unchecked.defaultof<StaticMesh.StaticMesh>
      Material = Unchecked.defaultof<Texture.Material>
      Positions = positions
      Indices = indices }

let private envAsset (id: string) (submeshes: StaticSubmesh array) : EnvironmentAsset.EnvironmentAsset =
    { Id = id
      Name = id
      ModelPath = ""
      Orientation = Quaternion.Identity
      Bounds = bounds Vector3.Zero Vector3.One
      Submeshes = submeshes
      Parts = [||] }

// A device-free manager carrying one whole-model prop asset (`crate`).
let private propAssets () : Assets.AssetManager =
    let submeshes = [| assetSubmesh [| 0.0f; 0.0f; 0.0f;  1.0f; 0.0f; 0.0f;  0.0f; 1.0f; 0.0f |] [| 0; 1; 2 |] |]
    let props = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    props.["crate"] <- envAsset "crate" submeshes
    { Device = IntPtr.Zero
      Actors = Dictionary()
      Props = props
      Environments = Dictionary() }

let private sharedState (assets: Assets.AssetManager) : SharedState.SharedState =
    { Gpu = Unchecked.defaultof<Gpu.Gpu>; Assets = assets; Cast = { Casting.Bindings = Dictionary() }; Physics = Physics.create () }

// A gameplay state seeded with the single `main` zone, as the app starts.
let private newState () : GameplayState.GameplayState =
    GameplayState.create (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<DebugDraw.DebugDraw>)

let private actor (name: string) : Actor =
    { State =
        { Base = entity name
          AssetId = ""
          ShowCollider = false }
      Physics = None
      Renderer = Unchecked.defaultof<ActorRender.ActorRender> }

// A device-free manager carrying one environment (`room`) with a single part.
let private roomAssets () : Assets.AssetManager =
    let submeshes = [| assetSubmesh [| 0.0f; 0.0f; 0.0f;  1.0f; 0.0f; 0.0f;  0.0f; 1.0f; 0.0f |] [| 0; 1; 2 |] |]
    let part : EnvironmentPart =
        { Name = "wall"
          HierarchyPath = "wall"
          World = Matrix4x4.Identity
          Bounds = bounds Vector3.Zero Vector3.One
          SubmeshIndex = 0
          SubmeshCount = 1 }
    let envs = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    envs.["room"] <- { (envAsset "room" submeshes) with Parts = [| part |] }
    { Device = IntPtr.Zero; Actors = Dictionary(); Props = Dictionary(); Environments = envs }

let private bodyOf (zone: Zone.Zone) (ref: EntityRef) : BodyId =
    (zone.Props.Get(handleOf ref)).Physics.Value.Body

// ── Add / select ──

[<Fact>]
let ``addZone inserts an empty zone, makes it active, and rejects bad names`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    Assert.Equal("main", state.ActiveZone)

    GameplayState.addZone shared &state "classroom"
    Assert.True(state.Zones.ContainsKey "classroom")
    Assert.Equal("classroom", state.ActiveZone)
    Assert.Equal(0u, state.Zones.["classroom"].Actors.Count)

    let mutable duplicateRaised = false
    try GameplayState.addZone shared &state "classroom" with _ -> duplicateRaised <- true
    let mutable emptyRaised = false
    try GameplayState.addZone shared &state "" with _ -> emptyRaised <- true
    Assert.True duplicateRaised
    Assert.True emptyRaised
    Assert.Equal(2, state.Zones.Count)

[<Fact>]
let ``switchZone is a no-op for the active zone and raises for an unknown name`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    GameplayState.addZone shared &state "classroom"

    GameplayState.switchZone shared &state "classroom"
    Assert.Equal("classroom", state.ActiveZone)

    let mutable raised = false
    try GameplayState.switchZone shared &state "nope" with _ -> raised <- true
    Assert.True raised

// ── Physics isolation ──

[<Fact>]
let ``a switch detaches the outgoing zone's bodies and re-attaches them on return`` () =
    let assets = propAssets ()
    let shared = sharedState assets
    let mutable state = newState ()
    let main = GameplayState.activeZone state
    let ref = Zone.spawnProp main shared.Physics assets (entity "crate") "crate"
    let body = bodyOf main ref
    Assert.True(Physics.isAdded shared.Physics body)

    GameplayState.addZone shared &state "classroom"
    Assert.False(Physics.isAdded shared.Physics body)
    // The entity and its body survive the detach.
    Assert.True(Zone.isAlive main ref)

    GameplayState.switchZone shared &state "main"
    Assert.True(Physics.isAdded shared.Physics body)

[<Fact>]
let ``loading an environment changes only the active zone`` () =
    let assets = roomAssets ()
    let shared = sharedState assets
    let mutable state = newState ()
    GameplayState.addZone shared &state "classroom"

    let classroom = GameplayState.activeZone state
    let environment = Zone.loadEnvironment classroom shared.Physics assets "room"
    Assert.True(Option.isSome classroom.Environment)
    Assert.Equal(environment.Epoch, classroom.Environment.Value.Epoch)

    let main = state.Zones.["main"]
    Assert.True(Option.isNone main.Environment)
    GameplayState.switchZone shared &state "main"
    Assert.True(Option.isSome classroom.Environment)

// ── Delete ──

[<Fact>]
let ``the last zone cannot be deleted`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    let mutable raised = false
    try GameplayState.deleteZone shared &state "main" with _ -> raised <- true
    Assert.True raised
    Assert.True(state.Zones.ContainsKey "main")

[<Fact>]
let ``deleting an inactive zone releases it and leaves the active zone alone`` () =
    let assets = propAssets ()
    let shared = sharedState assets
    let mutable state = newState ()
    GameplayState.addZone shared &state "classroom"
    let classroom = GameplayState.activeZone state
    let ref = Zone.spawnProp classroom shared.Physics assets (entity "crate") "crate"
    let body = bodyOf classroom ref

    GameplayState.switchZone shared &state "main"
    Assert.False(Physics.isAdded shared.Physics body)

    GameplayState.deleteZone shared &state "classroom"
    Assert.False(state.Zones.ContainsKey "classroom")
    Assert.Equal("main", state.ActiveZone)
    Assert.False(state.UndoByZone.ContainsKey "classroom")
    Assert.Equal(0, classroom.BodyToEntity.Count)

[<Fact>]
let ``deleting the active zone makes another zone active`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    GameplayState.addZone shared &state "classroom"
    GameplayState.deleteZone shared &state "classroom"
    Assert.Equal("main", state.ActiveZone)
    Assert.False(state.Zones.ContainsKey "classroom")

// ── Per-zone undo ──

[<Fact>]
let ``switching zones swaps the per-zone undo history`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    GameplayState.addZone shared &state "classroom"

    let cmd = Command.Transform (actorRef 0u 0u, transformIdentity, { transformIdentity with Position = Vector3.One })
    state.Editor <- { state.Editor with Undo = UndoStack.push state.Editor.Undo cmd }
    Assert.False(List.isEmpty state.Editor.Undo.Applied)

    GameplayState.switchZone shared &state "main"
    Assert.True(List.isEmpty state.Editor.Undo.Applied)

    GameplayState.switchZone shared &state "classroom"
    Assert.False(List.isEmpty state.Editor.Undo.Applied)

// ── Per-zone player designation ──

[<Fact>]
let ``a player designation is per zone and never aliases a same-index actor elsewhere`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    let main = GameplayState.activeZone state
    let mainActor = main.Actors.Add(actor "player")
    main.Player <- ValueSome mainActor
    Assert.True(Systems.isPlayerLive main)

    GameplayState.addZone shared &state "classroom"
    let classroom = GameplayState.activeZone state
    // Same slot index and generation as `main`'s actor, but no designation here.
    let classroomActor = classroom.Actors.Add(actor "other")
    Assert.Equal(mainActor.Index, classroomActor.Index)
    Assert.False(Systems.isPlayerLive classroom)
    Assert.True(Systems.isPlayerLive main)

    classroom.Player <- ValueSome classroomActor
    Assert.True(Systems.isPlayerLive classroom)

[<Fact>]
let ``switching re-frames the camera on the incoming zone`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()

    // A designated player in `classroom` at a known position.
    GameplayState.addZone shared &state "classroom"
    let classroom = GameplayState.activeZone state
    let handle = classroom.Actors.Add(actor "player")
    let a = &classroom.Actors.Get handle
    a <- { a with State.Base.Transform = { a.State.Base.Transform with Position = Vector3(8.0f, 0.0f, 8.0f) } }
    classroom.Player <- ValueSome handle

    // Switch away and move the camera, then switch back: the camera pivots on the
    // incoming zone's designated player (feet + follow height).
    GameplayState.switchZone shared &state "main"
    state.Camera <- { state.Camera with Target = Vector3(2.0f, 2.0f, 2.0f) }
    GameplayState.switchZone shared &state "classroom"

    Assert.Equal(Vector3(8.0f, state.Follow.Height, 8.0f), state.Camera.Target)
