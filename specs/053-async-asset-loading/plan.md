# Implementation Plan: Async asset loading

**Branch**: `053-async-asset-loading` | **Date**: 2026-10-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/053-async-asset-loading/spec.md`

## Summary

An environment load blocks the render thread for its whole duration: measured on ArtRoom
**~5.1 s**, of which **~4.6 s is texture decode (CPU)**, ~0.23 s texture creation, ~0.19 s host
`memcpy`, ~0.10 s staging, ~3.7 ms submission, ~16 ms parse. Activating such a zone — or clicking
it in the Asset Browser — freezes the app for seconds.

This feature moves the **CPU half** of an environment load (file read, glTF parse, image decode)
onto a small pool of **worker threads**, and hands each decoded payload to the **render thread**,
which creates the GPU resources and uploads them in **bounded per-frame slices** so frames keep
running and the environment **pops in** a moment after its zone activates. Concurrent loads are
bounded by a **memory** limit (a decoded ArtRoom is ~1.3 GB of pixels), not by the GPU: SDL command
buffers are thread-affine and submissions serialise, so every `SDL_gpu` call stays on the render
thread.

Because the parse now happens off-thread, the zone no longer waits for a load: the zone switch
completes in-frame, the zone's environment **part resources** (colliders + renderers) are attached
when the asset lands, and the draw path tolerates a not-yet-resident environment (geometry appears
with the upload). This is the deliberate, spec-053 replacement of spec 050's load-before-commit
(FR-004 here vs. FR-017 there) and is called out in [research.md](./research.md) D8.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: **none new.** `System.Threading.Channels`, `System.Threading.SemaphoreSlim`,
`System.Threading.Thread`, `System.Threading.Monitor` and `System.Collections.Concurrent` are all BCL.
SDL_image (`IMG_Load`) is used unchanged, on a worker, for decode.

**Storage**: none persisted.

**Testing**: the existing suite (**412 tests**) is the regression gate. The queue
(bound / de-duplication / recency / pinning) is pure and asserted headlessly; the thread pool and
the GPU upload path need a device the suite cannot open, so they are verified by the per-asset
instrumentation and the GPU walkthrough in [quickstart.md](./quickstart.md) (AGENTS.md; every
rendering spec). The headless fixtures are **already-resident** assets, so the test suite keeps
using the immediate (resident hit) branch and needs no pump.

**Target Platform**: Linux x64 desktop, SDL3 + SDL_gpu (Vulkan backend).

**Project Type**: single-project native desktop game.

**Performance Goals**: no frame stalls during a load — frame time stays near the frame budget
throughout (SC-001), with the GPU half capped at a small per-frame budget
(`UploadBudgetMs = 4.0`, the odin-game figure). ArtRoom's decode (~4.6 s) is unchanged as
wall-clock but no longer blocks the loop; its ~0.5 s of GPU creation/upload spreads across frames.
A zone switch completes in the requesting frame, resident or not (SC-002, FR-004).

**Constraints**: parse + decode off the render thread (FR-002); GPU work on the render thread only
(FR-012); pending loads bounded, de-duplicated, most-recent-first, active pinned (FR-005/FR-006);
concurrent decodes bounded by a memory limit (FR-009); existing residency accounting, budget and LRU
unchanged (FR-010); clean shutdown (FR-011); a failed load logged + surfaced as a *failed* state,
never stuck loading (FR-008).

**Scale/Scope**: one new module (`world/AssetLoad.fs` — the payload, the bounded recency queue and
the worker pool), a split of `render/Texture.fs` decode/creation, a request+pump split of
`world/Assets.fs`, a `PartsSource` field on `world/Entity.fs`, small lifecycle changes in
`world/Zone.fs` / `state/GameplayState.fs`, the residency glyph in `editor/Outliner.fs`, and one
per-frame `pump` call in the gameplay screen. No new shader, pipeline, pass, dependency, or
persisted entity.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one new module of three small types, BCL concurrency only, no new dependency. The queue is an `Lru` (already in `core/`) plus a lock. |
| II. Fail Loudly | Pass — a missing/corrupt model or texture fails on the worker and is surfaced as a logged error + a `Failed` residency state, never a silent default; a non-resident use still raises (spec 050 FR-011); a partial build rolls back. |
| III. Spec-First | Pass — `specs/053-async-asset-loading/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — the payload, the queue and the pool are records/DUs with explicit mutable state; no object graph, no inheritance. |
| V. Minimise GC Pressure | Pass with care — the payload is one array per decoded image and is dropped after upload; no per-frame allocation on the resident hot path (`pump` is amortised and off the steady state). |
| N-V. Testing is the Authoring Tool | Not engaged — narrative subsystem, untouched. |

## Complexity Tracking

The one justified complexity is the **rendezvous between the worker pool and the render thread**
(bounded recency queue + claim + ready channel). It is required by FR-005/FR-009: the user asked
specifically for a most-recent-first, de-duplicated, bounded queue with a separate concurrent-decode
limit, which no single existing structure provides.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| A worker pool + a concurrent queue (new module) | Decode (~90% of the load) must leave the render thread (FR-002), and a burst must be bounded and most-recent-first (FR-005). | A single `Channel` FIFO gives neither recency reordering nor drop-oldest-with-pinning; a bare `Task.Run` per request has no bound and no de-duplication. |
| Deferred environment-part activation (the `ZoneEnvironment.Awaiting` case) | The parse is now off-thread, so the zone's part metadata/colliders cannot be built at the moment of the switch (FR-004). | Blocking the render thread until the parse lands would reintroduce the stall (FR-001); building parts with no colliders would break physics. |

