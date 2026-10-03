# Data Model: Async asset loading

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

This feature adds the CPU/GPU handoff (a decoded payload), a bounded recency queue and a small
worker pool. It changes no persisted entity and no loaded-unit shape. It does **replace two
option-typed fields with sum types** (design D12): the asset's `Loaded : EnvironmentLoaded option`
becomes a `Residency` DU, and the zone's `Environment : Environment option` becomes a
`ZoneEnvironment` DU. In both cases the DU **adds a state the option could not express** (asset:
loading/failed; zone: awaiting), rather than removing a flag that already existed. The loaded unit
itself (`EnvironmentLoaded`) is unchanged. One small field is **added** to `Environment` — see
"Parts source" — because the deferred load needs to know whether its parts are still to be
materialised.

## Type aliases (documentation only)

`world/AssetLoad.fs` names its string keys, so a signature reads without a comment. F# erases type
abbreviations, so these are documentation, **not** type safety (a real distinction would need
single-case DUs, which is more friction than this cold path warrants).

```fsharp
type AssetId     = string   // an environment asset id
type ModelPath   = string   // an absolute glTF path
type TextureKey  = string   // an absolute texture path (the texture cache key)
```

## `DecodedImage` (`render/Texture.fs`)

One texture's pixels, decoded off the render thread. The exact RGBA8 layout `Texture.load` already
packs. It lives in `Texture.fs` (not `AssetLoad.fs`) because `Texture.decode` returns it and
`Texture.fs` is compiled first.

```
DecodedImage
  Width  : int
  Height : int
  Pixels : byte array      // Width*4*Height, rows compacted (no pitch padding)
```

## `DecodedEnvironment` (`world/AssetLoad.fs`)

The CPU half of one environment load, handed from a worker to the render thread.

```
DecodedEnvironment
  AssetId  : AssetId
  Model    : Gltf.Model                        // parsed geometry, parts, bounds, materials
  Images   : Dictionary<TextureKey, Texture.DecodedImage>   // distinct path → pixels
  ParseNs  : int64                             // worker-measured, for the instrumentation line
  DecodeNs : int64
  QueueNs  : int64                             // request → claim, set by the worker
```

- **Identity**: one per requested asset id.
- **Lifetime**: created by a worker; consumed (or discarded) by `Assets.pump`; dropped after upload.
- **Invariant**: `Images` holds an entry for every texture path any `Model.Materials` entry
  references (that is where the pixels for a cache miss come from).
- **Accepted cost**: the worker has no view of the render thread's texture cache, so it decodes
  **every** distinct path in the model and the render thread discards the hits. A reload re-decodes
  everything anyway (spec 050 drops the parsed unit on eviction), so this is accepted; see
  research D14 for the revisit.

## `LoadOutcome` (`world/AssetLoad.fs`)

What a worker posts back: success or a failure message (never a throw across the thread boundary).

```
LoadOutcome =
  | Decoded of DecodedEnvironment
  | LoadFailed of AssetId : AssetId * Message : string
```

## `LoadQueue` (`world/AssetLoad.fs`)

The bounded, de-duplicated, most-recent-first set of outstanding requests, plus what each worker has
claimed, what is pinned, and the one upload with GPU work in flight.

```
LoadQueue
  Gate         : obj                  // the lock guarding every field below
  Pending      : Lru                  // head = most recently requested; queued + decoding + uploading
  Claimed      : HashSet<AssetId>     // claimed by a worker, being uploaded, or awaiting upload
  Pinned       : HashSet<AssetId>     // the active environment: never dropped, never evicted
  Paths        : Dictionary<AssetId, ModelPath>   // the claim reads these
  RequestTicks : Dictionary<AssetId, int64>       // for the queue-wait timing
  mutable Uploading : AssetId option  // the id whose GPU work is in progress (at most one; never dropped)
  Capacity     : int                  // the pending-set bound
```

