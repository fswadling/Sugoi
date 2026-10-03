# Contract: Bounded environment residency

The interfaces this feature touches, and what explicitly does not change. The residency
machinery sits behind three call sites (two ensure hooks and the Outliner read); the render
pipelines, the batch, undo and the registry are untouched.

## `core/Lru.fs` — NEW

A bounded-recently-used ordering over string keys (data-model.md, research D3):

```fsharp
type Lru = { Order : LinkedList<string>; Nodes : Dictionary<string, LinkedListNode<string>> }
val create   : unit -> Lru
val contains : lru:Lru -> key:string -> bool
val touch    : lru:Lru -> key:string -> unit
val remove   : lru:Lru -> key:string -> unit
val tail     : lru:Lru -> string option
val keys     : lru:Lru -> string list
val count    : lru:Lru -> int
```

Compiled after `core/SlotMap.fs` in `fsharp-game.fsproj`.

## `render/Texture.fs` — CHANGED

`Texture` records its GPU byte size (data-model.md); its constructors and `release` are
otherwise unchanged:

```fsharp
[<Struct>]
type Texture = { Device : Gpu.Device; Handle : Gpu.Texture; Sampler : Gpu.Sampler; Bytes : int64 }
```

## `world/EnvironmentAsset.fs` — CHANGED

One loaded unit behind a single option (data-model.md, research D6). `EnvironmentPart` is
unchanged.

```fsharp
[<Struct>] type LoadedSubmesh =
    { Positions; Normals; Uvs; Indices; MaterialIndex; Mesh : StaticMesh; Material : Texture.Material }
type EnvironmentLoaded =
    { Submeshes : LoadedSubmesh array; Parts; Bounds; Materials; SubmeshBytes : int64; TextureKeys : string array }
type EnvironmentAsset =
    { Id; Name; ModelPath; Orientation
      mutable Loaded : EnvironmentLoaded option }

// Asserting accessors (FR-011):
val submeshes : asset:EnvironmentAsset -> LoadedSubmesh array
val parts     : asset:EnvironmentAsset -> EnvironmentPart array
val bounds    : asset:EnvironmentAsset -> Gltf.Bounds
val materials : asset:EnvironmentAsset -> Gltf.Material array
```

Physics reads `LoadedSubmesh.Positions`/`Indices`; the batch reads `LoadedSubmesh.Mesh` — from
the one array, through the accessor. There is no CPU/GPU accessor split.

## `world/Assets.fs` — CHANGED (the bulk)

```fsharp
[<Literal>] val ENVIRONMENT_BUDGET_BYTES : int64 = 2048L * 1024L * 1024L

val scan : root:string -> device:Gpu.Device -> AssetManager   // metadata only for environments

// Resident accessor (asserts presence; FR-011):
val submeshes : manager:AssetManager -> assetId:string -> LoadedSubmesh array

// Residency:
val isEnvironmentResident     : manager:AssetManager -> assetId:string -> bool
val ensureEnvironmentResident : manager:AssetManager -> assetId:string -> protectedIds:string list -> unit
val releaseEnvironment        : manager:AssetManager -> assetId:string -> unit
val evictionCandidates : order:string list -> protectedIds:string list -> string list
```

- `scan` records the manifests of environments and props; environments are left `Loaded = None`;
  props are still built eagerly (out of the budget).
- `ensureEnvironmentResident` parses the model (if `Loaded = None`), uploads buffers and resolves
  materials (acquiring textures from `Loader.Textures`), computes `SubmeshBytes`, sets `Loaded`,
  touches `Cache`, then walks `evictionCandidates (Lru.keys Cache) protectedIds` via
  `releaseEnvironment`, stopping once the bytes `releaseEnvironment` actually frees bring
  `ResidentBytes` within budget. The caller passes the asset being loaded — the environment
  becoming active — so the environment on screen is never evicted; a future load for a non-active
  zone must also pass the active environment's id.
