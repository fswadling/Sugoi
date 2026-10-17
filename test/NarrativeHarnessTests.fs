module NarrativeHarnessTests

// T036, T041–T045: the authoring harness. A scenario runs with no game, the report is
// stable and diffable, and generated scenarios are held to invariants across many casts
// rather than one hand-built case.

open FsCheck
open FsCheck.FSharp
open Xunit
open Narrative
open Roster

let private aiko = NarrativeTestSupport.aiko
let private akane = NarrativeTestSupport.akane
let private shinji = NarrativeTestSupport.shinji

let private headlineScenario =
    { Name = "headline"
      InitialEvents = NarrativeTestSupport.headline
      Steps = [] }

// --- T042: the report is the deliverable -------------------------------------------------

/// Written out in full rather than summarised as a count: a test that only counts facts
/// passes on an implementation that cannot explain itself, which would fail the point of the
/// feature. Premises appear in the order the rule bound them.
let private expectedReport =
    """history
    1  -1000  Arrived(Aiko)
    2  -1000  Arrived(Akane)
    3  -1000  Arrived(Shinji)
    4  -900  TraitAssigned(Aiko, Shy)
    5  -800  FriendshipStarted(Aiko, Akane)
    6  -300  CrushStarted(Aiko, Shinji)
    7  -200  CrushStarted(Shinji, Akane)
    8  -100  RelationshipStarted(Shinji, Akane)

facts
    AttractedTo(Aiko, Shinji)
        projected by "crush-started" from CrushStarted(Aiko, Shinji)
    AttractedTo(Shinji, Akane)
        projected by "crush-started" from CrushStarted(Shinji, Akane)
    BottledUpFeelings(Aiko, Akane)
        inferred by "bottled-up" from FriendshipThreatened(Aiko, Akane), HasTrait(Aiko, Shy)
    Friends(Aiko, Akane)
        projected by "befriended" from FriendshipStarted(Aiko, Akane)
    FriendshipThreatened(Aiko, Akane)
        inferred by "threatened" from JealousOf(Aiko, Akane), Friends(Aiko, Akane)
    HasTrait(Aiko, Shy)
        projected by "trait-assigned" from TraitAssigned(Aiko, Shy)
    InRelationship(Akane, Shinji)
        projected by "together" from RelationshipStarted(Shinji, Akane)
    JealousOf(Aiko, Akane)
        inferred by "jealousy" from AttractedTo(Aiko, Shinji), InRelationship(Akane, Shinji)"""

[<Fact>]
let ``the headline report is exactly the quickstart's`` () =
    // The fact line reads `InRelationship(Akane, Shinji)` rather than as the event worded it:
    // a mutual fact's canonical text is its participants in order, so which rule concluded it
    // first cannot change the report. The event line keeps `(Shinji, Akane)`, because history
    // records what happened.
    let result = Harness.run NarrativeTestSupport.mvpRules headlineScenario
    Assert.Equal(expectedReport, result.Report)

// --- FR-015: the facts of a character ----------------------------------------------------
//
// `NarrativeCommitTests` records why this is asserted here rather than there: "the facts of a
// character" needs facts to exist, which needs rules.

[<Fact>]
let ``a character's facts are every fact naming them, in either position`` () =
    // Akane is named second in most of these, which is the point: the lookup is not by the
    // first argument, and nothing is filtered out (FR-015).
    //
    // Rendered as canonical text, and in the order the engine reports facts — by construct, then by
    // the arguments, which is the order the values have (research D34). The value keeps the wording
    // history recorded — the projection stored `InRelationship(Shinji, Akane)` — while its text sorts
    // the participants, which is why the report prints the other order.
    let engine = NarrativeTestSupport.headlineEngine ()

    Assert.Equal<string list>(
        [ "Friends(Aiko, Akane)"
          "AttractedTo(Shinji, Akane)"
          "InRelationship(Akane, Shinji)"
          "JealousOf(Aiko, Akane)"
          "FriendshipThreatened(Aiko, Akane)"
          "BottledUpFeelings(Aiko, Akane)" ],
        Engine.factsOf engine akane |> List.map Canonical.factText)

[<Fact>]
let ``a character the facts say nothing about has no facts`` () =
    // Not an error and not a special case: a character who is not named by a fact has none.
    let engine = NarrativeTestSupport.headlineEngine ()

    Assert.Equal<Fact list>([], Engine.factsOf engine Yuki)

// --- T036: how a failure reads -----------------------------------------------------------

[<Fact>]
let ``a failed assertion attaches the whole report rather than a comparison`` () =
    let result = Harness.run NarrativeTestSupport.mvpRules headlineScenario

    let failure =
        try
            Harness.shouldDerive result [ Fact.HasTrait(aiko, Shy) ]
            None
        with e -> Some e.Message

    match failure with
    | None -> Assert.Fail "an incomplete expectation passed"
    | Some message ->
        Assert.Contains("facts", message)
        Assert.Contains("jealousy", message)

[<Fact>]
let ``an assertion that holds reports nothing`` () =
    let result = Harness.run NarrativeTestSupport.mvpRules headlineScenario
    Harness.shouldDerive result result.Facts

[<Fact>]
let ``an assertion holds however a mutual fact's names are ordered`` () =
    // FR-009's first clarification, and the regression guard for comparing canonical text: the
    // engine recorded `InRelationship(Shinji, Akane)` from the event, and a caller asking about
    // that same fact is entitled to write it the other way round.
    let result = Harness.run NarrativeTestSupport.mvpRules headlineScenario

    let flipped =
        result.Facts
        |> List.map (function
            | InRelationship (a, b) -> InRelationship(b, a)
            | Friends (a, b) -> Friends(b, a)
            | fact -> fact)

    Harness.shouldDerive result flipped

