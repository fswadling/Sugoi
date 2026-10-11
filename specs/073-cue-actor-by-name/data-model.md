# Data Model: Cue Actors Addressed by Name

## `world/Cue.fs` — actor ops carry a string; `AddActor` carries an asset

```fsharp
// before (spec 072)
| AddActor of place: Place * character: Roster.Character * look: Casting.Look * waypoint: string
| RemoveActor of character: Roster.Character
// ... MoveActor/RotateActor/Animate/CrossFade/Morph/CrossMorph/EnablePlayer/DisablePlayer by Roster.Character
// ... BlendState/MoveActorState/RotateActorState carry Roster.Character

// after
| AddActor of place: Place * actor: string * asset: string * waypoint: string
| RemoveActor of actor: string
// ... and every other actor op (and its …State case) names `actor: string`
```

`world/Cue.fs` names no `Roster` or `Casting` value. The op set is otherwise unchanged; no new case.

## `world/CueEngine.fs` — effects

Shared resolution:

```text
zone(place)  = state.Zones.TryGetValue place                       // AddActor: absent ⇒ no-op
pose         = Zone.tryWaypointByName zone waypoint                // AddActor: None ⇒ no-op
actor        = GameplayState.tryActorByName state name             // (Place * Zone * EntityRef) option
```

### `AddActor (place, actor, asset, waypoint)` (research D2, D8)

| Case | Condition | Effect |
|------|-----------|--------|
| **no-op** | `place` not present, or `waypoint` not resolved in its zone | nothing |
| **no-op** | `asset` names no discovered asset (`not (shared.Assets.Actors.ContainsKey asset)`) | nothing |
| **spawn** | the above resolved | build `Entity.baseEntity actor` at the waypoint's pose; `Zone.spawnActor zone physics assets entity asset`; detach the body if `place <> state.ActiveZone` |

No member lookup, no index, no binding: a second `AddActor` of the same name places a second actor.

### `RemoveActor actor` and the acting ops

Unchanged from `specs/072` except that `actor` is a string: the first live actor whose `Name` matches (over the
present places) is removed / acted on; a miss performs nothing. `noOp` takes the string directly.

## `state/GameplayState.fs`

```fsharp
// before (spec 072)
let tryActorByName (state) (character: Roster.Character) : (Place * Zone.Zone * EntityRef) option
// after
let tryActorByName (state) (name: string) : (Place * Zone.Zone * EntityRef) option
```

The scan is identical (`Place.all` order); only the argument type and the dropped `Roster.characterName`
conversion change.

## Editor — where each actor control lives

| Op | Controls | Drawn by |
|----|----------|----------|
| existing-actor ops (`RemoveActor`, `MoveActor`, `RotateActor`, blends, player) | **actor combo** = the viewed place's live actors | `CueEditor.drawNodeText` (new `drawActorCombo`) |
| `AddActor` | **place combo** (closed set) | `CueTree.drawEditor` (unchanged) |
| `AddActor` | **asset combo** (discovered actor assets) | `CueEditor` (new `drawAssetCombo`) |
| `AddActor` | **actor name** (typed; defaults to the asset's) | `CueEditor` (`textOf`/`withText`) |
| `AddActor`, `MoveActor`, `RotateActor`, camera ops | **waypoint combo** (the named/active place's zone) | `CueEditor` (`drawWaypointCombo`, unchanged) |
| blends, `MoveActor` | **clip/morph name** (from the actor's asset) | `CueEditor` (`drawName` with `assetNamesOf`) |

`CueTree` write helpers: `withActor` (string; replaces `withCharacter`), `withAsset` (new); `withLook` deleted;
`withPlace`/`withWaypoint`/`textOf`/`withText` updated. The `AddActor` label reads
`Add actor: <name>/<asset> in <place> @ <waypoint>`.

## Validation rules

- **Closed only where it is a value**: the place is still a `Place`; the actor, the asset and the waypoint are
  strings (the actor and waypoint resolved by a zone scan; the asset by the asset manager).
- **The chooser is the scene**: an existing-actor chooser offers only the viewed place's live actors; nothing
  from the roster (spec FR-005, SC-002).
- **Art and names from the placed actor**: the clip/morph choosers read the actor of the named name in the
  viewed zone's asset (spec FR-006, SC-003).
- **`AddActor` is not idempotent**: two runs place two actors; a name is a label (spec `specs/072`).
- **No-op totality**: absent place, unresolved waypoint, undiscovered asset, or absent actor performs nothing.
- **Runtime only**: no actor op writes the undo history (`specs/064` FR-007).
- **Portraits untouched**: the binding and `Dialogue.Kind` are not referenced by any cue module (spec FR-007).