- **De-duplication**: `request id` moves the id to the head (0→1 entries).
- **Bound**: when `Lru.count Pending > Capacity`, tails are dropped, skipping only pinned ids and
  `Uploading`. A *claimed* (decoding) tail **is** droppable: it leaves `Pending` but its decode is
  not cancelled, so its payload is discarded on completion (FR-005, clarification 1). A *waiting*
  cursor (drained, decoded, not yet the active upload) holds no GPU work, so it is droppable too and
  its payload is discarded when the pump reaches it. Because `Capacity (8) > pinned (1) + uploading
  (≤1)`, a droppable tail always exists, so `Lru.count Pending ≤ Capacity` holds (SC-004).
- **Claim**: `claimNext` returns the most-recently-requested id in `Pending` that is not `Claimed` and
  not `Uploading`. **The claim is held from claim until the id becomes resident, is discarded, or
  fails** — so an asset being uploaded is never decoded a second time.
- **Pinning**: `Pinned` is replaced by `Assets.setPinned`, which the gameplay layer calls every frame
  with the active zone's environment (`AssetManager.PinnedIds` is the durable home the queue's mirror
  is seeded from). It never accumulates ids of environments no longer in use. The set is read by
  `Assets.pump` for both the drop bound here and eviction protection (FR-006).
- **Uploads**: `beginUpload id` sets `Uploading` when the pump starts building a cursor's batch and
  `endUpload` clears it at settle/abort. At most one, because `pump` builds cursors one at a time.
- **Resident**: `makeResident id` removes the id from `Pending`, `Claimed` and `Uploading`.

## `LoadPool` (`world/AssetLoad.fs`)

```
LoadPool
  Queue         : LoadQueue
  Ready         : Channel<LoadOutcome>   // unbounded; worker → render
  Slots         : SemaphoreSlim          // (workers, workers): bounds decoded-but-not-uploaded payloads
  Stop          : CancellationTokenSource // cancelled by `stopPool` to wake slot waits
  Workers       : Thread array           // IsBackground = true (shutdown does not block on a decode)
  ActiveDecodes : int ref                // decodes running now (`inFlight`), via Interlocked
  Stopping      : bool ref               // read/written under `Queue.Gate` with Monitor.Wait/PulseAll
```

- Created lazily by `Assets` on the first request for a not-yet-resident asset, seeded with the
  manager's pin set. `decode` is the injected function (`AssetLoad.decodeEnvironment`); the id→path
  table and the claim live on the `LoadQueue`, so everything the worker needs is under one gate.
- `loadWorkers = 2` (clamped to ≥ 1, `FFS_LOAD_WORKERS`), `LoadQueueCapacity = 8`.
- `Slots` bounds the payloads that are decoded-but-not-uploaded to `workers`, so a fast decode cannot
  run ahead of the budgeted upload and pile up decoded pixels. A slot is taken before a decode and
  released only when the payload's cursor **settles, is discarded, or fails** — so it covers the
  decode, the ready queue and the cursor alike (`Assets.releaseSlot`).
- `inFlight` is the **active decodes** (`ActiveDecodes`), not the claim count — a worker may hold a
  claim across an upload without decoding.
- Workers are **background** threads: `stopPool` cancels, wakes and joins with a short bounded
  timeout, then abandons a worker mid-decode. An abandoned worker touches only managed memory (never
  the device), so its payload is dropped harmlessly.

## `Residency` (`world/EnvironmentAsset.fs`) — CHANGED

The asset's coarse lifecycle, **single-writer on the render thread**. It replaces
`mutable Loaded : EnvironmentLoaded option`, so *not loaded*, *loading*, *resident* and *failed*
are mutually exclusive by construction (the option could represent an absent unit while a load is
under way). The loaded unit is readable only in the `Resident` case.

```
Residency =
  | NotLoaded                // discovered, or evicted back to nothing
  | Loading                  // requested/pending/decoding/uploading
  | Resident of EnvironmentLoaded
  | Failed of Message : string
```

- `EnvironmentAsset` gains `mutable Residency : Residency` (in place of `mutable Loaded`).
- The accessors `submeshes` / `parts` / `bounds` / `materials` match `Resident loaded -> ...` and
  otherwise `invalidOp` (spec 050 FR-011, unchanged in behaviour).
