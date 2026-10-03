# Tasks: Async asset loading

**Input**: Design documents from `/specs/053-async-asset-loading/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/environment-loading.md, quickstart.md

**Tests**: Not TDD. Device-dependent behaviour (the worker pool, the payload handoff, the budgeted
GPU build) cannot be exercised by the suite — no device can be opened headlessly (AGENTS.md; every
rendering spec) — so it is verified by the GPU walkthrough in `quickstart.md` with
`FFS_LOAD_TIMING=1`. The **412-test suite is the regression gate** and must stay green (0 warnings,
both projects). One new headless test file is generated for **US3**, because the pending-set policy
(bound / de-duplication / recency / pinning / drop) and the concurrent-decode bound are pure and
FR-009/SC-004 depend on them.

**Organization**: Tasks are grouped by user story. The CPU/GPU split, the decoded payload, the bounded
recency queue and the worker pool are shared foundation (Phases 1–2) and block every story.
**US1** (P1) makes a zone switch non-blocking; **US2** (P1) is the off-thread mechanism it rests on;
**US3** (P2) bounds and prioritises a burst; **US4** (P2) is the readout; **US5** (P2) is failure.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US5)
- Include exact file paths in descriptions

## Path Conventions

Single-project F# repository. Paths are relative to the repository root (`fsharp-game/`):

- `core/`, `render/` (GPU layer), `world/` (game-domain), `state/`, `editor/`, `screen/`,
  `test/`, `specs/`, `docs/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the new module's place in the compile order and the three constants the feature turns on.

- [X] T001 In `fsharp-game.fsproj`, add `world/AssetLoad.fs` **after** `world/EnvironmentRender.fs` and **before** `world/Assets.fs` (it depends on `core/Gltf.fs` and `render/Texture.fs`, both compiled earlier, and `world/Assets.fs` depends on it); create `world/AssetLoad.fs` with its module header, the `AssetId`/`ModelPath`/`TextureKey` aliases, and the constants `loadWorkers = 2`, `LoadQueueCapacity = 8` and the `FFS_LOAD_WORKERS` override, and add the matching `UploadBudgetMs = 4.0` to `world/Assets.fs`. Clamp the `FFS_LOAD_WORKERS` override to ≥ 1 (spec Edge Cases — the pool is never empty). (plan.md Project Structure; research D6.)

**Checkpoint**: the module exists, is compiled in the right place, and both projects build.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: the payload, the queue, the pool, the texture split and the state DUs must exist
before any user story can be implemented or verified.

