# Research: Per-Actor Physics Modes and Properties

**Feature**: `specs/102-actor-physics`  
**Date**: 2026-10-17

## R1 — Kinematic cue movement that stops at geometry

**Decision**: Keep the actor's body as `MotionType.Kinematic` and make the
`MoveActor` step collision-aware with a capsule shape cast. Each step computes
the desired capsule-centre delta toward the spline target, casts the body's own
capsule shape in that direction, clamps the delta to the closest hit, and writes
the clamped target to the body. `RotateActor` continues to write the body
rotation directly.

**Rationale**:
- Kinematic bodies are not pushed by collisions, which is the core NPC
  behaviour the feature needs.
- `JoltPhysicsSharp.NarrowPhaseQuery.CastShape` is exposed by the pinned
  2.22.0 binding, and the actor's capsule shape can be read from the existing
  body with `BodyInterface.GetShape`.
- The cast runs only while a move cue is executing, so there is no idle
  per-frame cost.
- The body stays kinematic for the whole cue, so a player collision cannot
  shove her mid-move.

**Alternatives considered**:
- *Pure kinematic, no cast*: simplest, but she walks through walls, violating
  FR-015.
- *Dynamic body with gravity disabled*: geometry stops her, but the player can
  still push her, violating FR-005, and she may jitter at rest.
- *Jolt `CharacterVirtual`*: capable, but it is a second locomotion model with
  its own step/collision rules and is heavier than the feature needs.

## R2 — Applying mass in JoltPhysicsSharp

**Decision**: Use `BodyCreationSettings.OverrideMassProperties =
MassAndInertiaProvided` and
`BodyCreationSettings.MassPropertiesOverride =
shape.GetMassProperties().ScaleToMass(mass)`.

**Rationale**: Reflection over `JoltPhysicsSharp` 2.22.0 shows
`BodyCreationSettings` has no `Mass` property. It exposes
`OverrideMassProperties` and `MassPropertiesOverride`; `MassProperties` has
`ScaleToMass`. This is the binding's supported path.

**Alternatives considered**:
- Leave Jolt's calculated mass: no way to author a heavier or lighter actor.
- Per-shape density: not exposed by the binding and not a per-actor property.

## R3 — Changing a mode without leaking or drifting the body

**Decision**: Rebuild the actor's body in `Zone.setActorPhysics`: destroy the
old attachment (if any), build a new one from the asset-derived capsule and the
new setting, map the new body to the same ref, and leave the renderer untouched.
Preserve the entity transform exactly.

**Rationale**:
- The project already rebuilds a body when an actor's scale changes
  (`Zone.setTransform`); a mode change is the same cold-path lifecycle.
- Rebuilding applies motion type, allowed DOFs, mass, friction, restitution, and
  gravity factor in one place, with no risk of leaving a stale Jolt setting.
- Re-occupying the actor would rebuild GPU renderer resources and could lose
  runtime state; rebuilding only the body is smaller and faster.

**Alternatives considered**:
- Mutate the existing body with `SetMotionType`, `SetFriction`,
  `SetRestitution`, and `SetGravityFactor`: possible, but mass and allowed-DOF
  changes still need extra work, and the setter list is easy to keep
  incomplete.
- A full `occupyActor`: unnecessary renderer churn.

## R4 — Backward-compatible persistence

**Decision**: Add `Physics : ActorPhysicsDto option` to `ActorDto`. On decode,
`None` means `dynamicPhysics`; on encode, write `Some`. Keep
`projectVersion = 1`.

**Rationale**: Prime's symbolic serializer decodes an expanded record that is
missing a field by filling that field with `defaultof`; a quick FSI check
confirmed that a missing `int option` field decodes to `None`, and a missing
union field decodes to the first case. An option therefore gives an explicit
"no authored setting" signal. Existing manifests load unchanged, and existing
actors keep their old `Dynamic` behaviour.

**Alternatives considered**:
- Bump the format version and build a legacy DTO tree: more code and a
  conversion step for a field that can be defaulted safely.
- Non-optional field relying on union ordering: works, but the default is
  implicit and an absent field is indistinguishable from an authored
  `Dynamic`.

## R5 — Keeping `NoPhysics` actors viewport-selectable

**Decision**: Add an editor-only actor-bounds fallback to `Viewport.pick`. The
physics raycast remains the first path (props, environment, and body-backed
actors). If the nearest actor-bounds hit is closer than the physics hit, or the
physics ray misses, return the actor. Add the pure ray/capsule (or ray/bounds)
test to `editor/Raycast.fs` so it is headless-testable.

