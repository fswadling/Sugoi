# Implementation Plan: Waypoints

**Branch**: `026-waypoints` | **Date**: 2026-09-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/026-waypoints/spec.md`

## Summary

Give a zone waypoints: named points of interest that record where to stand and
which way to face. A waypoint becomes a first-class zone entity by composing the
shared entity base (name, transform, editor flags) and living in its own slot map
on the zone, so the editor's existing selection, gizmo, inspector, duplicate,
delete, and undo machinery applies to it with no special cases. The Outliner
gains a **Waypoints** section that lists them and offers two deliberate adds —
*Add Waypoint* at the origin and *Add at Camera* at the editor camera's position
and orientation — and the viewport draws every waypoint as a marker with a
facing shaft and arrow head, highlighted when selected. Waypoints carry no
renderer, collider, or asset binding: they are authoring data for later phases,
and adding one changes nothing about collision or geometry.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`)

**Primary Dependencies**: `System.Numerics` (`Vector3`, `Quaternion`,
`Matrix4x4` — the project's value-type math), SDL3-CS/SDL_gpu via the existing
`render/DebugDraw.fs` line accumulator, custom cimgui bindings
(`render/ImGui*.fs`) for the Outliner controls. JoltPhysicsSharp is used
unchanged and is not involved in waypoints: they have no bodies.

**Storage**: N/A — in-memory zone data; persistence is a later phase

**Testing**: xUnit via `dotnet test test/fsharp-game.Tests.fsproj` (flat
`test/<Module>Tests.fs`); the waypoint behaviour is headless-testable through
`Zone`, `Spawning`, `Selection`, and `UndoStack`, so no display is needed

**Target Platform**: Linux desktop, SDL3 + SDL_gpu, software 60 FPS cap

**Project Type**: single-project desktop game with a docked editor

**Performance Goals**: hold 60 FPS with waypoints present; markers are appended
to the existing pre-allocated line accumulator with no per-frame allocation; no
body, shape, or GPU resource is created per waypoint

**Constraints**: the debug-draw pipeline is lines only (no cone or triangle
primitive), so an arrow head must be composed from line segments; no new source
file is added, so the `.fsproj` compile order is unchanged; a waypoint has no
collider, so it is not click-pickable in the viewport and is selected from the
Outliner

**Scale/Scope**: tens of waypoints per zone; no spatial indexing or culling

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — a waypoint is the shared `Entity` base and nothing else; no new module, no new type for the container, no new dependency, and no cone primitive added to the line pipeline (the arrow head is four line segments composed at its single call site). |
| II. Fail Loudly | Pass — a waypoint that carries no asset resolves `bindingNone` explicitly rather than falling into the catch-all that raises, and duplicating a kind with no entity still raises. Nothing silently returns a default for a waypoint. |
| III. Spec-First | Pass (retrospective) — `specs/026-waypoints/spec.md` records the ratified behaviour; this plan and its tasks were written to it afterwards. |
| IV. Data-Oriented Architecture | Pass — `Waypoint` is a `[<Struct>]` payload composing `Entity`, held in the zone's pre-sized `SlotMap<Waypoint>`; there is no inheritance and no heap wrapper. |
| V. Minimise GC Pressure | Pass — adding, moving, and rendering waypoints allocate nothing per frame: markers append to the reused line accumulator, and a transform edit writes the slot in place. |

No violations; the Complexity Tracking table is empty.

## Project Structure

### Documentation (this feature)

```text
specs/026-waypoints/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── waypoints.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
world/
├── Entity.fs            # Waypoint composes Base : Entity; + waypointRef
├── Zone.fs              # + Waypoints : SlotMap<Waypoint>; isAlive / tryEntityAt /
│                        # setTransform / removeEntity / setActorWithAsset cover it;
│                        # + bindingNone, bindingAt returns it, + spawnWaypoint
└── (unchanged: Actor.fs, EnvironmentBatch.fs, Systems.fs, Assets.fs)

editor/
├── Spawning.fs          # + spawnWaypoint; + Waypoint case in duplicate
├── Outliner.fs          # + Waypoints section: rows, Del/Dup, Add Waypoint, Add at Camera
├── Viewport.fs          # + waypoint markers: box, facing shaft, arrow head, selection colour
└── (unchanged: Selection.fs, Inspector.fs, Editor.fs — they already work by ref)

test/
├── EntityTests.fs       # Waypoint composes the shared base
├── ZoneTests.fs         # waypoint refs: spawn/resolve, transform, remove, slot reuse, own map
└── SpawningTests.fs     # duplicate a waypoint; delete a waypoint and undo
```

**Structure Decision**: single project, existing domain clusters, and **no new
source file** — so no `.fsproj` change and no compile-order risk. `world/Entity.fs`
still precedes `world/Zone.fs`, and `editor/` still follows `state/`, which is all
the ordering this feature needs. Waypoint storage is a slot map on the zone (not
a new container module) because a waypoint is an entity addressed by
`EntityRef`, exactly like actors and props, and reusing that machinery is what
makes selection, duplicate, delete, and undo work unchanged. The Outliner is
already the entity-hierarchy panel and needs no native dock-layout change, so the
Waypoints section is a collapsing header inside it rather than a new panel.

## Phase 0: Research

See [research.md](./research.md). It decides the storage shape (a slot map on the
zone), why a waypoint composes the shared entity base rather than carrying a bare
position, why it carries no body/renderer/binding, the facing convention and the
camera-seeded rotation, how an arrow head is drawn with a line-only pipeline, the
Outliner placement and its two add actions, and the scope boundaries (rename,
viewport picking, triggers).

## Phase 1: Design

- [data-model.md](./data-model.md) — the `Waypoint` record and its ref, the zone
  storage, the `bindingNone` binding, the operations added, the editor surfaces,
  the state transitions, and the validation rules.
- [contracts/waypoints.md](./contracts/waypoints.md) — the public surfaces added
  or changed (`Entity`, `Zone`, `Spawning`, the Outliner section, the viewport
  marker).
- [quickstart.md](./quickstart.md) — runnable validation scenarios.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one record field change, one ref constructor, one slot map, one spawn function, one Outliner section, one viewport block; no new module, file, or dependency. |
| II. Fail Loudly | Pass — `bindingAt` answers for a waypoint instead of raising, and every other kind without a binding still raises. |
| III. Spec-First | Pass (retrospective). |
| IV. Data-Oriented | Pass — struct payload in a pre-sized slot map; refs carry index + generation. |
| V. Minimise GC | Pass — no allocation on add, move, render, or delete paths. |
