# Implementation Plan: Project Structure Consolidation

**Branch**: `094-structure-consolidation` (none — applied retrospectively on the working tree) | **Date**: 2026-10-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/094-structure-consolidation/spec.md`

## Summary

Align the physical folder tree with the F# compile-list layers: reunite `state/`
into one contiguous compile block, re-home the two files that formed the one
genuine ordering inversion (`ScenarioFork`, `CueEngine`) into `state/`, move the
debug overlay to `render/`, group `world/` into `assets/` and `render/`
subfolders, and remove three module-name collisions (`History`→`Undo`,
`Engine`→`Shell`, `Entity.Actor`→`Entity.ActorEntity`). The approach was set by a
full dependency-graph scan of the 82 source files (see `research.md`): every
cross-file reference was mapped against the compile order, which showed the
documented cluster layering was already almost perfectly held — the work is a
small set of mechanical, compiler-verified moves, not a re-architecture.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: SDL3-CS, JoltPhysicsSharp, Prime; xunit + FsCheck (tests)

**Storage**: N/A — the "data" of this feature is the file layout itself

**Testing**: `dotnet test` (xunit), 666 tests; structural invariants verified by
a one-off dependency-graph scan script (see `quickstart.md`)

**Target Platform**: Linux desktop (SDL3)

**Project Type**: desktop game + editor, single F# assembly, compile order
declared in `fsharp-game.fsproj`

**Performance Goals**: N/A — no runtime behavior changes

**Constraints**: zero behavior change; every rename call site must be caught by
the compiler; `narrative/` untouched; `specs/` (past features) untouched;
`docs/` and code/fsproj comments must describe the new layout

**Scale/Scope**: 82 source files across 9 clusters; 14 files moved; 3 modules
renamed (~120 call sites); 9 docs + ~25 code comments updated

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Verdict | Notes |
|-----------|---------|-------|
| I. Minimalism | PASS | Moves and renames only; no new abstractions. A new `runtime/` cluster for the two inverted files was considered and rejected — a folder for two files is over-structuring. `Entity.Prop` left alone: no collision to fix. |
| II. Fail Loudly | PASS | Rename coverage is enforced by the compiler (a missed call site is a compile error), not by convention. No failure-path behavior changes. |
| III. Spec-First | **VIOLATION (justified)** | The work was analysed and landed before the spec exists. This spec/plan/tasks are retrospective, requested by the maintainer to complete the trail. Recorded in Complexity Tracking. |
| IV. Data-Oriented Architecture | N/A | No runtime data model touched. |
| V. Minimise GC Pressure | N/A | No hot path touched. |
| N-I…N-V (narrative) | N/A | `narrative/` is explicitly out of scope (FR-007). |

**Post-design re-check**: unchanged — the design artefacts below introduce no
code, only the already-landed structure and its invariants.

## Project Structure

### Documentation (this feature)

```text
specs/094-structure-consolidation/
├── spec.md                # The feature specification (retrospective)
├── plan.md                # This file (/speckit-plan command output)
├── research.md            # Phase 0 output: the dependency-graph scan + decisions
├── data-model.md          # Phase 1 output: clusters, compile list, modules, invariants
├── quickstart.md          # Phase 1 output: how to verify the structure end-to-end
├── contracts/             # Phase 1 output: the structural invariants as checkable rules
│   └── structure-invariants.md
└── tasks.md               # Phase 2 output (/speckit-tasks command — completed tasks)
```

### Source Code (repository root)

```text
fsharp-game/
├── Program.fs                  # [<EntryPoint>] main → Shell.run
├── Shell.fs                    # app shell: SDL3 + ImGui bring-up, frame loop, teardown (was Engine.fs)
├── narrative/                  # UNCHANGED (vestigial; out of scope)
├── core/                       # SlotMap, Diagnostics, Lru, Iqm, Gltf, Input, Maths
├── physics/                    # Physics (Jolt adapter)
├── render/                     # Gpu, Texture, Mesh, StaticMesh, InstanceBuffer, ImGui,
│                               #   ImGuiSdl3, Camera, Skydome, DebugDraw, Ui,
│   └── DebugOverlay.fs         #   ← moved from screen/ (needs only Diagnostics + ImGui)
├── audio/                      # AudioAsset, Audio, Music, Sound
├── world/                      # domain data + systems: Dialogue, Actor, Sway, Advent, Cue,
│                               #   Entity, ZoneTemplate, Zone, ScenarioZone, ScenarioRegistry, Systems
│   ├── assets/                 #   AssetLoad, Assets, ActorAsset, EnvironmentAsset
│   └── render/                 #   ActorRender, EnvironmentRender, EnvironmentBatch,
│                               #     DialogueRender, PromptRender
├── state/                      # Screen, SharedState, Command, EditorState, GameplayState,
│   ├── Undo.fs                 #   ← was state/History.fs, module History → Undo
│   ├── ScenarioFork.fs         #   ← moved from world/ (effects land on GameplayState)
│   └── CueEngine.fs            #   ← moved from world/ (effects land on GameplayState)
├── editor/                     # 16 files, unchanged
└── screen/                     # MainMenuScreen, GameplayScreen, ScreenManager (DebugOverlay left)
```

**Structure Decision**: One rule — a file's folder is its compile-list layer.
`state/` holds the gameplay state and the engines whose effects land on it, so
the previous single inversion point disappears without a new cluster. `world/`
stays one layer but gains grouping subfolders (`narrative/Units/` precedent);
its files interleave in the compile order across the sub-concerns (e.g.
`Advent` between the renderers and the asset substrate), so splitting `world/`
into top-level clusters would impose an order the dependencies do not have. The
authoritative description is `docs/PROJECT_STRUCTURE.md`.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Retrospective spec (Constitution III) | The restructuring was analysis-led and landed in one pass; the maintainer asked for the speckit trail to be completed after the fact | Not recording it — leaves a structural change with no spec trail, worse than a late one |
