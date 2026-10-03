# Data Model: Bounded environment residency

The feature adds one small structure (the LRU), one field on each of two existing records
(`Texture`, `AssetManager`), and one loaded-unit type behind a single option on
`EnvironmentAsset`. Nothing is persisted.

## `Lru` — new, in `core/Lru.fs`

Keyed by environment-asset id (a `string`). A dictionary gives O(1) membership and node
lookup; a linked list gives O(1) move-to-head and tail-evict.

```fsharp
/// A bounded-recently-used ordering over string keys. The head is the most recently used; the
/// tail is the eviction candidate. It knows nothing about bytes — the budget is the caller's
/// (`Assets`), which owns the size of what it caches.
type Lru =
    { Order : LinkedList<string>
      Nodes : Dictionary<string, LinkedListNode<string>> }

val create   : unit -> Lru
val contains : lru:Lru -> key:string -> bool
val touch    : lru:Lru -> key:string -> unit   // move to head; add if absent
val remove   : lru:Lru -> key:string -> unit
val tail     : lru:Lru -> string option        // least recently used, if any
val keys     : lru:Lru -> string list          // most-recently-used first
val count    : lru:Lru -> int
```

## `Texture` — byte size for accounting (`render/Texture.fs`)

```fsharp
[<Struct>]
type Texture =
    { Device : Gpu.Device
      Handle : Gpu.Texture
      Sampler : Gpu.Sampler
      // The GPU bytes this texture occupies: level 0 (W*H*4) plus its mip chain (~4/3 of that).
      // Captured at load so the budget can be accounted; the 1x1 placeholder is 4.
      Bytes : int64 }
```

## `EnvironmentAsset` — one loaded unit (`world/EnvironmentAsset.fs`)

Metadata is always present; the whole loaded unit is an option (research D6).

```fsharp
/// One submesh, its parsed geometry and (while loaded) its uploaded buffers together. The
/// geometry is retained only for the life of the load — physics reads it at collider-build time,
/// which is always after the ensure.
[<Struct>]
type LoadedSubmesh =
    { Positions : float32 array
      Normals : float32 array
      Uvs : float32 array
      Indices : int array
      MaterialIndex : int
      Mesh : StaticMesh           // GPU buffers
      Material : Texture.Material } // GPU bindings

/// Everything that becomes resident when an environment is loaded and is dropped when it is
/// evicted: the geometry, the parts, the source materials, the GPU submeshes, the unit's byte
/// size, and the shared texture keys it acquired.
type EnvironmentLoaded =
    { Submeshes : LoadedSubmesh array
      Parts : EnvironmentPart array
      Bounds : Gltf.Bounds
      Materials : Gltf.Material array
      SubmeshBytes : int64
      TextureKeys : string array }

type EnvironmentAsset =
    { Id : string
      Name : string
      ModelPath : string                     // absolute, resolved once at scan
      Orientation : Quaternion
      mutable Loaded : EnvironmentLoaded option } // None == not loaded (never parsed/uploaded)
```

`EnvironmentPart` is unchanged (`world/EnvironmentAsset.fs:26`). Accessors assert the unit is
present (FR-011), so a render or physics path over a not-loaded asset fails loudly:

```fsharp
val submeshes : asset:EnvironmentAsset -> LoadedSubmesh array
val parts     : asset:EnvironmentAsset -> EnvironmentPart array
val bounds    : asset:EnvironmentAsset -> Gltf.Bounds
val materials : asset:EnvironmentAsset -> Gltf.Material array
```

The CPU geometry and the GPU buffers are the same array element (`LoadedSubmesh`), so physics
reads `sub.Positions` and the batch reads `sub.Mesh` from one array — no parallel arrays and
no CPU/GPU accessor split. Prop assets are built eagerly at scan (`Loaded = Some`) and are not
governed by the budget.

## `AssetManager` and the texture cache (`world/Assets.fs`)

The `Loader` (texture cache + placeholder) is created lazily so a device-free scan never
allocates a texture; its cache is refcounted.

```fsharp
/// A texture the loader created, with the number of loaded assets currently binding it.
type TextureEntry =
    { Texture : Texture.Texture
      mutable Refs : int }

type Loader =
    { Device : Gpu.Device
      White : Texture.Texture            // permanent; not refcounted
      DefaultMaterial : Texture.Material // permanent
      Textures : Dictionary<string, TextureEntry> }

type AssetManager =
    { Device : Gpu.Device
      Root : string
      Actors : Dictionary<string, ActorAsset.ActorAsset>
      Props : Dictionary<string, EnvironmentAsset.EnvironmentAsset>
      Environments : Dictionary<string, EnvironmentAsset.EnvironmentAsset>
      Loader : Lazy<Loader>              // created on first use
      Cache : Lru.Lru                    // residency order, by asset id
      mutable ResidentBytes : int64 }    // sum of loaded units' bytes + live textures
```