// --- T043: SC-004 ------------------------------------------------------------------------

[<Fact>]
let ``SC-004: the same scenario run one hundred times is identical every time`` () =
    let reference = Harness.run NarrativeTestSupport.mvpRules headlineScenario

    for _ in 1 .. 100 do
        let run = Harness.run NarrativeTestSupport.mvpRules headlineScenario
        Assert.Equal(reference.Report, run.Report)

// --- T041, T045: generated scenarios and the invariants they must satisfy ----------------

type private GeneratedScenario =
    { Cast : Character list
      Traits : (Character * Trait) list
      Friendships : (Character * Character) list
      Crushes : (Character * Character) list
      Relationships : (Character * Character) list }

let private pairGen (cast: Character list) : Gen<Character * Character> =
    Gen.zip (Gen.elements cast) (Gen.elements cast)

/// A cast drawn from the roster, so a generated scenario cannot name a character who does not
/// exist: a closed roster makes an arbitrary name unrepresentable rather than merely unlikely
/// (research D28). Shuffled rather than taken in order, so the scenarios are not all variations
/// of the same four people.
let private castGen : Gen<Character list> =
    Gen.choose (2, 4)
    |> Gen.bind (fun count ->
        Gen.shuffle Roster.characters
        |> Gen.map (fun shuffled -> shuffled |> Array.toList |> List.truncate count))

let private scenarioGen : Gen<GeneratedScenario> =
    castGen
    |> Gen.bind (fun cast ->
        Gen.listOf (Gen.zip (Gen.elements cast) (Gen.elements [ Shy; HotHeaded ]))
        |> Gen.bind (fun traits ->
            Gen.listOf (pairGen cast)
            |> Gen.bind (fun friendships ->
                Gen.listOf (pairGen cast)
                |> Gen.bind (fun crushes ->
                    Gen.listOf (pairGen cast)
                    |> Gen.map (fun relationships ->
                        { Cast = cast
                          Traits = traits
                          Friendships = friendships
                          Crushes = crushes
                          Relationships = relationships })))))

/// Events in non-decreasing game time, with each character's arrival before the relations
/// that name her.
let private eventsOf (scenario: GeneratedScenario) : (GameTime * Event) list =
    [ for i, character in List.indexed scenario.Cast -> GameTime i, Arrived character ]
    @ [ for i, (character, ascribed) in List.indexed scenario.Traits -> GameTime(100 + i), TraitAssigned(character, ascribed) ]
    @ [ for i, (a, b) in List.indexed scenario.Friendships -> GameTime(200 + i), FriendshipStarted(a, b) ]
    @ [ for i, (a, b) in List.indexed scenario.Crushes -> GameTime(300 + i), CrushStarted(a, b) ]
    @ [ for i, (a, b) in List.indexed scenario.Relationships -> GameTime(400 + i), RelationshipStarted(a, b) ]

let private engineOf (scenario: GeneratedScenario) : Engine.Engine =
    List.fold Engine.commit (Engine.create NarrativeTestSupport.mvpRules) (eventsOf scenario)

[<Fact>]
let ``T045: every generated scenario satisfies the invariants`` () =
    let rules = NarrativeTestSupport.mvpRules

    let ruleNames =
        Set.ofList (
            (rules.Projections |> List.map (fun p -> p.Name))
            @ (rules.Derivations |> List.map (fun d -> d.Name)))

    let check (scenario: GeneratedScenario) : bool =
        let engine = engineOf scenario
        let current = Engine.facts engine

        for fact in current do
            let reasons = Engine.reasonsFor engine fact

            if List.isEmpty reasons then
                failwithf "%s is reported with no reason at all" (Canonical.factText fact)

            for reason in reasons do
                if not (Set.contains reason.Rule ruleNames) then
                    failwithf "%s is attributed to %s, which is not a rule in the set" (Canonical.factText fact) reason.Rule

                for premise in reason.Premises do
                    match premise with
                    | FromFact premiseFact ->
                        if not (List.contains premiseFact current) then
                            failwithf "%s is attributed to %s, which is not currently reported"
                                (Canonical.factText fact) (Canonical.factText premiseFact)
                    | FromEvent _ -> () // a committed event always holds: history is immutable

        // Replay: the same scenario twice, from the same data, must read identically.
        if Harness.report (engineOf scenario) <> Harness.report engine then
            failwith "the same generated scenario did not replay identically"

        true

    Check.One(Config.QuickThrowOnFailure, Prop.forAll (Arb.fromGen scenarioGen) check)

[<Fact>]
let ``T045: a shy cast fires bottled-up and a hot-headed one does not`` () =
    // FR-027: the behaviour is checked against the two distinct trait values rather than 200
    // identical builds. The assertion is still two-sided: a rule that fired always, or never,
    // fails one of them.
    let scenario ascribed =
        { Cast = [ aiko; akane; shinji ]
          Traits = [ (aiko, ascribed) ]
          Friendships = [ (aiko, akane) ]
          Crushes = [ (aiko, shinji) ]
          Relationships = [ (shinji, akane) ] }

    let fires ascribed =
        List.contains (Fact.BottledUpFeelings(aiko, akane)) (Engine.facts (engineOf (scenario ascribed)))

    Assert.True(fires Shy, "bottled-up did not fire for a shy cast")
    Assert.False(fires HotHeaded, "bottled-up fired for a hot-headed cast")
