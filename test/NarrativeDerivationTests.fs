module NarrativeDerivationTests

// T020: derivations. Facts to facts, recursing to a fixed point, every conclusion carrying
// the rule and the premises it applied to (FR-008–FR-012, FR-014).
//
// T029: SC-001, the headline situation, end to end.
//
// The mechanism tests carry their own ad-hoc rules so a failure points at one thing. Two of
// them conclude the same fact by different routes, which is what makes the multi-reason and
// surviving-reason cases testable at all.

open System.Diagnostics
open Xunit
open Narrative
open Roster

let private aiko = Aiko
let private akane = Akane
let private shinji = Shinji

// T008/F-79: the projections are the shared test-only declarations in `NarrativeTestSupport`;
// the derivations below stay local because they are what these tests are about.

let private jealousy =
    Rules.derivation "jealousy" [ FactKind.AttractedTo; FactKind.InRelationship ] (fun facts ->
        facts {
            let! struct (a, wanted) = Facts.Sources.attractedTo
            let! struct (x, y) = Facts.Sources.partnersOf wanted
            let partner = if x = wanted then y else x

            if a <> partner then
                yield Fact.JealousOf(a, partner)
        })

/// One route to a threatened friendship: it is with somebody you are jealous of.
let private threatenedByFriendship =
    Rules.derivation "threatened-by-friendship" [ FactKind.JealousOf; FactKind.Friends ] (fun facts ->
        facts {
            let! struct (a, c) = Facts.Sources.jealousOf
            let! struct (x, y) = Facts.Sources.friendsOf a
            let friend = if x = a then y else x

            if friend = c then
                yield Fact.FriendshipThreatened(a, c)
        })

/// A second, independent route to the same fact: it is with somebody you are jealous of and
/// you are hot-headed about it. Independent premises are what FR-010's multi-reason case
/// needs.
let private threatenedByTemper =
    Rules.derivation "threatened-by-temper" [ FactKind.JealousOf; FactKind.HasTrait ] (fun facts ->
        facts {
            let! struct (a, c) = Facts.Sources.jealousOf
            let! struct (_, ascribed) = Facts.Sources.traitsOf a

            if ascribed = HotHeaded then
                yield Fact.FriendshipThreatened(a, c)
        })

let private bottledUp =
    Rules.derivation "bottled-up" [ FactKind.FriendshipThreatened; FactKind.HasTrait ] (fun facts ->
        facts {
            let! struct (a, c) = Facts.Sources.friendshipThreatened
            let! struct (_, ascribed) = Facts.Sources.traitsOf a

            if ascribed = Shy then
                yield Fact.BottledUpFeelings(a, c)
        })

let private rules =
    { Projections =
        [ NarrativeTestSupport.befriended
          NarrativeTestSupport.attraction
          NarrativeTestSupport.relationship
          NarrativeTestSupport.traitAssigned ]
      Derivations = [ jealousy; threatenedByFriendship; threatenedByTemper; bottledUp ] }

/// Aiko likes Shinji, Shinji is going out with Akane, Aiko and Akane are friends, and Aiko
/// is both shy and hot-headed — which is what makes the two routes to the same fact
/// available at once.
let private seed : (GameTime * Event) list =
    [ (GameTime -1000, Arrived aiko)
      (GameTime -1000, Arrived akane)
      (GameTime -1000, Arrived shinji)
      (GameTime -900, TraitAssigned(aiko, Shy))
      (GameTime -900, TraitAssigned(aiko, HotHeaded))
      (GameTime -800, FriendshipStarted(aiko, akane))
      (GameTime -300, CrushStarted(aiko, shinji))
      (GameTime -100, RelationshipStarted(shinji, akane)) ]

let private runRules (ruleSet: RuleSet) (extra: (GameTime * Event) list) =
    List.fold Engine.commit (Engine.create ruleSet) (seed @ extra)

let private run (extra: (GameTime * Event) list) = runRules rules extra

let private reasonsOf engine fact =
    Engine.reasonsFor engine fact |> List.map (fun r -> r.Rule) |> List.sortWith Canonical.ordinal