**Rationale**: A `NoPhysics` actor has no body, so the existing physics raycast
cannot find her. An editor-only geometric fallback keeps selection independent
of runtime physics, adds no runtime body, and avoids the lifecycle of an
editor-only sensor.

**Alternatives considered**:
- An editor-only sensor body: reintroduces a body for the mode that is defined
  as having no body, and adds create/destroy work on mode changes.
- Outliner-only selection: worse authoring UX and rejected in the clarification
  session.

## R6 — Keeping the player designation and the mode consistent

**Decision**:
- `Selection.setPlayer` records `Batch [ ActorPhysicsSet(...); SetPlayer(...) ]`
  when the target is not `Dynamic`; one undo step covers both.
- `Zone.ensurePlayerDynamic` is called when a zone is occupied and when a zone
  becomes active, and from the `SetPlayer` apply/undo path, covering staged
  scenarios and undo/redo.
- `CueEngine.EnablePlayer` promotes the actor at runtime before binding her.

**Rationale**: A kinematic player would follow input velocity but ignore walls,
which is worse than no promotion. Promotion at every designation/activation
entry point establishes one invariant: a designated player is `Dynamic`.

**Alternatives considered**:
- Let `playerInput` drive any body: a kinematic player passes through geometry.
- Clear the player designation when a non-dynamic mode is chosen: surprising for
  the author and adds a second hidden edit.
- Only promote in the Inspector: misses scenarios and runtime `EnablePlayer`.

## R7 — Defaults for new and legacy actors

**Decision**: Newly placed actors and cue-added actors use `kinematicPhysics`.
Actors decoded from a manifest without a physics field use `dynamicPhysics`.
The default is chosen by the spawn call sites (`Spawning.spawnActor` and
`CueEngine.addActor`); `Zone.spawnActor` consumes whatever `ActorState` it is
given, so `ScenarioFork` can pass the captured state unchanged.

**Rationale**: Kinematic is the behaviour the feature exists to provide for
placed NPCs, while legacy projects keep behaving as they did. A never-moving
NPC is a kinematic actor with no move cues, so no fourth mode is needed.

**Alternatives considered**:
- Default everything, including legacy files, to `Kinematic`: would silently
  change existing scenes.
- Per-asset defaults: a new authoring concept, outside the spec.

## R8 — Property defaults and bounds

**Decision**:

| Property | Default | Bounds |
|----------|---------|--------|
| Mass | 75 kg | 1–500 |
| Friction | 0.5 | 0–2 |
| Restitution | 0.0 | 0–1 |
| Gravity Factor | 1.0 | 0–4 |

Constants live beside `ActorPhysics` in `world/Entity.fs`. The Inspector bounds
each control to its range. The project decode rejects an out-of-range value with
`invalidArg`, which the existing `Project` error path reports; no invalid body is
built and no value is silently corrected.

**Rationale**: 75 kg is a plausible adult, 0.5 is Jolt's default friction, 0 is
Jolt's default restitution, and 1 is world gravity. The bounds keep mass
positive, keep restitution in its physical range, and allow up to four times
gravity for a heavy fall without allowing an unbounded value.

**Alternatives considered**:
- Use Jolt's raw defaults with no UI bounds: the spec requires bounded values.
- Clamp silently on decode: rejected because it hides a hand-editing error and
  conflicts with the project's Fail Loudly principle.
- Expose linear/angular damping and velocity limits: out of scope for the
  authoring surface.

## R9 — Property visibility by mode

**Decision**: `Dynamic` shows all four properties. `Kinematic` shows `Friction`
and `Restitution` only. `NoPhysics` shows none.

**Rationale**: Mass and gravity factor have no effect on a kinematic body,
because it is not integrated and is never pushed. Friction and restitution still
shape the contact between a kinematic body and a dynamic body, such as the
player, so they remain useful. A bodyless actor has no contacts at all.

## Related design notes

- The physics leaf keeps all New Jolt types (`MotionType`, `AllowedDOFs`,
  `MassPropertiesOverride`, `ShapeCastSettings`, body filters) inside
  `physics/Physics.fs`; `world/Entity.fs` and `world/Zone.fs` see only the
  authored setting.
- The shape cast excludes the actor's own body with a small `BodyFilter`
  subclass in `physics/Physics.fs`. It may hit other bodies on the way,
  including the player; that is consistent with "stops at an obstruction".
- The body's rotation remains locked for every actor mode, so no mode can make
  an actor tip over (FR-023).
