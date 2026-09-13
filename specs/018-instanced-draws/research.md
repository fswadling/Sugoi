# Research: Instanced Static Draws

Decisions for batching the static scene. Evidence and measurements are in
`docs/RENDER_PERFORMANCE.md` §4–§7; prior art (Nu's instancing-by-default) is in
§12.

## Decision 1 — One instanced draw per unique submesh identity

- **Decision**: draw each distinct static submesh with a single instanced call
  covering all of its placed occurrences, instead of one call per occurrence.
- **Rationale**: the frame is CPU-bound on per-draw submission, not GPU
  throughput (§4–§6), and eliminating ~860 binds/frame had no effect while the
  draw count stayed at 521 (§5b). Reducing call count is the only lever that fits
  the evidence.
- **Alternatives considered**: geometry merging by material (fewer draws, ~51, but
  static-only, requires re-baking buffers, loses per-part culling — deferred);
  culling (reduces instances, not calls; tried and reverted); multithreaded
  recording and indirect draws (don't reduce per-draw CPU cost).

## Decision 2 — Key on per-asset submesh identity, never a bare index

- **Decision**: the grouping key is the shared submesh within its owning asset
  (`(asset, submeshIndex)`, realised as the uploaded submesh's identity), not a
  global submesh index.
- **Rationale**: `EnvironmentRender.SubmeshIndex` is relative to
  `r.Asset.Submeshes`, and `r.Asset` is the prop asset for props but the
  environment asset for parts (`world/Assets.fs` `createPropRender` /
  `createPartRender`). Two assets can share index 0 while naming different
  geometry and materials; a bare index would merge them and draw one with the
  wrong buffers.
- **Alternatives considered**: bare index (incorrect, the identified hazard);
  `(assetId, index)` string/tuple keys (correct but allocates/hashes per lookup —
  the uploaded submesh's identity is a cheaper, allocation-free key).

## Decision 3 — Instance *vertex* buffer, not a storage buffer

- **Decision**: feed per-instance data through a second vertex-buffer slot with
  `InputRate = Instance` and `InstanceStepRate = 0`.
- **Rationale**: SDL_gpu supports instance input rate and a byte offset on
  `GPUBufferBinding`, so per-group ranges bind directly. It avoids the
  storage-buffer binding-slot arithmetic and `NumStorageBuffers` plumbing
  (`RENDERING_NOTES.md` §4) with the same shader data.
- **Alternatives considered**: a vertex storage buffer indexed by
  `gl_InstanceIndex` (Nu's mechanism; works, but more moving parts in SDL_gpu and
  no benefit here).

## Decision 4 — Instance layout: model + normal rows, 112 B

- **Decision**: one instance is seven `Float4` attributes (locations 3..9,
  stride 112): four model rows then three normal rows, written in the same
  component order as today's pushed matrices.
- **Rationale**: preserving the existing row order keeps the no-transpose
  convention (`RENDERING_NOTES.md` §7) and reproduces the current transform
  exactly. The normal matrix is stored rather than derived in-shader to keep
  today's non-uniform-scale behaviour.
- **Alternatives considered**: reconstructing row 3 as `(0,0,0,1)` for affine
  transforms (saves 16 B and one attribute; not worth the special case now);
  deriving the normal matrix with an in-shader `inverse()` (changes behaviour for
  non-uniform scale).

## Decision 5 — Push `viewProj` once for the whole static block

- **Decision**: the static vertex uniform shrinks to `{ mat4 viewProj; }` and is
  pushed once before the batch loop, not per submesh.
- **Rationale**: pushes are command-buffer-scoped (`RENDERING_NOTES.md` §5) and
  `viewProj` is identical for every static draw, so per-draw pushes buy nothing.
  This removes the last 192-byte-per-draw uniform traffic.
- **Alternatives considered**: keeping a per-group push (harmless but pointless).

## Decision 6 — Dense submesh registry keyed by the uploaded buffer

- **Decision**: `EnvironmentBatch` keeps a grow-only registry mapping each
  distinct static submesh's uploaded identity to a dense slot, holding the
  `StaticMesh` + `Material` needed to draw it; per-group counts/offsets are flat
  arrays indexed by that slot.
- **Rationale**: a dense id makes the gather and prefix-sum contiguous and
  allocation-free, and the uploaded submesh is already the natural identity
  (Decision 2). Submeshes are created once at asset load, so the registry grows
  only when a new asset appears.
- **Alternatives considered**: a `Dictionary` keyed by `(asset, index)` per frame
  (hashes per instance, and record/array hashing is expensive); a global
  submesh-index array (incorrect per Decision 2).

## Decision 7 — Two-pass, allocation-free gather, recomputed each frame

- **Decision**: per frame, count instances per group, prefix-sum into start
  offsets, then fill the model/normal rows into a reused staging buffer and
  upload once; recompute every frame.
- **Rationale**: props/parts can be spawned or deleted while editing, so the
  counts must reflect the current zone. The work is ~521 increments and ~521×28
  float writes — negligible next to the cost being removed — and reuses buffers
  (constitution V, FR-010).
- **Alternatives considered**: caching counts at environment load (Nu's freeze-time
  pre-batch; deferred until profiling shows the gather matters, per §7).

## Decision 8 — Instrument draw calls and instances

- **Decision**: report the frame's draw-call count (and instance count) in the
  existing per-frame log, via counters on `EnvironmentBatch`.
- **Rationale**: the feature's value is a draw-call reduction; it must be measured
  in a real run (SC-002, SC-005), and it guards against silent regressions. Nu
  does the same (`Hl.ReportDrawCall`, §12).
- **Alternatives considered**: inferring the count from the asset (doesn't prove
  what the frame actually issued).

## Decision 9 — Actors and debug draws are untouched

- **Decision**: only the static pipeline gains instancing; `ActorRender` (skinned
  meshes) and `DebugDraw`/gizmos keep their existing path and draw counts.
- **Rationale**: actors are few and move per frame (not batchable this way), and
  the debug layers are small. Scope stays on the static scene that dominates the
  cost.
- **Alternatives considered**: batching actors too (out of scope; few draws).

## Decision 10 — Culling and freeze-time caching stay deferred

- **Decision**: no frustum culling and no load-time count cache in this feature.
- **Rationale**: culling was tried and reverted and never showed a benefit
  (§5a); it reduces instances, not call count. The per-frame gather is cheap
  enough that the freeze-time cache is premature (§7 step 3).
- **Alternatives considered**: reintroducing per-instance culling inside the batch
  (Nu does this; revisit only if profiling says so).

## Decision 11 — Size the instance buffer on first gather; retire old buffers safely

- **Decision**: size the instance buffer on the first `gather` to the scene's
  gathered instance count (the batch has no scene at construction), keep it
  grow-only from then on, and on any growth retain the old GPU buffer until it is
  no longer referenced by an in-flight frame before releasing it (a small retired
  list).
- **Rationale**: SDL_GPU keeps `MAX_FRAMES_IN_FLIGHT = 3` frames in flight, so a
  buffer bound by a submitted frame may still be in use when the next frame's
  gather decides to grow. Releasing it immediately is a use-after-free / debug
  validation error. Sizing on the first gather matches the loaded scene exactly,
  so growth happens only when the scene itself grows (a spawned or larger asset);
  the retired list covers that rare case.
- **Alternatives considered**: releasing in place (unsafe with frames in flight);
  a fixed maximum capacity with no growth (wastes memory and still needs a
  fallback); a per-frame re-allocating buffer (defeats FR-010).
