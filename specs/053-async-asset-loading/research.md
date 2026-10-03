# Phase 0 Research: Async asset loading

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

## Context

An environment load runs synchronously on the render thread today: `Gltf.load`, then per submesh
`StaticMesh.create` + texture decode + `Gpu.recordTexture`, all inside `Assets.buildLoaded`. On
ArtRoom that is ~5.1 s — **decode 4582.6 ms (89%)**, tex-create 234.5 ms, memcpy 186.2 ms,
stage 104.9 ms (7 flushes), submit 3.7 ms, parse 16.2 ms — and the whole time the frame loop is
dead. The dominant cost is CPU (decode), so the fix is to move the CPU half off the render thread
and hand decoded payloads back for a budgeted GPU build. This phase settles how.

Measurements reused (spec 052 baseline, `FFS_LOAD_TIMING=1`): ArtRoom ~5.14 s; Island 11.9 ms;
decode is the wall-clock floor. `SubmeshBytes` ~1.84 MB vs textures ~1.68 GB → ~1.3 GB decoded
pixels.

## Decision 1 — Workers do CPU only; every `SDL_gpu` call stays on the render thread

- **Decision**: a worker performs file read, `Gltf.load` and texture decode. It never touches a
  device: GPU buffers/textures, command buffers and the swapchain are created, recorded and
  submitted only on the render thread (in `Assets.pump`, called per frame).
- **Rationale**: SDL_gpu command buffers are **thread-affine** (acquire/record/submit on the same
  thread) and the window/swapchain is window-thread-only; submissions serialise on one internal
  lock, so parallel *upload* threads would gain nothing. This is FR-012 and a hard SDL rule
  (`SDL_gpu.h`, SDL Vulkan backend, verified previously).
- **SDL_image off-thread**: `Texture.decode` also calls `SDL3.Image.Load`, `SDL.ConvertSurface` and
  `SDL.DestroySurface` on a worker. SDL_image is effectively stateless per call and SDL3's error state
  is thread-local, so this is assumed safe; T016 checks it explicitly rather than leaving it implicit.
  (`preloadImageDep` is a `Lazy`, so two workers forcing it at once is thread-safe.)
- **Alternatives considered**: **a GPU worker thread separate from the render thread** — SDL
  requires the swapchain and command buffers on the render/window thread, so the GPU half cannot
  move; **parallel upload threads** — no gain (serialised submissions), more risk.

## Decision 2 — The decoded payload is the unit handed from a worker to the render thread

- **Decision**: a worker posts a `DecodedEnvironment = { AssetId; Model : Gltf.Model; Images }`,
  where `Model` is the parsed geometry/parts/bounds/materials (all flat `float32[]`/`int[]`) and
  `Images` maps each **distinct** texture path to a `DecodedImage = { Width; Height; Pixels : byte[] }`
  (RGBA8, rows compacted the way `Texture.load` packs them today). The worker also carries its own
  parse/decode nanosecond counts for instrumentation.
- **Rationale**: exactly the CPU half of today's `buildLoaded` — everything the render thread needs
  to create GPU resources and nothing it must not. `Gltf.Model` is already a device-free, flat
  structure, so it crosses threads unchanged; the decoded pixels are the one new intermediate.
- **Alternatives considered**: **handing back SDL surfaces** — surface ownership/format is fiddly
  across threads and the render thread only needs the compacted pixels; **handing back a built
  `EnvironmentLoaded`** — impossible, it holds GPU handles.

## Decision 3 — The render-side build is resumable across frames within a time budget

- **Decision**: `Assets.pump budgetMs` (render thread, once per frame) drains completed payloads
  into a `PendingUpload` cursor and, for a payload still wanted, continues building it — create the
  GPU mesh for the next submesh, resolve the next material (creating its textures from the payload's
  pixels) — recording into one `Gpu.UploadBatch`, until the budget is spent; the cursor persists to
  the next frame. When the asset is fully built, `finishUpload` runs, the `EnvironmentLoaded` is
  installed, bytes are accounted, the LRU is touched and the budget is enforced.
