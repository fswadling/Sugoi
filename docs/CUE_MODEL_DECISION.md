# Cue model — immutable cue, pure run

**Status**: **superseded as the design; kept for the measurements and the route.** This note was written for the
first model — an immutable authored cue kept beside a run of frames reached from a `Frame voption` root. That was
then simplified once more: **the cue *is* its run** — one type, shrinking to `Fin`, forked rather than spliced.
Read it for what was measured and why each earlier arrangement was dropped (the state array it retired, the
`value option` finding, the size of a frame against an op), not as the current design. The runnable evidence is
now `test/CueTests.fs` — the suite that asserts the numbers below — and `specs/046-cue-core` is the
specification.
**Evidence**: `test/CueTests.fs` (spec 046) asserts the properties these numbers describe: 0 bytes per idle tick
and a bounded per-op cost. They were first measured by a scratch script, `cue_machine_sketch.fsx`, which has
since been removed — the numbers it produced are the ones below, and the suite is what keeps them honest.

## What this decides

A cue is authored data that the game executes. This note fixes five things:

1. **The authored cue is an immutable value, and a run is a value derived from it by a pure step function.**
   Nothing in this model is mutated anywhere: the program is what you author, the run is what folding `tick`
   over it produces.
2. **A sequence's run state is its remainder** — the children it has not started yet — never an index into the
   authored children. This is what makes a subcue a `Cue` in its own right. Children are a deque, so the
   remainder can be taken from the front and added at either end.
3. **The run is a tree of frames reached from a single root that may be absent.** No state array, no capacity,
   no handle, no generation, and nothing to own.
4. **The cue, and its run, belong to the gameplay screen** — not to a zone, and not to `SharedState`.
5. **Cue edits are undoable in their own history**, which never touches the run. (The editor's feature; this
   note keeps the decision and the scope question.)

## The constraints that drive it

- **The whole game can live inside one running cue.** A cue runs for as long as the game does: triggers and
  scenarios splice new subtrees into the running cue at runtime. So the run's bound must be *how many nodes are
  live at once*, never how many steps have been run, and never the size of the authored tree.
- **A subcue is a cue.** A cue a trigger splices in never knew the cue it was spliced into, and a subcue lifted
  out of its parent is meaningful on its own. Splicing is the same operation at either end — a cue put in to run
  *next* goes on the front, one deferred behind what is already queued goes on the back — so the children
  collection has to be cheap at both. The editor's view follows from the same rule: *the running cue in its
  current state, the running op at the top, finished ops gone.* Both are satisfied by one restriction —
  **progress is always expressed in cue values, never as a position in someone else's structure.**
- **A cue spans zones.** It can switch zones, so it cannot belong to a zone and must outlive a zone switch.
- **Per-frame cost is the hot path — but that is a claim to measure, not to assume.** Two measurements shaped
  this design, and both retired an earlier arrangement: a state array bought nothing on the frame path (*Why no
  state array*), and a pure run costs nothing there either (*Why the run is a value*). `specs/046-cue-core`
  makes the second claim a success criterion.

## The program — immutable, authored

The editor's subject, the thing persisted, the thing a trigger or scenario holds, and the thing a run never
modifies. (The sketch uses a reduced op set; the real one is the cue op family, whose first cut
`specs/046-cue-core` fixes by *shape*.)

```fsharp
type Cue =
    | Sequence of children: Cue FDeque   // Prime's double-ended queue
    | Parallel of Cue * Cue
    | Fork of Cue            // "run beside the rest of my sequence"
    | Wait of seconds: float
    | Say of text: string    // stands in for the instant effects
```

**Why a deque.** The model needs four operations, and they sit at opposite ends: the run takes the front (pop
as a child finishes) and puts to the front (a cue spliced in to run next), and a trigger may put to the back
(a cue deferred behind what is already queued). An array can do none of them without copying, and — the point
at issue — an *index* into an array cannot express a remainder at all. A list does the front pair for free
(`Tail` is structure-sharing, no allocation), but its back-append is O(children), and that is exactly the case
a trigger fires into.

