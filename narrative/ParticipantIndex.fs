namespace Narrative

// The participant index: the facts of each construct, keyed by the character they name, so a rule
// that constrains a join on an argument it has already bound can look the facts up instead of
// re-scanning the whole construct. Without it every join is quadratic in the fact count, which is
// the data-structure mistake research D7 records as the difference between 29.6 ms and 4.0 ms.
//
// Keyed by the construct as a value rather than as its name, so a key needs no rendering. It lives
// here rather than inside `Facts` because the fact set does not need it: it is built for the rules
// that read a set, and handed to them, not stored with it.

/// Facts by construct and participant. A fact naming two characters is filed under both, so a lookup
/// answers for either position — which is what makes a mutual relation match however its names were
/// ordered when it was recorded (research D22).
type ParticipantIndex = Map<FactKind * Character, Fact list>

module ParticipantIndex =

    let empty : ParticipantIndex = Map.empty

    /// File a fact under every participant it names.
    let add (fact: Fact) (index: ParticipantIndex) : ParticipantIndex =
        let kind = Vocabulary.factKind fact

        (index, Vocabulary.factParticipants fact)
        ||> List.fold (fun acc participant ->
            let key = kind, participant
            Map.add key (fact :: defaultArg (Map.tryFind key acc) []) acc)

    /// Add a run of facts. Sound for a fixpoint pass, which only ever adds facts: nothing already
    /// indexed can have gone stale, so the index can be extended rather than rebuilt per pass.
    let addFacts (facts: Fact list) (index: ParticipantIndex) : ParticipantIndex =
        (index, facts) ||> List.fold (fun index fact -> add fact index)

    /// The facts of one construct that name one character, in either position. Empty when there are
    /// none, which is what a keyed source needs: no facts naming a character means the join matches
    /// nothing rather than failing.
    let factsOf (kind: FactKind) (participant: Character) (index: ParticipantIndex) : Fact list =
        match Map.tryFind (kind, participant) index with
        | Some facts -> facts
        | None -> []

    /// The index of a run of facts.
    let ofFacts (facts: Fact list) : ParticipantIndex = addFacts facts empty
