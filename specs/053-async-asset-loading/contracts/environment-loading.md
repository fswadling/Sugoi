# Contract: Async environment loading

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Data model**: [data-model.md](./data-model.md)

The surfaces that change, what they guarantee, and what explicitly does not.

## `render/Texture.fs` — CHANGED

```fsharp
/// Decode an image to compacted RGBA8, CPU-only (worker-safe): no device, no batch.
/// Raises on a missing/corrupt file (the worker turns it into a `LoadFailed`).
val decode : path:string -> DecodedImage

/// Create the GPU texture + sampler from decoded pixels and record the level-0 copy (and, for
/// levels > 1, a mip registration) into `batch`. Render thread only.
val createFromDecoded : batch:Gpu.UploadBatch -> device:Gpu.Device -> image:DecodedImage -> Texture
```

- `DecodedImage = { Width : int; Height : int; Pixels : byte array }` is declared **here**, in
  `Texture.fs` (it is the decode output, and `Texture.fs` is compiled before `world/AssetLoad.fs`).
  `world/AssetLoad.fs` **references** `Texture.DecodedImage` and does not re-declare it.
- `load` is replaced by this pair.
- The `Bytes` accounting, format, sampler and mip levels are identical to today's `load`.
- Decode timing is returned by the worker in the payload, not written to a process-global.
- `solid` and `makeMaterial`/`defaultMaterial` are unchanged.

## `world/AssetLoad.fs` — NEW

```fsharp
/// The CPU half of one environment load. `Images` uses `Texture.DecodedImage` (declared in Texture.fs).
type DecodedEnvironment =
    { AssetId : AssetId
      Model : Gltf.Model
      Images : Dictionary<TextureKey, Texture.DecodedImage>
      ParseNs : int64
      DecodeNs : int64
      QueueNs : int64 }   // request → claim, set by the worker

// Documentation-only aliases (F# erases them; no type safety):
type AssetId = string
type ModelPath = string
type TextureKey = string
type TextureUri = string

type LoadOutcome =
    | Decoded of DecodedEnvironment
    | LoadFailed of AssetId : AssetId * Message : string

/// Parse the model and decode every distinct texture it references. CPU-only; raises nothing —
/// a failure is returned as `LoadFailed`.
val decodeEnvironment : assetId:AssetId -> modelPath:ModelPath -> LoadOutcome

/// The bounded, de-duplicated, most-recent-first pending set (an `Lru` under a lock).
type LoadQueue
val createQueue        : capacity:int -> LoadQueue
val request            : queue:LoadQueue -> assetId:AssetId -> modelPath:ModelPath -> AssetId list  // dropped ids
val setPinned          : queue:LoadQueue -> pinnedIds:AssetId list -> unit   // replaces; gameplay layer owns it
val beginUpload        : queue:LoadQueue -> assetId:AssetId -> unit         // the one id with GPU work in flight
val endUpload          : queue:LoadQueue -> assetId:AssetId -> unit
val claimNext          : queue:LoadQueue -> (AssetId * ModelPath) option
val isPending          : queue:LoadQueue -> assetId:AssetId -> bool
val isClaimed          : queue:LoadQueue -> assetId:AssetId -> bool
val makeResident       : queue:LoadQueue -> assetId:AssetId -> unit   // leaves Pending and releases the claim

/// The worker pool: claim → `decode` → post to `Ready`.
type LoadPool
val createPool    : workers:int -> capacity:int -> decode:(AssetId -> ModelPath -> LoadOutcome) -> LoadPool
val requestLoad   : pool:LoadPool -> assetId:AssetId -> modelPath:ModelPath -> AssetId list
val tryTakeReady  : pool:LoadPool -> LoadOutcome option
val inFlight      : pool:LoadPool -> int     // decodes running now (≤ workers; not the claim count)
val stopPool      : pool:LoadPool -> unit    // cancel, wake, bounded join
```

- `Assets` passes `AssetLoad.decodeEnvironment` as the `decode` function; the seam lets the suite
  inject a stub and assert the FR-009 bound (`inFlight ≤ workers`) headlessly.
- **Guarantee (FR-009)**: at most `workers` decodes run at once; the pool size is ≥ 1. `Slots` also
  bounds decoded-but-not-uploaded payloads to `workers`, so a fast decode cannot run ahead of the
  budgeted upload.
