# Data Model: Actor Cue Ops

## `world/Cue.fs` — the two new ops

```fsharp
type Cue =
    | ...
    // Actor lifecycle (spec 064): a cue names a member by closed identity. `SetActor` names a place and places
    // her at a waypoint in it; `RemoveActor` names the character alone, since the cast index knows her place.
    // Both are no-progress leaves, and both are no-ops on state they cannot act on, so a cue is safe to re-run.
    | SetActor of place: Place * character: Roster.Character * look: Casting.Look * waypoint: string
    | RemoveActor of character: Roster.Character
```

- `Place` (`world/Place.fs`) and `Roster.Character` (`cast/Roster.fs`) and `Casting.Look` (`world/Casting.fs`)
  all precede `Cue` in the compile order, so the op may name them.
- No `…State` case: neither op spans time (spec FR-010 defers the timed movement/animation ops).

## `world/CueEngine.fs` — the effects

`advance` gains an arm per op. Shared resolution:

```text
member       = GameplayState.tryCastActor state character   // (Place * EntityRef) option; global, unique
zone(place)  = state.Zones.TryGetValue place                // set only: absent ⇒ the op is a no-op
pose         = Zone.tryWaypointByName zone waypoint          // set only: None ⇒ the op is a no-op
```

### `SetActor (place, character, look, waypoint)`

| Case | Condition | Effect |
|------|-----------|--------|
| **no-op** | `place` not present; or `waypoint` not resolved in its zone | nothing |
| **spawn** | `member = None`, and `Casting.tryAsset { Character; Look = look } shared.Cast = Some asset` | build `Entity.baseEntity (Roster.characterName character) waypointPose`; `Zone.spawnActor zone physics assets entity asset (Some character)`; detach the body if `place <> state.ActiveZone`; `GameplayState.addCast &state character place ref` |
| **no-op** | `member = None`, and `tryAsset = None` (no art for the look) | nothing |
| **move (same place)** | `member = Some (p, ref)` with `p = place` | `Zone.setTransform zone physics ref waypointPose`; detach if `place <> state.ActiveZone`; index unchanged |
| **move (cross-zone)** | `member = Some (p, ref)` with `p <> place` | capture `ActorState` from `p`'s zone; `Zone.removeActor oldZone physics ref`; spawn into `place` with the **captured `AssetId`** and `Cast = Some character`; detach if inactive; `GameplayState.removeCast` then `addCast character place newRef` |

`waypointPose` = the waypoint entity's `Transform.Position` and `Transform.Rotation`, read through
`Zone.tryEntityAt`.

### `RemoveActor character`

| Case | Condition | Effect |
|------|-----------|--------|
| **no-op** | `member = None` | nothing |
| **remove** | `member = Some (place, ref)` | `Zone.removeActor state.Zones.[place] physics ref`; `GameplayState.removeCast &state character` |

### Detaching an inactive place's body (D5)

A body built by `Zone.spawnActor` / `Zone.setTransform` joins the simulation; when the target place is not the
active one it is detached with `Physics.removeBody`, guarded by `Physics.isAdded`. Only that body is touched.

## `world/Entity.fs` — the shared constructor (D6)

```fsharp
/// The base entity every spawned entity starts from: a name, a transform, and the four editor flags.
/// `Spawning.baseEntity` and `Outliner.newEntity` delegate here; `CueEngine` uses it for a cue-spawned actor.
let baseEntity (name: string) (transform: Transform) : Entity =
    { Name = name; Transform = transform; Enabled = true; Visible = true; Selectable = true; Draggable = true }
```

## `editor/CueTree.fs` and `editor/CueEditor.fs` — the authoring surface

- `kinds` gains `"Set actor"` and `"Remove actor"`.
- `kindName`, `label`, `executing` (both are non-executing leaves), `children` (no children) gain the two cases.
- `defaultOfKind`: `"Set actor" -> SetActor(Place.start, List.head Roster.characters, List.head Casting.allLooks, "")`;
  `"Remove actor" -> RemoveActor(Place.start, List.head Roster.characters)`.
- `withPlace` extended to `SetActor` (place is its first field); it raises for `RemoveActor` (which carries no
  place).
- New `withCharacter` (both ops) and `withLook` (`SetActor` only) — the character and look choosers.
- `textOf`/`withText` extended to `SetActor`'s waypoint (the second string-bearing leaf after the camera ops).
- `CueEditor.drawNodeText`: for `SetActor`, offer the waypoint from the **named place's** zone when that place is
  present, else fall back to the typed field. `RemoveActor` carries no waypoint.

## Validation rules

- **Closed identity**: the place, character and look are values from closed unions; only the waypoint is a
  string, and it is a name in the named place's zone.
- **One actor per character** (`specs/063` FR-003): a set never leaves two actors for one character — a move
  reuses the existing actor, and a cross-zone set moves it.
- **Art is creation-time** (spec FR-004): the op's look is consulted only when the member has no actor; an
  existing actor keeps her `AssetId`.
- **Index exact** (spec FR-005, `specs/063` SC-003): a spawn adds one entry, a cross-zone move re-points it, a
  remove drops it; no absent actor keeps one.
- **No-op totality** (spec FR-006): an absent place, an unbound look on a first placement, or an unresolved
  waypoint performs nothing and the cue continues.
- **Runtime only** (spec FR-007): no op writes the undo history.
