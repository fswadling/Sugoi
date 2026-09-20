module RosterTests

// The cast is declared once, in `cast/Roster.fs`, and both the engine and the game read that one
// declaration (spec 032). These are the facts about the declaration itself: it is complete — every
// case enumerated, none listed twice — and every case it enumerates renders. The cast panel's tree
// and canonical text both label a character through `characterName`, so a case the enumeration
// reaches and the rendering does not would be a row with no label.
//
// Nothing here opens `Narrative`: the roster is not the engine's, which is the point of the hoist.

open Xunit
open Roster

[<Fact>]
let ``the roster enumerates every character once`` () =
    // `Roster.characters` is what a caller uses to build a scenario out of the whole cast, and what
    // the cast panel walks to draw its tree.
    Assert.Equal(4, List.length characters)
    Assert.Equal(4, characters |> List.distinct |> List.length)

[<Fact>]
let ``the roster renders every case it enumerates`` () =
    // The panel and canonical text both label a character by `characterName`, and this is the only
    // check that fails if a case is added and left unnamed — the compiler's incomplete match on
    // `characterName` is a warning, not an error.
    let rendered = characters |> List.map characterName
    // Distinct, so a case whose rendering was mis-wired in the match is caught rather than shown
    // twice under one label.
    Assert.Equal(characters.Length, rendered |> List.distinct |> List.length)
