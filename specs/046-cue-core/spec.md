# Feature Specification: Cue Core

**Feature Branch**: `046-cue-core`

**Created**: 2026-09-30

**Status**: Draft

**Input**: User description: "let's run specify" — for the cue system settled in this session's design, recorded in
`docs/CUE_MODEL_DECISION.md`.

## Origin

Phase 9 of `docs/ROADMAP.md` — "Cue engine + editor" — is delivered as **two** features. This spec is the **engine
half**: the cue vocabulary, the run, and the state the editor will read. The editor half (the Cue panel in the left
column's tab strip, and the cue's own undo history) follows as its own spec, so phase 9's "done when" — *you can
author and run a scripted sequence in the cue editor* — is met by the pair rather than by this one alone. Nothing
here authors a cue through the interface; cues are authored in code and exercised by tests.

**A cue *is* its run.** `My Game 3`'s lineage — "where the cue *value* is the run state" — is not just the lineage
for the remainder here; it is the whole model. The cue held on the gameplay screen *is* the run: an immutable value
that shrinks as it is stepped, down to `Fin`, which is what is left when a cue finishes and where it then rolls.
Three constraints shape it:

- **A subcue is a cue.** A cue forked in never knew the cue it was forked into, and a subcue lifted out of its
  parent is meaningful on its own. So progress is expressed **in cue values** — never as a position in the
  structure a run started from.
- **The whole game can live inside one running cue.** Triggers and scenarios extend the running cue at runtime, so
  the bound is *how many nodes are live at once* — never how many steps have been run, and never the size of the
  authored cue.
- **The editor shows the running cue.** What the author sees is what is left to run, so the current state is the
  cue itself rather than something tracked beside it. As the cue runs it shrinks until it is `Fin`; that is the
  intended behaviour, not a loss.

