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
      Assets =
        { Device = IntPtr.Zero
          Actors = Dictionary()
          Props = Dictionary()
          Environments = Dictionary() }
      Physics = Physics.create () }

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

    Zone.removeEntity zone shared.Physics ref

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

    // Both links resolve again: the entries were never destroyed, and the restored
    // waypoint re-occupied its slot with the generation the links recorded.
    Assert.True(Zone.isAlive zone b)
    Assert.Equal(Some b, Zone.waypointNext zone a)
    Assert.Equal(Some c, Zone.waypointNext zone b)