// --- T104: a nested premise concluded by a later pass ------------------------------------

/// A trait concluded from the threatened friendship, so it only enters the fact set in the pass
/// after `threatened` fires.
let private moody =
    Rules.derivation "moody" [ FactKind.FriendshipThreatened ] (fun facts ->
        facts {
            let! struct (a, _) = Facts.Sources.friendshipThreatened
            yield Fact.HasTrait(a, Shy)
        })

/// Reads the outer premise and a nested trait premise. The trait is derived in the pass after the
/// threatened friendship, so the rule must be re-enabled when the trait arrives — which only
/// happens if `Mentions` names it (F-08).
let private nestedPremise =
    Rules.derivation "nested-premise" [ FactKind.FriendshipThreatened; FactKind.HasTrait ] (fun facts ->
        facts {
            let! struct (a, c) = Facts.Sources.friendshipThreatened
            let! struct (_, ascribed) = Facts.Sources.traitsOf a

            if ascribed = Shy then
                yield Fact.BottledUpFeelings(a, c)
        })

let private nestedRules =
    { Projections =
        [ NarrativeTestSupport.befriended
          NarrativeTestSupport.attraction
          NarrativeTestSupport.relationship ]
      Derivations = [ jealousy; threatenedByFriendship; moody; nestedPremise ] }

/// No trait event: the shy trait is only ever concluded, in a later pass than the threat.
let private nestedSeed : (GameTime * Event) list =
    [ (GameTime -1000, Arrived aiko)
      (GameTime -1000, Arrived akane)
      (GameTime -1000, Arrived shinji)
      (GameTime -800, FriendshipStarted(aiko, akane))
      (GameTime -300, CrushStarted(aiko, shinji))
      (GameTime -100, RelationshipStarted(shinji, akane)) ]

[<Fact>]
let ``conclusions of conclusions are reached`` () =
    // FR-008: the fixpoint, not one pass. BottledUpFeelings is three rules away from any
    // event that was committed.
    let engine = run []
    Assert.Contains(Fact.BottledUpFeelings(aiko, akane), Engine.facts engine)

[<Fact>]
let ``a fact reached by two independent chains is reported once, carrying both`` () =
    // FR-009, FR-010, SC-002.
    let engine = run []
    let facts = Engine.facts engine
    Assert.Equal(1, facts |> List.filter (fun f -> f = Fact.FriendshipThreatened(aiko, akane)) |> List.length)
    Assert.Equal<string>(
        [ "threatened-by-friendship"; "threatened-by-temper" ],
        reasonsOf engine (Fact.FriendshipThreatened(aiko, akane)))

[<Fact>]
let ``reasons are returned in the stored canonical order`` () =
    // FR-044: the stored list is already canonical, so a reader gets it without a re-sort. Rule
    // order must not leak into that order either (FR-019), so a reversed rule set reads the same.
    let expected = [ "threatened-by-friendship"; "threatened-by-temper" ]

    let reasonsFrom ruleSet =
        let engine = runRules ruleSet []
        Engine.reasonsFor engine (Fact.FriendshipThreatened(aiko, akane)) |> List.map (fun r -> r.Rule)

    Assert.Equal<string>(expected, reasonsFrom rules)
    Assert.Equal<string>(expected, reasonsFrom { rules with Derivations = List.rev rules.Derivations })

[<Fact>]
let ``no fact is reported twice however many reasons support it`` () =
    let engine = run []
    let facts = Engine.facts engine
    Assert.Equal(List.length (List.distinct facts), List.length facts)

[<Fact>]
let ``when one reason ceases to hold the fact survives with exactly the surviving reason`` () =
    // SC-003, FR-011: the friendship ends, so the route through it goes, and the route
    // through being hot-headed remains.
    let engine = run [ (GameTime 0, FriendshipEnded(aiko, akane)) ]
    Assert.Contains(Fact.FriendshipThreatened(aiko, akane), Engine.facts engine)
    Assert.Equal<string>([ "threatened-by-temper" ], reasonsOf engine (Fact.FriendshipThreatened(aiko, akane)))

