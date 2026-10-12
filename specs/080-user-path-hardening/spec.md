# Feature Specification: Graceful Failure on User-Facing Paths

**Feature Branch**: `080-user-path-hardening`

**Created**: 2026-10-12

**Status**: Draft

**Input**: User description: "Assess the application's error handling — the policy is failing fast to avoid unnecessary complexity, but the application must not crash unexpectedly during actions that are part of expected user stories. An audit found several fail-fast sites reachable from ordinary play and edit actions."

## Origin

The project's policy (constitution, *Fail Loudly*) is that **unexpected state is a bug**: it prefers to
fail loudly rather than silently return a default, reserving graceful handling for *expected* failure
paths. That policy is sound — but an audit of the codebase found places where a **normal user action**
reaches a fatal failure, so the whole process dies. These are not programming bugs; they are cases where
**authored content or runtime state** (a scene, a cue, an asset file, or the fact of having played for a
while) violates an assumption the code treats as an invariant. The application has no top-level boundary
— any failure ends the run — so each of them is a hard crash.

The codebase already contains the correct pattern for this kind of failure: most cue operations
**perform no effect and report a reason** when their target is missing, rather than aborting. This
feature applies that same principle consistently across the user-facing paths, and reserves fail-fast
for the states only a programming error can produce.

## Scope

This feature covers the crash classes an ordinary play or edit session can reach:

- an actor whose asset lacks a locomotion clip (Play);
- a cue operation whose target is missing or stale;
- editing a conditional branch in the cue editor;
- undo/redo after a play session changed the scene;
- creating an entity past a place's capacity;
- capturing a place into a scenario while a link is dangling;
- a bad or missing asset at load time;
- a top-level boundary so an unexpected failure is reported, not silent.

**Out of scope** (recorded here so the boundary is explicit):

- **Cue undoability** — whether a cue's runtime effects become steps on the history, or Play becomes a
  history boundary, is a separate design decision and its own spec.
- Narrower runtime failures that need a window or GPU state: window-resize font / render-target failures,
  long-text and UI batch-capacity limits, environment-residency races, stale physics handles, and native
  interop access violations. These remain as they are for now.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Playing with an actor whose asset lacks a locomotion clip (Priority: P1)

An author sets an actor as the player and enters Play. Even when that actor's asset does not define the
clips the player is switched between when standing and moving, the session continues: the actor is shown
in a defined pose (or a default clip) and a diagnostic explains the substitution.

**Why this priority**: it is the simplest and most certain crash. Walking is the core play loop, and the
code that *builds* a player actor already deliberately tolerates a missing idle clip — the code that
*advances* it then makes the same configuration fatal. This is the shortest path from a normal action to
a dead process.

**Independent Test**: set as player an actor whose asset defines neither the standing nor the moving
clip; enter Play; stand still and then move. Assert the application keeps running and the actor is
rendered in a defined pose, with a reported reason.

**Acceptance Scenarios**:

1. **Given** a player actor whose asset defines neither the standing nor the moving clip, **When** Play
   begins and the player stands or moves, **Then** the application does not terminate and the actor is
   displayed in a defined pose.
2. **Given** a player actor whose asset defines the standing clip but not the moving clip, **When** the
   player presses a movement key, **Then** the session continues and the reason is reported once.
3. **Given** a player actor whose asset defines both clips, **When** the player moves, **Then** behaviour
   is unchanged from today.

---

### User Story 2 - Running a cue whose target is missing or stale (Priority: P1)

An author runs a cue whose operation names a place, waypoint, actor, trigger or scenario that is not (or
is no longer) present. The operation performs nothing for that step, a reason is reported, and the cue
continues or completes — the session never ends.

**Why this priority**: it is the documented contract for most cue operations already, and the operations
that break it are reachable by ordinary authoring — wire a camera operation to a waypoint, then rename or
delete that waypoint; or author a camera flight whose speed is dragged to zero; or place a scenario-fork
operation anywhere other than the front of a sequence.

**Independent Test**: author a camera operation aimed at a waypoint, then delete or rename the waypoint
(or target an absent place, or set the speed to zero, or give the path fewer than two waypoints); run the
cue; assert the step performs nothing, a reason is reported, and the run continues to its end. Separately,
run a cue that is a single scenario-fork operation and assert it runs to completion.

**Acceptance Scenarios**:

1. **Given** a cue whose set-camera operation names a place that is not present, **When** the cue runs,
   **Then** the operation performs nothing, a reason is reported, and the session does not end.
2. **Given** a cue whose set-camera operation names a waypoint the place no longer holds, **When** the cue
   runs, **Then** the operation performs nothing, a reason is reported, and the session does not end.
