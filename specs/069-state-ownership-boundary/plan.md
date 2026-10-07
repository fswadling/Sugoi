# Implementation Plan: State Ownership Boundary

**Branch**: `069-state-ownership-boundary` | **Date**: 2026-10-07 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/069-state-ownership-boundary/spec.md`

## Summary

Give the two state containers one rule — **a member lives where its readers are** — and apply it.
Move `ZoneRegistry` (the recipe table for the zones) and `Physics` (the simulation the zones'
bodies attach to) from `SharedState` onto `GameplayState`, beside the `Zones` they serve. Leave
`Gpu`, `Assets`, `Cast` and `Advents` on `SharedState` (engine / any-screen readers). Retire the
obsolete per-zone-undo rationale (`spec 034`) from the code and docs. This is a relocation, not a
redesign: one physics world, one registry, one history, unchanged behaviour; only the container and
the expression naming the field change. No `Zone.*` signature and no compile order change, because
`Zone.*` already receives physics as a parameter and both `world/ZoneRegistry.fs` and
`physics/Physics.fs` compile below `state/GameplayState.fs`.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: none new — `JoltPhysicsSharp` (`Physics.World`), the existing
`ZoneRegistry.Registry`, `System.Collections.Generic`

**Storage**: N/A (nothing persisted yet; ROADMAP phase 15)

**Testing**: xUnit, headless (`dotnet test`); no device

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: none new — no hot-path change; the active-zone lookup and the one physics
step per play frame are unchanged

**Constraints**: no `shared.Physics` / `shared.ZoneRegistry` reference remains; no module cycle or
new `.fsproj` ordering constraint; the build is warning-free; the suite is green

**Scale/Scope**: ~12 source files + `Engine.fs` + 5 test files + 3 docs; no new module

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. A field relocation and a rationale correction; no new abstraction. The
  rule is stated once in each header rather than enforced by a new type.
- **II. Fail Loudly** — PASS. Behaviour is unchanged; no guard is added, moved or weakened.
- **III. Spec-First** — PASS. This feature is `specs/069-state-ownership-boundary`.
- **IV. Data-Oriented Architecture** — PASS. `GameplayState` is already a struct holding the
  zones; the moved members are a class reference (`Physics.World`) and a record with a dictionary
  (`Registry`), so the struct's shape is unaffected and mutation stays through references.
- **V. Minimise GC Pressure** — PASS. No allocation change; the relocated fields are read exactly
  as often as before.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/069-state-ownership-boundary/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── state-ownership.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
state/SharedState.fs        # remove ZoneRegistry + Physics; rewrite the header to the readership
                            #   rule and drop the per-zone-undo (spec 034) rationale
state/GameplayState.fs      # add ZoneRegistry + Physics; construct both in create; swapZone /
                            #   populateZone / deleteZone read it from the state, not from `shared`
world/CueEngine.fs          # shared.Physics -> state.Physics; shared.ZoneRegistry -> state.ZoneRegistry
                            #   (the AddZone/PreloadZone template read)
state/History.fs            # shared.Physics -> state.Physics
screen/GameplayScreen.fs    # shared.Physics -> state.Physics (play chain: reconcile, player, step, sync)
editor/Editor.fs            # shared.Physics -> state.Physics (nudge, interact)
editor/Selection.fs         # shared.Physics -> state.Physics (apply/undo call sites)
editor/Spawning.fs          # shared.Physics -> state.Physics
editor/AssetBrowser.fs      # shared.Physics -> state.Physics
editor/ZoneRegistryPanel.fs # shared.ZoneRegistry -> state.ZoneRegistry
Engine.fs                   # manager.Shared.Physics -> manager.Gameplay.Physics (shutdown release)
fsharp-game.fsproj          # no change (compile order already valid); verify only
test/GameplayStateTests.fs  # drop ZoneRegistry/Physics from the hand-built SharedState
test/AdventTests.fs         #   "
test/CastIndexTests.fs      #   "
test/CuePanelTests.fs       #   "
test/CueTests.fs            #   " ; seed a template on the state's registry; shared.Physics -> state.Physics

docs/ARCHITECTURE.md        # §State management reflects the new boundary + the readership rule
docs/PROJECT_STRUCTURE.md   # SharedState.fs / GameplayState.fs one-line descriptions
docs/EDITOR_DESIGN.md       # correct the D-011 note that says `SharedState` hosts the zone registry
docs/ENVIRONMENT_LRU_PLAN.md, docs/ROADMAP.md   # check and correct any `Shared.Physics` reference
```

**Structure Decision**: No new module and no `.fsproj` change. Both moved members already compile
*below* `state/GameplayState.fs` — `physics/Physics.fs` (line 44) and `world/ZoneRegistry.fs`
(line 99) — so referencing them from the gameplay state introduces no ordering constraint. The
members move as fields only; `Zone.*` continues to take the physics world as a parameter (a zone
must never own it, so there is one world for every zone), and `Command.apply`/`undo` continue to
take `(zone, physics, assets)` as values, so no `Command → GameplayState` cycle can form.

## Complexity Tracking

> No Constitution Check violations.
