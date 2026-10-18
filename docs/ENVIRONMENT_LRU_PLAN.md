# Bounded residency for environments (LRU cache)

> **Revised 2026-10-03.** The design below (sections 1–7) is the original, which split each asset
> into a retained CPU half and an evictable GPU half. Measurement showed the CPU half is small
> (~2 MB/environment) and a re-parse is ~10 ms, so the split left RAM unbounded and bought
> nothing measurable. It was **removed**: an environment's parsed geometry and its GPU data are
> now **one loaded unit**, evicted and reloaded together, so the one budget bounds RAM and VRAM.
> `specs/050-environment-lru-cache/` is the authoritative, current design; the **As implemented**
> section at the end records what shipped. Sections 1–7 are kept for the rationale they capture.

Decouple "a zone exists" from "a zone's assets are loaded".

Ported from `odin-game/docs/ENV_MODEL_LRU_PLAN.md` (the reference implementation),
adapted to the F# asset model and the measured sizes of *this* project's assets.

## Goal

A game can hold many zones in `GameplayState.Zones`, and a scenario may spawn all of
them up front. Zone *state* (actors, props, waypoints, triggers, the environment's
part list, collision geometry) is cheap and should stay resident. The zone's
**environment asset GPU data is not**: it is the memory that blows.

Today `Assets.scan` (`world/Assets.fs:777`) eagerly parses **and uploads every**
environment and prop asset at startup (`loadStaticModel`, `Assets.fs:234-273`).
As the asset set grows, startup and VRAM grow with it — with no upper bound.

We want: on zone activation, ensure its environment asset's GPU data is resident;
keep only a bounded subset resident; evict least-recently-used when a new one must
come in. Zone state and CPU-side geometry never evict, so nothing about physics,
undo, the registry, or the editor's part lists changes.

## Why this matters here (measured, 2026-10)

The project's asset sizes are not uniform, which drives the capacity decision below.

| Asset | On-disk | GPU estimate |
|---|---|---|
| `environments/ArtRoom` | 286 MB | **≈ 1.68 GB** (87 textures, 330 Mpx, RGBA8 + full mips) + ~2 MB geometry |
| `environments/Island` | 2.2 MB | a few MB (0 textures; geometry only) |
| `props/Oak`, `Oak2`, `Beech` | 30 / 27 / 20 MB | loaded eagerly today too (deferred to a follow-up) |

`ArtRoom` alone is ~1.7 GB of VRAM once mips exist. `Island` is negligible. A
16-zone game of `ArtRoom`-scale environments would ask for ~27 GB. That is the bug.

## Design: two-phase residency

- **Zone instantiation stays as it is.** `Zone.create`, `Zone.loadEnvironment`,
  `occupyZone`, and every entity/body/part is built from CPU data and is unaffected.
  The per-instance renderer (`EnvironmentRender`, `world/EnvironmentRender.fs:24`)
  already holds only a reference to the shared asset plus a submesh range — it copies
  no GPU data, so an evicted-then-reloaded asset is seen through the same reference.
- **Zone activation ensures GPU residency.** The active zone's environment asset is
  loaded on demand — on a zone switch, and when an environment is instantiated — and
  is touched (moved to the LRU head) each time. No per-frame work is needed: see
  "Load on switch and on instantiation" below for why the just-loaded asset is always
  the head and therefore never its own eviction victim.
- **Only the active zone is ever drawn** (`GameplayScreen.fs:114`, one
  `activeZone` lookup per frame), so an evicted *inactive* asset is never read by a
  render path. This is the safety property the whole scheme rests on; keep it true
  (no cross-zone crossfade).
- **Eviction frees GPU only.** On evict: release the asset's `StaticMesh` buffers and
  its textures, clear the GPU handle, set `GpuLoaded <- false`. The CPU arrays
  (`StaticSubmesh.Positions`/`Indices`, `EnvironmentAsset.fs:17-21`), the parts list,
  the materials' scalar factors, and every zone entity stay put. Physics, picking,
  undo, and the registry never observe an eviction.

## What stays resident vs. what evicts

| Resident always (CPU) | Resident boundedly (GPU) |
|---|---|
| `GameplayState.Zones` and every zone's entities, bodies, ways, triggers | `StaticSubmesh.Mesh` vertex/index buffers |
| `Environment.Parts` slot map + `PropState` bindings | `Material.Bindings` texture handles |
| `StaticSubmesh.Positions` / `Indices` (mesh colliders, bounds) | `EnvironmentBatch` instance buffers (per zone; small, unchanged) |
| `EnvironmentAsset.Parts`, `Bounds`, material scalar factors | |
| Actor assets, portraits, morph buffers (unchanged — out of scope) | |

## The cache: a byte-bounded LRU keyed by environment-asset id

### Existing structures / libraries — survey