### Budget

```fsharp
/// Bound on environment residency. Bounds the *whole loaded unit* — parsed geometry plus GPU
/// meshes and textures — so it caps process RAM as well as VRAM. Sized for the measured worst
/// case: ArtRoom is ~1.68 GB of textures alone; its parsed geometry is ~2 MB. A byte budget
/// (not a count) keeps one heavy environment plus many light ones resident and caps growth.
[<Literal>]
let ENVIRONMENT_BUDGET_BYTES : int64 = 2048L * 1024L * 1024L   // 2 GB
```

### Residency operations

```fsharp
/// True when the asset's loaded unit is present. Pure; drives the editor glyph.
val isEnvironmentResident : manager:AssetManager -> assetId:string -> bool

/// Make an environment asset loaded: parse its model if needed, upload its buffers and resolve
/// its materials (acquiring their textures), insert it at the LRU head, then evict tails while
/// over budget, skipping every id in `protectedIds` (the caller passes the asset being loaded,
/// which — since every load is for the zone becoming active — is the environment on screen; a
/// future non-active load must also pass the active environment's id). A cache hit only
/// touches. Raises if the asset or its model file is missing/corrupt, before any state changes,
/// so a failed load cancels cleanly.
val ensureEnvironmentResident : manager:AssetManager -> assetId:string -> protectedIds:string list -> unit

/// Free a loaded unit: release its meshes, release its acquired textures (free each at refcount
/// zero), subtract its bytes, drop it (geometry is collectable), and remove it from the cache.
val releaseEnvironment : manager:AssetManager -> assetId:string -> unit
```

The eviction *decision* is factored pure for testing (research D9):

```fsharp
/// The eviction order: the LRU order walked from the tail, skipping every id in `protectedIds`.
/// Pure; the caller releases them, stopping once the bytes actually freed are within budget.
val evictionCandidates : order:string list -> protectedIds:string list -> string list
```

## Residency state machine (per environment asset)

| State | `Loaded` | In LRU | Reachable by |
|---|---|---|---|
| **Discovered** (after metadata-only scan) | `None` | no | `Assets.scan` |
| **Loaded** | `Some` | yes (a node) | `ensureEnvironmentResident` |

- Transitions: Discovered →(ensure)→ Loaded (parse then upload); Loaded →(ensure, hit)→ Loaded
  (touch only); Loaded →(release/evict)→ Discovered (a reload re-parses).
- `Loaded = None` is the single source of truth; there is no "parsed but not resident" or
  "resident but unparsed" state, because the geometry and the GPU data are one unit.
- A zone with no environment has no asset and never enters this machine (spec edge case).

## Validation rules

- **The resident set is under budget, except the environment in use.** After every ensure,
  `ResidentBytes <= budget` unless the single resident asset is protected; the caller passes
  the environment becoming active, so it is never evicted (FR-001, FR-005, SC-001).
- **The budget bounds parsed and GPU memory together.** A unit's `SubmeshBytes` counts its
  retained geometry and its vertex/index buffers; the budget adds a texture's bytes only while
  its refcount is positive, so a shared texture counts once (FR-001, FR-018, SC-010).
- **Eviction frees the whole unit.** `releaseEnvironment` releases the meshes and textures and
  drops `Loaded`; it never touches a zone's entities, bodies, the batch, the registry or undo,
  because those live elsewhere (FR-006, SC-003/SC-004).
- **Reload is identical.** A reload re-parses the same model and re-uploads it, producing the
  same geometry, materials and bytes (FR-007, SC-003).
- **A failed ensure changes nothing.** `ensureEnvironmentResident` raises before any mutation
  of the caller's state, so a switch/instantiate is cancelled (FR-017, SC-009).
- **One residency per asset id.** Two zones (or the same asset twice) resolve to one `Cache`
  entry and one `Loaded` (FR-008).
- **Metadata-only at startup.** After `scan`, every environment asset has `Loaded = None` and
  `Cache` is empty (FR-010, SC-005).
- **No non-resident read.** The `submeshes`/`parts`/`bounds`/`materials` accessors assert the
  unit is present; a render path over a not-loaded asset fails loudly (FR-011).
- **Shutdown frees once.** `Assets.release` releases every loaded environment and prop (guarded
  on `Loaded = Some`), then frees every texture (via the refcount) and the placeholder (FR-012).
