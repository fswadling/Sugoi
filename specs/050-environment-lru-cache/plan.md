# Implementation Plan: Bounded environment residency

**Branch**: `050-environment-lru-cache` | **Date**: 2026-10-02 (revised 2026-10-03) | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/050-environment-lru-cache/spec.md`

## Summary

`Assets.scan` formerly parsed **and uploaded** every environment and prop at startup
(`world/Assets.fs`; the old `loadStaticModel`, now the on-demand `buildLoaded`). One environment
here — `ArtRoom` — is ~1.68 GB of textures alone, so a project with many zones cannot start or
survive.

Decouple *zone state* from *environment residency*. Zone entities, placed parts, live
colliders and the registry stay resident always; an environment's **loaded data** — its
parsed geometry *and* its GPU meshes and textures, as **one unit** — is loaded on demand
(on a zone switch or when an environment is instantiated) and kept under a **byte budget**
(`ENVIRONMENT_BUDGET_BYTES`, 2 GB) by an **LRU** over environment-asset ids, evicting
the least-recently-used first. Startup becomes manifest/metadata only (no parse, no
upload). Eviction frees the whole unit (geometry and GPU data together); a reload
re-parses the model. The editor's **Outliner** zones list gains a minimal per-zone
residency glyph.

The reference implementation is `odin-game` (`docs/ENV_MODEL_LRU_PLAN.md`,
`MAX_LOADED_ENVIRONMENT_MODELS :: 3`, `core:container/lru`); this plan ports its shape and
replaces the count with a byte budget (the measured assets here differ by ~1000×), uses a
refcounted shared-texture cache (spec `FR-018`), and loads on switch/instantiate rather
than touching every frame.

**Design revision (2026-10-03).** An earlier draft split each asset into a retained CPU half
and an evictable GPU half. Measurement showed the CPU half is small — ~2 MB per environment
(art room 1.84 MB, island 2.37 MB) — and its only consumers (collider construction, part
lists) run at (re)build time *after* the load, never while evicted; the colliders themselves
live in the physics world, not the CPU arrays. So the split bought ~2 MB per environment of
avoided re-parse while leaving RAM unbounded and adding a third residency state and a second
accessor family. The split is removed: **one loaded unit, evicted and reloaded as a whole**,
so the one budget bounds RAM and VRAM (research D6).

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`).

**Primary Dependencies**: **none new.** The LRU is ~30 lines over BCL
`System.Collections.Generic.LinkedList<'k>` + `Dictionary<'k, LinkedListNode<'k>>`
(research D3). No new NuGet package, no native change. The editor glyph reuses the
already-bound `ImGui.text`.

**Storage**: none. Residency is runtime state; nothing is persisted (phase 15 owns
persistence). No authored format changes.

**Testing**: pure logic headlessly — the LRU (`test/LruTests.fs`), the residency reader and
the eviction decision (`test/AssetsTests.fs`, extended), and the zero-device
`Zone`/`GameplayState` paths (existing suites, fixtures updated to build loaded assets). The
parse/upload and evict themselves, the budget under real assets, and the editor glyph are the
quickstart walkthrough — tests cannot open a GPU device (AGENTS.md; every rendering spec).

**Target Platform**: Linux x64 desktop, SDL3 + SDL_gpu (Vulkan backend).

**Project Type**: single-project native desktop game with a docked ImGui editor.

**Performance Goals**: hold 60 FPS. The hot path is unchanged: `EnvironmentBatch.gather`
runs once per frame for the active zone and resolves submeshes through
`EnvironmentRender.Asset`; the ensure runs only on switch/instantiate, not per frame, so no
per-frame work or allocation is added (constitution V).

**Constraints**: a single byte budget bounding the whole loaded unit (FR-001, FR-009); load
on switch and instantiate (FR-002/003); LRU eviction under budget (FR-004); the environment
in use never evicted (FR-005); eviction frees parsed geometry and GPU data together, leaving
zone state/colliders/undo/registry unaffected (FR-006); reload re-parses and is identical
(FR-007); one residency per asset, safe across assets sharing a texture (FR-008, FR-018);
metadata-only startup (FR-010); loud on non-resident use (FR-011); no double-free (FR-012); a
minimal Outliner glyph (FR-013–FR-016); a failed load cancels the action, no half-change
(FR-017); 20-environment validation (SC-001).

**Scale/Scope**: `core/Lru.fs` (new, ~30 lines); `world/EnvironmentAsset.fs` (one loaded unit,
one option); `world/Assets.fs` (the bulk: metadata-only scan, load/evict, refcounted texture
cache, budget); `render/Texture.fs` (texture byte size); `world/Zone.fs` (ensure on
instantiate); `state/GameplayState.fs` (ensure on switch); `editor/Outliner.fs` (glyph); test
fixtures across a few files; one new test file. No new panel, pass, shader or dependency.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one small `Lru` module (a data structure the BCL lacks), no library; one reused field on `AssetManager`; one glyph in the existing row label; no new panel/binding/pass. Collapsing the CPU/GPU split *strengthens* this: one record, one option, one accessor, no third residency state. |
| II. Fail Loudly | Pass — a non-resident asset in a render/physics path asserts (FR-011); a failed load cancels the action (FR-017) rather than leaving a half-state; the not-loaded state is `None`, so a freed handle is unrepresentable. |
| III. Spec-First | Pass — `specs/050-environment-lru-cache/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — `EnvironmentAsset` stays a record with explicit mutable residency; the loaded unit is a plain record of arrays and GPU handles; the LRU is a dict + intrusive list; no object graph. |
| V. Minimise GC Pressure | Pass — the per-frame path is unchanged; load/evict run off the hot path; the eviction loop walks a linked list; no per-frame allocation. |
| N-V. Testing is the Authoring Tool | Not engaged — narrative subsystem, untouched. |

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Refcounted texture cache (FR-018) | The loader already shares textures by path; a shared texture must not be freed while another resident environment draws it. | **Per-asset texture ownership** — cannot satisfy `FR-018` when two *different* assets reference the same file (spec Q4 answer A). |

The earlier CPU/GPU split is **gone**, not justified: with the CPU half ~2 MB and a ~10 ms
re-parse, the split left RAM unbounded and bought nothing measurable, so it was removed
rather than defended (research D6).

## Project Structure

### Documentation (this feature)

```text
specs/050-environment-lru-cache/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── environment-residency.md   # Phase 1 output — the surfaces that change
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
core/
└── Lru.fs               # NEW: an intrusive LRU over LinkedList + Dictionary (by key).