`FDeque` is Prime's double-ended queue — the structure `My Game 3` used — and its operation set is the four
above plus the mirror image: `cons`/`head`/`tail` at the front, `conj`/`last`/`initial` at the back, with
`append`, `ofCatLists`, `rev` (O(1)) and `Cons`/`Nil` active patterns. Prime is **already a live dependency** of
this project (`world/assets/Assets.fs`, `world/assets/ActorAsset.fs`, `test/DialogueTests.fs` all `open Prime`) and targets
netstandard2.1, so this adds no package.

What it does not have is indexed access — no `item`, `insertAt`, `removeAt`. The editor therefore reads children
with `toSeq` and rebuilds with `ofList` on an edit: O(children) per edit, which is fine because editing is not
the hot path (see *Known wrinkles*).

Because the program is immutable, it is safe to share — between the editor and a run, between a trigger and the
library it came from — and the same value can be run again from the start.

## The run — a value

```fsharp
/// What advancing a node did. This is what replaces mutation: a node *reports* what happened, and a parent
/// that is told `Unchanged` returns itself — the same reference — and rebuilds nothing.
[<Struct>]
type Outcome =
    | Ended                      // the node finished; its parent drops it
    | Unchanged                  // still running, exactly as it was
    | Updated of frame: Frame    // still running, and its state moved

/// A running node. A leaf carries the op's own numbers rather than the op itself, so a frame cannot hold a
/// cue shape that could not have produced it — `Waiting` can only have come from a `Wait`. (The real op
/// family has many time-based ops, each with its own state case.) A composite *is* its children: the ones not
/// started yet, and the one running now.
///
/// A reference union, not a struct one, because it holds `Frame voption`s of itself.
type Frame =
    | Waiting of seconds: float * endsAt: float
    | Sequencing of remaining: Cue FDeque * child: Frame voption
    | Branching of left: Frame voption * right: Frame voption
```

A run is a `Frame voption`. There is no wrapper type: `Machine` was only ever a name for a mutable root, and
there is no root to mutate. (If the specs want a noun, `type Run = Frame voption` costs nothing.)

Five things about this shape are load-bearing:

- **`Sequencing` carries a remainder, not a cursor.** `remaining` is the children that have *not started*;
  `child` is the one running. When the child finishes, the next runnable one is the head of `remaining` — the
  finished child is gone, not stepped over. A cue spliced in to run next is `FDeque.cons cue remaining`; one
  deferred behind the rest is `FDeque.conj cue remaining`. So the run's state *is* what the editor should show,
  and a subcue is a value the run can hand to anyone.
- **A parent holds a reference to its running child.** That reference *is* the run's node identity — exact,
  free, and impossible to alias onto a different node. A node that finishes is dropped by its parent; the
  collector does the rest.
- **`advance` reports an `Outcome` instead of writing into the child.** A parent told `Unchanged` returns
  *itself*, so an unchanged tick rebuilds nothing — at any depth, however many frames the walk touches. This is
  the property that makes a pure run free on the tick path.
- **A leaf carries the op's numbers, not the op.** `Waiting` can only have come from a `Wait`, so it holds a
  duration and a deadline and nothing that could disagree with them. The real family generalises this to one
  state case per time-based op.
- **Child links are `voption`, not `option`.** `Some` of a reference allocates a 24-byte wrapper and `ValueSome`
  is a struct that does not. Purity demotes this from a per-tick cost to a per-completed-op one (see *Why the
  run is a value*), but it is still free to get right.

### Why no state array

The first design kept frames in a generational slot map (`core/SlotMap.fs`, the same one entities use), with the
state holding `Handle voption`s. The second made frames referenced records with a mutable state field. Both were
built and measured against what is now the design:

| | slot-mapped frames | referenced frames, mutated | **the run as a value** |
|---|---|---|---|
| tick with a child in flight | 0 bytes | 0 bytes | **0 bytes** (and 0 at five frames deep) |
| per completed op | 72 | 168 | **144** (184 one level deeper) |
| capacity | fixed 64, loud failure | none | none |
| mutation | a frame field, driven by a machine | a frame field, driven by a machine | **none** |
| node identity | a generational handle | the reference | the reference |
| `create ()` | a 64-slot array allocated up front | `{ Root = ValueNone }` | nothing — a run is a value |
| engine signature | `Machine -> now -> Cue -> Handle voption` | `Machine -> now -> Frame voption` | `now -> run -> run` |

