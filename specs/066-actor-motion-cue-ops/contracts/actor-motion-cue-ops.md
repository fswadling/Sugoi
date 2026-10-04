# Contract: Actor Motion & Animation Cue Ops

What a cue can make a cast member do — blend her animation, walk her, turn her, drive her — each addressed by
`Roster.Character`, resolved through the cast index (`specs/063`).

## The ops — `world/Cue.fs`

```fsharp
| Animate of character: Roster.Character * clip: string * weight: float32 * duration: float
| CrossFade of character: Roster.Character * fromClip: string * toClip: string * weight: float32 * duration: float
| Morph of character: Roster.Character * morph: string * weight: float32 * duration: float
| CrossMorph of character: Roster.Character * fromMorph: string * toMorph: string * weight: float32 * duration: float
| MoveActor of character: Roster.Character * path: string * speed: float * movingClip: string * idleClip: string
| RotateActor of character: Roster.Character * waypoint: string * duration: float
| EnablePlayer of character: Roster.Character
| DisablePlayer of character: Roster.Character
```

Plus the in-flight `BlendState`, `MoveActorState` and `RotateActorState` (data-model.md).

## Resolution

- **The actor** is a `Roster.Character`; `GameplayState.tryCastActor` answers her `(place, ref)`. Not in the
  scene ⇒ the op performs nothing.
- **The path / waypoint** is a name in *her place's* zone (`Zone.tryWaypointByName` / `waypointChain`). Not
  resolved ⇒ the op performs nothing; a single waypoint is a straight walk to it from where she stands, and only
  a path with no length (she already stands on its only waypoint) performs nothing.
- **The clip / morph** is the actor's asset's own name (`Actor.tryFindAnimIndex`, the morph names). The asset
  lacks it ⇒ the op performs nothing (never the setter's raise).
- **The active zone** (motion only): a motion op is physics-driven, so it requires the actor's zone to be the
  **active** one. Her zone is not active ⇒ the op performs nothing. (A blend has no such requirement.)

## Behaviour

| Op | Does |
|----|------|
| `Animate (c, clip, w, d)` | move `clip`'s weight from its current value to `w` over `d` |
| `CrossFade (c, from, to, w, d)` | `from` fades to 0 while `to` fades to `w`, each from its current value, over `d` |
| `Morph` / `CrossMorph` | the same over morph slots |
| `MoveActor (c, path, speed, moving, idle)` | drive her body from where she stands, through the chain to its end, over `length/speed`, slerping her facing from her own to the last waypoint's arrow, blending `moving` → `idle` on arrival — **physics-driven**, so geometry can block her; active-zone only |
| `RotateActor (c, waypoint, d)` | turn in place from her current yaw to the waypoint's over `d` |
| `EnablePlayer c` | make her the zone's player (the editor's "Set as Player"; the camera follows) |
| `DisablePlayer c` | clear her as the player, when she is |

Blends and movements are **leaves with a `…State`**: each tick updates the actor in place and reports nothing
to change (`ValueNone`), ending at the deadline (`ValueSome` — `Fin`). A zero-or-less duration is over before it
begins. A blend lands exactly on its target; an unobstructed move arrives at its endpoint, while a blocked one
stops where the collision leaves it.

## Postconditions

- **Endpoints**: a blend reaches its target weight; an unobstructed move arrives at the last waypoint, facing its
  orientation, with the idle clip at 1.0 (a blocked move stops where it is); a turn lands on the waypoint's yaw.
- **No per-tick allocation**: weights and the body's velocity/facing are written in place.
- **Re-run safe**: an op on a missing actor, an unresolved path, a name the asset lacks, or (for motion) an actor
  in a zone that is not the active one performs nothing (spec FR-005).
- **Runtime only**: no op is a step on the undo history (spec FR-006).

## Explicitly out of scope

- **The non-actor toggles** — `Enable/DisableCameraFollow`, `Enable/DisableInteractions`,
  `Enable/DisableTrigger` — and `Print` (spec FR-008).
- **Changing camera-follow separately**: enabling the player follows her because the player follows; turning
  the camera off is a separate op.
- **The undo history, the narrative engine, and new zone/actor vocabulary.**
