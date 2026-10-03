module SelectionTests

// `Selection.renameRef` through the real command/undo path on a headless gameplay
// state. A rename is a scene edit, so what matters is the one command it records and
// what undo does with it — plus the cases it must refuse (an empty name) or ignore
// (an unchanged name, a ref that no longer resolves).

open Xunit
open System
open System.Collections.Generic
open System.Numerics
open Command
open Entity

let private entity (name: string) : Entity =
    { Name = name
      Transform = transformIdentity
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

// A device-free shared state: a rename touches no asset and no body, so empty tables
// and a bare physics world are enough.
let private sharedState () : SharedState.SharedState =
    { Gpu = Unchecked.defaultof<Gpu.Gpu>
      Assets = TestSupport.manager IntPtr.Zero (Dictionary()) (Dictionary()) (Dictionary())
      Cast = { Casting.Bindings = Dictionary() }
      ZoneRegistry = ZoneRegistry.create ()
      Physics = Physics.create () }

let private gameplayState (zone: Zone.Zone) : GameplayState.GameplayState =
    let zones = Dictionary<string, Zone.Zone>()
    zones.["test"] <- zone
    { Camera = Camera.create Vector3.Zero Vector3.Zero
      DebugDraw = Unchecked.defaultof<DebugDraw.DebugDraw>
      GizmoDraw = Unchecked.defaultof<DebugDraw.DebugDraw>
      Ui = Unchecked.defaultof<Ui.Ui>
      Dialogue = Dialogue.empty
      Zones = zones
      ActiveZone = "test"
      Editor = EditorState.create ()
      Editing = true
      Follow = Systems.createFollow ()
      InteractableTrigger = ValueNone
      Cue = Cue.Fin
      PlayTime = 0.0 }

// ── Rename ──

[<Fact>]
let ``renaming records one Rename step and undo and redo swap the names`` () =
    let shared = sharedState ()
    let zone = Zone.create ()
    let ref = Zone.spawnWaypoint zone (entity "start")
    let mutable state = gameplayState zone

    Selection.renameRef shared &state ref "entrance"

    Assert.Equal("entrance", (Zone.tryEntityAt zone ref).Value.Name)
    // Exactly one step, carrying both sides of the change.
    match state.Editor.Undo.Applied with
    | [ Rename (r, before, after) ] ->
        Assert.Equal<EntityRef>(ref, r)
        Assert.Equal("start", before)
        Assert.Equal("entrance", after)
    | other -> Assert.Fail(sprintf "expected exactly one Rename command, got %A" other)

    let undo = UndoStack.undo state.Editor.Undo zone shared.Physics shared.Assets
    state.Editor <- { state.Editor with Undo = undo }
    Assert.Equal("start", (Zone.tryEntityAt zone ref).Value.Name)

    let redo = UndoStack.redo state.Editor.Undo zone shared.Physics shared.Assets
    state.Editor <- { state.Editor with Undo = redo }
    Assert.Equal("entrance", (Zone.tryEntityAt zone ref).Value.Name)

[<Fact>]
let ``consecutive renames are separate undo steps and leave the selection alone`` () =
    let shared = sharedState ()
    let zone = Zone.create ()
    let ref = Zone.spawnWaypoint zone (entity "start")
    let mutable state = gameplayState zone
    // A different entity is selected while the two renames happen.
    let selected = Zone.spawnWaypoint zone (entity "other")
    state.Editor <- { state.Editor with Selection = Some selected }

    Selection.renameRef shared &state ref "one"
    Selection.renameRef shared &state ref "two"

    // Most recent first, and not folded into one another (only transforms merge).
    match state.Editor.Undo.Applied with
    | [ Rename (_, "one", "two"); Rename (_, "start", "one") ] -> ()
    | other -> Assert.Fail(sprintf "expected two separate Rename commands, got %A" other)
    Assert.Equal(Some selected, state.Editor.Selection)

[<Fact>]
let ``a rename trims the name it commits`` () =
    let shared = sharedState ()
    let zone = Zone.create ()
    let ref = Zone.spawnWaypoint zone (entity "start")
    let mutable state = gameplayState zone

    Selection.renameRef shared &state ref "  entrance  "

    Assert.Equal("entrance", (Zone.tryEntityAt zone ref).Value.Name)

// ── The commit decision (pure) ──

[<Fact>]
let ``a finished edit commits to the entity it was typed for, not the selection`` () =
    let typedFor : EntityRef = { Kind = EntityKind.Waypoint; Index = 3u; Generation = 1u; Epoch = 0u }

    // The selection may have moved on, but the text belongs to `typedFor`.
    match Selection.renameTarget (Some typedFor) true "entrance" with
    | Some (target, name) ->
        Assert.Equal<EntityRef>(typedFor, target)
        Assert.Equal("entrance", name)
    | None -> Assert.Fail("expected the finished edit to commit")

[<Fact>]
let ``an unfinished, ownerless, or empty edit commits nothing`` () =
    let owner : EntityRef = { Kind = EntityKind.Waypoint; Index = 3u; Generation = 1u; Epoch = 0u }

    // Still typing: the field has neither been submitted nor left.
    Assert.True(Option.isNone (Selection.renameTarget (Some owner) false "entrance"))
    // Nothing was being edited.
    Assert.True(Option.isNone (Selection.renameTarget None true "entrance"))
    // An empty or whitespace-only edit is not a name.
    Assert.True(Option.isNone (Selection.renameTarget (Some owner) true ""))
    Assert.True(Option.isNone (Selection.renameTarget (Some owner) true "   "))

[<Fact>]
let ``a committed name is trimmed`` () =
    let owner : EntityRef = { Kind = EntityKind.Waypoint; Index = 3u; Generation = 1u; Epoch = 0u }

    Assert.Equal(Some (owner, "entrance"), Selection.renameTarget (Some owner) true "  entrance  ")

// ── Refusals and no-ops ──

[<Fact>]
let ``an empty or whitespace-only name is refused`` () =
    let shared = sharedState ()
    let zone = Zone.create ()
    let ref = Zone.spawnWaypoint zone (entity "start")
    let mutable state = gameplayState zone

    for name in [ ""; "   "; "\t" ] do
        let mutable message = ""
        try
            Selection.renameRef shared &state ref name
        with ex ->
            message <- ex.Message
        Assert.Contains("the name is empty", message)

    Assert.Equal("start", (Zone.tryEntityAt zone ref).Value.Name)
    Assert.Empty(state.Editor.Undo.Applied)

[<Fact>]
let ``renaming to the name the entity already has records nothing`` () =
    let shared = sharedState ()
    let zone = Zone.create ()
    let ref = Zone.spawnWaypoint zone (entity "start")
    let mutable state = gameplayState zone

    Selection.renameRef shared &state ref "start"

    Assert.Equal("start", (Zone.tryEntityAt zone ref).Value.Name)
    Assert.Empty(state.Editor.Undo.Applied)

[<Fact>]
let ``renaming a ref that no longer resolves changes and records nothing`` () =
    let shared = sharedState ()
    let zone = Zone.create ()
    let ref = Zone.spawnWaypoint zone (entity "start")
    let mutable state = gameplayState zone

    Zone.removeWaypoint zone ref

    Selection.renameRef shared &state ref "ghost"

    // Nothing is resurrected, nothing is created, and no step is recorded.
    Assert.True(Option.isNone (Zone.tryEntityAt zone ref))
    Assert.Equal(0u, zone.Waypoints.Count)
    Assert.Empty(state.Editor.Undo.Applied)

// ── Chain links ──

[<Fact>]
let ``linking pushes one Link step carrying both sides, and undo restores the previous`` () =
    let shared = sharedState ()
    let zone = Zone.create ()
    let a = Zone.spawnWaypoint zone (entity "a")
    let b = Zone.spawnWaypoint zone (entity "b")
    let c = Zone.spawnWaypoint zone (entity "c")
    let mutable state = gameplayState zone

    Selection.linkRef shared &state a (Some b)

    Assert.Equal(Some b, Zone.waypointNext zone a)
    match state.Editor.Undo.Applied with
    | [ Link (r, None, Some after) ] ->
        Assert.Equal<EntityRef>(a, r)
        Assert.Equal<EntityRef>(b, after)
    | other -> Assert.Fail(sprintf "expected exactly one Link command, got %A" other)

    // Retargeting carries the previous next, so undo restores b rather than nothing.
    Selection.linkRef shared &state a (Some c)
    Assert.Equal(Some c, Zone.waypointNext zone a)

    let undo = UndoStack.undo state.Editor.Undo zone shared.Physics shared.Assets
    state.Editor <- { state.Editor with Undo = undo }
    Assert.Equal(Some b, Zone.waypointNext zone a)

[<Fact>]
let ``unlinking is one step and linking to the current next records nothing`` () =
    let shared = sharedState ()
    let zone = Zone.create ()
    let a = Zone.spawnWaypoint zone (entity "a")
    let b = Zone.spawnWaypoint zone (entity "b")
    let mutable state = gameplayState zone

    Selection.linkRef shared &state a (Some b)
    // Already pointing there: no second step.
    Selection.linkRef shared &state a (Some b)
    Assert.Equal(1, state.Editor.Undo.Applied.Length)

    Selection.linkRef shared &state a None
    Assert.True(Option.isNone (Zone.waypointNext zone a))
    Assert.Equal(2, state.Editor.Undo.Applied.Length)

[<Fact>]
let ``deleting a mid-chain waypoint and undoing restores the whole chain`` () =
    let shared = sharedState ()
    let zone = Zone.create ()
    let a = Zone.spawnWaypoint zone (entity "a")
    let b = Zone.spawnWaypoint zone (entity "b")
    let c = Zone.spawnWaypoint zone (entity "c")
    do Zone.setWaypointNext zone a (Some b)
    do Zone.setWaypointNext zone b (Some c)
    let mutable state = gameplayState zone

    Selection.deleteRef shared &state b

    // The chain stops where the deleted waypoint was.
    Assert.Equal(2u, zone.Waypoints.Count)
    Assert.True(Option.isNone (Zone.waypointNext zone a))

    let undo = UndoStack.undo state.Editor.Undo zone shared.Physics shared.Assets
    state.Editor <- { state.Editor with Undo = undo }

    // Both links resolve again: the restored waypoint re-occupied its slot with the
    // generation the links recorded, and its own link came back with the record the
    // delete carried.
    Assert.True(Zone.isAlive zone b)
    Assert.Equal(Some b, Zone.waypointNext zone a)
    Assert.Equal(Some c, Zone.waypointNext zone b)

// ── Recorded state through the editor paths ──

[<Fact>]
let ``deleting a waypoint and undoing restores every authored field`` () =
    let shared = sharedState ()
    let zone = Zone.create ()
    let moved = { transformIdentity with Position = Vector3(1.0f, 2.0f, 3.0f) }
    let ref = Zone.spawnWaypoint zone { entity "start" with Transform = moved }
    let next = Zone.spawnWaypoint zone (entity "next")
    do Zone.setWaypointNext zone ref (Some next)
    let mutable state = gameplayState zone

    Selection.deleteRef shared &state ref
    Assert.False(Zone.isAlive zone ref)

    let undo = UndoStack.undo state.Editor.Undo zone shared.Physics shared.Assets
    state.Editor <- { state.Editor with Undo = undo }

    // Name, transform, and the link are all back; none of them was reset to a default.
    let restored = (Zone.tryEntityAt zone ref).Value
    Assert.Equal("start", restored.Name)
    Assert.Equal(moved, restored.Transform)
    Assert.Equal(Some next, Zone.waypointNext zone ref)

[<Fact>]
let ``deleting a trigger and undoing restores its shape but not its runtime flag`` () =
    let shared = sharedState ()
    let zone = Zone.create ()
    let ref = Zone.spawnTrigger zone (entity "area")
    let payload = Zone.captureTrigger zone ref
    do Zone.setTrigger zone ref { payload with Radius = 3.0f; Height = 4.0f; Interactable = true; Prompt = "Open" }
    // The flag is the trigger system's; nothing authors it through a command.
    let slot = &zone.Triggers.Get(handleOf ref)
    slot.Triggered <- true
    let mutable state = gameplayState zone

    Selection.deleteRef shared &state ref

    let undo = UndoStack.undo state.Editor.Undo zone shared.Physics shared.Assets
    state.Editor <- { state.Editor with Undo = undo }

    let restored = (Zone.tryTriggerAt zone ref).Value
    Assert.Equal(3.0f, restored.State.Radius)
    Assert.Equal(4.0f, restored.State.Height)
    Assert.Equal("Open", restored.State.Prompt)
    Assert.True(restored.State.Interactable)
    // Runtime state is the trigger system's to recompute.
    Assert.False(restored.Triggered)

[<Fact>]
let ``repeating delete, undo, and redo leaves the entity identical`` () =
    let shared = sharedState ()
    let zone = Zone.create ()
    let ref = Zone.spawnWaypoint zone (entity "start")
    let mutable state = gameplayState zone

    for _ in 1 .. 50 do
        Selection.deleteRef shared &state ref
        Assert.False(Zone.isAlive zone ref)
        let undo = UndoStack.undo state.Editor.Undo zone shared.Physics shared.Assets
        let redo = UndoStack.redo undo zone shared.Physics shared.Assets
        let undone = UndoStack.undo redo zone shared.Physics shared.Assets
        state.Editor <- { state.Editor with Undo = undone }

    // Fifty cycles leave exactly the entity the first one did, with no drift.
    Assert.True(Zone.isAlive zone ref)
    Assert.Equal("start", (Zone.tryEntityAt zone ref).Value.Name)
    Assert.Equal(1u, zone.Waypoints.Count)

[<Fact>]
let ``the collider overlay is recorded with the actor and is not an undo step`` () =
    let zone = Zone.create ()
    // Added straight to the slot map: an actor's renderer needs a GPU device both to build
    // and to release, so this test stops at what a delete records. Undoing an actor delete
    // is covered by the interactive walk in the quickstart.
    let handle =
        zone.Actors.Add({ State =
                            { Base = entity "hero"
                              AssetId = "character"
                              ShowCollider = false }
                          Physics = None
                          Renderer = Unchecked.defaultof<ActorRender.ActorRender> })
    let ref = actorRef handle.Index handle.Generation
    let mutable state = gameplayState zone

    do Zone.setShowCollider zone ref true

    // The overlay is a visualisation preference rather than a scene edit, so toggling it
    // records nothing.
    Assert.Empty(state.Editor.Undo.Applied)

    // The overlay is part of the record a delete carries, which is what makes it
    // round-trip. The shared payload this feature removes held only the base entity, so
    // undoing a delete silently turned the overlay off.
    Assert.True((Zone.captureActor zone ref).ShowCollider)

// ── Undo granularity ──

[<Fact>]
let ``consecutive transforms on one ref merge into a single step`` () =
    let shared = sharedState ()
    let zone = Zone.create ()
    let a = Zone.spawnWaypoint zone (entity "a")
    let b = Zone.spawnWaypoint zone (entity "b")
    let mutable state = gameplayState zone

    let first = { transformIdentity with Position = Vector3(1.0f, 0.0f, 0.0f) }
    let second = { transformIdentity with Position = Vector3(2.0f, 0.0f, 0.0f) }

    state.Editor <- { state.Editor with Undo = UndoStack.push state.Editor.Undo (Command.Transform (a, transformIdentity, first)) }
    state.Editor <- { state.Editor with Undo = UndoStack.push state.Editor.Undo (Command.Transform (a, first, second)) }

    // One step spanning the whole gesture, from its first transform to its last.
    match state.Editor.Undo.Applied with
    | [ Transform (r, before, after) ] ->
        Assert.Equal<EntityRef>(a, r)
        Assert.Equal(transformIdentity, before)
        Assert.Equal(second, after)
    | other -> Assert.Fail(sprintf "expected one merged Transform, got %A" other)

    // Another ref's transform is a step of its own.
    state.Editor <- { state.Editor with Undo = UndoStack.push state.Editor.Undo (Command.Transform (b, transformIdentity, first)) }
    Assert.Equal(2, state.Editor.Undo.Applied.Length)

// ── Player designation (spec 045) ──

// An actor added straight to the slot map: a designation holds only a handle, so nothing here needs the
// renderer a spawn would build (the collider-overlay test above stops at the same line).
let private playerZone () : Zone.Zone * EntityRef * EntityRef =
    let add (name: string) (zone: Zone.Zone) =
        zone.Actors.Add({ State =
                            { Base = entity name
                              AssetId = "character"
                              ShowCollider = false }
                          Physics = None
                          Renderer = Unchecked.defaultof<ActorRender.ActorRender> })
    let zone = Zone.create ()
    let hero = add "hero" zone
    let rival = add "rival" zone
    (zone, actorRef hero.Index hero.Generation, actorRef rival.Index rival.Generation)

[<Fact>]
let ``setting the player records one SetPlayer step and undo puts the previous designation back`` () =
    let (zone, hero, rival) = playerZone ()
    let shared = sharedState ()
    let mutable state = gameplayState zone

    Selection.setPlayer shared &state hero
    match state.Editor.Undo.Applied with
    | [ SetPlayer (ValueNone, ValueSome designated) ] -> Assert.Equal(hero.Index, designated.Index)
    | other -> Assert.Fail(sprintf "expected exactly one SetPlayer command, got %A" other)

    // Re-designating carries the previous actor, so the first undo is the *previous actor*, not no player.
    Selection.setPlayer shared &state rival
    let undo = UndoStack.undo state.Editor.Undo zone shared.Physics shared.Assets
    state.Editor <- { state.Editor with Undo = undo }
    Assert.Equal(hero.Index, zone.Player.Value.Index)

    let undo = UndoStack.undo state.Editor.Undo zone shared.Physics shared.Assets
    state.Editor <- { state.Editor with Undo = undo }
    Assert.True(ValueOption.isNone zone.Player)

[<Fact>]
let ``binding the actor who is already the player records nothing`` () =
    let (zone, hero, _) = playerZone ()
    let shared = sharedState ()
    let mutable state = gameplayState zone

    Selection.setPlayer shared &state hero
    Selection.setPlayer shared &state hero

    // One step for the two clicks, and the state did not move (FR-004).
    Assert.Equal(1, state.Editor.Undo.Applied.Length)
    Assert.Equal(hero.Index, zone.Player.Value.Index)
