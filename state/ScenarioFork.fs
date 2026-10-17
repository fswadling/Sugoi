module ScenarioFork

// The scenario fork (spec 078, extended by spec 079): the one materialiser both entry points call — a cue's
// `ForkScenario` op and the Scenario panel's Fork control. It **ensures** each recorded zone by name (creating an
// absent one and staging it, or restaging a present one), **appends** the zone's narrative entities, and **forks**
// the cue. It returns the commands that reverse the scene work, so the editor gets one undoable step and the cue op
// a runtime effect with no history.
//
// Listed after `state/GameplayState.fs` and before `state/CueEngine.fs` because it mutates the gameplay
// state (needs `GameplayState`, `Zone`) and both `CueEngine` (the op) and `editor/ScenarioPanel.fs`
// (the control) call it.

open Command
open Entity
open ScenarioRegistry
open SlotMap
open ZoneTemplate

/// Load a scenario into the live scene: **ensure** each recorded zone by name (create and stage an absent one;
/// restage a present one whose staging differs), **append** its narrative entities into it, and leave the cue to
/// the caller. Returns, in apply order, the commands that reverse the scene work — or `None` when the capacity
/// pre-check refuses, in which case nothing was changed (FR-009; the `option` separates "applied, nothing to add"
/// from "refused").
///
/// Non-idempotent for entities (FR-005): a second fork adds a second copy, and a duplicate entity name is a
/// legitimate scene. Zones are never duplicated — a zone's name is its identity (FR-003). Nothing here changes the
/// active zone, the camera or the selection (FR-007).
let materialise
    (shared: SharedState.SharedState)
    (state: byref<GameplayState.GameplayState>)
    (contents: ScenarioContents) : Command list option =

    // ── capacity pre-check: all-or-nothing, before anything is touched (FR-009) ──
    // Appended entities need free room (`Capacity - Count`); a staging's props replace all present props, so they
    // need the whole props map to hold them. A zone that is absent is created empty, so it has full capacity.
    let room (map: SlotMap<'T>) (need: int) : bool = int map.Capacity - int map.Count >= need
    let roomFixed (capacity: uint32) (need: int) : bool = int capacity >= need
    let mutable fits = true
    for KeyValue (name, scenarioZone) in contents.Zones do
        if fits then
            let needActors = Array.length scenarioZone.Actors
            let needWaypoints = Array.length scenarioZone.Waypoints
            let needTriggers = Array.length scenarioZone.Triggers
            let needProps = Array.length scenarioZone.Staging.Props
            let ok =
                match state.Zones.TryGetValue name with
                | true, zone ->
                    room zone.Actors needActors
                    && room zone.Waypoints needWaypoints
                    && room zone.Triggers needTriggers
                    && int zone.Props.Capacity >= needProps
                | false, _ ->
                    // A fresh zone has the same fixed capacities `Zone.create` uses, without being built (F-44).
                    roomFixed Zone.ActorsCapacity needActors
                    && roomFixed Zone.WaypointsCapacity needWaypoints
                    && roomFixed Zone.TriggersCapacity needTriggers
                    && int Zone.PropsCapacity >= needProps
            if not ok then fits <- false

    if not fits then
        Diagnostics.reportf "scenario fork: a target zone lacks room for the scenario's entities or props"
        None
    else

    let commands = ResizeArray<Command>()
    let active = state.ActiveZone

    for KeyValue (name, scenarioZone) in contents.Zones do
        // ── ensure the zone: create-and-stage when absent, restage when present and different ──
        if not (state.Zones.ContainsKey name) then
            do GameplayState.presentZone shared &state name scenarioZone.Staging
            commands.Add(ZonePresent (name, scenarioZone.Staging))
        else
            let previous = ScenarioZone.captureStaging state.Zones.[name]
            if previous <> scenarioZone.Staging then
                do GameplayState.stageZone shared &state name scenarioZone.Staging
                commands.Add(ZoneStage (name, previous, scenarioZone.Staging))

        let zone = state.Zones.[name]

        // actors: the authored overlay is written back (`spawnActor` opens it off); a body built into an
        // inactive zone is detached so it does not simulate.
        for actor in scenarioZone.Actors do
            let ref = Zone.spawnActor zone state.Physics shared.Assets actor.Base actor.AssetId
            do Zone.setShowCollider zone ref actor.ShowCollider
            if name <> active then do Zone.detachActorBody state.Physics zone ref
            commands.Add(ActorCreate(name, ref, Zone.captureActor zone ref))

        // waypoints: spawn all first, then re-link from the scenario's own indices, so each recorded
        // `WaypointCreate` payload carries the resolved `Next : EntityRef option` — no separate `Link` step.
        let refs = scenarioZone.Waypoints |> Array.map (fun waypoint -> Zone.spawnWaypoint zone waypoint.Base)
        scenarioZone.Waypoints |> Array.iteri (fun i waypoint ->
            match waypoint.Next with
            | Some next -> do Zone.setWaypointNext zone refs.[i] (Some refs.[next])
            | None -> ())
        for i in 0 .. refs.Length - 1 do
            commands.Add(WaypointCreate(name, refs.[i], Zone.captureWaypoint zone refs.[i]))

        // triggers: the authored shape is written back (`spawnTrigger` builds a default); no body to detach.
        for trigger in scenarioZone.Triggers do
            let ref = Zone.spawnTrigger zone trigger.Base
            do Zone.setTrigger zone ref trigger
            commands.Add(TriggerCreate(name, ref, Zone.captureTrigger zone ref))

    Some(List.ofSeq commands)
