# Phase 0 Research: Bounded environment residency

The capability is settled by the reference (`odin-game`) and by the port's own asset model;
the work is in the choices the reference does not pin down for *this* codebase — what the
residency unit is (a whole loaded environment, or a split of it), how a byte budget is
accounted, how a shared texture is freed safely, and where the two load hooks sit.

## Decision 1 — The residency unit is an environment *asset*'s loaded data, keyed by id

- **Decision**: bound the loaded data of each `EnvironmentAsset` — its parsed geometry and
  its GPU meshes and textures, as **one unit** — keyed by asset id, shared by every zone that
  loads it. Zone state (entities, parts, colliders, batch) is not the unit.
- **Rationale**: the cost lives on the shared asset, not the zone: `EnvironmentRender` copies
  no data, and every zone's environment builds its parts from the same `EnvironmentAsset`
  (`Zone.loadEnvironment`). Bounding the asset is exactly `odin-game`'s `model_cache` over
  `^Environment_Asset` (`src/assets.odin:185`, `MAX_LOADED_ENVIRONMENT_MODELS :: 3`). Two
  zones using one asset are one residency entry (spec `FR-008`), which a per-zone bound could
  not express.
- **Alternatives considered**: **per-zone environments** — would duplicate shared geometry
  and textures, and break `FR-008`; **per-zone GPU batches** — the batch is a small instance
  buffer already owned by the zone, not the cost.

## Decision 2 — Load on demand; startup is metadata only

- **Decision**: `scan` records id/name/model path/orientation only. **Parsing** (`Gltf.load`
  → geometry, parts, bounds, materials) and **uploading** (static buffers + textures) both
  happen on first use, and a later eviction frees both; a reload repeats both.
  `ensureEnvironmentResident` does parse-then-upload for an asset that is not loaded.
- **Rationale**: spec Q3 / `FR-010` / `SC-005` — startup performs neither. The pre-feature
  `loadStaticModel` fused parse and upload at scan; splitting them (now `buildLoaded`, called on
  demand) makes launch cost independent of environment count and size, which is what a
  20-environment validation (SC-001) needs. This is also what `odin-game` does through its
  manifest-only scan (`ENV_MODEL_LRU_PLAN.md` §3).
- **Alternatives considered**: **parse at scan, upload lazily** — still reads every
  environment's glTF at launch (rejected by Q3); **upload at scan, never evict** — the status
  quo that blows memory.

## Decision 3 — LRU: a small custom type over BCL collections; no library

- **Decision**: `core/Lru.fs`, keyed by `string`, over `LinkedList<string>` +
  `Dictionary<string, LinkedListNode<string>>`.
- **Rationale**: the survey (recorded in `docs/ENVIRONMENT_LRU_PLAN.md`) found no reusable
  LRU: `odin-game`'s `core:container/lru` is Odin-only; Prime's `KeyedCache`/`MutantCache`
  are single-value memoisation caches; the BCL has none (`MemoryCache` is size-limited but
  compacts by time, not strict LRU, and cannot budget bytes); `BitFaster.Caching` is the
  closest library but is count-based and adds a dependency. The BCL linked-list + dictionary
  pair *is* the intrusive LRU list with O(1) touch/evict, and one node per resident asset is
  negligible. House heuristic: custom where the framework has a gap, as with `SlotMap`.
- **Alternatives considered**: **a new package** (Minimalism I; count-based, wrong unit);
  **`System.Collections.Immutable`** (no LRU); **an `FDeque` ordering** (touch becomes O(n)
  and allocating on a path that can run repeatedly).

## Decision 4 — A byte budget bounding the whole loaded unit, charged as actual resident bytes

- **Decision**: one constant, `ENVIRONMENT_BUDGET_BYTES = 2 GB`. `AssetManager` tracks
  `ResidentBytes`; `ensureEnvironmentResident` inserts the incoming asset and then evicts LRU
  tails `while ResidentBytes > budget`, never evicting a protected id (the asset being loaded,
  which is the environment becoming active).
