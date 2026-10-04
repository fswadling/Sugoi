module Casting

// The game's half of the cast: which look of a character a slot names, and which discovered actor
// asset plays that slot (spec 032). The roster is `Roster`'s and the engine's; none of this is the
// engine's business, and all of it refers to assets.
//
// The binding is **sparse**: it holds an entry only for a member-and-look the game has art for. That
// entry is also the existence check — nothing declares which looks a member has, so not every member
// needs every look, and an absent entry means "no model here" rather than an error.
//
// This is the **binding** half. The **runtime** half — which actor in the scene plays which character —
// is `ActorState.Cast` (authored) plus the gameplay state's cast index, keyed by `Character` rather than
// by a `Slot`, because the scene holds one actor per character, not one per look (spec 063).

open System.Collections.Generic
open Microsoft.FSharp.Reflection

/// Which look of a character a slot names: one of the visual variants the game has art for. A closed
/// union so a look is a value rather than a string, and shared by every member, because whether a
/// member has a look is a fact about the art rather than about her type.
[<RequireQualifiedAccess>]
type Look =
    | SchoolUniform
    | Casual

/// The renderable name of a look — matched over the cases for the same reason `Roster.characterName`
/// is, rather than formatted, so adding a look is a case here and the compiler reports it.
let lookName (look: Look) : string =
    match look with
    | Look.SchoolUniform -> nameof Look.SchoolUniform
    | Look.Casual -> nameof Look.Casual

/// Every look, enumerated from the type rather than listed again, so a case cannot be left out of the
/// panel. Computed once, as `Roster.characters` is, so drawing the panel reflects nothing per frame.
let allLooks : Look list =
    FSharpType.GetUnionCases typeof<Look>
    |> Array.toList
    |> List.map (fun case -> unbox<Look> (FSharpValue.MakeUnion(case, [||])))

/// The unit an actor asset is assigned to: one member and one look.
[<Struct>]
type Slot =
    { Character : Roster.Character
      Look : Look }

/// Which actor asset plays which slot. Sparse deliberately: an entry says the game has a model for
/// that slot, and an absent one says it does not — which is the whole of the existence check.
type Cast =
    { Bindings : Dictionary<Slot, string> }

/// The binding's question: the asset id for a slot, or nothing when the member has no model for that
/// look. An expected absence, so an `option` rather than a sentinel, and this is what a later consumer
/// — placing a cast member, or the simulation acting on one — calls.
let tryAsset (slot: Slot) (cast: Cast) : string option =
    match cast.Bindings.TryGetValue slot with
    | true, assetId -> Some assetId
    | false, _ -> None