Two arrangements were considered and retired. The **reference's** template/runtime split — `Cue = { template_root,
state_root, state_map }` with a separate `CueState` union and a slot map — was built, measured *and* dropped: its
array bought **nothing** on the frame path (0 bytes per frame either way; the zero allocation that seemed to
justify it came from `value option`), and it cost a fixed 64-frame capacity that fails loudly in a shipped game
plus a handle-and-generation concept for every reference. A **second** arrangement, an immutable authored cue kept
beside a separate run of frames with an `Outcome`, survived being run but split one idea into two types and needed
a flag to tell "not yet begun" from "finished". Both are superseded by the model this spec now specifies;
`docs/CUE_MODEL_DECISION.md` records the measurements, and this spec's own suite (`test/CueTests.fs`) asserts
them.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A cue runs as written, and shrinks to Fin (Priority: P1)

A cue is put on the screen's cue state. The game does what the cue says: each op at the time the cue asks for it,
ops that cost no time costing none, and when the cue is done what is left is `Fin` — which then rolls where it is.

**Why this priority**: this is the capability. Every later story is a property *of* a running cue.

**Independent Test**: build a cue in code, put it on a state's cue field, step it against a scripted clock, and
assert the cue after each step. Needs no editor, no trigger and no GPU — the whole story is a value transformed by
a function.

**Acceptance Scenarios**:

1. **Given** a cue of "wait two, an instant op, wait one", **When** it is stepped a second at a time, **Then**
   the instant op happens at t=2 and what is left is `Fin` at t=3 — not later, and not early.
2. **Given** a cue of two branches, **When** it is stepped, **Then** both run and the cue reaches `Fin` only when
   the later one does.
3. **Given** a sequence with a detached branch in the middle, **When** it is stepped, **Then** the branch runs
   beside the rest of the sequence and the cue reaches `Fin` only when the remainder *and* the branch have.
4. **Given** a cue whose ops all cost no time, **When** it is stepped, **Then** it is `Fin` within the first step.
5. **Given** a cue that has reached `Fin`, **When** it is stepped again, **Then** it stays `Fin` — `Fin` advanced
   is itself, and nothing further happens.
6. **Given** a cue value, **When** it is run a second time from that same value, **Then** it runs identically from
   the start — running returned new values and consumed nothing of the one it was given.
7. **Given** a cue and a clock, **When** it is stepped twice against the same steps, **Then** both runs are in the
   same state after every step — nothing about a run depends on when, where or how often it is made.

---

### User Story 2 - A cue can be forked (Priority: P2)

A cue runs beside another: a `Fork` written in a sequence runs its branch beside the rest of that sequence, and a
cue arriving from elsewhere is forked into whatever is running. What is running is not disturbed — a wait halfway
through stays halfway through.

**Why this priority**: it is the mechanism by which the whole game lives inside one running cue, and it is the
constraint that fixed the shape of a sequence's state. It ranks below Story 1 because a cue that cannot run cannot
be forked into.

**Independent Test**: from a running cue, fork a second in; assert the forked cue runs, and that the running cue's
remaining time is unchanged.

**Acceptance Scenarios**:

1. **Given** a sequence with a `Fork` in it, **When** it is stepped, **Then** the branch runs beside the remainder,
   and the remainder is not disturbed.
2. **Given** a cue part-way through a wait, **When** a cue is forked into it, **Then** the forked cue runs beside
   the running one, and the running one keeps its remaining time (0 ms disturbed).
3. **Given** a forked cue, **When** it is stepped, **Then** it runs exactly as if it had been written there — it
   carries no knowledge of its host, and the same value can be forked in again elsewhere.
4. **Given** nothing running, **When** a cue is forked in, **Then** it is simply the cue (`Fin` forked into is the
   cue itself).

---

### User Story 3 - The cue is the screen's, and it is frozen while editing (Priority: P3)

The cue lives on the gameplay screen, not on a zone, so a cue that switches zone keeps running; and it advances
only while playing, so opening the editor freezes it and Play resumes it.

**Why this priority**: it is a property of *where the state lives* and *when it advances* rather than a separate
capability, and it is the whole reason the cue is not a zone's. It ranks below the two above because the engine is
useful before it is exercised.

**Independent Test**: run a cue that switches zone mid-sequence, assert every later op still happens and nothing
before the switch is lost; then open and close the editor and assert the cue resumed.

**Acceptance Scenarios**:

1. **Given** a cue switching zone between two waits, **When** it runs, **Then** the switch happens at its place
   and the cue continues with its remaining time intact.
2. **Given** a detached branch still running, **When** the sequence switches zone, **Then** both continue.
3. **Given** a cue part-way through, **When** the editor is opened and Play pressed again, **Then** the cue
   resumes where it was rather than restarting.

---

### Edge Cases

- **A cue that never ends**: normal, not an edge — the game is expected to live inside one. Nothing accumulates
  as it runs (Story 1 / FR-011).
- **A detached branch with nothing after it**: the sequence is just the branch, and the cue is over when the
  branch is.
- **A detached branch that costs no time**: it happens where it is reached and the sequence carries on in the
  same step.
- **A branch ending while the remainder still runs, and the reverse**: the cue is over only when *both* have
  finished; neither side finishing early ends it.
- **Nesting**: sequences inside branches, branches inside nested sequences, and branches inside branches are all
  legal, and each level's completion rule is the one above it.
- **A `Fork` outside a sequence**: meaningless — a cue written that way is malformed and must fail loudly rather
  than be ignored.
- **A zero or negative duration**: an op that asks for no time, or for time that has passed, is over before it
  begins.
- **Forking into nothing**: a cue forked in with nothing running is simply the cue (`Fin` forked into is the cue).
- **Forking into a finished cue**: there is nothing to run beside, so the forked cue is the cue.
- **A cue ending exactly on a step boundary**: the cue is `Fin` at that step, once — not twice, and not one step
  later.
- **Entering and leaving play mode mid-cue**: the cue is frozen while the editor is open and resumes on Play
  (FR-002); nothing about the cue is consumed by doing so.
- **The cue replaced while running**: replacing the cue's state *is* the beginning of the new cue; there is no run
  beside it to release (FR-013).
- **A very long run of no-time ops**: thousands in a row must not grow the stack or retain anything per op
  (FR-011, Story 1's scenario 4).
- **A `Fin` written into a sequence**: it is nothing to run, so an authored `Fin` is simply **skipped** and
  the sequence carries on past it (`Sequence [Fin; Exposit "x"]` runs the exposition). The type system does not
  prevent it, and stopping the editor from offering it is out of scope.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A cue MUST be an immutable value, and advancing it MUST return the next value without modifying the
  one it was given — so the same value can be run again from the start by a holder, and shared between holders.
- **FR-002**: A cue MUST advance **only while playing**. With the editor open it MUST be frozen, and pressing
  Play again MUST resume it where it was — the existing gate on `not state.Editing` is what provides this.
- **FR-003**: The cue MUST be a state of the **gameplay screen** and outlive a zone switch. It is not a zone's
  state, and it is not cross-screen state.
- **FR-004**: A sequence MUST run its children in order; a child that costs no time MUST be performed where it is
  reached; the child after the running one MUST start in the same step the running one completes.
- **FR-005**: Two branches of a cue MUST run at once, and the point MUST complete only when both sides have.
- **FR-006**: A `Fork` MUST run beside the rest of its sequence, and that sequence MUST complete only when the
  remainder and every fork under it have finished.
- **FR-007**: An op with no progress of its own — an effect performed where it is reached — MUST hold no state
  record: there MUST be nothing to leak, and a cue made only of such ops MUST be `Fin` within its first step.
  (Distinct from a *timed* op whose duration happens to be zero.)
- **FR-008**: A cue MUST be able to change the game's place (zone) and carry on: the switch is one of the ops, and
  everything not yet run survives it.
- **FR-009**: A cue MUST be forkable into whatever is running — it then runs beside it — and forking MUST NOT
  alter what is already running, timers included. A cue forked in carries no knowledge of its host.
- **FR-010**: The screen's cue MUST *be* the current state of the run: the running op first, the unstarted ops
  behind it, the ops already run simply absent; and with nothing running it MUST be `Fin` — the value the
  editor's panel will render as `cue_end`. Reading it MUST NOT alter it.
- **FR-011**: The memory a cue holds as it runs MUST NOT grow as it advances — no matter how many steps it has run,
  or how many cues have been forked into it. The cue MUST be bounded by how many nodes are live at once, and a
  very long run of no-progress ops MUST NOT grow the stack.
- **FR-012**: A step that changes nothing MUST allocate nothing, and a step that completes an op MUST allocate
  only a small, bounded amount that does not grow with the cue's length. (Constitution V; the game loop runs every
  frame.)
- **FR-013**: A cue MUST begin, be replaced and end by the single act of setting the screen's cue state: there is
  no separate start call, and no run kept beside the cue to release.
- **FR-014**: A malformed cue MUST fail loudly at the point it is reached, not be silently skipped. (The one such
  shape in the first cut is a `Fork` outside a sequence's current op.)
- **FR-015**: The cue MUST advance deterministically: the same cue stepped with the same clock MUST reach the same
  state, with no dependence on iteration order, hashing, timing or ambient state.
- **FR-016**: The engine MUST be exercisable **headlessly** — no window, no GPU, no device, and no content loaded —
  so cue behaviour is testable as plain logic (the test project cannot open a device; it builds a device-free
  state the way `test/GameplayStateTests.fs` does).
- **FR-017**: The first cut of the cue vocabulary MUST include the three control-flow shapes (run in order, run at
  once, run beside), a terminal `Fin`, and leaves proving each *shape* of op: one with no progress of its own, one
  that changes the game's place, one whose progress is a deadline, and one whose progress is more than a deadline.
  Adding a further leaf MUST NOT change the shape of the run or the walk — it adds a leaf op and, only if its
  progress needs numbers the authored form does not carry, its own `…State` case.
- **FR-018**: `dotnet build` MUST be clean and `dotnet test` MUST pass, with new tests covering the stories above
  as pure logic requiring no GPU.

### Key Entities *(include if feature involves data)*

- **Cue** (`world/Cue.fs`, new): the vocabulary *and* the run — one type. The control flow (`Sequence`, `Parallel`,
  `Fork`), the leaves, and the terminal `Fin`. A value: advancing returns the next value and mutates nothing.
- **`Fin`**: the terminal cue. What a finished cue is, and the cue that rolls — advancing it is itself.
- **`…State` case**: a leaf's in-flight form, carrying the numbers the authored form does not — `WaitState`'s
  deadline, `MoveCameraState`'s start. Named for the op with a `State` suffix.
- **A sequence's `current` and `rest`**: the op in flight, and the queue not yet started, held as cue values. The
  running op is held *out* of the deque so that a step that changes nothing touches no deque at all.
- **Screen cue state** (`state/GameplayState.fs`): the screen's `Cue`, where the running cue lives, beside its
  `PlayTime`. There is no separate run.
- **Play chain** (`screen/GameplayScreen.fs`, over the systems in `world/Systems.fs`): the ordered steps that run
  while playing; advancing the cue is the last of them.
- **Fork**: the mechanism by which a cue runs beside another — the `Fork` op inside a sequence, and the engine's
  fork of an arriving cue into the running one.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A cue runs exactly as written: for a scripted clock, every op happens at the step the cue asks for
  and the cue is `Fin` at the step after its last op (0 timing deviations across the acceptance scenarios).
- **SC-002**: A cue of no-progress ops is `Fin` within one step, and a cue of 200,000 of them in a row neither
  fails nor grows the stack.
- **SC-003**: A cue's footprint does not grow with its length: a 40-step cue holds the same peak number of live
  nodes as a 4-step one, and ten thousand steps hold no more than the longest single op needs.
- **SC-004**: A waiting cue costs the game loop nothing: 0 bytes allocated per step while an op is in flight
  (measured, flat and nested), and a bounded amount per op completed that does not grow with the cue's length.
- **SC-005**: Forking disturbs nothing: the running cue's remaining time is unchanged (0 ms), and the forked cue
  runs beside it.
- **SC-006**: A cue that switches zone loses nothing: every op after the switch runs (0 dropped), and no op before
  it runs twice (0 duplicated).
- **SC-007**: The screen's cue always matches what is executing: 0 readings that show a finished op, and a
  finished cue is `Fin`.
- **SC-008**: The engine is demonstrable with no graphics device: the cue stories are covered by tests that run
  headlessly, `dotnet build` is clean and `dotnet test` passes.
- **SC-009**: Two runs of the same cue against the same clock reach the same state after every step (0 differences
  across the acceptance scenarios).
- **SC-010**: Every shape of op in the first cut is exercised by at least one acceptance scenario — no-time,
  changes-place, deadline, and more-than-a-deadline (4 of 4 shapes, 0 unexercised).

## Assumptions

- **The editor is a separate feature.** The Cue panel, the cue's own undo history and the questions they raise are
  deferred to the editor spec. This spec supplies the state that panel reads (the screen's cue, which shrinks to
  `Fin`). **Deferral with a stated trigger**: the editor spec starts as soon as this one is implemented.
- **Cues are authored in code here.** There is no interface for building a cue in this feature; the tests are the
  authoring surface. That is what makes phase 9's engine half independently testable.
- **The first cut of the op vocabulary** (informed guess, recorded so review can veto cheaply): the control flow
  (`Sequence`, `Parallel`, `Fork`) and the terminal `Fin`; `Wait` for the deadline shape; `Exposit` for the
  no-time shape; `SwitchZone` for the "changes the game's place" shape; and `MoveCamera` for the shape whose
  progress is more than a deadline. An animation op was the first candidate for that fourth shape and was
  rejected — an actor's animation state lives on its renderer, so it could not be asserted headlessly. The
  reference's wider family — camera paths, actor and avatar warps, fades, emotes, enable/disable sets, advent
  flags, scenario binds — is added per op later, each as its own increment (FR-017), and each is the *narrative*
  half of the roadmap's later phases rather than this one's.
- **There is no join and no splice.** The mechanism by which a cue arrives at a running one is the **fork**
  (FR-009): it runs beside what is running rather than being inserted into it. Composing cues into a new value —
  "run this, then that" — is authoring (`Sequence`), not a run operation.
- **Trigger → cue wiring is not here.** Triggers exist (`specs/029-triggers`), but forking a cue into the running
  one is exercised here as a pure function; wiring a placed trigger to a cue is phase 10. Advents and the scenario
  library are phases 10–11.
- **No persistence.** The project has no save/load; nothing about a cue is written to disk (phase 15), and there
  are no saved projects to migrate.
- **Play mode is the only time a cue advances.** Editing and play are exclusive in the existing editor, and that
  is the freeze FR-002 relies on — not a new pause mechanism. The cue is the **last** step in the play chain
  (after animation and camera follow), following the reference's ordering (`docs/ARCHITECTURE.md` lists the chain
  as `… clock → cue`), so an op's effect on a zone or the view is what the *next* frame's systems see.
- **Determinism is a property of a run of this program**, not a bitwise guarantee across machines: the same cue
  stepped with the same clock and the same world state reaches the same state, with no dependence on iteration
  order, hashing or ambient state. A fixed simulation timestep — and any claim that spans machines — is phase
  13's (`docs/ROADMAP.md`, "Systems + fixed timestep"), and this feature neither requires nor prevents it.
- **The cue's state is not undoable.** Undo and redo act on the authored cue alone (the editor spec); the screen's
  running cue is never on a stack and no command touches it. Because the running cue shrinks as it runs, the
  editor's history is the editor's to define.
- **The documentation this feature corrects** is expected to move with it: `docs/ARCHITECTURE.md`'s state diagram
  and its "Scenario & Cue" section; `docs/EDITOR_DESIGN.md` D-010; `docs/ROADMAP.md` phase 9; and
  `docs/PROJECT_STRUCTURE.md`'s module list.
