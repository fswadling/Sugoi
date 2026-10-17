module SpawningTests

// Drive the real `Spawning`/`Zone` paths through a headless asset manager (the
// prop/part render path never touches the GPU device), so command pairing and kind
// dispatch are covered for real rather than mocked.

open Xunit
open System
open System.Collections.Generic
open Prime
open System.Numerics
open Command
open Entity
open EnvironmentAsset
open Gltf
open Physics
open TestSupport

// ── Headless rigs ──
// Only the fields the collider/render paths read are populated; a zero Device is
// safe because `EnvironmentRender.create` never dereferences it. Tests that read no asset use `bareShared`.

let private headlessAssets () : Assets.AssetManager =
    managerWith [ "aiko", actorAsset "aiko" ] [ "crate", crateAsset () ] [ "room", roomAsset () ]

let private sharedState (assets: Assets.AssetManager) : SharedState.SharedState =
    headlessSharedWith assets

/// A device-free shared state with no assets: enough for the waypoint/trigger/duplicate paths.
let private bareShared () : SharedState.SharedState =
    headlessShared ()

let private gameplayState (zone: Zone.Zone) : GameplayState.GameplayState =
    stateWithZone zone

// ── Create ──

[<Fact>]
let ``a create carries the authored value it was given, and redo rebuilds the resources`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let mutable state = gameplayState zone

    Spawning.spawnProp shared &state (entity "crate") "crate"

    let ref = state.Editor.Selection.Value
    let live = (zone.Props.Get(handleOf ref)).State
    // The create records the authored value it was given, field for field.
    match state.Editor.Undo.Applied with
    | [ PropCreate (_, r, created) ] ->
        Assert.Equal<EntityRef>(ref, r)
        Assert.Equal(live, created)
    | other -> Assert.Fail(sprintf "expected exactly one PropCreate command, got %A" other)

    Assert.True(Option.isSome (zone.Props.Get(handleOf ref)).Physics)

    // Undo destroys the entity and its body, redo rebuilds both from the recorded value.
    do Undo.undoHistory shared &state
    Assert.False(Zone.isAlive zone ref)

    do Undo.redoHistory shared &state

    Assert.True(Zone.isAlive zone ref)
    Assert.Equal(live, (zone.Props.Get(handleOf ref)).State)
    Assert.True(Option.isSome (zone.Props.Get(handleOf ref)).Physics)

[<Fact>]
let ``a create is refused when the kind's map is at capacity`` () =
    // spec 080, US5: filling a kind to its fixed capacity must refuse the next create rather than let
    // `SlotMap.Add` raise. Triggers cap at 32; fill the map directly, then create through `Spawning`.
    let shared = bareShared ()
    let zone = Zone.create ()
    let mutable state = gameplayState zone
    for i in 1 .. int zone.Triggers.Capacity do
        do ignore (Zone.spawnTrigger zone (entity (sprintf "t%d" i)))

    let before = zone.Triggers.Count
    Spawning.spawnTrigger &state (entity "overflow")

    Assert.Equal(before, zone.Triggers.Count)                 // nothing was created
    Assert.Equal(0, List.length state.Editor.Undo.Applied)    // and nothing was recorded

// ── Duplicate ──

[<Fact>]
let ``duplicating a part spawns a copy, selects it, and pushes the reversing command`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let mutable state = gameplayState (Zone.create ())
    let zone = GameplayState.activeZone state
    let environment = Zone.loadEnvironment zone state.Physics assets "room"
    let source = partRef environment.Epoch 0u 0u

    Spawning.duplicate shared &state source (entity "wall-copy")

    Assert.Equal(2u, environment.Parts.Count)
    // The source is untouched and the copy is a distinct, live part ref.
    Assert.True(Zone.isAlive zone source)
    match state.Editor.Selection with
    | Some copy ->
        Assert.NotEqual(source, copy)
        Assert.Equal(EntityKind.Part, copy.Kind)
        Assert.Equal(environment.Epoch, copy.Epoch)
        Assert.True(Zone.isAlive zone copy)
        Assert.Equal("wall-copy", (Zone.tryEntityAt zone copy).Value.Name)
    | None -> Assert.Fail("duplicate did not select the copy")

    // The paired command must name the copy, so undoing it removes exactly that.
    match state.Editor.Undo.Applied with
    | PartCreate (_, r, _) :: _ -> Assert.Equal(state.Editor.Selection.Value, r)
    | other -> Assert.Fail(sprintf "expected a PartCreate command, got %A" other)

[<Fact>]
let ``duplicating a waypoint spawns a copy, selects it, and pushes the reversing command`` () =
    let shared = bareShared ()
    let zone = Zone.create ()
    let source = Zone.spawnWaypoint zone (entity "start")
    let mutable state = gameplayState zone

    Spawning.duplicate shared &state source (entity "start-copy")

    Assert.Equal(2u, zone.Waypoints.Count)
    // The source is untouched and the copy is a distinct, live waypoint ref.
    Assert.True(Zone.isAlive zone source)
    match state.Editor.Selection with
    | Some copy ->
        Assert.NotEqual(source, copy)
        Assert.Equal(EntityKind.Waypoint, copy.Kind)
        Assert.Equal("start-copy", (Zone.tryEntityAt zone copy).Value.Name)
    | None -> Assert.Fail("duplicate did not select the copy")

    match state.Editor.Undo.Applied with
    | WaypointCreate (_, r, _) :: _ -> Assert.Equal(state.Editor.Selection.Value, r)
    | other -> Assert.Fail(sprintf "expected a WaypointCreate command, got %A" other)

