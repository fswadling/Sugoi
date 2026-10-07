module ScenarioZone

// A zone as a **scenario** holds it (spec 070): the authored record of one zone, free of every reference the
// origin zone used — no `EntityRef`, no live `Environment`, and waypoint chains and the player designation by
// index. Deliberately its own type, not `ZoneContents`: that record exists to reverse a zone *delete*, so it must
// keep each entity's ref and the live environment (epoch and parts) for a restore to land in the same slots. A
// scenario carries none of that — a fork instantiates into fresh zones — so reusing `ZoneContents` would push
// zone-scoped identity and its `(ref, state)` list shape onto scenarios. `docs/ZONE_MODEL_DECISION.md` records
// the reasoning.
//
// Listed after `world/Zone.fs`, whose live zone is what `capture` reads, and before `world/ScenarioRegistry.fs`,
// which files one under a scenario. Loading a scenario back (`occupy`, the fork) is a later increment.

open System.Collections.Generic
open System.Numerics
open Entity
open SlotMap
open Zone

/// The environment as authored: the asset and where it sits, and its parts as authored states. The live
/// `Environment` (`world/Entity.fs`) instead carries an epoch and a parts slot map, which only a restore needs.
type ScenarioEnvironment =
    { AssetId : string
      Base : Entity
      Orientation : Quaternion
      Parts : PropState array }

/// A waypoint whose chain link is the **index** of the next waypoint in this zone's own `Waypoints` array — not
/// the live-zone `EntityRef` a `Waypoint` holds, which a fresh zone has no slot for.
type ScenarioWaypoint =
    { Base : Entity
      Next : int option }

/// One zone as a scenario holds it. Every collection is an array of authored states (the shape a bulk load
/// walks), and the two places a live zone carries identity — a waypoint's link and the player — are indices.
type ScenarioZone =
    { Environment : ScenarioEnvironment option
      Light : Texture.LightSettings
      Actors : ActorState array
      Props : PropState array
      Waypoints : ScenarioWaypoint array
      Triggers : TriggerState array
      Player : int option }

// ── The capture ──

// The authored parts of a loaded environment, as states.
let private captureEnvironmentParts (environment: Environment) : PropState array =
    let parts = ResizeArray<PropState>(int environment.Parts.Count)
    environment.Parts.IteriAlive (fun _ _ part -> parts.Add part.State)
    parts.ToArray()

// A zone's actors as authored states, and the index of the player among them. The player is a slot handle, so it
// is mapped to the position of the actor it names — `None` when it names nothing alive (a stale handle).
let private captureActors (zone: Zone) : ActorState array * int option =
    let actors = ResizeArray<ActorState>(int zone.Actors.Count)
    let indices = ResizeArray<uint32>(int zone.Actors.Count)
    zone.Actors.IteriAlive (fun index _ actor ->
        indices.Add index
        actors.Add actor.State)

    let player =
        match zone.Player with
        | ValueNone -> None
        | ValueSome handle ->
        let found = indices.IndexOf handle.Index
        if found >= 0 then Some found else None

    actors.ToArray(), player

// A zone's waypoints, their chains re-expressed as indices into the returned array. Two passes: collect the live
// refs and their authored values, then resolve each `Next` to the position of the waypoint it names. A `Next`
// that names a waypoint not in this zone is a bug, so it raises rather than being silently dropped.
let private captureWaypoints (zone: Zone) : ScenarioWaypoint array =
    let live = ResizeArray<EntityRef * Waypoint>(int zone.Waypoints.Count)
    zone.Waypoints.IteriAlive (fun index generation waypoint -> live.Add(waypointRef index generation, waypoint))

    let position = Dictionary<EntityRef, int>(int zone.Waypoints.Count)
    live |> Seq.iteri (fun i (ref, _) -> position.[ref] <- i)

    live
    |> Seq.map (fun (_, waypoint) ->
        let next =
            match waypoint.Next with
            | None -> None
            | Some ref ->
            match position.TryGetValue ref with
            | true, i -> Some i
            | false, _ -> failwith "ScenarioZone.capture: a waypoint's next is not a live waypoint"
        { Base = waypoint.Base; Next = next })
    |> Seq.toArray

/// Take a zone as a scenario holds it: the environment as authored, the light, and the actors, placed props,
/// waypoints and triggers as arrays. Pure and device-free — it reads the live zone and nothing else — so it can
/// be asserted with no window. De-refs every entity, and resolves the waypoint chains and the player to indices.
let capture (zone: Zone) : ScenarioZone =
    let actors, player = captureActors zone

    let props = ResizeArray<PropState>(int zone.Props.Count)
    zone.Props.IteriAlive (fun _ _ prop -> props.Add prop.State)

    let triggers = ResizeArray<TriggerState>(int zone.Triggers.Count)
    zone.Triggers.IteriAlive (fun _ _ trigger -> triggers.Add trigger.State)

    let environment =
        zone.Environment
        |> Option.map (fun env ->
            { AssetId = env.AssetId
              Base = env.Base
              Orientation = env.Orientation
              Parts = captureEnvironmentParts env })

    { Environment = environment
      Light = zone.Light
      Actors = actors
      Props = props.ToArray()
      Waypoints = captureWaypoints zone
      Triggers = triggers.ToArray()
      Player = player }
