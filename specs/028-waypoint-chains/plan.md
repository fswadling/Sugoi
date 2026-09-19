# Implementation Plan: Waypoint Chains

**Branch**: `028-waypoint-chains` | **Date**: 2026-09-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/028-waypoint-chains/spec.md`

## Summary

Give a waypoint a successor, so waypoints form chains. The link is **zone data** —
a `Dictionary<EntityRef, EntityRef>` keyed by the source waypoint — rather than a
field on the waypoint, which keeps `Waypoint`, the shared
`Spawn`/`Delete`/`Duplicate` commands, and their restore path untouched, and makes
delete-then-undo restore a whole chain because a link is a generation-checked
reference that was never destroyed. A pure `waypointNext` resolves a link (target
alive, key matching exactly); one `setWaypointNext` writes or clears it and
refuses a self-link; a `Link` command (before/after) makes link and unlink edits
undoable; a composite `LinkSpawn` command makes "add next at camera" — create a
waypoint and link it — a single undo step, and lives in `Spawning` beside the
other spawns. The Outliner's Waypoints section gains *Add Next at Camera*, an
*Unlink* action, and a per-row *Link* action, and the viewport draws a connector
with a direction arrow for every link.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`)

**Primary Dependencies**: `System.Numerics` (the connector's vector arithmetic),
the existing `render/DebugDraw.fs` line accumulator (no new primitive), the custom
cimgui bindings for the section's buttons (`beginDisabled`/`endDisabled` already
bound). No new package, no native rebuild, no physics or asset involvement:
waypoints have no body, renderer, or binding.

**Storage**: N/A — in-memory zone data; the link map lives with the zone

**Testing**: xUnit via `dotnet test test/fsharp-game.Tests.fsproj`. The model and
the commands are headless-testable (`Zone.setWaypointNext`/`waypointNext`, the
`Link` and `LinkSpawn` commands through `UndoStack`, and `Selection`'s link edit);
the section's button gating and the connectors need an ImGui frame and are covered
by the quickstart's interactive walk.

**Target Platform**: Linux desktop, SDL3 + SDL_gpu, software 60 FPS cap

**Project Type**: single-project desktop game with a docked editor

**Performance Goals**: hold 60 FPS; a connector is three line segments appended to
the reused accumulator, and the per-waypoint resolution is one dictionary lookup —
no per-frame allocation

**Constraints**: a link is zone-scoped and never crosses zones; a stale link must
never resolve to the wrong waypoint, and must stay inert rather than being pruned
(pruning would need the delete command to capture it); no production code walks a
chain, so nothing may assume one is acyclic (research Decision 9)

**Scale/Scope**: tens of waypoints per zone, so a chain is a few links; the link
map is one entry per linked waypoint

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one map field, one read, one write, two command cases (one of which exists only so one gesture is one undo step), three Outliner actions, one viewport block. No new module, file, type, or dependency; no chain traversal is added for a consumer that does not exist (research Decision 9). |
| II. Fail Loudly | Pass — `setWaypointNext` raises on a self-link and on a target that is not a waypoint (preconditions the editor's disabled buttons make unreachable, per the precedent in `specs/024-multiple-zones`), while a *read* of an unresolvable link honestly answers "no next" rather than raising or silently repairing. |
| III. Spec-First | Pass — `specs/028-waypoint-chains/spec.md` is approved and drives this plan. |
| IV. Data-Oriented Architecture | Pass — the link map is ref-keyed struct pairs on the zone, resolved per lookup; no allocation, no wrapper type, and the waypoint record is untouched. |
| V. Minimise GC Pressure | Pass — no per-frame allocation: connectors append to the reused line accumulator and each resolution is a dictionary lookup with no boxing. |

No violations; the Complexity Tracking table is empty.

## Project Structure

### Documentation (this feature)

```text
specs/028-waypoint-chains/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── waypoint-links.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
world/
└── Zone.fs              # + Links : Dictionary<EntityRef, EntityRef>; + waypointNext,
                         # + setWaypointNext. Nothing else in the module changes.

state/
└── Command.fs           # + Link (before/after next) and + LinkSpawn (composite extend)

editor/
├── Spawning.fs          # + spawnLinkedWaypoint (create + link + one command)
├── Selection.fs         # + linkRef (point at a waypoint, or break the link)
├── Outliner.fs          # Waypoints section: Add Next at Camera, Unlink, per-row Link
└── Viewport.fs          # + connectors: a link line to the segment midpoint with an arrow head

test/
├── ZoneTests.fs         # link resolution, self-link refusal, staleness, per-zone isolation
├── SpawningTests.fs     # the extend gesture is one LinkSpawn step and undoes cleanly
└── SelectionTests.fs    # link/unlink are one Link step; delete + undo restores a chain
```

**Structure Decision**: single project, existing domain clusters, and the existing
`test/` modules — no new source or test file, so no `.fsproj` change and no
compile-order risk. The link lives on `Zone` (the module that owns entity storage
and, already, the `BodyToEntity` relationship map), is made undoable through
`Command` like every other edit, and is authored through `Spawning`/`Selection`
like every other spawn and ref-addressed edit, so the Outliner and the viewport
only draw and dispatch.

## Phase 0: Research

See [research.md](./research.md). It decides the link's home (zone data, not a
field on the waypoint, with the reference's shape and the command-coupling
reasoning recorded), the pure resolution rule, the single write that covers
linking and unlinking, the retention of inert orphan entries, the two command
cases and why the extend gesture needs a composite one, where the gesture lives,
the connector geometry and the coincidence guard the reference lacks, the Outliner
actions' placement and disabled-not-hidden presentation, and the deliberate
absence of a production chain walk.

## Phase 1: Design

- [data-model.md](./data-model.md) — the `Links` map, the resolution and write
  rules, the two commands, the Outliner actions, the connector, the state
  transitions, and the validation rules.
- [contracts/waypoint-links.md](./contracts/waypoint-links.md) — the public
  surfaces added or changed (`Zone`, `Command`, `Spawning`, `Selection`, the
  Outliner section, the viewport connectors).
- [quickstart.md](./quickstart.md) — runnable validation scenarios.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — as checked: two `Zone` functions, two command cases, three UI actions, one viewport block; nothing speculative. |
| II. Fail Loudly | Pass — write preconditions raise; an unresolvable link reads as "no next" without being silently rewritten. |
| III. Spec-First | Pass. |
| IV. Data-Oriented | Pass — ref-keyed map on the zone, resolution per lookup. |
| V. Minimise GC | Pass — no per-frame allocation introduced. |