[<Fact>]
let ``a duplicate records the copy's own authored value, so redo restores the copy`` () =
    let shared = bareShared ()
    let zone = Zone.create ()
    let source = Zone.spawnWaypoint zone (entity "start")
    let mutable state = gameplayState zone

    Spawning.duplicate shared &state source (entity "start-copy")

    let copy = state.Editor.Selection.Value
    match state.Editor.Undo.Applied with
    | [ WaypointCreate (_, r, recorded) ] ->
        Assert.Equal<EntityRef>(copy, r)
        // The value belongs to the copy, not to the original: a redo must restore the copy
        // as the duplicate left it rather than a second original.
        Assert.Equal(Zone.captureWaypoint zone copy, recorded)
        Assert.Equal("start-copy", recorded.Base.Name)
    | other -> Assert.Fail(sprintf "expected exactly one WaypointCreate command, got %A" other)

    do Undo.undoHistory shared &state
    Assert.False(Zone.isAlive zone copy)

    do Undo.redoHistory shared &state

    Assert.True(Zone.isAlive zone copy)
    Assert.Equal("start-copy", (Zone.tryEntityAt zone copy).Value.Name)

[<Fact>]
let ``deleting a waypoint and undoing restores its transform at the recorded slot`` () =
    let shared = bareShared ()
    let zone = Zone.create ()
    let moved =
        { transformIdentity with
            Position = Vector3(1.0f, 2.0f, 3.0f)
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f) }
    let ref = Zone.spawnWaypoint zone { entity "start" with Transform = moved }
    let mutable state = gameplayState zone

    Selection.deleteRef shared &state ref
    Assert.False(Zone.isAlive zone ref)

    // Undo re-occupies the recorded slot with the entity the delete captured.
    do Undo.undoHistory shared &state

    Assert.True(Zone.isAlive zone ref)
    Assert.Equal(moved.Position, (Zone.tryEntityAt zone ref).Value.Transform.Position)
    Assert.Equal(moved.Rotation, (Zone.tryEntityAt zone ref).Value.Transform.Rotation)

[<Fact>]
let ``a waypoint at the player copies her position and full rotation with unit scale`` () =
    let zone = Zone.create ()
    let position = Vector3(1.0f, 2.0f, 3.0f)
    let rotation = Quaternion.CreateFromYawPitchRoll(0.75f, 0.3f, 0.0f)
    let baseActor = actor "Aiko"
    let player =
        { baseActor with
            State =
                { baseActor.State with
                    Base =
                        { baseActor.State.Base with
                            Transform =
                                { transformIdentity with
                                    Position = position
                                    Rotation = rotation
                                    Scale = Vector3(2.0f, 2.0f, 2.0f) } } } }
    zone.Player <- ValueSome (zone.Actors.Add player)
    let camera = Camera.create Vector3.Zero Vector3.UnitZ

    match Spawning.waypointAt zone camera EditorState.Player with
    | Some entity ->
        Assert.Equal("Player 1", entity.Name)
        Assert.Equal(position, entity.Transform.Position)
        // The full rotation is copied, not just its yaw component.
        Assert.Equal(rotation, entity.Transform.Rotation)
        // A waypoint is a point, not a scaled copy of the actor.
        Assert.Equal(Vector3.One, entity.Transform.Scale)
    | None -> Assert.Fail "expected a waypoint at the player"

[<Fact>]
let ``the player anchor is unavailable without a live player`` () =
    let zone = Zone.create ()
    let camera = Camera.create Vector3.Zero Vector3.UnitZ
    Assert.False(Spawning.anchorAvailable zone EditorState.Player)
    Assert.True((Spawning.waypointAt zone camera EditorState.Player).IsNone)

    // A stale designation is no player either.
    zone.Player <- ValueSome { Index = 0u; Generation = 99u }
    Assert.False(Spawning.anchorAvailable zone EditorState.Player)
    Assert.True((Spawning.waypointAt zone camera EditorState.Player).IsNone)

[<Fact>]
let ``spawning at the player creates and selects one waypoint in one step`` () =
    let shared = bareShared ()
    let zone = Zone.create ()
    zone.Player <- ValueSome (zone.Actors.Add(actor "Aiko"))
    let mutable state = gameplayState zone

    Spawning.spawnWaypointAt &state EditorState.Player

    let ref = state.Editor.Selection.Value
    Assert.Equal(EntityKind.Waypoint, ref.Kind)
    Assert.Equal("Player 1", (Zone.tryEntityAt zone ref).Value.Name)
    match state.Editor.Undo.Applied with
    | [ WaypointCreate (_, r, waypoint) ] ->
        Assert.Equal<EntityRef>(ref, r)
        Assert.Equal("Player 1", waypoint.Base.Name)
    | other -> Assert.Fail(sprintf "expected exactly one WaypointCreate command, got %A" other)

    do Undo.undoHistory shared &state
    Assert.Equal(0u, zone.Waypoints.Count)

    do Undo.redoHistory shared &state
    Assert.Equal(1u, zone.Waypoints.Count)