There is no ready-made structure to reuse, so this is a small custom type (the
project's `SlotMap` is precedent: custom where the framework has a gap).

- **odin-game's `core:container/lru`** — the reference, but Odin-only. There is no
  .NET port, and nothing in this repo reuses it.
- **Prime (already referenced)** — ships `KeyedCache` and `MutantCache`, but both are
  *single-value* memoization caches (validity-keyed, clone-on-get). Neither is a
  multi-entry bounded LRU; Prime has no LRU.
- **Nu's engine (`Repos/Nu`)** — uses `Dictionary`-based caches (`EntityCachedOpt`,
  `EventAddressCache`); nothing LRU or byte-bounded.
- **BCL** — nothing. `System.Runtime.Caching.MemoryCache` and
  `Microsoft.Extensions.Caching.Memory.MemoryCache` (not referenced) are size-limited,
  but their eviction is a time-based compaction heuristic, not strict LRU, and
  neither budgets **bytes**. Overkill and semantically the wrong policy here.
- **NuGet** — `BitFaster.Caching` (`ClassicLru`/`TLru`) is the closest real library,
  but it is capacity(count)-based, its lock-free machinery buys nothing for
  single-threaded use, and adding a dependency for one small cache cuts against the
  project's minimalism.
- **`System.Collections.Immutable`** (present transitively) has no LRU.

Recommendation: **`System.Collections.Generic.LinkedList<'k>` +
`Dictionary<'k, LinkedListNode<'k>>`** — both BCL, zero dependencies. That pair is
precisely an intrusive doubly-linked LRU list with O(1) touch/evict, and the node is
allocated once per *resident asset* (a handful), never per touch.

`core/Lru.fs` — API:

```fsharp
type Lru =
    { Order : LinkedList<string>
      Nodes : Dictionary<string, LinkedListNode<string>> }
Lru.create : unit -> Lru
Lru.touch  : Lru -> string -> unit    // move to head; add if absent
Lru.remove : Lru -> string -> unit
Lru.tail   : Lru -> string option     // LRU victim
```

Insert it after `core/SlotMap.fs` in `Sugoi.fsproj` (before `world/Assets.fs`).

### Capacity: a byte budget only

Unlike odin — where every environment is a similar ~1.2 GB — the assets here differ
by ~1000× (`ArtRoom` ~1.68 GB vs `Island` ~few MB), so a count is the wrong unit.

```fsharp
// Bound on environment GPU residency. Sized for the measured worst case:
// ArtRoom is ~1.68 GB of textures alone; Island is a few MB. A byte budget keeps
// one heavy zone plus many light ones resident and caps the future, where a count
// of 3 could otherwise reserve ~5 GB of ArtRoom-scale zones.
ENVIRONMENT_GPU_BUDGET_BYTES : int64 = 2048L * 1024L * 1024L   // 2 GB
```

2 GB holds **both** shipping environments today with headroom. The constant is the
only knob. The asset being loaded is exempt: `ensureResident` never evicts it, so a
single asset larger than the budget still loads.

Residency bytes are computed once per asset at load: sum of each texture's
`W * H * 4 * 4/3` (mips) plus each submesh's vertex (32 B/vertex) and index bytes.
Cheap and exact enough to budget with. Eviction runs `while bytes > budget` over
`Lru.tail`, stopping before the asset being loaded.

## F# changes

### 1. `core/Lru.fs` (new) — the structure above.

### 2. `world/EnvironmentAsset.fs` — a mutable GPU side, immutable CPU side

`EnvironmentAsset` keeps its immutable authored/CPU fields. GPU residency becomes
mutable and explicitly flagged (odin's `model` + `model_loaded`):

```fsharp
type StaticSubmesh =
    { Mesh : StaticMesh          // gpu; valid only when its asset is resident
      Material : Texture.Material
      Positions : float32 array  // cpu; always resident (colliders/bounds)
      Indices : int array }

type EnvironmentAsset =
    { Id : string; Name : string; ModelPath : string
      Orientation : Quaternion; Bounds : Gltf.Bounds
      Parts : EnvironmentPart array
      // Retained so a reload can rebuild without re-parsing glTF.
      Source : Gltf.Model
      mutable Submeshes : StaticSubmesh array
      mutable GpuBytes : int64
      mutable GpuLoaded : bool }
```

Retaining `Gltf.Model` (or at least its materials/`TextureRef`s) is new: the current
`loadStaticModel` discards it after upload (`Assets.fs:235-273`), but a reload needs the
texture URIs. Keeping the parsed model also makes the CPU data available before the
first GPU load.

### 3. `world/Assets.fs` — split parse from upload, own textures per asset

- **Scan (`scan`, `Assets.fs:277`)** parses glTF into CPU `EnvironmentAsset`s
  (`Gltf.load`, `Parts`, `Bounds`, material factors) and **does not upload**. Startup
  becomes parse-only — odin's "manifest/JSON parse only" startup (its
  `ENV_MODEL_LRU_PLAN.md` §3). With only `ArtRoom`+`Island` this is a few MB of bins;
  if parse ever costs too much, defer it too behind `ensureAssetCpuData` as odin does.
