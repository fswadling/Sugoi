module ActorPhysicsTests

open Xunit
open System
open System.Numerics
open Command
open Cue
open Entity
open Physics
open Raycast
open ScenarioRegistry
open TestSupport

// ── fixtures ──

let private actorShared () : SharedState.SharedState =
    let anim (name: string) : Iqm.Anim =
        { Name = name; FirstFrame = 0; NumFrames = 2; FrameRate = 24.0f; Loop = true }
    let model = { emptyModel () with Anims = [| anim "Idle"; anim "Jog" |] }
    let asset = { actorAsset "aiko" with Model = Actor.createModel model }
    headlessSharedWith (managerWith [ "aiko", asset ] [] [])

let private spawnAt (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (physics: ActorPhysics) (position: Vector3) : EntityRef =
    let zone = GameplayState.activeZone state
    Zone.spawnActor zone state.Physics shared.Assets
        { Base = { entity "aiko" with Transform = { transformIdentity with Position = position } }
          AssetId = "aiko"
          ShowCollider = false
          Physics = physics }

let private bodyOf (zone: Zone.Zone) (ref: EntityRef) : BodyId =
    (zone.Actors.Get(handleOf ref)).Physics.Value.Body

let private feetOf (zone: Zone.Zone) (ref: EntityRef) : Vector3 =
    (zone.Actors.Get(handleOf ref)).State.Base.Transform.Position

let private playerSettings : CapsuleSettings =
    { Motion = Physics.dynamicMotion
      Friction = 0.5f
      Restitution = 0.0f
      Mass = 75.0f
      GravityFactor = 0.0f
      AllowSleeping = false }

let private noPhysics = { kinematicPhysics with Mobility = NoPhysics }

// ── modes ──

[<Fact>]
let ``a newly placed actor defaults to kinematic`` () =
    let shared = actorShared ()
    let mutable state = headlessState ()
    Spawning.spawnActorAt shared &state (entity "aiko") "aiko" EditorState.Origin
    let zone = GameplayState.activeZone state
    match Zone.tryActorByName zone "aiko" with
    | Some ref ->
        let actor = zone.Actors.Get(handleOf ref)
        Assert.Equal(Kinematic, actor.State.Physics.Mobility)
        Assert.Equal(1.0f, actor.State.Physics.GravityFactor)
        Assert.True(actor.Physics.IsSome)
    | None -> failwith "expected the placed actor"

[<Fact>]
let ``a kinematic actor is not displaced by a dynamic body and blocks it`` () =
    let shared = actorShared ()
    let mutable state = headlessState ()
    let zone = GameplayState.activeZone state
    let ref = spawnAt shared &state kinematicPhysics Vector3.Zero
    let actorBody = bodyOf zone ref
    let struct (before, _) = Physics.getBodyTransform state.Physics actorBody

    let player = Physics.createCapsuleBody state.Physics (Vector3(1.5f, 0.5f, 0.0f)) Quaternion.Identity 0.15f 0.35f playerSettings
    do Physics.setBodyVelocity state.Physics player (Vector3(-5.0f, 0.0f, 0.0f))
    for _ in 1 .. 120 do Physics.step state.Physics (1.0f / 60.0f)

    let struct (after, afterRotation) = Physics.getBodyTransform state.Physics actorBody
    assertVec3 0.01f before after
    Assert.True(abs afterRotation.X + abs afterRotation.Y + abs afterRotation.Z < 1e-3f, "expected the actor to stay upright")

    let struct (playerPosition, _) = Physics.getBodyTransform state.Physics player
    Assert.True(playerPosition.X < 1.0f, sprintf "expected the player to be blocked, got %A" playerPosition)

[<Fact>]
let ``a NoPhysics actor has no body and does not block`` () =
    let shared = actorShared ()
    let mutable state = headlessState ()
    let zone = GameplayState.activeZone state
    let ref = spawnAt shared &state noPhysics Vector3.Zero
    Assert.True((zone.Actors.Get(handleOf ref)).Physics.IsNone)

    let player = Physics.createCapsuleBody state.Physics (Vector3(0.5f, 0.5f, 0.0f)) Quaternion.Identity 0.15f 0.35f playerSettings
    let struct (before, _) = Physics.getBodyTransform state.Physics player
    for _ in 1 .. 120 do Physics.step state.Physics (1.0f / 60.0f)
    let struct (after, _) = Physics.getBodyTransform state.Physics player
    assertVec3 0.01f before after

[<Fact>]
let ``a NoPhysics actor is still picked in the viewport`` () =
    let shared = actorShared ()
    let mutable state = headlessState ()
    let ref = spawnAt shared &state noPhysics Vector3.Zero
    let ray : Ray = { Origin = Vector3(0.0f, 0.5f, 5.0f); Dir = -Vector3.UnitZ }
    Assert.Equal(Some ref, Viewport.pick shared.Assets state.Physics state ray)

// ── properties ──

[<Fact>]
let ``gravity factor moves a dynamic actor and leaves a zero-gravity one alone`` () =
    let shared = actorShared ()
    let mutable falling = headlessState ()
    let fallingZone = GameplayState.activeZone falling
    let fallingRef = spawnAt shared &falling { dynamicPhysics with GravityFactor = 1.0f } Vector3.Zero
    let fallingBody = bodyOf fallingZone fallingRef
    for _ in 1 .. 60 do Physics.step falling.Physics (1.0f / 60.0f)
    let struct (fallingPosition, _) = Physics.getBodyTransform falling.Physics fallingBody
    Assert.True(fallingPosition.Y < 0.49f, sprintf "expected the actor to fall, got %A" fallingPosition)

    let mutable floating = headlessState ()
    let floatingZone = GameplayState.activeZone floating
    let floatingRef = spawnAt shared &floating { dynamicPhysics with GravityFactor = 0.0f } Vector3.Zero
    let floatingBody = bodyOf floatingZone floatingRef
    for _ in 1 .. 60 do Physics.step floating.Physics (1.0f / 60.0f)
    let struct (floatingPosition, _) = Physics.getBodyTransform floating.Physics floatingBody
    assertVec3 0.01f (Vector3(0.0f, 0.5f, 0.0f)) floatingPosition

[<Fact>]
let ``a heavier dynamic actor is displaced less by the same collision`` () =
    let displacement (mass: float32) : float32 =
        let shared = actorShared ()
        let mutable state = headlessState ()
        let zone = GameplayState.activeZone state
        let ref = spawnAt shared &state { dynamicPhysics with Mass = mass; GravityFactor = 0.0f } Vector3.Zero
        let actorBody = bodyOf zone ref
        let player = Physics.createCapsuleBody state.Physics (Vector3(1.5f, 0.5f, 0.0f)) Quaternion.Identity 0.15f 0.35f playerSettings
        do Physics.setBodyVelocity state.Physics player (Vector3(-5.0f, 0.0f, 0.0f))
        for _ in 1 .. 120 do Physics.step state.Physics (1.0f / 60.0f)
        let struct (position, _) = Physics.getBodyTransform state.Physics actorBody
        (position - Vector3(0.0f, 0.5f, 0.0f)).Length()

    Assert.True(displacement 10.0f > displacement 500.0f, "expected the light actor to be displaced further")

[<Fact>]
let ``the authored contact properties reach the body`` () =
    let shared = actorShared ()
    let mutable state = headlessState ()
    let zone = GameplayState.activeZone state
    let ref = spawnAt shared &state { dynamicPhysics with Friction = 1.5f; Restitution = 0.8f } Vector3.Zero
    let body = bodyOf zone ref
    match body with
    | BodyId id ->
        let mutable bodyId = JoltPhysicsSharp.BodyID(id)
        Assert.Equal(1.5f, state.Physics.System.BodyInterface.GetFriction(&bodyId))
        Assert.Equal(0.8f, state.Physics.System.BodyInterface.GetRestitution(&bodyId))

[<Fact>]
let ``a physics edit is one undoable step`` () =
    let shared = actorShared ()
    let mutable state = headlessState ()
    let zone = GameplayState.activeZone state
    let ref = spawnAt shared &state kinematicPhysics Vector3.Zero

    Selection.updateActorPhysicsRef shared &state ref (fun p -> { p with Mass = 123.0f })
    Assert.Single(state.Editor.Undo.Applied) |> ignore
    Assert.Equal(123.0f, (zone.Actors.Get(handleOf ref)).State.Physics.Mass)

    Undo.undoHistory shared &state
    Assert.Equal(kinematicPhysics.Mass, (zone.Actors.Get(handleOf ref)).State.Physics.Mass)

// ── cues and the player ──

[<Fact>]
let ``a kinematic move stops at a wall`` () =
    let shared = actorShared ()
    let mutable state = headlessState ()
    let zone = GameplayState.activeZone state
    let ref = spawnAt shared &state kinematicPhysics Vector3.Zero

    let wallVertices =
        [| Vector3(0.0f, -2.0f, -2.0f); Vector3(0.0f, -2.0f, 2.0f)
           Vector3(0.0f, 2.0f, 2.0f); Vector3(0.0f, 2.0f, -2.0f) |]
    let wallIndices = [| 0; 1; 2;  0; 2; 3 |]
    ignore (Physics.createMeshBody state.Physics (Vector3(2.0f, 0.0f, 0.0f)) Quaternion.Identity wallVertices wallIndices)
    ignore (Zone.spawnWaypoint zone { entity "far" with Transform = { transformIdentity with Position = Vector3(4.0f, 0.0f, 0.0f) } })

    state.Cue <- MoveActor("aiko", "far", 2.0, "Jog", "Idle")
    CueEngine.tick shared &state 0.0
    let mutable now = 0.0
    while not state.Cue.IsFin && now < 5.0 do
        Physics.step state.Physics 0.02f
        now <- now + 0.02
        CueEngine.tick shared &state now

    Assert.True(state.Cue.IsFin, "expected the move to finish")
    do Systems.syncActors state.Physics zone
    let feet = feetOf zone ref
    Assert.True(feet.X > 1.0f, sprintf "expected her to move, got %A" feet)
    Assert.True(feet.X < 2.0f, sprintf "expected the wall to stop her, got %A" feet)

[<Fact>]
let ``a move cue on a NoPhysics actor performs nothing`` () =
    let shared = actorShared ()
    let mutable state = headlessState ()
    let zone = GameplayState.activeZone state
    let ref = spawnAt shared &state noPhysics Vector3.Zero
    ignore (Zone.spawnWaypoint zone { entity "far" with Transform = { transformIdentity with Position = Vector3(4.0f, 0.0f, 0.0f) } })

    state.Cue <- MoveActor("aiko", "far", 2.0, "Jog", "Idle")
    CueEngine.tick shared &state 0.0
    Assert.True(state.Cue.IsFin)
    assertVec3 1e-4f Vector3.Zero (feetOf zone ref)

[<Fact>]
let ``EnablePlayer promotes a kinematic actor to dynamic`` () =
    let shared = actorShared ()
    let mutable state = headlessState ()
    let zone = GameplayState.activeZone state
    let ref = spawnAt shared &state kinematicPhysics Vector3.Zero

    state.Cue <- EnablePlayer(GameplayState.startZone, "aiko")
    CueEngine.tick shared &state 0.0

    Assert.Equal(Dynamic, (zone.Actors.Get(handleOf ref)).State.Physics.Mobility)
    Assert.Equal(ValueSome (Systems.playerHandleOf ref), zone.Player)

[<Fact>]
let ``AddActor creates a kinematic actor`` () =
    let shared = actorShared ()
    let mutable state = headlessState ()
    let zone = GameplayState.activeZone state
    ignore (Zone.spawnWaypoint zone { entity "spawn" with Transform = transformIdentity })

    state.Cue <- AddActor(GameplayState.startZone, "New", "aiko", "spawn")
    CueEngine.tick shared &state 0.0

    match Zone.tryActorByName zone "New" with
    | Some ref -> Assert.Equal(Kinematic, (zone.Actors.Get(handleOf ref)).State.Physics.Mobility)
    | None -> failwith "expected the added actor"

[<Fact>]
let ``a restored zone promotes a kinematic designated player`` () =
    let shared = actorShared ()
    let zone = Zone.create ()
    let ref = actorRef 0u 0u
    let handle : SlotMap.Handle = { Index = 0u; Generation = 0u }
    let actorState = { Base = entity "npc"; AssetId = "aiko"; ShowCollider = false; Physics = kinematicPhysics }
    let contents : Zone.ZoneContents =
        { Actors = [ ref, actorState ]
          Props = []
          Waypoints = []
          Triggers = []
          Environment = None
          Light = Texture.defaultLight
          Player = ValueSome handle }
    Zone.occupyZone zone (Physics.create ()) shared.Assets contents

    let actor = zone.Actors.Get handle
    Assert.Equal(Dynamic, actor.State.Physics.Mobility)
    Assert.True(actor.Physics.IsSome)

[<Fact>]
let ``SetPlayer undo promotes a restored kinematic player`` () =
    let shared = actorShared ()
    let mutable state = headlessState ()
    let zone = GameplayState.activeZone state
    let previous = spawnAt shared &state noPhysics Vector3.Zero
    let current = spawnAt shared &state kinematicPhysics (Vector3(2.0f, 0.0f, 0.0f))

    zone.Player <- ValueSome (Systems.playerHandleOf current)
    Command.undo zone state.Physics shared.Assets (Command.SetPlayer (GameplayState.startZone, ValueSome (Systems.playerHandleOf previous), ValueSome (Systems.playerHandleOf current)))

    Assert.Equal(ValueSome (Systems.playerHandleOf previous), zone.Player)
    Assert.Equal(Dynamic, (zone.Actors.Get(handleOf previous)).State.Physics.Mobility)

// ── authored paths ──

[<Fact>]
let ``a physics edit on an inactive zone leaves the new body detached`` () =
    let shared = actorShared ()
    let mutable state = headlessState ()
    do GameplayState.addZone shared &state "Hall"
    let hall = state.Zones.["Hall"]
    let ref = Zone.spawnActor hall state.Physics shared.Assets
                  { Base = entity "npc"; AssetId = "aiko"; ShowCollider = false; Physics = kinematicPhysics }
    do GameplayState.switchZone shared &state GameplayState.startZone

    do Undo.apply shared &state (ActorPhysicsSet ("Hall", ref, kinematicPhysics, dynamicPhysics))

    let body = (hall.Actors.Get(handleOf ref)).Physics.Value.Body
    Assert.False(Physics.isAdded state.Physics body)

[<Fact>]
let ``duplicate copies the physics setting`` () =
    let shared = actorShared ()
    let mutable state = headlessState ()
    let zone = GameplayState.activeZone state
    let ref = spawnAt shared &state { dynamicPhysics with Mass = 123.0f; Restitution = 0.8f } Vector3.Zero
    let entity = (Zone.tryEntityAt zone ref).Value

    Spawning.duplicate shared &state ref entity
    let newRef =
        match state.Editor.Undo.Applied with
        | ActorCreate (_, r, _) :: _ -> r
        | _ -> failwith "expected an ActorCreate step"

    Assert.Equal(123.0f, (zone.Actors.Get(handleOf newRef)).State.Physics.Mass)
    Assert.Equal(0.8f, (zone.Actors.Get(handleOf newRef)).State.Physics.Restitution)
    Assert.Equal(Dynamic, (zone.Actors.Get(handleOf newRef)).State.Physics.Mobility)

[<Fact>]
let ``scenario capture and fork preserve the physics setting`` () =
    let shared = actorShared ()
    let mutable sourceState = headlessState ()
    let sourceZone = GameplayState.activeZone sourceState
    ignore (spawnAt shared &sourceState { dynamicPhysics with Mass = 123.0f; Restitution = 0.8f } Vector3.Zero)
    let captured = ScenarioZone.capture sourceZone
    let contents = { Zones = Map.ofList [ GameplayState.startZone, captured ]; Cue = Cue.Fin }

    let mutable targetState = headlessState ()
    ScenarioFork.materialise shared &targetState contents |> ignore
    let targetZone = GameplayState.activeZone targetState
    match Zone.tryActorByName targetZone "aiko" with
    | Some ref ->
        let actor = targetZone.Actors.Get(handleOf ref)
        Assert.Equal(123.0f, actor.State.Physics.Mass)
        Assert.Equal(0.8f, actor.State.Physics.Restitution)
        Assert.Equal(Dynamic, actor.State.Physics.Mobility)
    | None -> failwith "expected the forked actor"

// ── performance ──

[<Fact>]
let ``syncActors allocates nothing across all three modes`` () =
    let shared = actorShared ()
    let mutable state = headlessState ()
    let zone = GameplayState.activeZone state
    ignore (spawnAt shared &state dynamicPhysics Vector3.Zero)
    ignore (spawnAt shared &state kinematicPhysics (Vector3(1.0f, 0.0f, 0.0f)))
    ignore (spawnAt shared &state noPhysics (Vector3(2.0f, 0.0f, 0.0f)))
    assertAllocatesNothing 100 1000 (fun () -> Systems.syncActors state.Physics zone)
