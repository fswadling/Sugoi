# Research: Commands Carry the Zone They Act On

**Feature**: `specs/077-commands-carry-zone` | **Date**: 2026-10-11

No open `NEEDS CLARIFICATION` items — the spec's Clarifications session settled the two scope decisions (keep
`ZoneMove` undoable; remove the temporary switch in a zone delete's undo). The notes below record the design
decisions this plan rests on so `/speckit-tasks` and implementation do not re-derive them.

## Decision: the place is the first field of every zone-content command

**Decision.** Each command that changes a zone's contents carries `Place` as its first field: `Transform`, `Rename`,
`ActorCreate/Delete`, `PropCreate/Delete`, `PartCreate/Delete`, `WaypointCreate/Delete`, `TriggerCreate/Delete`,
`Link`, `TriggerSet`, `LinkSpawn`, `SetEnvironment`, `SetLight`, `SetPlayer`. State-level steps (`ZoneMove`,
`ZoneAdd`, `ZoneDelete`, `SetCue`, `Advent`, `SetScenario`) and `Batch` do **not** carry a place.

**Rationale.** The place is the command's "where"; the spec (FR-001/FR-002) requires a step to be self-contained.
Naming it first keeps the reading uniform, matching `ZoneDelete of Previous * Name * Contents`.

**Alternatives considered.**

- *A wrapper command `InPlace of Place * Command`.* Rejected: it nests the union and makes every consumer unpack.
- *Carry it only where needed (creates/deletes).* Rejected: property edits (`Transform`, `Rename`, `SetLight`) act on
  a zone too, and the delete-restore and coalescing rules need the place for them as much as for creates.

## Decision: the state-level runner resolves place → zone; `Command` stays above the state

**Decision.** `Command` exposes `placeOf : Command -> Place option` (`Some place` for a zone-content command, `None`
for a state-level step and for `Batch`). `History.apply`/`undo` — the state-level runner — look the place up in
`state.Zones` and pass the resolved zone to `Command.apply`/`undo`, whose signatures are unchanged. The editor sites
that apply a command directly (`Selection.*Ref`, `Spawning.instantiateZone`) pass the zone they built the command
against (the active one).

**Rationale.** `Command` is compiled **before** `GameplayState` and cannot name it (an existing hard constraint).
Resolving in the state-level runner keeps that boundary and satisfies FR-007. Passing the resolved zone to
`Command.apply` (rather than the zones map or a resolver) keeps the command-level runner's signature and its
`zone`/`physics`/`assets` values unchanged.

**Alternatives considered.**

- *`Command.apply` takes the zones map and resolves internally.* Rejected: it moves resolution into the command type's
  dependency set and contradicts FR-007 (resolution belongs in the state-level runner).
- *`Command.apply` takes a `Place -> Zone` resolver function.* Works, but a function argument is heavier than the
  existing `byref`-friendly value arguments and buys nothing over `placeOf` + a lookup.
- *Put the place on `Batch`.* Rejected: a batch's sub-commands each name their own place (that is the point).

## Decision: a content command whose named place is absent fails loudly

**Decision.** If `History` reaches a content command whose place is not present in `state.Zones`, it raises.

**Rationale.** LIFO guarantees the place is present when the step is reached (a place that was removed has its own
step, reached first on the way back), so an absent place is a broken invariant, not a state to tolerate (Fail Loudly;
the spec's edge case).

**Alternatives considered.**

- *Silently skip.* Rejected: it would hide a real bug and make the test suite lie.

## Decision: `ZoneMove` is unchanged (FR-008)

**Decision.** Switching the active place stays a `ZoneMove` step on the history, exactly as today.

**Rationale.** Settled in the spec's Clarifications: zone navigation stays an edit the author can undo. The refactor
removes `ZoneMove`'s *load-bearing* role (LIFO no longer needs it), but changing navigation's undoability is a
separate, later decision.

**Alternatives considered.**

- *Make zone switches a non-undoable view change and retire `ZoneMove`.* Rejected **for now** — a behaviour change
  beyond this refactor's remit, with its own tests to rewrite; recorded as a candidate follow-up.

## Decision: a deleted zone's restore rebuilds in place, with no switch (FR-014)

**Decision.** `History.undo` of `ZoneDelete` no longer switches the active place. It creates the zone, rebuilds its
contents with `Zone.occupyZone`, then calls `Zone.deactivatePhysics` to detach the bodies it just built (the zone is
not active, so its bodies must not simulate).

**Rationale.** The switch existed only because `occupy*` builds bodies that join the simulation at creation. Detaching
right after is the same build-then-detach `GameplayState.populateZone` already uses when it populates a place that is
not active — an existing, proven pattern. This honours FR-014 ("not change the active place, even temporarily") and
removes the last camera re-frame from a scene edit's undo.

**Alternatives considered.**

- *Leave the switch.* Rejected by the spec's Clarifications (the same "no view churn" goal this refactor is about).
- *Teach `occupy*` itself to build detached.* Rejected: it would push a place-relative concern into every caller;
  detaching once after a bulk rebuild is simpler and local.

## Decision: the transform-coalescing key becomes (place, ref)

**Decision.** `UndoStack.push`'s merge of consecutive `Transform` steps requires the same **place** as well as the
same ref.

**Rationale.** An `EntityRef` is a per-zone `(index, generation)`; the same index exists in every zone. Today a
`ZoneMove` step always separates transforms in different places, so a ref-only key never merges across places — but
that is incidental, and this refactor removes the reason the separation was guaranteed. Keying on (place, ref) makes
the merge correct by construction.

**Alternatives considered.**

- *Leave the ref-only key.* Rejected: correct only by accident of the interleaving `ZoneMove`; a latent bug.

## Decision: `Batch` dispatch is unchanged

**Decision.** `History.apply`/`undo` keep recursing into a `Batch` per sub-command; each sub-command resolves its own
place. `Command.apply`/`undo` keep applying a `Batch` forwards/backwards.

**Rationale.** A multi-place gesture is exactly a `Batch` of content commands with different places (plus any
state-level steps); the existing recursion already gives forward/reverse ordering, and this feature only removes the
need for `ZoneMove` bracketing in such a batch.

## Decision: the state-level steps are untouched

**Decision.** `ZoneMove`, `ZoneAdd`, `ZoneDelete` (its apply; its undo changes per FR-014), `SetCue`, `Advent` and
`SetScenario` keep their place in the state-level runner and their behaviour.

**Rationale.** They are not a zone's contents; the spec (FR-010) keeps them. `ZoneAdd` in particular keeps its "make
present **and** active" meaning — a "make present without entering" step is the fork's concern, not this feature's.

## Decision: docs and comments, and the historical spec

**Decision.** Update `docs/EDITOR_DESIGN.md` (D-011) and `docs/ARCHITECTURE.md`, and the `Command`/`History` module
headers. Leave `specs/048-unified-undo-stack` as the historical record (superseded, not rewritten — the convention
`specs/076` followed with `specs/070`).

**Rationale.** The guiding `docs/` and in-code comments are the source of truth (`AGENTS.md`) and must not state a
rule that no longer holds. Specs record what was built when.

## Note: the zone-level steppers in tests

`UndoStack.undo`/`redo` (in `state/Command.fs`) step a stack against **one** zone and are kept for the tests that need
no `GameplayState`. With place-carrying commands they remain valid for stacks whose commands all name the same zone;
a test that builds a **multi-place** batch must drive it through `History` instead. This is a test-authoring note, not
a code constraint.