- **Guarantee (FR-005)**: the queue is de-duplicated by id, bounded by `capacity`, most-recent-first,
  and drops only tails that are neither pinned nor the current `Uploading` id; a repeat request moves
  an id to the head.
- **Guarantee**: a claim is held from `claimNext` until the id becomes resident, is discarded, or
  fails — so an asset being uploaded is never decoded a second time. The id currently uploading is
  not droppable, and no payload for an id already uploading is ever installed over its cursor.
- Workers are **background** threads; `stopPool` cancels, wakes and does a bounded join (research D10).

## `world/Assets.fs` — CHANGED

```fsharp
/// Request that an environment become resident: de-duplicated, bounded, most-recent-first. The pin
/// set is owned by the gameplay layer (`setPinned`); a request never adds to it. Returns immediately
/// (never loads on the calling thread).
val requestEnvironment : manager:AssetManager -> assetId:AssetId -> unit

/// REPLACE the pin set: the ids never dropped from the pending set and never evicted. The gameplay
/// layer calls this every frame with the active zone's environment, so it never accumulates ids of
/// environments no longer in use. The durable home is `AssetManager.PinnedIds` (the queue's mirror is
/// seeded from it), because this may run before the lazy pool exists.
val setPinned : manager:AssetManager -> pinnedIds:AssetId list -> unit

/// Render thread, once per frame: drain decoded payloads, advance the resumable GPU build within
/// `budgetMs` (sequentially, submission order; one cursor's `UploadBatch` at a time), and settle
/// assets that land (accounting, LRU touch, eviction). Discards payloads for assets no longer wanted.
val pump : manager:AssetManager -> budgetMs:float -> unit

/// The readout: `isEnvironmentResident` is unchanged in behaviour, re-expressed over `Residency`;
/// two predicates are added.
val isLoading : manager:AssetManager -> assetId:AssetId -> bool
val hasFailed : manager:AssetManager -> assetId:AssetId -> bool
```

- `ensureEnvironmentResident` is **replaced** by `requestEnvironment` + `pump`.
- `evictionCandidates`, `releaseEnvironment`, `submeshes`, `isEnvironmentResident`, `scan`, `release`
  keep their roles; `release` now stops the pool first.
- `AssetManager` gains `Pool : LoadPool option`, `Uploads : Dictionary<AssetId, PendingUpload>` and
  `PinnedIds : HashSet<string>`. `ResidentBytes`, `Cache` (the LRU), `ENVIRONMENT_BUDGET_BYTES` and
  the texture refcount cache are unchanged (FR-010).

## `world/EnvironmentAsset.fs` — CHANGED

```fsharp
/// The asset's coarse lifecycle, single-writer on the render thread. Replaces
/// `mutable Loaded : EnvironmentLoaded option`.
type Residency =
    | NotLoaded
    | Loading
    | Resident of EnvironmentLoaded
    | Failed of Message : string

/// `EnvironmentAsset` carries `mutable Residency : Residency`.
val submeshes : asset:EnvironmentAsset -> LoadedSubmesh array   // matches `Resident`, else invalidOp
val parts     : asset:EnvironmentAsset -> EnvironmentPart array
val bounds    : asset:EnvironmentAsset -> Gltf.Bounds
val materials : asset:EnvironmentAsset -> Gltf.Material array
```

- The resident accessors keep their behaviour exactly (raise when not resident); only the pattern
  match changes from `Some loaded` to `Resident loaded`.
- `isEnvironmentResident` matches `Resident`.
- `EnvironmentLoaded` itself is unchanged.

## `world/Entity.fs` — CHANGED (one field)

```fsharp
/// How an environment's parts were obtained, so a deferred load knows whether to materialise them.
type PartsSource =
    | FromAsset    // the asset's parsed parts; materialise when it lands
    | Authored     // final (a registered template, or an undo/redo payload)

/// `Environment` gains `PartsSource : PartsSource`.
```

- `loadEnvironment` builds `FromAsset` with an empty parts map; `loadEnvironmentFrom` builds
  `Authored` from the template; undo/redo restores the stored value (marker included).
- The field is on `Environment` (not `ZoneEnvironment`) so it survives the `Command.SetEnvironment`
  and `ZoneContents` payloads. Field-wise constructions in `Zone.fs` and the suite are updated.

## `world/Zone.fs` — CHANGED

