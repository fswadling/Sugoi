module ScenarioTests

// The scenario library (spec 070), asserted headlessly: the vocabulary, the capture, and the registration step
// on the one history. Nothing here opens a window, a GPU or a device — the capture is pure over the zones it is
// handed, and the step writes the state-level library, so both are checkable with no device.

open Xunit
open System
open System.Collections.Generic
open Command
open Entity
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

/// A headless gameplay state: the single `GameplayState.startZone` zone, as the app starts.
let private newState () : GameplayState.GameplayState =
    GameplayState.create (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<Ui.Ui>)

/// A device-free `SharedState`: an empty asset manager and an empty flag set.
let private sharedState () : SharedState.SharedState =
    { Gpu = Unchecked.defaultof<Gpu.Gpu>
      Assets = TestSupport.manager IntPtr.Zero (Dictionary()) (Dictionary()) (Dictionary())
      Advents = Set.empty }

/// Register the current scene under `Intro`, the way the panel's Register control does.
let private register (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    do ScenarioPanel.commit shared &state Intro (Some (capture state.Zones state.Cue))

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
let ``the capture carries every present zone and the cue`` () =
    let zones = Dictionary<string, Zone.Zone>()
    let main = Zone.create ()
    do ignore (Zone.spawnWaypoint main (entity "start"))
    zones.[GameplayState.startZone] <- main
    zones.["Island"] <- Zone.create ()
    let cue = Cue.Print "scenario"

    let contents = capture zones cue

    Assert.Equal(2, contents.Zones.Count)
    Assert.Equal(ScenarioZone.capture main, Map.find GameplayState.startZone contents.Zones)
    Assert.Equal(ScenarioZone.capture zones.["Island"], Map.find "Island" contents.Zones)
    Assert.Equal(cue, contents.Cue)
    // The waypoint it was authored with is in the capture, part for part.
    let capturedMain = Map.find GameplayState.startZone contents.Zones
    Assert.Equal(1, Array.length capturedMain.Waypoints)
    Assert.Equal("start", capturedMain.Waypoints.[0].Base.Name)

[<Fact>]
let ``a captured zone is ref-free: waypoint chains become indices`` () =
    let zone = Zone.create ()
    let first = Zone.spawnWaypoint zone (entity "a")
    let second = Zone.spawnWaypoint zone (entity "b")
    do Zone.setWaypointNext zone first (Some second)
    do ignore (zone.Actors.Add(actor "pupil"))
    do ignore (zone.Triggers.Add({ State = { Base = entity "area"; Radius = 1.0f; Height = 2.0f; Interactable = false; Prompt = ""; Cue = Cue.Fin }; Triggered = false }))

    let captured = ScenarioZone.capture zone

    // A chain is an index into the captured waypoints, not an `EntityRef` into a live zone.
    let expected : ScenarioZone.ScenarioWaypoint array =
        [| { Base = entity "a"; Next = Some 1 }
           { Base = entity "b"; Next = None } |]
    Assert.Equal<ScenarioZone.ScenarioWaypoint>(expected, captured.Waypoints)
    // The narrative entities are captured; the zone's staging (environment, light, player) is not.
    Assert.Equal(1, Array.length captured.Actors)
    Assert.Equal("pupil", captured.Actors.[0].Base.Name)
    Assert.Equal(1, Array.length captured.Triggers)
    Assert.Equal("area", captured.Triggers.[0].Base.Name)

[<Fact>]
let ``a place with no narrative entities is captured as an empty scenario zone`` () =
    let zones = Dictionary<string, Zone.Zone>()
    zones.[GameplayState.startZone] <- Zone.create ()
    zones.["Island"] <- Zone.create ()

    let contents = capture zones Cue.Fin

    Assert.Equal(2, contents.Zones.Count)
    let island = Map.find "Island" contents.Zones
    Assert.Equal(0, Array.length island.Actors)
    Assert.Equal(0, Array.length island.Waypoints)
    Assert.Equal(0, Array.length island.Triggers)

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
        | Some contents -> Array.length (Map.find GameplayState.startZone contents.Zones).Waypoints
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
let ``a registration carries the zone's light but not its player`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    let handle = zone.Actors.Add(actor "pupil")

    do register shared &state
    let first = tryContents Intro state.Scenarios

    // The player designation is not staging: changing it alone leaves the capture as it was (spec 079).
    zone.Player <- ValueSome handle
    do register shared &state
    let afterPlayer = tryContents Intro state.Scenarios
    Assert.Equal<ScenarioContents option>(first, afterPlayer)

    // The light IS staging now (spec 079): changing it changes the capture.
    zone.Light <- { zone.Light with Pitch = 90.0f }
    do register shared &state
    let afterLight = tryContents Intro state.Scenarios
    Assert.NotEqual<ScenarioContents option>(afterPlayer, afterLight)

[<Fact>]
let ``the zone-level runner refuses a scenario step, which is state-level`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    let step = SetScenario (Intro, None, None)
    Assert.True(raises (fun () -> Command.apply zone state.Physics shared.Assets step))
    Assert.True(raises (fun () -> Command.undo zone state.Physics shared.Assets step))

