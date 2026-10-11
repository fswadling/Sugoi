# Research: Fork a Scenario into the Scene

**Feature**: `specs/078-fork-scenario` | **Date**: 2026-10-11

No open `NEEDS CLARIFICATION` items — the spec's Clarifications session settled the one decision (capacity is
all-or-nothing, checked up front). The notes below record the design decisions this plan rests on so `/speckit-tasks`
and implementation do not re-derive them.

## Decision: a new presence step, `ZonePresent of Name : Place`

**Decision.** Add one state-level command that makes a place present **without** entering it: `apply` is
`GameplayState.presentZone` (streaming in the registered template when there is one), and `undo` is
`GameplayState.deleteZone`. It carries no `Previous` because it never changes the active place.

**Rationale.** The editor fork must bring a scenario's absent places into existence as **one undoable step**, and the
existing `ZoneAdd` does two things — it makes a place present *and* makes it active (`GameplayState.addZone` swaps to
it), which would switch the layout mid-fork. `presentZone` already makes a place present without entering it (the cue's
`AddZone` uses it); this gives that act a reversing command. Placing it in `Command.fs`'s state-level group keeps the
`Command` type above the state and is symmetric with `ZoneAdd`/`ZoneDelete`.

**Alternatives considered.**

- *Reuse `ZoneAdd` and suppress the activation.* Rejected: it changes the Outliner's "add a place" behaviour, which
  legitimately wants to enter the new place.
- *Make places present by direct mutation with no recorded step.* Rejected: the fork must be one undoable step, so
  creating a place has to be reversible.
- *A `ZonePresent` that carries the template.* Rejected: the runner can resolve it from `state.ZoneRegistry`, exactly
  as the cue's `AddZone` does, so the command carries only the place.

## Decision: one materialiser module, shared by both entry points

