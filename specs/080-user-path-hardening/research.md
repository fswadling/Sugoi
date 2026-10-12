# Research: Graceful Failure on User-Facing Paths

**Feature**: `specs/080-user-path-hardening` | **Date**: 2026-10-12

No open `NEEDS CLARIFICATION` items — the spec settled all four decisions (minimum scope, report-and-skip
for bad assets, skip-and-report at the history runner, and a top-level boundary in scope). The notes below
record the design decisions this plan rests on so `/speckit-tasks` and implementation do not re-derive
them. Each cites the site it changes.

## Decision: the diagnostic surface is the existing one-line convention

**Decision.** Every no-op, refusal and skip reports through the existing console diagnostic convention —
the `CueEngine.noOp`/`noOpTrigger` `eprintfn` pattern — extended with one variant for place-targeted ops.
No new UI surface, no overlay.

**Rationale.** Consistency (the cue engine already reports this way, and the spec's Assumptions fix on it),
and it is allocation-free on the happy path. A message is written only when something is skipped.

**Alternatives considered.** An ImGui notification/toast — rejected: a new surface, new lifetime, and the
constitution's Minimalism. A structured log file — rejected: no consumer.

## Decision: `playClip` falls back instead of raising (US1)

**Decision.** `world/ActorRender.fs`'s `playClip` no longer raises when the asset lacks the requested clip.
It falls back: use the other locomotion clip if the asset defines it, else keep a clip the actor already
holds, else clip 0, else leave the pose. A single reason is reported. `create` already treats a missing
`"Idle"` as a supported configuration (`ActorRender.fs:69-76`), so this makes the advance path agree with
the build path.

**Rationale.** The player is driven through `playClip` every play frame with `"Idle"`/`"Jog"`
(`Systems.locomotionClip` → `Systems.animate` → `playClip`), so any player actor whose asset lacks either
clip is a guaranteed crash. Falling back is the defined pose the spec's US1 asks for.

**Alternatives considered.** Requiring the clips at asset scan — rejected: an asset without them is legal
today, and rejecting it moves the crash earlier rather than removing it. Silently ignoring (no diagnostic)
— rejected: the author can no longer tell a fallback from a working clip.

## Decision: camera cue ops perform no effect and report (US2)

**Decision.** In `world/CueEngine.fs`, `SetCamera` and `FlyCamera` stop calling `invalidOp` and instead
take the same no-op-and-report path the other ops take (currently `noOp`/`noOpTrigger`). The conditions:
absent place; waypoint that does not resolve or is not alive; a flight path of fewer than two live
waypoints; a speed of zero or below. `namedWaypoint`/`poseAt`/`resolvedPath` become option-returning
helpers rather than raising.

**Rationale.** The spec's FR-003 requires *every* op to be consistent, and these are the only ones that
break the contract. They are reachable by ordinary authoring (rename/delete a waypoint after wiring a
camera op; drag the speed to 0, which the unclamped `dragNumber` permits — `editor/CueTree.fs:521-524`).

**Alternatives considered.** Clamp a non-positive speed to a minimum — rejected as a silent behaviour
change; the op performing nothing is the same rule the rest of the cue follows. Gating authoring in the
editor — rejected: the run path must be robust regardless (a cue can be reused in a scene that lacks the
waypoint).

## Decision: a bare `Fork` runs its inner cue (US2)

**Decision.** In `CueEngine.advance`, replace `| Fork _ -> invalidOp "a Fork is only meaningful at a
Sequence's current op"` with running the inner cue: `| Fork inner -> advance … inner`. This mirrors the
existing empty-rest branch of `stepSequenceNext` (`CueEngine.fs:667-671`, which already does exactly this
when a sequence has nothing beside the fork).

**Rationale.** The only producer of a bare `Fork` is the `ForkScenario` op, which returns
`ValueSome (Fork contents.Cue)` (`CueEngine.fs:462`). When the op is not at a sequence front — a lone op,
or inside a `Parallel` — the returned `Fork` becomes the current node and the next tick hits the raise. A
`Fork` with nothing beside it is, by the engine's own rule, just its inner cue.

**Alternatives considered.** Wrap the op's result as `Sequence(Fork cue, empty)` so it is always a
sequence front — equivalent for the empty-rest case but narrower; handling `Fork` in `advance` covers
every position. The existing test asserting the raise (`test/CueTests.fs` "a Fork outside a sequence
raises") is updated to assert the new defined behaviour.

## Decision: `withChildren` handles the conditional shape (US3)

**Decision.** In `editor/CueTree.fs`, `withChildren` gains the `If` case, rebuilding an `If` from its two
children (`If (advent, thenCue, elseCue)`, with the engine's collapse rules for a single remaining branch
matching `Parallel`/`Fork`). `children` already returns two children for `If` (`CueTree.fs:122`), so every
`withAt`/`withoutAt` path on a branch becomes total.

**Rationale.** It is a straight omission: the editor presents both branches for editing, but the one
function that turns an edited child list back into a node has no `If` arm, so any branch edit falls to
the `_ -> invalidOp` at `CueTree.fs:194`.

**Alternatives considered.** Forbid editing a branch in the editor — rejected: the branches are a core
part of the conditional op and are already drawn as editable rows.

## Decision: the history runner skips and reports a step it cannot carry out (US4)

**Decision.** In `state/History.fs`, both `apply` and `undo` gain a precondition check per step: before
routing a step it resolves the place (for content commands) or the target state (for the state-level
commands) and, when the step cannot be carried out, reports it and moves on rather than calling the
mutator. The mutators in `state/GameplayState.fs` (`switchZone`, `deleteZone`, `presentZone`, `stageZone`,
`addZone`) keep their `invalidOp` for direct misuse (FR-007: "the check is made by the history runner").

**Rationale.** A play session's cue mutates the scene as a runtime effect with no history step
(`SetZone`/`RemoveZone`/`ForkScenario`), while the recorded edits still name their place; the runner's
invariant ("LIFO reaches a step while its place is still present", `History.fs:52-54`) does not hold
across a Play session. Skipping at the runner is the smallest change that stops the whole family of
crashes without weakening the mutators.

**Alternatives considered.** Guard every state mutator with a no-op (the option the spec declined) —
rejected in favour of keeping mutators fail-fast. Make cues undoable (record runtime effects as steps) —
deferred to a separate spec; it is a semantic redesign, not a hardening fix.

## Decision: capacity is pre-checked at every creation surface (US5)

**Decision.** `editor/Spawning.fs`'s `spawn*` (and the `editor/Outliner.fs` / `editor/AssetBrowser.fs`
controls that call them) refuse a create when the target map has no room, reporting a reason, mirroring
the existing `ScenarioFork.materialise` pre-check. Room is `map.Count < map.Capacity`
(`core/SlotMap.fs:48-52`).

**Rationale.** `SlotMap.Add` raises when full (`SlotMap.fs:54-56`) and the caps are small and fixed
(`world/Zone.fs:53-58`: actors 64, props 32, waypoints 128, triggers 32). The Outliner's zone-name Add
already gates on `canAdd` (`Outliner.fs:75-79`), so the pattern exists to reuse — the entity create
buttons simply lack it.

**Alternatives considered.** Raise the capacities — rejected: the bound is real (fixed-size contiguous
storage is the design) and hiding it moves the failure later. A modal dialog — rejected: no new UI.

## Decision: scenario capture uses the tolerant link accessor (US6)

**Decision.** `world/ScenarioZone.fs`'s `captureWaypoints` resolves a waypoint's successor through
`Zone.waypointNext` (which returns `None` for a link whose target is gone) instead of reading the raw
`waypoint.Next` and raising.

**Rationale.** The runtime deliberately treats a dangling link as a chain end (`Zone.waypointNext`,
`Zone.fs:612-618`); capture reading the raw field makes the same state fatal, so an edit the runtime
accepts cannot be registered.

**Alternatives considered.** Prune dangling links at capture — rejected: they are reversible by undo, so
capturing them as absent (the value the accessor already gives) preserves the author's data.

## Decision: bad assets are reported and skipped; an undegradable resource is reported and the run ends cleanly (US7)

**Decision.** `world/Assets.fs`'s eager paths (`scan`'s prop loop, `loadActors`) wrap each asset in a
`try/with`; a failure is reported with the asset id and the asset is omitted (its dictionary entry is not
added), and the scan continues. The asynchronous environment path already does this
(`Assets.pump` → `failAsset`). The font is handled by degrading the UI to text-disabled when it cannot be
opened. A resource with no sensible degradation (a shader, without which nothing renders) is reported and
the run ends cleanly via the boundary rather than aborting.