- **Rationale**: ArtRoom's GPU half is ~0.5 s (create + memcpy + stage); doing it in one frame would
  stall for ~half a second, defeating SC-001. FR-003 explicitly permits resuming across frames. The
  batch already flushes when full (spec 052), so peak staging stays bounded while a batch spans
  frames.
- **Ordering**: `pump` advances the cursors **sequentially in submission order** against one shared
  budget, and a cursor's `UploadBatch` is created **lazily when it becomes the active cursor**. So
  exactly one staging buffer exists at a time and peak staging stays at the spec-052 bound (not one
  per waiting cursor). With `loadWorkers = 2` at most one payload waits its turn holding only its
  decoded pixels, the same transient memory the two-decode pool already implies.
- **Alternatives considered**: **one-shot GPU build** — a ~0.5 s stall; **count-based slicing**
  (n resources per frame) — less predictable than a time budget and can still stall on a huge
  texture.

## Decision 4 — Texture decode and texture creation are split

- **Decision**: `Texture.decode : path -> DecodedImage` is pure CPU (image library preload,
  `IMG_Load`, format normalise, row compaction) and runs on a worker;
  `Texture.createFromDecoded : batch -> device -> DecodedImage -> Texture` creates the GPU texture
  and sampler and records the level-0 copy into the batch, and runs on the render thread inside the
  pump. `DecodedImage` is declared in `Texture.fs` (it is the decode output, and `Texture.fs` is
  compiled before `world/AssetLoad.fs`). The shared, refcounted texture cache (`Loader.Textures`) is
  touched **only** on the render thread.
- **Rationale**: the cache is not thread-safe and its refcounts must stay balanced (spec 050
  FR-018); keeping decode pure leaves the cache and creation single-threaded. The payload's pixels
  are used on a cache miss and discarded on a hit.
- **Alternatives considered**: **locking the cache for workers** — the cache entry's creation needs
  a device (render thread) anyway, so a lock buys nothing; **workers returning only paths** — then
  the render thread would decode, defeating the feature.

## Decision 5 — Pending loads are a bounded, de-duplicated, most-recent-first set

- **Decision**: a `LoadQueue` holds the outstanding asset ids in an `Lru` (head = most recently
  requested) under a lock, plus `Claimed`, `Pinned` and a single `Uploading` slot (the one id whose
  GPU work is in flight). `request id` is de-duplicated (a repeat moves the id to the head, no second
  entry). The active environment's id is pinned so it is never dropped.
- **Rationale**: FR-005/US3 exactly — de-dup, bound, most-recent-first, drop oldest, active pinned.
  Reusing `core/Lru.fs` avoids a new structure.
- **Bound eligibility (SC-004)**: a drop removes the least-recently-requested entry that is neither
  pinned nor *currently uploading*; a **decoding** entry *is* droppable — the drop does not cancel
  the decode (no worker is interrupted), it only removes the entry from the wanted set, so the
  completed payload is discarded. This is the only way a decoding load can become "no longer wanted"
  (clarification 1), and it keeps the bound literal: with `Capacity = 8`, the non-droppable entries
  are at most the pinned ids (≤ 2: incoming + outgoing) plus the one uploading id, so a droppable
  tail always exists and `Lru.count Pending ≤ Capacity` holds (SC-004).
- **Pinning — one source of truth, refreshed every frame**: the pin set lives on `AssetManager`
  (`PinnedIds`), seeded into the queue's `Pinned` mirror at creation and forwarded on change.
  `Assets.setPinned` **replaces** it, and `GameplayScreen.update` calls it every frame with the active
  zone's environment, so it can never accumulate ids of environments no longer in use. (An earlier
  design **unioned** a request's ids, which left stale ids permanently unevictable and defeated the
  byte budget.) `Assets.pump` reads the set for eviction protection (FR-006); there is no separate
  `protectedIds` argument, and a request does not touch the pin set at all.
