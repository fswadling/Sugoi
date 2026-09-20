namespace Narrative

// Canonical text: `Construct(arg, arg)`. This is a rendering and nothing else — it is what the
// harness reports, and what a test reads to say what happened (research D34). It is no longer how a
// fact is stored (that is the value), how the participant index is keyed (that is
// `Vocabulary.factKind`) or how a reason is identified (that is structural comparison).
//
// There is no decoder, because nothing reads text: events arrive as values the game already holds,
// and history is held in memory (research D32).
//
// It renders a fact as it finds it. Putting the participants of an interchangeable construct in
// canonical order is `Vocabulary.sortParticipants`, which the engine applies as a fact is stored, so
// a stored fact is canonical and this is canonical text for it.

open System
open Roster

module Canonical =

    /// Ordinal string comparison, curried, for the sort functions. This is the *renderer's* ordering,
    /// and it is ordinal because the default comparer is culture-sensitive, which would make FR-021
    /// false in a locale nobody tested in. Nothing the engine stores, keys or identifies is ordered
    /// by this.
    let ordinal (a: string) (b: string) : int = String.CompareOrdinal(a, b)

    // --- encoding -------------------------------------------------------------------
    //
    // By matching on the construct rather than by asking the type what it is. `GetUnionFields`
    // re-derives a union's cases on every call, and measured at ~17 us against ~0.3 us for the
    // same text written out, which is what a commit's time used to go on (research D30).

    let private one (character: Character) : string list = [ Roster.characterName character ]

    /// The two names of a fact, in the order the value has them. Where the construct declares its
    /// participants interchangeable, the *value* is the way the engine guarantees that the order they
    /// were written in cannot distinguish two facts (research D22, D33).
    let private pair (a: Character) (b: Character) : string list =
        [ Roster.characterName a; Roster.characterName b ]

    let private eventParts (event: Event) : string * string list =
        match event with
        | Arrived a -> nameof Arrived, one a
        | TraitAssigned (a, ascribed) ->
            nameof TraitAssigned, [ Roster.characterName a; Vocabulary.traitName ascribed ]
        | FriendshipStarted (a, b) -> nameof FriendshipStarted, pair a b
        | FriendshipEnded (a, b) -> nameof FriendshipEnded, pair a b
        | CrushStarted (a, b) -> nameof CrushStarted, pair a b
        | CrushEnded (a, b) -> nameof CrushEnded, pair a b
        | RelationshipStarted (a, b) -> nameof RelationshipStarted, pair a b
        | RelationshipEnded (a, b) -> nameof RelationshipEnded, pair a b

    let private factParts (fact: Fact) : string * string list =
        match fact with
        | HasTrait (a, ascribed) -> nameof HasTrait, [ Roster.characterName a; Vocabulary.traitName ascribed ]
        | Friends (a, b) -> nameof Friends, pair a b
        | AttractedTo (a, b) -> nameof AttractedTo, pair a b
        | InRelationship (a, b) -> nameof InRelationship, pair a b
        | JealousOf (a, b) -> nameof JealousOf, pair a b
        | FriendshipThreatened (a, b) -> nameof FriendshipThreatened, pair a b
        | BottledUpFeelings (a, b) -> nameof BottledUpFeelings, pair a b

    let private render (name: string) (args: string list) : string =
        if List.isEmpty args then name + "()"
        else name + "(" + String.Join(", ", args) + ")"

    let eventText (event: Event) : string =
        let name, args = eventParts event
        render name args

    let factText (fact: Fact) : string =
        let name, args = factParts fact
        render name args