**Rationale.** The spec's C2 is "report and skip"; the model paths degrade cleanly, and the font can
degrade to no text. A missing shader has nothing to degrade to, so the honest outcome is a clear message
and a clean exit — still "never an unhandled exception".

**Alternatives considered.** Fail startup on any bad asset (the declined option) — rejected by C2.
Silently drop the asset — rejected: the author must be able to tell.

## Decision: the top-level boundary wraps the frame loop; startup is wrapped at the entry point (US8)

**Decision.** In `Engine.fs`, the main `while` loop is wrapped in `try/with`; the handler reports the
current phase (the loop already tracks phase names for timing) plus the exception, then falls through to
the existing teardown, so the run ends with a controlled shutdown. `Program.fs` wraps `Engine.run ()` so a
failure during startup (before the loop) is reported and returns exit code 1 rather than escaping with a
stack trace.

**Rationale.** The spec's US8 asks for a report naming the failing operation and a controlled teardown.
The loop already computes a per-phase label, so naming the failing phase is nearly free; the teardown
after the loop frees the device/window/assets as it does on a normal quit. Startup has nothing built yet,
so reporting and returning is the clean analogue.

**Alternatives considered.** A handler that swallows-and-continues — rejected: contradicts *Fail Loudly*;
the boundary reports and ends the run, it does not hide the fault. Wrapping only `Program.fs` — rejected:
a mid-loop throw would skip the teardown.
