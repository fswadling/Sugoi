# Implementation Plan: Actors Addressed by Name — Retiring the Cast Role and its Index

**Branch**: `072-actor-name-addressing` | **Date**: 2026-10-09 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/072-actor-name-addressing/spec.md`

## Summary

Retire the two things `specs/063-cast-index` added: the actor's **cast role** (`ActorState.Cast`) and the
gameplay state's **runtime cast index** (`GameplayState.Cast`, with `tryCastActor`/`addCast`/`removeCast`/
`removeCastZone`). An actor is then addressed by its **name**, resolved by a scan of the present places —
the same pattern `Zone.tryWaypointByName` / `tryTriggerByName` already use — so nothing has to be kept
exact at the five or six sites an actor comes into or leaves existence. The roster stays (`Roster.Character`
is still the closed set a cue names), the look vocabulary and cast **binding** stay, and the **cast panel**
stays (it authors the binding). The dialogue portrait path is untouched, because it resolves through the
binding and never through the actor. The creation op is renamed **`SetActor` → `AddActor`** and made a
**pure spawn** (no upsert), and the ops that act on an existing actor (remove, move, rotate, blends, player
toggles) resolve by name across the present places. No module moves and no compile order changes.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: `Roster` (`cast/Roster.fs`, unchanged — the closed roster), `Casting` (unchanged —
looks + binding), `Place` (`world/Place.fs`, for the closed place union and its `all` order), `Zone`
(`world/Zone.fs`, gains `tryActorByName`; `spawnActor` drops its cast argument), `GameplayState`
(loses `Cast`; gains a name-scan resolver), `Cue` / `CueEngine` (the actor ops, renamed/re-based),
`CueTree` / `CueEditor` (authorship). All present; no new dependency.

**Storage**: N/A (nothing persisted; the runtime scene stays in memory)

**Testing**: xUnit, headless (`dotnet test`); no device — a fabricated `ActorAsset` with defaulted GPU
handles via the `test/CastIndexTests.fs` fixture style, retargeted at name resolution

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: none new — each op runs once when the walk reaches it and is a no-progress leaf. The
only added work is a name scan over the present places × their actor slots (a closed `Place` union and a
handful of actors), which allocates nothing; an idle cue still allocates nothing.

**Constraints**: the roster, look and place remain closed unions; **no** uniqueness invariant is enforced
(a name is a label); the build is warning-free; the suite is green; no devices opened.

**Scale/Scope**: ~6 source files (`world/Entity.fs`, `world/Zone.fs`, `world/Cue.fs`,
`world/CueEngine.fs`, `state/GameplayState.fs`, `state/History.fs`), ~6 editor files (`editor/Spawning.fs`,
`editor/CastPanel.fs`, `editor/Outliner.fs`, `editor/ScenarioPanel.fs`, `editor/Inspector.fs`,
`editor/CueEditor.fs`, `editor/CueTree.fs`), ~6 test files; 3 docs; **no `.fsproj` change**.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. The feature is net-removal: it deletes a field, a dictionary, four accessors and
  their maintenance sites, and adds one scan helper (`Zone.tryActorByName`) and one resolver. The
  `Command` module's vestigial `open Casting` goes with it.
- **II. Fail Loudly** — PASS, with the same nuance `specs/062`/`064` record. `addCast`'s `invalidOp` (the
  uniqueness guard) is **deliberately removed**, not silenced — the spec withdraws the invariant (FR-008), so
  a duplicate is expected data rather than a bug. Precondition failures elsewhere (e.g. `Zone.spawnActor` on a
  live slot) still raise, and the **cue layer** keeps its no-op rule (`specs/064` D7): an op that cannot act
  on state performs nothing and continues.
- **III. Spec-First** — PASS. This feature is `specs/072-actor-name-addressing`.
- **IV. Data-Oriented Architecture** — PASS. Resolution is a scan of the zones' contiguous actor slot maps
  keyed by the entity's `Name`; no new heap structure, and one `Dictionary` is removed from `GameplayState`.
- **V. Minimise GC Pressure** — PASS. A scan reads `Span`-backed slot maps and compares strings; it allocates
  nothing. The ops remain no-progress leaves off the per-frame path.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/072-actor-name-addressing/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── actor-name-addressing.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
world/Entity.fs           # ActorState loses `Cast` (the only change here)
world/Zone.fs             # spawnActor drops its `cast` argument; add `tryActorByName` (name scan)
world/Cue.fs              # `SetActor` → `AddActor`; comments lose the index rationale
world/CueEngine.fs        # `setActor` → `addActor` (pure spawn); `removeActor` + `tryResolveActor`
                          #   re-based on the name scan
state/GameplayState.fs    # remove `Cast` + `tryCastActor`/`addCast`/`removeCast`/`removeCastZone`;
                          #   add a resolver scanning the present places in `Place.all` order
state/History.fs          # remove the cast-index reconciliation in ActorCreate/ActorDelete/ZoneDelete
state/Command.fs          # drop the now-unused `open Casting`
editor/Spawning.fs        # spawnActor drops the cast argument and the duplicate guard
editor/CastPanel.fs       # the Spawn control passes no cast (its assignment behaviour is unchanged)
editor/Selection.fs       # deleteRef's comment no longer cites the cast index
editor/CueTree.fs         # "Add actor" label/kind/default; withCharacter/withLook; executing/children
editor/CueEditor.fs       # assetNames reads the binding only (no placed-actor lookup)
editor/Outliner.fs        # remove `castTags` and its use on the zone rows
editor/ScenarioPanel.fs   # remove `actorTags` and its use
editor/Inspector.fs       # remove the actor's Character readout
test/CueTests.fs          # AddActor is a pure spawn; remove/move/blend/player resolve by name
test/CuePanelTests.fs     # labels, kinds, write helpers under the new name
test/SpawningTests.fs     # spawn path without a cast argument
test/ScenarioTests.fs     # a scenario captures actors with no role; tags removed
test/PlayerTests.fs       # cue player toggles resolve by name
test/GameplayStateTests.fs# no cast index
test/CastIndexTests.fs    # RETIRED → replaced by a name-resolution suite (Zone.tryActorByName)
docs/ARCHITECTURE.md      # the runtime-cast paragraph
docs/ROADMAP.md           # the phase-10 cast paragraph
docs/EDITOR_DESIGN.md     # D-022 / workspace references to the runtime cast
```

**Structure Decision**: No module moves and no compile-order change. `Zone.tryActorByName` sits beside the
existing `tryWaypointByName` / `tryTriggerByName` in `world/Zone.fs`; the cross-place resolver belongs on
`GameplayState` (which already owns `Zones`) and is read by `CueEngine` and `CueEditor`. `Entity`, `Zone` and
`Place` all precede the consumers that need them. The cast **binding** stays on `SharedState`; the roster
stays in `cast/Roster.fs`; `world/Dialogue.fs` and `world/DialogueRender.fs` are untouched. The cue's actor
ops remain **runtime** effects carried by the state-level walk, never steps on the undo history
(`specs/064` FR-007).

## Complexity Tracking

> No Constitution Check violations.
