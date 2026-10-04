# Research: Actor Motion & Animation Cue Ops

## D1 — The ops name a character; the index resolves her place

**Decision**: every op is addressed by a `Roster.Character`, with no place argument — `MoveActor` and
`RotateActor` resolve the actor's zone through the cast index (`GameplayState.tryCastActor`) and then resolve
their waypoint/path in that zone, exactly as `RemoveActor` does (`specs/064`).

**Rationale**: a character is unique across all zones and the actor must already exist for these ops, so the
index already knows her place. A place argument would be redundant (and could disagree with the index).
Recorded in `specs/064`'s Assumptions: placement names a place; everything else names the character.

## D2 — One `BlendState` for the four animation/morph ops

**Decision**: `Animate`, `CrossFade`, `Morph` and `CrossMorph` share one in-flight state and one step routine:

```fsharp
| BlendState of character: Roster.Character * morph: bool
               * fromName: string * toName: string
               * initialFrom: float32 * initialTo: float32
               * target: float32 * duration: float * endsAt: float
```

`Animate`/`Morph` are the `from = to` case; `CrossFade`/`CrossMorph` move `from` toward 0 while `to` moves
`toward `target`. The step branches on that, rather than writing both blindly (which would only work by write
order):

    if from = to then                       // Animate / Morph: one slot, toward the target
        write from = initialFrom + (target - initialFrom) * t
    else                                    // CrossFade / CrossMorph: one out, one in
        write from = initialFrom * (1 - t)
        write to   = initialTo + (target - initialTo) * t

Both start from the weights the actor had when the op was reached. Each tick writes the weights with
`ActorRender.setClipWeight` / `setMorphWeight`; at the deadline it writes the exact endpoints (from 0, to
`target`) and is done. This mirrors odin's single `Cue_BlendState`.

**Rationale**: the four ops differ only in their endpoints, exactly as `FadeIn`/`FadeOut` differ only in theirs
(`specs/060`), so one state is the whole of the machinery. Capturing the initial weights where the walk reaches
the op makes a blend from part-way correct.

**Alternatives considered**: a separate state per op (rejected — four identical shapes); driving weights with
an `…State` per slot (no — one state, reused).

## D3 — Movement: a physics-driven walk and a facing turn, active-zone only

**Decision**: `MoveActor` resolves the actor and the named waypoint chain in **her** zone (through the index),
and requires that zone to be the **active** one (motion is active-zone only). It builds a spline from the actor's
current position through the chain to its end (`Zone.waypointChain`, `Maths.evalSpline`), computes
`duration = pathLength points 10 / speed`, and holds a `MoveActorState`. Each tick it **physics-drives** her: it
computes the spline point for the current time, sets the body's **horizontal velocity** toward it (magnitude
`speed`, preserving the vertical component so gravity still acts) and **slerps her facing from her own to the last
waypoint's arrow read as an actor facing** (so a waypoint the author turned along the path leaves her facing it),
letting Jolt resolve collisions, and blends a moving clip down to an idle clip.
`MoveActor`'s **begin** also writes the moving clip to weight 1 and the idle clip to 0, so she starts in the
moving pose rather than blending up from whatever she held. `RotateActor` interpolates her facing from its
current yaw to a named waypoint's over `RotateActorState`, writing the body rotation. At the deadline an
unobstructed move arrives at the last waypoint (facing its orientation, idle clip at 1.0); an obstructed one
stops where the collision left it — the op never forces her through geometry.

**Rationale**: movement is part of the simulation (a character can be blocked by a wall), so the op drives the
body the way `Systems.playerInput` does — velocity + facing — rather than teleporting it; the spec records that
the user chose physics-driven motion. The spline maths and waypoint chains already exist (the camera flight uses
them, `specs/054`), and `Physics.setBodyVelocity` / `setBodyRotation` exist for exactly this.

**Active-zone only**: a move needs a simulated body, and an inactive zone's bodies are **detached** (`removeBody`,
not destroyed), so a physics-driven move cannot run there and the op performs nothing. This revises the
act-anywhere decision for motion only (spec Clarifications) — a **blend** still acts anywhere, because it only
writes the renderer's slot weights.

**A duplicated start point is skipped**: the walk's spline is `actorPos :: chainPositions`, but when she already
stands at the chain's start that duplicates the first point, and Catmull-Rom's endpoint tangent then bends the
first segment *backwards* — the walk stalls while the target loops out and back. So the chain's own points are
used when `actorPos` is within an epsilon of the chain's start.

**Facing convention**: an actor's forward is its local `+Z` — confirmed against Akane's IQM bind pose, which is
Z-up with the toes along `-Y`, and `ActorRender.upAxisFix` maps `-Y` to `+Z` (`Systems.forwardOf`, and the model
matrix `ActorRender` reads, agree). A waypoint's arrow — what `editor/Viewport.fs` draws and `newWaypointAtCamera`
authors — is its local `-Z`. So every op that faces an actor at a waypoint (`SetActor`'s placement, `RotateActor`'s
target and `MoveActor`'s end facing) converts the waypoint's rotation to the Y-yaw whose `+Z` aligns with the
waypoint's `-Z` (`CueEngine.actorFacing`); without it the actor faces 180° the wrong way. `MoveActor` slerps from
her own facing to that target. This also corrected `SetActor` (`specs/064`), which used the waypoint's rotation
unchanged.

**An actor's body carries its authored rotation.** The body is the source of truth while playing (`Systems.syncActors`
copies body→entity each frame), and `Physics.createCapsuleBody` builds at identity rotation — so `Zone.capsuleBody`
writes the transform's rotation onto the body. Without it, an editor-gizmo or `SetActor` facing lived only on the
entity and was overwritten by the identity body the moment Play began (the actor visibly turned in the editor but
snapped back in Play), which is what made `MoveActor` see her 90° off the path.

**Alternatives considered**: writing the transform per tick (`Physics.setBodyTransform`) — rejected, it is a
teleport that ignores geometry and the user chose physics-driven motion; `Zone.setTransform` per tick — rejected
(it destroys and recreates the body each frame); acting on a detached body — rejected (nothing integrates it).

## D4 — The player binding

**Decision**: `EnablePlayer character` sets the actor's zone's `Player` to her handle
(`Systems.playerHandleOf ref`); `DisablePlayer` clears it when she is the player and does nothing
otherwise. The camera follows her because `Systems.cameraFollow` runs whenever a player is live (`specs/045`).

**Rationale**: F# has no separate locomotion subsystem to attach — the player *is* the actor the play-mode input drives.
This is the runtime counterpart of the editor's "Set as Player", and the spec's recorded answer (Q2 = A).

**Alternatives considered**: a separate "driven actor" apart from the player (rejected — new machinery for no
case the spec has); toggling the follow system (that is a separate `CameraFollow` op, out of scope).

## D5 — Read a slot's current weight with a small reader

**Decision**: add `ActorRender.tryClipWeight` / `tryMorphWeight` (the named slot's current weight, or `None` when
the asset does not define it — a slot the actor does not hold reads 0), so a blend captures its starting weights
when it begins and detects a name the asset lacks.

**Rationale**: the setters exist but there is no reader (`Actor.slotIndexOf` and the morph equivalent are
private). The blend needs the starting weight once; the existence check is what makes an unknown name a no-op
(D7) rather than the setter's raise.

## D6 — Names, numbers, character, waypoints and clips: what is chosen and what is typed

**Decision**: a clip/morph name is a **combo over the character's asset's clip/morph names** (resolved from the
actor in the scene when she is placed, else from any cast binding for her — the authoring-time stand-in for the
art the cue resolves at run time), falling back to a typed field when the asset does not resolve; a `MoveActor`
path / `RotateActor` waypoint is a **combo over the active zone's** waypoints, offered by
`CueEditor.drawNodeText` exactly as the camera ops' waypoint combo is (`drawWaypointCombo`); the
weight/duration/speed are `dragNumber`s; the character is a chooser over `Roster.characters`. The panel takes
`SharedState` for the asset manager, and each two-name op's second field carries a distinct ImGui id (the same
id for both fields is an ImGui id collision).

**Rationale**: the op names a character, not a place, so the *control* (`CueTree`) has no zone or asset to read —
but the *panel* (`CueEditor`) has both, as it does for `SetCamera`. The author works in the zone the cue acts on
(spec Clarifications), so the active zone's waypoints are the right list; the name is still stored as a string and
resolved in the actor's own zone at run time, so the chooser is a convenience, not a coupling. A clip name is the
asset's own, so offering the asset's names (rather than typing) is what keeps the two in step — the earlier
"typed because the asset is not known at edit time" no longer holds now the panel has the asset manager.

## D7 — An unknown clip/morph name, and the no-op rule

**Decision**: an op performs nothing when the actor does not exist, when the waypoint/path does not resolve, or
when the asset lacks the named clip/morph (checked with `Actor.tryFindAnimIndex` and the morph names). It never
raises on redundant or contradictory state.

**Rationale**: the cue no-op rule (`specs/062`/`specs/064`) and the spec's Q3 = A. The existence check is done
once when the op begins, so the per-tick setters (which raise for a bad name) are never reached with one.

## D8 — Verification is headless

**Decision**: drive `CueEngine.tick` against a `GameplayState` carrying the fabricated `ActorAsset` fixture
(`specs/063`/`064`), with clips/morphs the fixture's asset defines (an empty model has none — the fixture is
extended with at least an `Idle` clip and a morph so blends can run), **stepping the physics world** for the
motion cases (an unobstructed move's body only moves when the world is stepped). Assert: a blend's weights move
from where they started to the target and land exactly; a move's body follows the spline and arrives at the last
waypoint with the idle clip at full weight; a turn's yaw reaches the waypoint's; enable binds the player and
disable clears it; every op no-ops on a missing actor / path / name, and a motion op no-ops when her zone is not
the active one. No device.