- `isEnvironmentResident` becomes `match asset.Residency with Resident _ -> true | _ -> false`.
- `Loading` is coarse on purpose: the fine phases (`Queued`/`Decoding`/`Uploading`) are written by
  two threads and are **derived** (below), so they cannot live on the single-writer DU.

## `ZoneEnvironment` (`world/Zone.fs`) — CHANGED

The zone's environment, as one DU, replacing the single `Environment : Environment option` field.
**Drawability is encoded**: only `Active` has attached part resources and may be gathered.

```
ZoneEnvironment =
  | NoEnvironment            // the zone has no environment
  | Active of Environment    // resident; part bodies + renderers attached
  | Awaiting of Environment  // set, but the asset is not resident yet; attached on landing
```

- `Zone` stores `mutable EnvironmentState : ZoneEnvironment` and exposes a read-only
  `member Environment : Environment option` so every existing read site (`zone.Environment`, ~8
  files and the suite) is unchanged. A member **cannot share a record field's name** in F# (FS0023),
  which is why the field is renamed; the ~2 write sites (in `Zone.fs` and one test) move to
  `EnvironmentState`.
- `gatherStatic` passes the parts only for `Active`; `Awaiting` draws no parts (pop-in) and may be
  empty (a fresh asset load) or populated (a template/undo), so the reconcile both *materialises*
  and *attaches*.
- **On failure the environment stays `Awaiting`** (the authored value is kept, nothing is drawn);
  `reconcileEnvironment` acts only on `Resident`, so a `Failed` asset never attaches. A later request
  may move it to `Active`. (FR-008.)

### Parts source (addition to `Environment`, `world/Entity.fs`)

`Environment` carries its `Parts : SlotMap<Prop>` by value, and that value travels in
`Command.SetEnvironment` and in `ZoneContents`. Because the parse is now off-thread, a
`loadEnvironment` may store an environment whose parts are not yet materialised — so the value must
record **where its parts come from**, and whether that is still pending. Without this, a
materialise-on-empty rule would wrongly fill a registered template that deliberately has a subset
(spec 034) with the asset's full parts.

```
PartsSource =
  | FromAsset    // parts are the asset's parsed parts; materialise them when it lands
  | Authored     // parts are final (a registered template, or an undo/redo payload)

Environment gains:  PartsSource : PartsSource
```

- `loadEnvironment` builds `PartsSource = FromAsset` with an empty parts map.
- `loadEnvironmentFrom` (a template) builds `PartsSource = Authored` with the template's parts
  (possibly none) — never materialised from the asset.
- Undo/redo restores the stored value, so the marker survives capture (this is why it lives on
  `Environment`, not on `ZoneEnvironment`).
- `reconcileEnvironment` materialises **only** when `PartsSource = FromAsset && Parts.Count = 0`,
  then attaches every part and sets `Active`. A redo of an already-materialised value has
  `Count > 0`, so it is not filled twice; a bare template (`Authored`, `Count = 0`) stays empty.

## `LoadState` (derived, `world/Assets.fs`)

The **readout view**, derived on the render thread from `Residency` plus the queue/pump detail (no
second source of truth for "resident").

```
LoadState =
  | NotLoaded      // Residency = NotLoaded
  | Queued         // Residency = Loading, in Pending, not claimed, no cursor
  | Decoding       // Residency = Loading, claimed by a worker
  | Uploading      // Residency = Loading, a PendingUpload (cursor) exists
  | Resident       // Residency = Resident
  | Failed of string
```

- `isLoading` = Queued | Decoding | Uploading; `hasFailed` = Failed.
- `Uploading` means a `PendingUpload` cursor exists (active **or** waiting); the queue's single
  `Uploading` slot is only the *active* one, and exists solely so `request`'s drop can skip it.

## `PendingUpload` (`world/Assets.fs`)

The resumable render-side build cursor for one payload (one asset).