- **Alternatives considered**: **a plain bounded `Channel`** — FIFO with no dedup and no recency
  reorder, so it cannot implement FR-005; **unbounded FIFO (odin-game)** — the reference has no
  MRU/drop because its loads are serialised and cheap, which is not this case; **an F#
  `MailboxProcessor`** — rejected, see D5a.

## Decision 5a — `MailboxProcessor` is not used for the queue or the handoff

- **Decision**: the pending set stays an `Lru` under a lock; the worker → render handoff is a
  `System.Threading.Channels` channel; the worker wake-up is a `SemaphoreSlim`. No
  `MailboxProcessor`.
- **Rationale**: a `MailboxProcessor` is an unbounded FIFO agent, which fights three requirements.
  (i) **Policy**: its mailbox has no capacity and no reordering (`Scan` can only *search*), so
  most-recent-first and drop-oldest cannot be expressed on it — the `Lru` state would live inside
  the agent anyway, leaving the mailbox an expensive wake-up signal. (ii) **Synchronous inspection**:
  `pump`, `isLoading` (the per-zone-per-frame glyph), pinning and the "still wanted?" check all run
  *on the render thread in-frame*; agent state is private and only reachable through
  `PostAndReply`/`Async.RunSynchronously`, which would block or deadlock the render thread — the very
  stall this feature removes. (iii) **Blocking CPU work**: the body is an `Async` workflow on the
  thread pool, so a multi-second `IMG_Load`/`Gltf.load`/file read would tie up a pool thread, and one
  consumer means a 2-worker pool needs two mailboxes and two queues, breaking the single global MRU
  order. It is also hidden state, so the bound/dedup/recency assertions FR-005 needs would become
  timeout-based and flaky. A full rationale and the counter-case (a pure FIFO, odin-game-shaped
  worker) are recorded here because the choice is non-obvious in an F# codebase.
- **Alternatives considered**: **one `MailboxProcessor` for the ready handoff** — no synchronous
  `TryReceive`, so the render thread's non-blocking drain becomes an async await; a `Channel` is
  strictly better. **A `MailboxProcessor` per worker** — the handoff style would be idiomatic, but
  it buys nothing over `Thread` + `SemaphoreSlim` and still needs the shared locked `Lru` for a
  global bound. `MailboxProcessor` *would* be a good fit for a plain unbounded FIFO (odin-game's
  model), which is exactly what FR-005 rules out.

## Decision 6 — The concurrent-decode limit is a memory bound: a small pool

- **Decision**: `loadWorkers = 2` (a named constant, overridable by `FFS_LOAD_WORKERS`): that many
  worker threads decode at once; the queue capacity is separate (`LoadQueueCapacity = 8`). A
  `SemaphoreSlim(workers)` also bounds the payloads that are decoded-but-not-uploaded, so a fast
  decode cannot run ahead of the budgeted upload and pile up decoded pixels. The pool is created
  lazily on the first request that actually needs decoding and stopped in `Assets.release`.
- **Rationale**: FR-009 makes this a *memory* bound — a decoded ArtRoom is ~1.3 GB, so the number of
  simultaneous decodes caps transient RAM, independent of the GPU. Two allows a large and a small
  environment to overlap while keeping peak decoded memory ~2× a heavy environment. The queue
  capacity is a *count* bound on outstanding requests, unrelated to decode concurrency.
- **Alternatives considered**: **one worker (odin-game)** — safe but serialises a burst of small
  environments; **many workers** — transient decoded memory blows up (N × asset size) for no GPU
  benefit.

## Decision 7 — Residency gains *loading* and *failed* states, as a sum type

