# Data Model: Project Structure Consolidation

**Date**: 2026-10-16 | **Spec**: [spec.md](spec.md)

The "data" of this feature is the project structure itself: clusters, the
compile list, and modules.

## Entities

### Cluster

A top-level folder naming a dependency layer. A file may only reference symbols
defined in earlier files of the compile list, so a cluster's files must never
reference a later cluster.

Ordered layers: `narrative` (standalone, consumed by no other cluster yet) →
`core` → `physics` → `render` → `audio` → `world` → `state` → `editor` →
`screen` → root (`Shell`, `Program`).

A cluster may contain **grouping subfolders** (`narrative/Units/`,
`world/assets/`, `world/render/`) that aid navigation without affecting the
compile order.

### Compile list

The ordered `<Compile Include>` sequence in `fsharp-game.fsproj` — the single
source of truth for dependency order (F# compiles in list order).

**Invariant CL-1 (contiguity)**: every cluster's files form one contiguous
block in the list.

**Invariant CL-2 (order soundness)**: for every cross-file reference A → B, B
appears no later than A in the list. (Enforced by the compiler; the scan
double-checks it.)

**Invariant CL-3 (folder = layer)**: for every cross-cluster reference A → B,
B's cluster is no later than A's cluster in the layer order. After this
feature: zero exceptions.

### Module

One top-level `module X` per file, matching the filename; no namespace (the
`Narrative` namespace excepted).

**Invariant M-1 (unique global names)**: no two files declare the same global
module name.

**Invariant M-2 (no nested shadowing)**: a nested module inside an `open`ed
module must not share a global module's name.

## File mapping (old → new)

| Old | New | Kind |
|-----|-----|------|
| `state/History.fs` (module `History`) | `state/Undo.fs` (module `Undo`) | rename + reorder |
| `Engine.fs` (module `Engine`) | `Shell.fs` (module `Shell`) | rename |
| `Entity.Actor` (nested module) | `Entity.ActorEntity` | rename |
| `world/ScenarioFork.fs` | `state/ScenarioFork.fs` | move |
| `world/CueEngine.fs` | `state/CueEngine.fs` | move |
| `screen/DebugOverlay.fs` | `render/DebugOverlay.fs` | move |
| `world/AssetLoad.fs` | `world/assets/AssetLoad.fs` | move |
| `world/Assets.fs` | `world/assets/Assets.fs` | move |
| `world/ActorAsset.fs` | `world/assets/ActorAsset.fs` | move |
| `world/EnvironmentAsset.fs` | `world/assets/EnvironmentAsset.fs` | move |
| `world/ActorRender.fs` | `world/render/ActorRender.fs` | move |
| `world/EnvironmentRender.fs` | `world/render/EnvironmentRender.fs` | move |
| `world/EnvironmentBatch.fs` | `world/render/EnvironmentBatch.fs` | move |
| `world/DialogueRender.fs` | `world/render/DialogueRender.fs` | move |
| `world/PromptRender.fs` | `world/render/PromptRender.fs` | move |

Unchanged on purpose: `Entity.Prop` (no global `Prop` namesake), everything in
`narrative/`, `editor/` (16 files), `audio/`, `physics/`, `core/`, and the
remaining `world/` root files.

## Validation rules (from requirements)

- FR-001 → CL-1; FR-002 → CL-3 for the `world`→`state` edge; FR-003 →
  `render/DebugOverlay.fs` compiles within the `render/` block; FR-004 → the
  two subfolders exist with exactly the listed members and the compile order is
  byte-identical to before the move; FR-005/FR-006 → M-1, M-2; FR-007 →
  `narrative/` byte-identical; FR-008 → `docs/PROJECT_STRUCTURE.md` names every
  existing file and no retired ones; FR-009 → build + 666 tests green.

## State transitions

None — no runtime state. The only transition is the repository's: before
(folder ≠ layer for 3 files, `state/` split, 3 name collisions) → after
(invariants CL-1…3, M-1…2 all hold).