3. **Given** a cue whose fly-camera operation names a chain of fewer than two live waypoints, **When** the
   cue runs, **Then** the operation performs nothing, a reason is reported, and the session does not end.
4. **Given** a cue whose fly-camera operation is authored with a speed of zero or below, **When** it runs,
   **Then** the operation performs nothing and the session does not end.
5. **Given** a cue that is a single scenario-fork operation, **When** it runs, **Then** the scenario is
   forked and the cue completes rather than aborting.
6. **Given** any other cue operation with a missing target, **When** it runs, **Then** its existing
   no-effect-and-report behaviour is unchanged.

---

### User Story 3 - Authoring a conditional branch in the cue editor (Priority: P1)

An author builds a cue containing a conditional node, and can select either branch, change its kind, edit
its fields, add a child beneath it, and remove it — the editor never terminates the application.

**Why this priority**: it is a plain oversight with no preconditions — the editor offers the two branches
of a conditional node for editing, but the code that rebuilds a node from an edited child list omits the
conditional shape. It is reachable in two clicks from any cue.

**Independent Test**: build a cue with a conditional node; for each branch, change its kind, edit a field,
add a child, and remove the branch. Assert each edit is applied and the application continues.

**Acceptance Scenarios**:

1. **Given** a cue containing a conditional node, **When** a branch is selected and its kind is changed,
   **Then** the change is applied and the editor continues.
2. **Given** a cue containing a conditional node, **When** a branch is removed, **Then** the node is
   rebuilt correctly (the remaining branch preserved) and the editor continues.
3. **Given** a cue containing a conditional node, **When** a child is added beneath a branch, **Then** the
   child is queued and the editor continues.
4. **Given** a cue containing a conditional node, **When** the conditional node itself is selected and
   edited, **Then** behaviour is unchanged.

---

### User Story 4 - Undo and redo after a play session that changed the scene (Priority: P2)

An author edits a scene, plays a cue that changes the scene (switching, adding or removing places, or
forking a scenario), returns to editing, and undoes and redoes freely. When the history reaches a step it
can no longer apply or reverse, it **skips that step and reports it** rather than ending the session.

**Why this priority**: it is structural rather than local — a play session's cue mutates the world as a
runtime effect, which can invalidate a recorded step's target — but playing is a normal part of the edit
workflow, so the two must not be able to crash each other.

**Independent Test**: record an edit that names a place; play a cue that removes that place; return to
editing; drive the history to both ends with undo and redo. Assert the application remains stable, the
stale step is skipped and reported, and steps naming present places still work.

**Acceptance Scenarios**:

1. **Given** an applied step naming a place that a play session has since removed, **When** undo reaches
   that step, **Then** the step is skipped and reported, and the session does not end.
2. **Given** the same situation on the redo side, **When** redo reaches that step, **Then** the step is
   skipped and reported, and the session does not end.
3. **Given** a step that switched to a place a play session has since removed, **When** it is undone or
   redone, **Then** it is skipped and reported, and the session does not end.
4. **Given** steps naming places that are still present, **When** they are undone and redone, **Then**
   behaviour is unchanged from today.

---

### User Story 5 - Placing an entity when a place is full (Priority: P2)

An author creates actors, props, waypoints or triggers in a place until it reaches that kind's capacity.
The next create is refused with an explanation instead of ending the session.

**Why this priority**: the fixed capacity is a real bound that ordinary clicking can reach, and one
creation surface (scenario forking) already pre-checks it, so the pattern exists to be applied uniformly.

**Independent Test**: fill a place to capacity for a kind using the create controls; attempt one more;
assert nothing is created, a reason is reported, and the application continues.

**Acceptance Scenarios**:

1. **Given** a place at capacity for triggers, **When** the author adds another trigger, **Then** nothing is
   created, a reason is reported, and the session continues.
2. **Given** a place at capacity for actors, props or waypoints, **When** the author adds another of that
   kind, **Then** nothing is created, a reason is reported, and the session continues.
3. **Given** a place with room, **When** the author creates an entity, **Then** behaviour is unchanged.

---

### User Story 6 - Capturing a place whose links are dangling (Priority: P2)

An author links waypoints into a chain, deletes a successor that the runtime already ignores, and then
registers the scene as a scenario. The capture tolerates the dangling link the same way the runtime does,
rather than ending the session.

**Why this priority**: the runtime deliberately treats a waypoint whose successor is gone as a chain end,
but the capture path reads the raw link and makes that same state fatal — so an edit the runtime accepts
cannot be registered.

**Independent Test**: build a waypoint chain, delete a successor so a link dangles, register the scene as
a scenario; assert the registration succeeds (the dangling link captured as an absent successor) and the
application continues.

**Acceptance Scenarios**:

1. **Given** a waypoint whose successor has been deleted, **When** the scene is registered as a scenario,
   **Then** the capture succeeds with the link recorded as absent, and the session does not end.
2. **Given** a chain with no dangling link, **When** the scene is registered, **Then** behaviour is
   unchanged.

---

### User Story 7 - Starting up when an asset is bad or missing (Priority: P3)

A developer runs the application against an asset file that is unreadable, malformed or uses an
unsupported feature, or with a required resource (such as a font or shader) missing. The problem is
reported per asset and the run continues, skipping the asset — never an unhandled exception.

**Why this priority**: it is startup-focused, so it does not interrupt a live session, but a malformed
asset currently produces a raw, uncatchable failure rather than an actionable report.

**Independent Test**: point the loader at an unreadable, malformed and unsupported asset in turn; assert
each is reported and the process does not die from an unhandled error, and that the run continues.

**Acceptance Scenarios**:

1. **Given** an unreadable, malformed or unsupported model asset, **When** it is loaded, **Then** the
   problem is reported, the asset is skipped, and the process does not die from an unhandled exception.
2. **Given** a model whose node graph is invalid (for example, cyclic), **When** it is parsed, **Then** the
   parse fails as a reported error rather than an unrecoverable failure.
3. **Given** a required resource that is missing, **When** startup runs, **Then** the failure is reported and
   the run continues with the affected feature degraded where degradation is possible (a missing font disables
   text); where no degradation is possible (a shader, without which nothing renders), the failure is reported and
   the run ends cleanly rather than aborting.

---

### User Story 8 - No silent process death from an unexpected failure (Priority: P3)

If something genuinely unexpected does happen, the application reports it with enough context to act on —
naming the failing operation — and ends the run cleanly, rather than vanishing without explanation.

**Why this priority**: it is a safety net for the cases not covered above, and it preserves *Fail Loudly*
(unexpected state is still surfaced, never swallowed) while removing the "silent crash" failure mode.

**Independent Test**: force an unexpected error on the frame path; assert a diagnostic naming the failing
operation is emitted and the run ends in a controlled teardown, not an abrupt abort.

**Acceptance Scenarios**:

1. **Given** an unexpected error on the frame path, **When** it occurs, **Then** a diagnostic naming the
   failing operation is emitted and the application tears down cleanly.
2. **Given** a normal session, **When** it runs, **Then** per-frame overhead is unchanged.

---

### Edge Cases

- A cue operation names an actor, waypoint or trigger by a name that resolves to nothing, or to a handle
  that has since been freed.
- A cue is run more than once, or re-entered while an earlier run is still in flight.
- Undo or redo is issued while a cue is mid-flight, or immediately after a place it references is removed.
- A waypoint a camera operation depends on is deleted or renamed after the operation is wired.
- A fly-camera operation is authored with a path of one waypoint, or zero.
- A scenario is registered (captured) while a waypoint chain points at a successor that has been deleted.
- A creation surface is driven past a kind's capacity by repeated clicking.
- A required startup resource is missing while the affected feature is otherwise unused.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An action that is part of an **expected user story** MUST NOT terminate the application,
  even when the state it acts on is missing, stale, renamed, deleted, or at a bound.
- **FR-002**: Failing the run MUST be reserved for states only a programming error can produce. State that
  a user or an authored asset can reach MUST be handled without ending the run — via a no-effect-and-report
  path or an equivalent graceful outcome.
- **FR-003**: A cue operation whose target (place, waypoint, actor, trigger or scenario) does not resolve
  MUST perform no effect and report a reason, **consistently across every cue operation** — including the
  camera operations.
- **FR-004**: A cue MUST run to its natural end when any of its operations performs no effect, rather than
  aborting — including a cue that is a single scenario-fork operation.
- **FR-005**: An actor whose asset does not define a requested animation clip MUST still play and render
  (a defined pose or a default clip), rather than aborting the session.
- **FR-006**: The cue editor MUST support every node shape it presents for editing, including **both
  branches** of a conditional node, for every edit it offers (change kind, edit fields, add child, remove).
- **FR-007**: When the history reaches a step it cannot apply or reverse without ending the run, it MUST
  skip that step and report it. The check is made by the history runner; the underlying state operations
  MAY remain fail-fast for direct misuse.
- **FR-008**: Creating an entity MUST be refused with a reported reason when the target place is at that
  kind's capacity, on every creation surface.
- **FR-009**: Capturing a place into a scenario MUST tolerate the links the runtime already tolerates
  (for example, a waypoint whose successor has been deleted).
- **FR-010**: An asset or resource that fails to load (unreadable, malformed, unsupported, or missing)
  MUST be reported and skipped, and the run MUST continue where the affected feature can degrade (a missing
  font disables text). Where no degradation is possible (a shader, without which nothing renders), the failure
  MUST be reported and the run MUST end with a controlled teardown — never an unhandled exception.
