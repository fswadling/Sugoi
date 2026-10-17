module NarrativeVocabularyTests

// T015: canonical text names a construct and its arguments, which is what a report and a test
// failure read. Since D34 it is a *rendering* and nothing more: a fact is stored, indexed and
// identified by its value, so a gap here is a gap in what a reader is shown rather than in what the
// engine knows.
//
// Only the writing direction is asserted, because there is nothing to read text back with: nothing
// in the engine decodes, since events arrive as values and history is held in memory (research D32).
// That is why these are literal expected strings rather than "whatever it wrote, it can read".

open System.Globalization
open Xunit
open Narrative
open Roster

let private aiko = Aiko
let private akane = Akane
let private shinji = Shinji

[<Fact>]
let ``every event construct writes its canonical text`` () =
    Assert.Equal("Arrived(Aiko)", Canonical.eventText (Arrived aiko))
    Assert.Equal("TraitAssigned(Aiko, Shy)", Canonical.eventText (TraitAssigned(aiko, Shy)))
    Assert.Equal("FriendshipStarted(Aiko, Akane)", Canonical.eventText (FriendshipStarted(aiko, akane)))
    Assert.Equal("FriendshipEnded(Aiko, Akane)", Canonical.eventText (FriendshipEnded(aiko, akane)))
    Assert.Equal("CrushStarted(Aiko, Akane)", Canonical.eventText (CrushStarted(aiko, akane)))
    Assert.Equal("CrushEnded(Aiko, Akane)", Canonical.eventText (CrushEnded(aiko, akane)))
    Assert.Equal("RelationshipStarted(Aiko, Akane)", Canonical.eventText (RelationshipStarted(aiko, akane)))
    Assert.Equal("RelationshipEnded(Aiko, Akane)", Canonical.eventText (RelationshipEnded(aiko, akane)))

[<Fact>]
let ``every fact construct writes its canonical text`` () =
    Assert.Equal("HasTrait(Aiko, Shy)", Canonical.factText (HasTrait(aiko, Shy)))
    Assert.Equal("Friends(Aiko, Akane)", Canonical.factText (Friends(aiko, akane)))
    Assert.Equal("AttractedTo(Aiko, Akane)", Canonical.factText (AttractedTo(aiko, akane)))
    Assert.Equal("InRelationship(Aiko, Akane)", Canonical.factText (InRelationship(aiko, akane)))
    Assert.Equal("JealousOf(Aiko, Akane)", Canonical.factText (JealousOf(aiko, akane)))
    Assert.Equal("FriendshipThreatened(Aiko, Akane)", Canonical.factText (FriendshipThreatened(aiko, akane)))
    Assert.Equal("BottledUpFeelings(Aiko, Akane)", Canonical.factText (BottledUpFeelings(aiko, akane)))

[<Fact>]
let ``canonical text is Construct(arg, arg)`` () =
    Assert.Equal("Friends(Aiko, Akane)", Canonical.factText (Friends(aiko, akane)))
    Assert.Equal("Arrived(Aiko)", Canonical.eventText (Arrived aiko))
    Assert.Equal("TraitAssigned(Aiko, Shy)", Canonical.eventText (TraitAssigned(aiko, Shy)))

[<Fact>]
let ``a mutual fact is one fact whichever way its names are ordered`` () =
    // FR-009, clarification 1: the order the names were given in must not distinguish
    // facts, or a rule premise written the other way round silently matches nothing. Since D33 the
    // invariant is carried by the *value* rather than by the text: the engine puts a fact into
    // canonical participant order as it stores it, so both wordings are one entry.
    Assert.Equal<Fact>(Vocabulary.sortParticipants (Friends(akane, aiko)), Vocabulary.sortParticipants (Friends(aiko, akane)))
    Assert.Equal<Fact>(
        Vocabulary.sortParticipants (InRelationship(akane, aiko)),
        Vocabulary.sortParticipants (InRelationship(aiko, akane)))

[<Fact>]
let ``a mutual fact written the other way round finds the fact rather than making one`` () =
    let reason = { Rule = "seeded"; Premises = []; Origin = Projected }
    let _, held = Facts.add (Friends(akane, aiko)) reason Facts.empty
    let _, again = Facts.add (Friends(aiko, akane)) reason held

    // The second wording added a reason to the one fact rather than a second fact.
    Assert.Equal<Fact>([ Friends(aiko, akane) ], Facts.toList again)
    Assert.Equal(1, Facts.reasonsFor (Friends(aiko, akane)) again |> List.length)

[<Fact>]
let ``an event keeps the order it was committed in`` () =
    // The other half of the same decision: history records what happened, including who
    // acted, so only facts are normalised — an event's text reads as committed (research D22).
    Assert.NotEqual<string>(
        Canonical.eventText (RelationshipStarted(aiko, akane)),
        Canonical.eventText (RelationshipStarted(akane, aiko)))
    Assert.Equal("RelationshipStarted(Shinji, Akane)", Canonical.eventText (RelationshipStarted(shinji, akane)))

[<Fact>]
let ``a directional relation keeps its argument order`` () =
    // The other half of the same decision: where a construct does not declare its
    // participants interchangeable, order carries meaning and must survive.
    Assert.Equal<Fact>(AttractedTo(aiko, akane), Vocabulary.sortParticipants (AttractedTo(aiko, akane)))
    Assert.NotEqual<Fact>(AttractedTo(aiko, akane), Vocabulary.sortParticipants (AttractedTo(akane, aiko)))
    Assert.Equal<Fact>(JealousOf(aiko, akane), Vocabulary.sortParticipants (JealousOf(aiko, akane)))
    Assert.NotEqual<Fact>(JealousOf(aiko, akane), Vocabulary.sortParticipants (JealousOf(akane, aiko)))

[<Fact>]
let ``the ordering is the same in every culture`` () =
    // FR-021, research D13 and D28. Turkish is the culture that catches this: it is where a
    // culture-sensitive comparison of `i` and `I` disagrees with an ordinal one, so an ordering
    // taken from the ambient culture changes here and nowhere else the suite runs.
    // Three producers, because the three things that get ordered are ordered by different means:
    // a fact set by the value (which compares no strings at all), the report by rendered text
    // ordinally, and a reason by its rule's name — the one string comparison in the engine that is
    // F#'s structural one, and so the one the third producer exists to hold to account.
    let bySet () =
        [ HasTrait(aiko, Shy)
          Friends(aiko, akane)
          AttractedTo(aiko, shinji)
          InRelationship(akane, shinji)
          JealousOf(aiko, akane)
          BottledUpFeelings(aiko, akane) ]
        |> List.fold (fun set fact -> Facts.add fact { Rule = "seeded"; Premises = []; Origin = Projected } set |> snd) Facts.empty
        |> Facts.toList
        |> List.map Canonical.factText

    let byReport () =
        (Harness.report (NarrativeTestSupport.headlineEngine ())).Split('\n') |> Array.toList

    let byStructuralSort () = [ "Isumi"; "isumi"; "IZUMI"; "Aiko"; "aiko" ] |> List.sort

    let inCulture (name: string) (order: unit -> string list) =
        let previous = CultureInfo.CurrentCulture

        try
            CultureInfo.CurrentCulture <- CultureInfo.GetCultureInfo name
            order ()
        finally
            CultureInfo.CurrentCulture <- previous

    for order in [ bySet; byReport; byStructuralSort ] do
        let invariant = inCulture "" order
        Assert.Equal<string list>(invariant, inCulture "tr-TR" order)
        Assert.Equal<string list>(invariant, inCulture "en-US" order)
