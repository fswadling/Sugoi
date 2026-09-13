# Quickstart: Actor Skinning Hot Path

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK and SDL3 for the windowed app.
- The Akane actor asset (`assets/actors/Akane/Akane.iqm`, 166 joints), for the
  frame-time measurement.

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj
```

## Validation scenarios

1. **Golden pose (SC-004, US3)** — `dotnet test` includes the new `ActorTests` that
   sample a model with active channels and assert the expected `Local`/`World`/
   `Skinning` matrices. They must match the pre-change values.
2. **Debug cost (SC-002, US2)** — run the app in **Debug** with one Akane actor and
   compare the per-frame `record` phase against the baseline: one actor added
   ~3 ms, so the target is **≤ 2.2 ms** added per actor (≥25% lower,
   `RENDER_PERFORMANCE.md` §9). Add a second actor and confirm the improvement
   scales per actor.
3. **Release unchanged (SC-003, US2)** — run in Release with the same scene and
   confirm the per-actor `record` delta is unchanged (~0.1 ms) — no regression.
4. **Animation identical (SC-004, US3)** — in both configurations, the actor's idle
   animation looks exactly as before (no popping, no offset, no scale drift).

## Automated coverage

- `ActorTests` covers the sampled-pose math (FR-003) and the no-op guards
  (FR-006) headlessly.
- FR-001 (no allocation) and FR-002 (one fewer multiply) are structural — verified by
  inspection and by the measured Debug improvement.
- SC-002/SC-003 need the running app (a GPU).

## Expected outcome

- The sample path allocates nothing per frame and composes the local transform with
  one fewer multiply.
- The sampled pose and on-screen animation are unchanged.
- Debug per-actor `record` is materially lower; Release does not regress.
- `dotnet build` is clean; `dotnet test` passes.