So the array bought **nothing at all** on the tick path, which is where it was supposed to pay — both forms are
0 bytes per frame. It *is* cheaper per completed op (72 against 144), and that was accepted rather than
overlooked: per-op allocation at human timescales is nothing, while a fixed capacity that fails loudly is a
hazard in a shipped game, and the handle and generation it needs for every reference are a concept this design
does not otherwise carry. Two arguments that look like they favour it do not:

- **Cache locality.** A flat scan of state records is what wants contiguous storage. The walk is a tree
  descent: root → child → grandchild, touching the same handful of frames every tick, with no scan anywhere.
  There is no locality to recover, and the array adds an indirection *on top of* the ones the walk already does.
- **Stale-handle detection.** The map's generations catch a reference to a slot that has been freed and reused.
  With references that failure mode does not exist: a frame reference cannot resolve to a different frame, and
  identity is exact without machinery.

The honest steelman: if frame allocation ever matters, a free list of frames *is* that array, so the array is a
local optimisation rather than the model — and it can be added later behind the same `start`/`tick` API. Nothing
has asked for it, so the simpler shape is the default.

`core/SlotMap.fs` is untouched by this and stays where it is: entities and zones genuinely want a scan and
genuinely want `Occupy` for undoing a deletion.

### Why the run is a value

Every write in the mutated version was a parent updating its link to a child. An `Outcome` says the same thing
without a write, which is what lets the whole run be a value:

| | mutated frames | the run as a value |
|---|---|---|
| tick with a child in flight (flat, and five frames deep) | 0 bytes | **0 bytes** |
| per completed op, flat | 168 | **144** |
| per completed op, one level of nesting deeper | — | 184 |
| a run can be held, stepped or snapshotted | no | **yes** |

The costs, stated plainly:

- **~40 bytes per completed op per level of nesting** on the changed path — the spine is rebuilt from the
  changed node up to the root. Flat, 144; one level deeper, 184. This lands once per completed op, never per
  frame, and it is proportional to *nesting depth*, not to the size of the cue.
- **The `Outcome` discipline.** An edit that forgets to report `Unchanged` puts the rebuild — and so the
  allocation — back on the tick path. That is the inverted form of the mutated version's "forgot to write the
  frame back" hazard, and it is equally invisible in the types. This one at least shows up as allocation rather
  than as stale state the panel displays.

What it buys:

- **No mutation anywhere**, and no container: `tick` is a value transform, and the host assigns the result.
- **A run can be held and stepped without committing.** The editor can advance a *copy* to preview the next op
  and the original run is untouched — the sketch prints this, and splicing too.
- **A run snapshot is a pointer**, sharing structure with its neighbours, so holding one is free.
- **The engine is testable as a pure function** — a value in, a value out, no device and no marshalling. That
  matters here: the test project cannot open a GPU, and `specs/046-cue-core` makes headless exercise a
  requirement.

**The lifting trap.** `tick` acts on a frame, so the caller lifts it to the run — and how it does so costs more
than it looks:

```fsharp
run <- match run with ValueSome frame -> tick now frame | ValueNone -> ValueNone   // 0 bytes per tick
run <- run |> ValueOption.bind (tick now)                                          // 24 bytes per tick
```

The second is the line that reads best and it allocates a closure over `now` on **every** tick — measured in
the sketch, and missed by a micro-benchmark whose small stand-in `tick` the JIT inlined away. So the lift is a
match wherever it runs per tick: the play chain and the sketch's drivers.

### Execution

```fsharp
val start       : now: float -> Cue -> Frame voption        // begin a cue; none if it is instantly fin
val advance     : now: float -> Frame -> Outcome            // internal: reports what moved
val tick        : now: float -> Frame -> Frame voption      // the frame it is now, or none when it ends
val spliceNext  : Cue -> Frame -> Frame                     // run this cue next
val spliceLater : Cue -> Frame -> Frame                     // run this cue after what is queued
```

- **Instant steps get no frame.** They are performed where they are reached, so "finished" and "never had state"
  are the same to the caller. A whole sequence of instant steps is consumed in one walk, and the walk is
  recursive with both self-calls in tail position, so F# compiles it to a loop: a cue with 200,000 instant
  children in a row is consumed by one `start` without growing the stack.
