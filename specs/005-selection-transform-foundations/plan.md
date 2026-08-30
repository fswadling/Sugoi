# Implementation Plan: Selection + Transform Foundations

**Branch**: `005-selection-transform-foundations` | **Date**: 2026-08-30 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/005-selection-transform-foundations/spec.md`

## Summary

Provide the data and editor-state layer that the selection + transform phase
(2c) builds on: a `Zone` world container (actors + environment-owned props), an
index-based read/write/remove surface on the generational slot map, the editor's
transform-tool/drag state, and box/sphere debug-draw primitives. Retrospective —
the code already landed in commit `ca73afd`; these artifacts document it.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`)

**Primary Dependencies**: `System.Numerics` (math), SDL3-CS (GPU), no new runtime
dependencies.

**Storage**: in-memory only (fixed-capacity slot maps). No disk persistence this
phase.

**Testing**: xUnit (`dotnet test`); `SlotMapTests.fs` and `EntityTests.fs` cover
the storage lifecycle.

**Target Platform**: Linux x64 desktop (native).

**Project Type**: native desktop game.

**Performance Goals**: 60 fps; O(1) entity add/read/remove; no per-frame
allocation in the debug-draw path.

**Constraints**: slot-map indices are `uint32`; `.NET` arrays are `int`-indexed,
so index↔array conversions are centralized in `SlotMap.fs`. No "no entity"
sentinel — absence is `EntityRef option`.

**Scale/Scope**: one zone, two entity collections (actors cap 64, props cap 512),
two debug-draw shapes, one editor-state record.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: three index accessors (`tryGetByIndex`/`setByIndex`/
  `removeByIndex`), a minimal `Zone`, two debug shapes, and the editor fields the
  next phase needs. No speculative waypoint/trigger or multi-zone support.
- **II. Fail Loudly** — PASS: out-of-range writes/removes raise `invalidOp`;
  reads of absent entities return `None` (an expected case), not a default.
- **III. Spec-First** — PASS (retroactively): this spec backfills the work.
- **IV. Data-Oriented** — PASS: `Zone` is a record of slot maps; entity data is
  contiguous inline arrays; editor state is a small record, not a class graph.
- **V. Minimise GC Pressure** — PASS: slot maps and entities are `[<Struct>]`;
  the debug-draw shapes append to the preallocated accumulator (no allocation).
  Editor state is cold-path, so its record/DU shape is fine.

## Project Structure

### Documentation (this feature)

```text
specs/005-selection-transform-foundations/
├── plan.md              # This file
├── research.md          # Phase 0 decisions
├── data-model.md        # Phase 1 entity model
├── quickstart.md        # Phase 1 validation guide
├── contracts/
│   └── entity-foundations.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
core/SlotMap.fs          # + tryGetByIndex / setByIndex / removeByIndex
core/Entity.fs           # + Environment.Props, transformIdentity; EntityRef.Index : uint32
core/Zone.fs             # NEW: Zone { Name; Actors; Environment } + create
screen/GameplayState.fs  # + Zone : Zone.Zone
render/DebugDraw.fs      # + wireframeBox / sphere
editor/EditorState.fs    # + Tool / GizmoAxis / Drag / Snap
test/SlotMapTests.fs     # + index-accessor tests
test/EntityTests.fs      # + Environment.Props; - NO_ENTITY sentinel
```

**Structure Decision**: `core/Zone.fs` sits after `core/Entity.fs` in the
`.fsproj` (Zone depends on Entity + SlotMap). `Entity.fs` now depends on
`SlotMap` for `Environment.Props`, so `Prop` is declared before `Environment`.
No other compile-order changes.

## Complexity Tracking

No constitution violations to justify.
