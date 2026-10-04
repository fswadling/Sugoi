# Implementation Plan: Actor Cue Ops

**Branch**: `064-actor-cue-ops` | **Date**: 2026-10-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/064-actor-cue-ops/spec.md`

## Summary

Give the cue the actor **lifecycle**: **`SetActor`** and **`RemoveActor`**. A set names a `(Place,
Roster.Character)` pair and brings a member into that place, placing her **at a waypoint** (standing
at its position, facing along its orientation) whether she is not in the scene yet (a spawn) or already there
(a move) — one op replacing odin's `AddActor` and `WarpActor`. Resolution is the runtime cast index (`specs/063`):
a member already **anywhere** is reused — moved across zones when the place differs — keeping her existing art;
only a member nowhere is spawned, resolving her asset from `(character, look)` through `Casting.tryAsset`. A
remove names the character alone and drops her actor wherever the index says she is. Every op is a **no-op** on
a place that is not present or on state it cannot act on, so a cue is safe to re-run. No new module: the ops go in `world/Cue.fs`, their
effects and guards in `world/CueEngine.fs`, a shared base-entity constructor in `world/Entity.fs` (the rule of
three), and editor entries in `editor/CueTree.fs` / `editor/CueEditor.fs`.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: `Place` (closed zone identity), `Roster.Character` (closed cast), `Casting.Look` +
`Casting.tryAsset` (the binding), the cast index (`GameplayState.tryCastActor` / `addCast` / `removeCast`,
`specs/063`), `Zone.tryWaypointByName` (waypoint resolution), `Zone.spawnActor` / `setTransform` / `removeActor`,
`Physics.removeBody` (detach for an inactive place), `Prime` — all present

**Storage**: N/A (nothing persisted; the cue and the runtime scene stay in memory, roadmap phase 15)

**Testing**: xUnit, headless (`dotnet test`); no device — a fabricated `ActorAsset` with defaulted GPU handles
lets the real spawn/remove paths run, as `specs/063`'s `test/CastIndexTests.fs` established

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: none new — each op runs once when the walk reaches it and is a no-progress leaf (an idle
cue still allocates nothing); the only per-op work is a dictionary lookup (the place) and, on an add, one
body build

**Constraints**: actor ops name only a declared place and a rostered character; one character at most across all
zones (`specs/063`); the build is warning-free; the suite is green

**Scale/Scope**: 3 source files (`world/Cue.fs`, `world/CueEngine.fs`, `world/Entity.fs`), 4 editor files
(`editor/CueTree.fs`, `editor/CueEditor.fs`, and the two delegations in `editor/Spawning.fs` /
`editor/Outliner.fs`); 2 test files; no `.fsproj` change

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. Two leaf ops, one resolution path (the cast index built in `specs/063`), one small
  entity constructor. No new module, no new state, no new subsystem. Movement/locomotion/animation ops are
  explicitly deferred (spec FR-010).
- **II. Fail Loudly** — PASS, with the same recorded nuance as `specs/062`. The **editor's** functions
  (`GameplayState.addCast` and the `Zone` builders) keep raising on a precondition they are handed wrongly; only
  the **cue layer** turns a redundant or contradictory op into a no-op, because a cue is runtime data that may
  run twice (spec FR-006). The guard lives in `CueEngine`, so the loud behaviour is not diluted at its source.
- **III. Spec-First** — PASS. This feature is `specs/064-actor-cue-ops`.
- **IV. Data-Oriented Architecture** — PASS. The ops are union cases carrying closed-union values; no new heap
  structure. A placement goes through the same `Zone.spawnActor` / `setTransform` everything else uses.
- **V. Minimise GC Pressure** — PASS. The ops are no-progress leaves; the guards are dictionary lookups; a
  spawn builds one body, off the per-frame path.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/064-actor-cue-ops/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── actor-cue-ops.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
world/Cue.fs              # add SetActor / RemoveActor (leaves carrying Place + Roster.Character)
world/CueEngine.fs        # their effects and guards: resolve place → index → spawn/move/remove; detach
                          #   a body built into an inactive place
world/Entity.fs           # baseEntity: the shared new-entity literal (rule of three)
editor/Spawning.fs        # Spawning.baseEntity delegates to Entity.baseEntity (one literal, not two)
editor/Outliner.fs        # newEntity delegates to Entity.baseEntity (the third call site)
editor/CueTree.fs         # kinds, labels, defaultOfKind, withPlace, withCast, textOf/withText, selectable
editor/CueEditor.fs       # the set op's waypoint chooser against the *named place's* zone
test/CueTests.fs          # the ops' effects and no-ops, through CueEngine.tick
test/CuePanelTests.fs     # labels, kinds, writes for the new ops
test/CastIndexTests.fs    # (or CueTests) the index stays exact through a cue set / move / remove
```

**Structure Decision**: No module moves and no compile-order change. `Place` (`world/Place.fs`),
`Roster` (`cast/Roster.fs`), `Casting` (`world/Casting.fs`) all precede `Cue` (`world/Cue.fs`), so the op may
name a `Place`, a `Roster.Character` and a `Casting.Look`. `Zone` and `Entity` precede `CueEngine`; the cast
index lives on `GameplayState`, which `CueEngine` already reads. `Spawning`/`Outliner` are compiled after
`Entity`, so the base-entity constructor can move down without a cycle. The cue's actor changes are **runtime**,
carried by the state-level walk exactly as `SetZone` and the zone ops are — never a step on the undo history
(spec FR-007).

## Complexity Tracking

> No Constitution Check violations.
