# Data Model: Zones as Places

## `world/Place.fs` — NEW (the vocabulary)

```fsharp
/// Which sitting of the art room (moved here from `ZoneRegistry`).
type Period =
    | Morning
    | Evening

/// The game's zones and their variants — the authored identity of a runtime zone. A closed union, so a
/// zone that is not declared here cannot exist.
type Place =
    | ArtRoom of Period
    | Island

val placeName : Place -> string     // "ArtRoom Morning" / "Island", matched over the cases (as Advent.name)
val all : Place list                // every place, in declaration order
val start : Place                   // the designated starting place (GameplayState.create uses it)
```

## `state/GameplayState.fs`

```fsharp
type GameplayState =
    { ...
      Zones : Dictionary<Place, Zone>
      mutable ActiveZone : Place
      ... }

val create : ... -> GameplayState   // makes Place.start present and active
val activeZone : GameplayState -> Zone
val switchZone / selectZone / addZone / deleteZone : ... -> Place -> unit
```

- `deleteZone` refuses when `place = ActiveZone` (the active place cannot be made absent), and on an allowed
  removal releases the zone and unloads its environment (FR-010).
- A present place with no registered template is valid.

## `state/Command.fs`

```fsharp
| ZoneMove   of Previous : Place * Next : Place
| ZoneAdd    of Previous : Place * Name : Place
| ZoneDelete of Previous : Place * Name : Place * Contents : ZoneContents
```

`History.apply/undo` carry them out by place; `ZoneDelete`'s undo re-adds an **inactive** place (the active
place is unchanged by the delete), still activating it temporarily to rebuild bodies, then switching back.

## `state/EditorState.fs`

- Remove `NewZoneName : byte[]` and `zoneNameBufferSize` (the free-text name field is gone).

## `world/Zone.fs`

- Remove `Name : string` (a zone's identity is its place).

## `world/Cue.fs`

```fsharp
| SetZone of place: Place
```

## Editor surfaces

- **Outliner** — the Zones section lists present places (declaration order) with the active marker and the
  environment-residency markers; **add** is a chooser over absent places; **remove** is disabled for the
  active place.
- **CueTree** — `SetZone` draws a place chooser and labels with `placeName`.
- **ZoneRegistryPanel** — Register captures the active place (offered for the active place); Instantiate
  makes the named place present and active and loads its template as one step.

## Validation rules

- At most one zone per place; adding a present place is refused (FR-003).
- A place the game does not declare cannot be added or addressed (FR-001).
- The active place cannot be made absent; a lone present place is therefore unremovable (FR-011).
- Removing a zone releases it and unloads its environment when nothing else needs it (FR-010).
- A cue's `SetZone` naming a not-present place keeps today's behaviour (raise); the no-op rule is the next
  feature.
