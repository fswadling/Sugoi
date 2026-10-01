# Phase 0 Research: Cue Core

The design is settled — `docs/CUE_MODEL_DECISION.md` records it, and a scratch sketch demonstrated it (its second
one, where the cue value *is* the run). That sketch has since been removed; this feature's own suite
(`test/CueTests.fs`) now asserts what it measured. So most of this phase is *not* inventing: it is finding where
the settled design meets the port's existing shape, and several of the eight decisions below exist only because
it does.

## Decision 1 — The first cut of the vocabulary: the control flow, `Fin`, and four leaves

- **Decision**: the control flow (`Sequence`, `Parallel`, `Fork`), the terminal `Fin`, and four leaves —
  `Wait`/`WaitState`, `Exposit`, `SwitchZone`, and `MoveCamera`/`MoveCameraState`. The first three leaves prove the
  shapes the spec names (*no progress of its own*, *changes the game's place*, *progress is a deadline*);
  `MoveCamera` proves the one that matters most — **a timed op whose progress is more than a deadline**.
- **Rationale**: FR-017 requires one leaf per *shape* rather than a particular family, because the thing being
  proved is that a leaf's run state generalises beyond "a deadline is in the future". `Wait` is a single float;
  `MoveCamera` carries where the camera was, where it is going, and when it should arrive — so it exercises the
  case that a leaf's state can be several values of its own. Each of the three effects is chosen for being
  **assertable headlessly**: an exposition writes `GameplayState.Dialogue` (screen state, and already precedent for
  outliving a zone switch), a zone switch calls `GameplayState.switchZone` (existing, and already failing loudly
  for an unknown zone), and a camera move writes `GameplayState.Camera` (plain data, read by
  `Systems.cameraFollow`).
- **The `…State` convention**: a leaf whose run form holds numbers the authored form does not gets a second case
  suffixed `State` — `Wait` → `WaitState`, `MoveCamera` → `MoveCameraState`. That is `My Game 3`'s convention
  (`wait`/`waitstate`) and FR-017's rule ("a leaf op and its own state case"). It is not a template/runtime split:
  the `…State` case is the *same op*, carrying its own numbers, and it is what the value becomes as it begins.
- **Alternatives considered**: the reference's wider family (camera *paths*, actor and avatar warps, fades,
  emotes, enable/disable sets, advent flags, scenario binds) — deferred per op, as the spec's Assumptions say.
  A fade was the first candidate for the fourth shape and was **rejected**: the reference's `Gameplay_Screen` has a
  `fade` field and this port's does not, so a fade op would add a field and a render-path consumer — a second
  feature hiding inside this one. An animation op (an actor's slot weight moved over a duration) was also
  rejected: an actor's animation state lives on its renderer, so it could not be asserted in a suite that cannot
  open a device. `FlyCamera` with a waypoint chain is the deferred sibling of `MoveCamera`.

## Decision 2 — The cue *is* the run: one type, and `Fin` is the third state

- **Decision**: one type. There is no `Frame`, no `Outcome`, no authored cue kept beside a run, and no state array.
  A cue value *is* the run: `Fin` (the terminal), `Sequence(current, rest)`, `Parallel`, `Fork`, the leaves, and a
  `…State` case per timed leaf. Advancing reports the cue as it now stands (`Cue voption`): a cue in hand —
  `Fin` included — or nothing to change.
- **Rationale**: this is the `My Game 3` lineage the first sketch kept only for the *remainder* — "the cue value is
  the run state" — taken all the way. It collapses two ideas into one, and it removes a piece of bookkeeping the
  first arrangements needed: **"not yet begun" and "finished" are no longer confused**, because finished is `Fin`,
  a *value*, not an absence. Story 1's "an ended cue stays ended" is then just `advance Fin = Fin`, with no
  `CueStarted` flag and no `Run : Frame voption`. It also makes "the editor shows the running cue" a
  tautology: the screen's cue *is* the run, so there is nothing to derive (`live`/`current` are gone).
- **A measured correction, inside this decision.** The most literal form — `Sequence of children: Cue FDeque`, the
  running op at the front — was drawn, implemented and measured: **72 bytes per idle tick at every level of
  nesting** (144000 B / 2000 flat, 432000 / 2000 three deep). Stepping the front means `FDeque.uncons` on the
  *idle* path, and `uncons` allocates the tail it returns, every frame, whether or not anything moved. Holding the
  op in flight out of the deque (`Sequence(current, rest)`) removes it: the idle path then touches no deque at all
  and reports nothing to change, and the suite measures 0 bytes. This is the sort of thing that regresses silently,
  so it is recorded as a decision rather than left to the implementer's taste.
- **Alternatives considered**: **the reference's arrangement** (`Cue = { template_root, state_root, state_map }`
  with a separate `CueState` union and a slot map) — built and measured *and* dropped: the array bought nothing on
  the tick path (0 bytes per frame either way — the zero allocation that seemed to justify it came from
  `value option`), and it cost a fixed 64-frame capacity that fails loudly in a shipped game plus a handle and a
  generation per reference. **A second arrangement** — an immutable authored cue beside a run of frames with an
  `Outcome`, which is the first sketch — survived being run but split one idea into two types and needed a flag to
  tell "not yet begun" from "finished"; retired in favour of this one. **A `SequenceState` sibling** for the
  running sequence — rejected: it would be a template/runtime split in miniature, which is the split this model
  exists to collapse. **The front in the deque** — rejected on the measurement above.