## Project Structure

### Documentation (this feature)

```text
specs/053-async-asset-loading/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── environment-loading.md  # Phase 1 output — the surfaces that change
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
render/
├── Texture.fs      # CHANGED: `DecodedImage` is declared here; `decode` (worker-safe, CPU-only:
│                   #   IMG_Load → compacted RGBA8) is split out of `load`; `createFromDecoded`
│                   #   (render thread: GPU texture + sampler + record the level-0 copy into a
│                   #   batch) replaces the create/record half. Decode timing moves to the payload.
│                   #   `solid` unchanged.

world/
├── AssetLoad.fs    # NEW: `DecodedEnvironment`, `LoadOutcome`; the bounded, de-duplicated,
│                   #   most-recent-first `LoadQueue` (an `Lru` under a lock, with pinning and
│                   #   drop-oldest); `decodeEnvironment` (the CPU half: `Gltf.load` + decode
│                   #   every distinct texture); and the worker pool (claim → decode → post to a
│                   #   ready channel).
├── Assets.fs       # CHANGED: `requestEnvironment` replaces `ensureEnvironmentResident`
│                   #   (enqueue + pin, return); `pump` drains payloads and drives a resumable
│                   #   GPU build within a budget; `isLoading`/`hasFailed`; the pool is created
│                   #   lazily and stopped in `release`.
├── EnvironmentAsset.fs  # CHANGED: `mutable Loaded : EnvironmentLoaded option` becomes a
│                   #   `Residency` DU (`NotLoaded | Loading | Resident | Failed`) — the coarse,
│                   #   single-writer lifecycle; the resident accessors match on `Resident`.
├── Entity.fs       # CHANGED: `Environment` gains `PartsSource` (`FromAsset | Authored`) so a
│                   #   deferred load knows whether its parts are still to be materialised; the
│                   #   marker rides the `SetEnvironment`/`ZoneContents` payload so undo keeps it.
├── Zone.fs         # CHANGED: the single `Environment : Environment option` field becomes a
│                   #   `ZoneEnvironment` DU (`NoEnvironment | Active | Awaiting`), stored as
│                   #   `EnvironmentState` with a read-only `member Environment` (FS0023);
│                   #   `setEnvironment` sets the incoming environment immediately and attaches
│                   #   parts when the asset is resident (else `Awaiting`); `reconcileEnvironment`
│                   #   materialises (only `FromAsset`) + attaches the parts once the asset lands;
│                   #   `gatherStatic` draws parts only for `Active` (pop-in).
└── EnvironmentBatch.fs  # unchanged.

state/
└── GameplayState.fs  # CHANGED: `swapZone` requests instead of ensures (completes in-frame);
                     #   the reconcile hook for the active zone.

editor/
└── Outliner.fs     # CHANGED: the residency glyph gains a loading state (and a failed state).

screen/
└── GameplayScreen.fs  # CHANGED: one `Assets.pump` per frame (render thread), after update.

Engine.fs           # unchanged call order; shutdown already routes through `Assets.release`.

test/               # CHANGED (fixtures): `TestSupport.manager` gains the new fields; new headless
                    #   tests for the queue (bound/dedup/recency/pinning). Resident fixtures keep
                    #   the existing immediate branch, so the 412 tests stay green.
```

**Structure Decision**: the worker/queue/payload live together in `world/AssetLoad.fs`, beside the
asset layer that owns them (`world/Assets.fs`), and depend only on `core/Gltf.fs` and
`render/Texture.fs` (both compiled earlier). `AssetLoad.fs` is compiled **before** `Assets.fs`. No
new module belongs in `render/`: nothing there may run off the render thread but `Texture.decode`,
which stays with `Texture`.

## Phase 0: Research

See [research.md](./research.md). It settles the thread split (D1), the payload (D2), the resumable
render-side build (D3), the texture decode/create split (D4), the bounded recency queue (D5, and why
not an F# `MailboxProcessor`, D5a), the worker-pool size (D6), the residency states (D7), the
deferred activation and the superseded load-before-commit semantics (D8), failure (D9), shutdown
(D10), scope (D11), the typed state (D12) and why the decode buffers are not pooled (D13).

## Phase 1: Design

- [data-model.md](./data-model.md) — `DecodedImage` / `DecodedEnvironment` / `LoadOutcome`, the
  `LoadQueue`, the per-asset `LoadState`, the `PendingUpload` cursor, the lifecycle and the
  validation rules.
- [contracts/environment-loading.md](./contracts/environment-loading.md) — the surfaces that change
  (`Texture`, `AssetLoad`, `Assets`, `Zone`, `GameplayState`, `Outliner`, `GameplayScreen`) and what
  explicitly does not (residency accounting, the LRU/budget, the draw path, props/actors, shaders).
- [quickstart.md](./quickstart.md) — headless validation and the GPU walkthrough that shows the
  frame loop stays live while ArtRoom loads and the environment pops in.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one module, BCL only; the queue reuses `core/Lru.fs`. |
| II. Fail Loudly | Pass — worker failures surface as logged errors + a `Failed` state; resident accessors still raise. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — records/DUs, explicit locks and cursors, no object graph. |
| V. Minimise GC Pressure | Pass — decode is off the hot path; the payload is dropped after upload; `pump` is a no-op in steady state. |
| N-V. Testing is the Authoring Tool | Not engaged. |
