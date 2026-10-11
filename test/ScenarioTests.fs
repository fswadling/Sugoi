module ScenarioTests

// The scenario library (spec 070), asserted headlessly: the vocabulary, the capture, and the registration step
// on the one history. Nothing here opens a window, a GPU or a device — the capture is pure over the zones it is
// handed, and the step writes the state-level library, so both are checkable with no device.

open Xunit
open System
open System.Collections.Generic
open Command
open Entity
open Place
open Scenario
open ScenarioRegistry

// ── fixtures ──

let private entity (name: string) : Entity =
    { Name = name
      Transform = transformIdentity
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

// An actor value, as the player-designation test needs one — no renderer, which it never dereferences.
let private actor (name: string) : Actor =
    { State = { Base = entity name; AssetId = ""; ShowCollider = false }
      Physics = None
      Renderer = Unchecked.defaultof<ActorRender.ActorRender> }

/// A headless gameplay state: the single `Place.start` zone, as the app starts.
let private newState () : GameplayState.GameplayState =
    GameplayState.create (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<Ui.Ui>)

/// A device-free `SharedState`: an empty asset manager and an empty flag set.
let private sharedState () : SharedState.SharedState =
    { Gpu = Unchecked.defaultof<Gpu.Gpu>
      Assets = TestSupport.manager IntPtr.Zero (Dictionary()) (Dictionary()) (Dictionary())
      Advents = Set.empty }

/// Register the current scene under `Intro`, the way the panel's Register control does.
let private register (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    do ScenarioPanel.commit shared &state Intro (Some (capture state.Zones state.ActiveZone state.Cue))

let private raises (f: unit -> unit) : bool =
    try
        f ()
        false
    with _ ->
        true

// A scenario zone carrying just its actors (an empty zone otherwise).

// ══════ the vocabulary (FR-003) ══════

[<Fact>]
let ``the declared set is enumerated in declaration order`` () =
    Assert.Equal<string list>([ "Intro" ], all |> List.map scenarioName)

// ══════ the capture (FR-002, FR-009, SC-003) ══════

[<Fact>]
let ``the capture carries every present zone, the active place and the cue`` () =
    let zones = Dictionary<Place, Zone.Zone>()
    let main = Zone.create ()
    do ignore (Zone.spawnWaypoint main (entity "start"))
    zones.[Place.start] <- main
    zones.[Place.Island] <- Zone.create ()
    let cue = Cue.Print "scenario"

    let contents = capture zones Place.Island cue

    Assert.Equal(2, contents.Zones.Count)
    Assert.Equal(ScenarioZone.capture main, Map.find Place.start contents.Zones)
    Assert.Equal(ScenarioZone.capture zones.[Place.Island], Map.find Place.Island contents.Zones)
    Assert.Equal(Place.Island, contents.Active)
    Assert.Equal(cue, contents.Cue)
    // The waypoint it was authored with is in the capture, part for part.
    let capturedMain = Map.find Place.start contents.Zones
    Assert.Equal(1, Array.length capturedMain.Waypoints)
    Assert.Equal("start", capturedMain.Waypoints.[0].Base.Name)

[<Fact>]
let ``a captured zone is ref-free: waypoint chains and the player become indices`` () =
    let zone = Zone.create ()
    let first = Zone.spawnWaypoint zone (entity "a")
    let second = Zone.spawnWaypoint zone (entity "b")
    do Zone.setWaypointNext zone first (Some second)
    let handle = zone.Actors.Add(actor "pupil")
    zone.Player <- ValueSome handle

    let captured = ScenarioZone.capture zone

    // A chain is an index into the captured waypoints, not an `EntityRef` into a live zone.
    Assert.Equal(2, Array.length captured.Waypoints)
    Assert.Equal(Some 1, captured.Waypoints.[0].Next)
    Assert.Equal(None, captured.Waypoints.[1].Next)
    // The player is an index into the captured actors, not a slot handle.
    Assert.Equal(1, Array.length captured.Actors)
    Assert.Equal(Some 0, captured.Player)
    // Nothing names a live slot: the record's own values are the authored ones.
    Assert.Equal("a", captured.Waypoints.[0].Base.Name)
    Assert.Equal("pupil", captured.Actors.[0].Base.Name)

[<Fact>]
let ``register files the scene and clear empties it`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    Assert.True(Option.isNone (tryContents Intro state.Scenarios))

    do register shared &state
    Assert.True(Option.isSome (tryContents Intro state.Scenarios))

    do ScenarioPanel.commit shared &state Intro None
    Assert.True(Option.isNone (tryContents Intro state.Scenarios))

// ══════ the step on one history (FR-004, FR-005, FR-011) ══════

[<Fact>]
let ``register records one step, and undo and redo round-trip it`` () =
    let shared = sharedState ()
    let mutable state = newState ()

    do register shared &state
    Assert.Equal(1, List.length state.Editor.Undo.Applied)

    do History.undoHistory shared &state
    Assert.True(Option.isNone (tryContents Intro state.Scenarios))
    Assert.Equal(0, List.length state.Editor.Undo.Applied)

    do History.redoHistory shared &state
    Assert.True(Option.isSome (tryContents Intro state.Scenarios))
    Assert.Equal(1, List.length state.Editor.Undo.Applied)

[<Fact>]
let ``undo of a re-registration restores the previous contents, not nothing`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let capturedWaypoints (s: GameplayState.GameplayState) : int =
        match tryContents Intro s.Scenarios with
        | Some contents -> Array.length (Map.find Place.start contents.Zones).Waypoints
        | None -> -1

    do register shared &state
    Assert.Equal(0, capturedWaypoints state)

    do ignore (Zone.spawnWaypoint (GameplayState.activeZone state) (entity "path"))
    do register shared &state
    Assert.Equal(1, capturedWaypoints state)

    do History.undoHistory shared &state
    Assert.Equal(0, capturedWaypoints state) // the earlier registration, not an empty slot

[<Fact>]
let ``undo of a clear restores the contents it held`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do register shared &state
    let filed = tryContents Intro state.Scenarios

    do ScenarioPanel.commit shared &state Intro None
    Assert.True(Option.isNone (tryContents Intro state.Scenarios))

    do History.undoHistory shared &state
    Assert.Equal(filed, tryContents Intro state.Scenarios)

[<Fact>]
let ``registering disturbs nothing in the scene`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let zones = state.Zones.Count
    let active = state.ActiveZone
    let cue = state.Cue
    let position = state.Camera.Position
    let target = state.Camera.Target
    let selection = state.Editor.Selection

    do register shared &state
    do History.undoHistory shared &state

    Assert.Equal(zones, state.Zones.Count)
    Assert.Equal(active, state.ActiveZone)
    Assert.Equal(cue, state.Cue)
    Assert.Equal(position, state.Camera.Position)
    Assert.Equal(target, state.Camera.Target)
    Assert.Equal(selection, state.Editor.Selection)

[<Fact>]
let ``the zone-level runner refuses a scenario step, which is state-level`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    let step = SetScenario (Intro, None, None)
    Assert.True(raises (fun () -> Command.apply zone state.Physics shared.Assets step))
    Assert.True(raises (fun () -> Command.undo zone state.Physics shared.Assets step))

