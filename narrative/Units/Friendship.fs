namespace Narrative

// Unit: friendship, and what a friendship costs when something is wrong with it. It reads
// `JealousOf`, which the romance unit concluded, and the romance unit reads `Friends` in
// return — which is the point of a shared fact set (FR-018).

module Friendship =

    /// "befriended" — a friendship, in either order, since the construct declares its
    /// participants interchangeable (research D22).
    let befriended : Projection =
        Rules.projection "befriended"
            { EstablishedBy =
                function
                | FriendshipStarted (a, b) -> ValueSome(struct (a, b))
                | _ -> ValueNone
              WithdrawnBy =
                Some(
                    function
                    | FriendshipEnded (a, b) -> ValueSome(struct (a, b))
                    | _ -> ValueNone
                )
              Yield = fun (struct (a, b)) -> Fact.Friends(a, b) }

    /// "threatened" — being jealous of a friend threatens the friendship. Reads a fact the
    /// romance unit concluded, which is what makes the two units one rule set rather than
    /// two. The friendship is looked up by the jealous character, and which name comes first
    /// in it is irrelevant — that is what the interchangeable declaration buys (research D22).
    let threatened : Derivation =
        Rules.derivation "threatened" (fun facts ->
            facts {
                let! struct (a, c) = Facts.Sources.jealousOf
                let! struct (x, y) = Facts.Sources.friendsOf a
                let friend = if x = a then y else x

                if friend = c then
                    yield Fact.FriendshipThreatened(a, c)
            })

    /// "bottled-up" — a shy person who cannot confront the friend they are jealous of keeps
    /// it to themselves. The trait is a fact another unit concluded, so a unit that has no
    /// opinion about traits can still be the reason this fires.
    let bottledUp : Derivation =
        Rules.derivation "bottled-up" (fun facts ->
            facts {
                let! struct (a, c) = Facts.Sources.friendshipThreatened
                let! struct (_, ascribed) = Facts.Sources.traitsOf a

                if ascribed = Shy then
                    yield Fact.BottledUpFeelings(a, c)
            })

    let rules : RuleSet =
        { Projections = [ befriended ]
          Derivations = [ threatened; bottledUp ] }