- **FR-011**: An unexpected failure MUST be reported with the failing operation named, and the run MUST end
  with a controlled teardown rather than an abrupt abort.
- **FR-012**: Existing fail-fast behaviour for genuine programming bugs (stale handles, double-free of a
  storage slot, broken internal invariants) MUST be preserved; this feature narrows *which* states count as
  "user-reachable", it does not remove fail-fast.
- **FR-013**: A diagnostic report MUST be emitted once per event (once per failed step, once per distinct
  missing clip, once per refused creation), never once per frame, and the reporting MUST add no per-frame cost
  on the happy path.

### Key Entities

- **Place**: a named container of the scene's entities; can be present or absent, and may be created,
  restaged or removed at runtime by a cue or an edit.
- **Entity**: an authored scene object (actor, prop, waypoint, trigger) with a fixed per-kind capacity in a
  place.
- **Cue operation**: a step in a cue, each of which can name a target; a step whose target does not resolve
  is expected to perform no effect and report a reason.
- **Undo history**: the single ordered record of edits, whose steps name the place they act on.
- **Asset**: a file-backed resource (model, environment, texture, font, shader) that may be missing,
  malformed or unsupported.
- **Diagnostic report**: the reason a step, a creation or a load performed nothing, surfaced to the author.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every crash path identified by the audit for the user stories above no longer terminates the
  application; each is covered by an automated check that fails against today's behaviour and passes after
  the change.
- **SC-002**: A play session in which the player actor's asset defines neither the standing nor the moving
  clip runs to a user-chosen end without terminating.
- **SC-003**: A scripted smoke run that performs every action named in this spec — spawn, select, edit,
  delete, undo, redo, register, fork, run each cue operation kind, and play — completes with no
  unintended termination.
- **SC-004**: The existing suite of fail-fast / invariant tests continues to pass, so genuine programming
  errors still fail loudly.
- **SC-005**: For the actions named in this spec, the number of user-reachable paths that still abort the
  process is zero; any remaining abort corresponds to a state the user interface cannot reach.
- **SC-006**: Per-frame cost is unchanged (no new per-frame allocation is introduced on the play path).

## Assumptions

- The "users" here are the project's authors (using the editor) and players (using Play mode).
- *Fail Loudly* (constitution) is preserved: this feature draws the line between **programmer error**
  (still fatal) and **user-reachable state** (handled), rather than weakening fail-fast generally.
- The existing no-effect-and-report contract of most cue operations is the model to follow; the feature
  extends it rather than inventing a new mechanism.
- "Session" means one application run; there is no multi-user or server component.
- No new user-facing surface is required beyond the existing diagnostic/report convention — refusal and
  no-op feedback reuse it.
- **Bad-asset policy is report-and-skip** (Clarifications): an unloadable asset is omitted and the run
  continues. A required resource that can degrade (the font) degrades; one that cannot (a shader) is reported
  and the run ends cleanly (FR-010). If the font/shader behaviour proves undesirable in practice, it is
  revisited separately.
- **Scope is the runtime crash classes enumerated above**; the narrower window/GPU-state failures, the
  physics/native-interop failures, and cue undoability are explicitly out of scope.
- The project already runs headless, so the checks for these behaviours can be automated without a window.
- The headless suite is deliberately **device-free**: a path that can only be exercised with a GPU (such as the
  font-disable degrade in the UI layer) is validated by the quickstart run rather than an automated test, and
  `SC-001` is read accordingly.

## Clarifications

### Session 2026-10-12

- Q: Which runtime crash classes are in scope? → A: **The minimum set** — animation clip, camera cue ops,
  the conditional branch in the cue editor, undo/redo after a play session, capacity bounds, and dangling
  links at scenario capture. Window/GPU-state failures (resize, UI batch caps, environment residency) and
  the physics/native-interop failures are out of scope.
- Q: When a model asset is unreadable, malformed or unsupported at startup, should the application skip it
  and continue, or fail startup? → A: **Report and skip** — the failure is reported per asset and the run
  continues.
- Q: When undo or redo reaches a step whose place a play session has since removed, what is the defined
  outcome? → A: **Skip and report at the history runner**, leaving the underlying state operations
  fail-fast for direct misuse (as opposed to guarding every state mutator, or making cues undoable).
- Q: Is a top-level boundary in scope? → A: **Yes, in scope** — an unexpected failure is reported with the
  failing operation named and the run ends with a controlled teardown; it never swallows and continues.
- Q: Should cues be undoable (runtime effects recorded as steps)? → A: **Deferred.** Out of scope for this
  spec; a separate design decision and spec.