[<Fact>]
let ``a reason whose premises stopped holding stops being offered`` () =
    // FR-012: what a conclusion is attributed to reflects the current situation, not the
    // situation it was concluded in.
    let engine = run [ (GameTime 0, FriendshipEnded(aiko, akane)) ]
    Assert.DoesNotContain("threatened-by-friendship", reasonsOf engine (Fact.FriendshipThreatened(aiko, akane)))

[<Fact>]
let ``a conclusion that loses one route survives, and what depends on it survives too`` () =
    // The friendship ends, so the route to the threatened friendship through it goes, but the
    // hot-headed route remains — and BottledUpFeelings reads the threatened friendship rather than
    // the friendship, so it is unaffected (FR-011).
    let engine = run [ (GameTime 0, FriendshipEnded(aiko, akane)) ]
    Assert.Contains(Fact.FriendshipThreatened(aiko, akane), Engine.facts engine)
    Assert.Contains(Fact.BottledUpFeelings(aiko, akane), Engine.facts engine)

[<Fact>]
let ``an inferred fact whose only support is withdrawn disappears`` () =
    // Research D19: only a projection's own withdrawal touches the projected set, and the inferred
    // set is recomputed from what is left (D7) — so an inferred fact is retracted by its projected
    // support going, with no withdrawal step of its own and no route left to re-derive it.
    let before = NarrativeTestSupport.headlineEngine ()
    Assert.Contains(Fact.JealousOf(aiko, akane), Engine.facts before)

    let after = Engine.commit before (GameTime 0, RelationshipEnded(shinji, akane))

    Assert.DoesNotContain(Fact.JealousOf(aiko, akane), Engine.facts after)
    Assert.DoesNotContain(Fact.FriendshipThreatened(aiko, akane), Engine.facts after)
    Assert.DoesNotContain(Fact.BottledUpFeelings(aiko, akane), Engine.facts after)

[<Fact>]
let ``an event no rule matches concludes nothing`` () =
    // FR-006. Here there are rules, so this can actually fail if the engine concluded
    // something from an event nothing reads. The facts are left as the same value, so a commit
    // that no projection reads does not rebuild them either (F-81).
    let before = run []
    let after = Engine.commit before (GameTime 0, CrushEnded(shinji, akane))

    Assert.Contains(Fact.JealousOf(aiko, akane), Engine.facts after)
    Assert.Same(before.Facts, after.Facts)

[<Fact>]
let ``a conclusion does not depend on which way round a mutual fact was recorded`` () =
    // The silent trap the interchangeable declaration exists to remove. Here the friendship
    // is started as (Akane, Aiko) — the reverse of the seed — and the jealous-friend rule
    // must still fire. Without the participant lookup it would match nothing and say
    // nothing, which is the failure mode D22 is about.
    let reversed =
        seed
        |> List.map (fun (time, event) ->
            match event with
            | FriendshipStarted (a, b) -> time, FriendshipStarted(b, a)
            | other -> time, other)

    let engine = List.fold Engine.commit (Engine.create rules) reversed
    Assert.Contains(Fact.FriendshipThreatened(aiko, akane), Engine.facts engine)
    Assert.Contains(Fact.BottledUpFeelings(aiko, akane), Engine.facts engine)

[<Fact>]
let ``a nested premise that appears in a later pass re-enables the rule`` () =
    // T104, F-08: `nested-premise` reads FriendshipThreatened (pass 2) and HasTrait, which
    // `moody` only concludes in pass 3. Without HasTrait on the rule's Mentions it would run once
    // in pass 3, find no trait, and never be tried again.
    let engine = List.fold Engine.commit (Engine.create nestedRules) nestedSeed

    Assert.Contains(Fact.HasTrait(aiko, Shy), Engine.facts engine)
    Assert.Contains(Fact.BottledUpFeelings(aiko, akane), Engine.facts engine)
    Assert.Equal<string>([ "nested-premise" ], reasonsOf engine (Fact.BottledUpFeelings(aiko, akane)))