**Decision.** A new `world/ScenarioFork.fs` exposes `materialise` (working name) that takes the gameplay state, the
shared state and a `ScenarioContents`, mutates the scene to add the scenario (creating absent places, appending every
zone's entities, detaching bodies built into inactive places), and **returns `Command list option`**: `Some` the
reversal (which the editor pushes as one `Batch`, without re-applying, and the cue op discards), or `None` when the
capacity pre-check refuses — the one signal that separates "applied, nothing to add" (a legitimate empty list, cue
still forks) from "refused" (cue not forked).

**Rationale.** Both entry points do exactly the same scene work and differ only in what they do with the reversal.
One module keeps them from drifting, mirrors `Spawning.instantiateZone`'s mutate-and-record shape, and is the seam the
capacity pre-check and the detach live in. It needs `GameplayState`, so it is compiled at the deliberate inversion
point (after `GameplayState`, before `CueEngine`).

**Alternatives considered.**

- *Two implementations (one editor, one engine).* Rejected: duplicated scene logic would diverge.
- *Put the reversal in `Command` as a special `Fork` case.* Rejected: `specs/077` already makes a fork an ordinary
  `Batch`, so a bespoke command is exactly the special case 077 removed.

## Decision: waypoint chains are reproduced in the create payloads (two passes)

**Decision.** For a scenario zone's waypoints: spawn them all first, collecting their new refs; then, for each, write
its chain link directly (`Zone.setWaypointNext`) and record a single `WaypointCreate` whose payload carries the
**resolved** `Next : EntityRef option` (the scenario's `Next : int option` mapped through the ref array). No separate
`Link` commands are recorded.

**Rationale.** The scenario records chains as indices into its own array (`specs/070`); a fork must translate an index
to the newly added waypoint. Recording the resolved link in the create payload makes undo (remove) and redo (occupy
the recorded value) both faithful, and keeps the step to one command per waypoint. A `WaypointCreate`'s payload is
authored state and may hold the link — `Zone.occupyWaypoint` writes it.

**Alternatives considered.**

- *Record a `Link` command per chained waypoint.* Rejected: it doubles the command count for no gain, and a create's
  payload already carries the link.
- *Leave each appended waypoint unchained.* Rejected: the scenario's chains are authored content and must reproduce.

## Decision: capacity is checked up front, all-or-nothing

**Decision.** Before mutating anything, the materialiser computes, per target place, the number of actors, waypoints
and triggers to add, and checks each against the zone's free room (`SlotMap.Capacity - Count`). If **any** target
lacks room, it logs the reason and returns no commands, having changed nothing; no place is created, no entity added,
and the cue is not forked. This needs one small addition: `SlotMap.Capacity`.

**Rationale.** The spec's Clarifications chose all-or-nothing so a fork never lands half-applied. Checking first means
nothing is added transiently either. `SlotMap` exposes `Count` but not capacity; a `Capacity` member is a one-line
addition to the core type and is generally useful. Free slots are exactly `Capacity - Count` (every non-alive slot is
on the free list).

**Alternatives considered.**

- *Attempt the adds and catch `invalidOp "slot map is full"`.* Rejected: using an exception for expected control flow
  contradicts Fail Loudly's intent and is fragile.
- *Skip the affected zone and fork the rest.* Rejected by the spec's Clarifications (partial forks are surprising).
- *Hard-code capacities at the fork.* Rejected: they live in `Zone.create`; a `Capacity` member keeps one source.

## Decision: the editor fork is one `Batch`, and pruning reaches into it

**Decision.** The editor's Fork control builds the reversal of `materialise` plus a `SetCue (before, forkInto
scenarioCue before)` into one `Batch`, applies the cue change directly (the materialiser already did the scene work),
and pushes it with `UndoStack.push` (no re-apply). `History.pruneCueSteps` is taught to **recurse into a `Batch`**:
it drops a nested `SetCue` and keeps the rest (collapsing a batch emptied by it).

**Rationale.** One gesture is one step (spec FR-007). The cue part must be pruned on the way into play like any
`SetCue` (FR-012), because the cue advances while playing; the entity part must stay undoable. Recursing is the
smallest change that keeps both.

**Alternatives considered.**

- *Record the cue fork as a separate `SetCue` step.* Rejected: it would make a fork two undos, against the spec.
- *A bespoke command carrying both.* Rejected: 077 makes the `Batch` the right shape.

## Decision: the cue op materialises as a runtime effect and desugars to `Fork`

**Decision.** `ForkScenario of scenario: Scenario` is added to `world/Cue.fs`. In `CueEngine.advance`, reaching it
looks the scenario up (`ScenarioRegistry.tryContents`): if registered, it `materialise`s the scene (discarding the
command list) and answers `ValueSome (Fork contents.Cue)`; if not, it reports a diagnostic (`noOp` convention) and
answers `ValueSome Fin`.

**Rationale.** Cue ops are runtime effects, not undo steps (spec FR-006). Returning `Fork cue` reuses the existing
"run beside the rest" construct (`stepSequenceNext` handles `Fork`), so no new control flow is needed; it inherits
`Fork`'s constraint (only meaningful at a sequence's current op), which the spec's Assumptions note.

**Alternatives considered.**

- *Write `state.Cue` from inside `advance`.* Rejected: `advance` sees only its subtree, and `tick` writes the cue
  afterwards, so the fork would be lost.
- *Return the scenario cue inline (a `Sequence`).* Rejected: the spec wants it to run **beside** the rest, which is
  `Fork`, not inline.

## Decision: actor bodies built into an inactive place are detached

**Decision.** When the materialiser appends an actor into a place that is not active, it detaches the body it built
(`Physics.removeBody`, guarded by `isAdded`), mirroring `CueEngine.detachIfInactive`. Waypoints and triggers carry no
bodies, so nothing else needs it.

**Rationale.** `Zone.spawnActor` builds a Jolt body that joins the simulation when created; a non-active zone's bodies
must not simulate (the codebase detaches them everywhere else — `populateZone`, `detachIfInactive`). Appending into the
active place needs no detach; appending into another does.

**Alternatives considered.**

- *Switch to the place, add, switch back.* Rejected: the spec forbids changing the active place, and `specs/077`
  makes the switch unnecessary.

## Decision: the cue op's controls live in the shared tree control

**Decision.** `ForkScenario` joins `CueTree`'s op vocabulary: a `"Scenarios"` group in `kinds`, `kindName` and
`defaultOfKind` (defaulting to the first declared scenario), `label`, `children` (`[]`), and `placeOf` (`ValueNone`).
A `withScenario` writer is added, and `CueEditor` draws a scenario chooser reading the closed `Scenario.all` (filtered,
like every other combo, per `specs/068`).

**Rationale.** Every cue op is authored through the one shared control (`specs/047`); a scenario is a closed value, so
it is chosen, not typed. The compiler reports each match that must account for the new case, which is the project's
intended way to add vocabulary.

## Decision: docs, and the historical spec

**Decision.** Update `docs/ARCHITECTURE.md` ("Scenario & Cue": running a scenario is forking it into the scene),
`docs/EDITOR_DESIGN.md` (the Workspace's Scenario row gains Fork; the cue op is listed) and `docs/ROADMAP.md`
(phase 11's remainder lands). Leave `specs/070-scenario-library` as the historical record (superseded).

## Note: out of scope

`InlineScenario`/`ClearScenario` and the camera-follow/interaction toggles are named by `docs/ROADMAP.md` phase 11 but
are separate increments (spec FR-013). No persistence (the library is not persisted). No change to what a scenario
captures (`specs/076`) or to the command layer (`specs/077`) beyond the one presence step.