- **Nothing frees anything.** A node that finishes is dropped by its parent in the same step, and the frames
  under it become unreachable. The root is the only root, so nothing unreachable can leak.
- **A run is a value.** `tick` returns the next run and leaves the one it was given intact, so a copy can be
  stepped for a preview; and a tick that changes nothing returns the *same run*, which is why it allocates
  nothing.
- **`tick` and the splices take a frame, not a run.** They act on a node, so what "nothing was running" means is
  the caller's business — the play chain lifts with a match. For the splices this is what removes a raise: a
  trigger firing with nothing running should decide whether to start the cue or ignore it, and that decision
  belongs to the trigger system rather than to a pure spine-rebuilding function.
- **A `Fork` is handled by its sequence, not by the program.** When the walk reaches one, the sequence's frame
  keeps the remainder as the left branch and the branch runs beside it in a new `Branching` frame. The
  **authored tree is never rewritten** — only the remainder moves. A `Fork` anywhere else is meaningless and
  raises.
- **A splice rebuilds the left spine** of the run down to the sequence at the top of the walk, with the cue
  consed or snoced onto its remainder. The spliced cue is an ordinary value that never learnt anything about its
  host, which is the whole reason a trigger can supply one at all.

### The live view

```fsharp
val live    : Frame -> Cue                  // the running cue as it now stands
val current : Frame voption -> Cue option   // what the panel shows; none is cue_end
```

`live` reads a frame back out **as a cue**: a `Wait` reports itself; a sequence reports
`Sequence (FDeque.cons (live child) remaining)` — **the running op first, the remainder behind it, the ops
already run simply absent**; a `Branching` frame reports a `Parallel` of its live sides. The result is a genuine
`Cue` value, so it can be described, edited, spliced back in, or run again elsewhere. It is what makes the
editor's panel a *derivation* rather than a second thing to keep in step — the subject of
`specs/046-cue-core`'s fourth story.

Because execution and display read the same value there is no cursor to keep in sync; the flip side is that a
node left in the run after it has finished is a view that lies, which is why a finished node is reported `Ended`
rather than parked. `live` rebuilds the remainder, so it allocates — editor-path only, never in `tick`.

## Where it lives

**On `GameplayState`, beside the authored cue** — the screen's, not a zone's and not `SharedState`'s:

```fsharp
mutable Cue : Cue option          // the authored cue: the editor's subject, undoable
mutable Run : Frame voption       // the run: a value; not undoable, never persisted
```

A cue spans zones, so it cannot be zone state. It also ends when the screen does, so it is not cross-screen
state either. This **contradicts `docs/ARCHITECTURE.md`**, whose diagram puts `Cue` under `Shared_State`
(`SS --> C[Cue]`) and whose "Scenario & Cue" section describes the retired `state_map`/`CueState` arrangement;
`specs/046-cue-core` carries those amendments, together with the matching ones in `docs/EDITOR_DESIGN.md` (D-010)
and `docs/ROADMAP.md` phase 9.

The run advances only while playing — the play chain in `screen/GameplayScreen.fs` is already gated on
`not state.Editing`, and the lift above is the line it adds.

### Pausing is the editor — there is no Stop control

Play mode is the only time a run advances, and the editor's panels are **not drawn while playing**
(`Editor.draw` returns early unless `state.Editing`). So a cue is frozen for exactly as long as the editor is on
screen — the run value survives the toggle, so pressing Play again without touching anything **resumes** the run
where it was. That is free rather than a feature, and it means the live view is only ever seen frozen. A run
therefore ends by finishing, or by the authored cue being replaced (invariant 2) — never by a button.

### What the panel shows, and what editing it means

The panel shows `current state.Run` — the **live** cue: while a run is in flight, its current state with the
running op at the top; with nothing running, the stored cue. There is no "authored tree with a mark on it":
what the author sees is what is left to run.

Because the display *is* the run, an edit to a mid-run cue is not a change to some parallel authored copy — it
is a decision about the run, and the options are laid out under *Open questions*. Both that question and the one
about what the panel shows when a run ends are the **editor feature's** to answer; `specs/046-cue-core` defers
them deliberately rather than settling them by accident.

## Undo/redo — one history per ownership scope