- [X] T002 [P] In `render/Texture.fs`, declare `DecodedImage = { Width : int; Height : int; Pixels : byte array }` and **split `load`**: `decode : path -> DecodedImage` (CPU-only: `preloadImageDep`, `IMG_Load`, format normalise, row compaction — no device, no batch) and `createFromDecoded : batch -> device -> DecodedImage -> Texture` (GPU texture + sampler + record the level-0 copy via `Gpu.recordTexture`, mips when levels > 1). Keep the format, sampler, mip levels and `Bytes` byte-for-byte identical to today's `load`. Delete `load`. (data-model.md; contracts; research D4.)
- [X] T003 In `world/AssetLoad.fs`, add `DecodedEnvironment` (`AssetId`, `Model : Gltf.Model`, `Images : Dictionary<TextureKey, Texture.DecodedImage>`, `ParseNs`, `DecodeNs`), `LoadOutcome = Decoded | LoadFailed of AssetId * Message`, and `decodeEnvironment : AssetId -> ModelPath -> LoadOutcome` (`Gltf.load`, then decode every **distinct** texture path any material references, deduped; a throw is caught and returned as `LoadFailed`). Reference `Texture.DecodedImage`; do **not** re-declare it. (Depends on T002; research D2/D14; FR-002.)
- [X] T004 In `world/AssetLoad.fs`, add `LoadQueue`: an `Lru` (head = most recently requested) under one lock, with `Claimed`, `Pinned` and a single `mutable Uploading : string option`, and the operations `createQueue` / `request` / `setPinned` / `beginUpload` / `endUpload` / `claimNext` / `isPending` / `isClaimed` / `makeResident`. `request` de-duplicates (touches to the head) and drops the least-recently-requested tail that is neither pinned nor the `Uploading` id (a claimed/decoding tail **is** droppable — its decode is not cancelled); `makeResident` clears `Pending`, `Claimed` and `Uploading`; the claim is held from `claimNext` until then, so an asset being uploaded is never decoded again. (Depends on T001; data-model.md `LoadQueue`; research D5/D5a; FR-005/FR-006.)
- [X] T005 In `world/AssetLoad.fs`, add `LoadPool`: `loadWorkers` **background** threads that wait on a `SemaphoreSlim`, `claimNext` an id (skipping ids no longer `isPending`), call an **injected** `decode` function (`AssetLoad.decodeEnvironment` in production), and post the `LoadOutcome` to a `Channel`; plus `createPool` (taking the `decode` function, seeded with the pin set) / `requestLoad` / `tryTakeReady` (non-blocking) / `inFlight` / `stopPool` (set stopping, wake, **bounded** join — a worker mid-decode is abandoned, which is safe as it never touches the device). Track `id → modelPath` for the claim. The `decode` seam lets the suite assert the FR-009 bound headlessly. (Depends on T003, T004; data-model.md `LoadPool`; contracts; research D6/D10.)
- [X] T006 [P] In `world/EnvironmentAsset.fs`, replace `mutable Loaded : EnvironmentLoaded option` with `mutable Residency : Residency` where `Residency = NotLoaded | Loading | Resident of EnvironmentLoaded | Failed of string`; change the resident accessors (`submeshes`/`parts`/`bounds`/`materials`) to match `Resident loaded` and otherwise `invalidOp` (same behaviour). (data-model.md; contracts; research D7/D12.)
- [X] T007 In `world/Entity.fs` and `world/Zone.fs`: add `PartsSource = FromAsset | Authored` and a `PartsSource` field to `Environment` (updated in every field-wise construction — `Zone.fs`, `test/EntityTests.fs`, `test/GameplayStateTests.fs`); replace the single `mutable Environment : Environment option` with `mutable EnvironmentState : ZoneEnvironment` where `ZoneEnvironment = NoEnvironment | Active of Environment | Awaiting of Environment`, and add a read-only `member Environment : Environment option` (the field cannot share the member's name — FS0023); update the write sites (`deactivateEnvironment`, `activateEnvironment`, `release`) and make `gatherStatic` pass the parts only for `Active`. (Depends on T006; data-model.md `ZoneEnvironment`/`PartsSource`; contracts; research D8/D12.)
- [X] T008 [P] In `render/Gpu.fs` and `render/Texture.fs`, make the instrumentation thread-safe: decode timing is carried in `DecodedEnvironment` (worker-measured) and no worker writes a process-global; `Gpu.UploadStats` stays render-thread-only. (Depends on T002, T003; plan.md Technical Context; data-model.md `DecodedEnvironment`.)
- [X] T009 In `world/Assets.fs`, replace `ensureEnvironmentResident` with `requestEnvironment` (resident → touch `Cache`; else lazy-create the pool, enqueue, set `Residency = Loading`, return) plus `setPinned` (**replaces** `PinnedIds`, seeding the queue) and `pump budgetMs`; split `buildLoaded` into the payload-based `uploadEnvironment` (a resumable cursor: create the next submesh's GPU mesh, resolve the next material from the payload's pixels on a cache miss, a **lazy** `UploadBatch` created only when the cursor becomes active, `finishUpload` and install `Resident` when complete, accounting `SubmeshBytes + TextureAdded`, touching `Cache` and evicting over `ENVIRONMENT_BUDGET_BYTES` against the pin set) and its inline use for the eager prop path; add `PinnedIds`, `Uploads` and `isLoading`/`hasFailed`; `release` stops the pool first. `isLoading`/`hasFailed` read `Residency` first and take the queue lock only when needed, and neither allocates (constitution V). (Depends on T002–T008; data-model.md; contracts; research D3/D5/D7.)
- [X] T010 Update `test/TestSupport.fs` for the DUs: `residentAsset` builds `Residency = Resident ...`; `manager` gains `Pool = None`, `Uploads = Dictionary()` and `PinnedIds = HashSet()`; `loader` unchanged. Also rewrite `test/AssetsTests.fs`, which uses `Loaded = None` (line 67) and the removed `ensureEnvironmentResident` (lines 82–83), to `Residency`/`requestEnvironment`. (Depends on T006, T007, T009.)
- [X] T011 Build `fsharp-game.fsproj` and `test/fsharp-game.Tests.fsproj` with **0 warnings** and run the full suite (**412 tests**). Follow the DU/split call-site fallout to green. (Depends on T009, T010.)

**Checkpoint**: the CPU/GPU split and the concurrent machinery are in place, the suite is green, and
a load is still requested only by the existing synchronous callers (nothing async is wired yet).

---

## Phase 3: User Story 1 - Switching zones never freezes the app (Priority: P1) 🎯 MVP

**Goal**: activating a non-resident environment returns immediately; the zone becomes active in that
frame and the environment pops in when its uploads land.

**Independent Test**: activate a non-resident environment and confirm the frame loop keeps running
(frames near budget, no ~5 s stall) and the environment renders when it lands (quickstart §2–§3).

### Implementation for User Story 1

- [X] T012 [US1] In `state/GameplayState.fs`, change `swapZone` to `requestEnvironment` and complete in-frame — no wait for a load; the pin set is refreshed every frame by `GameplayScreen`. Rewrite `test/GameplayStateTests.fs`'s "a switch to a zone whose environment is missing is cancelled" test: FR-004 supersedes load-before-commit, so a missing asset no longer cancels the switch. (Depends on T009; FR-004; research D8.)
- [X] T013 [US1] In `world/Zone.fs`, make `setEnvironment` set `Active` when the asset is resident (materialising the parts first when `PartsSource = FromAsset` and the map is empty) else `Awaiting`, and add `reconcileEnvironment zone world assets` that moves `Awaiting → Active` once the asset lands — materialising the parts **only** when `PartsSource = FromAsset && Parts.Count = 0` (never filling an `Authored` template from the asset, VR-010), then attaching bodies/renderers; route `loadEnvironment` (`FromAsset`) / `loadEnvironmentFrom` (`Authored`) through `requestEnvironment`. `reconcileEnvironment` acts only on `Resident`; on a `Failed` asset the environment stays `Awaiting` (never attached, never drawn) so the authored environment is not lost, and a later request retries (FR-008). Add a headless `ZoneTests` case: an asset-authored environment materialises its parts when the asset lands, while a zero-part `Authored` template stays empty. (Depends on T007, T009; FR-004; research D8; VR-010.)
- [X] T014 [US1] In `screen/GameplayScreen.fs`, call `Assets.pump shared.Assets UploadBudgetMs` once per frame on the render thread (after the update that may switch zones) and then `Zone.reconcileEnvironment` for the active zone. (Depends on T012, T013; FR-003.)
- [X] T015 [US1] Verify on a GPU run (quickstart §2–§3): switching to the ArtRoom zone shows no multi-second `ui` frame, the loop stays live, and the environment pops in; zone switches complete in the requesting frame (SC-001, SC-002, FR-001, FR-004). (Depends on T014.) **Needs a person + device.**

**Checkpoint**: the freeze is gone; this is the MVP.

---

## Phase 4: User Story 2 - Decoding and parsing run off the main thread (Priority: P1)

**Goal**: the file read, glTF parse and image decode run on workers; the render thread only creates
and uploads GPU resources.

**Independent Test**: during a load confirm the main thread is not blocked while decode runs and that
GPU work happens on the render thread (quickstart §2, §5).

### Implementation for User Story 2

- [X] T016 [US2] Audit the thread split: confirm `decodeEnvironment`/`Texture.decode` make no `SDL_gpu`/window call and that every device call lives in `Assets.pump` / `Texture.createFromDecoded` (the command buffers, the swapchain and resource creation stay render-thread-only). Confirm explicitly that the SDL_image calls made on a worker (`SDL3.Image.Load`, `SDL.ConvertSurface`, `SDL.DestroySurface`) are safe there (research D1); note the finding. (Depends on T011; FR-002, FR-012.)
- [X] T017 [US2] In `FFS_LOAD_TIMING=1` output, report the worker-measured parse/decode and the render-side staging/memcpy/submit plus the queue wait, so a load's cost is attributable across the split; keep one line per asset. (Depends on T008, T014; FR-002; research D7.)
- [X] T018 [US2] Verify on a GPU run (quickstart §2, §5): the ArtRoom decode (~4.6 s) no longer blocks the loop, the load line attributes decode to the worker, `draws 191 / instances 521 / parts 463` are unchanged once it lands, and shutdown is clean (SC-003, SC-005, FR-010). (Depends on T017.) **Needs a person + device.**

**Checkpoint**: the CPU/GPU split is demonstrated and the result is byte-identical.

---

## Phase 5: User Story 3 - A burst of requests is de-duplicated and prioritised (Priority: P2)

**Goal**: the pending set holds one request per asset, is bounded, and drops the least recently
requested in favour of the most recent; the active environment is never dropped.

**Independent Test**: fire more distinct requests than the bound and confirm the set never exceeds it,
the most recent are served, stale ones are dropped, and the active one survives (quickstart §4).

### Tests for User Story 3

- [X] T019 [P] [US3] Add `test/AssetLoadTests.fs` (and its entry in `test/fsharp-game.Tests.fsproj`) asserting headlessly: a repeat `request` yields one entry, moved to the most-recent end; a burst beyond `LoadQueueCapacity` drops the least-recently-requested and keeps the newest; a pinned id is never dropped; the `Uploading` id is never dropped; a dropped claimed id is no longer `isPending` while `isClaimed` still holds it; and the FR-009 bound — a pool built with a stub `decode` that blocks fires more requests than `loadWorkers`, and `LoadPool.inFlight` never exceeds `loadWorkers`. (Depends on T004, T005; FR-005, FR-009, SC-004.)

### Implementation for User Story 3

- [X] T020 [US3] In `world/Assets.fs`, wire request-time dropping: `requestEnvironment` returns immediately after the queue's drop, and each dropped id's `Residency` is reset to `NotLoaded` (never left `Loading`); a dropped-but-decoding payload is discarded by `pump` (`isPending` is false) and its id is reset there too if still `Loading`. (Depends on T009, T019; FR-005/FR-008; research D5; VR-011.)
- [X] T021 [US3] Verify on a GPU run (quickstart §4): holding undo / cycling many zones keeps the app responsive, only the most recently requested environments load, and the active environment always loads (SC-004, FR-005/FR-006). (Depends on T020.) **Needs a person + device.**

**Checkpoint**: a burst is bounded and most-recent-first.

---

## Phase 6: User Story 4 - The readout shows loading (Priority: P2)

**Goal**: the per-zone residency indicator distinguishes loading from resident, not resident, no
environment, and failed.

**Independent Test**: activate a non-resident environment and watch its indicator move loading →
resident; a zone with no environment is shown distinctly (quickstart §3).

### Implementation for User Story 4

- [X] T022 [US4] In `editor/Outliner.fs`, extend the residency glyph to `[-]` no environment, `[ ]` not loaded, `[~]` loading, `[+]` resident, `[x]` failed, reading `Residency`/`Assets.isLoading`/`Assets.hasFailed`. (Depends on T009; FR-007/FR-008.)
- [X] T023 [US4] Verify on a GPU run (quickstart §3): the glyph moves `[~] → [+]` with no manual refresh, and a zone with no environment stays `[-]` (SC-003, FR-007). (Depends on T022.) **Needs a person + device.**

**Checkpoint**: the intermediate state is visible.

---

## Phase 7: User Story 5 - A failed load is surfaced, not half-applied (Priority: P2)

**Goal**: a missing/corrupt environment asset fails asynchronously, is logged and marked failed, and
leaves the zone in a defined state; shutdown with work in flight is clean.

**Independent Test**: point an environment at a missing/corrupt asset, load it, and confirm the
failure is logged, the glyph reads failed, the zone is not stuck loading, and no resource leaks
(quickstart §6).

### Implementation for User Story 5

- [X] T024 [US5] In `world/Assets.fs` and `world/AssetLoad.fs`, surface worker failures: a `LoadFailed` for a still-wanted id is logged, sets `Residency = Failed`, and leaves `Pending` (never stuck `Loading`); a discarded payload's id is reset `Loading → NotLoaded`; a later `request` clears the failure and re-queues (`Failed → Loading`). Leave the zone's `ZoneEnvironment` in `Awaiting` (do **not** revert it to `NoEnvironment`), so the authored environment is kept (FR-008, VR-011). (Depends on T009; research D9.)
- [X] T025 [US5] In `world/Assets.fs`, roll back a mid-build failure exactly: `Gpu.abortUpload`, release every `Created` mesh and every `Keys` texture, then set `Residency = Failed`. (Depends on T009; FR-008; research D9.)
- [X] T026 [US5] In `world/Assets.fs` `release`, stop the pool before the existing environment/texture/actor/placeholder release, using the pool's bounded join (background workers, a worker mid-decode is abandoned — it never touches the device), so shutdown with loads in flight is prompt and clean. (Depends on T005, T009; FR-011; research D10.)
- [X] T027 [US5] Verify on a GPU run (quickstart §6): a missing/corrupt asset logs an error, shows `[x]`, is not stuck loading, leaves no partial environment, keeps the app running; closing the window mid-load is a prompt, clean shutdown (SC-007, FR-008, FR-011). (Depends on T024, T025, T026.) **Needs a person + device.**

**Checkpoint**: failure is surfaced and shutdown is clean.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: make the measurement permanent and record the result.

- [X] T028 [P] Finalize the instrumentation (research D7): the per-asset line reports worker parse/decode and render staging/memcpy/submit plus the queue wait, opt-in via `FFS_LOAD_TIMING=1`; remove any temporary counters. (Depends on T017.)
- [X] T029 [P] Update `docs/RENDER_PERFORMANCE.md` (and/or `docs/ENVIRONMENT_LRU_PLAN.md`) to record the CPU/GPU split, the worker pool, the bounded recency queue, and the before/after frame behaviour.
- [X] T030 Run `quickstart.md` §1–§7 and record the outcome (headless suite; the ArtRoom frame log before/after; the glyph transitions; the burst; the failure/shutdown paths). Include SC-006 explicitly: resident + staging memory return to steady state after the first load/unload cycle, and shutdown leaks nothing.
- [X] T031 Commit the change.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: T002→T003→T005; T004→T005; T006→T007; T002/T003→T008; T002–T008→T009; T006/T007/T009→T010; T009/T010→T011. Blocks all stories.
- **US1 (Phase 3)**: T012/T013 need T009; T014 needs T012/T013; T015 needs T014.
- **US2 (Phase 4)**: T016 needs T011; T017 needs T008/T014; T018 needs T017. (T016 ∥ T017.)
- **US3 (Phase 5)**: T019 needs T004, T005; T020 needs T009/T019; T021 needs T020.
- **US4 (Phase 6)**: T022 needs T009; T023 needs T022. (Independent of US2/US3.)
- **US5 (Phase 7)**: T024/T025 need T009; T026 needs T005/T009; T027 needs T024–T026.
- **Polish (Phase 8)**: T028–T031 last.

### Same-file constraint (sequential)

- `world/AssetLoad.fs` — T001 → T003 → T004 → T005.
- `render/Texture.fs` — T002 → T008.
- `world/Entity.fs` + `world/Zone.fs` — T007.
- `world/Assets.fs` — T009 → T020 → T024 → T025 → T026.
- `world/EnvironmentAsset.fs` — T006.
- `test/TestSupport.fs` / `test/AssetsTests.fs` — T010; `test/AssetLoadTests.fs` — T019.

### Parallel Opportunities

- **T002 ∥ T006** — `render/Texture.fs` and `world/EnvironmentAsset.fs` are different files.
- **T016 ∥ T017** — the thread-split audit and the instrumentation line are independent.
- **T019 ∥ T022** — the queue tests and the readout glyph are different files.
- **T028 ∥ T029** — instrumentation and docs are different files.

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1–2: the split, the queue, the pool and the state DUs; suite green.
2. Phase 3 (US1): a zone switch returns immediately and the environment pops in.
3. **STOP and VALIDATE** on GPU: no ~5 s stall; the environment lands.

### Incremental Delivery

1. Foundational → the machinery exists, behaviour still synchronous.
2. US1 → no freeze (MVP).
3. US2 → the split is demonstrated and the render is identical.
4. US3 → a burst is bounded and prioritised.
5. US4 → the loading state is visible.
6. US5 → failure and shutdown are clean.

---

## Notes

- [P] tasks = different files, no dependencies.
- US2, US4 and US5 verify or extend the wiring US1 lands (T012–T014), so they are ordered after
  it and are **not** independently testable without US1 — the same accepted shape as spec 052's US2.
  US3 is independent (the queue and the pool's bound are pure).
- The compile order matters: `world/AssetLoad.fs` sits after `render/Texture.fs`/`core/Gltf.fs` and
  before `world/Assets.fs`; `DecodedImage` is declared in `render/Texture.fs`, not `AssetLoad.fs`
  (a cycle otherwise).
- `Decoding`/`Uploading` are **derived** (queue `Claimed`/`Uploading` + `Assets.Uploads`); only
  `Residency` is stored, and only on the render thread (research D7/D12).
- Decode buffers are **not** pooled (research D13): `DecodedImage.Pixels` is a plain `byte array`,
  dropped after `createFromDecoded` or when the payload is discarded. Revisit only if measurement
  shows LOH/Gen2 pauses stalling the frame loop.
- The worker re-decodes textures that are already resident (research D14, accepted): it cannot see
  the render thread's cache, and the redundancy only hits a shared, still-resident texture.
- `AssetId`/`ModelPath`/`TextureKey` are documentation-only string aliases (F# erases them); they
  name the keys without adding type safety.
- GPU-dependent tasks (T015, T018, T021, T023, T027, T030) need a person and a device; a plain
  `dotnet run` prints no load lines — always run with `FFS_LOAD_TIMING=1`.
- The 412-test suite is the regression gate throughout; commit after each logical group.

---

## Implementation status (2026-10-03)

**Done (T001–T014, T016, T017, T019, T020, T022, T024, T025, T026, T028, T029).** Both projects
build with **0 warnings** and the suite passes **421 tests** (412 + 7 queue/pool + 2 materialisation).

- `render/Texture.fs` — `DecodedImage` declared here; `load` split into `decode` (CPU-only) and
  `createFromDecoded`; decode timing moved into the image (no worker writes a global).
- `world/AssetLoad.fs` (new) — `DecodedEnvironment`/`LoadOutcome`, `decodeEnvironment`, the bounded
  most-recent-first `LoadQueue` (pinning, the single `Uploading` guard, drop-oldest), and the
  background-thread `LoadPool` (injected decode, `inFlight`, bounded-join `stopPool`).
- `world/EnvironmentAsset.fs` — `Residency` DU replacing `Loaded`.
- `world/Entity.fs` — `PartsSource` (`FromAsset | Authored`) on `Environment`.
- `world/Assets.fs` — `requestEnvironment`/`setPinned`/`pump` + the resumable `PendingUpload` cursor
  (lazy batch) replacing `ensureEnvironmentResident`; `isLoading`/`hasFailed`; pool stopped in
  `release`; the eager props/actors path recomposed at `acquireTexture`.
- `world/Zone.fs` — `ZoneEnvironment` DU (`EnvironmentState` field + read-only `member Environment`),
  `setEnvironment`/`reconcileEnvironment`/`materialiseParts`, `gatherStatic` draws `Active` only.
- `state/GameplayState.fs` — `swapZone` requests and completes in-frame (the pin set is refreshed per frame by `GameplayScreen`).

- `screen/GameplayScreen.fs` — one `Assets.pump` + `Zone.reconcileEnvironment` per frame.
- `editor/Outliner.fs` — glyph `[-]`/`[ ]`/`[~]`/`[+]`/`[x]`.
- `test/` — `TestSupport` DUs, `AssetsTests`/`GameplayStateTests`/`ZoneTests`/`ZoneRegistryTests`/
  `EntityTests` updated; new `AssetLoadTests.fs` and two `ZoneTests` materialisation cases.
- `docs/RENDER_PERFORMANCE.md` — the async-loading section.

**Verified (2026-10-03):** T015, T018, T021, T023, T027, T030 — the GPU walkthrough in
`quickstart.md` with `FFS_LOAD_TIMING=1` passes (no frame stall; the environment pops in; the glyph
`[~] → [+]`; the burst; the failure and shutdown paths; SC-006 steady state).

**Done (T031):** committed with this change.

**Review fixes (2026-10-03).** Two implementation reviews found two real bugs plus polish; all fixed;
build still 0 warnings, **421 tests** pass.

- **Claim held until resident**: `pump` no longer releases the claim when it drains a payload, so an
  asset being uploaded is never re-claimed and re-decoded, and a duplicate payload can never
  overwrite (and leak) a cursor in progress. `claimNext` also skips the `Uploading` id.
- **Pins refreshed every frame**: `requestEnvironment` no longer unions `protectedIds`; the pin set is
  replaced by `GameplayScreen.update` with the active zone's environment every frame, so it can never
  accumulate ids of environments no longer in use (which had made them permanently unevictable and
  defeated the byte budget).
- **`inFlight` = active decodes**, not the claim count (`ActiveDecodes` via `Interlocked`), so the
  FR-009 bound is meaningful.
- **Outstanding-payload cap**: `LoadPool.Slots` bounds decoded-but-not-uploaded payloads to the worker
  count, so a fast decode cannot run ahead of the budgeted upload.
- **One batch at a time**: `pump` advances a cursor already holding a `Batch` first, independent of
  dictionary order.
- **Cleanup**: `makeResident` drops the id's `Paths`/`RequestTicks` entries; docs (contract,
  data-model, research D5/D6) reconciled with the implementation and constant names made consistent.

**New test:** a claimed id is not re-claimed until it is resident (the claim-hold invariant).

**Slot-lifetime correction (2026-10-03).** A review found `Slots` was released too early — at drain,
when the payload becomes a cursor that retains its pixels until it settles — so it did not actually
bound decoded-payload memory. `Assets.releaseSlot` is now called only when a cursor **settles, is
discarded, or fails** (and on a discarded/failed/duplicate outcome at drain), so `Slots` bounds
decode + `Ready` + `Uploads` to `workers` as D6/FR-009 intend. Also: a claimed id dropped while a
worker waits for a slot is re-checked and abandoned before decoding; and the `FFS_LOAD_WORKERS` name
(which an earlier rename had corrupted to `FFS_loadWorkers` in the docs) is corrected.
