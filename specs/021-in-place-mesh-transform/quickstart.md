# Quickstart: In-Place Mesh Transform

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK and SDL3 for the windowed app.
- A scene with at least one placed prop that has a mesh collider (a prop asset, not
  a collider-less part).

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj
```

## Validation scenarios

1. **Smooth drag (SC-001, US1)** — start the app, place a prop, and drag it. Watch
   the per-frame timing log: the `update` phase must stay under 5 ms for the whole
   drag (the pre-fix symptom was ~200–280 ms per affected frame).
2. **Collider follows a move (SC-002, US1)** — after dragging the prop, click it at
   its new position: it selects. The collider moved with the mesh.
3. **Scale still rebuilds (SC-003, US2)** — scale the prop (uniform and then
   non-uniform). Its collider must match the new size: picking hugs the scaled
   geometry, and the `update` cost of a scale change is a one-off (a rebuild is
   expected here, unlike a move).
4. **Nudge is as cheap as a move (FR-008, US1)** — nudge the prop repeatedly with the
   keyboard; each nudge must not stall `update`.
5. **Identity preserved (SC-005, US3)** — move a prop, select it, undo the move: the
   same entity is restored to its prior transform with its collider.
6. **No-collider entity (FR-005, US3)** — a part with no derivable collider still
   moves when transformed (no error).

## Automated coverage

- `dotnet test` includes the `ZoneTests` body-identity tests: a spawned prop's body
  id is unchanged across a move/rotate and changes across a scale change, with
  `BodyToEntity` consistent in both cases. These run headlessly (no GPU).
- Scenarios 1, 3, and 4 need the running editor (a GPU); their evidence is the
  timing log and visual collider fidelity.

## Expected outcome

- Dragging or nudging a prop causes no per-frame stall.
- Moving/rotating a prop updates its collider in place; scaling still rebuilds it.
- The prop keeps its identity and its collider keeps matching the geometry.
- `dotnet build` is clean; `dotnet test` passes.
