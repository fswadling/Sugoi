module CastingTests

// The binding (spec 032): which actor asset plays a member and a look. The map is sparse and an
// entry's presence is the existence check, so these assert exactly that — a slot resolves to what was
// assigned, an absent one resolves to nothing, and no assignment disturbs another. Nothing here needs
// a GPU device or a loaded asset: the binding holds ids.

open System.Collections.Generic
open Xunit
open Roster
open Casting

let private empty () : Cast = { Bindings = Dictionary() }

let private slot (character: Character) (look: Look) : Slot = { Character = character; Look = look }

[<Fact>]
let ``a slot resolves to what was assigned, and reassigning replaces it`` () =
    let cast = empty ()
    cast.Bindings.[slot Akane Look.SchoolUniform] <- "akane_uniform"
    Assert.Equal(Some "akane_uniform", tryAsset (slot Akane Look.SchoolUniform) cast)

    cast.Bindings.[slot Akane Look.SchoolUniform] <- "akane_uniform_alt"
    Assert.Equal(Some "akane_uniform_alt", tryAsset (slot Akane Look.SchoolUniform) cast)

[<Fact>]
let ``an unassigned slot resolves to nothing, and clearing returns it to that`` () =
    // No default model and no fallback: an absent entry says the member has no model for that look
    // (FR-010).
    let cast = empty ()
    Assert.Equal<string option>(None, tryAsset (slot Akane Look.Casual) cast)

    cast.Bindings.[slot Akane Look.Casual] <- "akane_casual"
    Assert.True(cast.Bindings.Remove(slot Akane Look.Casual))
    Assert.Equal<string option>(None, tryAsset (slot Akane Look.Casual) cast)

[<Fact>]
let ``a member's looks are independent, and two members may share an asset`` () =
    // The existence check, and the stand-in model: assigning one look changes nothing about another,
    // and one asset playing two members is a legitimate assignment rather than a conflict.
    let cast = empty ()
    cast.Bindings.[slot Akane Look.SchoolUniform] <- "akane_uniform"
    cast.Bindings.[slot Shinji Look.SchoolUniform] <- "akane_uniform"

    Assert.Equal(Some "akane_uniform", tryAsset (slot Akane Look.SchoolUniform) cast)
    Assert.Equal(Some "akane_uniform", tryAsset (slot Shinji Look.SchoolUniform) cast)
    Assert.Equal<string option>(None, tryAsset (slot Akane Look.Casual) cast)

[<Fact>]
let ``every member and look is assignable, resolvable and clearable`` () =
    // The whole cross product, because the panel offers all of it and nothing declares which looks a
    // member has (FR-006, SC-003).
    let cast = empty ()

    for character in characters do
        for look in allLooks do
            let value = characterName character
            cast.Bindings.[slot character look] <- value
            Assert.Equal(Some value, tryAsset (slot character look) cast)
            Assert.True(cast.Bindings.Remove(slot character look))

    Assert.Equal(0, cast.Bindings.Count)

[<Fact>]
let ``the look vocabulary renders every case it enumerates`` () =
    // The panel labels each row with `lookName`, and this is the only check that fails if a look is
    // added and left unnamed — the compiler's incomplete match on `lookName` is a warning, not an
    // error.
    let rendered = allLooks |> List.map lookName
    Assert.Equal(allLooks.Length, rendered |> List.distinct |> List.length)