```
PendingUpload
  Payload        : DecodedEnvironment
  Batch          : Gpu.UploadBatch option               // created only when this cursor is active
  Created        : ResizeArray<StaticMesh.StaticMesh>   // for rollback
  Keys           : ResizeArray<TextureKey>              // acquired texture keys, for rollback
  MaterialCache  : Dictionary<int, Texture.Material>
  Submeshes      : ResizeArray<LoadedSubmesh>           // built so far
  NextSubmesh    : int                                  // the cursor
  SubmeshBytes   : int64
  TextureAdded   : int64
```

- **Lifetime**: created when a wanted payload is drained; advanced each `pump` until complete; then
  discarded (the `EnvironmentLoaded` is installed and the cursor dropped).
- **`Batch` is lazy** (an `option`): only the **active** cursor has one, so at most one staging
  buffer exists at a time and peak staging stays at the spec-052 bound. A waiting cursor holds only
  its pixels.
- **Invariant**: `Created`, `Keys` and `SubmeshBytes`/`TextureAdded` track exactly what has been
  made, so a failure mid-build releases exactly that (FR-008).

## `AssetManager` (`world/Assets.fs`) — CHANGED

```
AssetManager
  ... existing fields unchanged (ResidentBytes, Cache, Loader, ...)
  mutable Pool       : LoadPool option              // lazy; None until a decode is needed
  mutable Uploads    : Dictionary<AssetId, PendingUpload>
  mutable PinnedIds  : HashSet<string>              // durable pin set; seeds the queue on creation
```

The residency fields (`Cache`, `ResidentBytes`) and the texture cache (`Loader.Textures`) are
unchanged; a failure is recorded on the asset's own `Residency.Failed`, not in a side map. The pin
set lives here (not only in the queue) because `setPinned`/`requestEnvironment` may run before the
lazy pool exists.

## Lifecycle / state transitions

Three coordinated machines. **`Residency` is the coarse, single-writer truth**; the queue phases are
a derived view; `ZoneEnvironment` is the per-zone consumer.

```
                         request(id)                       claimNext(id)
  Nothing ────────────────────────► Pending(id) ────────────────────────► Claimed(id)
     ▲                                  │                                     │
     │ drop (bound, unpinned,           │ drop (bound, unpinned,              │ worker posts
     │       un-uploading)              │       un-uploading)                 ▼
     │                                  └───────────────┐             Ready(id, outcome)
     │                                                  │                    │ pump drains
     │                                                  │        ┌───────────┴───────────┐
     │                                                  │    wanted                  unwanted
     │                                                  │        │                       │
     │                                                  │        ▼                       ▼
     │                                                  │  cursor(id)              discard (no upload)
     │                                                  │   │  beginUpload(id)
     │                                                  │   │        │
     │                                                  │   │  ┌─────┴─────┐
     │                                                  │   │ complete   failure/dropped
     │                                                  │   │  │            │
     │                                                  │   ▼  ▼            ▼
     └────────────────────────────────────────────────► NotLoaded      Failed
                       (the dropped id's Residency is reset here)
```

`Residency` transitions (render thread only):