render/
└── Texture.fs           # CHANGED: `Texture` records its byte size (W*H*4, mips incl.)
                         #   so the budget can be accounted; `load`/`solid` set it.

world/
├── EnvironmentAsset.fs  # CHANGED: one loaded unit — `EnvironmentLoaded` (submeshes with
│                        #   CPU geometry + GPU mesh/material, parts, bounds, materials,
│                        #   byte size, acquired texture keys) behind a single
│                        #   `mutable Loaded : EnvironmentLoaded option`; accessors assert.
├── Assets.fs            # CHANGED: metadata-only `scan`; `ensureEnvironmentResident`
│                        #   (parse + upload, LRU touch, evict tails under budget);
│                        #   `releaseEnvironment` (drops the whole unit); `isEnvironmentResident`;
│                        #   a refcounted shared-texture cache on the (lazy) `Loader`; the
│                        #   budget constant. Props keep eager build via the same builder.
└── Zone.fs              # CHANGED: `setEnvironment` ensures the incoming environment
                         #   resident (covers load, instantiate and undo-restore).

state/
└── GameplayState.fs     # CHANGED: `swapZone` ensures the incoming zone's environment
                         #   before it becomes active (load-before-commit, FR-017).

editor/
└── Outliner.fs          # CHANGED: the zones row label carries a residency glyph
                         #   (resident / not resident / no environment), no new binding.

world/
└── EnvironmentBatch.fs  # CHANGED: resolve submeshes through the resident accessor
                         #   (asserting) instead of `asset.Submeshes` directly.

test/
├── LruTests.fs          # NEW: touch/evict order, membership, count.
├── AssetsTests.fs       # CHANGED: residency reader, eviction decision, metadata-only scan.
├── GameplayStateTests.fs# CHANGED: a switch to a zone whose environment is missing is cancelled.
├── TestSupport.fs       # NEW: shared device-free fixtures in the loaded shape.
└── fsharp-game.Tests.fsproj  # CHANGED: add `TestSupport.fs`, `LruTests.fs`.

fsharp-game.fsproj       # CHANGED: add `core/Lru.fs` after `core/SlotMap.fs`.
```

**Structure Decision**: the LRU is a **data structure the BCL lacks**, so it lives in
**`core/`** beside `SlotMap` (the project's precedent: custom where the framework has a gap).
The residency machinery belongs with the assets it governs (**`world/Assets.fs`**), the single
loaded unit with the type it describes (**`world/EnvironmentAsset.fs`**), and the two ensure
hooks sit at the two places an environment becomes current — `Zone.setEnvironment`
(instantiation, undo-restore) and `GameplayState.swapZone` (activation). The glyph is one
string in the **Outliner** row that already exists. No new module beyond `core/Lru.fs`.

## Phase 0: Research

See [research.md](./research.md). It settles the residency unit (D1), the two-phase
load/evict lifecycle and metadata-only startup (D2), the LRU and why no library fits (D3),
the byte budget and its accounting (D4), the refcounted shared-texture cache (D5), **the
single loaded unit — one option, evicted and re-parsed as a whole (D6)**, the two ensure
hooks and load-before-commit (D7), the editor glyph (D8), and what is tested headlessly (D9).

## Phase 1: Design

- [data-model.md](./data-model.md) — `Lru`, the `EnvironmentAsset` single loaded unit
  (`EnvironmentLoaded`), the `AssetManager` cache fields, the budget, the two-state residency
  model, and the validation rules.
- [contracts/environment-residency.md](./contracts/environment-residency.md) — the surfaces
  that change (`Lru`, `Texture`, `EnvironmentAsset`, `Assets`, `Zone`, `GameplayState`,
  `EnvironmentBatch`, `Outliner`) and what explicitly does not (shaders, pipelines, the batch
  buffer, undo, the registry).
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite and the run
  walkthrough (low-budget thrash, the startup check, the Outliner glyph, shutdown).

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one small module, one field on `AssetManager`, one glyph, one loaded-unit record; no library/panel/binding/pass. |
| II. Fail Loudly | Pass — resident accessors assert; a failed load aborts the action; the not-loaded state is `None`. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — records, one option and arrays; explicit local mutation. |
| V. Minimise GC Pressure | Pass — off the per-frame path entirely; a linked-list walk on eviction only. |
| N-V. Testing is the Authoring Tool | Not engaged. |