[<Fact>]
let ``creation names waypoints per anchor and reuses the lowest free number`` () =
    let shared = bareShared ()
    let zone = Zone.create ()
    let mutable state = gameplayState zone

    Spawning.spawnWaypointAt &state EditorState.Camera
    let camera1 = state.Editor.Selection.Value
    Spawning.spawnWaypointAt &state EditorState.Camera
    let camera2 = state.Editor.Selection.Value
    Spawning.spawnWaypointAt &state EditorState.Origin
    let origin1 = state.Editor.Selection.Value

    Assert.Equal("Camera 1", (Zone.tryEntityAt zone camera1).Value.Name)
    Assert.Equal("Camera 2", (Zone.tryEntityAt zone camera2).Value.Name)
    Assert.Equal("Origin 1", (Zone.tryEntityAt zone origin1).Value.Name)

    // A rename frees the number for the next creation at that anchor.
    Selection.renameRef shared &state camera1 "Hall"
    Spawning.spawnWaypointAt &state EditorState.Camera
    Assert.Equal("Camera 1", (Zone.tryEntityAt zone state.Editor.Selection.Value).Value.Name)

    // Deleting a waypoint frees its number too.
    Selection.deleteRef shared &state camera2
    Spawning.spawnWaypointAt &state EditorState.Camera
    Assert.Equal("Camera 2", (Zone.tryEntityAt zone state.Editor.Selection.Value).Value.Name)

[<Fact>]
let ``creating at an anchor is refused at capacity and records nothing`` () =
    let zone = Zone.create ()
    for i in 1 .. int zone.Waypoints.Capacity do
        do ignore (Zone.spawnWaypoint zone (entity (sprintf "w%d" i)))
    let mutable state = gameplayState zone
    let before = zone.Waypoints.Count

    Spawning.spawnWaypointAt &state EditorState.Origin

    Assert.Equal(before, zone.Waypoints.Count)
    Assert.Empty(state.Editor.Undo.Applied)

[<Fact>]
let ``adding a next waypoint links it and undoes as one step`` () =
    let shared = bareShared ()
    let zone = Zone.create ()
    let a = Zone.spawnWaypoint zone (entity "a")
    let mutable state = gameplayState zone

    Spawning.spawnLinkedWaypoint &state a (entity "b")

    Assert.Equal(2u, zone.Waypoints.Count)
    let b = state.Editor.Selection.Value
    Assert.Equal(EntityKind.Waypoint, b.Kind)
    Assert.Equal("b", (Zone.tryEntityAt zone b).Value.Name)
    Assert.Equal(Some b, Zone.waypointNext zone a)
    // The old end of the chain has no next of its own.
    Assert.True(Option.isNone (Zone.waypointNext zone b))

    // The spawn and the link are one command, naming the predecessor and the new waypoint.
    match state.Editor.Undo.Applied with
    | [ LinkSpawn (_, from, None, r, _) ] ->
        Assert.Equal<EntityRef>(a, from)
        Assert.Equal<EntityRef>(b, r)
    | other -> Assert.Fail(sprintf "expected exactly one LinkSpawn command, got %A" other)

    // One undo removes the new waypoint and restores the chain end it extended.
    do Undo.undoHistory shared &state
    Assert.Equal(1u, zone.Waypoints.Count)
    Assert.True(Option.isNone (Zone.waypointNext zone a))

    // Redo reapplies both halves at the recorded ref.
    do Undo.redoHistory shared &state
    Assert.Equal(2u, zone.Waypoints.Count)
    Assert.Equal(Some b, Zone.waypointNext zone a)

[<Fact>]
let ``extending a middle waypoint inserts it and undo restores both links`` () =
    let shared = bareShared ()
    let zone = Zone.create ()
    let a = Zone.spawnWaypoint zone (entity "a")
    let original = Zone.spawnWaypoint zone (entity "original")
    do Zone.setWaypointNext zone a (Some original)
    let mutable state = gameplayState zone

    Spawning.spawnNextWaypointAt &state a EditorState.Camera

    Assert.Equal(3u, zone.Waypoints.Count)
    let inserted = state.Editor.Selection.Value
    Assert.Equal("Camera 1", (Zone.tryEntityAt zone inserted).Value.Name)
    // The new waypoint goes between a and original; the tail is preserved.
    Assert.Equal(Some inserted, Zone.waypointNext zone a)
    Assert.Equal(Some original, Zone.waypointNext zone inserted)

    // Undo restores a's original next and removes the inserted waypoint.
    do Undo.undoHistory shared &state
    Assert.Equal(Some original, Zone.waypointNext zone a)
    Assert.True(Option.isNone (Zone.tryEntityAt zone inserted))

    // Redo restores the insertion, including the preserved tail.
    do Undo.redoHistory shared &state
    Assert.Equal(Some inserted, Zone.waypointNext zone a)
    Assert.Equal(Some original, Zone.waypointNext zone inserted)

[<Fact>]
let ``a duplicated waypoint carries the link to the same successor`` () =
    let shared = bareShared ()
    let zone = Zone.create ()
    let a = Zone.spawnWaypoint zone (entity "a")
    let b = Zone.spawnWaypoint zone (entity "b")
    do Zone.setWaypointNext zone a (Some b)
    let mutable state = gameplayState zone

    Spawning.duplicate shared &state a (entity "a-copy")

    // A copy carries every property of the original, its link included, so the chain now
    // branches at `b`; the original's own link is untouched.
    let copy = state.Editor.Selection.Value
    Assert.NotEqual<EntityRef>(a, copy)
    Assert.Equal(Some b, Zone.waypointNext zone copy)
    Assert.Equal(Some b, Zone.waypointNext zone a)

    // The copy's link is a reference like any other: it goes stale rather than pointing
    // somewhere else when its target is deleted.
    do Zone.removeWaypoint zone b
    Assert.True(Option.isNone (Zone.waypointNext zone copy))
    Assert.True(Option.isNone (Zone.waypointNext zone a))

