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
    let private applyProjections (projections: Projection list) (event: Event) (facts: Facts.Set) : Facts.Set =
        let withdrawn = projections |> List.fold (Projection.withdraw event) facts
        projections |> List.fold (Projection.establish event) withdrawn

    /// Commit one event, at the game time the caller gives it. Its time must not precede the
    /// last committed one, or the commit raises `TimeWentBackwards` having changed nothing, so
    /// the caller still holds a usable engine.
    ///
    /// Committing a run is a fold of this function. There is no batch entry point: the engine
    /// commits one event per call, and a caller with several folds them, so the unit the engine
    /// works in is the unit the simulation supplies.
    let commit (engine: Engine) (time, event) : Engine =
        let history = History.append engine.History (time, event)

        // The projected layer is recovered by dropping what the rules concluded last time, this
        // event is applied to it, and the rules run to a fixed point over the result. Nothing is
        // patched: a conclusion is recomputed from the projected facts every commit, which is what
        // lets a fact an event ends and a fact a rule concludes be the same kind of thing
        // (research D7, D19).
        let facts =
            engine.Facts
            |> Facts.dropInferred
            |> applyProjections engine.Rules.Projections event
            |> Derive.infer engine.Rules.Derivations engine.Bound

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
        Facts.canonical (Facts.reasonsFor fact engine.Facts)

    /// The committed history, in order. Never rewritten (FR-002).
    let history (engine: Engine) : CommittedEvent list = History.toList engine.History
