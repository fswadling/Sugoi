# Quickstart: Walk Around

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- `JoltPhysicsSharp` (with `JoltPhysics.Native`) restored; the Jolt native library
  copied next to the apphost by the build.
- An actor asset with `Idle` and `Jog` animation clips, and an environment with
  collidable geometry.

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj   # PlayerTests + PhysicsTests
```

## Validation scenarios

1. **Bind a player** — select an actor and invoke *Set as Player* (Inspector); the
   actor becomes the player. Selecting a prop or environment part shows no such
   action (the binding helper rejects a non-actor if called directly).
2. **No player drives nothing** — with no player bound, press the play key: no
   character is driven and the camera is unchanged, but the world still simulates
   (non-player actors settle). Exit play normally.
3. **Walk (SC-006)** — bind the actor, enter play, hold `W`: the character moves
   forward along its facing, responding in the same frame. `S` reverses; `A`/`D`
   turn in place without translating.
4. **Collide** — walk into a wall or prop: the character stops against it and
   cannot pass through. Turning while touching a collider does not push through it.
5. **Gravity** — raise the player in the editor, enter play: it falls and comes to
   rest on the ground, staying upright. Walk off a ledge: it falls. (Raised
   non-player actors settle too — all actors are dynamic.)
6. **Camera follow** — throughout 3–5 the camera stays behind and above the player
   and rotates with its facing, with no manual camera input.
7. **Leave play (SC-004)** — exit play; the editor shows the character at exactly
   the walked-to position and facing (no reset). Re-enter and confirm it resumes
   there.
8. **Stale binding** — delete the player actor and confirm play is idle rather
   than driving a reused slot: the handle simply stops resolving, with no clear
   step.
9. **Animation** — idle when no movement key is held; jog while a movement key is
   held; releasing returns to idle. A player pressed against a wall while holding
   `W` still reads as moving.
10. **Frame rate** — the existing on-screen frame-rate overlay stays at the target
    60 fps while walking.

## Expected outcome

- An actor can be deliberately bound as the player, and only then does play mode
  drive it.
- Tank movement is physical: the dynamic capsule is stopped by the environment
  while the player stays upright and rests on the ground.
- The camera follows as a separate system; with no player bound nothing is driven
  (though the world still simulates).
- Play/edit transitions preserve the player's position; the editor passively
  observes it and does not simulate.
- `dotnet build` is clean; `dotnet test` passes (existing suite + `PlayerTests`).
