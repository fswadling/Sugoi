# Implementation Plan: Walk Around

**Branch**: `016-walk-around` | **Date**: 2026-09-12 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/016-walk-around/spec.md`

## Summary

Phase 7 of `docs/ROADMAP.md`: the playable backbone. Bind one of the zone's
actors as the player through a deliberate editor action, then drive it in play
mode with tank controls (`W`/`S` along facing, `A`/`D` turn) and physical
collision. Actor capsules become dynamic with rotation locked, so gravity and
collision come from the physics step and the character stays upright; the player's
position, velocity, and facing live on that existing body. The play-mode system
chain is `player_input → physics step → transform sync → camera_follow` (plus the
idle/jog animation), with camera follow as its own system and state. When no
player is bound (or the binding is stale) every player system is a no-op.
`Set as Player` records only the handle — it creates, resets, and destroys
nothing — and play/edit simply includes or excludes the systems.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`).

**Primary Dependencies**: `JoltPhysicsSharp` 2.22 (dynamic bodies via
`BodyCreationSettings.AllowedDOFs`), `System.Numerics`. No new managed dependency;
input is already gathered by the engine (`core/Input.fs`).

**Storage**: N/A — in-memory only. The player binding is not persisted and is not
undoable.

**Testing**: xUnit — new `test/PlayerTests.fs` for the pure helpers
(`playerHandleOf`, `locomotionClip`, `cameraPose`) and the no-op guards (the
systems do nothing with no player or a stale handle), plus a headless
`test/PhysicsTests.fs` case that a dynamic capsule falls, lands, and stays upright.
No GPU device or character object required.

**Target Platform**: Linux x64 desktop (native).

**Project Type**: native desktop game.

**Performance Goals**: 60 fps while walking. The play chain is per-frame hot code:
systems mutate state in place, the actor body is *not* rebuilt per frame, and no
per-frame allocations are introduced.

**Constraints**: F# compile order is the dependency order. `physics/Physics.fs`
stays a leaf over `JoltPhysicsSharp` + `System.Numerics` (no `Entity`/GPU
dependency). Systems live in `world/Systems.fs` and take domain arguments
explicitly (`Zone`, `Physics.World`, the player handle, `byref` state) to avoid a
`world → state` module cycle; `GameplayScreen` (composed last) drives the order.

**Scale/Scope**: one player, one active zone, static environment/prop colliders
(hundreds at most) plus the actor capsules. No sprint, jump, or crouch.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: four small systems, no character object, no locomotion
  copy (the body is the state), and no speculative modes (no walk/run blend, no
  jump); camera follow is deliberately separate rather than folded into input.
- **II. Fail Loudly** — PASS: *Set as Player* rejects non-actors; a missing
  idle/jog clip raises; a binding that no longer resolves is inert, not silently
  re-pointed.
- **III. Spec-First** — PASS: `spec.md` precedes this plan and the tasks.
- **IV. Data-Oriented Architecture** — PASS: camera-follow state is a `[<Struct>]`
  record mutated in place; the binding is a value-type `SlotMap.Handle option`; the
  player's motion lives on an existing struct body; systems are plain `unit`
  procedures.
- **V. Minimise GC Pressure** — PASS: per-frame code writes through `byref`s and
  never rebuilds bodies or records; no new per-frame allocations.

## Project Structure

### Documentation (this feature)

```text
specs/016-walk-around/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 validation guide
├── contracts/
│   └── player-systems.md # System + physics adapter surface
├── checklists/
│   └── requirements.md  # /speckit-specify quality gate
└── tasks.md             # /speckit-tasks (NOT created here)
```

### Source Code (repository root)

```text
world/Systems.fs            # NEW: CameraFollow + playerInput, syncActors, cameraFollow, animate
physics/Physics.fs          # EXTENDED: dynamic rotation-locked actor capsules + body accessors
world/ActorRender.fs        # EXTENDED: Idle/Jog clip switch helper
state/GameplayState.fs      # EXTENDED: Player handle + camera-follow state
state/Command.fs            # (unchanged: player binding is not a command)
editor/Inspector.fs         # EXTENDED: Set as Player on actors
screen/GameplayScreen.fs    # EXTENDED: run the chain (and step) only in play mode
fsharp-game.fsproj          # add world/Systems.fs after world/Zone.fs
test/PlayerTests.fs         # NEW: handle/clip/camera-pose helpers
test/PhysicsTests.fs        # EXTENDED: dynamic capsule fall/rest
test/fsharp-game.Tests.fsproj
```

**Structure Decision**: `world/Systems.fs` is inserted after `world/Zone.fs` so it
can see `Zone`, `Entity`, `Physics`, `Camera`, and `Input` while remaining below
`state/`. It depends on none of `SharedState`/`GameplayState`; `GameplayScreen`
supplies `shared.Physics`, `state.Zone`, and `state.Player`, and gates the chain on
play mode — the world steps with or without a player. No character object is created: the actor's existing dynamic
capsule body is the player body, so editor picking and the `BodyToEntity` map keep
working unchanged.

## Complexity Tracking

No constitution violations to justify.
