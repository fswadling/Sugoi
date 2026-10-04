# Data Model: Actor Motion & Animation Cue Ops

## `world/Cue.fs` — the ops and their in-flight states

```fsharp
    // Actor animation / morph blends (spec 066). One `BlendState` serves all four; `Animate`/`Morph` are the
    // from = to case. A blend updates each tick and ends at its deadline.
    | Animate of character: Roster.Character * clip: string * weight: float32 * duration: float
    | CrossFade of character: Roster.Character * fromClip: string * toClip: string * weight: float32 * duration: float
    | Morph of character: Roster.Character * morph: string * weight: float32 * duration: float
    | CrossMorph of character: Roster.Character * fromMorph: string * toMorph: string * weight: float32 * duration: float
    | BlendState of character: Roster.Character * morph: bool * fromName: string * toName: string
                   * initialFrom: float32 * initialTo: float32 * target: float32 * duration: float * endsAt: float

    // Actor movement (spec 066).
    | MoveActor of character: Roster.Character * path: string * speed: float * movingClip: string * idleClip: string
    | MoveActorState of character: Roster.Character * points: Vector3[] * speed: float * duration: float
                      * endsAt: float * initialRotation: Quaternion * finalRotation: Quaternion
                      * blend: float * movingClip: string * idleClip: string
    | RotateActor of character: Roster.Character * waypoint: string * duration: float
    | RotateActorState of character: Roster.Character * initialRotation: Quaternion * targetRotation: Quaternion * duration: float * endsAt: float

    // The player toggle (spec 066): the runtime counterpart of the editor's player designation.
    | EnablePlayer of character: Roster.Character
    | DisablePlayer of character: Roster.Character
```

`Vector3` / `Quaternion` come from `System.Numerics`, already open in `Cue`.

## `world/CueEngine.fs` — the effects

Shared resolution: `member = GameplayState.tryCastActor state character` → `(Place * EntityRef) option`; the
zone is `state.Zones.[place]`, the actor `&zone.Actors.Get(handleOf ref)`.

### Blends — `Animate` / `CrossFade` / `Morph` / `CrossMorph`

| Case | Condition | Effect |
|------|-----------|--------|
| **no-op** | no actor; or `tryClipWeight`/`tryMorphWeight` says the asset lacks `from`/`to` | nothing |
| **instant** | `duration <= 0` | write the endpoints (from 0, to `target`) and `Fin` |
| **begin** | otherwise | capture `initialFrom`/`initialTo` and hold a `BlendState` |

`BlendState`: `t = (now - (endsAt - duration)) / duration`; the step branches — for `Animate`/`Morph`
(`from = to`) write that one slot `= initial + (target - initial) * t`; otherwise write `from = initialFrom *
(1 - t)` **and** `to = initialTo + (target - initialTo) * t`. At the deadline write the exact endpoints and
`Fin`. If the actor is gone mid-flight, `Fin` (the run stops touching it).

### `MoveActor` / `RotateActor` (active-zone only)

Both first require the actor's zone to be the **active** one (motion is active-zone only); otherwise they are a
no-op.

