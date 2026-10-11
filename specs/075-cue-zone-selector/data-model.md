# Data Model: An Explicit Zone on Every Entity Cue Op

## `world/Cue.fs` — nine ops gain a `place`; the rest are unchanged

```fsharp
// before
| RemoveActor of actor: string
| Animate of actor: string * clip: string * weight: float32 * duration: float
| CrossFade of actor: string * fromClip: string * toClip: string * weight: float32 * duration: float
| Morph of actor: string * morph: string * weight: float32 * duration: float
| CrossMorph of actor: string * fromMorph: string * toMorph: string * weight: float32 * duration: float
| EnablePlayer of actor: string
| DisablePlayer of actor: string
| SetCamera of waypoint: string
| FlyCamera of path: string * speed: float

// after — the place is the first argument, as `AddActor` and the trigger toggles already have it
| RemoveActor of place: Place * actor: string
| Animate of place: Place * actor: string * clip: string * weight: float32 * duration: float
| CrossFade of place: Place * actor: string * fromClip: string * toClip: string * weight: float32 * duration: float
| Morph of place: Place * actor: string * morph: string * weight: float32 * duration: float
| CrossMorph of place: Place * actor: string * fromMorph: string * toMorph: string * weight: float32 * duration: float
| EnablePlayer of place: Place * actor: string
| DisablePlayer of place: Place * actor: string
| SetCamera of place: Place * waypoint: string
| FlyCamera of place: Place * path: string * speed: float
```

Unchanged: `SetZone`, `AddZone`, `RemoveZone`, `PreloadZone`, `AddActor`, `EnableTrigger`,
`DisableTrigger`, `MoveActor`, `RotateActor`, every `…State` case (`BlendState`/`MoveActorState`/
`RotateActorState` already carry `place`; `FlyCameraState` captures its points), and the non-entity ops
(`Wait`/`Fade`/`Exposit`/`Print`/`Advent`/`If`).

## `world/CueEngine.fs` — resolution

Shared actor resolution becomes a per-place lookup:

```text
tryActorIn place actor
  = state.Zones.TryGetValue place        absent ⇒ None (no-op)
  | Some zone -> Zone.tryActorByName zone actor   miss ⇒ None (no-op)
```

| Op | Place used | Miss behaviour |
|----|-----------|----------------|
| `RemoveActor (place, actor)` | `place` | no-op when the place is absent or holds no such actor |
| `Animate`/`CrossFade`/`Morph`/`CrossMorph (place, actor, …)` | `place` | `Fin` (no-op), as today |
| `EnablePlayer`/`DisablePlayer (place, actor)` | `place` | no-op |
| `MoveActor (actor, …)` / `RotateActor (actor, …)` | the **active zone** | no-op |
| `SetCamera (place, waypoint)` | `place` | **raises** (`namedWaypoint`) |
| `FlyCamera (place, path, speed)` | `place` | **raises** (`resolvedPath`) |

`tryResolveActor` becomes `tryActorIn place actor`; `beginMove`/`beginRotate` call it with
`state.ActiveZone` and drop their `place <> state.ActiveZone` guard. `setCamera`/`FlyCamera` read
`state.Zones.[place]` instead of `GameplayState.activeZone`. The `noOp` reason strings gain the place
(`"no actor named 'X' in 'Island'"`), so a no-op names the zone it looked in.

## `state/GameplayState.fs` — the global resolver goes

```fsharp
// before (spec 072/073) — the first live match over the present places, in Place.all order
let tryActorByName (state: GameplayState) (name: string) : (Place * Zone.Zone * EntityRef) option
// after — removed; each op resolves in its own place (research D2)
```

`Zone.tryActorByName` (the per-zone scan) is unchanged and is what the engine and the editor call.

## Editor — where each control lives

| Op(s) | Zone selector | Entity chooser |
|-------|---------------|----------------|
| the nine new ops | **place combo** (closed `Place.all`) in `CueTree.drawEditor` | read the **op's place** in `CueEditor.drawNodeText` |
| `MoveActor`/`RotateActor` | none (active zone) | read the **active zone** |
| `AddActor` | place combo (existing) | asset combo + typed name + the place's waypoint (existing) |
| zone ops | place combo (existing) | — |
| `EnableTrigger`/`DisableTrigger` | place combo (existing) | the place's trigger chooser (existing) |

`CueTree` write helpers: `withPlace` gains the nine ops; `withActor`, `withText`, `withText2` and
`withWaypoint` preserve the new `place`. `defaultOfKind (place: Place) (kind: string)`; `drawEditor` gains
that `defaultPlace` (the active zone). Labels show the place (research D10).

## Validation rules

- **The zone is a value**: an op's place is a `Place` from the closed union; a name is a string resolved in
  that place's zone. No op names an absent entity kind.
- **One address, one answer**: an actor op acts on the **first live actor of that name in its own place's
  zone**; two same-named actors in two zones are told apart by the op's place (spec SC-001).
- **No-op totality (actor ops)**: an absent place, or a name with no live entity in it, performs nothing and
  the run continues (spec FR-003).
- **Camera ops raise**: an unresolved name, or an absent place, raises (`specs/054` FR-009, research D4).
- **Motion keeps the active zone**: `MoveActor`/`RotateActor` name no zone and act on the active zone
  (spec FR-009).
- **In-flight ops keep their place**: a begun blend/motion captures its place and is not re-targeted by a
  later zone switch (spec FR-008).
- **Runtime only**: no cue op writes the undo history; only an editor-side zone change to a cue op is a
  `SetCue`/`TriggerSet` step (`specs/049`, `specs/064` FR-007).
- **Default is the current zone**: a newly-created op's place is the active zone (spec FR-007).
