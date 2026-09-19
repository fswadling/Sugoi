module SpawningTests

// Drive the real `Spawning`/`Zone` paths through a headless asset manager (the
// prop/part render path never touches the GPU device), so command pairing and kind
// dispatch are covered for real rather than mocked.

open Xunit
open System
open System.Collections.Generic
open System.Numerics
open Command
open Entity
open EnvironmentAsset
open Gltf
open Physics

let private entity (name: string) : Entity =
    { Name = name
      Transform = transformIdentity
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

// ── Headless asset fixture ──
// Only the fields the collider/render paths read are populated; a zero Device is
// safe because `EnvironmentRender.create` never dereferences it.

let private bounds (mn: Vector3) (mx: Vector3) : Gltf.Bounds =
    { Min = mn; Max = mx }

let private assetSubmesh (positions: float32 array) (indices: int array) : StaticSubmesh =
    { Mesh = Unchecked.defaultof<StaticMesh.StaticMesh>
      Material = Unchecked.defaultof<Texture.Material>
      Positions = positions
      Indices = indices }

let private assetPart (submeshIndex: int) (submeshCount: int) : EnvironmentPart =
    { Name = "wall"
      HierarchyPath = "wall"
      World = Matrix4x4.Identity
      Bounds = bounds Vector3.Zero Vector3.One
      SubmeshIndex = submeshIndex
      SubmeshCount = submeshCount }

// A one-triangle room whose single part covers submesh 0.
let private roomAsset () : EnvironmentAsset.EnvironmentAsset =
    { Id = "room"
      Name = "room"
      ModelPath = ""
      Orientation = Quaternion.Identity
      Bounds = bounds Vector3.Zero Vector3.One
      Submeshes = [| assetSubmesh [| 0.0f; 0.0f; 0.0f;  1.0f; 0.0f; 0.0f;  0.0f; 1.0f; 0.0f |] [| 0; 1; 2 |] |]
      Parts = [| assetPart 0 1 |] }

let private headlessAssets () : Assets.AssetManager =
    let environments = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    environments.["room"] <- roomAsset ()
    { Device = IntPtr.Zero
      Actors = Dictionary()
      Props = Dictionary()
      Environments = environments }

let private sharedState (assets: Assets.AssetManager) : SharedState.SharedState =
    { Gpu = Unchecked.defaultof<Gpu.Gpu>; Assets = assets; Physics = Physics.create () }

// A minimal gameplay state: the fake draws are never touched on these paths. The
// zone is installed under a name with exactly one active zone.
let private gameplayState (zone: Zone.Zone) : GameplayState.GameplayState =
    let zones = Dictionary<string, Zone.Zone>()
    zones.["test"] <- zone
    { Camera = Camera.create Vector3.Zero Vector3.Zero
      DebugDraw = Unchecked.defaultof<DebugDraw.DebugDraw>
      GizmoDraw = Unchecked.defaultof<DebugDraw.DebugDraw>
      Zones = zones
      ActiveZone = "test"
      Editor = EditorState.create ()
      Editing = true
      Follow = Systems.createFollow ()
      UndoByZone = Dictionary() }

// ── Duplicate ──

[<Fact>]
let ``duplicating a part spawns a copy, selects it, and pushes the reversing command`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let environment = Zone.loadEnvironment zone shared.Physics assets "room"
    let source = partRef environment.Epoch 0u 0u
    let mutable state = gameplayState zone

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
    | Duplicate (r, _, _) :: _ -> Assert.Equal(state.Editor.Selection.Value, r)
    | other -> Assert.Fail(sprintf "expected a Duplicate command, got %A" other)

[<Fact>]
let ``duplicating a waypoint spawns a copy, selects it, and pushes the reversing command`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
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
    | Duplicate (r, _, _) :: _ -> Assert.Equal(state.Editor.Selection.Value, r)
    | other -> Assert.Fail(sprintf "expected a Duplicate command, got %A" other)

[<Fact>]
let ``deleting a waypoint and undoing restores its transform at the recorded slot`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
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
    let undo = UndoStack.undo state.Editor.Undo zone shared.Physics shared.Assets
    state.Editor <- { state.Editor with Undo = undo }

    Assert.True(Zone.isAlive zone ref)
    Assert.Equal(moved.Position, (Zone.tryEntityAt zone ref).Value.Transform.Position)
    Assert.Equal(moved.Rotation, (Zone.tryEntityAt zone ref).Value.Transform.Rotation)

[<Fact>]
let ``adding a next waypoint links it and undoes as one step`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let a = Zone.spawnWaypoint zone (entity "a")
    let mutable state = gameplayState zone

    Spawning.spawnLinkedWaypoint &state a (entity "b")

    Assert.Equal(2u, zone.Waypoints.Count)
    let b = state.Editor.Selection.Value
    Assert.Equal(EntityKind.Waypoint, b.Kind)
    Assert.Equal("b", (Zone.tryEntityAt zone b).Value.Name)
    Assert.Equal(Some b, Zone.waypointNext zone a)

    // The spawn and the link are one command, naming the predecessor and the new waypoint.
    match state.Editor.Undo.Applied with
    | [ LinkSpawn (from, None, r, _) ] ->
        Assert.Equal<EntityRef>(a, from)
        Assert.Equal<EntityRef>(b, r)
    | other -> Assert.Fail(sprintf "expected exactly one LinkSpawn command, got %A" other)

    // One undo removes the new waypoint and restores the chain end it extended.
    let undo = UndoStack.undo state.Editor.Undo zone shared.Physics shared.Assets
    state.Editor <- { state.Editor with Undo = undo }
    Assert.Equal(1u, zone.Waypoints.Count)
    Assert.True(Option.isNone (Zone.waypointNext zone a))

    // Redo reapplies both halves at the recorded ref.
    let redo = UndoStack.redo state.Editor.Undo zone shared.Physics shared.Assets
    state.Editor <- { state.Editor with Undo = redo }
    Assert.Equal(2u, zone.Waypoints.Count)
    Assert.Equal(Some b, Zone.waypointNext zone a)

[<Fact>]
let ``extending a waypoint that already has a next restores the previous one on undo`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let a = Zone.spawnWaypoint zone (entity "a")
    let original = Zone.spawnWaypoint zone (entity "original")
    do Zone.setWaypointNext zone a (Some original)
    let mutable state = gameplayState zone

    Spawning.spawnLinkedWaypoint &state a (entity "b")

    Assert.Equal(3u, zone.Waypoints.Count)
    let b = state.Editor.Selection.Value
    Assert.Equal(Some b, Zone.waypointNext zone a)

    // Undo puts the previous next back, not none.
    let undo = UndoStack.undo state.Editor.Undo zone shared.Physics shared.Assets
    state.Editor <- { state.Editor with Undo = undo }
    Assert.Equal(Some original, Zone.waypointNext zone a)
    Assert.True(Option.isNone (Zone.tryEntityAt zone b))

[<Fact>]
let ``a duplicated waypoint starts its own chain`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let a = Zone.spawnWaypoint zone (entity "a")
    let b = Zone.spawnWaypoint zone (entity "b")
    do Zone.setWaypointNext zone a (Some b)
    let mutable state = gameplayState zone

    Spawning.duplicate shared &state a (entity "a-copy")

    // The copy carries no link — a link belongs to the waypoint it is set on — and the
    // original's chain is untouched.
    let copy = state.Editor.Selection.Value
    Assert.NotEqual<EntityRef>(a, copy)
    Assert.True(Option.isNone (Zone.waypointNext zone copy))
    Assert.Equal(Some b, Zone.waypointNext zone a)

[<Fact>]
let ``duplicating a kind with no entity fails loudly`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let mutable state = gameplayState zone

    for kind in [ EntityKind.Environment; EntityKind.Trigger ] do
        let source : EntityRef = { Kind = kind; Index = 0u; Generation = 0u; Epoch = 0u }
        let mutable message = ""
        try
            Spawning.duplicate shared &state source (entity "ghost")
        with ex ->
            message <- ex.Message
        Assert.Contains("has no entity to duplicate", message)

// ── Restore onto an occupied slot ──

[<Fact>]
let ``restoring onto an occupied slot reports the reused slot instead of clobbering it`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
    let zone = Zone.create ()
    let environment = Zone.loadEnvironment zone shared.Physics assets "room"
    // The part's slot is live, so restoring a recorded ref that names it is a bug.
    let ref = partRef environment.Epoch 0u 0u
    let binding = Zone.bindingAt zone ref

    let mutable message = ""
    try
        Zone.setActorWithAsset zone shared.Physics assets ref (entity "restored") binding
    with ex ->
        message <- ex.Message

    Assert.Contains("slot 0 of ref", message)
    Assert.Contains("is occupied", message)
    Assert.Contains("reused by an untracked Add", message)
    Assert.Equal("wall", (Zone.tryEntityAt zone ref).Value.Name)
