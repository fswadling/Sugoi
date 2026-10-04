module GameplayStateTests

// Zone container behaviour: add/select/delete, name validation, physics
// detach/attach across a switch, the one undo history (spec 048) and the cue's steps on
// it (spec 049), and per-zone player designation. Headless: the prop/part render paths
// never touch the GPU device, so a hand-built asset manager drives the real
// `Zone`/`GameplayState` code without a GPU.

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
open TestSupport
open Place

// The game's places, as these tests name them. `Place.start` is where a state begins; the other two are
// further declared places.
let private mainPlace : Place = Place.start
let private classroomPlace : Place = Place.ArtRoom Period.Evening
let private atriumPlace : Place = Place.Island

let private entity (name: string) : Entity =
    { Name = name
      Transform = transformIdentity
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

// ── Headless fixtures ──

// A device-free manager carrying one whole-model prop asset (`crate`).
let private propAssets () : Assets.AssetManager =
    let submeshes = [| loadedSubmesh [| 0.0f; 0.0f; 0.0f;  1.0f; 0.0f; 0.0f;  0.0f; 1.0f; 0.0f |] [| 0; 1; 2 |] |]
    let props = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    props.["crate"] <- residentAsset "crate" submeshes [||]
    TestSupport.manager IntPtr.Zero (Dictionary()) props (Dictionary())

let private sharedState (assets: Assets.AssetManager) : SharedState.SharedState =
    { Gpu = Unchecked.defaultof<Gpu.Gpu>; Assets = assets; Cast = { Casting.Bindings = Dictionary() }; ZoneRegistry = ZoneRegistry.create (); Advents = Set.empty; Physics = Physics.create () }

// A gameplay state seeded with the single `main` zone, as the app starts.
let private newState () : GameplayState.GameplayState =
    GameplayState.create (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<Ui.Ui>)

let private actor (name: string) : Actor =
    { State =
        { Base = entity name
          AssetId = ""
          ShowCollider = false }
      Physics = None
      Renderer = Unchecked.defaultof<ActorRender.ActorRender> }

// A device-free manager carrying one environment (`room`) with a single part.
let private roomAssets () : Assets.AssetManager =
    let submeshes = [| loadedSubmesh [| 0.0f; 0.0f; 0.0f;  1.0f; 0.0f; 0.0f;  0.0f; 1.0f; 0.0f |] [| 0; 1; 2 |] |]
    let envs = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    envs.["room"] <- residentAsset "room" submeshes [| TestSupport.part "wall" 0 1 |]
    TestSupport.manager IntPtr.Zero (Dictionary()) (Dictionary()) envs

let private bodyOf (zone: Zone.Zone) (ref: EntityRef) : BodyId =
    (zone.Props.Get(handleOf ref)).Physics.Value.Body

// ── Add / select ──

[<Fact>]
let ``addZone makes an absent place present and active, and refuses one already present`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    Assert.Equal(mainPlace, state.ActiveZone)

    GameplayState.addZone shared &state classroomPlace
    Assert.True(state.Zones.ContainsKey classroomPlace)
    Assert.Equal(classroomPlace, state.ActiveZone)
    Assert.Equal(0u, state.Zones.[classroomPlace].Actors.Count)

    let mutable duplicateRaised = false
    try GameplayState.addZone shared &state classroomPlace with _ -> duplicateRaised <- true
    Assert.True duplicateRaised
    Assert.Equal(2, state.Zones.Count)

[<Fact>]
let ``presentZone makes an absent place present with its template and leaves the active place alone`` () =
    let assets = roomAssets ()
    let shared = sharedState assets
    let mutable state = newState ()
    let activeBefore = state.ActiveZone
    let cameraBefore = state.Camera

    let part : PropState = { Base = entity "wall"; AssetId = None; SubmeshIndex = 0; SubmeshCount = 1 }
    let template : ZoneTemplate.ZoneTemplate =
        { AssetId = "room"; Base = entity "room"; Orientation = Quaternion.Identity
          Light = Texture.defaultLight; Parts = [| part |]; Props = [||] }

    GameplayState.presentZone shared &state atriumPlace (Some template)

    Assert.True(state.Zones.ContainsKey atriumPlace)
    Assert.Equal(activeBefore, state.ActiveZone) // an add prepares; it does not enter
    Assert.Equal(cameraBefore, state.Camera)
    let zone = state.Zones.[atriumPlace]
    Assert.Equal(Some "room", zone.Environment |> Option.map (fun environment -> environment.AssetId))
    // The new zone is inactive, so its body was built and then detached: it must not simulate.
    let mutable added = false
    zone.Environment.Value.Parts.IteriAlive (fun _ _ p ->
        match p.Physics with
        | Some physics -> added <- added || Physics.isAdded shared.Physics physics.Body
        | None -> ())
    Assert.False added

    // Presenting a place already present is a bug (the cue guards it).
    let mutable raised = false
    try GameplayState.presentZone shared &state atriumPlace None with _ -> raised <- true
    Assert.True raised

[<Fact>]
let ``switchZone is a no-op for the active place and raises for an absent one`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    GameplayState.addZone shared &state classroomPlace

    GameplayState.switchZone shared &state classroomPlace
    Assert.Equal(classroomPlace, state.ActiveZone)

    let mutable raised = false
    try GameplayState.switchZone shared &state atriumPlace with _ -> raised <- true // atriumPlace is absent
    Assert.True raised

[<Fact>]
let ``selectZone makes a zone the Inspector's subject even when it is already active`` () =
    // The Inspector shows the active zone when no entity is selected (spec 043), so a zone row click
    // has to drop the entity it was showing — including on the zone already in use, where a plain
    // `switchZone` is a no-op and the stale selection would otherwise stay on screen.
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    GameplayState.addZone shared &state classroomPlace

    state.Editor <- { state.Editor with Selection = Some (propRef 0u 0u) }
    GameplayState.selectZone shared &state classroomPlace
    Assert.Equal(classroomPlace, state.ActiveZone)
    Assert.True state.Editor.Selection.IsNone

    // It still switches when the zone is another one, clearing the entity on the way.
    state.Editor <- { state.Editor with Selection = Some (propRef 0u 0u) }
    GameplayState.selectZone shared &state mainPlace
    Assert.Equal(mainPlace, state.ActiveZone)
    Assert.True state.Editor.Selection.IsNone

    let mutable raised = false
    try GameplayState.selectZone shared &state atriumPlace with _ -> raised <- true // atriumPlace is absent
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

    GameplayState.addZone shared &state classroomPlace
    Assert.False(Physics.isAdded shared.Physics body)
    // The entity and its body survive the detach.
    Assert.True(Zone.isAlive main ref)

    GameplayState.switchZone shared &state mainPlace
    Assert.True(Physics.isAdded shared.Physics body)

[<Fact>]
let ``loading an environment changes only the active zone`` () =
    let assets = roomAssets ()
    let shared = sharedState assets
    let mutable state = newState ()
    GameplayState.addZone shared &state classroomPlace

    let classroom = GameplayState.activeZone state
    let environment = Zone.loadEnvironment classroom shared.Physics assets "room"
    Assert.True(Option.isSome classroom.Environment)
    Assert.Equal(environment.Epoch, classroom.Environment.Value.Epoch)

    let main = state.Zones.[mainPlace]
    Assert.True(Option.isNone main.Environment)
    GameplayState.switchZone shared &state mainPlace
    Assert.True(Option.isSome classroom.Environment)

// ── Delete ──

[<Fact>]
let ``the active place cannot be made absent, so a lone present place cannot be removed`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    let mutable raised = false
    try GameplayState.deleteZone shared &state mainPlace with _ -> raised <- true
    Assert.True raised
    Assert.True(state.Zones.ContainsKey mainPlace)

[<Fact>]
let ``deleting an inactive zone releases it and leaves the active zone alone`` () =
    let assets = propAssets ()
    let shared = sharedState assets
    let mutable state = newState ()
    GameplayState.addZone shared &state classroomPlace
    let classroom = GameplayState.activeZone state
    let ref = Zone.spawnProp classroom shared.Physics assets (entity "crate") "crate"
    let body = bodyOf classroom ref

    GameplayState.switchZone shared &state mainPlace
    Assert.False(Physics.isAdded shared.Physics body)

    GameplayState.deleteZone shared &state classroomPlace
    Assert.False(state.Zones.ContainsKey classroomPlace)
    Assert.Equal(mainPlace, state.ActiveZone)
    Assert.Equal(0, classroom.BodyToEntity.Count)

[<Fact>]
let ``the active place cannot be made absent — the author switches first`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    GameplayState.addZone shared &state classroomPlace

    // `classroomPlace` is active; removing it is refused (FR-011).
    let mutable raised = false
    try GameplayState.deleteZone shared &state classroomPlace with _ -> raised <- true
    Assert.True raised
    Assert.True(state.Zones.ContainsKey classroomPlace)

    // Switch away, then it can be removed.
    GameplayState.switchZone shared &state mainPlace
    GameplayState.deleteZone shared &state classroomPlace
    Assert.False(state.Zones.ContainsKey classroomPlace)
    Assert.Equal(mainPlace, state.ActiveZone)

// ── The one undo history (spec 048) ──

[<Fact>]
let ``switching zones leaves the one undo history alone`` () =
    // The per-zone swap is gone: an edit recorded in one zone is still on the history after moving to another,
    // which is what lets it be undone later (see the cross-zone case below).
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    GameplayState.addZone shared &state classroomPlace

    let cmd = Command.Transform (actorRef 0u 0u, transformIdentity, { transformIdentity with Position = Vector3.One })
    state.Editor <- { state.Editor with Undo = UndoStack.push state.Editor.Undo cmd }
    Assert.False(List.isEmpty state.Editor.Undo.Applied)

    GameplayState.switchZone shared &state mainPlace
    Assert.False(List.isEmpty state.Editor.Undo.Applied) // nothing parked, nothing loaded

    GameplayState.switchZone shared &state classroomPlace
    Assert.False(List.isEmpty state.Editor.Undo.Applied)

// ── The cue's steps on the one history (spec 049) ──

[<Fact>]
let ``a play prunes the cue's steps and leaves a zone step undoable`` () =
    // SC-003 / FR-005, FR-010. A `SetCue` step is stale the moment the cue advances, so it goes from the
    // history on the way into play — while a zone step is unaffected and must survive.
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    let lightBefore = zone.Light
    let lightAfter = { lightBefore with Pitch = 20.0f }

    do History.commit shared &state (SetCue (Cue.Fin, Cue.Wait 1.0))
    do History.commit shared &state (SetLight (lightBefore, lightAfter))
    Assert.Equal(2, List.length state.Editor.Undo.Applied)
    Assert.Equal(lightAfter, zone.Light)

    do History.pruneCueSteps &state

    // The cue contributes no step, and the zone step is still on the history.
    Assert.Equal(1, List.length state.Editor.Undo.Applied)
    match state.Editor.Undo.Applied with
    | [ SetLight _ ] -> ()
    | other -> Assert.Fail(sprintf "expected the zone step to survive the prune, got %A" other)

    do History.undoHistory shared &state
    Assert.Equal(lightBefore, zone.Light)

[<Fact>]
let ``a pruned cue step goes from the redo side too`` () =
    // FR-005: both sides. A cue edit taken back is still a cue step, and is dropped all the same.
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    do History.commit shared &state (SetCue (Cue.Fin, Cue.Wait 1.0))
    do History.undoHistory shared &state
    Assert.Equal(1, List.length state.Editor.Undo.Redo) // the cue step, undone

    do History.pruneCueSteps &state
    Assert.Equal(0, List.length state.Editor.Undo.Applied)
    Assert.Equal(0, List.length state.Editor.Undo.Redo)

[<Fact>]
let ``pruning a history with no cue steps changes nothing`` () =
    let mutable state = newState ()
    do History.pruneCueSteps &state
    Assert.Equal(0, List.length state.Editor.Undo.Applied)
    Assert.Equal(0, List.length state.Editor.Undo.Redo)

[<Fact>]
let ``a play with no cue edits leaves the history exactly as it was`` () =
    // US3/AC2. The prune removes cue steps and nothing else, so a history that holds only zone steps is
    // untouched — in count, in order, and on both sides.
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    let light = zone.Light

    do History.commit shared &state (SetLight (light, { light with Pitch = 10.0f }))
    GameplayState.addZone shared &state classroomPlace // direct here; the editor records a `ZoneAdd`
    do History.commit shared &state (ZoneMove (classroomPlace, mainPlace))
    do History.undoHistory shared &state // so the redo side is non-empty too

    do History.pruneCueSteps &state

    match state.Editor.Undo.Applied, state.Editor.Undo.Redo with
    | [ SetLight _ ], [ ZoneMove (classroomPlace, mainPlace) ] -> ()
    | applied, redo -> Assert.Fail(sprintf "expected the history untouched, got applied %A, redo %A" applied redo)

// ── Per-zone player designation ──

[<Fact>]
let ``a player designation is per zone and never aliases a same-index actor elsewhere`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    let main = GameplayState.activeZone state
    let mainActor = main.Actors.Add(actor "player")
    main.Player <- ValueSome mainActor
    Assert.True(Systems.isPlayerLive main)

    GameplayState.addZone shared &state classroomPlace
    let classroom = GameplayState.activeZone state
    // Same slot index and generation as `main`'s actor, but no designation here.
    let classroomActor = classroom.Actors.Add(actor "other")
    Assert.Equal(mainActor.Index, classroomActor.Index)
    Assert.False(Systems.isPlayerLive classroom)
    Assert.True(Systems.isPlayerLive main)

    classroom.Player <- ValueSome classroomActor
    Assert.True(Systems.isPlayerLive classroom)

[<Fact>]
let ``an edit in one zone is undone after a move, in the order the edits were made`` () =
    // The one history (spec 048): switching zones does not park or discard anything, and the moves themselves
    // are steps — so an edit made before moving away is still reachable afterwards.
    let shared = sharedState (propAssets ())
    let mutable state = newState ()

    // An edit in `main`.
    let main = GameplayState.activeZone state
    let mainActor = main.Actors.Add(actor "player")
    do History.commit shared &state (SetPlayer (ValueNone, ValueSome mainActor))
    Assert.Equal(ValueSome mainActor, main.Player)

    // A move to `classroom`, itself a step.
    GameplayState.addZone shared &state classroomPlace
    do History.commit shared &state (ZoneMove (mainPlace, classroomPlace))
    let classroom = GameplayState.activeZone state

    // An edit in `classroom`.
    let before = classroom.Light
    do History.commit shared &state (SetLight (before, { before with Pitch = 20.0f }))
    Assert.NotEqual(before, classroom.Light)

    // The first undo takes back classroom's edit, with classroom still on screen.
    do History.undoHistory shared &state
    Assert.Equal(before, classroom.Light)
    Assert.Equal(classroomPlace, state.ActiveZone)

    // The second steps back over the move, carrying the view to `main` — where the first edit is now reachable,
    // which is the whole point: no edit is stranded by moving away.
    do History.undoHistory shared &state
    Assert.Equal(mainPlace, state.ActiveZone)
    Assert.Equal(ValueSome mainActor, main.Player)

    // The third takes back the edit made in `main`.
    do History.undoHistory shared &state
    Assert.True(ValueOption.isNone main.Player)

    // Redo replays them in the order they were made, with the view following.
    do History.redoHistory shared &state
    Assert.Equal(ValueSome mainActor, main.Player)
    Assert.Equal(mainPlace, state.ActiveZone)
    do History.redoHistory shared &state
    Assert.Equal(classroomPlace, state.ActiveZone)
    do History.redoHistory shared &state
    Assert.NotEqual(before, classroom.Light)

[<Fact>]
let ``each move is its own step, and moving away parks nothing`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    GameplayState.addZone shared &state classroomPlace // direct here; US2 makes the editor's add a step
    do History.commit shared &state (ZoneMove (classroomPlace, mainPlace))
    Assert.Equal(mainPlace, state.ActiveZone)

    // Exactly one step: the move, and nothing parked by the switch it caused.
    Assert.Equal(1, List.length state.Editor.Undo.Applied)

    do History.undoHistory shared &state
    Assert.Equal(classroomPlace, state.ActiveZone)
    Assert.Equal(0, List.length state.Editor.Undo.Applied)
    Assert.Equal(1, List.length state.Editor.Undo.Redo)

    do History.redoHistory shared &state
    Assert.Equal(mainPlace, state.ActiveZone)

[<Fact>]
let ``adding a zone and undoing it leaves the zones and the active zone as they were`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    let before = state.Zones.Count

    do History.commit shared &state (ZoneAdd (mainPlace, atriumPlace))
    Assert.True(state.Zones.ContainsKey atriumPlace)
    Assert.Equal(atriumPlace, state.ActiveZone)

    // One undo takes back the add **and** the zone change it caused: one click, one step (research D3).
    do History.undoHistory shared &state
    Assert.False(state.Zones.ContainsKey atriumPlace)
    Assert.Equal(mainPlace, state.ActiveZone)
    Assert.Equal(before, state.Zones.Count)

    do History.redoHistory shared &state
    Assert.True(state.Zones.ContainsKey atriumPlace)
    Assert.Equal(atriumPlace, state.ActiveZone)

// ── A zone naming an unknown environment asset fails loudly (spec 053 FR-004 supersedes 050 FR-017) ──

[<Fact>]
let ``a switch to a zone naming an unknown environment asset fails loudly`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()

    // The zone names an environment asset the manager does not hold. This is an authoring bug, not a
    // recoverable async failure: `requestEnvironment` raises on an unregistered id. (A *registered*
    // asset whose model file is missing now fails asynchronously and the switch completes — FR-004.)
    let absent : Environment =
        { Base = entity "room"
          AssetId = "missing"
          Orientation = Quaternion.Identity
          Epoch = 1u
          Parts = SlotMap<Prop>(4u)
          PartsSource = Authored }
    state.Zones.[mainPlace].EnvironmentState <- Zone.Active absent

    GameplayState.addZone shared &state classroomPlace
    Assert.Equal(classroomPlace, state.ActiveZone)
    let raised =
        try GameplayState.switchZone shared &state mainPlace; false
        with _ -> true
    Assert.True raised
    Assert.Equal(classroomPlace, state.ActiveZone)

[<Fact>]
let ``deleting a zone and undoing restores its entities, environment and light`` () =
    let assets = roomAssets ()
    // A placed prop asset too, so the round trip covers a prop as well as the environment's own part.
    assets.Props.["crate"] <- residentAsset "crate" [| loadedSubmesh [| 0.0f; 0.0f; 0.0f;  1.0f; 0.0f; 0.0f;  0.0f; 1.0f; 0.0f |] [| 0; 1; 2 |] |] [||]
    let shared = sharedState assets
    let mutable state = newState ()

    GameplayState.addZone shared &state classroomPlace
    let classroom = GameplayState.activeZone state
    let environment = Zone.loadEnvironment classroom shared.Physics shared.Assets "room"
    let partRef' = partRef environment.Epoch 0u 0u
    let propRef' = Zone.spawnProp classroom shared.Physics shared.Assets (entity "crate") "crate"
    let waypointRef' = Zone.spawnWaypoint classroom (entity "path")
    let triggerRef' = Zone.spawnTrigger classroom (entity "area")
    let light = { classroom.Light with Pitch = 33.0f }
    classroom.Light <- light
    Assert.True(Zone.isAlive classroom partRef')

    // Switch away first: the active place cannot be made absent (FR-011).
    GameplayState.switchZone shared &state mainPlace

    // Delete it: one step, carrying everything needed to put it back.
    do History.commit shared &state (ZoneDelete (mainPlace, classroomPlace, Zone.captureZone state.Zones.[classroomPlace]))
    Assert.False(state.Zones.ContainsKey classroomPlace)
    Assert.Equal(mainPlace, state.ActiveZone)

    // Undo: the zone is back with its entities at the **same refs** — so a ref held before the delete still
    // resolves (FR-009) — and the same environment, light and player designation. It is restored, not switched
    // to, so the active place is unchanged.
    do History.undoHistory shared &state
    Assert.True(state.Zones.ContainsKey classroomPlace)
    Assert.Equal(mainPlace, state.ActiveZone)
    let restored = state.Zones.[classroomPlace]
    Assert.True(Zone.isAlive restored partRef')
    Assert.True(Zone.isAlive restored propRef')
    Assert.True(Zone.isAlive restored waypointRef')
    Assert.True(Zone.isAlive restored triggerRef')
    Assert.Equal(light, restored.Light)

    // Redo deletes it again.
    do History.redoHistory shared &state
    Assert.False(state.Zones.ContainsKey classroomPlace)
    Assert.Equal(mainPlace, state.ActiveZone)

[<Fact>]
let ``deleting an inactive zone and undoing leaves the active zone alone`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    GameplayState.addZone shared &state classroomPlace
    GameplayState.switchZone shared &state mainPlace

    // `previous` is the zone active at the time — `main` — because the deleted zone is not the active one.
    do History.commit shared &state (ZoneDelete (mainPlace, classroomPlace, Zone.captureZone state.Zones.[classroomPlace]))
    Assert.False(state.Zones.ContainsKey classroomPlace)
    Assert.Equal(mainPlace, state.ActiveZone)

    do History.undoHistory shared &state
    Assert.True(state.Zones.ContainsKey classroomPlace)
    Assert.Equal(mainPlace, state.ActiveZone) // restored, but not switched to

    do History.redoHistory shared &state
    Assert.False(state.Zones.ContainsKey classroomPlace)
    Assert.Equal(mainPlace, state.ActiveZone)

[<Fact>]
let ``a zone's contents carry its actors and its player designation`` () =
    // SC-003's actor clause, at the half the headless envelope reaches. `captureZone` reads the actor's ref and
    // authored state and the designation; *restoring* an actor is the other half and needs a renderer, which
    // `ActorRender.create` builds from the GPU device (`SDL.CreateGPUBuffer`) — so a restore is outside this
    // envelope, and `quickstart.md`'s step 3 is where the restored actor is seen by hand.
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    let handle = zone.Actors.Add(actor "pupil")
    zone.Player <- ValueSome handle

    let contents = Zone.captureZone zone
    Assert.Equal(1, List.length contents.Actors)
    let ref', captured = List.head contents.Actors
    Assert.Equal(handle.Index, ref'.Index)
    Assert.Equal((zone.Actors.Get handle).State, captured)
    Assert.Equal(ValueSome handle, contents.Player)

    // And `occupyZone` puts the designation back — a field of its own, which needs no actor to restore.
    let target = Zone.create ()
    do Zone.occupyZone target shared.Physics shared.Assets { contents with Actors = [] }
    Assert.Equal(ValueSome handle, target.Player)

[<Fact>]
let ``undo and redo with nothing on their side are no-ops`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()
    let activeZoneAtStart = state.ActiveZone

    do History.undoHistory shared &state
    do History.redoHistory shared &state
    do History.undoHistory shared &state

    Assert.True(List.isEmpty state.Editor.Undo.Applied)
    Assert.True(List.isEmpty state.Editor.Undo.Redo)
    Assert.Equal(activeZoneAtStart, state.ActiveZone)

[<Fact>]
let ``undoing through the whole history returns the project to where it started`` () =
    let shared = sharedState (roomAssets ())
    let mutable state = newState ()
    let main = GameplayState.activeZone state
    let lightAtStart = main.Light

    // A session that touches every kind of step: add a zone, move between zones, edit one, delete the added one.
    do History.commit shared &state (ZoneAdd (mainPlace, atriumPlace))
    do History.commit shared &state (ZoneMove (atriumPlace, mainPlace))
    do History.commit shared &state (SetLight (lightAtStart, { lightAtStart with Pitch = 10.0f }))
    do History.commit shared &state (ZoneDelete (mainPlace, atriumPlace, Zone.captureZone state.Zones.[atriumPlace]))
    Assert.False(state.Zones.ContainsKey atriumPlace)

    // Undo the whole session: the project is exactly as it began, zones and entities included.
    for _ in 1 .. 4 do History.undoHistory shared &state
    Assert.Equal(1, state.Zones.Count)
    Assert.Equal(mainPlace, state.ActiveZone)
    Assert.Equal(lightAtStart, main.Light)
    Assert.True(List.isEmpty state.Editor.Undo.Applied)

    // A new edit after the undos clears the redo side.
    do History.commit shared &state (SetLight (lightAtStart, { lightAtStart with Pitch = 5.0f }))
    Assert.True(List.isEmpty state.Editor.Undo.Redo)

[<Fact>]
let ``switching re-frames the camera on the incoming zone`` () =
    let shared = sharedState (propAssets ())
    let mutable state = newState ()

    // A designated player in `classroom` at a known position.
    GameplayState.addZone shared &state classroomPlace
    let classroom = GameplayState.activeZone state
    let handle = classroom.Actors.Add(actor "player")
    let a = &classroom.Actors.Get handle
    a <- { a with State.Base.Transform = { a.State.Base.Transform with Position = Vector3(8.0f, 0.0f, 8.0f) } }
    classroom.Player <- ValueSome handle

    // Switch away and move the camera, then switch back: the camera pivots on the
    // incoming zone's designated player (feet + follow height).
    GameplayState.switchZone shared &state mainPlace
    state.Camera <- { state.Camera with Target = Vector3(2.0f, 2.0f, 2.0f) }
    GameplayState.switchZone shared &state classroomPlace

    Assert.Equal(Vector3(8.0f, state.Follow.Height, 8.0f), state.Camera.Target)
