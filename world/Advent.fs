module Advent

// The named adventure flags (spec 055): story milestones — "the opening is complete", "the player has
// met Akane". A cue or trigger will branch on membership; the flags are a `Set<Advent>` on
// `SharedState`, the fact those branches read.
//
// A milestone is a **closed union**, not a string, for the reasons `cast/Roster.fs` gives for its
// cast: a milestone cannot be misspelled, matching on one is exhaustive, and nothing about a
// milestone's identity is decided by a string comparison. The game's own declaration grows by adding
// a case, which the build reports every match of — `name` below is an explicit match for that reason.
//
// The flags are F#'s `Set`: immutable, and ordered by a union value's case tag (declaration order),
// so iterating them is deterministic, and a change produces a new set — which is what makes an edit a
// before/after snapshot on the one undo history (`docs/EDITOR_DESIGN.md`, D-011). They are `Set<Advent>`
// and are manipulated with the standard `Set` functions; this module adds only the two things `Set`
// cannot know: the cases' enumeration (`all`) and their display names (`name`).
//
// It names no zone, screen, asset or device, so it loads on its own. It lives on `SharedState` rather
// than a zone because a flag outlives every zone (spec 055 FR-002).

open Microsoft.FSharp.Reflection

/// A story milestone. The game's own declaration, like `Roster.Character`: add a case to add a
/// milestone, and the build reports every match that must account for it.
type Advent =
    | OpeningComplete
    | MetAiko
    | MetAkane
    | MetShinji
    | MetYuki

/// Every milestone, in declaration order — enumerated from the type rather than listed again, so a
/// case cannot be left out of the panel (the way `Roster.characters` is enumerated).
let all : Advent list =
    FSharpType.GetUnionCases typeof<Advent>
    |> Array.toList
    |> List.map (fun case -> unbox<Advent> (FSharpValue.MakeUnion(case, [||])))

/// The renderable name of a milestone — the case name, so the panel and any future report spell it
/// the same way. An explicit match rather than `%A`: adding a case fails the build here until it is
/// named, and it stays off F#'s formatting path.
let name (advent: Advent) : string =
    match advent with
    | OpeningComplete -> nameof OpeningComplete
    | MetAiko -> nameof MetAiko
    | MetAkane -> nameof MetAkane
    | MetShinji -> nameof MetShinji
    | MetYuki -> nameof MetYuki