The cue is not a zone's, so it cannot ride `UndoByZone` (whose stacks are per zone and swapped on a switch). It
gets **its own history, beside it on the screen**: a second stack with a second scope, not a second undo
mechanism. `docs/EDITOR_DESIGN.md` (D-011) already anticipates a stack for the cue root; the correction is the
scope — **the screen's**, not the project's, since the cue does not outlive the screen.

The command carries **the whole tree, before and after**, exactly as `SetEnvironment`/`SetLight`/`SetPlayer` do:

```fsharp
type CueEdit = SetCue of Before : Cue option * After : Cue option
```

Nothing in the payload references a node — no ids, no paths, no indexes — so an undo cannot act "on a cue op" at
all; it swaps a root. That the payload is a `Cue` value (rather than a path into one) is what lets it carry a
*materialised live cue* just as happily as an authored one. Two pieces of plumbing are needed first:

- **`UndoStack` must be generalised.** Today it is hard-wired: `push : Command -> UndoStack`, and
  `undo : UndoStack -> Zone -> Physics.World -> Assets.AssetManager -> UndoStack` can only invert a `Command` in
  the presence of a zone. It becomes `UndoStack<'T>` with the direction supplied by the domain
  (`Command.undo zone physics assets` for scene edits).
- **`Command.apply` cannot see `GameplayState`** — it takes `(zone, physics, assets)` deliberately, to avoid a
  `Command → GameplayState → EditorState → Command` module cycle (D-011). So a cue edit is its own small type
  with its own one-line apply/invert, compiled after `GameplayState`; it does not join `Command`.

**Ctrl+Z target**: the focused scope, with no fall-through. If the Cue panel has focus it acts on the cue's
history; otherwise on the active zone's. The Edit menu names the action ("Undo: delete node"), and the Cue panel
carries its own ↶ ↷ so the shortcut is not the only way in. The cost, stated plainly: undo is **not one global
chronology** across scopes. That is accepted, because a merged history would break "an undo applies in the zone
it was made in".

The same rule then serves **advents and the scenario library** (phases 10–11; the library became a string-keyed
ordered map in `specs/083-scenario-strings`), which are non-zone-scoped for the
same reason.

## Invariants

1. **Undo and redo act on the authored cue only.** A run is a value, so a stack *could* hold one safely — but no
   command does, and no cue edit restores a run.
2. **Replacing the authored cue releases the run** — an edit, an undo, or a load. A structural change
   invalidates the run's position, so nothing ever has to reason about a live execution.
3. **Undoing a cue edit does not rewind what the cue already did**, nor does it put the run back: actor
   positions, a zone switch, a spawned actor, and the run's own position are not the undo's business.
4. **Each scope's history is its own**, and the shortcut never crosses scopes.
5. **A run is pinned to the values it started from.** A remainder holds cue values and a leaf holds its own
   numbers, so an edit cannot corrupt a live run — it is released instead (invariant 2). A run may also hold
   values from a trigger or the scenario library, which the stored cue need not mention.

## Evidence

Measured by a scratch script (`cue_machine_sketch.fsx`, since removed). The engine's refactors were checked by
diffing its whole output before and after — which is how a driver unification was caught changing how many ticks
the measuring loops ran rather than changing the engine. The suite now asserts the properties themselves
(`test/CueTests.fs`), so a refactor is checked by `dotnet test` rather than by a diff. The run reported its live
cue every tick — running op first, finished ops gone:

```
t=1    Seq[Wait 2, Say(hello), Wait 1]
       · hello
t=2    Seq[Wait 1]
t=3    cue_end
```

A trigger splicing a cue into a running sequence, and both ends at once:

```
before splice:  Seq[Wait 2, Wait 2, Wait 2]
after splice:   Seq[Wait 2, Seq[Say(spliced), Wait 1], Wait 2, Wait 2]

spliceNext (Say "next") + spliceLater (Say "later")  ->  Seq[Wait 2, Say(next), Wait 2, Say(later)]
```

A run is a value, so a copy can be stepped without committing — the original still reports its own state:

```
original (untouched): Seq[Wait 2, Say(second)]
stepped copy:         Seq[Wait 2, Say(second)]
```

The frames one tick into the fork case — the detach done on the *run*, with the program untouched:

```
Par(Seq[Wait 1], Wait 3)                 left yes, right yes
  Seq[Wait 1]                              remaining: 1, child none
  Wait 3                                   waiting: ends at t=4
live: Par(Seq[Wait 1], Wait 3)
```

