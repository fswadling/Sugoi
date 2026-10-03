# Tasks: Bounded environment residency

**Input**: Design documents from `/specs/050-environment-lru-cache/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: The plan's `Testing` section asks for the pure part (the LRU, the residency reader,
the eviction decision and the metadata-only scan) to be covered headlessly, and the repo's
convention is a test per pure capability, so those tests are included. The parse/upload, the
budget under real assets, the texture refcount and the editor glyph need a GPU device and are
the quickstart walkthrough — this port cannot open a device in tests.

**Organization**: Tasks are grouped by user story. The residency mechanism (the `Lru`, the
single loaded unit on `EnvironmentAsset`, `Assets`'s load/evict machinery and the refcounted
texture cache) is shared foundation (Phases 1–2). **US1** adds the byte budget and eviction
(the bound); **US2** adds the ensure hooks (load on switch/instantiate); **US3** is the
whole-unit-eviction and simulation-unaffected confirmation; **US4** is the editor glyph;
**US5** is the metadata-only-startup confirmation.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US5)
- Include exact file paths in descriptions

## Path Conventions

Single-project F# repository. Paths are relative to the repository root (`fsharp-game/`):

- `core/` — data structures; `render/` — GPU layer and ImGui bindings; `world/` — game-domain
  modules; `state/` — shared/editor state and commands; `editor/` — editor panels; `screen/` —
  the screens; `test/` — the xUnit project; `specs/` / `docs/` — documentation.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The two pieces every later task needs — the LRU ordering and the texture byte size.

- [X] T001 [P] Create `core/Lru.fs`: `type Lru = { Order : LinkedList<string>; Nodes : Dictionary<string, LinkedListNode<string>> }` and `create` / `contains` / `touch` (move to head, add if absent) / `remove` / `tail` / `keys` / `count`, over `System.Collections.Generic`. Add `<Compile Include="core/Lru.fs" />` after `core/SlotMap.fs` in `fsharp-game.fsproj`. (data-model.md; research D3.)
- [X] T002 [P] In `render/Texture.fs`, add `Bytes : int64` to the `Texture` struct; set it in `Texture.load` (`W*H*4*mips`) and `Texture.solid` (`4`). `release` is unchanged. (data-model.md; research D4.)

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: The single loaded unit on `EnvironmentAsset`, the `Assets` load/evict machinery and the resident accessors must exist before any user story can be built or tested.

- [X] T003 In `world/EnvironmentAsset.fs`, replace the types with the **single loaded unit** (data-model.md; research D6): `[<Struct>] LoadedSubmesh { Positions; Normals; Uvs; Indices; MaterialIndex; Mesh : StaticMesh; Material : Texture.Material }`; `EnvironmentLoaded { Submeshes : LoadedSubmesh array; Parts : EnvironmentPart array; Bounds : Gltf.Bounds; Materials : Gltf.Material array; SubmeshBytes : int64; TextureKeys : string array }`; `EnvironmentAsset` gains `mutable Loaded : EnvironmentLoaded option` (metadata `Id`/`Name`/`ModelPath`/`Orientation` unchanged). `EnvironmentPart` is unchanged. Add asserting accessors `submeshes` / `parts` / `bounds` / `materials` (FR-011). There is **no** CPU/GPU split and no Cpu/Gpu options.

- [X] T004 In `world/Assets.fs`, build the residency machinery (data-model.md; research D2, D4, D5):
  - `AssetManager` gains `Root`, `Loader : Lazy<Loader>`, `Cache : Lru.Lru`, `mutable ResidentBytes : int64`; the loader's cache becomes `Dictionary<string, TextureEntry>` (`TextureEntry = { Texture; mutable Refs : int }`) with acquire (increment; add bytes on 0→1) and release (decrement; free + subtract on 1→0). `White`/`DefaultMaterial` permanent.
  - `scan` becomes metadata-only for environments (`Loaded = None`, `ModelPath` absolute); props are still built eagerly through the same builder (`Loaded = Some`, out of the budget).
  - One build path: parse the glTF into `LoadedSubmesh` (geometry + uploaded `StaticMesh`) and resolve each material (acquiring its textures), producing an `EnvironmentLoaded` with its `SubmeshBytes` and `TextureKeys`.
  - Add `[<Literal>] ENVIRONMENT_BUDGET_BYTES = 2048L * 1024L * 1024L`; `isEnvironmentResident`; `ensureEnvironmentResident manager assetId protectedIds` (load if `Loaded = None`, touch `Cache` — eviction is US1 — never evict an id in `protectedIds`); `releaseEnvironment` (release meshes and textures, subtract bytes, drop `Loaded`, remove from `Cache`); the `submeshes` accessor by id (asserts presence; FR-011).
  - `Assets.release` releases every loaded environment and prop (guarded on `Loaded = Some`) before the textures/actors/props, and is guarded by `Loader.IsValueCreated`. (Depends on T001, T003.)

- [X] T005 In `world/Zone.fs`, route the CPU-side reads through the accessors: `meshCollider`/`propCollider`/`attachPropPhysics` read `LoadedSubmesh` via `Assets.submeshes`, and `assetPartStates` reads `EnvironmentAsset.parts`. (Depends on T003, T004.)

- [X] T006 In `world/EnvironmentBatch.fs`, resolve a renderer's submeshes through the resident accessor instead of `r.Asset.Submeshes` in `countInstance`/`fillInstance`, so a not-loaded asset cannot be read; the grouping key and buffers are unchanged. (Depends on T003, T004.)

- [X] T007 [P] Create `test/TestSupport.fs` (compiled **first** in `test/fsharp-game.Tests.fsproj`): device-free fixtures that build a `LoadedSubmesh`/`EnvironmentLoaded`/`EnvironmentAsset` in the **loaded** shape and a device-free `AssetManager`. Update the `Assets.AssetManager`/`EnvironmentAsset` literals across `test/AssetsTests.fs`, `test/GameplayStateTests.fs`, `test/SpawningTests.fs`, `test/SelectionTests.fs`, `test/CueTests.fs`, `test/CuePanelTests.fs`, `test/DialogueTests.fs`, `test/PhysicsTests.fs`, `test/ZoneTests.fs` to use them. (Depends on T003, T004.)

- [X] T008 [P] Create `test/LruTests.fs` and add it (after `TestSupport.fs`) to the compile list in `test/fsharp-game.Tests.fsproj`: `touch` moves to head and adds when absent; `tail` is the least recently used; `remove`/`contains`/`count`. (Depends on T001.)

**Checkpoint**: `dotnet build` and `dotnet test` are green; environments load on instantiation and are never evicted; nothing is bounded yet.

---

## Phase 3: User Story 1 - A bounded environment footprint no matter how many zones exist (Priority: P1) 🎯 MVP

**Goal**: the loaded environment footprint (parsed geometry plus GPU data) stays under the byte budget, evicting the least-recently-used first and never the environment in use.

**Independent Test**: with a budget below the combined size of the environments in use, visit them in turn and confirm the loaded set never exceeds the budget, the least-recently-used is the one dropped, and the environment in use is never dropped (quickstart §4).

### Implementation for User Story 1

- [X] T009 [US1] In `world/Assets.fs`, add the byte budget: a pure `evictionCandidates : order:string list -> protectedIds:string list -> string list` (the LRU order from the tail minus the protected ids), and in `ensureEnvironmentResident` account `ResidentBytes` (the unit's `SubmeshBytes`, which now includes the parsed geometry, plus a texture's bytes on 0→1 acquire), then walk `evictionCandidates` via `releaseEnvironment`, stopping once the bytes actually freed bring `ResidentBytes` within budget and skipping every id in `protectedIds` (the incoming asset, which is the environment becoming active) so the environment in use is never evicted (FR-001, FR-004, FR-005). (Depends on T004.)
- [X] T010 [US1] In `test/AssetsTests.fs`, assert: `evictionCandidates` walks the LRU order from the tail (least recently used first) and skips `protected`; an asset id used by two zones yields one `Cache` entry and is loaded once (FR-008). (Depends on T009.)

**Checkpoint**: User Story 1 is functional on its own — the loaded set is bounded and the active environment is never its own eviction. This is the MVP.

---

## Phase 4: User Story 2 - Environments load on demand and render correctly (Priority: P1)

**Goal**: switching to, or instantiating, a zone whose environment is not loaded loads it then; a reload after eviction renders identically; a failed load cancels the action.

**Independent Test**: switch to a zone, evict its environment by visiting others, switch back, and confirm it renders again with no difference; make an environment unloadable and confirm the switch does not happen (quickstart §4, §5).

### Implementation for User Story 2

- [X] T011 [US2] In `world/Zone.fs`, ensure the incoming environment in `setEnvironment` (the `Some` case, `protectedIds = [ incoming.AssetId ]`) **before** `deactivateEnvironment`, and in `loadEnvironment`/`loadEnvironmentFrom` before `assetPartStates` reads the asset's parts (the load must precede the build), so a freshly built environment, the asset browser's `SetEnvironment` command and `History.undo`'s `occupyZone` restore all load in time and a failed load cancels before any mutation (FR-002, FR-003, FR-017; research D7). The incoming asset is the environment becoming active, so it is the active zone's environment; a future non-active load must also add the active environment's id to `protectedIds`. (Depends on T004, T005.)
- [X] T012 [US2] In `state/GameplayState.fs`, `swapZone` ensures the incoming zone's environment, passing `[ environment.AssetId ]` as `protectedIds`, **before** `deactivatePhysics`/`ActiveZone <- next`, so a switch to an evicted environment reloads and a failed load leaves the current zone active (FR-002, FR-017; research D7). The incoming environment is becoming the active one, so this protects the environment on screen. (Depends on T004.)
- [X] T013 [US2] In `test/GameplayStateTests.fs` / `test/AssetsTests.fs`, assert: `isEnvironmentResident` is false for a not-loaded asset and true for a loaded one; a switch whose ensure raises (a missing environment asset) leaves `ActiveZone` and the outgoing zone unchanged (FR-002, FR-007, FR-017). (Depends on T011, T012.)

**Checkpoint**: an environment loads exactly when it becomes current, and a broken asset cannot strand the project half-changed.

---

## Phase 5: User Story 3 - Unloading never changes what the game simulates (Priority: P2)

**Goal**: evicting frees the whole loaded unit (geometry and GPU data together) yet a zone's colliders, parts, undo and registry are unaffected, and a shared texture is freed only when neither environment needs it.

**Independent Test**: with a low budget, alternate zones and confirm prop collision, picking, the registry and undo/redo are identical to a run that never evicts; two environments sharing a texture file both render after either is unloaded (quickstart §1, §4).

### Implementation for User Story 3

- [X] T014 [US3] In `world/Assets.fs`, make `releaseEnvironment` drop the whole unit: release the meshes and textures, subtract the unit's bytes, set `Loaded <- None` (freeing the geometry to GC), and remove it from `Cache`; it must not touch any zone entity, body, the batch, the registry or undo. This call touches the GPU (mesh/texture release), so it is validated by the quickstart (`quickstart.md` §4, §6), not by a test task. (Depends on T004, T009.)
- [X] T015 [US3] Verify and record: after a `releaseEnvironment` a zone's placed parts, colliders and `captureZone`/`occupyZone` round-trip are unchanged, and a reload rebuilds identical submeshes/materials/bytes from a fresh parse. Add cases to `test/ZoneTests.fs` if a gap shows. Note honestly that the texture refcount (free-at-zero) needs a GPU and is the quickstart's step (§4). (Depends on T004, T014.)

**Checkpoint**: eviction is invisible to the simulation and safe across assets that share a file.

---

## Phase 6: User Story 4 - Residency at a glance in the editor (Priority: P2)

**Goal**: each zone in the Outliner shows whether its environment is loaded.

**Independent Test**: with a budget that forces eviction, switch between zones and watch the glyph beside each change between loaded and not-loaded, matching which zones render immediately (quickstart §3).

### Implementation for User Story 4

- [X] T016 [US4] In `editor/Outliner.fs`, fold a residency glyph into each zone row's existing label via `Assets.isEnvironmentResident`: `"[+]"` loaded, `"[ ]"` not loaded, `"[-]"` no environment; no new ImGui binding, panel, column or layout; the active marker, click-to-select and delete control are unchanged (FR-013–FR-016). (Depends on T004.)
- [X] T017 [US4] In `test/AssetsTests.fs`, assert `isEnvironmentResident` is false for a not-loaded asset and true for a loaded one (the no-environment case is the glyph's `None` branch, T016). (Depends on T004.)

**Checkpoint**: the developer can tell at a glance which zones are loaded, with no manual refresh.

---

## Phase 7: User Story 5 - Startup no longer reads or uploads every environment (Priority: P3)

**Goal**: launching performs no environment parse or upload, while every environment is still listed.

**Independent Test**: launch with several environments present and confirm none is parsed or uploaded before first use, while the asset browser and registry still list every one (quickstart §2).

### Implementation for User Story 5

- [X] T018 [US5] Verify and record: after `Assets.scan`, every environment has `Loaded = None` and `Cache` is empty; nothing parses or uploads before a switch/instantiate; the asset browser and the registry still list every environment by name. Assert the post-scan state headlessly (a manifest with a **missing** model file must not raise). (Depends on T004.)

**Checkpoint**: launch cost is independent of environment count and size.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T019 [P] Update `docs/ENVIRONMENT_LRU_PLAN.md` to record what was implemented (the single loaded unit and why the split was removed, the byte budget as shipped, the refcounted texture cache, the ensure hooks and load-before-commit, the Outliner glyph) and note the deferred items (async, cue preload, prop/actor residency, texture compression).
- [X] T020 Run `dotnet build` and `dotnet test`, then the [quickstart.md](./quickstart.md) walkthrough (§1–§6) and record the outcome, including the lower-budget thrash and the memory-held check (§4.3), and the broken-asset abort. The rendered result and the glyph need a person and a GPU. (Validated by the user on 2026-10-03 against quickstart §2–§6.)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies. T001 and T002 are independent files.
- **Foundational (Phase 2)**: T003 needs T001/T002; T004 needs T001/T003; T005/T006/T007 need T004; T008 needs T001. Blocks all stories.
- **US1 (Phase 3)**: T009 needs T004; T010 needs T009. This is the MVP.
- **US2 (Phase 4)**: T011 needs T004/T005; T012 needs T004; T013 needs T011/T012.
- **US3 (Phase 5)**: T014 needs T004/T009; T015 needs T004/T014.
- **US4 (Phase 6)**: T016/T017 need T004 only — independent of US1–US3.
- **US5 (Phase 7)**: T018 needs T004 only.
- **Polish (Phase 8)**: T019/T020 last.

### Same-file constraint (sequential)

- `world/Assets.fs` — T004 → T009 → T014.
- `world/Zone.fs` — T005 → T011 → T021.
- `test/AssetsTests.fs` — T010 → T013 → T014 → T017 (T007 also touches it, earlier).
- `test/GameplayStateTests.fs` — T007 → T013.
- `core/Lru.fs` (T001, T008), `render/Texture.fs` (T002), `world/EnvironmentAsset.fs` (T003),
  `world/EnvironmentBatch.fs` (T006), `state/GameplayState.fs` (T012), `editor/Outliner.fs`
  (T016) each get their edits in one task.

### Parallel Opportunities

- **T001 ∥ T002** — the LRU and the texture byte size are independent.
- **T005 ∥ T006** — after T004, `Zone`'s accessor reads and the batch's accessor are different files.
- **T007 ∥ T008** — the fixtures and the LRU tests are different files.
- **T016 ∥ T009/T011** — the Outliner glyph and its reader test depend only on T004.
- Test files across stories (`LruTests`, `ZoneTests`, `SpawningTests`) are independent once their subject lands.

### Notes

- **One unit is the contract.** An environment's parsed geometry and its GPU data are present or
  absent together (`Loaded`), so a render or physics path over a not-loaded asset fails loudly
  (FR-011) and eviction bounds RAM and VRAM with one budget. The earlier CPU/GPU split was
  removed because the CPU half is small (~2 MB/env) and a re-parse is ~10 ms (research D6).
- **Buying the budget honestly.** `ResidentBytes` must be the *actual* footprint: a shared
  texture is charged once (0→1) and freed once (1→0) (T004, T014), or it would be
  double-counted or double-freed (FR-001, FR-018).
- **Load before commit** (T011, T012): the ensure must run before any state mutates, so a failed
  load cancels the action rather than stranding a half-changed switch (FR-017).
- **No per-frame touch.** Eviction only happens inside the ensure, and every ensure is for the
  environment becoming current, so the just-loaded asset is always the LRU head and never its
  own victim (research D7).
- **Props and actors stay eager** (spec Assumptions): only environment assets are bounded here.
- **No new dependency, no shader, no command.** `Lru` is ~30 lines over the BCL; the render
  pipelines, the batch buffers, undo and the registry are unchanged.
- **The GPU-touching half is the quickstart.** `releaseEnvironment`'s mesh/texture release, the
  texture refcount (FR-018), shutdown release-once (FR-012) and the no-leak steady state
  (SC-006) need a device the headless suite cannot open; they are validated by
  [quickstart.md](./quickstart.md) §4 and §6, deliberately not by a test task.

### Implementation status (2026-10-03)

- **T001–T019: done.** `dotnet build` is clean (0 warnings) for both projects and `dotnet test`
  passes **412 tests**, now against the **single loaded unit** (`EnvironmentLoaded` behind one
  option) — `EnvironmentAsset`, `Assets`, `Zone`, `EnvironmentBatch`, the Outliner glyph and the
  `test/TestSupport.fs` loaded-shape fixtures were all reworked, and the old CPU/GPU split types
  (`EnvironmentCpu`/`EnvironmentGpu`, `cpuSubmeshes`/`residentSubmeshes`) are gone.
- **T020: done — the GPU walkthrough** (`quickstart.md` §2–§6) was run by the user on 2026-10-03 and
  passed. The texture refcount (FR-018), release-once (FR-012) and the no-leak steady state (SC-006)
  are exercised there, not headlessly.
- **T021 (Phase 9 convergence): done.** `world/Zone.fs` now protects the environment being replaced
  (`protectedForLoad` includes `zone.Environment`) on the `setEnvironment`/`loadEnvironment`/
  `loadEnvironmentFrom` ensure, so the still-in-use active environment cannot be evicted before a
  replacement commits (FR-005). `dotnet build` is clean (0 warnings) and `dotnet test` passes **412
  tests**.

---

## Phase 9: Convergence

- [X] T021 In `world/Zone.fs`, protect the outgoing environment when it is replaced: pass the current `zone.Environment` asset id in `protectedIds` alongside the incoming one in `loadEnvironment` (≈L814), `loadEnvironmentFrom` (≈L833) and `setEnvironment` (≈L748), mirroring the `swapZone` fix, so the still-in-use active environment cannot be evicted before the replacement commits and a failure after that eviction cannot strand the active zone on a non-resident environment per FR-005 (partial)