[<Fact>]
let ``a trigger at the player uses her feet minus 0.1 m and the default volume`` () =
    let shared = bareShared ()
    let zone = Zone.create ()
    let feet = Vector3(1.0f, 2.0f, 3.0f)
    let baseActor = actor "Aiko"
    let player =
        { baseActor with
            State =
                { baseActor.State with
                    Base =
                        { baseActor.State.Base with
                            Transform =
                                { transformIdentity with
                                    Position = feet
                                    Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f)
                                    Scale = Vector3(2.0f, 2.0f, 2.0f) } } } }
    zone.Player <- ValueSome (zone.Actors.Add player)
    let mutable state = gameplayState zone

    Spawning.spawnTriggerAt &state EditorState.Player

    let ref = state.Editor.Selection.Value
    let trigger = (Zone.tryTriggerAt zone ref).Value
    Assert.Equal(EntityKind.Trigger, ref.Kind)
    Assert.Equal("Player 1", trigger.State.Base.Name)
    Assert.Equal(feet - Vector3(0.0f, 0.1f, 0.0f), trigger.State.Base.Transform.Position)
    // The anchor supplies position only: identity rotation and unit scale.
    Assert.Equal(Quaternion.Identity, trigger.State.Base.Transform.Rotation)
    Assert.Equal(Vector3.One, trigger.State.Base.Transform.Scale)
    // The existing trigger shape is unchanged.
    Assert.Equal(1.0f, trigger.State.Radius)
    Assert.Equal(2.0f, trigger.State.Height)

    match state.Editor.Undo.Applied with
    | [ TriggerCreate (_, r, _) ] -> Assert.Equal<EntityRef>(ref, r)
    | other -> Assert.Fail(sprintf "expected exactly one TriggerCreate command, got %A" other)

    do Undo.undoHistory shared &state
    Assert.Equal(0u, zone.Triggers.Count)

    do Undo.redoHistory shared &state
    Assert.Equal(1u, zone.Triggers.Count)

[<Fact>]
let ``a trigger at the camera uses the camera's exact position`` () =
    let zone = Zone.create ()
    let camera = Camera.create (Vector3(4.0f, 5.0f, 6.0f)) (Vector3(4.0f, 5.0f, 7.0f))

    match Spawning.triggerAt zone camera EditorState.Camera with
    | Some entity ->
        Assert.Equal("Camera 1", entity.Name)
        Assert.Equal(camera.Position, entity.Transform.Position)
        Assert.Equal(Quaternion.Identity, entity.Transform.Rotation)
        Assert.Equal(Vector3.One, entity.Transform.Scale)
    | None -> Assert.Fail "expected a trigger at the camera"

[<Fact>]
let ``the origin anchor is zero and an unavailable player creates nothing`` () =
    let zone = Zone.create ()
    let camera = Camera.create (Vector3(1.0f, 2.0f, 3.0f)) Vector3.Zero

    match Spawning.triggerAt zone camera EditorState.Origin with
    | Some entity -> Assert.Equal(Vector3.Zero, entity.Transform.Position)
    | None -> Assert.Fail "expected a trigger at the origin"

    Assert.False(Spawning.anchorAvailable zone EditorState.Player)
    Assert.True((Spawning.triggerAt zone camera EditorState.Player).IsNone)

    // A stale designation is no player either.
    zone.Player <- ValueSome { Index = 0u; Generation = 99u }
    Assert.True((Spawning.triggerAt zone camera EditorState.Player).IsNone)

    let shared = bareShared ()
    let mutable state = gameplayState zone
    Spawning.spawnTriggerAt &state EditorState.Player
    Assert.Equal(0u, zone.Triggers.Count)
    Assert.Empty(state.Editor.Undo.Applied)

[<Fact>]
let ``a trigger created at an anchor does not follow the player or camera`` () =
    let zone = Zone.create ()
    let handle = zone.Actors.Add(actor "Aiko")
    zone.Player <- ValueSome handle
    let mutable state = gameplayState zone

    // At the player: moving her afterwards leaves the trigger where it was stamped.
    Spawning.spawnTriggerAt &state EditorState.Player
    let playerTrigger = state.Editor.Selection.Value
    let playerBase = (Zone.tryTriggerAt zone playerTrigger).Value.State.Base.Transform.Position
    let player = &zone.Actors.Get handle
    player <- { player with State = { player.State with Base = { player.State.Base with Transform = { player.State.Base.Transform with Position = Vector3(9.0f, 9.0f, 9.0f) } } } }
    Assert.Equal(playerBase, (Zone.tryTriggerAt zone playerTrigger).Value.State.Base.Transform.Position)

    // At the camera: moving it afterwards leaves the trigger where it was stamped.
    let camera = Camera.create (Vector3(4.0f, 5.0f, 6.0f)) (Vector3(4.0f, 5.0f, 7.0f))
    state.Camera <- camera
    Spawning.spawnTriggerAt &state EditorState.Camera
    let cameraTrigger = state.Editor.Selection.Value
    let cameraBase = (Zone.tryTriggerAt zone cameraTrigger).Value.State.Base.Transform.Position
    state.Camera <- Camera.create (Vector3(20.0f, 20.0f, 20.0f)) (Vector3(20.0f, 20.0f, 21.0f))
    Assert.Equal(cameraBase, (Zone.tryTriggerAt zone cameraTrigger).Value.State.Base.Transform.Position)

