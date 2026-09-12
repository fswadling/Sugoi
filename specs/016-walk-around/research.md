# Research: Walk Around

Decisions derived from `docs/ROADMAP.md` phase 7, `docs/ARCHITECTURE.md` (system
chain and gameplay state), the spec's resolved controls/scope, and the
`JoltPhysicsSharp` 2.22 binding surface.

## Decision 1 — Dynamic actor capsules, rotation locked

- **Decision**: actor capsule bodies are created as `MotionType.Dynamic` with
  `AllowedDOFs` restricted to translation (`TranslationX|Y|Z`) and sleeping
  disabled. Jolt then applies the world gravity and resolves collisions, while the
  body cannot topple because rotation is locked. Facing is written directly.
- **Rationale**: this reuses the actor body that phase 6 already creates, needs no
  separate character object, and gives gravity + collision for free. Rotation lock
  is what keeps a walking character upright.
- **Alternatives considered**: a kinematic capsule driven by `MoveKinematic` —
  rejected because Jolt kinematic bodies are not stopped by static geometry, so the
  character would walk through walls; a Jolt `CharacterVirtual` — rejected because
  it means creating a separate physics object when the player is bound, and
  setting the player should not change the physics world. The binding exposes
  `BodyCreationSettings.AllowedDOFs`, `MotionType.Dynamic`, and `BodyInterface`
  velocity/rotation/transform accessors (verified in `JoltPhysicsSharp` 2.22).

## Decision 2 — Reuse the actor's existing physics body

- **Decision**: locomotion drives the actor's existing dynamic capsule body. The
  player's position, velocity, and facing live on that body and the actor's entity
  transform; no state is duplicated in a separate character or locomotion struct.
- **Rationale**: `Set as Player` must not create or alter physics, and actors
  already have a physics entity. The body is the single source of truth while
  playing, so a parallel `PlayerLocomotion` copy could only drift.
- **Alternatives considered**: a synced kinematic proxy plus a virtual character
  (rejected — two representations); a separate locomotion state struct (rejected —
  redundant with the body).

## Decision 3 — Tank controls, camera derived from the body

- **Decision**: `W`/`S` set the body's horizontal velocity along its facing; `A`/`D`
  add to the facing and write the body's rotation. The follow camera's yaw comes
  from the actor's facing. No mouse-look, no strafing.
- **Rationale**: resolved in the spec — tank controls keep the phase small and
  avoid cursor capture and window plumbing. Facing is recoverable from the actor's
  transform, so no yaw state is stored.
- **Alternatives considered**: mouse-look or camera-relative WASD (rejected — needs
  look input and window plumbing).

## Decision 4 — Gravity is the physics step's job; no gravity system

- **Decision**: there is no separate gravity system. The dynamic body is pulled by
  the world's gravity and integrated by `Physics.step`; the locomotion system
  preserves the body's vertical velocity and only writes the horizontal component.
- **Rationale**: `docs/ROADMAP.md` lists a gravity system from the odin-game chain,
  but a dynamic body already gets gravity from the simulation; a manual integrator
  would double-count it.
- **Alternatives considered**: manual gravity integration with a kinematic body
  (rejected with Decision 1).

## Decision 5 — No grounded tracking

- **Decision**: `Grounded` is not tracked; nothing in this phase needs it (there is
  no jump, and animation follows the keys).
- **Rationale**: a dynamic body has no character ground state, and a bespoke probe
  would be just-in-case code.
- **Alternatives considered**: a downward raycast from the feet (rejected — unused
  signal; revisit when jumping lands).

## Decision 6 — Systems in `world/Systems.fs`, gated by the editor

- **Decision**: the player systems live in `world/Systems.fs` as top-level `unit`
  functions taking explicit domain arguments (`Physics.World`, `Zone`, the player
  handle, `byref` camera-follow state). `GameplayScreen.update` runs them only while
  `not Editing`. There are no enter/leave-play callbacks.
- **Rationale**: `Set as Player` records everything the runtime needs (the handle),
  and play/edit is only a flag that includes or excludes the systems — matching the
  spec. `state/` compiles after `world/`, so the systems take domain pieces rather
  than `GameplayState`/`SharedState`.
- **Alternatives considered**: `beginPlay`/`endPlay` lifecycle on the mode toggle
  (rejected — the toggle must not create or reset anything); placing the systems in
  `state/` (rejected — breaks the planned `world/Systems.fs` layout).

## Decision 7 — Sync the entity transform from the body

- **Decision**: after the step, a `syncActors` system reads every actor body's position
  and rotation and writes that actor's entity transform in place (feet = capsule centre
  minus the capsule offset), through the `Actors.Get` byref — never by struct copy
  and never touching the body. It is not player-gated: all actors are dynamic.
- **Rationale**: while playing a body is authoritative, so every render transform must
  follow it. Syncing only the player left the other actors' colliders moving away from
  their frozen render positions, so the player pushed an invisible capsule and walked
  through the visible actor. The editor's `Zone.setTransform` still recreates the body,
  which keeps the two consistent when an actor is moved while editing.
- **Alternatives considered**: writing both body and entity each frame (rejected —
  fighting the simulation); syncing only while a player is bound (rejected — the world
  simulates without a player, so its actors would still desync).

## Decision 8 — Player binding is deliberate, top-level, and non-undoable

- **Decision**: `GameplayState.Player : SlotMap.Handle option` is set only by a
  deliberate *Set as Player* action on an actor (replacing any previous binding)
  and is not part of the undo stack. Because the handle is an index + generation
  (`SlotMap.Handle`, not the editor's `EntityRef`), a binding whose actor has been
  deleted or whose slot has been recycled fails `Actors.IsAlive` and is inert — no
  explicit clearing step is needed.
- **Rationale**: resolved in the spec/design. The player is always an actor, so the
  `EntityRef` kind and environment-epoch fields would be dead weight.
- **Alternatives considered**: derive the player from the editor selection or
  auto-pick the first actor (rejected — implicit); `EntityRef option` (rejected —
  editor construct with fields the runtime binding does not need).

## Decision 9 — Animation from held movement keys

- **Decision**: switch the player actor's animation slot between `Idle` and `Jog`
  based on whether a movement key is held (`W`/`A`/`S`/`D`), through a small
  `ActorRender` helper. The named clips are required; a missing clip raises.
- **Rationale**: resolved in the spec — the signal is raw key state, which is
  simple and matches player intent; a character pressed against a wall while
  holding a movement key still reads as moving.
- **Alternatives considered**: resolved speed/displacement (rejected — a blocked
  player would read as idle despite holding movement input); blend/weighted slots
  (rejected — deferred to phase 13).

## Decision 10 — Step the physics world for the whole of play mode

- **Decision**: `Physics.step` runs whenever the game is playing, whether or not a
  player is bound. The editor does not step.
- **Rationale**: actor bodies are dynamic, so stepping while editing would make
  placed actors fall — the editor is a passive view. In play mode the world should
  simply simulate: non-player actors settle and react, while the player systems
  (which no-op without a player) drive a character only when one is bound. Queries
  (picking) work against the broad phase without stepping.
- **Alternatives considered**: step only with a live bound player (rejected — the
  world should simulate in play mode regardless); toggle each body's motion type
  (rejected — per-entity mode churn).