Peak live frames, and what a run costs:

| Case | Peak | Allocated |
|---|---|---|
| `Seq[Wait 2, Say, Wait 1]` (the instant `Say` takes no frame) | 2 | |
| `Par(Wait 2, Wait 3)` | 3 | |
| `Seq[Wait 1, Fork(Wait 3), Wait 1]` | 4 | |
| 2000 ticks with a child in flight (flat) | 2 | **0 bytes — 0 per tick** |
| 2000 ticks with a child in flight, five frames deep | 5 | **0 bytes — 0 per tick** |
| 2000 ticks, each completing a child | | 288,000 bytes — **144 per op** |
| the same, one level of nesting deeper | | 368,000 bytes — 184 per op |
| 200 walks consuming 1000 instant children each | | 72,144 per walk — **72 per instant child** |
| a cue with 200,000 instant children in a row | | consumed by one `start`; stack unchanged |
| **40 sequential waits, one run** | **2** | |
| **three runs of a 5-step cue** | **2** | |

Two smaller measurements that settled details:

| Question | Answer |
|---|---|
| `Some` of a reference vs `ValueSome` | 24 bytes per `Some`, 0 per `ValueSome`; only `None` is free (it compiles to null) |
| the same with `option` instead of `voption` in the links | 288 per op rather than 144 — a *per-op* cost now, not per tick |
| `run \|> ValueOption.bind (tick now)` vs a match, per tick | 24 bytes vs 0 |

Every number here was reproduced in a compiled optimized build (`dotnet run -c Release`) as well as in FSI,
because `option`-of-a-reference and JIT inlining are both things the interactive host can mislead about.

## Lineage, and what is not inherited

The shape was derived from the constraints above; the reference was then checked against it, not the other way
round. Where they agree and where they differ:

- **`Nu/Projects/My Game 3` — the cue value is the run state.** `Sequence of Cue FDeque` holding the
  *remainder*, rewritten in place each step, with the editor rendering the live value. Its notion of progress,
  its structure, and its `Sequence (child :: remaining)` are this note's — the deque included, which an earlier
  draft dropped for a list and has since taken back. What it did not have is a program separate from the run:
  the value the editor rendered *was* the value being consumed, so a cue could not re-run, share, undo or
  persist. This port kept the remainder semantics and, for a while, added an authored cue beside the run; it has
  since taken My Game 3's model all the way — the cue *is* the run (see the Status note above).
- **`odin-game` (current) — immutable template + slot-mapped state.** The same program/run split this note
  lands on, and the source of the reuse requirement. Differing here: frames are referenced records rather than
  state slots, they carry their own numbers rather than naming a node in a template (so no index juggling, no
  `slot_index_from_ptr`, no `Fin` sentinel), the sequence's state is a remainder of cues rather than a cursor
  into a children array, the fork detach rewrites the run rather than returning scenario pointers, and the step
  is pure where the reference's mutates state in place.
- **The reverted first attempt at `specs/046-cue-core`.** Its storage was a slot map whose state was larger than
  it needed to be: a `Sequence` frame carried the template's children array *and* a cursor. The representation
  that replaced it is a remainder of cues in a frame referenced by its parent — and the frame now carries only
  what its own case needs.

## Known wrinkles

- **The `Outcome` discipline.** Reporting `Updated` when nothing moved puts a rebuild back on every tick. Shows
  up as allocation, which is how it was caught.
- **The lift at a per-tick call site is a match, not `ValueOption.bind`.** The bind reads better and costs 24
  bytes per tick (see *Why the run is a value*).
- **The child links are `voption` for a reason that is invisible in the types.** `option` of a reference
  allocates a 24-byte wrapper per `Some`; with a pure run that is a per-completed-op cost rather than a per-tick
  one, but it is 100% of the per-op difference.
- **A finished node must be reported `Ended`, not parked.** With references there is no "stale slot" to catch a
  parent that keeps a completed child — it simply stays alive and shows up in the live view.
- **A rebuild is proportional to nesting depth**, not to the cue's size: ~40 bytes per level per changed op.
- **A detach defers the remainder by one frame.** After the fork is detached, the sequence's next child starts
  on the *following* tick (the sketch shows `Wait 1` alone at t=2, not t=1). The reference behaves the same way.
  `specs/046-cue-core` deliberately leaves this open rather than making it a requirement.
