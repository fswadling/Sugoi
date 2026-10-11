module Place

// The game's places: the authored identity of a runtime zone (spec 061). A closed union, so a zone that
// is not declared here cannot exist — a place is a compile-time constant, never created or deleted at run
// time. Only its *presence* in the gameplay state changes at run time.
//
// This module is the **vocabulary**; the registry (`world/ZoneRegistry.fs`) is the sparse **config** store
// keyed by a place. Kept apart, it is why the runtime may key its zones by `Place` without depending on
// the registry.
//
// Pure and dependency-free (only reflection, to enumerate itself as `Roster.characters` does), so it sits
// early — before `world/Cue.fs`, which names a place in its `SetZone`.

open Microsoft.FSharp.Reflection

/// Which sitting of the art room: the same room, lit for a different part of the day.
type Period =
    | Morning
    | Evening

/// One of the game's zones together with one of the variants it declares. The island declares none: it is
/// a fieldless case, so it is one place named for itself, and giving it variants later is an edit to the
/// case rather than an entry in a second list.
type Place =
    | ArtRoom of Period
    | Island

/// The renderable name of a place — `"ArtRoom Morning"`, `"Island"` — matched over the cases as
/// `Advent.name` and `Roster.characterName` are, so adding a variant is a case here and the compiler
/// reports the match. (The registry's panel labels come from the same names, derived there from the union.)
let placeName (place: Place) : string =
    match place with
    | ArtRoom Morning -> "ArtRoom Morning"
    | ArtRoom Evening -> "ArtRoom Evening"
    | Island -> "Island"

/// Every place, in declaration order — the product of the zones' names and their variant sets, derived
/// from the union rather than listed beside it, so adding a variant needs no edit here. Computed once, as
/// `Roster.characters` is, so a caller walking it reflects nothing per frame.
let all : Place list =
    FSharpType.GetUnionCases typeof<Place>
    |> Array.toList
    |> List.collect (fun case ->
        let fields = case.GetFields()
        if fields.Length = 0 then
            [ unbox<Place> (FSharpValue.MakeUnion(case, [||])) ]
        else
        let variants = FSharpType.GetUnionCases fields.[0].PropertyType
        variants
        |> Array.toList
        |> List.map (fun variant -> unbox<Place> (FSharpValue.MakeUnion(case, [| FSharpValue.MakeUnion(variant, [||]) |]))))

/// The place the game starts at (spec 061 FR-006). Stated here rather than implied by declaration order.
let start : Place =
    ArtRoom Morning