```fsharp
/// The zone's environment as one state, replacing the single `Environment : Environment option`
/// field. Only `Active` has attached part resources and may be drawn.
type ZoneEnvironment =
    | NoEnvironment
    | Active of Environment
    | Awaiting of Environment

/// `Zone` carries `mutable EnvironmentState : ZoneEnvironment` and exposes a read-only
/// `member Environment : Environment option` (a member cannot share a record field's name, FS0023).
val setEnvironment : zone:Zone -> world:Physics.World -> assets:Assets.AssetManager -> environment:Environment option -> unit
val reconcileEnvironment : zone:Zone -> world:Physics.World -> assets:Assets.AssetManager -> unit
```

- `setEnvironment` sets `Active` when the asset is resident, else `Awaiting`; `reconcileEnvironment`
  moves `Awaiting → Active` once the asset lands, materialising the parts **only** when
  `PartsSource = FromAsset && Parts.Count = 0`, then attaching (VR-010).
- `loadEnvironment` / `loadEnvironmentFrom` request instead of ensuring; the environment value is
  built immediately (its parts materialised from the parsed asset when it lands, or taken from the
  template at once).
- `gatherStatic` draws parts only for `Active`; `Awaiting` passes `ValueNone`, so the draw path
  tolerates the gap (pop-in). The type, not a flag, enforces this.
- The write sites (`deactivateEnvironment`, `activateEnvironment`, `release`) move to
  `EnvironmentState`; the ~8 read files keep using `zone.Environment`.
- Physics: `deactivatePhysics`/`activatePhysics` are unchanged (a part with no body is a no-op).

## `state/GameplayState.fs` — CHANGED

- `swapZone` calls `requestEnvironment` and completes in-frame; it no longer waits for a load
  (FR-004). This supersedes spec 050 FR-017's load-before-commit. The pin set is a gameplay-layer
  concern, refreshed every frame (`GameplayScreen`), so `swapZone` does not touch it.

## `editor/Outliner.fs` — CHANGED

- The per-zone residency glyph: `[-]` no environment, `[ ]` not loaded, `[~]` loading,
  `[+]` resident, `[x]` failed (FR-007/FR-008).

## `screen/GameplayScreen.fs` — CHANGED

- Each frame on the render thread: `Assets.setPinned` to the active zone's environment (so the pin
  set never accumulates ids no longer in use), then `Assets.pump shared.Assets UploadBudgetMs`, then
  `Zone.reconcileEnvironment` for the active zone.

## Explicitly NOT changed

- **Residency behaviour** (spec 050): the byte budget, the LRU eviction order, load-on-demand, the
  loud failure on non-resident use, and the byte accounting for a loaded asset. (The asset's field
  becomes `Residency` — decision D12 — but its semantics are identical.)
- **The draw path**: `EnvironmentBatch`, `EnvironmentRender`, `StaticMesh.drawInstanced`, the
  instanced layout, buffer identity as the batch key.
- **Props and actors**: their eager load, at `scan`, on the render thread (spec 050's boundary).
  `Texture.load`'s one call site is `Assets.acquireTexture`, so they simply recompose `decode` +
  `createFromDecoded` inline at that choke point.
- **The texture refcount cache** (`Loader.Textures`): acquire/release semantics and the white
  placeholder.
- **Per-frame dynamic uploads**: `EnvironmentBatch.upload`, `DebugDraw.upload`, `Ui.upload`.
- **Shaders, pipelines, passes, formats, MSAA/depth targets, the swapchain**: untouched.
- **The GPU upload batching** (spec 052): `UploadBatch`, staging capacity and flush rule are reused
  as-is.

## Test surface

- Device-free: the queue (bound, de-duplication, recency, pinning, drop) and the pool's concurrency
  bound are pure and asserted headlessly. Core model/service logic (residency accounting, eviction
  order) is unchanged, so the **412-test suite is the regression gate** once the fixtures move to
  `Residency` / `EnvironmentState` / `PartsSource`. The headless fixtures use already-resident
  assets, so `setEnvironment` takes the immediate branch and no pump is needed.
  `test/AssetsTests.fs` uses `Loaded = None` and `ensureEnvironmentResident` today, and
  `test/GameplayStateTests.fs` has a "switch to a missing environment is cancelled" test that
  FR-004 supersedes — both are rewritten (T010/T012).
- Device-dependent: the worker pool, the payload handoff and the budgeted GPU build are verified by
  the GPU walkthrough in [quickstart.md](./quickstart.md) with `FFS_LOAD_TIMING=1`.
