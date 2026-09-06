# Quickstart: Actor Capsule Collider

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- The game/editor runs via `dotnet run` (see the repository `README.md`).

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj   # EntityTests + PhysicsTests (capsuleCollider/capsuleCenter)
```

## Validation scenarios

1. **Toggle on** — spawn an actor (e.g. Akane), select it, and check "Show
   Collider" in the Inspector; a teal capsule wireframe appears over the actor.
2. **Toggle off** — uncheck "Show Collider"; the wireframe disappears.
3. **Actor-only** — select a prop; the Inspector shows no "Show Collider" checkbox.
4. **Alignment** — with the overlay on, the capsule's base sits at the actor's
   feet and its top reaches the actor's head, spanning the full height.
5. **Consistent radius** — spawn a second actor asset; its capsule has the same
   width even though the actors differ in arm span.
6. **No behavior change** — toggling the overlay leaves the actor's position
   unchanged (drag/gizmo behavior is unaffected).

## Expected outcome

- `dotnet build` is clean; `dotnet test` passes (`EntityTests`, `PhysicsTests`).
- The capsule wireframe matches the physics body (both use `Zone.capsuleCenter`),
  spans the actor's full height, and uses a fixed radius.
