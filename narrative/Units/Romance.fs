namespace Narrative

// Unit: romance. Crushes and relationships, and what they make somebody feel about a third
// party. It reads facts the friendship unit concludes and vice versa — nothing scopes a
// fact to the unit that concluded it (FR-018).

module Romance =

    /// "crush-started" — a crush is attraction. `CrushEnded` withdraws it, instantiated
    /// through its own bindings, so "a crush, then it ends" reports no attraction without
    /// any reason ever being invalidated (research D19).
    let attraction : Projection =
        Rules.projection "crush-started"
            { EstablishedBy =
                function
                | CrushStarted (a, b) -> ValueSome(struct (a, b))
                | _ -> ValueNone
              WithdrawnBy =
                Some(
                    function
                    | CrushEnded (a, b) -> ValueSome(struct (a, b))
                    | _ -> ValueNone
                )
              Yield = fun (struct (a, b)) -> Fact.AttractedTo(a, b) }

    /// "together" — going out is a relationship, in either direction.
    let relationship : Projection =
        Rules.projection "together"
            { EstablishedBy =
                function
                | RelationshipStarted (a, b) -> ValueSome(struct (a, b))
                | _ -> ValueNone
              WithdrawnBy =
                Some(
                    function
                    | RelationshipEnded (a, b) -> ValueSome(struct (a, b))
                    | _ -> ValueNone
                )
              Yield = fun (struct (a, b)) -> Fact.InRelationship(a, b) }

    /// "jealousy" — wanting somebody who is going out with somebody else. The partner is
    /// whichever name in the relationship is not the one wanted, since the construct is
    /// mutual — and the relationship is looked up by the name already bound rather than by
    /// scanning every relationship there is (research D22, D7).
    let jealousy : Derivation =
        Rules.derivation "jealousy" (fun facts ->
            facts {
                let! struct (a, wanted) = Facts.Sources.attractedTo
                let! struct (x, y) = Facts.Sources.partnersOf wanted
                let partner = if x = wanted then y else x

                if a <> partner then
                    yield Fact.JealousOf(a, partner)
            })

    let rules : RuleSet =
        { Projections = [ attraction; relationship ]
          Derivations = [ jealousy ] }
