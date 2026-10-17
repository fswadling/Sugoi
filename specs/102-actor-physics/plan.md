# Implementation Plan: Per-Actor Physics Modes and Properties

**Branch**: `102-actor-physics` | **Date**: 2026-10-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/102-actor-physics/spec.md`

## Summary

Give every actor an authored physics setting: one of three modes (`Dynamic`,
`Kinematic`, `NoPhysics`) and four dynamic properties (`Mass`, `Friction`,
`Restitution`, `GravityFactor`). New actors default to `Kinematic`, so a placed
NPC stands still when the player collides with her. The Inspector edits the
setting as one undoable step, and it round-trips through duplicate, scenario
staging, and `project.manifest`. Existing projects load with their actors as
`Dynamic`, preserving today's behaviour.

The implementation extends the existing authored/derived split: the setting lives
on `ActorState`, the body is rebuilt from it in `Zone`, and the Jolt leaf gains a
parameterised capsule factory. Kinematic cue movement is made collision-aware
with a capsule shape cast before each step; the body itself stays kinematic, so
the player cannot push it.

## Technical Context

**Language/Version**: F# / .NET 10.0

**Primary Dependencies**: JoltPhysicsSharp 2.22.0, Prime 11.5.2, SDL3-CS 3.4.14.1, custom cimgui binding

**Storage**: `project.manifest` — Prime symbolic-expression text (s-syntax), auto-saved

**Testing**: xUnit + FsCheck via `dotnet test`; headless physics and editor tests

**Target Platform**: Linux desktop (SDL3)

**Project Type**: Desktop game with an in-engine editor; single F# executable plus a test project

**Performance Goals**: No per-frame cost proportional to actor count beyond the existing `Systems.syncActors` pass; no per-frame allocation added; existing 60 fps target unchanged

**Constraints**:
- No new entity kind and no new zone-level setting.
- No project format version bump: an actor record without a physics field decodes as `Dynamic`.
- Actor capsules remain derived from the asset bounds; the authored setting does not change the collider shape.
- Play mode remains non-authoring; physics controls are edit-mode only.

**Scale/Scope**: At most 64 actors per zone (existing `Zone.ActorsCapacity`); three modes; four numeric properties.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Result |
|-----------|-------|--------|
| I. Minimalism | Three modes, no new source modules, reuse the existing command/undo/persistence paths, no per-asset defaults | PASS |
| II. Fail Loudly | Stale refs stay no-ops as the surrounding code does; an unsupported mode or out-of-range property is rejected at the editor/decode boundary rather than silently defaulted | PASS |
| III. Spec-First | `specs/102-actor-physics/spec.md` is ratified as Draft before code | PASS |
| IV. Data-Oriented Architecture | `ActorPhysics` is a `[<Struct>]` record beside `ActorState`; the mode is a small union; no inheritance or object graph | PASS |
| V. Minimise GC Pressure | Body rebuild happens only on a cold edit; the per-frame sync gains a mode check and no allocation | PASS |

No violations. Complexity tracking is empty.

**Post-Phase 1 re-check**: PASS. The design adds no new project, no new module, and no per-frame allocation; the only non-trivial cost is a shape cast during a kinematic move cue, which runs only while that cue is executing.

## Project Structure

### Documentation (this feature)

```text
specs/102-actor-physics/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── checklists/
│   └── requirements.md  # Specification quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

No `contracts/` directory: the feature has no external API or file-format
contract beyond the existing project manifest, which is covered in
`data-model.md`.

### Source Code (repository root)

No new source modules are required. The work is distributed across existing
files in compile order:

