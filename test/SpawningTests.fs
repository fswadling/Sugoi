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
open TestSupport

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

// A one-triangle room whose single part covers submesh 0.
let private roomAsset () : EnvironmentAsset.EnvironmentAsset =
    residentAsset "room" [| loadedSubmesh [| 0.0f; 0.0f; 0.0f;  1.0f; 0.0f; 0.0f;  0.0f; 1.0f; 0.0f |] [| 0; 1; 2 |] |] [| TestSupport.part "wall" 0 1 |]

// A whole-model placed prop asset, so a created prop builds a mesh collider and a body.
let private propAsset () : EnvironmentAsset.EnvironmentAsset =
    residentAsset "crate" [| loadedSubmesh [| 0.0f; 0.0f; 0.0f;  1.0f; 0.0f; 0.0f;  0.0f; 1.0f; 0.0f |] [| 0; 1; 2 |] |] [||]

let private headlessAssets () : Assets.AssetManager =
    let environments = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    environments.["room"] <- roomAsset ()
    let props = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    props.["crate"] <- propAsset ()
    TestSupport.manager IntPtr.Zero (Dictionary()) props environments

let private sharedState (assets: Assets.AssetManager) : SharedState.SharedState =
    { Gpu = Unchecked.defaultof<Gpu.Gpu>; Assets = assets; Advents = Set.empty }

// A minimal gameplay state: the fake draws are never touched on these paths. The
// zone is installed under a name with exactly one active zone.
let private gameplayState (zone: Zone.Zone) : GameplayState.GameplayState =
    let zones = Dictionary<string, Zone.Zone>()
    zones.[GameplayState.startZone] <- zone
    { Camera = Camera.create Vector3.Zero Vector3.Zero
      DebugDraw = Unchecked.defaultof<DebugDraw.DebugDraw>
      GizmoDraw = Unchecked.defaultof<DebugDraw.DebugDraw>
      Ui = Unchecked.defaultof<Ui.Ui>
      Dialogue = Dialogue.empty
      Zones = zones
      Scenarios = ScenarioRegistry.create ()
      Physics = Physics.create ()
      ActiveZone = GameplayState.startZone
      Editor = EditorState.create ()
      Editing = true
      Follow = Systems.createFollow ()
      InteractableTrigger = ValueNone
      Cue = Cue.Fin
      PlayTime = 0.0
      Fade = 0.0f }

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
    let undone = UndoStack.undo state.Editor.Undo zone state.Physics shared.Assets
    Assert.False(Zone.isAlive zone ref)

    let redone = UndoStack.redo undone zone state.Physics shared.Assets
    state.Editor <- { state.Editor with Undo = redone }

    Assert.True(Zone.isAlive zone ref)
    Assert.Equal(live, (zone.Props.Get(handleOf ref)).State)
    Assert.True(Option.isSome (zone.Props.Get(handleOf ref)).Physics)

[<Fact>]
let ``a create is refused when the kind's map is at capacity`` () =
    // spec 080, US5: filling a kind to its fixed capacity must refuse the next create rather than let
    // `SlotMap.Add` raise. Triggers cap at 32; fill the map directly, then create through `Spawning`.
    let assets = headlessAssets ()
    let shared = sharedState assets
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
    | WaypointCreate (_, r, _) :: _ -> Assert.Equal(state.Editor.Selection.Value, r)
    | other -> Assert.Fail(sprintf "expected a WaypointCreate command, got %A" other)

[<Fact>]
let ``a duplicate records the copy's own authored value, so redo restores the copy`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
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

    let undone = UndoStack.undo state.Editor.Undo zone state.Physics shared.Assets
    Assert.False(Zone.isAlive zone copy)

    let redone = UndoStack.redo undone zone state.Physics shared.Assets
    state.Editor <- { state.Editor with Undo = redone }

    Assert.True(Zone.isAlive zone copy)
    Assert.Equal("start-copy", (Zone.tryEntityAt zone copy).Value.Name)

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
    let undo = UndoStack.undo state.Editor.Undo zone state.Physics shared.Assets
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
    | [ LinkSpawn (_, from, None, r, _) ] ->
        Assert.Equal<EntityRef>(a, from)
        Assert.Equal<EntityRef>(b, r)
    | other -> Assert.Fail(sprintf "expected exactly one LinkSpawn command, got %A" other)

    // One undo removes the new waypoint and restores the chain end it extended.
    let undo = UndoStack.undo state.Editor.Undo zone state.Physics shared.Assets
    state.Editor <- { state.Editor with Undo = undo }
    Assert.Equal(1u, zone.Waypoints.Count)
    Assert.True(Option.isNone (Zone.waypointNext zone a))

    // Redo reapplies both halves at the recorded ref.
    let redo = UndoStack.redo state.Editor.Undo zone state.Physics shared.Assets
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
    let undo = UndoStack.undo state.Editor.Undo zone state.Physics shared.Assets
    state.Editor <- { state.Editor with Undo = undo }
    Assert.Equal(Some original, Zone.waypointNext zone a)
    Assert.True(Option.isNone (Zone.tryEntityAt zone b))

[<Fact>]
let ``a duplicated waypoint carries the link to the same successor`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
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
let ``duplicating a trigger copies its payload but not its runtime state`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
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
    let assets = headlessAssets ()
    let shared = sharedState assets
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

    let undo = UndoStack.undo state.Editor.Undo zone state.Physics shared.Assets
    state.Editor <- { state.Editor with Undo = undo }

    let restored = (Zone.tryTriggerAt zone ref).Value
    Assert.Equal(5.0f, restored.State.Radius)
    Assert.Equal(9.0f, restored.State.Height)
    Assert.Equal("Here", restored.State.Prompt)
    Assert.True(restored.State.Interactable)

[<Fact>]
let ``duplicating a kind with no entity fails loudly`` () =
    let assets = headlessAssets ()
    let shared = sharedState assets
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
