namespace Narrative

// Unit: a character's traits. One thread of story, owning its rules and nothing else — the
// facts it reads and concludes are shared with every other unit (FR-018).
//
// Arrival is not here either. An `Arrived` event records that a character entered the story,
// but it gates nothing: any event naming any roster member is admissible, so arrival is
// narrative content a unit may project like any other event rather than bookkeeping the engine
// maintains (plan.md § Structure Decision).

module Cast =

    /// "trait-assigned" — a trait an event assigned is a trait the character has.
    let traitAssigned : Projection =
        Rules.projection "trait-assigned"
            { EstablishedBy =
                function
                | TraitAssigned (a, ascribed) -> ValueSome(struct (a, ascribed))
                | _ -> ValueNone
              WithdrawnBy = None
              Yield = fun (struct (a, ascribed)) -> Fact.HasTrait(a, ascribed) }

    let rules : RuleSet = { Rules.empty with Projections = [ traitAssigned ] }