- **A spliced cue is not in the stored cue.** The remainder is authoritative for a live frame, so a frame and
  the authored node it came from diverge as soon as anything is spliced. By design, but it means the stored cue
  and the run are not two views of one tree.
- **`live` allocates** (it rebuilds the remainder), so it is editor-path only — never call it from `tick`.
- **`FDeque.length` and `toSeq` are O(children)`, and `FDeque` has no indexed access**, so the panel's counts
  and the editor's *n*th-child edits each cost a walk. Neither belongs in `tick`.
- **`FDeque.uncons` raises on empty**; the walk guards with `isEmpty`. (`tryUncons` reads better but allocates an
  option on top of its tuple, and the walk is the one path where a per-child cost is real.)

## Open questions

1. **What happens when you edit an op that is running?** The display *is* the run, so an edit is a decision about
   live state, and the options are how much of the run survives it:

   | Option | What survives | Undo payload | Cost |
   |---|---|---|---|
   | **1. Take over** — commit the display as the cue, release the run | nothing; Play restarts the display from its top | `SetCue Before/After` | the running op restarts; the run's progress is folded into the cue |
   | **2. Scoped take-over** — freeze the spine to the running leaf, write edits back | everything but the node edited; a running op behind the edit keeps its timer | `(frame, whole remainder before, whole remainder after)` | a second payload kind; restoring a held run rewinds the run |
   | **3. Reconciled commit** — commit the whole cue, re-seat by diffing | every frame whose node is structurally `=` | `SetCue Before/After` | identity by equality is ambiguous for duplicate ops |
   | **4. Document subject** — panel edits the stored cue, run shown read-only | nothing (invariant 2, unchanged) | `SetCue Before/After` | you cannot edit what is running; two things on screen |

   Options 2 and 4 bracket the problem: 2 is `My Game 3`'s behaviour (there the cue *was* the run, so editing it
   in place restarted nothing — but it had no undo, which is exactly what makes it cheap there and not here), and
   4 is the simplest but gives up the panel's purpose. 1 and 3 share one undo payload and differ only in how much
   of the run is reused, so 1 → 3 is a change to one function if the restart ever bites. Recommendation: **1 now,
   3 if needed, 2 only if editing behind a long running op becomes a real workflow.** A run being a value makes
   option 2's payload exact rather than a set of handles that can go stale — "is this frame still in the run" is
   a walk over two to five frames. **This is the editor feature's question**; `specs/046-cue-core` defers it.
2. **What does the panel show when a run finishes?** Under option 1 the panel drops to the run's end state, and
   the stored cue is whatever the author last took over with (or the library entry that was played). Is that the
   intended meaning of "no cue running appears as cue_end", or should a run started from the stored cue restore
   it on finishing? Entangled with question 1: it is really *is a cue a place or a document?* Also the editor's.
3. **Does the detach's one-frame deferral matter?** `specs/046-cue-core` left it unspecified on purpose, so it is
   the plan's to pin down.
4. **`live` ordering under a detach** — `Par(Seq[Wait 1], Wait 3)` reports the remainder's sequence before the
   detached branch. The spec's fourth story fixes only that both appear and in the order they run.

**Decided**: the run is a tree of referenced frames with no state array and no machine; the step is pure and the
run is a value; there is no Stop control — a run ends by finishing, or by the authored cue being replaced.

## Follow-ups

- **The editor feature** (`/speckit-specify` for the Cue panel): answers questions 1 and 2, and carries the
  cue's own undo history — the `UndoStack<'T>` generalisation and the cue edit command below.
- **`specs/046-cue-core`'s plan** pins down questions 3 and 4, and chooses the first cut of the op vocabulary.
- **Documentation the spec carries**: `docs/ARCHITECTURE.md` (the state diagram, and the "Scenario & Cue"
  section), `docs/EDITOR_DESIGN.md` (D-010, and D-011's scope correction), `docs/ROADMAP.md` (phase 9's
  deliverables and its "done when").
- **`docs/ROADMAP.md` phases 10–11** are where trigger wiring, advents and the scenario library land, and where
  this note's splicing becomes a system rather than an engine capability.