## Decision 3 — The step returns the next cue, and performs its ops' effects where the walk reaches them

- **Decision**: `CueEngine.advance (shared) (state: byref<GameplayState>) (now) (cue: Cue) : Cue` — it walks the
  cue, performs each op's effect **where the walk reaches it** (`switchZone`, `state.Dialogue`, `state.Camera`),
  and returns the next cue. `CueEngine.tick` is `state.Cue <- advance shared &state now state.Cue`. There is no
  request type and no applier.
- **Rationale**: this is what the port's play-mode systems already do — `playerInput shared.Physics zone …`,
  `cameraFollow zone &state.Follow &state.Camera` — and it is what the design's own sketch does: its `effect`
  performs an effect where it is reached, which is the whole reason the cue can stay a value while the ops do their
  work. An earlier draft had the engine *ask* for its effects in a struct instead; it rested on a false premise —
  that a `GameplayState` cannot be built in a test — when `test/GameplayStateTests.fs:62` builds exactly that state
  headlessly and already calls `GameplayState.switchZone shared &state "classroom"`. The suite therefore asserts the
  **state the cue changed** rather than a request for it, which is the stronger assertion to have in a test.
- **Costs, stated**: (i) the engine's module is compiled **after** the state it affects, so a `world/` file sits
  below `state/` and the `.fsproj` has to say why; (ii) an op's effect happens where the walk reaches it, so a
  malformed cue — a `Fork` outside a sequence's current op — raises with the ops before it already applied. That is
  accepted: a malformed cue is an authoring bug, and the engine raises loudly naming the node it reached (FR-014).
