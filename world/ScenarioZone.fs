module ScenarioZone

// A zone as a **scenario** holds it (spec 070; the staging restored by spec 079): the authored record of one
// zone — its staging and its narrative entities — free of every reference the origin zone used (no
// `EntityRef`, and waypoint chains by index). Deliberately its own type, not `ZoneContents`: that record exists to
// reverse a zone *delete*, so it must keep each entity's ref and the live environment (epoch and parts) for a
// restore to land in the same slots. A scenario carries none of that — a fork instantiates into fresh zones — so
// reusing `ZoneContents` would push zone-scoped identity and its `(ref, state)` list shape onto scenarios.
// `docs/ZONE_MODEL_DECISION.md` records the reasoning.
//
// A zone's **staging** — its environment, light and placed props — rides with it (spec 079): a scenario owns it now
// that the Zone Registry has retired, so a fork brings a zone up fully. The player designation is per-zone runtime
// state (`Zone.Player`), not staging, and is not captured.
//
// Listed after `world/Zone.fs`, whose live zone is what `capture` reads, and before `world/ScenarioRegistry.fs`,
// which files one under a scenario. Loading a scenario back (`occupy`, the fork) is a later increment.

open System.Collections.Generic
open Entity
open Zone
open ZoneTemplate

/// A waypoint whose chain link is the **index** of the next waypoint in this zone's own `Waypoints` array — not
/// the live-zone `EntityRef` a `Waypoint` holds, which a fresh zone has no slot for.
type ScenarioWaypoint =
    { Base : Entity
      Next : int option }

/// One zone as a scenario holds it: its staging (environment, light and placed props) and its narrative entities
/// as arrays of authored states. A waypoint's link is an index into `Waypoints`, so nothing names a live slot.
type ScenarioZone =
    { Staging : ZoneStaging
      Actors : ActorState array
      Waypoints : ScenarioWaypoint array
      Triggers : TriggerState array }

// ── The capture ──

// A zone's actors as authored states.
let private captureActors (zone: Zone) : ActorState array =
    let actors = ResizeArray<ActorState>(int zone.Actors.Count)
    zone.Actors.IteriAlive (fun _ _ actor -> actors.Add actor.State)
    actors.ToArray()

// A zone's waypoints, their chains re-expressed as indices into the returned array. Two passes: collect the live
// refs and their authored values, then resolve each `Next` to the position of the waypoint it names, through the
// runtime's own tolerant accessor (`Zone.waypointNext`). A `Next` whose target is gone is recorded as absent —
// exactly the value the runtime already reads — rather than raising (spec 080, US6).
let private captureWaypoints (zone: Zone) : ScenarioWaypoint array =
    let live = ResizeArray<EntityRef * Waypoint>(int zone.Waypoints.Count)
    zone.Waypoints.IteriAlive (fun index generation waypoint -> live.Add(waypointRef index generation, waypoint))

    let position = Dictionary<EntityRef, int>(int zone.Waypoints.Count)
    live |> Seq.iteri (fun i (ref, _) -> position.[ref] <- i)

    live
    |> Seq.map (fun (ref, waypoint) ->
        let next =
            Zone.waypointNext zone ref
            |> Option.bind (fun target ->
                match position.TryGetValue target with
                | true, i -> Some i
                | false, _ -> None)
        { Base = waypoint.Base; Next = next })
    |> Seq.toArray

/// The staging a zone would be captured as (spec 079): its environment (`None` when it has none), its light and
/// its placed props. `capture` files one, and a fork compares a live zone's staging with a scenario's.
let captureStaging (zone: Zone) : ZoneStaging =
    { Environment = Zone.captureTemplate zone
      Light = zone.Light
      Skydome = zone.Skydome
      Props = Zone.captureProps zone |> Array.map snd }

/// Take a zone as a scenario holds it: its staging (environment, light, placed props) and its actors, waypoints
/// and triggers as arrays. Pure and device-free — it reads the live zone and nothing else — so it can be asserted
/// with no window. De-refs every entity, and resolves the waypoint chains to indices.
let capture (zone: Zone) : ScenarioZone =
    let triggers = ResizeArray<TriggerState>(int zone.Triggers.Count)
    zone.Triggers.IteriAlive (fun _ _ trigger -> triggers.Add trigger.State)

    { Staging = captureStaging zone
      Actors = captureActors zone
      Waypoints = captureWaypoints zone
      Triggers = triggers.ToArray() }
