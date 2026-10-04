module ZoneRegistryTests

// The registry (spec 034): the game's places as the union declares them, and the sparse map from a
// place to the zone registered there. Nothing here needs a GPU device: the vocabulary is reflection
// over a union, and the map holds authored records.

open System.Collections.Generic
open System.Numerics
open Xunit
open Microsoft.FSharp.Reflection
open Entity
open SlotMap
open ZoneTemplate
open ZoneRegistry
open Place

// ── The vocabulary (US1) ──

[<Fact>]
let ``every zone is a node, in declaration order`` () =
    // The check that fails if the reflection walk and the union stop agreeing — a case skipped,
    // renamed or reordered. Deriving the tree from the declaration buys that no one has to remember
    // to add a node, and this is the price: nothing else reports a case the walk cannot read.
    let cases = FSharpType.GetUnionCases typeof<Place> |> Array.map (fun case -> case.Name) |> Array.toList
    let nodes = byZone |> List.map (fun zone -> zone.Name)
    Assert.Equal<string list>(cases, nodes)

[<Fact>]
let ``every zone has at least one place, and every place is named`` () =
    // A zone with no rows would be a node the author cannot register anything under, so an empty
    // group is a walk that failed rather than a state to draw.
    for zone in byZone do
        Assert.NotEmpty zone.Places
        for entry in zone.Places do
            Assert.False(System.String.IsNullOrWhiteSpace entry.Name, sprintf "zone '%s' has an unnamed place" zone.Name)

[<Fact>]
let ``a fieldless zone is one place named for the zone itself`` () =
    // The island declares no variants, so it contributes one row rather than none, and its label comes
    // from the zone's own case name because there is no variant name to join to it.
    let island = byZone |> List.find (fun zone -> zone.Name = "Island")
    let only = Assert.Single island.Places
    Assert.Equal("Island", only.Name)

[<Fact>]
let ``a zone's variants are its own, and no place appears twice`` () =
    // Two zones declare unrelated sets — the art room two times of day, the island nothing — and no
    // place value is reachable from two zones, which is what "each environment can have a different
    // set" means once the sets are values rather than names.
    let places = byZone |> List.collect (fun zone -> zone.Places) |> List.map (fun entry -> entry.Place)
    Assert.Equal(places.Length, places |> List.distinct |> List.length)

    let names = byZone |> List.collect (fun zone -> zone.Places) |> List.map (fun entry -> entry.Name)
    Assert.Equal(names.Length, names |> List.distinct |> List.length)

    let byZoneOf (place: Place) =
        byZone |> List.filter (fun zone -> zone.Places |> List.exists (fun entry -> entry.Place = place)) |> List.length
    for place in places do
        Assert.Equal(1, byZoneOf place)

[<Fact>]
let ``a place is usable as a key, which is what the registry is built on`` () =
    // The map is `Dictionary<Place, ZoneTemplate>`, so a place has to hash and compare by value rather
    // than by identity. Every place in the vocabulary round-trips as a key.
    let keys = Dictionary<Place, string>()
    let places = byZone |> List.collect (fun zone -> zone.Places) |> List.map (fun entry -> entry.Place)
    for place in places do
        keys.[place] <- sprintf "%A" place
    for place in places do
        Assert.True(keys.ContainsKey place)
    Assert.Equal(places.Length, keys.Count)

// ── The map (US2) ──
//
// The environment below is built by hand: a record holds the authored values of an environment, so
// nothing on these paths needs a GPU device, a physics world, a zone or a loaded asset.

let private entity (name: string) (x: float32) : Entity =
    { Name = name
      Transform = { transformIdentity with Position = Vector3(x, 0.0f, 0.0f) }
      Enabled = true
      Visible = true
      Selectable = true
      Draggable = true }

// A part as an environment holds one while it is being assembled: an authored value, with no body and
// no renderer yet — activation attaches those.
let private prop (name: string) (submeshIndex: int) : Prop =
    { State = { Base = entity name (float32 submeshIndex); AssetId = None; SubmeshIndex = submeshIndex; SubmeshCount = 1 }
      Physics = None
      Renderer = Unchecked.defaultof<EnvironmentRender.EnvironmentRender> }

let private environment (assetId: string) (names: string array) : Environment =
    let parts = SlotMap<Prop>(uint32 names.Length)
    for i in 0 .. names.Length - 1 do
        parts.Add(prop names.[i] i) |> ignore
    { Base = entity assetId 0.0f
      AssetId = assetId
      Orientation = Quaternion.Identity
      Epoch = 1u
      Parts = parts
      PartsSource = Authored }

let private places () =
    byZone |> List.collect (fun zone -> zone.Places) |> List.map (fun entry -> entry.Place)

[<Fact>]
let ``an unregistered place answers nothing, and registering one answers it`` () =
    // The whole of the existence check: an absent entry says nobody has registered a zone there,
    // which is a state rather than an error.
    let registry = create ()
    let place = List.head (places ())
    Assert.Equal<ZoneTemplate option>(None, tryTemplate place registry)

    let template = ZoneTemplate.ofEnvironment (environment "artroom" [| "floor"; "wall" |]) [||] Texture.defaultLight
    registry.Templates.[place] <- template
    Assert.Equal(Some template, tryTemplate place registry)

[<Fact>]
let ``registering again replaces what was there, and disturbs no other place`` () =
    let registry = create ()
    let all = places ()
    let first = all.[0]
    let second = all.[1]

    registry.Templates.[first] <- ZoneTemplate.ofEnvironment (environment "artroom" [| "floor" |]) [||] Texture.defaultLight
    registry.Templates.[second] <- ZoneTemplate.ofEnvironment (environment "island" [| "ground" |]) [||] Texture.defaultLight
    Assert.Equal(2, registry.Templates.Count)

    // One entry per place: re-registering replaces rather than accumulating, and the other place is
    // untouched.
    registry.Templates.[first] <- ZoneTemplate.ofEnvironment (environment "artroom" [| "floor"; "wall" |]) [||] Texture.defaultLight
    Assert.Equal(2, registry.Templates.Count)
    Assert.Equal(2, (tryTemplate first registry).Value.Parts.Length)
    Assert.Equal(1, (tryTemplate second registry).Value.Parts.Length)
    Assert.Equal("island", (tryTemplate second registry).Value.AssetId)

[<Fact>]
let ``a record is a copy taken at a moment, not a view of the live parts`` () =
    // A registration has to survive the zone it was taken from, which holds only if taking it copies
    // the parts, and if taking it leaves the environment alone.
    let loaded = environment "artroom" [| "floor"; "wall" |]
    let template = ZoneTemplate.ofEnvironment loaded [||] Texture.defaultLight
    Assert.Equal(template, ZoneTemplate.ofEnvironment loaded [||] Texture.defaultLight)
    Assert.Equal(2, template.Parts.Length)

    let slots = loaded.Parts.Slots
    slots.[0].Value <- prop "moved" 7

    Assert.Equal("floor", template.Parts.[0].Base.Name)
    Assert.Equal(0, template.Parts.[0].SubmeshIndex)
    Assert.Equal(Vector3.Zero, template.Parts.[0].Base.Transform.Position)
    Assert.Equal(Vector3(1.0f, 0.0f, 0.0f), template.Parts.[1].Base.Transform.Position)
