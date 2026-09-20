# Implementation Plan: Entity Authored State

**Branch**: `031-entity-authored-state` | **Date**: 2026-09-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/031-entity-authored-state/spec.md`

## Summary

Give every kind that carries derived or runtime state an **authored value of its own**
— `ActorState`, `PropState`, `TriggerState` — held by the entity as its `State` field,
so that a command payload *is* that value and cannot hold a body, a renderer, or a
runtime flag. The boundary stops being maintained by hand: `capture*`'s clearing step
disappears (it becomes a resolve-and-read function), `occupy*` keep their field-by-field
construction and now take the authored value whole, and `spawn*` keep their present
parameters and assemble that value inside.

Authored reads are spelled through `State` (`a.State.Base.Transform.Position`): 84 of them
— 47 in the app across 8 files, 37 across 7 test modules — qualified when the members were
deleted. **Writes are found by the compiler** for the same reason: `State` is an immutable
field, so an in-place write through it does not compile, which is how `Systems.syncActors`'
two writes and the two in tests were named.

`Waypoint` and `Entity` are unchanged: a waypoint's record is already pure authored
state. The environment's part teardown is untouched too, because a swap genuinely
unloads the environment — that clearing is a lifecycle behaviour, not a way to make a
record safe.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`)

**Primary Dependencies**: none new. `System.Numerics` for the shared base; the
existing per-kind storage in `world/Zone.fs`; xUnit for the tests.

**Storage**: N/A — in-memory zone data; the entity records stay inline in their slot
maps (a nested struct adds no indirection and no allocation).

**Testing**: xUnit via `dotnet test test/fsharp-game.Tests.fsproj`. The whole change is
headless: the six entity-shape constructions, the per-kind round-trips 030 added, and
the classification experiments below.

**Target Platform**: Linux desktop, SDL3 + SDL_gpu, software 60 FPS cap

**Project Type**: single-project desktop game with a docked editor

**Performance Goals**: hold 60 FPS. No per-frame path gains work: the entity records
stay inline and an authored read is a field access rather than a member call, which is
why the read-through accessors were dropped instead of kept (the hot loops —
`Viewport.buildLines`, `GameplayScreen.renderScene`, `Systems` — are the regression
check).

**Constraints**: no new module or payload type beyond the three authored records; every
existing behaviour must be unchanged, so the 030 behaviour tests pass unmodified; no
per-frame kind check (030's FR-010); a derived field added to a kind must still fail the
build until it is produced.

**Scale/Scope**: three records split (`Actor`, `Prop` — and therefore parts — `Trigger`);
every authored read qualified with `State` (47 across eight source modules, 37 across
seven test modules); about 25 construction sites and a handful of state-shaped writes
updated.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — the change **deletes** a mechanism (a clear step and the restore that mirrored it) and replaces it with three small records. It adds no module, no dependency, and no payload type beyond the authored records themselves. An authored read is a field access through `State`, not a member call. |
| II. Fail Loudly | Pass — this is the principle the feature serves. An unsafe payload becomes unrepresentable rather than merely avoided, a derived field still fails the build until something produces it, and a stale or wrong-kind ref still raises at the function that reads the authored value. |
| III. Spec-First | Pass — `specs/031-entity-authored-state/spec.md` is approved and drives this plan. |
| IV. Data-Oriented Architecture | Pass — the authored records are structs nested inline in their entities, so the slot maps keep one contiguous payload per kind and nothing is boxed or allocated. |
| V. Minimise GC Pressure | Pass — no allocation is added; an authored read is a field access, and the derived resources are mutated in place exactly as they are today. |

## Complexity Tracking

None. An earlier revision of this plan recorded read-through accessors on each entity as
a deliberate cost — a repeated member, bought to keep the authored reads compiling
untouched. That cost was estimated at ~194 sites; the count this design actually qualifies
is the 84 in the Summary, and that is the figure the accessors were weighed against. They
were removed before the feature landed: they were not necessary, and they put a
struct-returning member call on every authored read, about half of them in per-frame code.
Authored reads are now spelled through `State`.

## Project Structure

### Documentation (this feature)

```text
specs/031-entity-authored-state/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── authored-state.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
world/
├── Entity.fs            # + `ActorState`, `PropState`, `TriggerState`; `Actor`,
│                        # `Prop`, and `Trigger` nest them as `State`, which is what
│                        # authored reads and writes go through. `Entity` and `Waypoint`
│                        # unchanged.
├── Zone.fs              # `capture*` become resolve-and-read (no clearing); `occupy*`
│                        # build from the authored value whole; `spawn*` keep their
│                        # present parameters and assemble the value inside; the `set*`
│                        # writers write `State`; `deactivateEnvironment` and `attachPart`
│                        # unchanged.
├── Systems.fs           # `syncActors`'s two in-place writes go through `State`
│                        # (`.State.Base.Transform.…`) and its authored reads are
│                        # qualified.
└── EnvironmentBatch.fs  # its two authored reads, qualified.

state/
├── Command.fs           # payloads become `ActorState` / `PropState` / `TriggerState`;
│                        # `Waypoint`/`Link`/`LinkSpawn` unchanged.
└── GameplayState.fs     # its one authored read, qualified.

editor/
├── Inspector.fs         # the overlay and trigger edits work on the authored value.
├── Selection.fs         # reads the authored value; no other change.
├── Spawning.fs          # builds creates from the authored value.
├── Outliner.fs          # its four authored reads, qualified.
└── Viewport.fs          # its eight authored reads in `buildLines`, qualified.

screen/
└── GameplayScreen.fs    # its three authored reads, qualified; no behaviour change.

test/
├── EntityTests.fs       # the six entity-shape constructions
├── ZoneTests.fs         # the prop/part/trigger helpers and every construction
├── SpawningTests.fs     # trigger payloads
├── SelectionTests.fs    # the actor construction and trigger edits
└── {Systems,GameplayState,Player}Tests.fs   # construction helpers and the two writes
```

**Structure Decision**: single project, existing clusters, existing modules only — no
new source or test file, so neither `.fsproj` changes. The authored value lives in
`world/Entity.fs` beside the entity it belongs to, because it *is* that entity's state;
`Zone` continues to own the operations that produce a live entity from it, and `Command`
continues to carry it. `render/`, `physics/`, and `core/` are outside this feature — none
of them reads an entity's authored state; the change reaches `screen/GameplayScreen.fs`
only in that its three authored reads are spelled through `State`.

## Phase 0: Research

See [research.md](./research.md). It settles the nesting, how reads and writes are
spelled, which kinds are split and which are left alone, what happens to `capture*`,
how the derived fields are still forced, and why the environment lifecycle keeps its
teardown.

## Phase 1: Design

- [data-model.md](./data-model.md) — the three authored records, the reshaped entities,
  the read and write rules, the payload mapping, and the validation rules.
- [contracts/authored-state.md](./contracts/authored-state.md) — the surfaces that
  change (`Entity`, `Zone`, `Command`, `Inspector`).
- [quickstart.md](./quickstart.md) — runnable validation scenarios.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — as checked: a mechanism deleted, three records added, and no new module. |
| II. Fail Loudly | Pass — an unsafe payload is unrepresentable, and a derived field still fails the build until produced. |
| III. Spec-First | Pass. |
| IV. Data-Oriented | Pass — structs nested inline; the slot-map payloads stay contiguous. |
| V. Minimise GC | Pass — no allocation added; derived resources mutate in place as before. |
