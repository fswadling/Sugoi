# Implementation Plan: Entity Command Families

**Branch**: `030-entity-command-families` | **Date**: 2026-09-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/030-entity-command-families/spec.md`

## Summary

Replace the editor's shared create/destroy/copy command record — a base `Entity`
plus an `AssetBinding`, the part every kind has in common — with **one create and
one delete per entity kind**, each carrying that kind's own record. The shared
record is exactly the problem: a kind's authored state beyond it has nowhere to be
recorded, which is why a trigger needed its own command set (`029`), a waypoint's
link needed a zone-level table (`028`), and an actor's collider overlay is silently
reset by a restore today.

The payload is the kind's own record, and the two directions of it are deliberate
mirrors. The **capture** clears the derived and runtime fields (`Physics`,
`Renderer`, `Triggered`) with a record-update, so a field added to a kind later is
carried automatically and a recorded command cannot retain a released renderer or a
stale body id. The **restore** builds the record field by field rather than updating
the payload, so a field added to a kind is a build failure until its author
classifies it as authored, derived, or runtime — which is how FR-006 and SC-004 hold
for a property and not only for a kind.

With that in place the waypoint's link moves **onto the waypoint** (`Waypoint.Next`)
and the zone's `Links` table is deleted, so a link travels with the waypoint it
belongs to and no orphan can accumulate. Kind-specific property edits stay in the
kind's family (`TriggerSet`, `Link`); edits of the shared base (`Transform`,
`Rename`) and the zone-level environment swap are unchanged.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`)

**Primary Dependencies**: none new. `System.Numerics` for the shared base;
the existing `world/Zone.fs` storage and physics/asset restore paths; xUnit for
the tests.

**Storage**: N/A — in-memory zone data; undo history is per zone (`UndoByZone`) and
does not survive a restart, so no recorded command needs migrating.

**Testing**: xUnit via `dotnet test test/fsharp-game.Tests.fsproj`. The model and
the commands are headless: the per-kind create/delete round-trip (`test/ZoneTests.fs`),
the create and extend gestures as one step each (`test/SpawningTests.fs`), and the
editor paths (`test/SelectionTests.fs`). The only interactive surface is the
editor itself, covered by the quickstart.

**Target Platform**: Linux desktop, SDL3 + SDL_gpu, software 60 FPS cap

**Project Type**: single-project desktop game with a docked editor

**Performance Goals**: hold 60 FPS. This is an editor-model change with no
per-frame path added: the systems still scan their own kind's slot map with no
kind check (FR-010), and a captured payload is copied only on an editor action.

**Constraints**: no new payload type and no new module; `EntityKind` matches
become exhaustive (the `| _ ->` catch-alls go) so a kind with no create/delete path
is a non-exhaustive match the compiler names — a warning, not a promoted error —
and `occupy*` constructs its record field by field, so a field added to a kind fails
to build until it is classified (FR-006, SC-004); a recorded
payload must not carry live rendering or physics resources (FR-007); every existing
gesture keeps its current undo granularity (FR-009), including the transform drag
merge.

