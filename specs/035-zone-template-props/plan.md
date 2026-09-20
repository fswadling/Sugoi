# Implementation Plan: A Registered Zone's Placed Props

**Branch**: `035-zone-template-props` | **Date**: 2026-09-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/035-zone-template-props/spec.md`

## Summary

An entry gains one field — `Props : PropState array`, beside the parts — and instantiating one replaces
the zone's props as well as its environment, in one undo step.

The field itself is the easy half, and it is the half `specs/034-zone-registry` planned: a prop is a
`PropState`, the value `Zone.captureProp` already answers and `PropCreate`/`PropDelete` already carry,
so deletions and duplicates fall out of reading the zone's live props. The hard half is FR-005, and it
is the reason this is a feature rather than a patch: instantiating with props is **one gesture with
`1 + N + M` mutations** — remove the *N* props the zone held, swap the environment, place the *M* the
entry records — and nothing in the command model expresses that today.

So the plan adds the smallest thing that does:

- **`Batch of Commands : Command list`**, the compound command. Its `apply` walks the list forwards,
  its `undo` walks it backwards, and it carries only commands that already exist. `LinkSpawn` is the
  precedent that a command may be several mutations, and this generalises it by exactly one case.
- **`Spawning.instantiateZone`**, because instantiating creates entities and `editor/Spawning.fs` is
  the single place the editor does that. It replaces the props through the *same* `PropDelete`
  commands the undo will replay, loads the environment from the entry's environment half, places the
  entry's props, and pushes one `Batch` carrying all three groups in that order — so one Ctrl+Z runs
  the three groups backwards and puts the zone back, props at their old identities included.
- **The environment swap moves out of the panel and into `Spawning`**, which is where 034's own
  research said it belonged and could not go: the reason it was left in the panel was that the swap
  had to reconcile a stale part selection, and `Spawning` is compiled above `Selection`. With the
  reconcile now the caller's step — the idiom `Editor.undoStep` and `redoStep` already use — the
  swap can live with the rest of entity creation, and the panel ends up holding two lines instead of
  five.

No new module, no new file, and no `.fsproj` change: one field, one command case, two functions in
`Zone`, one in `Spawning`, and the panel's instantiate branch.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`)

**Primary Dependencies**: none new, and **no new ImGui binding**: the panel's two controls are already
drawn, and this feature changes only what the Instantiate control does — it adds no widget. The native
dock line and `libcimgui.so` are untouched.

**Storage**: N/A. The registry is still session state; this feature adds a field to what an entry holds
and decides no file format.

**Testing**: two existing test files extended, and no new one, because the fixtures that drive these
paths headlessly already exist:

- `test/ZoneTests.fs` — **capture**: the record carries the zone's props as they stand (moved, renamed,
  deleted, duplicated), is a copy rather than a view, and keeps props and parts distinguishable.
- `test/SpawningTests.fs` — **the replacement**: `Spawning.instantiateZone` leaves the zone holding
  exactly the entry's props, in one undo step, and undoing that step restores the previous environment
  *and* the previous props at their old identities. This is where it goes because it is the only file
  with a `SharedState`/`GameplayState` fixture, which the editor path needs.
- `test/CommandTests.fs` does not exist, and this feature does not create one: `Batch` is exercised
  through the instantiation that needs it rather than in isolation, which is how `LinkSpawn` is
  covered too.

What no test reaches is the panel's two lines and the button's enabled state; those are `quickstart.md`.

**Target Platform**: Linux desktop, SDL3 + SDL_gpu, software 60 FPS cap

**Project Type**: single-project desktop game with a docked editor

**Performance Goals**: hold 60 FPS. Nothing here is on a play-mode path, and the one thing worth being
explicit about is that **the panel must not capture per frame**: `Zone.captureTemplate` allocates
arrays by walking the zone's props, so the Register control reads the cheap question (is an environment
loaded) to decide whether it is enabled, and captures only when it is activated.

**Constraints**: one instantiation is one edit (FR-005); registering stays a direct write with no
command and no scene change (FR-008); the zone's actors, waypoints and triggers are untouched by an
instantiation (FR-006); a place still cannot be registered without an environment (FR-007); every
existing behaviour is unchanged, the Asset Browser's environment load included (FR-009).

