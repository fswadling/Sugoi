module NarrativeRuleSetTests

// T030, T034: a unit is a module of rules reading one shared fact set, and where those rules
// sit relative to other units' changes nothing about the result (FR-018, FR-019, SC-007).
//
// Two of these assertions (FR-002 and FR-020) are here rather than in the commit tests
// because with no rules in the engine there is nothing for them to be about — an assertion
// that cannot fail is worse than no assertion.

open Xunit
open Narrative

let private unitRules = [ Cast.rules; Romance.rules; Friendship.rules ]

/// Every observable thing the engine will say, rendered canonically. This is the shape the
/// report takes, so byte-identity across permutations can be asserted before the report
/// rendering exists.
let private render (engine: Engine.Engine) : string =
    let lines =
        [ for fact in Engine.facts engine do
            yield sprintf "fact %s" (Canonical.factText fact)
            for reason in Engine.reasonsFor engine fact do
                let premises =
                    reason.Premises
                    |> List.map Facts.premiseText
                    |> List.sortWith Canonical.ordinal
                    |> String.concat " + "
                yield sprintf "  %s <- %s" reason.Rule premises
          for committed in Engine.history engine do
            yield sprintf "event %d %s" committed.Index (Canonical.eventText committed.Event) ]

    String.concat "\n" lines

/// Every ordering of the units, permuted by index because a rule set holds functions and so
/// has no structural equality to permute by.
let private permutations (count: int) : int list list =
    let rec go remaining =
        match remaining with
        | [] -> [ [] ]
        | _ ->
            [ for head in remaining do
                for tail in go (List.filter (fun x -> x <> head) remaining) do
                    yield head :: tail ]

    go [ 0 .. count - 1 ]

[<Fact>]
let ``SC-007: every permutation of unit order produces identical output`` () =
    let baseline =
        render (List.fold Engine.commit (Engine.create NarrativeTestSupport.mvpRules) NarrativeTestSupport.headline)

    for order in permutations (List.length unitRules) do
        let units = order |> List.map (fun i -> unitRules[i])

        let engine =
            List.fold Engine.commit (Engine.create (Rules.combine units)) NarrativeTestSupport.headline

        Assert.Equal(baseline, render engine)

[<Fact>]
let ``a rule in one unit reads a fact another unit concluded`` () =
    // Friendship's "threatened" reads JealousOf, which Romance concluded: nothing scopes a
    // fact to the unit that produced it (FR-018).
    let engine = NarrativeTestSupport.headlineEngine ()
    Assert.Equal<string>(
        [ "jealousy" ],
        Engine.reasonsFor engine (Fact.JealousOf(NarrativeTestSupport.aiko, NarrativeTestSupport.akane))
        |> List.map (fun r -> r.Rule))
    Assert.Equal<string>(
        [ "threatened" ],
        Engine.reasonsFor engine (Fact.FriendshipThreatened(NarrativeTestSupport.aiko, NarrativeTestSupport.akane))
        |> List.map (fun r -> r.Rule))

[<Fact>]
let ``a rule set missing a unit still runs and simply concludes less`` () =
    // Units are independent: dropping one leaves the others working (FR-019's other half).
    let withoutFriendship = Rules.combine [ Cast.rules; Romance.rules ]
    let engine =
        List.fold Engine.commit (Engine.create withoutFriendship) NarrativeTestSupport.headline

    Assert.Contains(Fact.JealousOf(NarrativeTestSupport.aiko, NarrativeTestSupport.akane), Engine.facts engine)
    Assert.DoesNotContain(Fact.FriendshipThreatened(NarrativeTestSupport.aiko, NarrativeTestSupport.akane), Engine.facts engine)

[<Fact>]
let ``a replaced rule set rebuilds from unchanged history and revisits no committed event`` () =
    // FR-002, FR-020: history is untouched by a change of rules, and all current state is
    // rebuilt from it under the new set.
    let original = NarrativeTestSupport.headlineEngine ()
    let replaced =
        List.fold Engine.commit (Engine.create (Rules.combine [ Cast.rules; Romance.rules ]))
            NarrativeTestSupport.headline

    Assert.Equal<CommittedEvent>(Engine.history original, Engine.history replaced)

    Assert.Equal<int>(
        Engine.history original |> List.map (fun e -> e.Index),
        Engine.history replaced |> List.map (fun e -> e.Index))

    // Different rules, different conclusions, same history.
    Assert.NotEqual(Engine.facts original |> List.length, Engine.facts replaced |> List.length)

[<Fact>]
let ``two rules of the same name are refused`` () =
    // A rule's name is a reason's identity, so a duplicate makes a conclusion unattributable.
    let twin =
        Rules.projection
            "trait-assigned"
            { EstablishedBy = function TraitAssigned (a, t) -> ValueSome(struct (a, t)) | _ -> ValueNone
              WithdrawnBy = None
              Yield = fun (struct (a, t)) -> Fact.HasTrait(a, t) }

    Assert.ThrowsAny<exn>(fun () ->
        Rules.combine [ Cast.rules; { Rules.empty with Projections = [ twin ] } ] |> ignore)
    |> ignore

[<Fact>]
let ``combining the shipped units declares each rule name once`` () =
    Rules.combine unitRules |> ignore