- **Rationale**: this project's assets differ by ~1000× (`ArtRoom` ≈ 1.68 GB of textures
  measured from 87 textures / 330 Mpx, RGBA8 + full mips; `Island` a few MB), so a count is
  the wrong unit — the same reason `odin-game` could use `:: 3` and this port cannot. 2 GB
  holds both shipping environments today and caps growth; spec Q1 makes the budget a bound on
  what is *kept*, so an asset larger than the budget still loads (it is head, never a victim).
  `ResidentBytes` is the sum, over resident assets, of each unit's submesh bytes (its retained
  geometry plus its vertex/index buffers) and the bytes of every texture with a positive
  refcount, so a shared texture counts once. Because the unit includes the parsed geometry,
  the same budget bounds **RAM** as well as VRAM.
- **Alternatives considered**: **a count cap (3, odin-parity)** — three `ArtRoom`-scale zones
  would reserve ~5 GB; **a hard ceiling that refuses an over-budget asset** — rejected by Q1.

## Decision 5 — A refcounted shared-texture cache; free at zero

- **Decision**: the `Loader` carries a path-keyed cache of `TextureEntry = { Texture;
  mutable Refs : int }`. Loading an asset *acquires* each texture it binds (incrementing
  `Refs`); evicting *releases* them (decrement); a texture is freed only when `Refs` reaches
  zero. The `White` placeholder and default material are permanent (never acquired/released).
- **Rationale**: spec `FR-018` / Q4 — the loader shares textures by path and used to free
  them only at shutdown; with eviction, freeing one asset's texture while another resident
  asset binds it would corrupt the other. Charging the budget on the 0→1 acquire and crediting
  on the 1→0 release also makes `ResidentBytes` the *actual* footprint, so a shared texture
  counts once (FR-001).
- **Alternatives considered**: **per-asset texture ownership** — cannot satisfy `FR-018` when
  two *different* assets reference the same file; **never free textures** — leaks VRAM on
  every eviction (the bug `odin-game`'s `unload_environment_model_textures` exists to avoid).

## Decision 6 — One loaded unit; the CPU/GPU split was removed

- **Decision**: `EnvironmentAsset` keeps its metadata and gains **one** option,
  `mutable Loaded : EnvironmentLoaded option`, holding the parsed geometry and the GPU data
  together:

  ```fsharp
  type LoadedSubmesh =
      { Positions; Normals; Uvs : float32 array; Indices : int array; MaterialIndex : int
        Mesh : StaticMesh          // GPU buffers
        Material : Texture.Material } // GPU bindings

  type EnvironmentLoaded =
      { Submeshes : LoadedSubmesh array
        Parts : EnvironmentPart array
        Bounds : Gltf.Bounds
        Materials : Gltf.Material array
        SubmeshBytes : int64
        TextureKeys : string array }
  ```

  `Loaded = None` is the not-loaded state. Eviction frees the buffers and textures and drops
  the whole value (the geometry arrays become collectable); a reload re-parses and re-uploads.
- **Rationale**: an earlier draft kept a separate always-resident CPU half and evicted only
  the GPU half. Measurement showed the CPU half is small — ~2 MB per environment (art room
  1.84 MB; island 2.37 MB) — and a re-parse is ~7–20 ms. Its only consumers run at (re)build
  time *after* a load — `Zone.meshCollider`/`attachPropPhysics` build colliders, `assetPartStates`
  reads parts — and the colliders themselves live in the physics world (Jolt copies the
  vertices into the mesh shape), so nothing needs the geometry while an environment is
  evicted. Retaining it therefore bought ~2 MB/env of avoided re-parse, left **RAM unbounded**,
  and added a third residency state and a second accessor family. One unit is simpler and
  bounds RAM and VRAM under the same budget (spec `FR-001`, `FR-006`).
- **Alternatives considered**: **the CPU/GPU split (odin parity)** — right for `odin-game`,
  whose CPU data is large (collision BVH, heightmaps, splat maps) and worth retaining, but not
  here; **mutable handles + a `GpuLoaded` bool** — leaves freed handles representable
  (constitution II prefers `None`); **parse eagerly at scan, evict only GPU** — launch cost
  grows with environment count (rejected by Q3).

## Decision 7 — Three ensure hooks: `setEnvironment`, `loadEnvironment[From]`, `swapZone`

- **Decision**: ensure residency at (a) `Zone.setEnvironment` for the `Some` case, before any
  mutation, (b) `GameplayState.swapZone`, before the active zone changes, and (c)
  `Zone.loadEnvironment`/`loadEnvironmentFrom`, before they read the asset's parts to build
  the environment.
- **Rationale**: (a) and (b) are the two ways an environment becomes current. `setEnvironment`
  is the single funnel for a freshly built environment — the asset browser's `SetEnvironment`
  command and `History.undo`'s `occupyZone` reach it — so a restore after eviction is covered
  there. (c) is separate because `loadEnvironment` reads the asset's parts (via
  `assetPartStates`) to *build* the environment before `setEnvironment` runs, so the load must
  happen first. `swapZone` covers re-activating an already-built environment whose asset was
  evicted. Placing each ensure *before* the mutation is what makes a failed load cancel
  cleanly (spec Q2 / `FR-017`): `swapZone` ensures before `deactivatePhysics`/`ActiveZone <-
  next`, and `setEnvironment` ensures before `deactivateEnvironment`.
  - **Deliberately not per-frame**: eviction only happens inside the ensure, and every ensure
    is for the environment becoming current, so the just-loaded asset is always the LRU head
    and never its own victim. The eviction exempts every id in `protectedIds`: the caller
    passes the asset being loaded, which — because every load is for the zone becoming (or
    already) active — *is* the active zone's environment, so the environment on screen is never
    evicted (`FR-005`). A future load for a non-active zone must add the active environment's
    id to `protectedIds` as well.
