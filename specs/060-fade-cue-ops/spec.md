# Feature Specification: Fade Cue Ops

**Feature Branch**: `060-fade-cue-ops`

**Created**: 2026-10-03

**Status**: Draft

**Input**: User description: "Implement the fade cue ops that are already implemented in odin game."

## Origin

`odin-game` carries two cue ops, `Cue_Op_FadeOut` and `Cue_Op_FadeIn`, each taking `duration: f32`
(`src/cue.odin:103-116`). They are the ops that drive the screen's `fade` field: on begin they capture the
current level into a run state (`initial_fade`, `start_time`, `end_time`); while in flight they set
`ctx.screen.fade = initial + t * (target - initial)` with `target` 1.0 for a fade-out and 0.0 for a
fade-in; and at the deadline they set the endpoint **exactly** and free the state
(`src/cue.odin:962-977, 1242-1260`). The cue editor labels them `FadeOut 1.0s` / `FadeIn 1.0s` and edits a
Duration field (`src/cue_editor.odin:597-600, 1465-1495`).

This port now has the field they drive — `GameplayState.Fade` and the black overlay the gameplay screen
draws over the game view (`specs/059-fade-overlay`) — but nothing animates it. This feature adds the two ops
that do, as the screen-transition primitive of the cue family the port builds op by op
(`specs/054`-`058`). It is the last step that makes a cue able to fade a scene to black and back.

The port follows the reference's shape but not its storage: a leaf that spans time carries its numbers in
its own `…State` case, the same convention `Wait`/`WaitState` and `FlyCamera`/`FlyCameraState` already use
(`world/Cue.fs`), rather than in a slot-mapped state table.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A fade op drives the screen level over its duration (Priority: P1)

A cue carrying a **fade out** darkens the gameplay view from wherever the level is when the op is reached
to fully black over its duration; a **fade in** clears it back to untouched. The level moves smoothly
across the op's duration and lands **exactly** on its endpoint. A duration of zero (or less) is over before
it begins: the level jumps straight to the endpoint and the op costs no time. Starting from a part-way
level works: the op fades from there, not from an endpoint.

**Why this priority**: it is the whole capability, and it is testable headlessly against `GameplayState.Fade`
with no window and no GPU.

**Independent Test**: set the level, put a fade op on the cue, step the play chain at a few times across
its duration, and assert the level at each; assert it lands exactly on 1.0 (out) or 0.0 (in) at the
deadline, and that a zero-duration fade completes in the step it begins.

**Acceptance Scenarios**:

1. **Given** the level at 0, **When** a `FadeOut` of duration 2 is reached and the play chain steps at 0,
   1 and 2, **Then** the level is 0, 0.5 and 1.0 respectively, and the cue has moved past the op.
2. **Given** the level at 1, **When** a `FadeIn` of duration 2 runs, **Then** the level is 1, 0.5 and 0.0
   at 0, 1 and 2, and the op completes.
3. **Given** the level is part-way (say 0.4), **When** a `FadeOut` of any duration begins, **Then** it
   darkens from 0.4 to 1.0 (not from 0), and a `FadeIn` likewise clears from 0.4 to 0.0.
4. **Given** a fade of duration 0 (or a negative duration), **When** it is reached, **Then** the level is
   set to the endpoint in that same step and the op holds no run state.
5. **Given** an in-flight fade, **When** the chain steps again, **Then** the level has moved but the cue
   value is unchanged (the op is still working), and no allocation is made per step.
6. **Given** a fade completes, **When** the next op in the sequence runs, **Then** the level stays at the
   endpoint it landed on until something else changes it.

---

### User Story 2 - The fade ops are authored in the cue panel (Priority: P2)

The cue panel lists both fade kinds; choosing one gives a default duration the author retunes with the same
number field the other timed ops use. Each accepted edit is one step on the one undo history, and a fade
that is in flight is read-only, exactly as the other running leaves are.

**Why this priority**: the authoring half. A fade is useful from code without it, but it is how the author
places one.

**Independent Test**: assert the kinds are offered, the labels, the default, that retuning commits one
step that undo restores, and that a running fade is locked.

**Acceptance Scenarios**:

1. **Given** a selected cue node, **When** the author opens the kind chooser, **Then** "Fade out" and
   "Fade in" are offered, and choosing either makes a fade with a sensible default duration.
2. **Given** a selected fade, **When** the author retunes its duration, **Then** the cue's fade changes as
   exactly one step on the one history, and undo restores the previous duration.
3. **Given** a fade in flight, **When** the panel draws it, **Then** its duration cannot be retuned (the
   running op is locked) but it is still shown as the op it came from (`Fade out` / `Fade in`).
