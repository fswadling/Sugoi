module Scenario

// The game's scenarios: the authored narrative units (spec 070). A scenario is a set of zones — each as the
// author left it — together with the cue to run, captured from the editor (`world/ScenarioRegistry.fs`). A
// closed union, so a scenario that is not declared here cannot exist, exactly as `Place` closes the set of zones.
//
// This module is the **vocabulary**; the store is the sparse map from a scenario to the contents captured under
// it. The same split `Place` (vocabulary) and `ZoneRegistry` (store) use, and it is what lets a later cue op name
// a scenario without depending on the store — as `world/Cue.fs` names a `Place` in its `SetZone` — so this file
// sits before `world/Cue.fs`.
//
// Pure and dependency-free (only reflection, to enumerate itself as `Place.all` does).

open Microsoft.FSharp.Reflection

/// One authored scenario — a unit of narrative: the zones it is made of, and the cue to run.
///
/// The set opens with one case; adding a scenario is a case here, and a build that reports every match that must
/// account for it — nothing else lists them. The name *is* the case name, which is what the panel shows, so a
/// report, a test and the code all spell a scenario the same way.
type Scenario =
    | Intro

/// The renderable name of a scenario — the case name, matched over the cases as `Place.placeName` and `Advent.name`
/// are, so adding a case is a case here and the compiler reports the match.
let scenarioName (scenario: Scenario) : string =
    match scenario with
    | Intro -> nameof Intro

/// Every scenario, in declaration order — derived from the union rather than listed beside it, so a case cannot be
/// left out and the panel walks the vocabulary itself (as `Place.all` does). Computed once, so drawing reflects
/// nothing per frame.
let all : Scenario list =
    FSharpType.GetUnionCases typeof<Scenario>
    |> Array.toList
    |> List.map (fun case -> unbox<Scenario> (FSharpValue.MakeUnion(case, [||])))
