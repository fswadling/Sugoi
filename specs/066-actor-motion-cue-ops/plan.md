# Implementation Plan: Actor Motion & Animation Cue Ops

**Branch**: `066-actor-motion-cue-ops` | **Date**: 2026-10-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/066-actor-motion-cue-ops/spec.md`

## Summary

Add the rest of odin's actor cue family, addressed by `Roster.Character` (the cast index resolves her place):
the **animation/morph blends** `Animate` / `CrossFade` / `Morph` / `CrossMorph`, which interpolate a slot's
weight over a duration; the **movement** ops `MoveActor` (walk a waypoint chain, turning from her own facing to
the last waypoint's arrow, blending a moving clip to idle) and `RotateActor` (turn in place to face a waypoint);
and the **player** ops `EnablePlayer` / `DisablePlayer`, which bind/clear the actor as the zone's player. Each timed
op is a leaf that carries a `…State` in flight (like `Wait`/`FlyCamera`) and ends at its deadline — a move
arrives at its last waypoint when unobstructed and stops where a collision leaves it otherwise. An op on an
actor that does not exist, a waypoint/path that does not resolve, a clip/morph the asset lacks, or (for motion)
an actor in a zone that is not the active one performs nothing, so a cue is safe to re-run. No new module: the
ops go in `world/Cue.fs`, their effects and states in `world/CueEngine.fs`, small readers in
`world/ActorRender.fs`, and editor entries in `editor/CueTree.fs`.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: the cast index (`GameplayState.tryCastActor`, `specs/063`), the actor's per-instance
slot weights (`ActorRender.setClipWeight` / `setMorphWeight`; `Actor.AnimationSlot` / `MorphSlot`,
`specs/036`/`037`), the spline maths (`core/Maths.fs` `evalSpline` / `evalRotation` / `pathLength`), waypoint
chains (`Zone.tryWaypointByName` / `waypointChain`), the body accessors (`Physics.setBodyVelocity` /
`setBodyRotation`), and the player binding (`Systems.playerHandleOf`, `zone.Player`) — all present

**Storage**: N/A

**Testing**: xUnit, headless (`dotnet test`); the fabricated-`ActorAsset` fixture (`specs/063`/`064`) drives the
real blend/move/remove paths with no device

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: a blend or move updates the actor's slots/body each tick and allocates nothing per
tick (the state is a value the run holds, as `FlyCameraState` is); the move drives the body
(`Physics.setBodyVelocity` / `setBodyRotation`) in place rather than recreating it

**Constraints**: actor ops name only a rostered character; an unknown clip/morph name is a no-op; motion is
physics-driven and active-zone only; the build is warning-free; the suite is green

**Scale/Scope**: 4 source files (`world/Cue.fs`, `world/CueEngine.fs`, `world/ActorRender.fs`,
`world/Zone.fs` — the capsule body carries the authored rotation), 1 state file (`state/EditorState.fs`, a
second text field), 3 editor files (`editor/CueTree.fs`, `editor/CueEditor.fs`, `editor/Editor.fs` — one
signature change); 2 test files; no `.fsproj` change

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. Seven leaf ops, three `…State` cases reused across them (one blend state for four
  blend ops), and two small readers. The ops reuse the existing slot-weight, spline and body code rather than
  adding systems.
- **II. Fail Loudly** — PASS, with the recorded nuance. The **editor's** setters (`ActorRender`) keep raising on
  a name the asset lacks; only the **cue layer** checks the name and no-ops, so a cue stays re-run safe
  (spec Clarifications). The guard lives in `CueEngine`, so the loud behaviour is not diluted.
- **III. Spec-First** — PASS. This feature is `specs/066-actor-motion-cue-ops`.
- **IV. Data-Oriented Architecture** — PASS. The ops are union cases; the states are values the run holds; the
  move drives the actor's body in place (velocity/facing, no body rebuild).
- **V. Minimise GC Pressure** — PASS. A blend/move tick allocates nothing (states are structs/arrays the run
  already holds; the per-tick path writes weights and the body's velocity/facing in place); a spline is resolved
  once when the op begins.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/066-actor-motion-cue-ops/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── actor-motion-cue-ops.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
world/Cue.fs              # the ops: Animate/CrossFade/Morph/CrossMorph, MoveActor/RotateActor,
                          #   Enable/DisablePlayer, and the BlendState/MoveActorState/RotateActorState leaves
world/ActorRender.fs      # tryClipWeight / tryMorphWeight: read a slot's current weight (the blend's start)
world/Zone.fs             # capsuleBody writes the transform's rotation onto the body, so an authored facing survives Play
world/CueEngine.fs        # the arms and states: begin a blend/move/turn, step it, land it; bind/clear the player;
                          #   the guards (actor exists, path resolves, name exists)
editor/CueTree.fs         # kinds/labels/defaultOfKind/textOf/textOf2/withText/withText2/withWaypoint/withCharacter and the drawEditor arms
editor/CueEditor.fs       # the waypoint/path chooser (active zone) and the clip/morph choosers (the character's asset, over SharedState), plus the second field
editor/Editor.fs          # CueEditor.draw now takes `shared` (the clip/morph choosers read the asset manager)
state/EditorState.fs      # a second reused text buffer for the two-field ops (a cross-fade, a move)
test/CueTests.fs          # the effects and no-ops, through CueEngine.tick
test/CuePanelTests.fs     # labels, kinds, writes for the new ops
```

**Structure Decision**: No module moves and no compile-order change. `Roster`, `Casting`, `Place`, `Cue`,
`Entity`, `Zone`, `Actor` and `ActorRender` all precede `CueEngine` (which is compiled after `GameplayState`),
so the ops may name an actor, resolve her zone through the index, and write her renderer and body where the walk
reaches them — exactly as `specs/064`'s ops and the camera ops do. The blend/move logic lives in `CueEngine`, not
`ActorRender`: `ActorRender` stays a renderer, and the walk owns the run state.

## Complexity Tracking

> No Constitution Check violations.