- `releaseEnvironment` releases meshes and textures, subtracts bytes, drops `Loaded`, removes the key.
- `Assets.release` (shutdown) releases every loaded environment and prop, then every texture.

`AssetManager` gains `Root`, `Loader : Lazy<Loader>`, `Cache`, `mutable ResidentBytes`; the loader's
cache is refcounted (data-model.md). Every construction site (including test fixtures) is updated.

## `world/Zone.fs` — CHANGED

`setEnvironment` ensures the incoming environment is loaded **before** it mutates, so a failed
load cancels the action (FR-017) and a restore after eviction reloads (research D7):

```fsharp
let setEnvironment zone world assets environment =
    match environment with
    | Some incoming -> do Assets.ensureEnvironmentResident assets incoming.AssetId [ incoming.AssetId ]
    | None -> ()
    do deactivateEnvironment zone world
    ...
```

`loadEnvironment`/`loadEnvironmentFrom` also call `ensureEnvironmentResident` before
`assetPartStates` reads the asset's `Parts` (the load must precede the build, which
`setEnvironment` runs after). `meshCollider`/`propCollider`/`attachPropPhysics` read
`LoadedSubmesh` geometry via `Assets.submeshes`; `submeshRangeBounds` is computed during the parse.

## `state/GameplayState.fs` — CHANGED

`swapZone` ensures the incoming zone's environment **before** it becomes active (FR-002,
FR-017, research D7):

```fsharp
let private swapZone shared (state: byref<GameplayState>) (outgoing: string) (next: string) =
    match state.Zones.[next].Environment with
    | Some environment -> do Assets.ensureEnvironmentResident shared.Assets environment.AssetId [ environment.AssetId ]
    | None -> ()
    do Zone.deactivatePhysics state.Zones.[outgoing] shared.Physics
    state.ActiveZone <- next
    ...
```

## `world/EnvironmentBatch.fs` — CHANGED

`gather`/`countInstance`/`fillInstance` resolve submeshes through the accessor instead of
`r.Asset.Submeshes`, so a not-loaded asset cannot be read; the grouping key stays the uploaded
vertex buffer. No buffer or draw change.

## `editor/Outliner.fs` — CHANGED (the zones row)

The zone row label gains a residency glyph, read through `Assets.isEnvironmentResident`
(research D8, FR-013–FR-016):

```fsharp
let glyph =
    match state.Zones.[name].Environment with
    | None -> "[-]"                                                                 // nothing to load
    | Some env when Assets.isEnvironmentResident shared.Assets env.AssetId -> "[+]" // loaded
    | Some _ -> "[ ]"                                                               // not loaded
if ImGui.button(sprintf "%s%s%s##zone_%s" marker glyph name name, ImGui.ImVec2(0.0f, 0.0f)) then ...
```

No new binding, panel, column or layout. The existing active marker (`"* "`), click-to-select
and delete control are unchanged.

## `fsharp-game.fsproj` / `test/fsharp-game.Tests.fsproj` — CHANGED

`core/Lru.fs` added after `core/SlotMap.fs`; `test/TestSupport.fs` (first) and `test/LruTests.fs`
added to the test compile list.

## Explicitly unchanged

- **Shaders and pipelines** — the static/cel/outline SPIR-V and the pipeline descriptors do not
  change; the same buffers are bound, they are merely absent while not loaded.
- **`EnvironmentBatch`'s buffers and draws** — the per-zone instance buffer, grouping and draw
  calls are unchanged; only where the submesh is resolved from differs.
- **Undo, the registry, the cast, dialogue and the cue** — residency is not a scene edit; no
  command is added, and a zone's contents are unaffected by eviction (FR-006).
- **Actor and prop asset loading** — actors and props keep their current eager load; only
  environment assets are bounded here (spec Assumptions).
- **The budget's application** — there is no settings UI; the constant is the single knob
  (FR-009, spec Assumptions).
- **Persistence** — nothing is written to disk (ROADMAP phase 15).