[<Fact>]
let ``trigger creation names per anchor and reuses the lowest free number`` () =
    let shared = bareShared ()
    let zone = Zone.create ()
    let mutable state = gameplayState zone

    Spawning.spawnTriggerAt &state EditorState.Camera
    let camera1 = state.Editor.Selection.Value
    Spawning.spawnTriggerAt &state EditorState.Camera
    let camera2 = state.Editor.Selection.Value
    Spawning.spawnTriggerAt &state EditorState.Origin
    let origin1 = state.Editor.Selection.Value

    Assert.Equal("Camera 1", (Zone.tryTriggerAt zone camera1).Value.State.Base.Name)
    Assert.Equal("Camera 2", (Zone.tryTriggerAt zone camera2).Value.State.Base.Name)
    Assert.Equal("Origin 1", (Zone.tryTriggerAt zone origin1).Value.State.Base.Name)

    zone.Player <- ValueSome (zone.Actors.Add(actor "Aiko"))
    Spawning.spawnTriggerAt &state EditorState.Player
    Assert.Equal("Player 1", (Zone.tryTriggerAt zone state.Editor.Selection.Value).Value.State.Base.Name)

    // A rename frees the number for the next creation at that anchor.
    Selection.renameRef shared &state camera1 "Hall"
    Spawning.spawnTriggerAt &state EditorState.Camera
    Assert.Equal("Camera 1", (Zone.tryTriggerAt zone state.Editor.Selection.Value).Value.State.Base.Name)

    // Deleting a trigger frees its number too.
    Selection.deleteRef shared &state camera2
    Spawning.spawnTriggerAt &state EditorState.Camera
    Assert.Equal("Camera 2", (Zone.tryTriggerAt zone state.Editor.Selection.Value).Value.State.Base.Name)

[<Fact>]
let ``trigger creation at an anchor is refused at capacity and records nothing`` () =
    let zone = Zone.create ()
    for i in 1 .. int zone.Triggers.Capacity do
        do ignore (Zone.spawnTrigger zone (entity (sprintf "t%d" i)))
    let mutable state = gameplayState zone
    let before = zone.Triggers.Count

    Spawning.spawnTriggerAt &state EditorState.Origin

    Assert.Equal(before, zone.Triggers.Count)
    Assert.Empty(state.Editor.Undo.Applied)

[<Fact>]
let ``an actor spawned at the player uses her feet and rotation with unit scale`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let feet = Vector3(1.0f, 2.0f, 3.0f)
    let rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.75f)
    let baseActor = actor "Aiko"
    let player =
        { baseActor with
            State =
                { baseActor.State with
                    Base =
                        { baseActor.State.Base with
                            Transform = { transformIdentity with Position = feet; Rotation = rotation; Scale = Vector3(2.0f, 2.0f, 2.0f) } } } }
    let handle = zone.Actors.Add player
    zone.Player <- ValueSome handle
    let mutable state = gameplayState zone

    Spawning.spawnActorAt shared &state (Spawning.baseEntity "aiko") "aiko" EditorState.Player

    let ref = state.Editor.Selection.Value
    let spawned = (zone.Actors.Get(handleOf ref)).State
    Assert.Equal(EntityKind.Actor, ref.Kind)
    Assert.Equal("aiko", spawned.Base.Name)
    Assert.Equal(feet, spawned.Base.Transform.Position)
    Assert.Equal(rotation, spawned.Base.Transform.Rotation)
    Assert.Equal(Vector3.One, spawned.Base.Transform.Scale)
    Assert.Equal("aiko", spawned.AssetId)
    match state.Editor.Undo.Applied with
    | [ ActorCreate (_, r, _) ] -> Assert.Equal<EntityRef>(ref, r)
    | other -> Assert.Fail(sprintf "expected exactly one ActorCreate command, got %A" other)

    // The spawn is a one-shot placement: moving the player does not move the actor.
    let moved = &zone.Actors.Get handle
    moved <- { moved with State = { moved.State with Base = { moved.State.Base with Transform = { moved.State.Base.Transform with Position = Vector3(9.0f, 9.0f, 9.0f) } } } }
    Assert.Equal(feet, (zone.Actors.Get(handleOf ref)).State.Base.Transform.Position)

    do Undo.undoHistory shared &state
    Assert.False(Zone.isAlive zone ref)

    do Undo.redoHistory shared &state
    Assert.True(Zone.isAlive zone ref)

[<Fact>]
let ``an actor spawned at the camera faces the horizontal view and stays upright`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let camera = Camera.create (Vector3(4.0f, 5.0f, 6.0f)) (Vector3(5.0f, 5.0f, 6.0f))
    let mutable state = gameplayState zone
    state.Camera <- camera

    Spawning.spawnActorAt shared &state (Spawning.baseEntity "aiko") "aiko" EditorState.Camera
    let first = state.Editor.Selection.Value
    let transform = (zone.Actors.Get(handleOf first)).State.Base.Transform
    Assert.Equal(camera.Position, transform.Position)
    Assert.Equal(Vector3.One, transform.Scale)
    let forward = Vector3.Transform(Vector3.UnitZ, transform.Rotation)
    Assert.True(abs (forward.X - 1.0f) < 1e-5f && abs forward.Y < 1e-5f && abs forward.Z < 1e-5f, sprintf "expected +X facing, got %A" forward)

    // A vertical view has no horizontal direction: identity yaw.
    state.Camera <- Camera.create (Vector3(0.0f, 2.0f, 0.0f)) (Vector3(0.0f, 3.0f, 0.0f))
    Spawning.spawnActorAt shared &state (Spawning.baseEntity "aiko") "aiko" EditorState.Camera
    let vertical = (zone.Actors.Get(handleOf state.Editor.Selection.Value)).State.Base.Transform.Rotation
    Assert.Equal(Quaternion.Identity, vertical)

    // Moving the camera afterwards does not move the spawned actor.
    state.Camera <- Camera.create (Vector3(20.0f, 20.0f, 20.0f)) (Vector3(20.0f, 20.0f, 21.0f))
    Assert.Equal(camera.Position, (zone.Actors.Get(handleOf first)).State.Base.Transform.Position)

