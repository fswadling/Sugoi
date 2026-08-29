# Implementation Plan: Entity Store

**Branch**: `002-entity-store` | **Date**: 2026-08-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-entity-store/spec.md`

## Summary

Build the foundational container and entity vocabulary: a **generational slot
map** (fixed-size slot array + free list + generation counter, O(1)
add/get/free, stale-reference detection) and the **entity model** (`EntityKind`,
`EntityRef`, base `Entity`, specialised entities). Verified by unit tests. No
visible result this phase — it is the data core that phases 2b–15 build on.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`)

**Primary Dependencies**: no new runtime dependencies — pure data structures
(slot map/entity model); xUnit is added as a test-only dependency

**Storage**: N/A (no persistence this phase)

**Testing**: xUnit in a separate `fsharp-game.Tests` project referencing the main
project; `dotnet test` runs it

**Target Platform**: Linux x64 desktop (native)

**Project Type**: library code inside a native desktop game (the types are
consumed by the existing `Exe`; tests run in a sibling test project)

**Performance Goals**: O(1) add/get/free; no per-frame allocation on scan loops

**Constraints**: fixed-capacity inline arrays of structs, `[<Struct>]` types

**Scale/Scope**: one generic slot map + one entity model; five entity kinds

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: only the five entity kinds from `docs/ARCHITECTURE.md`;
  no speculative ECS features or dynamic growth.
- **II. Fail Loudly** — PASS: out-of-capacity and invalid/stale handles raise.
- **III. Spec-First** — PASS: derives from `spec.md`.
- **IV. Data-Oriented** — PASS: `[<Struct>]` slot array + free list; entities use
  a nested `Entity` base field, not inheritance.
- **V. Minimise GC Pressure** — PASS: fixed arrays of structs; no per-entity heap
  allocation (the `Name : string` field is the one heap field, off the hot path).

## Project Structure

### Documentation (this feature)

```text
specs/002-entity-store/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── slotmap.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
core/
├── SlotMap.fs          # generic generational slot map
└── Entity.fs           # EntityKind, EntityRef, Entity, specialised entities

test/
├── fsharp-game.Tests.fsproj  # xUnit test project (references fsharp-game.fsproj)
├── SlotMapTests.fs
└── EntityTests.fs
```

**Structure Decision**: `core/` is the structural cluster (per
`docs/PROJECT_STRUCTURE.md`); these are the first files to land there and are
inserted at the top of the compile order (they depend on nothing). Tests follow
the `test/<Module>Tests.fs` convention from `docs/PROJECT_STRUCTURE.md`.

## Complexity Tracking

No constitution violations to justify.
