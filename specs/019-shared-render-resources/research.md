# Research: Shared Render Resources

Decisions for moving device-level render resources into shared app state. The
change touches `state/`, `screen/`, and `Engine.fs` only.

## Decision 1 — Shared state owns the whole GPU context, not just the pipelines

- **Decision**: store the existing `Gpu.Gpu` context (device, swapchain/depth
  formats, mesh + static pipelines, depth target) on `SharedState` as its single
  owner, rather than adding only the two pipeline fields.
- **Rationale**: the context already bundles exactly the device-derived resources
  the feature is about, and it already existed as the authoritative owner before
  `Engine` copied two of its fields into screen state. Moving the context removes
  the duplication at its source and keeps pipelines, formats, and the depth target
  together (they are recreated/consulted together).
- **Alternatives considered**: add only `MeshPipeline`/`StaticPipeline` to
  `SharedState` (leaves the depth target and formats still living in a local
  `Engine` value — two homes again); a new dedicated `RenderResources` type
  (duplicates `Gpu.Gpu` for no gain).

## Decision 2 — Per-screen render buffers stay on the screen

- **Decision**: `DebugDraw`, `GizmoDraw`, and `EnvironmentBatch` remain fields of
  `GameplayState`; the camera also stays.
- **Rationale**: they are tied to *this* screen's view — the grid/gizmos belong to
  the gameplay viewport and the batch is the gameplay screen's static-scene
  renderer, walking *that* zone. Moving them to shared state would confuse
  "belongs to the screen's view" with "belongs to the app."
- **Alternatives considered**: move all GPU memory to shared state (a stricter
  reading of "hardware goes to shared", but it would put the gameplay grid and the
  zone-specific batch in cross-screen state — rejected).

## Decision 3 — Screens read device resources via shared state, not constructor copies

- **Decision**: delete the pipeline fields from `GameplayState`, and pass the
  shared state into `GameplayScreen.renderScene` so it reads
  `shared.Gpu.MeshPipeline` / `shared.Gpu.StaticPipeline`.
- **Rationale**: FR-003 requires screens to read them from the single home; the
  screen already receives its own state, and the manager already holds the shared
  state, so threading it through is a small signature change rather than a new
  mechanism. It keeps the data flow explicit (no ambient global).
- **Alternatives considered**: keep passing the pipelines as parameters
  (contradicts "no hardware handles passed into the screen"); a global/ambient GPU
  accessor (hidden dependency, against the project's explicit-argument style).

## Decision 4 — The shared state owns lifetime; the run loop reads through it

- **Decision**: `Engine.run` creates the `Gpu` context after the device, hands it
  to `SharedState` at construction, and thereafter reaches it through the shared
  state (via the manager) for depth-target recreation, depth-target info, and
  release at shutdown.
- **Rationale**: one owner means the depth target recreated on resize must be the
  same object the render pass reads. Holding a second copy in a local would go
  stale after the first resize — exactly the duplication this feature removes.
- **Alternatives considered**: keep the local `Gpu` in `Engine` and also store a
  copy in shared state (stale-copy hazard — rejected).

## Decision 5 — The shared GPU context is mutable

- **Decision**: the `Gpu` field on shared state is mutable, so `ensureDepthTexture`
  can recreate the depth target on resize in place.
- **Rationale**: resize must mutate the one shared context; `SharedState` is a
  reference type, so a mutable field is reachable from the run loop without
  copying. The render pass then reads the recreated target.
- **Alternatives considered**: immutable field recreated wholesale (the depth
  target is the only mutable part; replacing the whole context each resize is
  needless churn).

## Decision 6 — `GameplayState.create` takes shared state (for the device)

- **Decision**: `GameplayState.create` loses the pipeline parameters and takes the
  shared state (or its device), so the batch and other per-screen buffers can
  still be created from the device.
- **Rationale**: per-screen buffers still need the device (FR-006); the shared
  state now holds it, so screen construction reads it from there instead of being
  handed a device and two pipelines.
- **Alternatives considered**: pass the device alone (works, but the shared state
  is available at the one call site and carries the device; taking shared state
  keeps the dependency honest).

## Decision 7 — No change to `world/`, `render/`, physics, or commands

- **Decision**: the batch, debug draws, pipelines' contents, the zone/entity
  model, and the undo model are untouched; only ownership and the render entry
  point change.
- **Rationale**: FR-009 and the spec's scope; a "pure move" that stays pure is
  verifiable by the unchanged draw counts.
- **Alternatives considered**: none — this is the boundary the spec drew.