- `NotLoaded → Loading` on `request` (also the retry path from `Failed`).
- `Loading → Resident` on a completed build.
- `Loading → Failed` on a decode/build error.
- `Loading → NotLoaded` on a **request-time drop** (queued, never decoded) and on a **pump-time
  discard** (a decoding payload whose id is no longer `Pending`) — so a dropped asset is never left
  showing "loading" (FR-008's "never stuck loading").
- `Failed → Loading` on a later `request` (retry).
- `Resident → NotLoaded` on eviction.

`Residency` never passes through `Uploading`/`Decoding` — those are the derived view.

- **`request`**: under the lock, touch the id (MRU) or insert; drop tails over capacity (skipping
  pinned and `Uploading`), resetting each dropped id's `Residency` to `NotLoaded`; record the model
  path; set `Residency = Loading`; release the signal. Returns immediately (FR-001/FR-004). A request
  for a `Resident` asset only touches the `Cache` LRU.
- **claim**: a worker takes the most-recent unclaimed id from `Pending`, marks it claimed, decodes,
  posts a `LoadOutcome`. Claimed ids stay in `Pending` (so they remain droppable); the claim is
  released by the render thread when it *consumes* the outcome, so at most one payload per id sits in
  `Ready`.
- **pump drain**: a wanted `Decoded` becomes a cursor (id moves `Claimed → ` cursor, with
  `Uploading` set when it becomes active); an unwanted payload is discarded (and its id, if still
  `Loading`, is reset to `NotLoaded`); a `LoadFailed` is logged, sets `Residency = Failed`, and
  leaves `Pending`.
- **pump build**: advance the active cursor within the budget (`beginUpload` when it starts, a lazy
  `Batch`); a waiting cursor whose id is no longer `Pending` is discarded when reached. On
  completion install `Resident loaded`, add `SubmeshBytes + TextureAdded` to `ResidentBytes`,
  `Lru.touch Cache`, evict tails over `ENVIRONMENT_BUDGET_BYTES` (skipping pinned), `endUpload`, and
  leave `Pending`.
- **pump failure**: `Gpu.abortUpload`, release `Created` meshes and `Keys` textures, set
  `Residency = Failed`, `endUpload`, drop the cursor, leave `Pending`.

### Zone consumer

- **`setEnvironment`**: set the incoming value immediately; if the asset is `Resident`, materialise
  (if `FromAsset` and empty) and attach, storing `Active`; else store `Awaiting`.
- **`reconcileEnvironment`** (per frame, active zone): `Awaiting env` + `Resident` asset →
  materialise if `FromAsset && Parts.Count = 0`, attach, store `Active`. `Awaiting` + `Failed` → do
  nothing (the environment stays present but undrawn).

## Validation rules

- **VR-001 (FR-002/FR-012)**: `decodeEnvironment` and `Texture.decode` call no `SDL_gpu`/window API;
  every device call happens in `pump`/`Texture.createFromDecoded` on the render thread.
- **VR-002 (FR-005, SC-004)**: the pending set holds one entry per asset id and never exceeds
  `Capacity` (a droppable tail always exists because `Capacity > pinned + uploading`); a repeat
  request never adds a second entry.
- **VR-003 (FR-005)**: a dropped not-yet-decoded request is never decoded (the claim skips ids not in
  `Pending`); a dropped *decoding* request finishes decoding but its payload is discarded, not
  uploaded. The id whose GPU work is in progress (`Uploading`) is never dropped.
- **VR-004 (FR-006)**: a pinned id is never dropped from the pending set, and the same pin set is the
  eviction-protection set, so the active environment is never evicted (spec 050's rule, unchanged).
- **VR-005 (FR-010)**: `ResidentBytes` for a loaded asset, the LRU order and the eviction outcomes
  are identical to the synchronous path; a not-yet-resident (pending) environment contributes
  nothing.
- **VR-006 (FR-011)**: `release` stops the pool before releasing any asset, and the workers are
  background threads, so shutdown never blocks on an in-flight decode and no worker touches a device.
- **VR-007 (FR-008)**: a failed load leaves `Residency = Failed` (never `Loading`); a partial build is
  rolled back exactly (`Created`/`Keys`); the zone's environment stays `Awaiting`.
- **VR-008 (FR-003)**: the render-side build resumes across frames; at most `UploadBudgetMs` of GPU
  work is done per pump, and at most one cursor holds a `Batch` (one staging buffer).
- **VR-009 (D12)**: `Residency` is written only on the render thread, so its cases stay mutually
  exclusive; a `Resident` and a `Failed` cannot coexist. `ZoneEnvironment.Active` is the only case
  `gatherStatic` draws, so a not-yet-resident environment cannot be gathered.
- **VR-010 (FR-004; D8)**: an environment's parts are materialised from the asset **only** when
  `PartsSource = FromAsset` and the parts map is empty; a template (`Authored`) is never filled from
  the asset, so a bare/subset template stays exactly as authored (spec 034).
- **VR-011 (FR-008)**: no dropped or discarded load leaves `Residency = Loading`; every exit from
  `Loading` is to `Resident`, `Failed` or `NotLoaded`.
