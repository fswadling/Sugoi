# Implementation Plan: Fork a Scenario into the Scene

**Branch**: `078-fork-scenario` | **Date**: 2026-10-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/078-fork-scenario/spec.md`

## Summary

**Fork** a registered scenario into the live scene: append every recorded zone's entities into that place's zone —
creating absent places first, without switching the active place — and fork the scenario's cue beside the running one.
Two entry points: a **`ForkScenario` cue op** (a runtime effect, like every cue op) and an **editor Fork control**
(one undoable step).

Two recent increments make this small. `specs/076` means a scenario carries only narrative entities, so a created
place takes its environment and light from the Zone Registry's template and the active place and player are
untouched. `specs/077` means commands carry their place, so a fork spanning several places is an ordinary `Batch` —
the editor step needs no bespoke command, and the runtime fork is a direct effect with no history.

The one new reversible step the editor fork needs is a **presence step** that makes a place present **without**
entering it (the existing "add a place" step also activates it). The materialisation itself is a new `world/`
module the cue engine and the panel both call: it appends the entities, detaches actor bodies built into an inactive
place, and returns the commands that reverse it.

## Technical Context

**Language/Version**: F# / .NET 10

**Primary Dependencies**: `world/ScenarioRegistry.fs` (`ScenarioContents`, `tryContents`), `world/ScenarioZone.fs`
(the entities forked), `world/Cue.fs` + `world/CueEngine.fs` (the op and the walk), `state/Command.fs` /
`state/History.fs` (the one new step and the place-carrying batch), `state/GameplayState.fs`
(`presentZone`/`deleteZone`, the zones), `world/ZoneRegistry.fs` (`tryTemplate`), `world/Zone.fs`
(`spawn*`/`tryWaypointByName`/`deactivatePhysics`), `editor/CueTree.fs` + `editor/CueEditor.fs` (the cue op's
controls), `editor/ScenarioPanel.fs` (the Fork control), `core/SlotMap.fs` (capacity). All present; no new dependency.

**Storage**: N/A — the scenario library is not persisted (ROADMAP phase 15); a fork produces no stored value.

**Testing**: xUnit, headless. The materialisation and the cue op are pure over the gameplay state plus the asset
manager, so both are asserted with no device; the cue-op test drives `CueEngine.tick`, as `CueTests` already does.

**Target Platform**: Linux desktop

**Project Type**: Desktop game (data-oriented engine + editor)

**Performance Goals**: a fork runs on a click or when a cue reaches the op — never per frame; the play loop's idle
path is untouched. The materialisation is O(entities forked), with the capacity pre-check O(target zones).

**Constraints**: the `Command` type stays above `GameplayState`; the cue op is a runtime effect (not on the history);
a fork never switches the active place or moves the player; capacity is logged and no-ops (see Complexity Tracking);
build warning-free; suite green; no device opened.

**Scale/Scope**: 2 new source files (`world/ScenarioFork.fs`, `test/ScenarioForkTests.fs`); ~10 touched
(`core/SlotMap.fs`, `world/Cue.fs`, `world/CueEngine.fs`, `state/Command.fs`, `state/History.fs`,
`editor/CueTree.fs`, `editor/CueEditor.fs`, `editor/ScenarioPanel.fs`, plus existing tests and
`fsharp-game.fsproj` + `test/fsharp-game.Tests.fsproj`); 3 docs (`docs/ARCHITECTURE.md`, `docs/EDITOR_DESIGN.md`,
`docs/ROADMAP.md`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS. One new module (the materialiser, shared by both entry points), one new command case (the
  presence step), one `SlotMap.Capacity` member, and one cue op. Nothing speculative; the two entry points share one
  materialiser rather than duplicating it.
- **II. Fail Loudly** — **justified deviation** (see Complexity Tracking): a fork that lacks room **logs and no-ops**
  instead of raising, because a raising runtime cue op would crash play. Every other unexpected state still raises
  (an unregistered scenario is an expected `None` → a diagnostic no-op; a malformed scenario payload is a bug).
- **III. Spec-First** — PASS. This feature is `specs/078-fork-scenario`.
- **IV. Data-Oriented Architecture** — PASS. A scenario's entities are arrays of authored states; the fork walks
  them and adds to the fixed slot maps. No object graph.
- **V. Minimise GC Pressure** — PASS. Editor/op-time only; no per-frame allocation. The runtime fork's command list
  is discarded (or avoided) rather than built.

The **Narrative Subsystem** principles do not apply (`world/`/`state/`/`editor/`, not `narrative/`).

## Project Structure

### Documentation (this feature)

```text
specs/078-fork-scenario/
├── spec.md
├── plan.md                    # this file
├── research.md                # Phase 0 output
├── data-model.md              # Phase 1 output
├── quickstart.md              # Phase 1 output
├── contracts/
│   └── scenario-fork.md
├── checklists/
│   └── requirements.md
└── tasks.md                   # /speckit-tasks
```

### Source Code (repository root)

```text
core/SlotMap.fs             # CHANGED: + `member _.Capacity` (free slots = Capacity - Count), for the pre-check
world/ScenarioFork.fs       # NEW: the materialiser — append a scenario's zones into the scene, returning
                            #   `Command list option` (the reversal, or `None` when the capacity pre-check refuses);
                            #   the capacity pre-check; body detach for inactive places
