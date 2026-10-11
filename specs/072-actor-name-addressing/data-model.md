# Data Model: Actors Addressed by Name

## `world/Entity.fs` — the actor loses its role

```fsharp
// before (spec 063)
[<Struct>]
type ActorState =
    { Base : Entity
      AssetId : string
      ShowCollider : bool
      Cast : Roster.Character option }   // ← removed

// after
[<Struct>]
type ActorState =
    { Base : Entity
      AssetId : string
      ShowCollider : bool }
```

The actor’s identity for addressing is `Base.Name`. Nothing else about `Actor` changes.

## `world/Zone.fs` — the spawn signature and the name scan

```fsharp
// spawnActor: the `cast` argument is dropped (no role to record)
let spawnActor (zone: Zone) (world: Physics.World) (assets: Assets.AssetManager)
               (entity: Entity) (assetId: string) : EntityRef

// new, beside tryWaypointByName / tryTriggerByName: the first live actor whose name matches
let tryActorByName (zone: Zone) (name: string) : EntityRef option
```

`tryActorByName` scans `zone.Actors.Slots` in index order (`slot.Alive && slot.Value.State.Base.Name = name`,
the same shape as the two existing lookups) and returns `actorRef i slot.Generation`.

## `state/GameplayState.fs` — the index goes, the resolver arrives

**Naming note:** `SharedState.Cast` is the cast **binding** (`Casting.Cast`) and is **retained**; only
`GameplayState.Cast` — the runtime index — is removed. The two share a field name, so `shared.Cast` (used by
`editor/CastPanel.fs`, `editor/DialoguePanel.fs`, `screen/GameplayScreen.fs` and the tests) must be left alone.

```fsharp
// removed
Cast : Dictionary<Roster.Character, Place * EntityRef>
let tryCastActor (state) (character) : (Place * EntityRef) option
let addCast (state: byref<GameplayState>) (character) (place) (ref)
let removeCast (state: byref<GameplayState>) (character)
let removeCastZone (state: byref<GameplayState>) (place)

// added: resolve a roster character by name over the present places, in Place.all order (D3)
let tryActorByName (state: GameplayState) (character: Roster.Character)
        : (Place * Zone.Zone * EntityRef) option
```

The resolver walks `Place.all`, skips a place not in `state.Zones`, and calls `Zone.tryActorByName` on each
present zone; the first hit wins. It returns the place, the zone and the ref, so a caller does not re-look-up
the zone. `Player` and the physics world are unchanged.

## `world/Cue.fs` — the op is renamed

```fsharp
// before
| SetActor of place: Place * character: Roster.Character * look: Casting.Look * waypoint: string

// after
| AddActor of place: Place * character: Roster.Character * look: Casting.Look * waypoint: string
```

`RemoveActor of character: Roster.Character` and every other actor op are unchanged in shape. No `…State`
case is added: `AddActor` is a no-progress leaf.

## `world/CueEngine.fs` — the effects

Shared resolution:

```text
zone(place)  = state.Zones.TryGetValue place                       // AddActor: absent ⇒ no-op
pose         = Zone.tryWaypointByName zone waypoint                // AddActor: None ⇒ no-op
member       = GameplayState.tryActorByName state character        // (Place * Zone * EntityRef) option
```

### `AddActor (place, character, look, waypoint)` — pure spawn (D4)

| Case | Condition | Effect |
|------|-----------|--------|
| **no-op** | `place` not present, or `waypoint` not resolved in its zone | nothing |
| **no-op** | `Casting.tryAsset { Character = character; Look = look } shared.Cast = None` (no art for the look) | nothing |
| **spawn** | the above resolved | build `Entity.baseEntity (Roster.characterName character) waypointPose`; `Zone.spawnActor zone physics assets entity asset`; detach the body if `place <> state.ActiveZone` (D6) |

There is **no** lookup for an existing actor and **no** index write: a second `AddActor` of the same name
places a second actor (spec FR-008).

### `RemoveActor character` (D5)

| Case | Condition | Effect |
|------|-----------|--------|
| **no-op** | `member = None` | nothing |
| **remove** | `member = Some (place, zone, ref)` | `Zone.removeActor zone physics ref` (destroy body, release renderer) |

### The acting ops (D5)

`tryResolveActor` is re-based on `GameplayState.tryActorByName` and keeps its `(Place * Zone * EntityRef) option`
shape, so `MoveActor`, `RotateActor`, the four blends and `EnablePlayer`/`DisablePlayer` are unchanged apart
from how the actor is found.

## Editor

- `editor/Spawning.fs`: `spawnActor` drops the `cast` argument and the duplicate guard; it is now a plain
  spawn (`Zone.spawnActor` + the reversing command + selection).
- `editor/CastPanel.fs`: the Spawn control passes no cast; the panel (binding authoring) is otherwise unchanged.
- `editor/CueTree.fs`: `"Set actor"` → `"Add actor"` in `kinds`, `kindName`, `label`, `defaultOfKind`; the
  `AddActor` case in `executing` and `children`; `withCharacter`/`withLook` rename their match.
- `editor/CueEditor.fs`: `assetNames` loses its `runtimeCast` parameter and its placed-actor branch (D9).
- `editor/Outliner.fs`, `editor/ScenarioPanel.fs`, `editor/Inspector.fs`: the role/index readouts are deleted
  (D10).

## Validation rules

- **Closed identity preserved**: a cue still names a `Roster.Character`, a `Place` and a `Casting.Look`; no
  free string for *who* or *where* (spec FR-004).
- **Name is the only address**: resolution is a scan of `Base.Name` over the present places, `Place.all`
  order, first match (spec FR-003, FR-008).
- **No uniqueness**: duplicates are permitted and resolve silently to the first match; nothing raises
  (spec FR-008).
- **`AddActor` is not idempotent**: two runs place two actors; moving is remove-then-add (spec FR-006).
- **Art is creation-time**: `AddActor`’s look is read from the binding at spawn; there is no re-skin path
  (spec FR-005).
- **No-op totality**: an absent place, an unbound look or an unresolved waypoint (for `AddActor`), and an
  absent member (for an acting op), perform nothing (D7).
- **Runtime only**: no actor op writes the undo history (`specs/064` FR-007).
- **No index maintenance**: a create, delete, undo, redo, zone delete or zone-delete-undo touches no cast
  bookkeeping (spec FR-009).