- `loadStaticModel` splits into `parseStaticModel` (CPU, at scan) and
  `uploadAsset` (GPU, on demand): build `StaticMesh` per submesh, resolve materials,
  compute `GpuBytes`, set `GpuLoaded <- true`.
- **Texture ownership.** Today all textures live in one shared path-keyed cache
  (`Loader.Textures`, `Assets.fs:63-82`) released only at shutdown (`Assets.release`,
  `Assets.fs:311`). Eviction must not free a texture another live asset still binds.
  Two options:
  1. **Per-asset ownership (recommended for env textures):** decode each environment's
     textures into a set owned by that asset; free them on evict. Keep the global
     `White` placeholder (and the default material) manager-owned. Safe because env
     textures are per-directory in practice.
  2. **Refcount the shared cache:** keep path sharing, add a count per texture, free at
     zero. More general, more bookkeeping.
  Either way, the material must be rebuildable on reload, which §2's `Source` provides.
- `Assets.release` becomes: release every *resident* asset (in any order) + actors +
  props + textures. Guard `releaseAsset` on `GpuLoaded` so a double-release can't free
  an already-freed handle.

### 4. `world/Assets.fs` — the cache and the two calls

```fsharp
// Assets.ensureEnvironmentResident: make the asset's GPU data resident, evicting LRU
// tails until it fits. A cache hit touches (moves to head) and returns; the asset
// being loaded is never evicted, so the active zone's environment (always the one
// just switched to or instantiated) is never the victim.
ensureEnvironmentResident : AssetManager -> string -> unit

// Assets.releaseEnvironment: the eviction hook — unload GPU, mark not loaded, leave CPU.
releaseEnvironment : AssetManager -> string -> unit
```

`AssetManager` gains `EnvironmentCache : Lru<string>` and `ResidentBytes : int64 ref`
(or a small mutable struct holder), mirroring `Asset_Manager.model_cache`.

### 5. Load on switch and on instantiation (no per-frame touch)

Two call sites, both already for the **active** zone:

- **Switch** — `GameplayState.swapZone` (`state/GameplayState.fs:83`). After the
  incoming zone becomes active, ensure its environment asset:

  ```fsharp
  // The incoming zone is the one about to render: bring its environment onto the GPU.
  match state.Zones.[next].Environment with
  | Some environment -> Assets.ensureEnvironmentResident shared.Assets environment.AssetId
  | None -> ()
  ```

- **Instantiation** — `Zone.loadEnvironment` and `Zone.loadEnvironmentFrom`
  (`world/Zone.fs:805`, `:824`), which already take the `AssetManager`. Ensure the
  asset as the environment is built:

  ```fsharp
  do Assets.ensureEnvironmentResident assets assetId            // loadEnvironment
  do Assets.ensureEnvironmentResident assets template.AssetId   // loadEnvironmentFrom
  ```

Why dropping odin's per-frame touch is safe here: eviction only ever happens inside
`ensureResident` — i.e. while loading — and every load is for the **active** zone
(switching activates the incoming zone; environment instantiation happens on the
active zone, via the Asset Browser, the registry, and `Undo.undo`). The asset just
loaded is therefore always the LRU head at the moment eviction runs, so it can never
be its own victim.

That invariant is the one to preserve: if a future feature ever loads a *non-active*
zone's environment, either touch the active zone's asset after that load, or exempt
it from eviction.

### 6. `Shell.fs` — release on shutdown

`Shell.fs:274` already releases every zone; ensure the LRU is torn down too
(release any still-resident assets, then the cache) before `Gpu.destroy`.

### 7. Defensive asserts

Add `assert sub`-level `GpuLoaded` checks at the entry of the environment draw/build
paths that dereference `sub.Mesh` (`EnvironmentBatch.gather`/`render`,
`StaticMesh.drawInstanced` callers), per the repo's loud-failure rule: a future render
path that forgets the ensure should fail at once, not read a freed handle.

## Gotchas to design for

- **`EnvironmentBatch` stale submesh references.** The batch's `Registry` holds
  `StaticSubmesh` objects and keys its `Lookup` on `sub.Mesh.VertexBuffer`
  (`EnvironmentBatch.fs:72-91`). A reload creates a *new* vertex buffer, so a zone that
  was evicted and reloaded would leave stale slots. Because gather is only ever
  called for the active zone (never evicted), a stale entry can only be a
  zero-count slot — harmless, but clear the batch's `Registry`/`Lookup` when a zone's
  asset reloads, or key by `(assetId, submeshIndex)` instead of the GPU handle, to keep
  it exact.
