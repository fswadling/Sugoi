# Quickstart: Physics + Picking

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK.
- `JoltPhysicsSharp` (with `JoltPhysics.Native`) restored; the Jolt native library
  copied next to the apphost by the build.

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj   # PhysicsTests (collider derivation + raycast)
```

## Validation scenarios

1. **Colliders exist** — import an environment (`artroom`) and spawn a `beech`
   prop; each part/prop owns a collider (visible via the debug collider overlay or
   asserted in tests).
2. **Accurate picking** — rotate a prop and click its visible surface; it selects
   even where the old AABB approximation would have missed. Click empty space
   beside it; nothing selects.
3. **Actor capsule** — spawn Akane; it owns a capsule collider sized to its bounds.
4. **Transform sync** — move/rotate/scale a prop in the editor, then click its
   moved shape; it still selects. Undo the transform; the collider returns with it.
5. **Delete** — delete a prop; no orphan collider remains (the entity↔body map is
   empty for that entity).

## Expected outcome

- Props/environment parts have mesh colliders; the actor has a capsule collider.
- Click-select raycasts against colliders and matches the drawn shape.
- Moving/undoing an entity keeps its collider in sync.
- `dotnet build` is clean; `dotnet test` passes (existing suite + `PhysicsTests`).
