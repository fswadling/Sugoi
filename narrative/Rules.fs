namespace Narrative

// Rules are authored in two categories and nothing else, which the type system enforces
// rather than a validation pass (FR-017, research D10). The difference is what they read:
// a projection reads the event being committed, a derivation reads the fact set. Keeping the
// two apart is what removes the need for an event index (research D6).

open System

/// A projection: history to facts. It establishes at most one fact on one event, and may
/// withdraw at most one on a second, instantiated through that second event's own bindings
/// (research D19). An option rather than a list: at most one is what the parts below can yield,
/// so the type says so rather than admitting more.
type Projection =
    { Name : string
      /// The fact this event establishes, if it is one of this rule's events.
      Establishes : Event -> Fact option
      /// The fact this event withdraws, if it is one of this rule's events.
      Withdraws : Event -> Fact option }
    
module Projection =

    /// Remove from the projected facts what this projection's withdrawing pattern matches on this
    /// event, instantiated through the event's own bindings (research D19). A projection that
    /// declares no withdrawal, or whose pattern does not match, leaves the set alone.
    let withdraw (event: Event) (facts: Facts.Set) (projection: Projection) : Facts.Set =
        match projection.Withdraws event with
        | None -> facts
        | Some fact -> Facts.withdraw projection.Name fact facts

    /// Add to the projected facts what this projection's establishing pattern matches on this event,
    /// with the event itself as the reason (FR-010). A projection that does not match leaves the set
    /// alone.
    let establish (event: Event) (facts: Facts.Set) (projection: Projection) : Facts.Set =
        match projection.Establishes event with
        | None -> facts
        | Some fact ->
        let reason = { Rule = projection.Name; Premises = [ FromEvent event ]; Origin = Projected }
        Facts.add fact reason facts

/// The parts of a projection. Named rather than positional because the establishing and
/// withdrawing patterns have the same type, so positional arguments could be swapped
/// silently (research D24).
type ProjectionParts<'T> =
    { /// Requires the shape both patterns bind, so a withdrawal is fully determined.
      EstablishedBy : Event -> 'T voption
      WithdrawnBy : (Event -> 'T voption) option
      Yield : 'T -> Fact }

/// A derivation: facts to facts. Its premises are bound with `let!` and captured by the
/// builder, so attribution is a property of the notation rather than something the author
/// restates (FR-010, research D24).
type Derivation =
    { Name : string
      /// Evaluate against a fact set: the conclusions with the premises each applied to,
      /// and the constructs the rule read. The index is the participant index for that set,
      /// handed in so every rule in a pass shares one build of it rather than making its
      /// own. The reads are recorded at binding time, so they are complete whether or not
      /// anything matched (research D7).
      Evaluate : Facts.Set -> ParticipantIndex -> (Fact * Premise list) array * FactKind list
      /// The constructs this rule reads. Used to skip rules that cannot have changed. Captured
      /// once, at declaration, rather than on every commit: a rule's bindings are fixed by its
      /// notation — the body receives only the builder, which does not expose the fact set — so
      /// what it reads cannot vary between evaluations.
      Mentions : FactKind list }

/// A rule set: rules, and nothing else. Where its rules came from is not observable when
/// it runs, so assembling units in a different order cannot change the result (FR-019).
type RuleSet =
    { Projections : Projection list
      Derivations : Derivation list }

module Rules =

    let empty : RuleSet = { Projections = []; Derivations = [] }

    /// Assemble a rule set from units. A rule's name is a reason's identity, so a rule set
    /// holding two rules of the same name is one whose conclusions could not be attributed
    /// (FR-010) — refused here rather than discovered in a report.
    let combine (units: RuleSet list) : RuleSet =
        let combined =
            { Projections = units |> List.collect (fun unit -> unit.Projections)
              Derivations = units |> List.collect (fun unit -> unit.Derivations) }

        let names =
            (combined.Projections |> List.map (fun p -> p.Name))
            @ (combined.Derivations |> List.map (fun d -> d.Name))

        let duplicates =
            names
            |> List.countBy id
            |> List.filter (fun (_, count) -> count > 1)
            |> List.map fst
            |> List.sort

        if not (List.isEmpty duplicates) then
            failwithf "these rule names appear more than once, so a conclusion could not be attributed to one rule: %s"
                (String.Join(", ", duplicates))

        combined

    /// The derivation builder. `let!` binds a source's facts and records each binding as a
    /// premise, so what a conclusion is attributed to is captured by the notation rather
    /// than restated by the author — which would be one place per rule to get attribution
    /// quietly wrong, since the conclusion would still be correct (FR-010, research D24).
    type DerivationBuilder(facts: Facts.Set, lookup: ParticipantIndex) =
        let reads = ResizeArray<FactKind>()

        /// The constructs this rule read. Recorded when a source is bound rather than when
        /// it matches, so a rule that matched nothing still reports what it looked at
        /// (research D7).
        member _.Reads : FactKind list = List.ofSeq reads

        member _.Bind(source: Facts.Source<'T>, body: 'T -> (Fact * Premise list) array) : (Fact * Premise list) array =
            reads.Add source.Kind
            // A reusable accumulator rather than a list comprehension: a join produces one
            // candidate per matching pair, and at a populated fact set that is tens of
            // thousands per commit, so the difference is measured rather than stylistic.
            let results = ResizeArray<Fact * Premise list>()

            for fact, value in source.Enumerate facts lookup do
                for conclusion, premises in body value do
                    results.Add(conclusion, FromFact fact :: premises)

            results.ToArray()

        member _.Yield(conclusion: Fact) : (Fact * Premise list) array = [| conclusion, [] |]
        member _.YieldFrom(conclusions: (Fact * Premise list) array) = conclusions
        member _.Return(conclusion: Fact) : (Fact * Premise list) array = [| conclusion, [] |]
        member _.Zero() : (Fact * Premise list) array = [||]
        member _.Combine(a: (Fact * Premise list) array, b: (Fact * Premise list) array) = Array.append a b
        member _.Delay(f: unit -> (Fact * Premise list) array) = f ()

    /// Declare a derivation. `body` runs once per evaluation, so the constructs it read are
    /// per-evaluation rather than state carried between commits. `Mentions` is captured once,
    /// against the empty fact set, where every bound source is still visited (research D7).
    let derivation (name: string) (body: DerivationBuilder -> (Fact * Premise list) array) : Derivation =
        let evaluate facts lookup =
            let builder = DerivationBuilder(facts, lookup)
            body builder, builder.Reads

        { Name = name
          Evaluate = evaluate
          Mentions = snd (evaluate Facts.empty ParticipantIndex.empty) }

    /// Declare a projection from its named parts. Deliberately not a computation
    /// expression: there is no sequencing to express, and with a single premise there is
    /// nothing for an author to restate wrongly — so a CE here would be machinery for a
    /// benefit that does not apply, while the named parts remove an argument-order mistake
    /// that positional arguments of the same type would invite (research D24).
    let projection (name: string) (parts: ProjectionParts<'T>) : Projection =
        let yields (pattern: Event -> 'T voption) event =
            match pattern event with
            | ValueSome value -> Some(parts.Yield value)
            | ValueNone -> None

        { Name = name
          Establishes = yields parts.EstablishedBy
          Withdraws =
            fun event ->
                match parts.WithdrawnBy with
                | Some pattern -> yields pattern event
                | None -> None }
