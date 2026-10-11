module ScenarioRegistry

// The contents captured under each scenario (spec 070; trimmed to narrative entities by spec 076): a scenario is a
// set of zones — each recorded as a ref-free `ScenarioZone` — together with the cue to run. The store is sparse, as
// `ZoneRegistry` is: an entry says a scenario has been registered, an absent one says nobody has captured it yet.
//
// A scenario's zones carry no staging (spec 076): no environment, no light, no player designation; and the contents
// carry no active place. Those belong to the place (or the gameplay state), not to the narrative.
//
// Listed after `world/ScenarioZone.fs`, whose capture it files, and after `world/Cue.fs`, which owns the cue a
// capture carries.

open System.Collections.Generic
open Scenario
open ScenarioZone
open Zone

/// One scenario's authored contents. The zones are `ScenarioZone` records — ref-free, keyed by the place each
/// sits at and held in an immutable `Map` so the value is safe to keep as an undo snapshot; `Cue` is the screen's
/// authored cue (an immutable value, so a reference). Nothing here names a slot in the zone it was captured from,
/// so a fork can materialise it in a fresh zone.
type ScenarioContents =
    { Zones : Map<string, ScenarioZone>
      Cue : Cue.Cue }

/// The scenario library: the contents registered under each scenario. Sparse deliberately: an entry says a
/// scenario has been registered, and an absent one says nobody has captured it yet.
type Registry =
    { Entries : Dictionary<Scenario, ScenarioContents> }

let create () : Registry =
    { Entries = Dictionary() }

/// The registry's question: the contents filed under this scenario, or nothing when nobody has registered one. An
/// expected absence, so an `option` rather than a sentinel, and this is what the panel asks.
let tryContents (scenario: Scenario) (registry: Registry) : ScenarioContents option =
    match registry.Entries.TryGetValue scenario with
    | true, contents -> Some contents
    | false, _ -> None

/// The one write: `Some` files contents under a scenario, `None` takes them away. The option rule lives here so
/// the state-level runner files `Next` and undoes `Previous` through the same call.
let setContents (scenario: Scenario) (contents: ScenarioContents option) (registry: Registry) : unit =
    match contents with
    | Some contents -> registry.Entries.[scenario] <- contents
    | None -> registry.Entries.Remove scenario |> ignore

// ── The capture ──

/// Take the current scene as a scenario's contents: every present zone, through `ScenarioZone.capture` and keyed
/// by the place it sits at; and the cue. Pure and device-free — it reads the live zones and nothing else — so it
/// can be asserted with no window. Each zone is read exactly as the author left it (a deleted part is absent, a
/// duplicate appears twice), and it is de-ref'd, so the value names no live slot. A zone's staging is not captured
/// (spec 076).
let capture (zones: Dictionary<string, Zone>) (cue: Cue.Cue) : ScenarioContents =
    { Zones = zones |> Seq.map (fun pair -> pair.Key, ScenarioZone.capture pair.Value) |> Map.ofSeq
      Cue = cue }
