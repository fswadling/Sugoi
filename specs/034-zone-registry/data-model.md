# Data Model: The Zone Registry

One union of authored values, one record, one dictionary. Nothing in the live model changes: an
`Environment`, a `Prop`, a `Zone`, a slot map and every command are as they were, and the feature is a
new pair of types over them.

```mermaid
graph LR
    Z["Active zone's Environment<br/>(Base · AssetId · Orientation · Parts)"] -->|ZoneTemplate.ofEnvironment| T["ZoneTemplate<br/>AssetId · Base · Orientation · Parts"]
    T -->|Templates.[place] &lt;-| R["ZoneRegistry.Registry<br/>Dictionary&lt;Place, ZoneTemplate&gt;"]
    R -->|tryTemplate place| T2["a registered ZoneTemplate"]
    T2 -->|Zone.loadEnvironmentFrom| E["a fresh Environment<br/>loaded and activated"]
    P["ZoneRegistry.byZone<br/>(reflection over Place)"] --> PANEL["the panel's tree"]
    R --> PANEL
```

The vertical direction is the one that matters: the same `ZoneTemplate` goes in and comes back, and
the round trip is what `test/ZoneTests.fs` asserts.

## The vocabulary — `world/ZoneRegistry.fs`

A **place** is a zone together with one of the variants that zone declares. The variants are the
case's payload, so a zone's set is its own type and nothing is shared between zones:

```fsharp
// Authored: the game's places and what each one can look like.
type Period = Morning | Evening

type Place =
    | ArtRoom of Period
    | Island
```

`byZone` derives the panel's tree from that declaration by reflection, once:

| Derived | Rule |
|---|---|
| one `ZoneGroup` per union case | in declaration order, `Name` = the case's name |
| `ZoneGroup.Places` | one `PlaceEntry` per fieldless case, or one per case of the case's variant union |
| `PlaceEntry.Name` | the zone's case name and the variant's (`"ArtRoom Morning"`), or the zone's name alone when the case has no fields (`"Island"`) |
| `PlaceEntry.Place` | the value the registry is keyed by (`Place.ArtRoom Period.Morning`) |

A case that is neither fieldless nor carries a single union type is a vocabulary bug and raises: the
panel has no tree to build for it, and "draw nothing" would hide a mistyped declaration.

## The record — `world/ZoneTemplate.fs`

```fsharp
type ZoneTemplate =
    { AssetId : string          // the environment asset to load
      Base : Entity             // the environment's own settings: its name and placement
      Orientation : Quaternion  //   … and its orientation
      Parts : PropState array } // the parts, as the author left them
```

| Recorded | From | Why it is enough |
|---|---|---|
| the asset | `Environment.AssetId` | it names the geometry the parts are pieces of |
| the settings | `Environment.Base`, `Environment.Orientation` | the environment's own name and transform, not a part's — grouped apart from the parts so that more settings join in one place (`docs/ARCHITECTURE.md`'s `settings`) |
| the parts | each live part's `PropState` | `{ Base; AssetId; SubmeshIndex; SubmeshCount }` — the four fields a part is authored with, and the same value `Zone.capturePart` answers and `PartCreate`/`PartDelete` carry |

`ofEnvironment` is the copy: it walks the live parts slot map and takes each part's authored value.
Because it is a copy and not a link, editing the zone afterwards cannot change a registration
(FR-009), and deleting the zone it came from cannot either (FR-010). Because it is taken from the
*slot map*, a part the author deleted is absent from the array and one she duplicated appears twice —
which is FR-007, recorded rather than implemented.

## The map — `world/ZoneRegistry.fs`

```fsharp
type Registry =
    { Templates : Dictionary<Place, ZoneTemplate> }

val create : unit -> Registry
val tryTemplate : place:Place -> registry:Registry -> ZoneTemplate option
```

Sparse deliberately, exactly as `world/Casting.fs`'s binding is: an entry's presence says a zone is
registered at that place, and an absent entry says nobody has registered one yet — which is the whole
of the existence check and not an error. That is why the panel asks `tryTemplate` rather than testing
`ContainsKey`, and why `Place` is the key rather than any part of the path being parsed.

## Loading a record — `world/Zone.fs`

```
Zone.loadEnvironment      : zone · world · assets · assetId             -> Environment
Zone.loadEnvironmentFrom  : zone · world · assets · template:ZoneTemplate -> Environment
```

Both take a fresh epoch, build the environment and make it the zone's current one, returning the
value so the caller's `SetEnvironment` command can retain it and undo can put the previous one back.
They differ only in where the base, the orientation and the part states come from — the asset, or the
record — and both go through one private builder, so an instantiated zone and a loaded asset cannot
diverge in how their parts are built, activated or stamped.

## Validation rules

| Rule | Requirement |
|---|---|
| Every zone in the union appears once, with one row per place it declares | FR-001, FR-002, SC-001 |
| A place with no entry answers `None`; registering it then answers it; registering again replaces it | FR-003, FR-004, FR-008 |
| A registration is a copy: the live zone's later edits do not reach it, and it does not reach them | FR-009, SC-006 |
| A record loaded back reproduces its asset, settings and parts, parts the author deleted included | FR-012, SC-002, SC-003 |
| Loading a record is one step that restores the zone's previous environment | FR-013, SC-004 |
| Registering writes no undo step and touches no other place | FR-009, FR-008, SC-005, SC-007 |
| The environment asset is not loaded, or a record's part range has run past its asset's submeshes | Fails loudly: the first raises on lookup, the second through `EnvironmentBatch.gather`'s existing range check |

## What is deliberately unchanged

| | Why |
|---|---|
| `Environment`, `Prop`, `PropState`, `Zone`'s entity collections and slot maps | the feature records an environment and rebuilds one; it adds no field to either and no operation to the live model |
| `Command`, and `SetEnvironment` in particular | instantiating reuses the command that already carries a whole environment and already reverses it |
| `Zone.setEnvironment`, `deactivateEnvironment`, `activateEnvironment` | the same activation path takes a record-built environment as an asset-built one |
| `world/Casting.fs`, `cast/Roster.fs` | the cast is a different vocabulary with its own registry-shaped map; nothing is shared |
| `editor/AssetBrowser.fs` behaviour | its environment load is unchanged — the new panel repeats its five-line swap rather than replacing it (`research.md` Decision 7) |
| Props, waypoints, triggers, splat maps | out of scope (FR-015), joining as siblings of the parts when they land |
