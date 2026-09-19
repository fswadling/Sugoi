namespace Narrative

// The authoring harness: run a scenario with no graphical interface, and get back the
// history and the facts in a form that can be diffed and asserted (FR-025–FR-028). The
// harness takes the simulation's place when the game is absent and a scenario script takes
// the director's, and both reach the engine the same way — as committed events.
//
// This is where rendering lives. The engine reports values in the order the values have; putting
// them in an order a reader wants is a presentation decision, so it is made here and nowhere else
// (research D34).

open System

type Scenario =
    { Name : string
      /// The starting situation, as pre-existing events (FR-003). A scenario that wants a
      /// character in the story from the start names her `Arrived` event here.
      InitialEvents : (GameTime * Event) list
      /// Committed after the initial history, in order.
      Steps : (GameTime * Event) list }

type RunResult =
    { History : CommittedEvent list
      Facts : Fact list
      /// The rendering of the engine that produced them, so a failed assertion can attach
      /// the whole state rather than a bare comparison (FR-028).
      Report : string }

module Harness =

    /// The facts in the order this file presents them: by canonical text, ordinally so that the
    /// order does not depend on the ambient culture (FR-021, FR-026).
    let private byText (facts: Fact list) : Fact list =
        facts |> List.sortWith (fun a b -> Canonical.ordinal (Canonical.factText a) (Canonical.factText b))

    /// History and facts with their reasons, stable and diffable. One route to a rendering:
    /// `RunResult.Report` is this applied to the run's engine, so an assertion and a
    /// caller's display cannot diverge.
    ///
    /// Premises appear in the order the rule bound them, which is the order the rule reads in.
    /// A reason is *identified* by its premises as a set rather than by their order — that is what
    /// `Facts.canonical` does — but the order they are held in is left alone, because printing them
    /// as the rule reads them is the point of a report.
    let report (engine: Engine.Engine) : string =
        let lines = ResizeArray<string>()
        lines.Add "history"

        for committed in Engine.history engine do
            let (GameTime time) = committed.Time
            lines.Add(sprintf "    %d  %d  %s" committed.Index time (Canonical.eventText committed.Event))

        lines.Add ""
        lines.Add "facts"

        for fact in byText (Engine.facts engine) do
            lines.Add(sprintf "    %s" (Canonical.factText fact))

            for reason in Engine.reasonsFor engine fact do
                let kind =
                    match reason.Premises with
                    | FromEvent _ :: _ -> "projected by"
                    | _ -> "inferred by"

                let premises = reason.Premises |> List.map Facts.premiseText |> String.concat ", "
                lines.Add(sprintf "        %s \"%s\" from %s" kind reason.Rule premises)

        String.Join("\n", lines)

    /// Run a scenario with an explicit derivation bound, so the failure path is reachable
    /// deliberately (FR-014). The scenario's events are committed in order, which is a fold of
    /// `Engine.commit`.
    let runBounded (bound: int) (rules: RuleSet) (scenario: Scenario) : RunResult =
        let started =
            scenario.InitialEvents
            |> List.fold Engine.commit (Engine.createBounded bound rules)

        let finished = scenario.Steps |> List.fold Engine.commit started

        { History = Engine.history finished
          Facts = Engine.facts finished
          Report = report finished }

    /// Run a scenario with no graphical interface, through the ordinary commit path with no
    /// privileged route (FR-005, FR-025).
    let run (rules: RuleSet) (scenario: Scenario) : RunResult =
        runBounded Engine.DefaultBound rules scenario

    let private factsText (facts: Fact list) : string =
        facts
        |> List.map (fun fact -> Canonical.factText (Vocabulary.sortParticipants fact))
        |> List.sortWith Canonical.ordinal
        |> List.map (fun text -> "    " + text)
        |> String.concat "\n"

    /// Raise on mismatch, attaching the whole report rather than a bare comparison
    /// (FR-028). A failure should read as a narrative diff, not as "expected true, got
    /// false".
    let shouldDerive (result: RunResult) (expected: Fact list) : unit =
        // Compared as values, not as text: a fact is its own identity (D33), and its interchangeable
        // participants are put in canonical order as it is stored, so an expected fact written the
        // other way round is the same fact (research D22). Text appears only in the message.
        let asSet (facts: Fact list) =
            facts |> List.map Vocabulary.sortParticipants |> Set.ofList

        if asSet result.Facts <> asSet expected then
            failwithf "expected these facts:\n%s\n\nbut the run reports:\n%s\n\nwhole report:\n%s"
                (factsText expected) (factsText result.Facts) result.Report
