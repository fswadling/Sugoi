# Quickstart: Actor Draw Allocation

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK and SDL3 for the windowed app.
- A scene with at least one animated actor.

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj
```

## Validation scenarios

1. **No allocation on the draw path (SC-001, US1)** — inspect
   `world/ActorRender.fs`: `draw` passes a preallocated `BoneStorageBuffers` array
   to the storage-buffer bind; no array/list literal remains on the per-frame path.
   Spot-check the rest of the path (`Actor.samplePose`, `ActorRender.upload`,
   `Mesh.draw`) for any other per-frame allocation — there should be none.
2. **Actor renders identically (SC-002, US2)** — run the app in play mode with an
   animated actor: the character deforms and animates exactly as before (skinning,
   transform, materials unchanged).
3. **Several actors (US1/AC2)** — with more than one actor, each still renders its
   own deformation (no cross-actor buffer mix-up).
4. **Clean suite (SC-003)** — `dotnet build` clean and `dotnet test` green.

## Automated coverage

- `dotnet build` + `dotnet test` cover compilation and the existing 103 headless
  tests; the GPU bind itself can't be unit-tested headlessly.
- SC-001 and SC-002 are confirmed by inspection and the runtime look above.

## Expected outcome

- The actor draw path allocates nothing per frame.
- Actors render identically, including with multiple actors.
- `dotnet build` is clean; `dotnet test` passes.