4. **Given** a fade is retuned while a copy of it is running, **When** the edit is committed, **Then** the
   running op is undisturbed; the edit changes what a *future* run fades.

---

### Edge Cases

- **Duration exactly 0, or negative**: over before it begins — the level is set to the endpoint and the op
  completes with no run state (the same rule `Wait`'s `beginWait` uses).
- **Starting part-way**: the op captures the level where the walk reaches it, so a fade out from 0.6 and a
  fade in from 0.6 both behave correctly (they do not assume an endpoint).
- **Landing on the endpoint**: at the deadline the level is set to exactly 1.0 (out) or 0.0 (in), never a
  hair short from floating-point interpolation — the reference's explicit endpoint assignment.
- **The level stays in range**: the start is in 0..1 (spec 059) and the endpoint is 0 or 1, so a linear
  interpolation between them never leaves 0..1; no clamping is needed.
- **Two fades at once** (a fade forked beside a running fade): both write the level in the frame; the walk's
  order decides which wins that frame, and each still lands its own endpoint when its deadline passes —
  document this rather than forbidding it (a cue value is immutable and forks are how cues compose).
- **A fade that is interrupted by a new fade beginning**: the new op captures the level as it stands, so
  there is no jump — this is what "capture the start" buys.
- **Editing a fade's duration while a copy runs**: the run holds its own numbers; the edit changes a future
  run only.
- **While editing**: cues do not run, so the level does not move; the Misc slider is the only writer while
  paused.
- **A fade in an empty sequence or after `Fin`**: unreachable ops never begin, so no fade happens.
- **Re-running the same authored cue**: each run captures the level afresh, so a repeat fades from wherever
  the level then is (the level persists across runs until something changes it).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The cue MUST gain two ops — a **fade out** and a **fade in** — each carrying a **duration**
  in seconds. Both MUST be ordinary leaf ops with no children.
- **FR-002**: When a fade out is reached, it MUST drive the screen's fade level from the level at that
  moment to 1.0 over its duration; a fade in MUST drive it to 0.0. The effect is on the gameplay screen's
  level (spec 059), so the fade level is what the overlay renders.
- **FR-003**: A duration of zero or less MUST be over before it begins: the level is set to the endpoint in
  the step the op is reached and the op completes with no run state.
- **FR-004**: While a fade is in flight it MUST hold its starting level and its deadline in its own
  run-state case, and it MUST cost no time beyond its duration.
- **FR-005**: At its deadline a fade MUST set the level to exactly its endpoint (1.0 for a fade out, 0.0
  for a fade in), not an interpolated approximation.
- **FR-006**: A fade in flight MUST allocate nothing per step (an idle-step property like the other timed
  ops).
- **FR-007**: The cue panel MUST offer both fade kinds with a default duration, MUST show a fade in flight
  as the op it came from (`Fade out` / `Fade in`), and MUST let the author retune the duration as one step
  on the one undo history.
- **FR-008**: A fade in flight MUST be read-only in the panel (the running-path lock), exactly as the other
  running leaves are.

### Key Entities *(include if data involved)*

- **Fade level**: the screen's 0..1 black-overlay opacity (`specs/059`), which these ops drive.
- **Fade out / fade in ops**: a duration each; leaves of the cue.
- **Fade run state**: the starting level, the duration and the deadline an in-flight fade holds, so it can
  interpolate toward its endpoint and land exactly on it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An author can darken the gameplay view to fully black or clear it to untouched over a chosen
  duration, from any starting level, in a cue.
- **SC-002**: A completed fade leaves the level exactly at its endpoint (1.0 or 0.0).
- **SC-003**: An in-flight fade allocates nothing per step, and a zero-duration fade costs no time.
- **SC-004**: An edit to a fade's duration is exactly one step on the one undo history.
- **SC-005**: The projects build with no warnings and the suite is green, with no device opened.

## Assumptions

- **Linear interpolation**, as the reference uses (`initial + t * (target - initial)`); no easing curve is
  specified by the request or the reference.
- **Seconds on play time**, the clock the other timed ops use (`Wait`, `FlyCamera`), so a fade freezes while
  the editor is open and resumes with play.
- **One shared level, no separate fader**: both ops drive the same `GameplayState.Fade` the overlay reads;
  there is no second fade value or colour (the overlay is black, spec 059).
- **No clamp in the editor**: a negative duration is allowed and means "immediate", the same as `Wait`; the
  reference clamps to 0, which is behaviourally identical because the engine treats `<= 0` as immediate.
- **The effect itself is spec 059's**: this feature only drives the level; the black overlay and the Misc
  slider are already built.
- **Fades are not persisted or authored data beyond the cue value**; like the other ops they exist only in
  the cue, which nothing serializes yet.
