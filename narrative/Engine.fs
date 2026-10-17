namespace Narrative

// The engine's public surface: commit events, read back what is currently true, and ask
// why. Facts are derived from history and are not writable by any other means — there is
// no way to assert a fact and no way to alter a committed event, which is absence rather
// than discouragement (FR-007, FR-032).

open System
open Roster

module Engine =

    type Engine =
        { History : History.State
          Rules : RuleSet
          Bound : int
          /// Every current fact and the reasons it holds. A reason says whether an event or a rule
          /// put it there, so there is one fact set rather than a projected one and an inferred one
          /// beside it (FR-011, research D7, D19).
          Facts : Facts.Set }

    /// One evaluation is one completed match of a rule's premises, which is the unit that
    /// grows with the number of facts and rules (research D9).
    [<Literal>]
    let DefaultBound = 1_000_000

    /// Create an engine with the default bound.
    let create (rules: RuleSet) : Engine =
        { History = History.empty
          Rules = rules
          Bound = DefaultBound
          Facts = Facts.empty }

    /// Create an engine with an explicit bound. F# permits optional arguments only on type
    /// members, so the bound is a second entry point rather than an optional parameter; it
    /// exists so the reporting path is reachable deliberately (research D9, SC-006).
    let createBounded (bound: int) (rules: RuleSet) : Engine =
        { create rules with Bound = bound }

    /// The facts after this event: every projection's withdrawal applied before any projection's
    /// establishment, so an event that both ends one thing and begins another takes effect in that
    /// order, and no projection can withdraw what another has just established (research D19).
    ///
    /// The flag reports whether the *set* of projected facts changed; a match that only edits
    /// reasons does not change what the rules can conclude.
    let private applyProjections
        (projections: Projection list)
        (event: Event)
        (facts: Facts.Set)
        : struct (bool * Facts.Set) =
        let mutable changed = false

        let withdrawn =
            projections
            |> List.fold
                (fun acc projection ->
                    let struct (removed, next) = Projection.withdraw event acc projection
                    changed <- changed || removed
                    next)
                facts

        let established =
            projections
            |> List.fold
                (fun acc projection ->
                    let struct (novel, next) = Projection.establish event acc projection
                    changed <- changed || novel
                    next)
                withdrawn

        struct (changed, established)

    /// Whether any projection reads this event. Asked before the inferred layer is dropped, so an
    /// event no projection reads costs no rebuild at all (F-81).
    let private anyProjectionMatches (projections: Projection list) (event: Event) : bool =
        projections
        |> List.exists (fun projection ->
            (projection.Establishes event).IsSome || (projection.Withdraws event).IsSome)

    /// Give a projected fact set new reasons while keeping the inferred reasons already held for
    /// those facts. Sound only when the projected *set* is unchanged, because a rule reads facts
    /// rather than their reasons, so the inference result cannot have moved (F-81).
    let private mergeReasons (projected: Facts.Set) (current: Facts.Set) : Facts.Set =
        projected
        |> Map.fold
            (fun acc fact reasons ->
                let inferred =
                    match Map.tryFind fact current with
                    | Some held -> held |> List.filter (fun reason -> reason.Origin = Inferred)
                    | None -> []

                Map.add fact (Facts.canonical (reasons @ inferred)) acc)
            current

    /// Commit one event, at the game time the caller gives it. Its time must not precede the
    /// last committed one, or the commit raises `TimeWentBackwards` having changed nothing, so
    /// the caller still holds a usable engine.
    ///
    /// Committing a run is a fold of this function. There is no batch entry point: the engine
    /// commits one event per call, and a caller with several folds them, so the unit the engine
    /// works in is the unit the simulation supplies.
    let commit (engine: Engine) (time, event) : Engine =
        let history = History.append engine.History (time, event)

        // No projection reads this event: history moves and the facts cannot have moved, so the
        // inferred layer is not even dropped.
        let facts =
            if not (anyProjectionMatches engine.Rules.Projections event) then engine.Facts
            else

            // The projected layer is recovered by dropping what the rules concluded last time, this
            // event is applied to it, and the rules run to a fixed point over the result. Nothing is
            // patched: a conclusion is recomputed from the projected facts every commit, which is
            // what lets a fact an event ends and a fact a rule concludes be the same kind of thing
            // (research D7, D19).
            let projected = Facts.dropInferred engine.Facts
            let struct (changed, after) = applyProjections engine.Rules.Projections event projected

            // A matched projection that only edited reasons keeps the inference result, so it does
            // not pay for a derivation pass either (F-81).
            if changed then Derive.infer engine.Rules.Derivations engine.Bound after
            else mergeReasons after engine.Facts

        { engine with
            History = history
            Facts = facts }

    /// Every current fact, in the order the values have.
    let facts (engine: Engine) : Fact list = Facts.toList engine.Facts

    /// Every fact naming this character, in any position, unfiltered (FR-015).
    let factsOf (engine: Engine) (character: Character) : Fact list =
        Facts.ofCharacter character engine.Facts

    /// The rules and premises currently supporting a fact, in canonical order, whether an event or
    /// a rule put them there. Empty when the fact is not current (FR-010).
    let reasonsFor (engine: Engine) (fact: Fact) : Reason list =
        Facts.reasonsFor fact engine.Facts

    /// The committed history, in order. Never rewritten (FR-002).
    let history (engine: Engine) : CommittedEvent list = History.toList engine.History