- **Decision**: `EnvironmentAsset.Loaded : EnvironmentLoaded option` is **replaced** by a `Residency`
  DU (`NotLoaded | Loading | Resident of EnvironmentLoaded | Failed of string`). `Resident` is the
  single source of truth for "resident" (the accessors match on it); `Loading` is coarse and written
  only by the render thread; the fine phases (`Queued | Decoding | Uploading`) are a **derived**
  readout view. `hasFailed` reads `Residency.Failed`; there is no side map for failures.
- **Rationale**: FR-007 needs a loading state and FR-008 a failed one, both on the existing
  readout. A DU makes not-loaded/loading/resident/failed mutually exclusive (the `Loaded` + side-map
  encoding could represent an absent unit while "loading"); deriving the fine phases keeps the
  cross-thread writes out of a single-writer value. See D12 for why a sum type is the right level of
  typing here and full type-state is not.
- **Alternatives considered**: **keeping `Loaded : option` plus a `Failed` map** — states are not
  exclusive and the readout stitches three sources; **expanding `EnvironmentLoaded` into a DU with
  partial states** — the loaded unit is built atomically, so only the coarse lifecycle needs typing.

## Decision 8 — A zone switch completes in-frame; environment parts activate on landing

- **Decision**: `requestEnvironment` returns immediately, so `swapZone` completes in the requesting
  frame whether or not the incoming asset is resident (FR-004). The zone's environment value is set
  immediately; its **part resources** (Jolt bodies + renderers) are attached when the asset is
  resident, else the zone's environment is set to `ZoneEnvironment.Awaiting` (D12) and a per-frame
  `reconcileEnvironment` attaches them once the asset lands. `gatherStatic` draws parts only for
  `Active`, so the draw path tolerates the gap (pop-in). For an environment authored from an asset
  (`loadEnvironment`), the environment value starts with empty parts and is populated from the
  parsed parts when the asset lands; a template/undo environment already carries its parts.
- **Rationale**: the parse is now off-thread, so `assetPartStates` (and the colliders that read the
  parsed geometry via `Assets.submeshes`) cannot run at switch time. This is the spec-053
  replacement of spec 050's load-before-commit / fail-the-switch semantics: the switch no longer
  waits, so a load failure is surfaced at the zone (FR-008) rather than cancelling the switch.
  In the headless suite the fixture assets are already resident, so `setEnvironment` takes the
  immediate branch and the 412 tests are unaffected.
- **Materialising parts**: because the parse is off-thread, an asset-authored environment starts
  with an empty parts map and its parts are materialised on landing. The `Environment` value records
  whether its parts are the asset's or already authored (`PartsSource = FromAsset | Authored`, on
  `Environment` so the marker survives the `SetEnvironment`/`ZoneContents` payload), and the reconcile
  materialises **only** for `FromAsset && Parts.Count = 0`. So a registered template that
  deliberately has a subset (spec 034) is never filled from the asset, and a redo of an
  already-materialised value is not filled twice (VR-010).
- **Alternatives considered**: **block the switch until the parse lands** — reintroduces the stall
  and violates FR-001/FR-004; **keep spec 050's load-before-commit** — contradicts FR-004.

## Decision 9 — Failure is logged and surfaced; nothing partial becomes resident

- **Decision**: a worker catches any throw from parse/decode and posts `LoadFailed message` instead
  of crashing the worker. The pump logs it, sets `Residency = Failed` (never left `Loading`) and
  leaves no loaded unit. A mid-build render-side failure aborts the batch and rolls
  back the meshes/textures already made (the spec-050/052 rollback), then records `Failed`. A later
  request re-queues the asset.
- **Rationale**: FR-008; a worker must not die on a bad asset, and the action has already happened
  so the failure is surfaced rather than cancelling it.
- **Alternatives considered**: rethrowing on the render thread — would crash or need a guard anyway;
  a modal/UI — clarification 3 says the existing indicator, no new UI.

## Decision 10 — Shutdown stops the workers before release