[<Fact>]
let ``exhausting the derivation bound is reported, naming the rules involved`` () =
    // FR-014, SC-006: the bound is injectable precisely so this path is driven rather than
    // assumed, since a well-formed rule set cannot reach it.
    let watch = Stopwatch.StartNew()
    // No budget at all: the first conclusion considered trips it. The point of the injectable
    // bound is that this path is driven rather than assumed, since a well-formed rule set
    // cannot reach the default of a million.
    let engine = Engine.createBounded 0 rules
    let raised =
        try
            List.fold Engine.commit engine seed |> ignore
            None
        with DerivationBoundExceeded names -> Some names

    watch.Stop()
    match raised with
    | None -> Assert.Fail "the bound was not reported"
    | Some names ->
        // The report names the rules that were still concluding, not the whole rule set:
        // only jealousy produced anything before the budget ran out.
        Assert.Equal<string>([ "jealousy" ], names)

    Assert.True(watch.ElapsedMilliseconds < 1000L, sprintf "reported after %d ms" watch.ElapsedMilliseconds)

// --- T029: SC-001 ---------------------------------------------------------------------

[<Fact>]
let ``SC-001: the headline situation adds exactly three conclusions to the five stated facts`` () =
    let engine = NarrativeTestSupport.headlineEngine ()

    // Expected in the form the engine stores: a mutual fact is held in canonical participant order,
    // so this is what comes back rather than the wording the event used (research D33).
    let stated =
        [ Fact.HasTrait(aiko, Shy)
          Fact.Friends(aiko, akane)
          Fact.AttractedTo(aiko, shinji)
          Fact.AttractedTo(shinji, akane)
          Fact.InRelationship(akane, shinji) ]

    let conclusions =
        [ Fact.JealousOf(aiko, akane)
          Fact.FriendshipThreatened(aiko, akane)
          Fact.BottledUpFeelings(aiko, akane) ]

    let byText = List.sortWith (fun a b -> Canonical.ordinal (Canonical.factText a) (Canonical.factText b))
    Assert.Equal<Fact>(byText (stated @ conclusions), byText (Engine.facts engine))

[<Fact>]
let ``SC-001: none of the conclusions appears in history`` () =
    let engine = NarrativeTestSupport.headlineEngine ()
    let historyTexts = Engine.history engine |> List.map (fun e -> Canonical.eventText e.Event)

    for conclusion in [ Fact.JealousOf(aiko, akane); Fact.FriendshipThreatened(aiko, akane); Fact.BottledUpFeelings(aiko, akane) ] do
        Assert.DoesNotContain(Canonical.factText conclusion, historyTexts)

[<Fact>]
let ``SC-001: each conclusion names the rule that produced it and the premises it applied to`` () =
    // The reasoning is the deliverable: a fact set alone would pass on an implementation
    // that cannot explain itself.
    let engine = NarrativeTestSupport.headlineEngine ()

    let premisesOf fact =
        Engine.reasonsFor engine fact
        |> List.collect (fun r -> r.Premises)
        |> List.choose (function FromFact f -> Some f | _ -> None)
        |> List.sortWith (fun a b -> Canonical.ordinal (Canonical.factText a) (Canonical.factText b))

    Assert.Equal<string>([ "jealousy" ], reasonsOf engine (Fact.JealousOf(aiko, akane)))
    Assert.Equal<Fact>(
        [ Fact.AttractedTo(aiko, shinji); Fact.InRelationship(akane, shinji) ],
        premisesOf (Fact.JealousOf(aiko, akane)))
    Assert.Equal<Fact>([ Fact.Friends(aiko, akane); Fact.JealousOf(aiko, akane) ], premisesOf (Fact.FriendshipThreatened(aiko, akane)))
    Assert.Equal<Fact>(
        [ Fact.FriendshipThreatened(aiko, akane); Fact.HasTrait(aiko, Shy) ],
        premisesOf (Fact.BottledUpFeelings(aiko, akane)))
