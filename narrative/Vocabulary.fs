namespace Narrative

// The closed vocabulary: one event type, one fact type, one trait type (research D20, D28).
// Adding a construct is one case here, after which the compiler reports every rule that must
// account for it — which is the deliberate cost of the closure (research D16, D26).
//
// The roster is not here either: `narrative/Roster.fs` declares it, compiled just before this module,
// so the engine's constructs can name a character (`specs/074` moved it in from the old `cast/` cluster).

open Roster

/// Traits arrive as events and are never fields on a cast member, so there is no second
/// source of truth for something history already records (spec Assumptions).
type Trait =
    | Shy
    | HotHeaded

/// Committed events. Immutable once committed, and the only source of historical truth.
///
/// Deliberately, no event's participants are interchangeable: history records what happened,
/// including who acted, so an event's argument order is part of the record. The normalisation
/// belongs on the facts a projection concludes from them, which is what makes an ending cancel
/// a beginning however each was worded (research D22).
type Event =
    /// A character enters the story. Narrative content like any other event rather than a gate:
    /// naming a character is always admissible, so this records an arrival rather than
    /// permitting one, and a rule can project it the way it projects any other event.
    | Arrived of Character
    | TraitAssigned of Character * Trait
    | FriendshipStarted of Character * Character
    | FriendshipEnded of Character * Character
    | CrushStarted of Character * Character
    | CrushEnded of Character * Character
    | RelationshipStarted of Character * Character
    | RelationshipEnded of Character * Character

/// Derived facts: the current ruleset's interpretation of history. They may appear,
/// change and disappear, and nothing may write one into history.
///
/// Whether a construct's participants are interchangeable — the order the names are given in
/// carries no meaning, so two orders are one fact and either order satisfies a premise — is
/// declared by `sortParticipants` below, as code rather than as metadata: an attribute on the
/// case is read by nothing while the engine runs, while this match names every case, so adding
/// a construct brings the compiler here (research D22, D33).
type Fact =
    | HasTrait of Character * Trait
    | Friends of Character * Character
    | AttractedTo of Character * Character
    | InRelationship of Character * Character
    | JealousOf of Character * Character
    | FriendshipThreatened of Character * Character
    | BottledUpFeelings of Character * Character

/// Which construct a fact is, as a value rather than as a name. The participant index is keyed by
/// this and a rule is said to read these, so neither the index nor the pass restriction is keyed by
/// text — a construct's *name* is something only rendering needs (research D34).
///
/// Qualified, so a case here cannot be confused with the fact case it names. Before this, both the
/// index and the pass restriction were keyed by a fact's canonical text, which made canonical text
/// the identity of a fact rather than a rendering of it — and is why a clash between an event case
/// name and a fact case name had to be refused at start-up.
[<RequireQualifiedAccess>]
type FactKind =
    | HasTrait
    | Friends
    | AttractedTo
    | InRelationship
    | JealousOf
    | FriendshipThreatened
    | BottledUpFeelings

module Vocabulary =

    /// The renderable name of a trait, matched for the same reason `Roster.characterName` is: a fieldless
    /// union is rendered by matching on its cases rather than by `string` or `ToString`. A trait
    /// appears in far fewer facts than a character does, so this is symmetry rather than a
    /// measured win — but it is one rule for rendering a fieldless union rather than one rule and
    /// one exception to it.
    let traitName (ascribed: Trait) : string =
        match ascribed with
        | Shy -> nameof Shy
        | HotHeaded -> nameof HotHeaded

    /// The participants a fact names, in order: which arguments are people, said by matching on the
    /// construct rather than by asking the type of each argument, because asking was measured at
    /// ~17 us a call and this runs once per fact whenever the participant index is built (research
    /// D30). `HasTrait`'s second argument is a trait, so that one names a single participant.
    let factParticipants (fact: Fact) : Character list =
        match fact with
        | HasTrait (a, _) -> [ a ]
        | Friends (a, b)
        | AttractedTo (a, b)
        | InRelationship (a, b)
        | JealousOf (a, b)
        | FriendshipThreatened (a, b)
        | BottledUpFeelings (a, b) -> [ a; b ]

    /// The construct a fact is. `factParticipants`' companion and the same shape: the vocabulary's
    /// own cases said once as a total match, so adding a fact construct is reported here too.
    let factKind (fact: Fact) : FactKind =
        match fact with
        | HasTrait _ -> FactKind.HasTrait
        | Friends _ -> FactKind.Friends
        | AttractedTo _ -> FactKind.AttractedTo
        | InRelationship _ -> FactKind.InRelationship
        | JealousOf _ -> FactKind.JealousOf
        | FriendshipThreatened _ -> FactKind.FriendshipThreatened
        | BottledUpFeelings _ -> FactKind.BottledUpFeelings

    /// A fact with the participants of an interchangeable construct put in one order, so the two
    /// ways of writing such a fact are one value rather than two. Every other fact is returned
    /// unchanged, order and all.
    ///
    /// This match is where interchangeability is declared: the cases with a reordering arm are the
    /// interchangeable ones. Every case is named, so adding a construct brings the compiler here.
    /// The order is the union's own — declaration order in the roster — compared on the values, so
    /// nothing about it goes through a name or a culture (research D22, D33). Which order is
    /// chosen matters only in that it is one order: a report shows the names either way.
    let sortParticipants (fact: Fact) : Fact =
        let pair (a: Character) (b: Character) =
            if compare a b <= 0 then a, b else b, a

        match fact with
        | Friends (a, b) ->
            let x, y = pair a b
            Friends(x, y)
        | InRelationship (a, b) ->
            let x, y = pair a b
            InRelationship(x, y)
        | HasTrait _
        | AttractedTo _
        | JealousOf _
        | FriendshipThreatened _
        | BottledUpFeelings _ -> fact