- **Decision**: workers are **background** threads (`IsBackground = true`). `Assets.release` sets the
  pool stopping, wakes the workers, and joins them with a short **bounded** timeout — then runs the
  existing release of environments, textures, actors and the placeholder. A worker still inside a
  multi-second `IMG_Load` at that point is **abandoned** (it finishes into a payload nobody reads);
  because a worker never touches the device, abandonment is safe and shutdown stays prompt (an
  uninterruptible decode would otherwise block it for seconds). The pool is created only when
  a decode is actually needed, so a headless scan starts no threads.
- **Rationale**: FR-011 — no crash, no leak; an in-flight payload is managed memory (no GPU
  resource), so dropping it is safe. Stopping before release avoids a worker handing a payload to a
  manager whose device is gone; a bounded join keeps close-to-quit responsive.
- **Alternatives considered**: **a full join** — blocks shutdown for a whole decode; **a foreground
  daemon thread** — keeps the process alive and can touch freed SDL state; **no acknowledgement at
  all** — the workers still only release managed memory, but a bounded join is cheap and makes the
  intent explicit.

## Decision 11 — Scope boundaries

- **Decision**: only **environment** loads become async. Props and actors keep their eager,
  synchronous load (spec 050's boundary): props still load at `scan` through the same CPU+GPU build
  run inline. The CPU/GPU split, the budget, the queue and the pool apply to environments only.
- **Rationale**: spec clarification 2 and Assumptions; actors are loaded once at startup and props
  are unbounded but few, so moving them is a separate concern. The shared build functions take a
  payload either way, so props reuse the render-side build with an inline payload.
- **Alternatives considered**: moving props/actors too — larger surface, no measured need.

## Decision 12 — Sum types where a single writer owns the state; derived views where threads share it

- **Decision**: encode the two **single-writer, local** state machines as F# DUs — the asset's
  `Residency` (D7) and the zone's `ZoneEnvironment`
  (`NoEnvironment | Active of Environment | Awaiting of Environment`, replacing the single
    `Environment : Environment option` field). Keep the **cross-thread, derived** phases
  (`Queued | Decoding | Uploading`) as a computed view, not stored state. Do **not** use phantom-type
  type-state.
- **Rationale**: a DU makes an illegal *state* unrepresentable and unreadable (you cannot read the
  loaded unit while loading; resident and failed cannot coexist), and it *adds* states the option
  could not express at all — the zone's `Awaiting`. For `ZoneEnvironment`, drawability falls out of
  the type: only `Active` has attached
  parts, so `gatherStatic` cannot gather a not-yet-resident environment. Phantom-type type-state is
  rejected because it assumes *one owner threading a value through calls*, which does not hold: the
  queue phases are written by workers under the queue's lock and are partly derived, so there is no
  single linear value to thread; F# has no linear/affine types, so the witness can be dropped or
  duplicated; and it cannot express the invariants that actually bite (claim release by the drain,
  one payload per id, supersede-during-decode, pin updates). Those are enforced by **confinement**
  (`LoadQueue` opaque, mutated only through its operations, all under one lock) plus `assert`s and the
  headless tests.
- **Churn**: `Residency` is a mechanical change to the `EnvironmentAsset` accessors and the
  `TestSupport` fixtures. For the zone, the field is renamed `EnvironmentState` and a read-only
  `member Environment : Environment option` absorbs the ~8 read files and the suite's reads (F#
  forbids a member sharing a record field's name — FS0023 — which is why the rename is required); the
  few write sites move to `EnvironmentState`, and `Environment` gains the `PartsSource` field, so its
  field-wise constructions (in `Zone.fs`, `EntityTests`, `GameplayStateTests`) are updated. FR-010 is
  about *accounting and eviction behaviour*, which is unchanged, so the field-type changes do not
  violate it.
- **Alternatives considered**: **flags/options** — the status quo, with representable illegal states;
  **phantom-type type-state** — rejected above; **storing the fine phases** — they are written by
  workers, so a single-writer DU cannot hold them without a shared mutable cell and a lock, which is
  what the queue already is.

## Decision 13 — The decode path does not pool its buffers

- **Decision**: `DecodedImage.Pixels` is a plain managed `byte array`, reclaimed by the GC; no
  `ArrayPool`/`MemoryPool` is used for the decoded pixels or the parsed geometry. The payload's
  lifetime stays the render thread's: allocated by the worker, dropped after `Texture.createFromDecoded`
  (or when the payload is discarded), and never reused.
- **Rationale**: loads are a **cold path** (a zone switch, not per frame), and constitution V says to
  prefer clarity there and measure before optimising. The one large allocation is the decoded pixels
  (ArtRoom ~1.3 GB, all on the **LOH**). A pool has the **wrong lifetime** for them: the payload must
  survive from the worker's decode until the render thread uploads it — possibly across frames, and
  indefinitely for a waiting payload — so pooled buffers would be **retained** up to the pool's
  high-water mark, which can worsen SC-006's steady state rather than improve it. Power-of-two
  buckets also round a 3 MB image up to 4 MB while held, and a use-after-return would add a second
  lifetime discipline to a feature whose main risk is already cross-thread lifetime. `JsonDocument`
  already rents from `ArrayPool` internally, and the geometry arrays are small (Gen0), so there is
  nothing else worth pooling.
- **Revisit trigger**: if measurement shows LOH/Gen2 pauses stalling the frame loop during a load —
  the concentration of large allocations on a worker can trigger a stop-the-world collection, which
  would substitute a GC hitch for the decode hitch. The preferred fix then is **not** a pool but the
  **native-surface handoff**: keep the converted `SDL_Surface` (native memory) and hand it across the
  thread, compacting rows straight into the staging transfer buffer on the render thread — removing
  the 1.3 GB of LOH allocation *and* one full copy, with one owner (`SDL_DestroySurface` after
  upload). `GC.AllocateUninitializedArray` (skips the zero-fill) and Server/concurrent-GC tuning are
  cheaper levers if the problem is pause duration rather than allocation volume.
- **Alternatives considered**: **`ArrayPool<byte>.Shared`** — rejected (lifetime, rounding, and the
  extra return discipline — all above); **`MemoryPool<byte>`** — the same lifetime problem with more
  machinery; **`GC.AllocateUninitializedArray`** — saves only the zero-fill, kept in reserve;
  **Server/concurrent GC tuning** — a configuration lever that does not touch ownership; prefer it if
  the issue is pause duration.

## Decision 14 — The worker re-decodes textures that are already resident (accepted)

- **Decision**: `decodeEnvironment` decodes every distinct texture the model references, with no view
  of the render thread's texture cache; the render thread discards the pixels for a cache hit.
  Accept the redundancy.
- **Rationale**: the worker only learns the model's texture paths **after** parsing, so the render
  thread cannot pre-filter a dispatch by the cached set — it does not know which paths the model will
  reference. The alternative, parsing on the render thread first, is exactly what FR-002 forbids. The
  redundant cost is bounded: it only applies to a texture shared with a *still-resident* environment,
  and a reload re-decodes everything anyway (spec 050 drops the parsed unit and its textures on
  eviction). It is not on the ArtRoom first-load path that dominates the measurement.
- **Revisit**: if the shared-texture redundancy shows up in measurement, snapshot the cache keys at
  dispatch and let the worker skip paths already present. The caveat is a race: a path cached at
  dispatch could be evicted before the render thread uploads it, so the render thread would find a
  miss with no pixels and must fail loudly (or re-decode).
- **Alternatives considered**: **pre-filtering at dispatch** — impossible without parsing first;
  **parse on the render thread** — reintroduces the stall FR-002 forbids; **a two-phase handshake**
  (parse, then ask the cache, then decode) — a round trip per load for a bounded gain.
