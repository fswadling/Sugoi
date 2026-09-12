# Implementation Plan: Entity Reference Integrity

**Branch**: `015-entity-ref-integrity` | **Date**: 2026-09-12 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/015-entity-ref-integrity/spec.md`

## Summary

Make an entity reference name a specific *incarnation* of an entity — its kind,
slot index, generation, and (for environment-imported parts) the epoch of the
environment that owns it — so deleting and reusing a slot, or swapping
environments, can never make a stale reference address the wrong entity.
Environment parts move out of the zone's placed-prop store into the environment's
own store. All editor entity creation is funnelled through one surface
(`editor/Spawning.fs`) that pairs every spawn with its reversing undo command, and
undo restores a deleted entity into its recorded (free) slot via a generation-
restoring `Occupy` that raises if the slot was taken.

Documented retroactively: the working tree already contains the implementation;
this plan and its artifacts capture the design as built.

## Technical Context

**Language/Version**: F# (.NET 10)

**Primary Dependencies**: No new runtime dependencies. Existing SDL3-CS, Dear
ImGui (custom `cimgui` binding), JoltPhysicsSharp; xUnit for tests.

**Storage**: N/A (in-memory zone + editor state)

**Testing**: xUnit (`fsharp-game.Tests`), driving the real `Zone`/`Spawning`/undo
paths through headless asset fixtures (zero GPU device), plus direct
`SlotMap`/`Zone` unit tests.

**Target Platform**: Linux desktop

**Project Type**: Desktop game + editor

**Performance Goals**: 60 fps. Entity scans stay allocation-free (read-only span
over the slot array); writes go through a `byref` into the payload.

**Constraints**: Fixed-capacity stores; hot-path data stays `[<Struct>]`; no
per-frame allocation. `Occupy` rebuilds the free list (O(capacity)) but only runs
on the undo/restore path.

**Scale/Scope**: One active zone; tens of actors/placed props; a few hundred
imported environment parts.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Minimalism** — PASS. The change adds two fields to `EntityRef`, one epoch +
  parts map to `Environment`, and one small `Spawning` module; `Occupy` replaces
  the old "occupy a free slot" branch of `setByIndex`. No new abstraction layers.
- **Fail Loudly** — PASS. `SlotMap.Occupy` and `Zone.ensureSlotFree` raise when a
  recorded slot is unexpectedly occupied; `Zone.bindingAt` raises for a part ref
  that does not belong to the loaded environment; `Spawning.duplicate` fails for a
  kind with no duplicable entity. No silent defaults.
- **Spec-First** — PASS (documented retroactively). This spec captures the
  already-implemented behaviour.
- **Data-Oriented Architecture** — PASS. `EntityRef`/`Slot`/`Handle` stay
  `[<Struct>]`; `SlotMap` becomes a sealed class mutated in place over a contiguous
  `Slot` array (a deliberate, documented departure from an immutable struct, since
  copies would desync from shared storage); `Environment` owns its own `Parts`
  slot map. Mutation is explicit and localized.
- **Minimise GC Pressure** — PASS. Scans use `ReadOnlySpan<Slot>`; writes use
  `byref`; `Get` and `IteriAlive` allocate nothing. The only added per-call
  allocation is the `Environment` value copy on an environment swap (cold path).

## Project Structure

### Documentation (this feature)

```text
specs/015-entity-ref-integrity/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── entity-refs.md
├── checklists/          # Spec quality checklist
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
core/
└── SlotMap.fs           # sealed class; read-only Slots span; byref Get; Occupy; IsAlive/TryGet/IsOccupied
world/
├── Entity.fs            # EntityRef + Generation + Epoch; actorRef/propRef/partRef; Environment.Epoch + Parts
└── Zone.fs              # isAlive/tryEntityAt/partsFor; setTransform; setActorWithAsset + ensureSlotFree; spawn*; environment lifecycle
state/
└── Command.fs           # SetEnvironment stores Environment values; Occupy-based restore; incarnation-aware merge
editor/
├── Spawning.fs          # NEW — the single undo-paired entity-creation surface
├── Selection.fs         # duplicate via Spawning; dropStalePartSelection
├── Editor.fs            # tryEntityAt; setTransform; shared undoStep/redoStep that reconcile selection
├── Outliner.fs          # per-kind refs with generation; new Parts section
├── Inspector.fs         # TryEntityAt + byref Get for the collider toggle
├── Viewport.fs          # Incarnation-aware selection compare; parts in the line build
└── AssetBrowser.fs      # spawn through Spawning; environment swap drops a stale part selection
screen/
└── GameplayScreen.fs    # span scan + byref writes; draws environment parts
test/
├── SlotMapTests.fs      # class API, generation, Occupy/IsAlive/IsOccupied
├── EntityTests.fs       # Environment record shape
├── ZoneTests.fs         # NEW — ref resolution, epoch scoping, environment lifecycle
├── SpawningTests.fs     # NEW — duplicate pairing, occupied-slot conflict
├── fsharp-game.Tests.fsproj
└── xunit.runner.json    # NEW — serialize collections (Jolt's process-global init)
fsharp-game.fsproj       # + editor/Spawning.fs in the compile list
```

**Structure Decision**: No new directories. `editor/Spawning.fs` joins the existing
`editor` cluster and is inserted in the `.fsproj` compile list after the `state/`
files and before `editor/Selection.fs`, so it can depend on `GameplayState`/`Command`
and be depended on by `Selection`. `test/ZoneTests.fs`/`SpawningTests.fs` join the
flat test project.

## Complexity Tracking

> No Constitution Check violations to justify.
