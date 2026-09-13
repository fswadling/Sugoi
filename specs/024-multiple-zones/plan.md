# Implementation Plan: Multiple Zones

**Branch**: `024-multiple-zones` | **Date**: 2026-09-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/024-multiple-zones/spec.md`

## Summary

Give the gameplay state several independent zones — a plain dictionary keyed by
the zone's name, with one active name — and a switch path that makes the active
zone the sole world that renders, edits, and simulates. Each zone owns its own
actor/prop/environment data, its own static-scene batch, and its own designated
player actor. Switching detaches the outgoing zone's bodies from the shared
physics world and re-attaches the incoming zone's, leaving inactive zones
dormant. The editor gains a Zones section (in the Outliner) that lists zones, adds
one under a developer-typed name, selects the active zone, and deletes a zone;
per-zone undo history and the player designation make an inactive zone's state
inert rather than aliasing the active one.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`)

**Primary Dependencies**: SDL3-CS (windowing/SDL_gpu), JoltPhysicsSharp 2.22 +
JoltPhysics.Native (physics), Prime (s-syntax manifests), custom cimgui
(`render/ImGui*.fs` + `native/libcimgui.so`; `igInputText` is already exported, so
a new text-field binding needs no native rebuild)

**Storage**: N/A — all in-memory; project persistence is phase 15

**Testing**: xUnit via `dotnet test test/fsharp-game.Tests.fsproj` (flat
`test/<Module>Tests.fs`, Jolt-backed tests serialized process-wide)

**Target Platform**: Linux desktop, SDL3 + SDL_gpu, software 60 FPS cap

**Project Type**: single-project desktop game with a docked editor

**Performance Goals**: hold 60 FPS while switching and playing; no growth in
physics bodies or GPU resources across repeated switches and add/delete cycles

**Constraints**: one shared physics world for every zone; only the active zone's
bodies are attached to the simulation (inactive zones are detached via
`RemoveBody`, not destroyed). No rebuild of the native cimgui dock layout, so the
zones UI lives inside the existing Outliner rather than a new docked panel

**Scale/Scope**: a handful of zones per session; no streaming, background
simulation, or per-zone physics worlds

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — the container is an existing `Dictionary` field plus one active name; no new type or module. Adds three zone lifecycle functions, a `Player` field, one ImGui `inputText` P/Invoke, and a zones section in the existing Outliner. |
| II. Fail Loudly | Pass — adding under an empty/duplicate name raises, switching to an unknown name raises, and a mis-designated non-actor raises (`Systems.playerHandleOf`); nothing silently falls back. |
| III. Spec-First | Pass — `specs/024-multiple-zones/spec.md` is approved and drives this plan. |
| IV. Data-Oriented Architecture | Pass — zones are held by their stable name, not a positional index; each zone's entity data stays contiguous in its own slot maps. |
| V. Minimise GC Pressure | Pass — switching is off the hot path; the per-frame paths resolve the active zone once and the name buffer is reused; no new per-frame allocation. |

No violations; the Complexity Tracking table is empty.

## Project Structure

### Documentation (this feature)

```text
specs/024-multiple-zones/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── zone-container.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
world/
├── Zone.fs              # + Player designation; + deactivatePhysics, activatePhysics, release
├── Systems.fs           # player systems read zone.Player; player parameter removed
└── (unchanged: Entity.fs, Assets.fs, EnvironmentBatch.fs, ActorRender.fs, others)

physics/
└── Physics.fs           # + removeBody, addBody, optimizeBroadPhase

state/
├── GameplayState.fs     # Zone -> Zones dictionary + ActiveZone; UndoByZone; Player removed;
│                        # activeZone / switchZone / addZone / deleteZone
├── EditorState.fs       # + NewZoneName : byte[] (reused name buffer)
└── Command.fs           # unchanged

render/
└── ImGui.fs             # + inputText P/Invoke (igInputText)

editor/
├── Outliner.fs          # + Zones collapsing section (list, Del, name field, Add Zone)
└── (unchanged: Inspector.fs — Set as Player writes the active zone's Player;
     AssetBrowser/Selection/Spawning resolve the active zone)

screen/
└── GameplayScreen.fs    # resolve active zone once per update/render

Engine.fs                # stats read the active zone; shutdown releases every zone's batch

test/
├── GameplayStateTests.fs  # NEW: add/switch/delete (names, bodies, resources, undo scoping)
├── SpawningTests.fs       # updated GameplayState construction
├── PlayerTests.fs         # updated for zone.Player and the new system signatures
└── fsharp-game.Tests.fsproj
```

**Structure Decision**: single project, existing domain clusters. There is no new
`world/ZoneMap.fs`: the container is a `Dictionary<string, Zone>` on
`GameplayState` (which already owns the undo map, camera, and editor state the
switch needs), so no `world/` module is added. The zones UI is a section inside the
existing `editor/Outliner.fs` because the dock layout is built in native cimgui
(`cimgui_setup_dock_layout`) and adding a docked window would require a native
rebuild. Naming uses one new `render/ImGui.fs` P/Invoke over an already-exported
symbol.

## Phase 0: Research

See [research.md](./research.md). It resolves the container shape (a plain
dictionary, no wrapper type), where the active zone and player designation are
addressed, the physics switch path, per-zone undo storage, the UI location, naming
by the developer, the input binding, the camera re-frame, and shutdown release.

## Phase 1: Design

- [data-model.md](./data-model.md) — the `GameplayState` container, the `Zone`
  additions, the naming buffer, validation rules, and the switch state transition.
- [contracts/zone-container.md](./contracts/zone-container.md) — the public surfaces
  added or changed (`GameplayState`, `Zone` lifecycle, `ImGui.inputText`,
  `EditorState`, `Systems`, the editor section).
- [quickstart.md](./quickstart.md) — runnable validation scenarios.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — no new type or module; one dictionary field, two lifecycle functions, one input binding, one Outliner section. |
| II. Fail Loudly | Pass — empty/duplicate name, unknown zone name, and non-actor designation all raise. |
| III. Spec-First | Pass. |
| IV. Data-Oriented | Pass — zones keyed by stable name; each zone's data contiguous. |
| V. Minimise GC | Pass — active zone resolved once per frame; switch path is cold; name buffer reused. |
