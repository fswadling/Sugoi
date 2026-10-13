module ScenarioForkTests

// The scenario fork (spec 078), asserted headlessly. The materialiser, the cue op, the editor step and its
// undo/redo, the footprint rules, non-idempotence, the capacity no-op and the unregistered no-op are all
// checkable with no window, no GPU and no device — as the rest of the suite is.

open System
open System.Collections.Generic
open System.Numerics
open Xunit
open Gltf
open Prime
open Command
open Cue
open Entity
open ScenarioRegistry
open ScenarioZone
open ZoneTemplate

// ── fixtures ──

// A zero model: enough for `ActorRender.create` to hold no clip and pose nothing.
let private emptyModel () : Iqm.Model =
    { Meshes = [||]
      Positions = [||]
      Normals = None
      Uvs = None
      Tangents = None
      BlendIndices = None
      BlendWeights = None
      Triangles = [||]
      Joints = [||]
      Poses = [||]
      Anims = [||]
      FrameChannels = [||]
      NumFrameChannels = 0
      PoseChannelOffsets = [||] }

// An actor asset in the loaded shape, its GPU mesh defaulted: `createActorRender` builds a per-instance
// renderer from it, and the zero-device buffers it creates are never read here.
let private actorAsset (id: string) : ActorAsset.ActorAsset =
    { Id = id
      Name = id
      ModelPath = ""
      Bounds = { Min = Vector3.Zero; Max = Vector3.One }
      Mesh = Unchecked.defaultof<Mesh.Mesh>
      Model = Actor.createModel (emptyModel ())
      Morphs = None
      Portraits = None }

/// A device-free `SharedState`: a fabricated actor asset, a resident environment asset, and an empty flag set.
let private sharedState () : SharedState.SharedState =
    let actors = Dictionary<string, ActorAsset.ActorAsset>()
    actors.["aiko"] <- actorAsset "aiko"
    let environments = Dictionary<string, EnvironmentAsset.EnvironmentAsset>()
    environments.["island-env"] <- TestSupport.residentAsset "island-env" [||] [||]
    { Gpu = Unchecked.defaultof<Gpu.Gpu>
      Assets = TestSupport.manager IntPtr.Zero actors (Dictionary()) environments
      Advents = Advent.empty }

let private newState () : GameplayState.GameplayState =
    GameplayState.create (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<DebugDraw.DebugDraw>) (Unchecked.defaultof<Ui.Ui>)