- **A load hitch, not a missing frame.** The first switch or instantiation of an
  unloaded environment does the parse/upload synchronously, so it stalls that frame
  (odin's first plan accepted the same). Loading at switch time avoids odin's
  one-frame-missing-geometry case entirely. The async loader
  (`odin-game/docs/ASYNC_LOADING_PLAN.md`) is the follow-up; the seam is correct for it.
- **Materials fall back to `White`.** `Texture.makeMaterial` substitutes the shared
  placeholder for absent maps (`Texture.fs:222-251`); the placeholder is never freed by
  eviction.
- **Registry/AssetBrowser read metadata only.** `ZoneRegistryPanel` reads
  `Environments.[id].Name`; `AssetBrowser` lists discovered assets. Both are fine with
  lazy GPU data as long as `AssetManager.Environments` still holds every discovered
  asset's metadata (it does).

## Out of scope (follow-ups, matching odin)

- Async/threaded load and a `PreloadZone` cue op. *(Both delivered: async loading is `specs/053`; the
  cue op is `specs/062-zone-cue-ops`.)*
- Prop-asset and actor-asset laziness/sharing (they are eagerly loaded today too;
  `Props`/`Actors` stay as-is in this cut).
- Texture compression / resolution budget (would shrink `ArtRoom` 4–8×).
- A loading-screen UX for switching to a pending zone.

## Verification

- `dotnet build` clean; `dotnet test` suite passes (the headless tests build
  `AssetManager` records directly — add the new fields to the test fixtures, which the
  suite already does for `AssetManager`).
- Set `ENVIRONMENT_GPU_BUDGET_BYTES` low (e.g. one asset) and alternate between two
  zones in the editor: each return reloads, renders correctly, and never double-frees.
- Confirm startup no longer uploads env textures (add a `printfn` around `uploadAsset`
  temporarily, or rely on the existing per-phase timing in `Shell.fs`).
- Confirm physics/picking inside an environment still work immediately after a
  switch — they read CPU data, so they must be unaffected by an eviction.
- Confirm shutdown after heavy zone-hopping leaks nothing (no SDL handle warning;
  `Gpu.destroy` clean).

## Risks

- **First activation per asset pays parse+upload.** Same total work as today, relocated
  from startup. The retained CPU half keeps repeats cheap.
- **Editor thrash between >budget heavy zones** re-decodes textures on each hop. The
  budget constant is the knob; texture compression is the real fix.
- **Texture ownership** was the riskiest edit: the refcounted cache is what keeps an
  eviction from freeing a texture a live asset still binds (FR-018).

## As implemented (spec 050)

Shipped as designed, with these concrete choices:

- **One loaded unit.** `EnvironmentAsset` carries a single `mutable Loaded : EnvironmentLoaded
  option` holding, together, the parsed geometry and the uploaded buffers — `LoadedSubmesh
  { Positions; Normals; Uvs; Indices; MaterialIndex; Mesh; Material }`, plus `Parts`, `Bounds`,
  the source `Materials`, the unit's `SubmeshBytes` and the acquired `TextureKeys`. `Loaded = None`
  is the not-loaded state, so a freed handle is unrepresentable. `EnvironmentAsset.submeshes` /
  `parts` / `bounds` / `materials` assert the unit is present.
- **Byte budget, 2 GB** (`ENVIRONMENT_BUDGET_BYTES`), no count cap. `ResidentBytes` sums each
  loaded unit's `SubmeshBytes` (retained geometry plus vertex/index buffers) and each texture's
  bytes while its refcount is positive, so a shared texture counts once. The budget therefore
  caps RAM as well as VRAM.
- **`core/Lru.fs`** over BCL `LinkedList` + `Dictionary`; no new dependency.
- **Refcounted texture cache** on the (lazily created) `Loader`; a load acquires and an eviction
  releases, freeing at zero.
- **Load hooks**: `Zone.setEnvironment` (covers the asset browser's `SetEnvironment` command and
  `Undo.undo`'s `occupyZone`), `Zone.loadEnvironment`/`loadEnvironmentFrom` (load before the
  parts read), and `GameplayState.swapZone` (activation). All load before mutating, so a failed
  load cancels the action.
- **Eviction** exempts a `protectedIds` list (the asset being loaded, which is the environment
  becoming active), so the environment in use is never its own victim.
- **Metadata-only startup**: `scan` records environment manifests only; the model is loaded on
  first use.
- **Editor glyph** in the Outliner zones row (`[+]` loaded / `[ ]` not / `[-]` none); no new
  binding or panel.

Deferred, as scoped: async/threaded loading, cue preloading, prop/actor residency, and
startup-cost parsing (the parse still happens on first use, which is the intended tradeoff).
