# Quickstart: Actor Motion & Animation Cue Ops

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`).
- A cast binding for the character used, and an actor asset whose model defines the clips/morphs the cue names
  (the headless tests use a fabricated asset extended with an `Idle` clip and a morph).

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with no warnings and the suite green, with no device opened. The tests drive
`CueEngine.tick` against a `GameplayState` carrying the fabricated `ActorAsset` fixture, so the real paths run
headlessly:

- an `Animate` moves a clip's weight from where it started to the target, reaching it exactly at the deadline;
- a `CrossFade` fades one clip out as another comes in;
- a `Morph` / `CrossMorph` blend a morph slot the same way;
- a `MoveActor` (with the physics world stepped) drives the actor's body along a waypoint chain — it follows the
  spline and arrives at the last waypoint at the deadline, facing its orientation, with the idle clip at 1.0;
- a `RotateActor` turns the actor's yaw to the waypoint's over the duration;
- `EnablePlayer` binds the actor as the player and `DisablePlayer` clears it;
- every op **no-ops** on an actor not in the scene, an unresolved waypoint/path, or a clip/morph the asset
  lacks, and a motion op **no-ops** when her zone is not the active one — and a re-run does not fault.

`test/CuePanelTests.fs` asserts the panel's pure half — the new labels, kinds and writes.

## Manual

1. **Blend**: author `Animate` (or `CrossFade`) on a placed member and Play — watch the clip (or the cross-fade)
   play out over the duration.
2. **Walk**: lay a chain of waypoints, author `MoveActor` naming the chain's start and a speed, and Play — the
   member walks the path, facing it, and settles into the idle clip.
3. **Turn**: author `RotateActor` naming a waypoint and a duration, and Play — she turns to face it.
4. **Drive**: author `EnablePlayer` and Play — the play-mode keys now drive her and the camera follows
   her; `DisablePlayer` stops it.

## References

- Contract: [contracts/actor-motion-cue-ops.md](./contracts/actor-motion-cue-ops.md)
- Data model: [data-model.md](./data-model.md)
- Research (the design decisions): [research.md](./research.md)
