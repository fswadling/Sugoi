module Advent

// The named adventure flags (spec 055; a string set since spec 082): story milestones — "the opening is
// complete", "the player has met Akane". A cue or trigger branches on membership; the flags are a set on
// `SharedState`, the fact those branches read.
//
// A milestone is a **plain string**, not a closed union (spec 082). The union could not be misspelled and
// matching on one was exhaustive, but it was compiled in: adding a milestone was a code change, and the
// panel could only offer the cases the build already knew. A string set lets the author name milestones in
// the editor. The trade is that spelling is unchecked and nothing cross-references a cue's name against the
// set — that is the author's responsibility.
//
// The set is `Prime`'s `OSet<string>`: an ordered, persistent set. It iterates in the order names were
// added (the authored order, which is what makes the panel's list stable), de-duplicates, and a change
// produces a new set — which is what makes an edit a before/after snapshot on the one undo history
// (`docs/EDITOR_DESIGN.md`, D-011). F#'s `Set<string>` would order alphabetically and lose the authored
// order; a list would admit duplicates. This module names no zone, screen, asset or device, so it loads on
// its own. It lives on `SharedState` rather than a zone because a flag outlives every zone (spec 055 FR-002).

open Prime

/// The story milestones that have happened — an ordered set of names, read in the order they were added.
type Set = OSet<string>

/// The empty set.
let empty : Set = OSet.ofSeq1 []

/// The set holding `names`, in order — how a caller builds one outright (a fixture, a test).
let ofList (names: string list) : Set = OSet.ofSeq1 names

/// Whether `name` is recorded.
let contains (name: string) (set: Set) : bool = OSet.contains name set

/// The set with `name` recorded.
let add (name: string) (set: Set) : Set = OSet.add name set

/// The set without `name`.
let remove (name: string) (set: Set) : Set = OSet.remove name set

/// The recorded names, in the order they were added — what the panel lists and a cue chooser offers.
let names (set: Set) : string list = OSet.toSeq set |> List.ofSeq

/// The name a panel or a cue stores: surrounding whitespace trimmed, so `" x "` and `"x"` are the same
/// milestone. Matching is otherwise exact (ordinal — `"MetAiko"` and `"metAiko"` differ), the by-name rule
/// `specs/074` set for speakers.
let normalise (name: string) : string = name.Trim()