**Scale/Scope**: one field on `ZoneTemplate`; `ZoneTemplate.fs` keeps its pure constructor and its
place in the compile order; `Zone.fs` gains `captureProps` and `captureTemplate`; `state/Command.fs`
gains one case and two lines; `editor/Spawning.fs` gains `instantiateZone`; the panel's instantiate
branch shrinks; two test files extend; four docs are corrected. No new file, no `.fsproj` change, no
new command payload type.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one field, one command case, three functions, and a panel branch that gets *shorter*. The compound command is general rather than shaped to one call site, which is what keeps it from being a special case: `apply` forwards and `undo` backwards is the whole of it. |
| II. Fail Loudly | Pass — an entry whose prop names an asset the game does not have raises through the existing prop-render path; a props array holding an entry shaped like a part (an absent asset id) is a broken registration and raises at the boundary rather than being drawn as something it is not. |
| III. Spec-First | Pass — `specs/035-zone-template-props/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — `PropState array`, contiguous and struct-valued, as the parts already are; no per-prop object, and no change to `Zone`, `Prop` or the slot maps. |
| V. Minimise GC Pressure | Pass — nothing on a per-frame path. The one allocation this feature could add per frame is captured only when the author activates a control, and the plan says so where an implementer would otherwise be tempted. |
| N-V. Testing is the Authoring Tool | Not engaged — it governs the narrative subsystem, which is untouched. Its spirit is served below: both halves of the feature are asserted headlessly, through the real paths. |

## Complexity Tracking

None. The compound command is the only structural addition, and it is four lines of match arms over a
list of commands that already exist; the alternative — a payload type describing a zone's contents
before and after — is a larger invention for the same behaviour.

## Project Structure

### Documentation (this feature)

```text
specs/035-zone-template-props/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── zone-props.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
world/
├── ZoneTemplate.fs      # CHANGED: `Props : PropState array`, beside `Parts`. Its constructor stays
│                        # pure and zone-free, taking the props as an argument, so a record can be
│                        # built in a test without a physics world — which is what the map's tests do.
└── Zone.fs              # CHANGED: `+ captureProps : Zone -> (EntityRef * PropState) array` (the
                         # zone's placed props, each with the ref a replacement has to record) and
                         # `+ captureTemplate : Zone -> ZoneTemplate option`, which pairs the loaded
                         # environment with them and answers `None` when there is no environment.
                         # `loadEnvironmentFrom` is unchanged and still loads the environment half only.
state/
└── Command.fs           # CHANGED: `+ Batch of Commands : Command list`. `apply` folds forwards,
                         # `undo` folds backwards, and nothing else about the command model moves.
editor/
├── Spawning.fs          # CHANGED: `+ instantiateZone`, which captures the props being replaced,
│                        # deletes them through the commands it will record, loads the environment,
│                        # places the entry's props, and pushes one `Batch` — the editor's single
│                        # place for entity creation keeping its promise for a gesture that creates
│                        # several.
└── ZoneRegistryPanel.fs # CHANGED: Register captures the whole entry; Instantiate calls
                         # `Spawning.instantiateZone` and then reconciles the stale part selection,
                         # which is now the caller's step rather than the swap's.
test/
├── ZoneTests.fs         # EXTENDED: what a record captures of a zone's props.
└── SpawningTests.fs     # EXTENDED: the replacement, its one-step undo, and what that restores.
docs/
├── EDITOR_DESIGN.md     # D-023 says an entry does not yet hold props; it does now. D-011's command
│                        # model gains the compound command, since "one gesture, several mutations" is
│                        # a shape the model did not previously have a name for.
├── ARCHITECTURE.md      # the `Zone_Registry` entry: `prop_template` is no longer deferred.
└── ROADMAP.md           # phase 12's note on what is deferred, which lists props among them.
specs/034-zone-registry/
├── contracts/zone-registry.md  # the panel's instantiate block, superseded: the swap moved to
│                               # `Spawning` and the panel reconciles after it.
└── research.md                 # Decision 7 recorded a trigger for extracting that swap at the third
                                # call site; it records what actually happened instead.
```

**Structure Decision**: no new module and no new file, which is the point. The field belongs on the
record the registry already has; the capture belongs in `Zone.fs` beside the `capture*` family it joins
(it reads a live zone, so it cannot be in `ZoneTemplate.fs`, which is compiled above `Zone.fs` because
the loader takes one); the compound command belongs in the command module; and the replacement belongs
in `Spawning` because it creates entities. The one file whose role changes is `ZoneRegistryPanel.fs`,
and it gets simpler: the swap it had to keep grew into something that creates entities, and entity
creation is not a panel's job.

## Phase 0: Research

See [research.md](./research.md). It settles how one gesture becomes one step, why the props are
replaced rather than added to, why the capture splits across two modules, why the compound command is
general rather than purpose-built, why instantiating selects nothing when every other creation in
`Spawning` selects, where the panel's five-line swap went and what that does to 034's recorded
trigger, and what a props array that holds a part-shaped entry means.

## Phase 1: Design

- [data-model.md](./data-model.md) — the entry, the prop-versus-part distinction, and what a
  replacement has to hold to be reversible.
- [contracts/zone-props.md](./contracts/zone-props.md) — the surfaces that change: `ZoneTemplate`,
  `Zone`, `Command`, `Spawning`, `ZoneRegistryPanel`, and the two `specs/034-zone-registry` lines this
  supersedes.
- [quickstart.md](./quickstart.md) — runnable validation scenarios, headless and interactive.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — as checked: no new file, no new payload type, no new widget, and the panel's instantiate branch is shorter than it was. |
| II. Fail Loudly | Pass — a named-but-missing prop asset raises through the existing path, and the one shape a registration cannot legitimately hold raises at the boundary instead of being drawn. |
| III. Spec-First | Pass. |
| IV. Data-Oriented | Pass — one contiguous `PropState array` beside the one that is already there; `Zone`, `Prop` and the slot maps are untouched. |
| V. Minimise GC | Pass — the capture is click-only, and the replacement walks the props it is replacing once. |
