module ScenarioRegistry

// The scenario library (spec 070; string-keyed since spec 083): the contents recorded under each scenario — a set of
// zones, each filed as a ref-free `ScenarioZone`, together with the cue to run. The library is an **ordered map keyed
// by scenario name**: a key present with `None` is a scenario the author has named but not registered yet, `Some
// contents` is a registered one. There is no closed vocabulary — a name is a plain string and the map's keys *are*
// the vocabulary (spec 083).
//
// A scenario's zones carry no active place: that belongs to the place (or the gameplay state), not to the narrative.
//
// Listed after `world/ScenarioZone.fs`, whose capture it files, and after `world/Cue.fs`, which owns the cue a
// capture carries.

open System.Collections.Generic
open Prime
open ScenarioZone
open Zone

/// One scenario's authored contents. The zones are `ScenarioZone` records — ref-free, keyed by the place each
/// sits at and held in an immutable `Map` so the value is safe to keep as an undo snapshot; `Cue` is the screen's
/// authored cue (an immutable value, so a reference). Nothing here names a slot in the zone it was captured from,
/// so a fork can materialise it in a fresh zone.
type ScenarioContents =
    { Zones : Map<string, ScenarioZone>
      Cue : Cue.Cue }

/// The scenario library: the contents recorded under each scenario name, in the order the names were added. A key
/// present with `None` is a scenario that has been named but not registered; `Some contents` is a registered one.
/// `OMap` is persistent, so a change produces a new library — a before/after pair is a complete undo snapshot.
type Registry = OMap<string, ScenarioContents option>

/// The empty library.
let create () : Registry = OMap.ofSeq1 []

/// The scenario names, in the order they were added — what the panel lists and a cue chooser offers.
let names (registry: Registry) : string list = OMap.toSeq registry |> Seq.map fst |> List.ofSeq

/// Whether `name` is in the library, registered or not.
let exists (name: string) (registry: Registry) : bool = OMap.containsKey name registry

/// The contents filed under `name`, or nothing when the scenario is absent or not yet registered. An expected
/// absence, so an `option` rather than a sentinel, and this is what the panel and the fork op ask.
let tryContents (name: string) (registry: Registry) : ScenarioContents option =
    match OMap.tryFind name registry with
    | Some (Some contents) -> Some contents
    | _ -> None

/// Name a new scenario, with no contents yet. A name already present is left exactly as it was, so a duplicate add
/// never clears a registered scenario's contents (spec 083 FR-007) — a bare `OMap.add` would overwrite the value.
let add (name: string) (registry: Registry) : Registry =
    if exists name registry then registry else OMap.add name None registry

/// Remove a scenario: its name and any contents recorded under it.
let remove (name: string) (registry: Registry) : Registry = OMap.remove name registry

/// The one write: `Some` files contents under a scenario, `None` clears them but keeps the name. Keeps the key's
/// position when the name is already present.
let setContents (name: string) (contents: ScenarioContents option) (registry: Registry) : Registry =
    OMap.add name contents registry

/// The name a panel or a cue stores: surrounding whitespace trimmed, so `" x "` and `"x"` are the same scenario.
/// Matching is otherwise exact (ordinal — `"Intro"` and `"intro"` differ), the by-name rule `specs/074` set.
let normalise (name: string) : string = name.Trim()

/// Whether `name` may be named as a new scenario: it survives trimming and is not already in the library. The
/// panel's Add control asks this — `add` itself refuses a duplicate only by leaving it unchanged, and does not refuse
/// a blank (spec 083 FR-007).
let canAdd (name: string) (registry: Registry) : bool =
    let name = normalise name
    name <> "" && not (exists name registry)

// ── The capture ──

/// Take the current scene as a scenario's contents: every present zone, through `ScenarioZone.capture` (its staging
/// and its narrative entities) keyed by the place it sits at; and the cue. Pure and device-free — it reads the live
/// zones and nothing else — so it can be asserted with no window. Each zone is read exactly as the author left it (a
/// deleted part is absent, a duplicate appears twice), and it is de-ref'd, so the value names no live slot.
let capture (zones: Dictionary<string, Zone>) (cue: Cue.Cue) : ScenarioContents =
    { Zones = zones |> Seq.map (fun pair -> pair.Key, ScenarioZone.capture pair.Value) |> Map.ofSeq
      Cue = cue }
