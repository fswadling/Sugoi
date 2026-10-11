module ScenarioFork

// The scenario fork (spec 078): the one materialiser both entry points call — a cue's `ForkScenario` op and the
// Scenario panel's Fork control. It appends a registered scenario's entities into the live scene (creating absent
// places first, without entering them) and returns the commands that reverse it, so the editor gets one undoable
// step and the cue op a runtime effect with no history.
//
// Listed after `state/GameplayState.fs` and before `world/CueEngine.fs` — the project's deliberate ordering
// inversion point — because it mutates the gameplay state (needs `GameplayState`, `Zone`, `ZoneRegistry`) and both
// `CueEngine` (the op) and `editor/ScenarioPanel.fs` (the control) call it.

open Command
open Entity
open Place
open ScenarioRegistry
open SlotMap

/// Detach the body an appended actor built when its place is not the active one, so it does not simulate —
/// mirroring `CueEngine.detachIfInactive`. Only the body just built is touched.
let private detachActor (world: Physics.World) (zone: Zone.Zone) (ref: EntityRef) : unit =
    let actor = &zone.Actors.Get(handleOf ref)
    match actor.Physics with
    | Some ph when Physics.isAdded world ph.Body -> do Physics.removeBody world ph.Body
    | _ -> ()

/// Append every recorded zone's entities into its place's zone, making absent places present first (from the Zone
/// Registry's template, else empty) **without** entering them, and detach actor bodies built into an inactive
/// place. Returns, in apply order, the commands that reverse the scene work — or `None` when the capacity
/// pre-check refuses, in which case nothing was changed (FR-010; the `option` separates "applied, nothing to add"
/// — an empty scenario over present places, whose cue still forks — from "refused").
///
/// Non-idempotent by design: it only ever appends, so a second call adds a second copy and a duplicate name is a
/// legitimate scene (FR-005). It never changes the active place, the camera or the selection (FR-004).
let materialise
    (shared: SharedState.SharedState)
    (state: byref<GameplayState.GameplayState>)
    (contents: ScenarioContents) : Command list option =

    // ── capacity pre-check: all-or-nothing, before anything is touched (FR-010) ──
    // A present place's free room is `Capacity - Count`; an absent one is created empty, so it has a fresh
    // zone's full capacity. If any target lacks room for the entities it would take, the whole fork is refused.
    let room (map: SlotMap<'T>) (need: int) : bool = int map.Capacity - int map.Count >= need
    let mutable fits = true
    for KeyValue (place, scenarioZone) in contents.Zones do
        if fits then
            let needActors = Array.length scenarioZone.Actors
            let needWaypoints = Array.length scenarioZone.Waypoints
            let needTriggers = Array.length scenarioZone.Triggers
            let ok =
                match state.Zones.TryGetValue place with
                | true, zone -> room zone.Actors needActors && room zone.Waypoints needWaypoints && room zone.Triggers needTriggers
                | false, _ ->
                    let fresh = Zone.create ()
                    room fresh.Actors needActors && room fresh.Waypoints needWaypoints && room fresh.Triggers needTriggers
            if not ok then fits <- false

    if not fits then
        eprintfn "scenario fork: a target place lacks room for the scenario's entities"
        None
    else

    let commands = ResizeArray<Command>()

    // ── make the scenario's absent places present, without entering them ──
    for KeyValue (place, _) in contents.Zones do
        if not (state.Zones.ContainsKey place) then
            do GameplayState.presentZone shared &state place (ZoneRegistry.tryTemplate place state.ZoneRegistry)
            commands.Add(ZonePresent place)

    // ── append each zone's entities into its place ──
    let active = state.ActiveZone
    for KeyValue (place, scenarioZone) in contents.Zones do
        let zone = state.Zones.[place]

        // actors: the authored overlay is written back (`spawnActor` opens it off); a body built into an
        // inactive place is detached so it does not simulate.
        for actor in scenarioZone.Actors do
            let ref = Zone.spawnActor zone state.Physics shared.Assets actor.Base actor.AssetId
            do Zone.setShowCollider zone ref actor.ShowCollider
            if place <> active then detachActor state.Physics zone ref
            commands.Add(ActorCreate(place, ref, Zone.captureActor zone ref))

        // waypoints: spawn all first, then re-link from the scenario's own indices, so each recorded
        // `WaypointCreate` payload carries the resolved `Next : EntityRef option` — no separate `Link` step.
        let refs = scenarioZone.Waypoints |> Array.map (fun waypoint -> Zone.spawnWaypoint zone waypoint.Base)
        scenarioZone.Waypoints |> Array.iteri (fun i waypoint ->
            match waypoint.Next with
            | Some next -> do Zone.setWaypointNext zone refs.[i] (Some refs.[next])
            | None -> ())
        for i in 0 .. refs.Length - 1 do
            commands.Add(WaypointCreate(place, refs.[i], Zone.captureWaypoint zone refs.[i]))

        // triggers: the authored shape is written back (`spawnTrigger` builds a default); no body to detach.
        for trigger in scenarioZone.Triggers do
            let ref = Zone.spawnTrigger zone trigger.Base
            do Zone.setTrigger zone ref trigger
            commands.Add(TriggerCreate(place, ref, Zone.captureTrigger zone ref))

    Some(List.ofSeq commands)