| Op | Begin | Step |
|----|-------|------|
| `MoveActor` | resolve the actor and the path (`Zone.tryWaypointByName` + `Zone.waypointChain`); a single waypoint is a straight walk from where she stands; positive speed; `points = actorPos :: chainPositions` — unless she already stands at the chain's start, when the chain's own points are used (a duplicated point would bend the spline backwards and stall the walk); `initialRotation = actorRotation` (her own facing), `finalRotation = actorFacing (last waypoint's rotation)` (the path's end facing); `speed` kept for the per-tick velocity; `duration = pathLength points 10 / speed`; `blend = min 0.25 (duration * 0.3)`; write the moving clip to 1 and the idle clip to 0 | target = `evalSpline points t`; set the body's horizontal velocity toward it at `speed` (vertical preserved); facing = `slerp initialRotation finalRotation t`; blend `movingClip → idleClip` over the final `blend` seconds |
| `RotateActor` | resolve the actor and the waypoint; `initialRotation` = the actor's facing, `targetRotation` = the waypoint's arrow as an actor facing | rotation = `slerp initialRotation targetRotation t`; write the body rotation |

An **unobstructed** move arrives at the endpoint at the deadline (`last waypoint` + `finalRotation` + idle at
1.0); an **obstructed** one stops where the collision left it (the op never teleports her through). No actor, an
unresolved waypoint/path, a path with no length, a non-positive speed, or a zone that is not active ⇒ no-op.

Movement drives the body like `Systems.playerInput` — `Physics.setBodyVelocity` (horizontal, vertical preserved)
and `setBodyRotation` — never writing the transform and never `Zone.setTransform` (which rebuilds the body).

### Facing a waypoint

An actor's forward is its local `+Z` (`Systems.forwardOf`), and Akane's model confirms it (her IQM bind pose is
Z-up with toes along `-Y`, which `ActorRender.upAxisFix` maps to `+Z`). A waypoint's arrow is its local `-Z`
(`editor/Viewport.fs`). `CueEngine.actorFacing` turns a waypoint's rotation into the Y-yaw whose `+Z` aligns with
the waypoint's `-Z`, so `SetActor`, `RotateActor` and `MoveActor`'s target all read the waypoint's arrow as an
actor facing. `MoveActor` slerps from her own facing to that target, so a waypoint the author turned to face the
path leaves her facing it.

### `EnablePlayer` / `DisablePlayer`

| Case | Condition | Effect |
|------|-----------|--------|
| **no-op** | no actor; or disable when she is not the player | nothing |
| **enable** | an actor | `zone.Player <- ValueSome (Systems.playerHandleOf ref)` (the camera then follows, `specs/045`) |
| **disable** | the player is this actor | `zone.Player <- ValueNone` |

## `world/ActorRender.fs` — the readers

```fsharp
// A clip/morph's current weight, or None when the asset does not define it (a named slot the actor does not
// hold reads 0). The guard a blend uses to no-op on a name the asset lacks.
val tryClipWeight  : byref<ActorRender> -> name:string -> float32 option
val tryMorphWeight : byref<ActorRender> -> name:string -> float32 option
```

## `editor/CueTree.fs` — the authoring surface

- `kinds`: add the new ops — under **Actors**: `Move actor`, `Rotate actor`, `Enable player`,
  `Disable player`; under a new **Animation** group: `Animate`, `Cross fade`, `Morph`, `Cross morph`.
- `kindName` / `label` / `executing` (the three `…State` cases are executing) / `children` (no children) /
  `defaultOfKind` gain the cases; `withCharacter` covers them all; `withWaypoint` writes the waypoint/path a
  chooser commits; `textOf`/`withText` cover a node's first text field and `textOf2`/`withText2` its second (a
  cross-fade's target clip/morph, a move's idle clip).
- `drawEditor`: a character chooser for every op; `dragNumber` for `weight`/`duration`/`speed`. `CueEditor.drawNodeText`
  offers `MoveActor`'s path and `RotateActor`'s waypoint from the **active zone's** waypoints (`drawWaypointCombo`),
  and a clip/morph name from the character's asset's names (`assetNames`, over `SharedState`'s manager) with a typed
  fallback; the two-name ops draw a second field from a second reused buffer (`EditorState.CueTextBuffer2` /
  `TriggerCueTextBuffer2`) with a distinct ImGui id.

## Validation rules

- **One actor, by character**: every op resolves through the index; a character not in the scene is a no-op.
- **Endpoint exactness**: a blend lands on its target weight; an unobstructed move arrives at the last waypoint
  facing its orientation with the idle clip at 1.0; a blocked move stops where it is; a turn lands on the
  waypoint's yaw.
- **Re-run safe**: no actor, an unresolved path, a name the asset lacks, a redundant player toggle, or (for a
  motion op) an actor whose zone is not the active one performs nothing (FR-005) — and says why, on stderr
  (`CueEngine.noOp`).
- **A blend acts in the actor's own zone, active or not; motion only in the active zone**: the index resolves her
  place; a blend drives her slots anywhere, while a motion op drives the body (which is only simulated while her
  zone is active).
- **Runtime only**: no op writes the undo history (FR-006).
- **No per-tick allocation**: a blend/move updates in place.