**Scale/Scope**: five entity kinds, so the command type grows from 11 to 16 cases
and `apply`/`undo` from 10 arms each to 16 — every new arm a one-line delegation to
a per-kind `Zone` function; six source modules and four to six test modules change.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass, with the growth recorded below — the change **removes** a mechanism (the shared record and its lossy inverse) rather than adding one, and removes two of the three workarounds the current code carries. The command type does grow (11 → 16 cases, 10 → 16 `apply`/`undo` arms) and the per-kind paths look alike, but each restore path is genuinely different — a capsule body and actor renderer, a mesh body and environment renderer, an epoch-checked part, a bare slot, a default shape — so the arms are not copies of one another. No new payload type, module, or dependency. |
| II. Fail Loudly | Pass — this is the principle the feature serves. `setActorWithAsset`'s and `removeEntity`'s `| _ -> ()` catch-alls disappear: a kind with no create or delete path is a non-exhaustive match, not a silent no-op. `occupy*` constructs its record field by field, so a field added to a kind fails to build until it is classified rather than passing through unhandled. The write preconditions that exist today (a self-link, a slot that is not free, a trigger ref of another kind) all remain. |
| III. Spec-First | Pass — `specs/030-entity-command-families/spec.md` is approved (clarifications resolved: the collider overlay, the family shape, the link's home, the duplicate rule, and property edits) and drives this plan. |
| IV. Data-Oriented Architecture | Pass — the per-kind slot maps are untouched (FR-010), every payload is a struct, and the waypoint's link is a value on the waypoint rather than a side table. |
| V. Minimise GC Pressure | Pass — no per-frame allocation is added; a captured payload is a struct copy on an editor action, and blanking its derived fields means a recorded command holds no reference to a released renderer. |

## Complexity Tracking

The one complexity this feature adds, and why it is warranted against Minimalism.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| The command type grows from 11 to 16 cases, and `apply`/`undo` from 10 arms each to 16 | One create and one delete per kind is the only way a command can carry a kind's *own* authored state; the shared record that made the old cases fewer is precisely what could not carry it, so the growth is the mechanism by which FR-001 to FR-008 and FR-013 to FR-015 are satisfiable at all | A single family with a kind-tagged payload union (3 cases, one dispatch): the owner's decision was one family per kind, matching the entity model's one-storage-per-kind shape and its refusal to discriminate on the hot path (FR-010). Keeping the shared record for the kinds it already fits (props and parts): a new kind could then be added against a path that records less than its state, which is exactly what FR-006 forbids, and a prop's fit today is a coincidence of its current fields. Each new arm is a one-line delegation to a per-kind `Zone` function, and the arms differ in substance rather than in shape |

## Project Structure

### Documentation (this feature)

```text
specs/030-entity-command-families/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── entity-commands.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
world/
├── Entity.fs            # Waypoint gains `Next : EntityRef option`. Nothing else.
└── Zone.fs              # `Links` removed; per-kind capture/occupy/remove replace
                         # `setActorWithAsset`, `removeEntity`'s dispatch, and
                         # `occupyTrigger`'s old role; `waypointNext`/`setWaypointNext`
                         # read and write the record; `EntityKind` matches become
                         # exhaustive.

state/
└── Command.fs           # `Spawn`/`Delete`/`Duplicate`/`TriggerDuplicate` replaced by
                         # per-kind `*Create`/`*Delete`; `Link`/`LinkSpawn`/`TriggerSet`/
                         # `Transform`/`Rename`/`SetEnvironment` keep their shape.

editor/
├── Spawning.fs          # Spawn and duplicate both build the same per-kind create.
└── Selection.fs         # Per-kind delete capture replaces the trigger special case.

test/
├── EntityTests.fs       # the `Waypoint` shape
├── ZoneTests.fs         # per-kind create/delete round-trip; link on the record
├── SpawningTests.fs     # create/extend are one step each; a copy carries the link
└── SelectionTests.fs    # link edit is one step; delete + undo restores in full
```

**Structure Decision**: single project, existing domain clusters, existing modules
only — no new source or test file, so `fsharp-game.fsproj` and
`test/fsharp-game.Tests.fsproj` need no `<Compile>` entry and no compile-order risk.
The kind dispatch that already lives in `Zone` (`isAlive`, `tryEntityAt`,
`setTransform`, `setName`) stays there; the change is that the *create/destroy*
dispatch moves out of a kind-agnostic payload and into one command per kind, which
is what makes each kind's authored state recordable. `editor/Viewport.fs` is
deliberately untouched — the connector still reads `Zone.waypointNext` — and the
Inspector's overlay toggle is its one change, routed through `Zone.setShowCollider` so
that the Inspector and a duplicate share a single write. The overlay stays a direct
write rather than an undo step (FR-003), and is carried by the record a delete or a
copy captures.

## Phase 0: Research

See [research.md](./research.md). It decides the per-kind family shape, the payload
being each kind's own record with the capture and restore as deliberate mirrors, why
`Spawn`/`Duplicate`/`TriggerDuplicate` merge into one create, the link moving onto
the waypoint with the zone table deleted, what "the kind's own family" covers for
property edits, and the shape of the `Zone` surface that results.

## Phase 1: Design

- [data-model.md](./data-model.md) — each kind's authored/derived/runtime split,
  the `Waypoint` record change, the command table with apply/undo per case, the
  link-resolution rule, state transitions, and the validation rules.
- [contracts/entity-commands.md](./contracts/entity-commands.md) — the public
  surfaces that change (`Entity`, `Zone`, `Command`, `Spawning`, `Selection`).
- [quickstart.md](./quickstart.md) — runnable validation scenarios.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — as checked: one mechanism replaces three, no new type or module, and the accepted growth is recorded in Complexity Tracking. |
| II. Fail Loudly | Pass — no `| _ -> ()` remains on a create, delete, or restore path, and the restore's explicit construction surfaces a new field at build time. |
| III. Spec-First | Pass. |
| IV. Data-Oriented | Pass — per-kind storage and struct payloads unchanged. |
| V. Minimise GC | Pass — no per-frame allocation; a payload holds no released resource. |