let private entity (name: string) : Entity =
    { Name = name
      Transform = transformIdentity
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

let private triggerState (name: string) : TriggerState =
    { Base = entity name; Radius = 1.0f; Height = 2.0f; Interactable = false; Prompt = ""; Cue = Cue.Fin }

/// A scenario zone with an actor, a chained waypoint pair and a trigger.
let private fullZone () : ScenarioZone =
    { Staging = ZoneTemplate.empty
      Actors = [| { Base = entity "pupil"; AssetId = "aiko"; ShowCollider = false } |]
      Waypoints =
        [| { Base = entity "wp-a"; Next = Some 1 }
           { Base = entity "wp-b"; Next = None } |]
      Triggers = [| triggerState "area" |] }

let private emptyZone : ScenarioZone =
    { Staging = ZoneTemplate.empty; Actors = [||]; Waypoints = [||]; Triggers = [||] }

let private contents (zones: (string * ScenarioZone) list) (cue: Cue.Cue) : ScenarioContents =
    { Zones = Map.ofList zones; Cue = cue }

/// Register `contents` under a scenario the way the panel's Register control does — one `SetScenario` step.
let private register (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (c: ScenarioContents) : unit =
    do ScenarioPanel.commit shared &state (setContents "Intro" (Some c) state.Scenarios)

let private light = { Texture.defaultLight with Pitch = 42.0f }

let private template () : ZoneTemplate =
    { AssetId = "island-env"
      Base = entity "island"
      Orientation = Quaternion.Identity
      Light = light
      Parts = [||]
      Props = [||] }

// ══════ US1 — the cue op (FR-001, FR-002, FR-003, FR-006, FR-009; SC-001) ══════

[<Fact>]
let ``the cue op materialises the scenario and forks its cue`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let scenarioCue = Cue.Print "scenario"
    do register shared &state (contents [ GameplayState.startZone, fullZone () ] scenarioCue)
    let start = GameplayState.activeZone state
    let actorsBefore = start.Actors.Count
    let waypointsBefore = start.Waypoints.Count

    state.Cue <- Sequence(ForkScenario "Intro", FDeque.ofList [])
    do CueEngine.tick shared &state 0.0

    // every entity is appended into the active place ...
    Assert.Equal(actorsBefore + 1u, start.Actors.Count)
    Assert.Equal(waypointsBefore + 2u, start.Waypoints.Count)
    Assert.Equal(1u, start.Triggers.Count)
    // ... and the scenario's cue runs beside the rest (a `Fork` of it)
    match state.Cue with
    | Sequence(Fork inner, _) -> Assert.Equal(scenarioCue, inner)
    | other -> Assert.Fail(sprintf "expected a sequence forking the scenario cue, got %A" other)

// A bare `ForkScenario` — not at a sequence front — still runs the scenario's cue to completion (spec 080,
// US2): the op returns `Fork cue`, and the engine runs a fork with nothing beside it.
[<Fact>]
let ``a bare scenario-fork op runs the scenario's cue to completion`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do register shared &state (contents [ GameplayState.startZone, emptyZone ] (Cue.Print "scenario"))

    state.Cue <- ForkScenario "Intro"
    do CueEngine.tick shared &state 0.0
    do CueEngine.tick shared &state 0.0

    Assert.True(state.Cue.IsFin, "the scenario's cue ran and the fork completed")

[<Fact>]
let ``a forked waypoint chain links the newly added waypoints`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do register shared &state (contents [ GameplayState.startZone, fullZone () ] Cue.Fin)

    state.Cue <- Sequence(ForkScenario "Intro", FDeque.ofList [])
    do CueEngine.tick shared &state 0.0

    let start = GameplayState.activeZone state
    let a = Zone.tryWaypointByName start "wp-a"
    let b = Zone.tryWaypointByName start "wp-b"
    match a, b with
    | Some ra, Some rb -> Assert.Equal(Some rb, Zone.waypointNext start ra)
    | _ -> Assert.Fail "expected both forked waypoints"

[<Fact>]
let ``the cue op creates an absent place without entering it, and detaches its actor body`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let active = state.ActiveZone
    do register shared &state (contents [ "Island", fullZone () ] Cue.Fin)

    state.Cue <- Sequence(ForkScenario "Intro", FDeque.ofList [])
    do CueEngine.tick shared &state 0.0

    Assert.True(state.Zones.ContainsKey "Island")
    Assert.Equal(active, state.ActiveZone) // the active place is untouched
    let island = state.Zones.["Island"]
    match Zone.tryActorByName island "pupil" with
    | Some ref ->
        let actor = &island.Actors.Get(handleOf ref)
        match actor.Physics with
        | Some ph -> Assert.False(Physics.isAdded state.Physics ph.Body) // built into an inactive place
        | None -> Assert.Fail "expected a physics body"
    | None -> Assert.Fail "expected the forked actor"

[<Fact>]
let ``an unregistered scenario is a diagnostic no-op`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let start = GameplayState.activeZone state
    let actorsBefore = start.Actors.Count

    state.Cue <- Sequence(ForkScenario "Intro", FDeque.ofList [])
    do CueEngine.tick shared &state 0.0

    Assert.Equal(actorsBefore, start.Actors.Count)
    Assert.True(state.Cue.IsFin) // the op performed nothing and left the walk at the end

[<Fact>]
let ``a fork naming a scenario that was deleted performs nothing and is not rewritten`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do register shared &state (contents [ GameplayState.startZone, fullZone () ] (Cue.Print "scenario"))
    let start = GameplayState.activeZone state
    let actorsBefore = start.Actors.Count

    // the cue names a scenario that has since been deleted from the library (spec 083, US2-AC4, FR-013)
    state.Scenarios <- remove "Intro" state.Scenarios
    state.Cue <- Sequence(ForkScenario "Intro", FDeque.ofList [])
    do CueEngine.tick shared &state 0.0

    Assert.Equal(actorsBefore, start.Actors.Count) // nothing materialised
    Assert.True(state.Cue.IsFin) // the op performed nothing, reported why, and advanced past it

[<Fact>]
let ``the cue op adds no step to the history`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do register shared &state (contents [ GameplayState.startZone, fullZone () ] (Cue.Print "scenario"))
    let steps = List.length state.Editor.Undo.Applied

    state.Cue <- Sequence(ForkScenario "Intro", FDeque.ofList [])
    do CueEngine.tick shared &state 0.0

    Assert.Equal(steps, List.length state.Editor.Undo.Applied)

// ══════ US2 — the editor fork as one undoable step (FR-004, FR-007, FR-008; SC-004) ══════

[<Fact>]
let ``the editor fork is one step, and undo and redo round-trip it`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let scenarioCue = Cue.Print "scenario"
    do register shared &state (contents [ GameplayState.startZone, fullZone () ] scenarioCue)
    let start = GameplayState.activeZone state
    let actorsBefore = start.Actors.Count
    let stepsBefore = List.length state.Editor.Undo.Applied
    let cueBefore = state.Cue

    do ScenarioPanel.fork shared &state "Intro"

    Assert.Equal(stepsBefore + 1, List.length state.Editor.Undo.Applied) // exactly one step
    Assert.Equal(actorsBefore + 1u, start.Actors.Count)
    Assert.Equal(Cue.forkInto scenarioCue cueBefore, state.Cue) // the screen cue became the fork

    do History.undoHistory shared &state
    Assert.Equal(actorsBefore, start.Actors.Count)
    Assert.Equal(cueBefore, state.Cue)

    do History.redoHistory shared &state
    Assert.Equal(actorsBefore + 1u, start.Actors.Count)
    Assert.Equal(Cue.forkInto scenarioCue cueBefore, state.Cue)

[<Fact>]
let ``undoing a fork removes the places it created and leaves active place and selection unchanged`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let active = state.ActiveZone
    let selection = state.Editor.Selection
    let cue = state.Cue
    do register shared &state (contents [ "Island", fullZone () ] Cue.Fin)

    do ScenarioPanel.fork shared &state "Intro"
    Assert.True(state.Zones.ContainsKey "Island")
    Assert.Equal(cue, state.Cue) // a `Fin` scenario cue leaves the running cue untouched

    do History.undoHistory shared &state
    Assert.False(state.Zones.ContainsKey "Island")
    Assert.Equal(active, state.ActiveZone)
    Assert.Equal(selection, state.Editor.Selection)
    Assert.Equal(cue, state.Cue)

[<Fact>]
let ``forking an unregistered scenario does nothing`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let placesBefore = state.Zones.Count
    let stepsBefore = List.length state.Editor.Undo.Applied

    do ScenarioPanel.fork shared &state "Intro"

    Assert.Equal(placesBefore, state.Zones.Count)
    Assert.Equal(stepsBefore, List.length state.Editor.Undo.Applied)

// ── the fork step's history interactions (FR-012) ──

[<Fact>]
let ``ZonePresent makes a zone present from its staging, and undo removes it`` () =
    let shared = sharedState ()
    let mutable state = newState ()

    do History.apply shared &state (ZonePresent ("Island", ZoneTemplate.ofTemplate (template ())))
    Assert.True(state.Zones.ContainsKey "Island")
    Assert.True(state.Zones.["Island"].Environment.IsSome) // its staging's environment streamed in
    Assert.Equal(light, state.Zones.["Island"].Light) // and its light
    Assert.NotEqual<string>("Island", state.ActiveZone) // presence, not entry

    do History.undo shared &state (ZonePresent ("Island", ZoneTemplate.ofTemplate (template ())))
    Assert.False(state.Zones.ContainsKey "Island")

[<Fact>]
let ``the zone-level runner refuses a presence step, which is state-level`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    Assert.Throws<InvalidOperationException>(fun () -> Command.apply zone state.Physics shared.Assets (ZonePresent ("Island", ZoneTemplate.empty))) |> ignore
    Assert.Throws<InvalidOperationException>(fun () -> Command.undo zone state.Physics shared.Assets (ZonePresent ("Island", ZoneTemplate.empty))) |> ignore

[<Fact>]
let ``pruneCueSteps drops a fork's cue step but keeps its entity commands`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do register shared &state (contents [ "Island", fullZone () ] (Cue.Print "scenario"))

    do ScenarioPanel.fork shared &state "Intro"
    do History.pruneCueSteps &state

    match state.Editor.Undo.Applied with
    | Batch kept :: _ ->
        Assert.False(kept |> List.exists (fun c -> match c with SetCue _ -> true | _ -> false), "the cue step should be pruned")
        Assert.True(kept |> List.exists (fun c -> match c with ZonePresent _ -> true | _ -> false), "the entity batch should survive")
    | _ -> Assert.Fail "expected the fork's batch to survive the prune"

// ══════ US3 — footprint and edges (FR-002, FR-005, FR-010; SC-002, SC-003, SC-005) ══════

[<Fact>]
let ``an absent zone is created from the scenario's staging, or empty when it carries none`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do register shared &state (contents [ "Island", { emptyZone with Staging = ZoneTemplate.ofTemplate (template ()) } ] Cue.Fin)

    do ScenarioPanel.fork shared &state "Intro"
    Assert.True(state.Zones.ContainsKey "Island")
    Assert.True(state.Zones.["Island"].Environment.IsSome)
    Assert.Equal(light, state.Zones.["Island"].Light)

    // with no staging in the scenario, the zone is present and empty but for its entities
    let mutable state2 = newState ()
    do register shared &state2 (contents [ "Island", fullZone () ] Cue.Fin)
    do ScenarioPanel.fork shared &state2 "Intro"
    Assert.True(state2.Zones.ContainsKey "Island")
    Assert.Equal(Texture.defaultLight, state2.Zones.["Island"].Light)

[<Fact>]
let ``a fork restages a present zone and leaves its player alone`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let start = GameplayState.activeZone state
    start.Light <- { start.Light with Pitch = 77.0f }
    let player = ValueSome (start.Actors.Add({ State = { Base = entity "hero"; AssetId = "aiko"; ShowCollider = false }; Physics = None; Renderer = Unchecked.defaultof<ActorRender.ActorRender> }))
    start.Player <- player

    do register shared &state (contents [ GameplayState.startZone, { emptyZone with Staging = ZoneTemplate.ofTemplate (template ()) } ] Cue.Fin)
    do ScenarioPanel.fork shared &state "Intro"

    Assert.Equal(light, start.Light) // the scenario's staging replaced the zone's look
    Assert.True(start.Environment.IsSome)
    Assert.Equal(player, start.Player) // the player is not staging and is untouched

[<Fact>]
let ``forking twice adds two copies`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do register shared &state (contents [ GameplayState.startZone, fullZone () ] Cue.Fin)
    let start = GameplayState.activeZone state
    let actorsBefore = start.Actors.Count

    do ScenarioPanel.fork shared &state "Intro"
    do ScenarioPanel.fork shared &state "Intro"

    Assert.Equal(actorsBefore + 2u, start.Actors.Count) // not idempotent

[<Fact>]
let ``a full target zone refuses the whole fork and creates no place`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let start = GameplayState.activeZone state

    // fill the trigger map to capacity (32), so the scenario's trigger cannot fit
    for i in 0 .. int start.Triggers.Capacity - 1 do
        do Zone.spawnTrigger start (entity (sprintf "t%d" i)) |> ignore
    Assert.Equal(start.Triggers.Capacity, start.Triggers.Count)
    Assert.Equal(0u, start.Triggers.Capacity - start.Triggers.Count)

    // the scenario names an absent place too: a refusal must not create it either
    let c = contents [ GameplayState.startZone, fullZone (); "Island", fullZone () ] Cue.Fin
    do register shared &state c
    let actorsBefore = start.Actors.Count

    Assert.True((ScenarioFork.materialise shared &state c).IsNone) // the pre-check refuses up front
    do ScenarioPanel.fork shared &state "Intro"

    Assert.Equal(actorsBefore, start.Actors.Count) // nothing appended
    Assert.False(state.Zones.ContainsKey "Island") // no place created

// ══════ scenario-owned staging (spec 079) ══════

[<Fact>]
let ``a fork brings a created zone up with its environment and light from the scenario`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do register shared &state (contents [ "Moon", { emptyZone with Staging = ZoneTemplate.ofTemplate (template ()) } ] Cue.Fin)

    do ScenarioPanel.fork shared &state "Intro"

    let created = state.Zones.["Moon"]
    Assert.Equal(Some "island-env", created.Environment |> Option.map (fun e -> e.AssetId))
    Assert.Equal(light, created.Light)

[<Fact>]
let ``a fork reuses a present zone and replaces its staging, without duplicating it`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let start = GameplayState.activeZone state
    start.Light <- { start.Light with Pitch = 90.0f } // a look the scenario will replace
    let zonesBefore = state.Zones.Count
    do register shared &state (contents [ GameplayState.startZone, { emptyZone with Staging = ZoneTemplate.ofTemplate (template ()) } ] Cue.Fin)

    do ScenarioPanel.fork shared &state "Intro"

    Assert.Equal(zonesBefore, state.Zones.Count) // reused, never duplicated
    Assert.True(start.Environment.IsSome)
    Assert.Equal(light, start.Light) // the scenario's staging won

[<Fact>]
let ``a restage is skipped when the zone's staging already matches`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let start = GameplayState.activeZone state
    do register shared &state (contents [ GameplayState.startZone, { emptyZone with Staging = ZoneTemplate.ofTemplate (template ()) } ] Cue.Fin)

    do ScenarioPanel.fork shared &state "Intro"
    let epoch = start.Environment.Value.Epoch // the environment the first fork loaded
    do ScenarioPanel.fork shared &state "Intro"

    Assert.Equal(epoch, start.Environment.Value.Epoch) // the second fork did not reload it
    Assert.Equal(light, start.Light)

[<Fact>]
let ``the editor fork's one step round-trips a restage`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let start = GameplayState.activeZone state
    // An **environment-less** zone with an authored light: staging carries the light with no environment (spec 079),
    // so the round-trip restores it exactly rather than resetting it to the default.
    start.Light <- { start.Light with Pitch = 90.0f }
    let lightBefore = start.Light
    do register shared &state (contents [ GameplayState.startZone, { emptyZone with Staging = ZoneTemplate.ofTemplate (template ()) } ] Cue.Fin)

    do ScenarioPanel.fork shared &state "Intro"
    Assert.Equal(light, start.Light) // restaged to the scenario's

    do History.undoHistory shared &state
    Assert.Equal(lightBefore, start.Light) // one step restores the previous staging

    do History.redoHistory shared &state
    Assert.Equal(light, start.Light)

[<Fact>]
let ``ZoneStage apply and undo round-trip a restage, detaching an inactive zone's bodies`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    do GameplayState.addZone shared &state "Hall" // present and active
    do ignore (Zone.spawnActor state.Zones.["Hall"] state.Physics shared.Assets (entity "npc") "aiko")
    do GameplayState.switchZone shared &state GameplayState.startZone // Hall is now inactive

    let previous = ScenarioZone.captureStaging state.Zones.["Hall"]
    do History.apply shared &state (ZoneStage ("Hall", previous, ZoneTemplate.ofTemplate (template ())))

    let hall = state.Zones.["Hall"]
    Assert.Equal(light, hall.Light)
    match Zone.tryActorByName hall "npc" with
    | Some ref ->
        let actor = &hall.Actors.Get(handleOf ref)
        match actor.Physics with
        | Some ph -> Assert.False(Physics.isAdded state.Physics ph.Body) // built while inactive
        | None -> Assert.Fail "expected a body"
    | None -> Assert.Fail "expected the actor"

    do History.undo shared &state (ZoneStage ("Hall", previous, ZoneTemplate.ofTemplate (template ())))
    Assert.Equal(Texture.defaultLight, hall.Light)

[<Fact>]
let ``the zone-level runner refuses a restage step, which is state-level`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let zone = GameplayState.activeZone state
    Assert.Throws<InvalidOperationException>(fun () -> Command.apply zone state.Physics shared.Assets (ZoneStage ("Hall", ZoneTemplate.empty, ZoneTemplate.empty))) |> ignore
    Assert.Throws<InvalidOperationException>(fun () -> Command.undo zone state.Physics shared.Assets (ZoneStage ("Hall", ZoneTemplate.empty, ZoneTemplate.empty))) |> ignore

[<Fact>]
let ``a fork refuses a target whose props the scenario's staging would not fit`` () =
    let shared = sharedState ()
    let mutable state = newState ()
    let start = GameplayState.activeZone state
    let bigProps =
        Array.init (int start.Props.Capacity + 1) (fun i ->
            { Base = entity (sprintf "p%d" i); AssetId = Some "crate"; SubmeshIndex = 0; SubmeshCount = 1 })
    let c = contents [ GameplayState.startZone, { emptyZone with Staging = ZoneTemplate.ofTemplate ({ (template ()) with Props = bigProps }) } ] Cue.Fin
    do register shared &state c

    Assert.True((ScenarioFork.materialise shared &state c).IsNone) // the props do not fit
    Assert.Equal(Texture.defaultLight, start.Light) // nothing changed at all
