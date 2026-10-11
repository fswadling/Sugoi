# Contract: Scenario capture (trimmed)

**Feature**: `specs/076-scenario-narrative-only`

This is an internal engine surface, so the "contract" is the module API the rest of the code depends on. Only the
parts this feature changes are contracted; everything not listed is unchanged.

## `world/ScenarioZone.fs` — CHANGED

```fsharp
// The ref-free authored record of one zone as a scenario holds it: the narrative entities only.
type ScenarioZone =
    { Actors : ActorState array
      Waypoints : ScenarioWaypoint array        // { Base : Entity; Next : int option }
      Triggers : TriggerState array }

// Reads the live zone and nothing else; device-free. Records actors, waypoints (chains as indices) and triggers.
let capture (zone: Zone) : ScenarioZone
```

**Removed**: the `ScenarioEnvironment` type, the `Environment`, `Light` and `Player` fields of `ScenarioZone`, the
`captureEnvironmentParts` helper, and the player-index computation in the actor capture.

**Invariants**

- No `EntityRef` appears anywhere in a `ScenarioZone`; a `ScenarioWaypoint.Next` is an index into the same record's
  `Waypoints` array.
- `capture` raises if a waypoint's `Next` names a waypoint not live in the zone (unchanged behaviour).
- `capture` reads only the zone it is handed: no environment, no light, no player, no device.

## `world/ScenarioRegistry.fs` — CHANGED

```fsharp
type ScenarioContents =
    { Zones : Map<Place, ScenarioZone>
      Cue : Cue.Cue }

// Every present zone (keyed by its place, empty ones included), and the cue. No active place.
let capture (zones: Dictionary<Place, Zone>) (cue: Cue.Cue) : ScenarioContents
```

**Removed**: the `Active : Place` field of `ScenarioContents`, and the `active` parameter of `capture`.

**Invariants**

- `Zones` maps **every** present zone, including a place with no narrative entities, to its `ScenarioZone`.
- `Zones` and `Cue` are immutable, so a `ScenarioContents` remains safe to hold as an undo snapshot.

**Unchanged**: `Registry`, `create`, `tryContents`, `setContents`.

## `state/Command.fs` — UNCHANGED

```fsharp
// The type it names shrinks; the case does not change.
| SetScenario of Name : Scenario * Previous : ScenarioRegistry.ScenarioContents option * Next : ScenarioRegistry.ScenarioContents option
```

## `editor/ScenarioPanel.fs` — CHANGED (one call)

```fsharp
// Register: capture the scene's narrative entities and the cue.
do commit shared &state scenario (Some (capture state.Zones state.Cue))
```

**Unchanged**: `commit` (reads the entry's `before` and writes one `SetScenario` step), the per-scenario row, the
Register and Clear controls, and the preview (`"%d zone(s)"` from `contents.Zones.Count`).

## Acceptance

1. `capture zones cue` returns contents whose every zone holds only actors, waypoints and triggers — with no
   environment, light or player — and whose contents hold no active place.
2. A scene differing only in zone environment/light/player yields equal contents.
3. Register, Clear, undo and redo round-trip exactly as before, one step on the one history.
4. `dotnet build` is warning-free and `dotnet test` is green with no device opened.