[<Fact>]
let ``an actor spawned at the origin keeps identity`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let mutable state = gameplayState zone

    Spawning.spawnActorAt shared &state (Spawning.baseEntity "aiko") "aiko" EditorState.Origin

    let transform = (zone.Actors.Get(handleOf state.Editor.Selection.Value)).State.Base.Transform
    Assert.Equal(Vector3.Zero, transform.Position)
    Assert.Equal(Quaternion.Identity, transform.Rotation)
    Assert.Equal(Vector3.One, transform.Scale)

[<Fact>]
let ``actor spawns refuse an unavailable player and a full map`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let mutable state = gameplayState zone

    Spawning.spawnActorAt shared &state (Spawning.baseEntity "aiko") "aiko" EditorState.Player
    Assert.Equal(0u, zone.Actors.Count)
    Assert.Empty(state.Editor.Undo.Applied)

    // A stale designation is no player either.
    zone.Player <- ValueSome { Index = 0u; Generation = 99u }
    Spawning.spawnActorAt shared &state (Spawning.baseEntity "aiko") "aiko" EditorState.Player
    Assert.Equal(0u, zone.Actors.Count)
    Assert.Empty(state.Editor.Undo.Applied)

    for i in 1 .. int zone.Actors.Capacity do
        do ignore (zone.Actors.Add(actor (sprintf "a%d" i)))
    Spawning.spawnActorAt shared &state (Spawning.baseEntity "aiko") "aiko" EditorState.Camera
    Assert.Equal(zone.Actors.Capacity, zone.Actors.Count)
    Assert.Empty(state.Editor.Undo.Applied)

[<Fact>]
let ``a prop spawned at the player uses her transform and keeps its asset`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let feet = Vector3(1.0f, 2.0f, 3.0f)
    let rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.75f)
    let baseActor = actor "Aiko"
    let player =
        { baseActor with
            State =
                { baseActor.State with
                    Base =
                        { baseActor.State.Base with
                            Transform = { transformIdentity with Position = feet; Rotation = rotation; Scale = Vector3(2.0f, 2.0f, 2.0f) } } } }
    let handle = zone.Actors.Add player
    zone.Player <- ValueSome handle
    let mutable state = gameplayState zone

    Spawning.spawnPropAt shared &state (Spawning.baseEntity "crate") "crate" EditorState.Player

    let ref = state.Editor.Selection.Value
    let prop = zone.Props.Get(handleOf ref)
    Assert.Equal(EntityKind.Prop, ref.Kind)
    Assert.Equal("crate", prop.State.Base.Name)
    Assert.Equal(feet, prop.State.Base.Transform.Position)
    Assert.Equal(rotation, prop.State.Base.Transform.Rotation)
    Assert.Equal(Vector3.One, prop.State.Base.Transform.Scale)
    Assert.Equal(Some "crate", prop.State.AssetId)
    Assert.True(Option.isSome prop.Physics)
    match state.Editor.Undo.Applied with
    | [ PropCreate (_, r, _) ] -> Assert.Equal<EntityRef>(ref, r)
    | other -> Assert.Fail(sprintf "expected exactly one PropCreate command, got %A" other)

    // The spawn is a one-shot placement: moving the player does not move the prop.
    let moved = &zone.Actors.Get handle
    moved <- { moved with State = { moved.State with Base = { moved.State.Base with Transform = { moved.State.Base.Transform with Position = Vector3(9.0f, 9.0f, 9.0f) } } } }
    Assert.Equal(feet, (zone.Props.Get(handleOf ref)).State.Base.Transform.Position)

    do Undo.undoHistory shared &state
    Assert.False(Zone.isAlive zone ref)

    do Undo.redoHistory shared &state
    Assert.True(Zone.isAlive zone ref)

[<Fact>]
let ``a prop spawned at the camera uses its position and horizontal yaw`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let camera = Camera.create (Vector3(4.0f, 5.0f, 6.0f)) (Vector3(5.0f, 5.0f, 6.0f))
    let mutable state = gameplayState zone
    state.Camera <- camera

    Spawning.spawnPropAt shared &state (Spawning.baseEntity "crate") "crate" EditorState.Camera
    let first = state.Editor.Selection.Value
    let transform = (zone.Props.Get(handleOf first)).State.Base.Transform
    Assert.Equal(camera.Position, transform.Position)
    Assert.Equal(Vector3.One, transform.Scale)
    let forward = Vector3.Transform(Vector3.UnitZ, transform.Rotation)
    Assert.True(abs (forward.X - 1.0f) < 1e-5f && abs forward.Y < 1e-5f && abs forward.Z < 1e-5f, sprintf "expected +X facing, got %A" forward)

    // A vertical view has no horizontal direction: identity yaw.
    state.Camera <- Camera.create (Vector3(0.0f, 2.0f, 0.0f)) (Vector3(0.0f, 3.0f, 0.0f))
    Spawning.spawnPropAt shared &state (Spawning.baseEntity "crate") "crate" EditorState.Camera
    let vertical = (zone.Props.Get(handleOf state.Editor.Selection.Value)).State.Base.Transform.Rotation
    Assert.Equal(Quaternion.Identity, vertical)

    // Moving the camera afterwards does not move the spawned prop.
    state.Camera <- Camera.create (Vector3(20.0f, 20.0f, 20.0f)) (Vector3(20.0f, 20.0f, 21.0f))
    Assert.Equal(camera.Position, (zone.Props.Get(handleOf first)).State.Base.Transform.Position)

