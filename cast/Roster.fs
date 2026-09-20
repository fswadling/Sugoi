module Roster

// The cast: who can exist in this story. Declared once, here — outside the narrative engine and
// outside the game — because both name the same cast and neither may own it (spec 032). The
// engine keys its constructs on a character; the game binds each member and look to an actor
// asset in `world/Casting.fs`.
//
// This module is deliberately **asset-free**: it may not reference `Casting`, `Assets`, `Zone`,
// or any render or physics module. It consumes nothing and is compiled first, so the engine can
// see it and still cannot reach the game.

open Microsoft.FSharp.Reflection

/// A member of the cast: everyone who can exist in this story, which is fixed, so the type is
/// closed (research D28). A union rather than a string so that a name cannot be misspelled, so
/// matching on a character is exhaustive, and so nothing about a character's identity is
/// decided by a string comparison.
///
/// The cast is the four the story has: three the MVP rules are written against, and the transfer
/// student. It grows by adding a case, which is one edit here and a build that reports every match
/// that must account for it — nothing else lists the cast.
///
/// The name *is* the case name, which is what canonical text writes — so a report, a test and
/// the code all spell a character the same way. Nothing reads that text back, so renaming a
/// case breaks no stored state (research D32).
///
/// Being in the roster is not the same as being in the story: nothing here says who is in play,
/// and nothing gates an event on it. An `Arrived` event is how history records a character
/// entering the story, so a character can be in the roster long before she walks into it.
///
/// Visual identity is the game's, not the engine's and not this module's: nothing here names a
/// portrait, an asset or an actor.
type Character =
    | Aiko
    | Akane
    | Shinji
    /// The transfer student: in the roster from the start, in the story only once declared.
    | Yuki

/// The renderable name of a character — the case name, so canonical text, the report and the
/// cast panel all spell a character the same way.
///
/// A match on the cases rather than `string` or `ToString()`, both of which go through F#'s
/// formatting machinery for a union. Canonical text is report-facing only — nothing stores,
/// keys or identifies by it — but a report renders every value it shows, and the difference
/// between the two was measured at ~17 us against ~0.3 us a value, which the render regression
/// guard in the performance tests holds (research D30).
let characterName (character: Character) : string =
    match character with
    | Aiko -> nameof Aiko
    | Akane -> nameof Akane
    | Shinji -> nameof Shinji
    | Yuki -> nameof Yuki

/// Everyone in the roster, in declaration order. Enumerated from the type rather than listed
/// again, so a case cannot be left out of it — which matters because a caller building a
/// scenario out of the whole cast has no other way to ask for one, and because the cast panel
/// walks this to draw its tree.
let characters : Character list =
    FSharpType.GetUnionCases typeof<Character>
    |> Array.toList
    |> List.map (fun case -> unbox<Character> (FSharpValue.MakeUnion(case, [||])))