```text
world/Entity.fs          # ActorMobility, ActorPhysics, ActorState.Physics
physics/Physics.fs       # capsule settings, mass override, capsule shape cast
world/Zone.fs            # body settings mapping, setActorPhysics, spawn from state,
                         # ensurePlayerDynamic
world/Project.fs         # ActorPhysicsDto, backward-compatible field, decode bounds
world/Systems.fs         # player input guard for Dynamic
state/Command.fs         # ActorPhysicsSet apply/undo and SetPlayer promotion
state/CueEngine.fs       # kinematic-aware move step; EnablePlayer promotion
state/GameplayState.fs   # promote the player on zone activation
state/ScenarioFork.fs    # spawn from the scenario's ActorState as-is
editor/Spawning.fs       # Kinematic default; duplicate copies the setting
editor/Selection.fs      # updateActorPhysicsRef; Set as Player batch
editor/Inspector.fs      # mode combo and dynamic property controls
editor/Raycast.fs        # editor-only ray/capsule or ray/bounds helper
editor/Viewport.fs       # mode-independent actor picking fallback
editor/Editor.fs         # pass assets to Viewport.pick
test/TestSupport.fs      # actor literal gains Physics
test/ActorPhysicsTests.fs # new: modes, properties, cues, picking, persistence, perf
test/Sugoi.Tests.fsproj  # compile the new test file after PhysicsTests/ZoneTests
```

**Structure Decision**: Keep the existing single-project layout. Physics types
stay in `world/Entity.fs` because `ActorState` composes them; the Jolt-specific
settings stay in `physics/Physics.fs`, the leaf that already isolates Jolt. The
new test file is the only new source file.

## Phase 0: Outline & Research

Research questions extracted from the spec and the current code:

1. How to keep a `Kinematic` actor cue-movable and collision-aware without
   making it pushable.
2. How to apply mass in JoltPhysicsSharp (`BodyCreationSettings` has no `Mass`
   property, only `MassPropertiesOverride`).
3. How to change an actor's mode without leaking or drifting the body.
4. How to add the persisted field without breaking existing manifests.
5. How to keep `NoPhysics` actors selectable in the viewport.
6. How to keep the player designation and the mode consistent.
7. What defaults and bounds to use for the four properties.

Findings and decisions are in [research.md](./research.md).

## Phase 1: Design & Contracts

### Data model

The authored type, its bounds, the default values, the body mapping, the
persistence DTO, and the state transitions are in
[data-model.md](./data-model.md).

### Interface surfaces

This feature has no external interface. Its editor contract is the Inspector's
actor section:

- A `Physics` combo with `Dynamic`, `Kinematic`, and `NoPhysics`.
- Four drag controls: `Mass`, `Friction`, `Restitution`, `Gravity Factor`.
- The controls are shown for a selected actor in edit mode.
- For the designated player, only `Dynamic` is offered.
- `Dynamic` shows all four properties; `Kinematic` shows `Friction` and
  `Restitution` only; `NoPhysics` shows none.

The command contract is one new `ActorPhysicsSet` step; duplicate, scenario
capture, and project persistence carry the setting through the existing
`ActorState` payload.

### Validation guide

Manual and automated validation steps are in [quickstart.md](./quickstart.md).

## Implementation Phases

### Phase A — Authored model and persistence

1. Add `ActorMobility` and `ActorPhysics` to `world/Entity.fs`, with
   `dynamicPhysics` / `kinematicPhysics` defaults and the bounds constants.
2. Add `Physics : ActorPhysics` to `ActorState`.
3. Add `ActorPhysicsDto` and the optional `Physics` field to `ActorDto` in
   `world/Project.fs`; map `None` to `dynamicPhysics`, and reject out-of-range
   values through the existing project decode error path.
4. Fix every `ActorState` literal (tests and test support) so the project
   compiles.

### Phase B — Physics and zone lifecycle

1. Add `CapsuleSettings` (motion type, friction, restitution, mass, gravity
   factor, sleeping, allowed DOFs) to `physics/Physics.fs`; make
   `createCapsuleBody` take rotation and settings.
2. Apply mass through `MassPropertiesOverride` (`ScaleToMass`) and the other
   fields directly on `BodyCreationSettings`.
3. Add a capsule shape-cast helper to `physics/Physics.fs`, with a filter that
   excludes the actor's own body.
4. Map `ActorPhysics` to settings in `world/Zone.fs`:
   `Dynamic -> Dynamic`, `Kinematic -> Kinematic`, `NoPhysics -> None`.
5. Add `Zone.setActorPhysics` that destroys the old body, rebuilds the new one,
   preserves the renderer, and maps the body to the ref.
6. Change `Zone.spawnActor` to consume a full `ActorState` so `ShowCollider` and
   `Physics` ride the same value. The default `kinematicPhysics` belongs to the
   spawn call sites: `Spawning.spawnActor` and `CueEngine.addActor` build it, while
   `ScenarioFork.materialise` passes the captured state.