- **Alternatives considered**: **effects as a request struct applied by the host** — it costs a type, an applier,
  more lines in the caller and a "the last request of each kind wins" caveat, and it existed to route around a
  premise that turned out to be wrong. If a *preview* of a cue ever becomes a requirement, this is the decision to
  revisit. A signal *list* (the reference's exact shape) — larger again, allocates a cons per signal, and the port
  has no signal type.

## Decision 4 — The clock is *play* time, absolute, and the deadline lives in the cue

- **Decision**: add `mutable PlayTime : float` to `GameplayState`, advanced by the frame's `Dt` **inside the
  existing `not state.Editing` block**, and pass it to the engine as `now`. Deadlines are absolute and are carried
  by the op's `…State` case (`WaitState of seconds * endsAt`).
- **Rationale**: two findings, both load-bearing.
  1. **An edit must not advance the clock.** The play chain already runs only while playing
     (`screen/GameplayScreen.fs`'s `if not state.Editing`), so a play clock accumulated inside it is frozen while
     the editor is open and **pressing Play resumes a cue rather than leaping it forward** — FR-002's whole point.
     The engine's `Screen.Frame` carries only `Dt` (no absolute time), and adding one there would accumulate in
     *every* frame including editing ones, which is precisely the wrong clock.
  2. **Absolute, not a countdown.** A timed op stores the deadline it computed; while it waits, the cue value is
     **identical** from tick to tick, so the step reports nothing to change and allocates nothing. A relative
     countdown would rewrite the cue on every tick, and a rewrite is an allocation on every frame of every cue in
     the game. The absolute form is what keeps a waiting cue free (research D6).
  3. **A duration is relative to the `now` the op began at.** `PlayTime` is a float64 summing the frame's float32
     `Dt`, so it drifts from wall time by the loop's own granularity — but every deadline is `now + seconds` from
     the *same* `now`, so the drift cancels across an op's life. Determinism (FR-015) is exact for a given `Dt`
     sequence; a bitwise claim across machines is phase 13's fixed timestep, not this feature's.
- **Alternatives considered**: an absolute time on `Screen.Frame` (wrong clock, above); a per-zone clock (a cue
  spans zones, so it cannot be a zone's); deriving `now` from the physics world's step count (Jolt's stepping is
  the physics world's business, and the cue must work when physics is idle); a relative countdown (rejected on the
  allocation argument — the strongest reason in this decision).

## Decision 5 — The cue advances last in the play chain, and it needs no start

- **Decision**: two lines, after `Systems.animate`, inside the existing `not state.Editing` block — advance
  `PlayTime` by the frame's `Dt`, then `tick`. There is no start call, no `CueStarted`, and no separate run:
  setting the cue's state is the beginning (FR-013).
- **Rationale**: the reference's chain ends `… clock → cue` (`docs/ARCHITECTURE.md`), and the port's chain
  (`screen/GameplayScreen.fs`: input → sync → triggers → camera follow → animate) has the same shape with the cue
  absent. Advancing last means an op's effect — a zone switch, a camera move, an exposition — is what the *next*
  frame's systems see, which is the ordering the reference's scripts were written against and the one that makes a
  camera op win over the follow camera rather than be overwritten by it. "No start" falls out of Decision 2's
  `Fin`: the first play tick reaches the cue's front op, performs it if it costs no time and begins it if it is
  timed — which is exactly what "starting" would have meant, without a call or a flag to keep in step. The
  reference does the same thing internally (`cue_tick` starts the root if idle), and it had to *clear* the template
  when the root finished so it would not restart; with `Fin` there is nothing to clear.
- **Accepted consequence**: because the cue runs last, an op's effect lands *after* this frame's systems have run.
  A dialogue line and a camera move are drawn this frame (`draw`/`renderScene` read the state the cue just wrote),
  but a zone switch means this frame simulated the outgoing zone and the next frame simulates the incoming one.
  That is the shape an editor-side zone switch already has (it lands mid-frame in `Editor.update`), and the one
  artefact it can produce is benign: `promptText` reads the *previous* zone's `InteractableTrigger` against the
  new zone, and a stale slot-map reference is not alive there, so it resolves to `ValueNone` and the frame shows
  no prompt rather than a wrong one.
- **Alternatives considered**: advancing first (a cue's camera move would then be overwritten by `cameraFollow` in
  the same frame, and a zone switch would happen after the frame's input had already been read against the outgoing
  zone); advancing in `world/Systems.fs` as a named system — the module is compiled before `state/`, so it cannot
  apply the effects, and the cue is not a zone system at all.

## Decision 6 — The two disciplines the frame rate depends on

- **Decision**: a step that changes nothing reports **nothing to change** (`ValueNone`), and the op in flight is
  held *out* of a sequence's deque (`Sequence(current, rest)`, never `Sequence of Cue FDeque`).
- **Rationale**: both were measured, and both are invisible in the types.
  - **Reporting nothing to change** replaces the first sketch's `Outcome.Unchanged` without a bespoke wrapper: the
    answer is a `Cue voption` — `ValueSome cue` is the cue as it now stands (which may be `Fin`, a *value* rather
    than an absence), `ValueNone` is "still working on what you were given, keep it". A parent told nothing moved
    keeps what it had rather than rebuilding. `ValueOption` is a struct, so the idle path is 0 bytes all the same —
    the suite measures it, flat and several sequences deep.
  - **The in-flight op out of the deque** is the measured correction in Decision 2: with it in the deque, stepping
    the front means `FDeque.uncons` on the idle path, and `uncons` allocates the tail — **72 bytes per tick per
    level**, every frame. With `current` held out, the idle path touches no deque at all.
- **Alternatives considered**: an `Outcome` type (the first sketch's four-case wrapper — replaced by a `Cue
  voption`, which carries the same distinction, "nothing moved" against "here is the cue", with no bespoke type,
  since `Fin` already *is* the ended value); **reference identity** (`physEq` / `ReferenceEquals`, as an earlier
  draft had it — rejected: it makes "nothing moved" an invariant every caller must know, rather than a fact the
  answer carries); the front in the deque (measured, above); `ValueOption.bind`-style lifts (`tick` reads and
  writes one field).

## Decision 7 — What the headless suite can assert, and what it cannot

- **Decision**: `test/CueTests.fs` asserts the three stories against cues and the state they changed: a sequence,
  a branch and a `Fork` step to a scripted clock and shrink to `Fin`; `forkInto` runs beside the running cue and
  leaves its deadline untouched; the bounded-footprint property (peak live nodes constant across a 4-step and a
  40-step cue); the two allocation properties of FR-012 (0 bytes for an idle tick and for `Fin`, a bounded amount
  per completed op) measured with `GC.GetAllocatedBytesForCurrentThread`;
  determinism; and — the half that needs a world — that a zone switch really switched, an exposition really wrote
  `Dialogue`, and a camera move really moved the camera, against the state `test/GameplayStateTests.fs` already
  builds headlessly (Decision 3).
- **Rationale**: this is the feature's own requirement (FR-016), and every part of it is reachable: a cue's *walk*
  is asserted through the value it is (`advance`), and its *effects* through the state it changed — a stronger
  assertion than asserting a request for them.
- **Alternatives considered**: asserting only requests (needs an `Effects` type; says what was asked for rather
  than what happened); a walkthrough-only feature (the project's tests are its dev loop, and a cue engine that
  cannot be exercised in them would be authored blind).

## Decision 8 — The documents that move with the feature

- **Decision**: amend `docs/ARCHITECTURE.md` (the state diagram's cue moves from `Shared_State` to the gameplay
  screen, and the "Scenario & Cue" section's retired `Cue`/`CueState`/`state_map` description is replaced by the
  cue that *is* its run, shrinking to `Fin`), `docs/EDITOR_DESIGN.md` (D-010 restated: no template/runtime split
  and no separate run; the cue's stack is its own, not a zone's) and `docs/ROADMAP.md` (phase 9's deliverables and
  its "done when" — the engine here, the editor next — and the next-step line). `docs/PROJECT_STRUCTURE.md`'s
  entries for `world/Cue.fs` and `world/CueEngine.fs` are corrected too, since they currently describe the retired
  arrangement.
- **Rationale**: each currently describes an arrangement this feature retires, and a spec that leaves them is a
  spec that has paid for two contradictory descriptions. The spec lists them under Assumptions, so this is the
  feature's work rather than an afterthought.
- **Alternatives considered**: amending them in a separate documentation feature (deferred promises rot, and these
  are three paragraphs, not a chapter); leaving `ARCHITECTURE.md` alone as a description of the *reference* —
  rejected for the state diagram specifically, which is the port's own arrangement and is what a reader consults.
