# Research: Zone-Owned Static Batch

Decisions for moving the static-scene batch onto the zone. The change touches
`world/`, `render/InstanceBuffer.fs`, `state/GameplayState.fs`, `screen/`, and
`Engine.fs`.

## Decision 1 — The zone owns the batch

- **Decision**: `Zone` gains a `Batch` field; `GameplayState` loses it.
- **Rationale**: the batch's entire content (submesh registry, per-group counts,
  instance buffer) is a function of one zone's placed props and environment parts.
  With multiple zones a screen-level batch is one buffer shared by all of them —
  it accumulates identities across zone switches and cannot draw two zones at once
  (spec US2). A zone already references GPU-backed assets through `Prop.Renderer`,
  and `world/` is documented as owning "entities, zones, assets, and their domain
  renderers", so a zone owning its renderer fits.
- **Alternatives considered**: keep it on the screen and reset it on zone switch
  (works for switch-only but re-accumulates, and blocks multi-zone rendering);
  a global/shared batch (worse — shared across screens and zones).

## Decision 2 — Break the compile cycle by removing `Zone` from the batch

- **Decision**: move `world/EnvironmentBatch.fs` to immediately before
  `world/Zone.fs` (after `world/Entity.fs`), and change `gather` to take the
  zone's prop map and optional parts map instead of the `Zone` type. `Zone` exposes
  a thin `gatherStatic` helper that passes its own maps.
- **Rationale**: a zone that owns the batch must see its type; a batch that takes
  `Zone` must see the zone — a cycle F# compile order cannot express. Splitting the
  *type* (before `Zone`) from the *zone-walking loop* (reads the maps) breaks it
  without a new module. `EnvironmentBatch` still holds `StaticSubmesh` (a
  `world/EnvironmentAsset` type), so it cannot move to `render/`.
- **Alternatives considered**: a separate `world/EnvironmentGather.fs` after
  `Zone.fs` (works, but adds a module for one function); keeping `gather` in
  `Zone.fs` (requires exposing the batch's private registry internals).

## Decision 3 — Lazy GPU buffers, so zone creation stays device-free

- **Decision**: `EnvironmentBatch.create` and `InstanceBuffer.create` take no
  device and allocate nothing; `ensure`/`upload`/`reclaim`/`release` take the
  device, and the buffer is allocated on first `ensure` (first render).
- **Rationale**: `Zone.create ()` is called by headless tests (`ZoneTests`,
  `PlayerTests`, `SpawningTests`) with no GPU device; allocating buffers at zone
  creation would call SDL with a null device and break them (spec FR-004, SC-004).
  The render path already has the device via shared state, so passing it where the
  buffer is actually used is natural.
- **Alternatives considered**: create the batch with the device at zone creation
  (breaks headless tests); inject the batch from the screen (defeats the ownership
  goal); make the batch an `option` created lazily in `renderScene` (adds an
  optional-state branch to every call site).

## Decision 4 — Per-frame flow unchanged; the device is threaded at the call site

- **Decision**: `gather device batch props parts` (CPU, sizes/allocates the buffer
  and fills it) → `upload device batch cmd` (copy pass) → `render batch …` (one
  instanced draw per group). `renderScene` gets the device from `shared.Gpu.Device`.
- **Rationale**: keeps the existing two-pass gather and draw sequence and the
  `draws`/`instances` counts; only the owner and the device's path change.
- **Alternatives considered**: store the device on the zone (reintroduces a device
  dependency on the zone — the thing 019 removed from the screen).

## Decision 5 — Release the batch's buffers; wire the live zone at shutdown

- **Decision**: add `InstanceBuffer.release`/`EnvironmentBatch.release`; release
  the live zone's batch during engine shutdown (after `WaitForGPUIdle`, before
  `Gpu.destroy`). Per-zone release on unload is wired when zone unload exists (the
  zone map).
- **Rationale**: with the batch per-zone, freeing its GPU memory belongs with the
  zone's lifetime (spec US2/FR-005). Zone unload does not exist yet, so the one
  caller today is shutdown; giving `release` that caller avoids dead code while
  making the per-zone path trivial later.
- **Alternatives considered**: no release (leaks per zone once zones unload); add
  release with no caller now (dead code, against Minimalism).

## Decision 6 — The screen state no longer needs shared state to construct

- **Decision**: `GameplayState.create` reverts to taking only the two debug draws
  (it took shared state solely to create the batch); `Zone.create ()` stays
  argument-free.
- **Rationale**: the batch (and its device) moved to the zone; nothing else on the
  screen is created from the device, so the extra parameter is no longer needed.
- **Alternatives considered**: keep `shared` for future use (just-in-case parameter,
  against Minimalism).

## Decision 7 — View buffers and camera stay on the screen

- **Decision**: `DebugDraw`, `GizmoDraw`, and `Camera` remain `GameplayState`
  fields.
- **Rationale**: they are the view's overlays and the screen's view state, not
  derived from the zone's content. The category distinction is "zone content →
  zone-scoped renderer; view/editor overlays → screen-scoped".
- **Alternatives considered**: move all per-screen GPU buffers to the zone (wrong —
  the grid and gizmos are not zone content).

## Decision 8 — No change to the entity model, physics, or commands

- **Decision**: `Zone`'s existing fields and semantics (refs, physics attachment,
  transforms, undo) are untouched; only a renderer field is added.
- **Rationale**: spec FR-008; the move must be behaviour-preserving.
- **Alternatives considered**: none — this is the boundary the spec drew.
