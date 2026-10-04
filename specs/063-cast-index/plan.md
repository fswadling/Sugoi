# Implementation Plan: Cast Index

**Branch**: `063-cast-index` | **Date**: 2026-10-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/063-cast-index/spec.md`

## Summary

Give the runtime a cast: an actor records the `Roster.Character` it plays as part of its **authored** state
(`ActorState.Cast`), and `GameplayState` holds an **index** from character to the actor playing it (its zone
and ref), so "which actor plays Aiko?" is one lookup and a character is **unique across all zones**. The index
is **maintained, not searched**: it is written at exactly the points an actor comes into or leaves existence —
a forward create or delete, an undo/redo of either, a zone delete, and a zone delete undone — which are all
reachable with the gameplay state (the undo/redo layer is the state's; `History` and the editor have it).
A spawn of a character already in the scene performs nothing, logging when the existing member is in another
zone. This feature adds **no cue ops**; addressing actors from a cue is the next feature.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: `Roster.Character` (the closed roster), `Casting.tryAsset` (the binding, unchanged),
`Entity`/`Zone` actor storage, `History` (the state-level command runner) — all present

**Storage**: N/A (the index is runtime state on the gameplay state; nothing persisted)

**Testing**: xUnit, headless (`dotnet test`); no device

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: none new — resolution is one dictionary lookup; the index is written only when an actor
enters or leaves existence (editor clicks, cue ops), never per frame

**Constraints**: one character at most across all zones; the build is warning-free; the suite is green

**Scale/Scope**: `world/Entity.fs` (+1 field), `state/GameplayState.fs` (index + helpers),
`editor/Spawning.fs` / `editor/CastPanel.fs` (the spawn path), `state/History.fs` + `editor/Selection.fs`
(the maintenance points); ~2–3 test files; no `.fsproj` change

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. One field, one map, a handful of one-line helpers; no new module, no new subsystem.
  The index is a maintainable structure over data that already exists, not a parallel model.
- **II. Fail Loudly** — PASS. `addCast`/`removeCast` assume the uniqueness invariant and fail loudly if a
  caller breaks it; the *spawn* no-op is deliberate (spec Q1 = A), and the surprising cross-zone case is logged
  rather than swallowed.
- **III. Spec-First** — PASS. This feature is `specs/063-cast-index`.
- **IV. Data-Oriented Architecture** — PASS. The cast role is a value on the actor's authored struct; the index
  is a dictionary keyed by a closed union.
- **V. Minimise GC Pressure** — PASS. Lookups are dictionary reads; writes happen only on actor-existence
  changes, off the frame.
- **N-I (narrative)** — PASS, relevant: the cast is capped ("no mechanism that only pays off at a larger
  scale"), which is why this is a small maintained map and not a general keyed collection.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/063-cast-index/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── cast-identity.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
world/Entity.fs          # ActorState gains Cast : Roster.Character option (authored)
state/GameplayState.fs   # Cast index + tryCastActor / addCast / removeCast / removeCastZone
editor/Spawning.fs       # spawnActor takes the cast and records it
editor/CastPanel.fs      # resolves the asset, enforces uniqueness (no-op + cross-zone log)
state/History.fs         # ActorCreate/ActorDelete arms + ZoneDelete-undo re-add
editor/Selection.fs      # delete routes through the state-level runner so the index updates in one place
test/*.fs                # resolution, uniqueness, and index exactness across undo/redo/zone delete
```

**Structure Decision**: The index is a field on `GameplayState` with helpers beside it, so no module is added
and no compile order changes — `GameplayState` and every updater (`History`, `Selection`, `Spawning`) already
compile after `Entity`, `Place` and `Roster`. `Command.fs` is deliberately untouched: it is zone-scoped and
cannot see the index, so the maintenance lives in the state-level runner (`History`) and the editor paths that
already hold the state (research D3).

## Complexity Tracking

> No Constitution Check violations.
