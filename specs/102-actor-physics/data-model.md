# Data Model: Per-Actor Physics Modes and Properties

**Feature**: `specs/102-actor-physics`

## Authored types

### ActorMobility

```fsharp
[<Struct>]
type ActorMobility =
    | Dynamic     // gravity and collisions move her
    | Kinematic   // solid, not pushed; cue move/rotate works
    | NoPhysics   // no body, no collision volume
```

The first two mirror Jolt's `MotionType` names. `NoPhysics` is the project's
extension for an actor with no body at all.

### ActorPhysics

```fsharp
[<Struct>]
type ActorPhysics =
    { Mobility : ActorMobility
      Mass : float32
      Friction : float32
      Restitution : float32
      GravityFactor : float32 }
```

Defaults:

```fsharp
let dynamicPhysics =
    { Mobility = Dynamic
      Mass = 75.0f
      Friction = 0.5f
      Restitution = 0.0f
      GravityFactor = 1.0f }

let kinematicPhysics =
    { dynamicPhysics with Mobility = Kinematic }
```

Bounds:

| Field | Range | Rationale |
|-------|-------|-----------|
| `Mass` | 1.0–500.0 | positive, human-plausible to heavy |
| `Friction` | 0.0–2.0 | Jolt contact friction range |
| `Restitution` | 0.0–1.0 | physical bounce range |
| `GravityFactor` | 0.0–4.0 | zero gravity to a heavy fall |

Bounds are enforced by the Inspector controls. A project decode rejects an
out-of-range value with `invalidArg`, which the existing `Project` error path
reports; no invalid body is built and no value is silently corrected.

### Property visibility by mode

| Mode | Mass | Friction | Restitution | Gravity Factor |
|------|------|----------|-------------|----------------|
| `Dynamic` | shown | shown | shown | shown |
| `Kinematic` | hidden | shown | shown | hidden |
| `NoPhysics` | hidden | hidden | hidden | hidden |

Mass and gravity factor do not affect a kinematic body. Friction and restitution
still shape its contacts with dynamic bodies, such as the player.

### ActorState

```fsharp
[<Struct>]
type ActorState =
    { Base : Entity
      AssetId : string
      ShowCollider : bool
      Physics : ActorPhysics }
```

`ActorState` remains the whole undo/duplicate/scenario payload. Adding the field
here means `ActorCreate`/`ActorDelete`, `Spawning.duplicate`, and
`ScenarioZone` capture all carry it without new command shapes.

## Derived types

### Actor

```fsharp
[<Struct>]
type Actor =
    { State : ActorState
      mutable Physics : PhysicsAttachment<CapsuleCollider> option
      mutable Renderer : ActorRender.ActorRender }
```

`Physics` is `None` for `NoPhysics` and `Some` for `Dynamic`/`Kinematic`. The
collider remains the asset-derived capsule; the authored setting does not change
its radius or half-height.

### Body mapping

| Mobility | Body | Allowed DOFs | Gravity | Collision response |
|----------|------|--------------|---------|--------------------|
| `Dynamic` | `MotionType.Dynamic` | translation only (upright lock) | `GravityFactor` from the setting | pushed by and pushes other dynamic bodies |
| `Kinematic` | `MotionType.Kinematic` | translation only | ignored (authored factor defaults to 1.0) | blocks dynamic bodies, is never pushed |
| `NoPhysics` | none (`None`) | — | — | no collision volume |

Every body-backed mode keeps rotation locked, so no actor can tip over.

## Persistence

```fsharp
[<SymbolicExpansion>]
type ActorPhysicsDto =
    { Mobility : ActorMobility
      Mass : float32
      Friction : float32
      Restitution : float32
      GravityFactor : float32 }

[<SymbolicExpansion>]
type ActorDto =
    { Base : EntityDto
      AssetId : string
      ShowCollider : bool
      Physics : ActorPhysicsDto option }
```

Mapping rules:

- Encode: `Some { ... }` from `ActorState.Physics`.
- Decode: `Some dto` becomes the authored value; `None` becomes
  `dynamicPhysics`.
- Decode rejects a `Some` whose values are outside the bounds with `invalidArg`;
  the project loader reports it and installs no partial library.
- `Address` and other entity fields are unchanged.
- The project version stays at `1`; a missing field decodes as `None`, which is
  the legacy signal.

## Command payload

```fsharp
| ActorPhysicsSet of
    ZoneId : string *
    Ref : EntityRef *
    Before : ActorPhysics *
    After : ActorPhysics
```

- `apply` writes `After`; `undo` writes `Before`.
- Both call `Zone.setActorPhysics`, which rebuilds the body.
- The command carries the authored value only; no body, renderer, or runtime
  state.

## State transitions

| From | Action | To | Body effect |
|------|--------|----|-------------|
| Any | Inspector mode/property edit | New setting | old body destroyed; new body built from the new setting |
| Any | Undo `ActorPhysicsSet` | Previous setting | old body destroyed; previous body rebuilt |
| Any | `Duplicate` | Copy with same setting | new actor built with the copied setting |
| Any | Scenario fork | Same setting | materialised actor built with the scenario's setting |
| Any | Save / reload | Same setting | round-trips through `ActorPhysicsDto` |
| Non-dynamic | `Set as Player` | `Dynamic` + player designation | body rebuilt as dynamic in the same undo step |
| Non-dynamic | Undo/redo of a `SetPlayer` step | `Dynamic` if restored as the player | body rebuilt as dynamic by the command path |
| Non-dynamic | Zone activation with a player designation | `Dynamic` | body rebuilt as dynamic before the zone runs |
| Non-dynamic | Cue `EnablePlayer` | `Dynamic` | body rebuilt as dynamic at runtime |
| Dynamic | Cue `DisablePlayer` | unchanged | no body change |

## Invariants

1. A designated player's actor is always `Dynamic`. This is enforced on
   designation, on zone activation/occupation, and on undo/redo of a `SetPlayer`
   step.
2. `NoPhysics` means `Actor.Physics = None`; no runtime body exists.
3. `Dynamic` and `Kinematic` always have a capsule attachment whose collider is
   derived from the actor asset and current scale.
4. Every actor mode keeps the body upright; rotation is locked on every axis.
5. `ActorState.Physics` is the only authored source of the setting. A body is
   always rebuilt from it, never mutated in place by an editor edit.
6. A physics edit that leaves `Before = After` records no command.