7. Add `Zone.ensurePlayerDynamic`, called from `Zone.occupyZone`,
   `GameplayState.swapZone`, and the `SetPlayer` apply/undo path in
   `state/Command.fs`.

### Phase C — Editor, command, and undo

1. Add `ActorPhysicsSet of ZoneId : string * Ref : EntityRef * Before : ActorPhysics * After : ActorPhysics`
   to `state/Command.fs`; add it to `placeOf`, `apply`, and `undo`.
2. Add `Selection.updateActorPhysicsRef`, mirroring `updateTriggerRef`, with a
   no-op when the value is unchanged.
3. Draw the Inspector controls in the actor-only section.
   - Combo commits immediately.
   - Drags commit on `isItemDeactivatedAfterEdit`.
   - The designated player gets a disabled/dynamic-only combo.
   - `Dynamic` shows all four properties; `Kinematic` shows `Friction` and
     `Restitution` only; `NoPhysics` shows none.
4. Make `Selection.setPlayer` record a `Batch [ ActorPhysicsSet; SetPlayer ]`
   when the target is not already `Dynamic`.
5. Make the `SetPlayer` apply and undo paths in `state/Command.fs` call
   `Zone.ensurePlayerDynamic` after writing the handle, so undo/redo cannot
   restore a non-dynamic player.

### Phase D — Runtime semantics

1. Guard `Systems.playerInput` on `Mobility = Dynamic`.
2. Keep `Systems.syncActors` as-is: it already skips `None` bodies and should
   copy kinematic bodies moved by cues.
3. Make `CueEngine.stepMove` branch on mobility:
   - `Dynamic`: existing velocity-driven step.
   - `Kinematic`: compute the target capsule centre, shape-cast the horizontal
     delta, clamp to the hit fraction, and write the body transform; keep the
     facing slerp and clip weights.
   - no body: the existing `None` path ends the state.
4. `CueEngine.EnablePlayer` promotes the actor to `Dynamic` before binding her.
5. Keep `RotateActor` unchanged; it works on both body-backed modes.

### Phase E — Mode-independent viewport picking

1. Add an editor-only ray/capsule (or ray/bounds) helper to `editor/Raycast.fs`.
2. Extend `Viewport.pick` to take the asset manager, compare the physics hit
   with the nearest actor bounds, and pick the nearer one. `NoPhysics` actors
   therefore remain selectable without a runtime body.
3. Update `Editor.beginDragOrPick` to pass `shared.Assets`.

### Phase F — Tests and verification

1. Add `test/ActorPhysicsTests.fs`:
   - Mode maps to the expected body presence and motion type, with the upright lock.
   - Kinematic and NoPhysics actors are not displaced by a dynamic player.
   - NoPhysics actors allow the player through and have no body.
   - Kinematic cue movement stops at a mesh wall; Dynamic still stops and
     NoPhysics no-ops, with the facing slerp and clip weights preserved.
   - Mass, friction, restitution, and gravity factor each affect a dynamic actor,
     and each edit is one undo step preserving position/facing.
   - Duplicate, scenario fork, save/reload, and delete/restore round-trip the setting.
   - A manifest without the field decodes as Dynamic; an out-of-range value is
     rejected through the project error path.
   - Player designation promotes a non-dynamic actor in one step, including when
     undo/redo re-designates a kinematic previous player, and on zone activation.
   - `Viewport.pick` finds a NoPhysics actor via the fallback.
   - `Systems.syncActors` allocates nothing across all three modes; a manual
     frame-rate check is recorded in the quickstart.
2. Add the test file to `test/Sugoi.Tests.fsproj` after the physics/zone tests.
3. Run `dotnet build` and `dotnet test`.

## Dependencies and Ordering

- Phase A is a prerequisite for every other phase.
- Phase B depends on Phase A (settings and state).
- Phase C depends on Phase B (`Zone.setActorPhysics`).
- Phase D depends on Phase B (motion mapping) and Phase A (`EnablePlayer`).
- Phase E is independent of B–D except for reading `ActorState.Physics`; it can
  be developed in parallel with C/D once A lands.
- Phase F follows the phases it verifies; tests can be written alongside each
  phase.

## Complexity Tracking

No Constitution Check violations, so this section is intentionally empty.
