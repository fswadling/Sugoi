# Quickstart: Zone-Owned Static Batch

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK and SDL3 for the windowed app.
- The ArtRoom environment, and ideally a second prop asset.

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj
```

## Validation scenarios

1. **Batch is zone state (SC-001, US1)** — inspect `state/GameplayState.fs`: no
   batch field. Inspect `world/Zone.fs`: it owns the batch.
2. **GPU-free zone creation (SC-004, FR-004)** — the headless suite still passes:
   `ZoneTests`, `PlayerTests`, and `SpawningTests` all call `Zone.create ()` with
   no device, and `dotnet test` is green.
3. **Identical rendering (SC-003, US3)** — load the ArtRoom and play: the frame log
   reports the same `draws 191 instances 521` as before, and the scene looks
   identical. Add a prop: `draws` rises only by the prop asset's distinct
   submeshes.
4. **One batch per zone (SC-002, US2)** — create two zones (headlessly, no device)
   and confirm each has its own batch object (a distinct reference). Cross-zone
   independence is structural: every zone owns its own batch. Multi-zone *rendering*
   is the zone phase, so there is nothing to run for this check.
5. **Release (SC-005, FR-005)** — start and quit the app: it shuts down cleanly and
   the live zone's batch buffers are released before the device is destroyed.
6. **No per-frame regression (FR-007)** — the per-phase log shows no added
   per-frame work; allocation does not occur every frame.
7. **No model change (FR-008)** — entity refs, physics attachment, transforms, and
   undo behave as before (the existing tests plus a quick edit pass).

## Expected outcome

- The zone owns the batch that renders its static scene; the screen state has none.
- Zone creation stays device-free; the headless suite passes.
- Rendering, draw counts, and appearance are unchanged.
- The batch's GPU buffers are released before the device is destroyed.
- `dotnet build` is clean; `dotnet test` passes.
