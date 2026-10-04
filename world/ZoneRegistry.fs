module ZoneRegistry

// The zone registered at each of the game's places: the config half of the world, and the editor's Zone
// Registry panel reads all of it (spec 034). The **vocabulary** — the `Place`/`Period` union — is
// `world/Place.fs` (spec 061), kept separate so the runtime may key its zones by a place without depending
// on this store, as `Roster` sits apart from `Casting`.
//
// A *place* is one of the game's zones together with one of the variants that zone declares —
// `Place.ArtRoom Period.Morning`. Each zone's variants are its own union, unlike the cast's single
// shared `Look`, so two zones declare unrelated sets and there is no shared vocabulary to keep in
// step. Because a place is a value from a closed union, the registry can be keyed by it: the map is
// sparse, an entry's presence says a zone is registered there, and an absent place is one nobody has
// registered yet rather than an error.
//
// Listed after `world/Zone.fs` in the compile list, and it needs neither that module nor any editor
// one: it records environments, not zones.

open System.Collections.Generic
open Microsoft.FSharp.Reflection
open Place
open ZoneTemplate

// ── The tree the panel draws ──

/// One place as the panel shows it: the row's label, and the value the registry is keyed by.
type PlaceEntry =
    { Name : string
      Place : Place }

/// One zone's node in the panel: its name, and one entry per place it declares.
type ZoneGroup =
    { Name : string
      Places : PlaceEntry list }

// The variants a case declares: the cases of the single union it carries, or none for a fieldless
// case, which is a zone with exactly one place. Any other shape has no tree to draw, so it raises
// rather than being skipped — a mistyped declaration is reported, not hidden.
let private variantCases (case: UnionCaseInfo) : UnionCaseInfo array =
    let fields = case.GetFields()
    if fields.Length = 0 then [||] else

    if fields.Length <> 1 || not (FSharpType.IsUnion fields.[0].PropertyType) then
        failwithf "ZoneRegistry: %s.%s must be fieldless or carry a single union of its variants" typeof<Place>.Name case.Name

    let variantType = fields.[0].PropertyType
    let variants = FSharpType.GetUnionCases variantType
    for variant in variants do
        if variant.GetFields().Length <> 0 then
            failwithf "ZoneRegistry: %s.%s must be fieldless" variantType.Name variant.Name

    variants

/// Every zone with one entry per place it declares, in declaration order — the panel's tree, and the
/// labels on it. Derived from the union rather than maintained beside it (FR-002), so adding a zone or
/// a variant needs no panel change; computed once, as `Roster.characters` is, so drawing reflects
/// nothing per frame.
///
/// The names come from the same walk as the tree, deliberately, where `Casting.lookName` matches its
/// cases by hand. A look's arms number one small union, so a hand-written match earns its keep there;
/// a place's arms are the product of the zones and their variants, and a second enumeration is the
/// list beside the vocabulary this function exists to avoid.
let byZone : ZoneGroup list =
    let placeOf (case: UnionCaseInfo) (variant: UnionCaseInfo) : Place =
        unbox<Place> (FSharpValue.MakeUnion(case, [| FSharpValue.MakeUnion(variant, [||]) |]))

    FSharpType.GetUnionCases typeof<Place>
    |> Array.toList
    |> List.map (fun case ->
        let variants = variantCases case
        let places =
            if variants.Length = 0 then
                // A fieldless zone declares no variants, so it is one place named for the zone.
                [ { Name = case.Name
                    Place = unbox<Place> (FSharpValue.MakeUnion(case, [||])) } ]
            else
                variants
                |> Array.toList
                |> List.map (fun variant ->
                    { Name = sprintf "%s %s" case.Name variant.Name
                      Place = placeOf case variant })

        { Name = case.Name
          Places = places })

// ── The registry ──

/// The zone registered at each place. Sparse deliberately: an entry says a zone is registered there,
/// and an absent one says nobody has registered one yet — which is the whole of the existence check.
type Registry =
    { Templates : Dictionary<Place, ZoneTemplate> }

let create () : Registry =
    { Templates = Dictionary() }

/// The registry's question: the zone registered at this place, or nothing when nobody has registered
/// one. An expected absence, so an `option` rather than a sentinel, and this is what the panel asks.
let tryTemplate (place: Place) (registry: Registry) : ZoneTemplate option =
    match registry.Templates.TryGetValue place with
    | true, template -> Some template
    | false, _ -> None
