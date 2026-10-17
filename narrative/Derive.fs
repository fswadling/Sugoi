namespace Narrative

// The fixpoint over the fact set. Inferred facts are recomputed from the projected ones on
// every commit rather than patched, which is what makes the two requirements about facts
// appearing and disappearing true by construction (FR-011, FR-013, research D7).

open System
open System.Collections.Generic

/// Raised, rather than returned, because reaching the bound is not caller data: with a
/// finite vocabulary and a closed roster a well-formed rule set cannot reach it, so it means
/// the rule set is malformed (research D9). Raising is safe because committing does not
/// mutate the value passed in, so the caller still holds a usable engine.
exception DerivationBoundExceeded of rules: string list

module Derive =

    /// The constructs the given facts belong to, which is what a rule is said to read.
    let private constructsOf (facts: Fact list) : Set<FactKind> =
        facts |> List.map Vocabulary.factKind |> Set.ofList

    /// The state between passes: the facts so far, the index over them, the evaluations the bound
    /// still allows, and the rules a further pass could still move.
    type private Snapshot =
        { Current : Facts.Set
          Index : ParticipantIndex
          Remaining : int
          Pending : Derivation list }

    /// Run the rules to a fixed point over the given facts — the projected layer — and return them
    /// with everything the rules concluded added.
    ///
    /// One snapshot per pass, and the result taken when a pass adds nothing. That is what makes rule
    /// order unobservable: a monotone fixpoint over a snapshot converges to the same set whatever
    /// order the rules are tried in (FR-019, research D14).
    ///
    /// Passes after the first evaluate only the rules that read a construct the previous pass gained.
    /// That is not an optimisation in the usual sense — it was the difference between passing and
    /// failing the throughput criteria when it was measured (research D7).
    ///
    /// The participant index is built once for the projected set and then extended with what each
    /// pass concludes, because a pass only adds facts: rebuilding it per pass was a measurable share
    /// of the cost of a commit.
    let infer (rules: Derivation list) (bound: int) (projected: Facts.Set) : Facts.Set =

        /// One pass over the rules still pending, against the facts as they stand. The snapshot it
        /// leaves is those facts with this pass's conclusions added — or, when the conclusions would
        /// exceed the bound, the pass raises with the rules that were still concluding.
        let pass (snapshot: Snapshot) : Snapshot =
            let mutable current = snapshot.Current
            let mutable gained = []
            let mutable remaining = snapshot.Remaining
            // One entry per rule rather than one per conclusion, and sorted only on the failure path,
            // so naming the rules costs nothing on a normal pass.
            let concluding = HashSet<string>()

            let addConclusion (name: string) (fact: Fact) (premises: Premise list) =
                // One conclusion is what the bound counts, and it is counted as it arrives rather
                // than after every conclusion has been materialised (F-84, research D9).
                if remaining <= 0 then
                    concluding.Add name |> ignore
                    let names = concluding |> Seq.sort |> List.ofSeq
                    raise (DerivationBoundExceeded names)

                remaining <- remaining - 1
                concluding.Add name |> ignore
                let reason = { Rule = name; Premises = premises; Origin = Inferred }
                let novel, updated = Facts.add fact reason current
                current <- updated
                // A further reason for a fact already current enables no new match, so it must not
                // count as the pass having gained anything (FR-009).
                if novel then gained <- fact :: gained

            for rule in snapshot.Pending do
                let conclusions = rule.Evaluate current snapshot.Index
                for fact, premises in conclusions do
                    addConclusion rule.Name fact premises

            let gainedConstructs = constructsOf gained

            { Current = current
              Index = ParticipantIndex.addFacts gained snapshot.Index
              Remaining = remaining
              Pending =
                if Set.isEmpty gainedConstructs then []
                else
                    rules
                    |> List.filter (fun rule ->
                        List.exists (fun mentioned -> Set.contains mentioned gainedConstructs) rule.Mentions) }

        let start =
            { Current = projected
              Index = ParticipantIndex.ofFacts (Facts.toList projected)
              Remaining = bound
              Pending = rules }

        let rec toFixpoint (state: Snapshot) : Snapshot =
            if List.isEmpty state.Pending then state
            else toFixpoint (pass state)

        (toFixpoint start).Current