[<Fact>]
let ``a prop spawned at the origin keeps identity`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let mutable state = gameplayState zone

    Spawning.spawnPropAt shared &state (Spawning.baseEntity "crate") "crate" EditorState.Origin

    let transform = (zone.Props.Get(handleOf state.Editor.Selection.Value)).State.Base.Transform
    Assert.Equal(Vector3.Zero, transform.Position)
    Assert.Equal(Quaternion.Identity, transform.Rotation)
    Assert.Equal(Vector3.One, transform.Scale)

[<Fact>]
let ``prop spawns refuse an unavailable player and a full map`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let mutable state = gameplayState zone

    Spawning.spawnPropAt shared &state (Spawning.baseEntity "crate") "crate" EditorState.Player
    Assert.Equal(0u, zone.Props.Count)
    Assert.Empty(state.Editor.Undo.Applied)

    // A stale designation is no player either.
    zone.Player <- ValueSome { Index = 0u; Generation = 99u }
    Spawning.spawnPropAt shared &state (Spawning.baseEntity "crate") "crate" EditorState.Player
    Assert.Equal(0u, zone.Props.Count)
    Assert.Empty(state.Editor.Undo.Applied)

    for i in 1 .. int zone.Props.Capacity do
        do ignore (zone.Props.Add(Unchecked.defaultof<Prop>))
    Spawning.spawnPropAt shared &state (Spawning.baseEntity "crate") "crate" EditorState.Camera
    Assert.Equal(zone.Props.Capacity, zone.Props.Count)
    Assert.Empty(state.Editor.Undo.Applied)

[<Fact>]
let ``duplicating a trigger copies its payload but not its runtime state`` () =
    let shared = bareShared ()
    let zone = Zone.create ()
    let source = Zone.spawnTrigger zone (entity "area")
    let payload = Zone.captureTrigger zone source
    do Zone.setTrigger zone source { payload with Radius = 4.0f; Prompt = "Open"; Interactable = true }
    // The flag is the trigger system's and nothing authors it through a command, so it is
    // set here straight on the slot: the copy must not pick it up.
    let slot = &zone.Triggers.Get(handleOf source)
    slot.Triggered <- true
    let mutable state = gameplayState zone

    Spawning.duplicate shared &state source (entity "area")

    Assert.Equal(2u, zone.Triggers.Count)
    let copy = state.Editor.Selection.Value
    let copied = (Zone.tryTriggerAt zone copy).Value
    Assert.NotEqual<EntityRef>(source, copy)
    Assert.Equal(4.0f, copied.State.Radius)
    Assert.Equal("Open", copied.State.Prompt)
    Assert.True(copied.State.Interactable)
    // The runtime flag is the next update's to recompute, so it is not copied.
    Assert.False(copied.Triggered)

    match state.Editor.Undo.Applied with
    | [ TriggerCreate (_, r, _) ] -> Assert.Equal<EntityRef>(copy, r)
    | other -> Assert.Fail(sprintf "expected exactly one TriggerCreate command, got %A" other)

[<Fact>]
let ``deleting a trigger and undoing restores its authored shape`` () =
    let shared = bareShared ()
    let zone = Zone.create ()
    let ref = Zone.spawnTrigger zone (entity "area")
    let payload = Zone.captureTrigger zone ref
    do Zone.setTrigger zone ref { payload with Radius = 5.0f; Height = 9.0f; Prompt = "Here"; Interactable = true }
    let mutable state = gameplayState zone

    Selection.deleteRef shared &state ref
    Assert.True(ValueOption.isNone (Zone.tryTriggerAt zone ref))

    // The delete records the whole payload, because a trigger's shape is not carried by
    // the base entity the shared Delete command stores.
    match state.Editor.Undo.Applied with
    | [ TriggerDelete (_, _, recorded) ] -> Assert.Equal(5.0f, recorded.Radius)
    | other -> Assert.Fail(sprintf "expected exactly one TriggerDelete command, got %A" other)

    do Undo.undoHistory shared &state

    let restored = (Zone.tryTriggerAt zone ref).Value
    Assert.Equal(5.0f, restored.State.Radius)
    Assert.Equal(9.0f, restored.State.Height)
    Assert.Equal("Here", restored.State.Prompt)
    Assert.True(restored.State.Interactable)

[<Fact>]
let ``duplicating a kind with no entity fails loudly`` () =
    let shared = bareShared ()
    let zone = Zone.create ()
    let mutable state = gameplayState zone

    for kind in [ EntityKind.Environment ] do
        let source : EntityRef = { Kind = kind; Index = 0u; Generation = 0u; Epoch = 0u }
        let mutable message = ""
        try
            Spawning.duplicate shared &state source (entity "ghost")
        with ex ->
            message <- ex.Message
        Assert.Contains("is not an addressable entity", message)

// ── Restore onto an occupied slot ──

[<Fact>]
let ``restoring onto an occupied slot reports the reused slot instead of clobbering it`` () =
    let assets = headlessAssets ()
    let zone = Zone.create ()
    let world = Physics.create ()
    let environment = Zone.loadEnvironment zone world assets "room"
    // The part's slot is live, so restoring a recorded ref that names it is a bug.
    let ref = partRef environment.Epoch 0u 0u
    let record = Zone.capturePart zone ref

    let mutable message = ""
    try
        Zone.occupyPart zone world assets ref record
    with ex ->
        message <- ex.Message

    Assert.Contains("slot 0 of ref", message)
    Assert.Contains("is occupied", message)
    Assert.Contains("reused by an untracked Add", message)
    Assert.Equal("wall", (Zone.tryEntityAt zone ref).Value.Name)

