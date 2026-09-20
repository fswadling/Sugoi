module NarrativeCommitTests

// T016: the commit path. A commit that raises has changed nothing, so the caller still holds a
// usable engine (FR-013), and the failure is raised rather than returned: the caller supplies
// the order (FR-024), so an event whose time precedes the last committed one is a defect on the
// caller's side rather than data the engine can act on.
//
// Committing a run is a fold of `Engine.commit`, so a refused event leaves the events before it
// committed and nothing after it: the commit is the unit that is whole or nothing, not the run.
//
// Two assertions that belong to this file's subject are deliberately NOT here:
// "an event no rule matches concludes nothing" needs rules to exist for it to be unmatched
// by, and "the facts of a character" needs facts to exist. Both live where there are rules,
// because an assertion that cannot fail is worse than no assertion.

open Xunit
open Narrative
open Roster

let private aiko = Aiko
let private akane = Akane
let private shinji = Shinji

let private engine = Engine.create Rules.empty

[<Fact>]
let ``an event naming a character who has not arrived is accepted`` () =
    // Arrival is narrative content, not a gate: any event naming any roster member is
    // admissible, and `Arrived` records an entry rather than permitting one.
    let committed =
        List.fold Engine.commit engine [
            (GameTime 0, TraitAssigned(aiko, Shy))
            (GameTime 1, FriendshipStarted(aiko, akane)) ]
    Assert.Equal(2, List.length (Engine.history committed))

[<Fact>]
let ``a game time that goes backwards is refused`` () =
    let committed = Engine.commit engine (GameTime 10, Arrived aiko)

    // Raised, not returned: the caller supplies the order (FR-024), so it is a defect on the
    // caller's side rather than data the engine can act on.
    let thrown =
        try
            Engine.commit committed (GameTime 9, TraitAssigned(aiko, Shy)) |> ignore
            None
        with TimeWentBackwards (GameTime t, GameTime p) -> Some(t, p)

    Assert.Equal(Some(9, 10), thrown)

    // The message has to be usable by whoever has to act on it (FR-028).
    Assert.Contains("game time 9", (TimeWentBackwards(GameTime 9, GameTime 10)).Message)
    Assert.Contains("previous committed time 10", (TimeWentBackwards(GameTime 9, GameTime 10)).Message)

[<Fact>]
let ``several events may share a game time`` () =
    // FR-023: their relative order is the caller's, and the dense index makes it repeatable.
    let committed =
        List.fold Engine.commit engine [
            (GameTime 0, Arrived aiko)
            (GameTime 0, Arrived akane)
            (GameTime 0, FriendshipStarted(aiko, akane))
            (GameTime 0, FriendshipStarted(akane, aiko)) ]
    let history = Engine.history committed
    Assert.Equal<int>([ 1; 2; 3; 4 ], (history |> List.map (fun e -> e.Index)))

[<Fact>]
let ``a refused event leaves the events before it committed`` () =
    // A commit is the unit that is whole or nothing, not the run: the fold stops at the refused
    // event, and what came before it stands.
    let mutable state = Engine.commit engine (GameTime 0, Arrived aiko)

    let raised =
        try
            state <- Engine.commit state (GameTime 2, FriendshipStarted(aiko, akane))
            state <- Engine.commit state (GameTime 1, TraitAssigned(shinji, Shy))
            false
        with TimeWentBackwards _ -> true

    Assert.True(raised)
    Assert.Equal(2, List.length (Engine.history state))
    Assert.Equal("FriendshipStarted(Aiko, Akane)", Canonical.eventText (Engine.history state |> List.last).Event)

[<Fact>]
let ``an engine with no committed events has no facts`` () =
    // FR-007: facts are derived from history, so with no events there is nothing to derive and
    // no route by which a caller could put one there.
    Assert.Empty(Engine.history engine)
    Assert.Empty(Engine.facts engine)

[<Fact>]
let ``an event that concludes nothing is accepted rather than refused`` () =
    // FR-006's first half. Distinct from an unrecognised event, which fails: this one the
    // vocabulary has, and simply no rule reads it.
    let committed =
        List.fold Engine.commit engine [
            (GameTime 0, Arrived aiko)
            (GameTime 0, Arrived akane)
            (GameTime 1, FriendshipStarted(aiko, akane)) ]
    Assert.Equal(3, List.length (Engine.history committed))
    Assert.Empty(Engine.facts committed)

[<Fact>]
let ``an arrival recorded twice is accepted`` () =
    // History records what happened; a repeated event is not a structural problem.
    let committed =
        List.fold Engine.commit engine [
            (GameTime 0, Arrived aiko)
            (GameTime 1, Arrived aiko) ]
    Assert.Equal(2, List.length (Engine.history committed))