- **Alternatives considered**: **ensure at the top of `renderScene`** (odin's per-frame
  touch) — unnecessary and adds per-frame work; **ensure inside `loadEnvironment` only** —
  misses the `swapZone` re-activation and the undo-restore.

## Decision 8 — The Outliner glyph reuses `ImGui.text`; no new binding

- **Decision**: fold a residency glyph into the zone row's existing label in
  `editor/Outliner.fs`, read through `Assets.isEnvironmentResident`: `"[+]"` resident, `"[ ]"`
  not resident, `"[-]"` no environment. No new ImGui binding, no new panel.
- **Rationale**: `FR-013` asks for a minimal glyph in the existing list, `SC-008` for no new
  panel/column. The row already composes a marker into its label, so one more prefix needs no
  layout change and no binding. The glyph is ASCII, avoiding any dependence on the ImGui
  default font's Unicode coverage. The reader is pure and asserted headlessly
  (`FR-014`–`FR-016`).
- **Alternatives considered**: **`igTextColored`** for a coloured dot — a new binding and a
  font-coverage question for a cosmetic gain; **`igBulletText`** — not bound, same issue;
  **a separate column/panel** — contradicts `SC-008`.

## Decision 9 — Test the pure half; the load/evict and the glyph are the walkthrough

- **Decision**: assert headlessly the LRU (`test/LruTests.fs`) and, in the extended
  `test/AssetsTests.fs`, the residency reader, the eviction decision and the metadata-only
  scan. The existing zero-device suites keep driving `Zone`/`GameplayState` with fixtures
  built in the **loaded** state. The parse/upload, eviction under the budget, the texture
  refcount and the glyph are the quickstart.
- **Rationale**: this port cannot open a GPU device in tests (AGENTS.md), so only pure logic
  is asserted. The eviction *decision* (which ids, in what order, given bytes) is the part a
  subtle bug would break invisibly, so it is factored pure and asserted; the fixture update is
  what keeps the device-free paths exercising the real code. The texture refcount needs a
  device and is the one piece deferred to the walkthrough.
- **Alternatives considered**: **golden memory numbers** (no device in the host); **test
  nothing** (leaves the LRU order and accounting — the parts most likely wrong — uncovered).