world/Cue.fs                # CHANGED: + `ForkScenario of scenario: Scenario` (Scenario is compiled just before it)
world/CueEngine.fs          # CHANGED: the `ForkScenario` arm — materialise (runtime effect) then `Fork contents.Cue`
state/Command.fs            # CHANGED: + `ZonePresent of Name : Place` (a state-level step; joins the invalidOp
                            #   group; `placeOf` answers `None`); `pruneCueSteps` recursion is in History
state/History.fs            # CHANGED: `ZonePresent` apply/undo (`presentZone`/`deleteZone`); `pruneCueSteps`
                            #   recurses into a `Batch` to strip a nested `SetCue`
editor/CueTree.fs           # CHANGED: `ForkScenario` in kinds/kindName/defaultOfKind/label/children/placeOf; a
                            #   `withScenario` writer
editor/CueEditor.fs         # CHANGED: the scenario chooser for the op (reads `Scenario.all`)
editor/ScenarioPanel.fs     # CHANGED: a Fork control per row (disabled when unregistered), one `Batch` step

fsharp-game.fsproj          # + world/ScenarioFork.fs (after GameplayState, before CueEngine)
test/ScenarioForkTests.fs   # NEW: the materialiser, the op, the editor step, footprint, non-idempotence,
                            #   capacity no-op, unregistered no-op
test/*.fs                   # CHANGED: CueTree/CueEngine op coverage; the Scenario panel row
test/fsharp-game.Tests.fsproj  # + test/ScenarioForkTests.fs

docs/ARCHITECTURE.md        # "Scenario & Cue": running a scenario is forking it into the scene
docs/EDITOR_DESIGN.md       # Workspace's Scenario row gains Fork; the cue op listed
docs/ROADMAP.md             # phase 11: running a scenario is done
```

**Structure Decision**: `world/ScenarioFork.fs` is a new module compiled **after `state/GameplayState.fs` and before
`world/CueEngine.fs`** — the project's deliberate inversion point — because it mutates the gameplay state (making
places present, appending entities) and both `CueEngine` (the op) and `editor/ScenarioPanel.fs` (the control) call it.
It returns `Command list option` — `Some` the reversal (so the editor pushes one `Batch` and the cue op discards the list), or `None` when the capacity pre-check refuses — which is what tells the cue op to fork the cue or not. The
new `ZonePresent` case lives in `state/Command.fs`, carried by the state-level runner like `ZoneAdd`/`ZoneDelete`
(spec 077 keeps the `Command` type above the state). The cue op lives in `world/Cue.fs` because `world/Scenario.fs`
is compiled just before it (`specs/070`'s plan anticipated exactly this).

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Fail Loudly (II): a fork that lacks capacity **logs and does nothing** instead of raising | The cue-op path runs mid-play; a raise there would crash the game on content the author authored and a fixed-capacity slot map. The author-facing rule ("no op, not a crash") was chosen in the spec's Clarifications. | With a pre-check, so nothing is applied partially. Rejected alternatives: raise everywhere (crashes play); silence the editor too (an author would get no signal). The editor and the cue op both log the reason. |