// ── Staging a zone from a template (spec 035; the restager is `GameplayState.stageZone`, spec 079) ──
//
// These drive the real restage path: a zone's contents are recorded as a `ZoneTemplate`, the zone moves on, and
// one `stageZone` puts the recorded ones back. (`Spawning.instantiateZone` retired with the Zone Registry; its
// undoable form is `Command.ZoneStage`, covered in `ScenarioForkTests`.)

[<Fact>]
let ``stageZone replaces the zone's props with the template's`` () =
    let assets = headlessAssets ()
    let zone = Zone.create ()
    let shared = sharedState assets
    let mutable state = gameplayState zone

    // A zone as an author leaves it: an environment loaded, and two props standing in it, one moved.
    do Zone.loadEnvironment zone state.Physics assets "room" |> ignore
    let lectern = Zone.spawnProp zone state.Physics assets (entity "lectern") "crate"
    let books = Zone.spawnProp zone state.Physics assets (entity "books") "crate"
    do Zone.setTransform zone state.Physics lectern { transformIdentity with Position = Vector3(1.0f, 0.0f, 0.0f) }
    let registered = (Zone.captureTemplate zone).Value
    let previousEnvironment = zone.Environment.Value
    Assert.Equal(2, registered.Props.Length)

    // The zone moves on: both props gone, one of its own in their place, and a waypoint and a trigger
    // standing there that an instantiation must leave alone.
    do Zone.removeProp zone state.Physics lectern
    do Zone.removeProp zone state.Physics books
    let desk = Zone.spawnProp zone state.Physics assets (entity "desk") "crate"
    let marker = Zone.spawnWaypoint zone (entity "marker")
    let door = Zone.spawnTrigger zone (entity "door")
    Assert.Equal(1u, zone.Props.Count)

    do Zone.setName zone desk "desk of my own"

    // One gesture: stage the zone from the template.
    do GameplayState.stageZone shared &state GameplayState.startZone (ZoneTemplate.ofTemplate registered)

    // The zone's props are the entry's and nothing else: same count, same names, same placement — and
    // the desk it held is gone rather than left standing beside them.
    let after = (Zone.captureTemplate zone).Value
    Assert.Equal(2, after.Props.Length)
    Assert.Equal<string list>(
        [ "books"; "lectern" ],
        after.Props |> Array.map (fun (prop: PropState) -> prop.Base.Name) |> Array.toList |> List.sort)
    let placed : PropState = Array.find (fun (prop: PropState) -> prop.Base.Name = "lectern") after.Props
    Assert.Equal(Vector3(1.0f, 0.0f, 0.0f), placed.Base.Transform.Position)
    Assert.DoesNotContain("desk", after.Props |> Array.map (fun (prop: PropState) -> prop.Base.Name))
    // The entry is unchanged, and the zone records what it recorded: the same asset, settings, parts and
    // props. The props are compared sorted by name because the order they come back in is not part of
    // the record — the slots they land in are the free list's business, and here they land in the order
    // the replaced ones were freed in, which is the reverse of the order they were placed.
    let ignoringOrder (template: ZoneTemplate.ZoneTemplate) =
        { template with Props = Array.sortBy (fun (prop: PropState) -> prop.Base.Name) template.Props }
    Assert.Equal(ignoringOrder registered, ignoringOrder after)
    // And nothing else in the zone moved.
    Assert.True(Zone.isAlive zone marker)
    Assert.True(Zone.isAlive zone door)

[<Fact>]
let ``a template that records no props clears the zone's`` () =
    let assets = headlessAssets ()
    let zone = Zone.create ()
    let shared = sharedState assets
    let mutable state = gameplayState zone

    // Registered from a zone with nothing placed in it: a template with no props is complete, not partial.
    do Zone.loadEnvironment zone state.Physics assets "room" |> ignore
    let registered = (Zone.captureTemplate zone).Value
    Assert.Empty(registered.Props)

    // The zone is set out afterwards, then staged from the bare template: the template is the whole of what
    // the zone's props become, so they are cleared rather than left standing.
    let lectern = Zone.spawnProp zone state.Physics assets (entity "lectern") "crate"
    do GameplayState.stageZone shared &state GameplayState.startZone (ZoneTemplate.ofTemplate registered)
    Assert.Equal(0u, zone.Props.Count)
    ignore lectern

[<Fact>]
let ``stageZone restores the template's light`` () =
    let assets = headlessAssets ()
    let zone = Zone.create ()
    let shared = sharedState assets
    let mutable state = gameplayState zone

    // An authored light, recorded with the zone (spec 043 FR-007).
    let authored = { Texture.defaultLight with Pitch = 20.0f; Yaw = 200.0f; Ambient = 0.9f }
    zone.Light <- authored
    do Zone.loadEnvironment zone state.Physics assets "room" |> ignore
    let registered = (Zone.captureTemplate zone).Value
    Assert.Equal(authored, registered.Light)

    // The zone moves on: a different light, then staged back from the template.
    let moved = { authored with Pitch = 80.0f; Yaw = 10.0f }
    zone.Light <- moved
    do GameplayState.stageZone shared &state GameplayState.startZone (ZoneTemplate.ofTemplate registered)
    Assert.Equal(authored, zone.Light)
