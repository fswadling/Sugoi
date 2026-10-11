module RosterTests

// The roster is declared once, in `narrative/Roster.fs`, and is the narrative engine's own vocabulary
// (`specs/074-retire-cast` moved it in from the old `cast/` cluster). These are the facts about the
// declaration itself: it is complete — every case enumerated, none listed twice — and every case it
// enumerates renders. Canonical text labels a character through `characterName`, so a case the
// enumeration reaches and the rendering does not would be a label miss.
//
// This opens `Roster` alone (not `Narrative`): the union is a standalone declaration below the engine.

open Xunit
open Roster

[<Fact>]
let ``the roster enumerates every character once`` () =
    // `Roster.characters` is what a caller uses to build a scenario out of the whole roster.
    Assert.Equal(4, List.length characters)
    Assert.Equal(4, characters |> List.distinct |> List.length)

[<Fact>]
let ``the roster renders every case it enumerates`` () =
    // Canonical text labels a character by `characterName`, and this is the only check that fails if a
    // case is added and left unnamed — the compiler's incomplete match on `characterName` is a warning,
    // not an error.
    let rendered = characters |> List.map characterName
    // Distinct, so a case whose rendering was mis-wired in the match is caught rather than shown
    // twice under one label.
    Assert.Equal(characters.Length, rendered |> List.distinct |> List.length)
