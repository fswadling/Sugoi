# Implementation Plan: Triggers

**Branch**: `029-triggers` | **Date**: 2026-09-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/029-triggers/spec.md`

## Summary

Give a zone trigger volumes. A `Trigger` composes the shared `Entity` base and
adds a radius, a height, an interactable flag, a prompt, and a mutable
player-inside flag, so it takes part in the same selection, gizmo, Inspector,
rename, duplicate, delete and undo paths the zone's other entities use — the shape
`specs/026-waypoints` established for a new stored kind. The zone gains a
`Triggers` slot map and the trigger cases of the shared operations; the viewport
gains a cylinder primitive that draws each volume to scale; the Inspector gains
radius/height/interactable/prompt/enabled edits, committed as one `TriggerSet`
step each; and one `Systems.updateTriggers` pass reports who is inside and which
interactable trigger is nearest, from which a play-mode overlay shows the prompt
and the names of the triggers the player is in. **Nothing runs a cue**: the
rising edge and the interaction are left to the cue engine, which the reference
project deferred identically.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`)

**Primary Dependencies**: the existing `render/DebugDraw.fs` line accumulator (one
new cylinder primitive), the custom cimgui bindings (`igDragFloat` and
`isItemDeactivatedAfterEdit` are both already exported by the vendored
`libcimgui.so`, so two P/Invokes and no native rebuild), `System.Numerics` for the
containment arithmetic. No physics, asset, or renderer involvement: a trigger has
none.

**Storage**: N/A — in-memory zone data; a trigger's size and flags are authored
data and its inside state is runtime state

**Testing**: xUnit via `dotnet test test/fsharp-game.Tests.fsproj`. Everything
except the Inspector widgets and the wireframe is headless: the ref/storage
behaviour in `test/ZoneTests.fs`, creation and duplication in
`test/SpawningTests.fs`, and the containment rule in a new `test/SystemsTests.fs`
(the update needs no physics world and no GPU — FR-012). The widgets, the
wireframe, and the overlay are covered by the quickstart's interactive scenarios.

**Target Platform**: Linux desktop, SDL3 + SDL_gpu, software 60 FPS cap

**Project Type**: single-project desktop game with a docked editor

**Performance Goals**: hold 60 FPS; the update is one pass over the active zone's
triggers writing a bool in place, with no allocation (SC-007)

**Constraints**: detection must run over a zone and a player position alone, with
no window and no GPU; a trigger takes part in no physics and is not click-picked;
the play-mode overlay is the only place detection is visible until cues exist

**Scale/Scope**: tens of triggers per zone; every trigger is visited every play
frame

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one payload type, one zone slot map with the existing shared operation cases, one `TriggerSet` command, one detection function, one cylinder primitive, two P/Invokes, and one Inspector block. No trigger-specific command family, no separate scan/system pair, and no rising-edge output for a consumer that does not exist (research Decision 5). |
| II. Fail Loudly | Pass — the trigger's writes go through the same kind-dispatched `Zone` operations as the other entities, whose guards already raise on a stale or taken slot; the containment rule has no error path to swallow, and a query for a kind that has no trigger simply does not resolve. |
| III. Spec-First | Pass — `specs/029-triggers/spec.md` is approved and drives this plan. |
| IV. Data-Oriented Architecture | Pass — the payload is a struct in a pre-sized slot map, addressed by a generation-checked ref; the inside flag is written in place, and the update allocates nothing. |
| V. Minimise GC Pressure | Pass — the update is one byref write per trigger and no allocation; the overlay reads state it already has. |

No violations; the Complexity Tracking table is empty.

## Project Structure

### Documentation (this feature)

```text
specs/029-triggers/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── triggers.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
world/
├── Entity.fs            # Trigger composes Entity; Trigger columns alongside it
├── Zone.fs              # + Triggers : SlotMap<Trigger>; + triggerRef; + trigger cases
│                        #   in isAlive / tryEntityAt / setTransform / removeEntity /
│                        #   setActorWithAsset / bindingAt; + setTrigger
└── Systems.fs           # + updateTriggers (containment + closest interactable)

state/
├── Command.fs           # + TriggerSet (before/after payload)
└── GameplayState.fs     # + InteractableTrigger (the tracked one, per frame)

render/
├── ImGui.fs             # + dragFloat P/Invoke
└── DebugDraw.fs         # + cylinder primitive

editor/
├── Spawning.fs          # + spawnTrigger; + Trigger case in duplicate
├── Outliner.fs          # Triggers section: Add Trigger + rows
├── Inspector.fs         # trigger block: radius, height, interactable, prompt, enabled
└── Viewport.fs          # trigger cylinder wireframe

screen/
└── GameplayScreen.fs    # call updateTriggers while playing; draw the play overlay

test/
├── EntityTests.fs       # Trigger composes the shared base
├── ZoneTests.fs         # trigger refs, setTrigger, and the shared operations
├── SpawningTests.fs     # spawnTrigger / duplicate; the no-entity list loses Trigger
└── SystemsTests.fs      # NEW: the containment rule and interactable tracking
```

**Structure Decision**: single project, existing domain clusters, one new **test**
module registered in `test/fsharp-game.Tests.fsproj` (as `specs/027-entity-rename`
did for `SelectionTests`). No new source file: the trigger is a payload type, its
storage and writes are cases in modules that already dispatch by kind, its
detection is a function on the existing `Systems`, and its UI is a section, a block,
and a wireframe in the panels that already host those. The trigger's properties
are edited through a command in `Command` rather than inline in the Inspector, so
every authored change is recorded the same way.

## Phase 0: Research

See [research.md](./research.md). It decides composition over a flat record, the
runtime flag's mutability and invisibility, storage via the shared operations, one
detection function deriving the player position, the absent rising-edge output and
why the spec still requires entry to be distinguishable, the tracked-interactable
field, the whole-record edit command, the new cylinder primitive, the drag-widget
binding, and the play-mode overlay that stands in for the deferred cue.

## Phase 1: Design

- [data-model.md](./data-model.md) — the payload, the zone storage and writes, the
  edit command, the detection function and its rule, the editor surfaces, the
  state transitions, and the validation rules.
- [contracts/triggers.md](./contracts/triggers.md) — the public surfaces added or
  changed (`Entity`, `Zone`, `Systems`, `Command`, `GameplayState`, `DebugDraw`,
  `ImGui`, the Outliner, the Inspector, the viewport, the gameplay screen).
- [quickstart.md](./quickstart.md) — runnable validation scenarios.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — as checked: one payload, one command, one update, one primitive, two bindings; nothing speculative, and the deferred cue is pinned by FR-010. |
| II. Fail Loudly | Pass — writes reuse the shared guarded operations. |
| III. Spec-First | Pass. |
| IV. Data-Oriented | Pass — struct payload, byref writes, no per-frame allocation. |
| V. Minimise GC | Pass — no allocation added to the play loop. |
