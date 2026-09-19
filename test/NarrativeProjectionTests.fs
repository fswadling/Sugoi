module NarrativeProjectionTests

// T019: projections. History to facts, with an optional event that withdraws what the
// projection established (FR-011, research D19).
//
// These tests carry their own ad-hoc rules: the behaviour under test is the projection
// mechanism, so depending on the MVP units would leave a failure unable to say which of the
// two was wrong.

open Xunit
open Narrative

let private aiko = Aiko
let private akane = Akane

let private befriended =
    Rules.projection
        "befriended"
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

/// Establishes and never withdraws, so the two halves of a projection are separable.
let private attracted =
    Rules.projection
        "crush-started"
        { EstablishedBy =
            function
            | CrushStarted (a, b) -> ValueSome(struct (a, b))
            | _ -> ValueNone
          WithdrawnBy = None
          Yield = fun (struct (a, b)) -> Fact.AttractedTo(a, b) }

let private rules =
    { Projections = [ befriended; attracted ]
      Derivations = [] }

let private run (events: (GameTime * Event) list) =
    let arrivals = [ (GameTime -10, Arrived aiko); (GameTime -10, Arrived akane) ]
    List.fold Engine.commit (Engine.create rules) (arrivals @ events)

let private at time event = (GameTime time, event)

[<Fact>]
let ``an establishing event projects its fact`` () =
    let engine = run [ at 0 (FriendshipStarted(aiko, akane)) ]
    Assert.Contains(Fact.Friends(aiko, akane), Engine.facts engine)

[<Fact>]
let ``a projected fact carries the rule and the event it applied to`` () =
    // FR-010: attribution is the deliverable, not a diagnostic.
    let engine = run [ at 0 (FriendshipStarted(aiko, akane)) ]
    let reasons = Engine.reasonsFor engine (Fact.Friends(aiko, akane))
    Assert.Single(reasons) |> ignore
    Assert.Equal("befriended", reasons[0].Rule)
    Assert.Equal<Premise>([ FromEvent(FriendshipStarted(aiko, akane)) ], reasons[0].Premises)

[<Fact>]
let ``the withdrawing event removes what the projection established`` () =
    let engine = run [ at 0 (FriendshipStarted(aiko, akane)); at 1 (FriendshipEnded(aiko, akane)) ]
    Assert.DoesNotContain(Fact.Friends(aiko, akane), Engine.facts engine)

[<Fact>]
let ``withdrawing leaves history alone`` () =
    // FR-002, FR-011: history keeps both the beginning and the end.
    let engine = run [ at 0 (FriendshipStarted(aiko, akane)); at 1 (FriendshipEnded(aiko, akane)) ]

    let beginningsAndEndings =
        Engine.history engine
        |> List.filter (fun committed ->
            match committed.Event with
            | FriendshipStarted _
            | FriendshipEnded _ -> true
            | _ -> false)

    Assert.Equal(2, List.length beginningsAndEndings)

[<Fact>]
let ``a withdrawal with nothing to withdraw is a no-op rather than a failure`` () =
    // Semantic oddity is accepted where structural problems are not (research D19).
    let engine = run [ at 0 (FriendshipEnded(aiko, akane)) ]
    Assert.Empty(Engine.facts engine)
    Assert.Equal(3, Engine.history engine |> List.length)

[<Fact>]
let ``an ending given in the reverse order still withdraws`` () =
    // The silent trap the interchangeable declaration exists to remove: without it the
    // friendship would stay true forever, and nothing would say so (research D22).
    let engine = run [ at 0 (FriendshipStarted(aiko, akane)); at 1 (FriendshipEnded(akane, aiko)) ]
    Assert.DoesNotContain(Fact.Friends(aiko, akane), Engine.facts engine)

[<Fact>]
let ``friendship, ended, friendship again reports only the current state`` () =
    let engine =
        run [
            at 0 (FriendshipStarted(aiko, akane))
            at 1 (FriendshipEnded(aiko, akane))
            at 2 (FriendshipStarted(aiko, akane)) ]

    // Reported once, with exactly one reason: the reason from before the ending is gone, so
    // nothing has to be invalidated and no history of states leaks through (Edge Cases).
    Assert.Contains(Fact.Friends(aiko, akane), Engine.facts engine)
    Assert.Single(Engine.reasonsFor engine (Fact.Friends(aiko, akane))) |> ignore

[<Fact>]
let ``an event no projection matches concludes nothing`` () =
    // FR-006: accepted, and nothing follows from it. Not a failure.
    let engine = run [ at 0 (CrushEnded(aiko, akane)) ]
    Assert.Empty(Engine.facts engine)

[<Fact>]
let ``a projection that declares no withdrawal keeps its fact`` () =
    let engine = run [ at 0 (CrushStarted(aiko, akane)) ]
    Assert.Contains(Fact.AttractedTo(aiko, akane), Engine.facts engine)
