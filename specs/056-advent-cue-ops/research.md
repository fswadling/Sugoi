# Phase 0 Research: Advent Cue Ops

The flag set and the cue both already exist; this feature is the seam between them. Most of the decisions are about
carrying the *closed* milestone and keeping the walk's shape.

## Decision 1 — The events carry the closed `Advent` union, not a string

- **Decision**: `AddAdvent`, `RemoveAdvent`, `Await` and `If` each carry an `Advent` value (`world/Advent.fs`).
  `world/Cue.fs` `open`s `Advent`.
- **Rationale**: `specs/055` made milestones a closed union precisely so one cannot be misspelled and matching on one
  is exhaustive; a string name would give that up at the one place it matters most (authored data). `Advent.fs` is
  pure (names no zone, screen, asset or device) and is compiled before `Cue.fs`, so the dependency keeps the
  vocabulary loadable on its own; it only widens `Cue.fs`'s note from "`Prime` + `System.Numerics`" to include one
  more pure module.
- **Alternatives considered**: **a string name resolved at runtime** (the reference's shape) — rejected: it discards
  spec 055's whole rationale. **An integer id** — rejected: indistinguishable from a string in kind and worse to read.

## Decision 2 — `AddAdvent`/`RemoveAdvent` effect the set where reached, and are idempotent

- **Decision**: `advance (AddAdvent m)` sets `shared.Advents <- Set.add m shared.Advents` and reports `ValueSome Fin`;
  `RemoveAdvent` is `Set.remove`. Adding an already-set flag, or removing one that is absent, is a no-op.
- **Rationale**: the flags are `SharedState`'s (spec 055 FR-002), which the walk already carries as it carries
  `shared` for `SwitchZone`; the effect is one line, with no state record, exactly the no-progress shape (`specs/046`
  FR-007). Idempotence is the expected state, not a bug: a cue may set a flag another path already set, and failing
  there would make composition fragile (Constitution II applies to *unexpected* state).
- **Alternatives considered**: **a `Set` step on the undo history** — rejected: the cue acts at *runtime*, and undo
  acts on the *authored* cue, not on what a run did (`specs/046`, invariant 3); the authored edit that sets a flag is
  the panel's `Advent` step, unchanged. **Raising on a duplicate add** — rejected: an expected composition case.

## Decision 3 — `Await` is a condition, and `AwaitState` is a begun-marker

- **Decision**: `Await m` completes exactly when `m` is in `shared.Advents`; otherwise it becomes `AwaitState m` and
  stays there, re-checked each step. `AwaitState` carries the same `Advent` as `Await`.
- **Rationale**: an await has no computed number — the condition is the milestone — so it needs no state for its
  *progress*. It needs one for its *identity*: the panel decides read-only-ness from the value alone (`CueTree.executing`),
  and a blocking op whose authored and in-flight forms were identical would let an author edit the op a cue is
  currently stuck on. `AwaitState` marks "begun", and it is the one deliberate exception to `specs/046`'s rule that a
  `…State` case appears where the run holds a *number* the authored form does not; it is recorded so the exception is
  visible rather than a quiet contradiction. The reference's `Cue_Op_Await`/`Cue_Await_State` is the same pair.
- **Alternatives considered**: **no state case, re-check `Await m` forever** — rejected: the panel cannot tell a
  running await from a queued one, so the running op would be editable. **A boolean "begun" flag** — rejected: that is
  a second mechanism for what one case already says.
- **Idle cost**: the in-flight step reports `ValueNone` and writes nothing, so it allocates nothing — the same
  measurement the rest of the engine is held to (`specs/046` FR-012).

## Decision 4 — `If` runs its chosen branch in its own place

- **Decision**: `advance (If (m, thenCue, elseCue))` advances the branch `m` selects, in the `If`'s place; that
  branch is what runs and what the caller then holds.
- **Rationale**: the flag is read at *reach time*, which is when the choice is actually made; the branch value is the
  run's remainder, so nothing extra has to be tracked. A branch that is `Fin` is the over-before-it-begins case (the
  `If` costs no time); a timed branch begins in the `If`'s place, exactly as a `Sequence`'s front is pulled forward.
  It is the same "a cue is a value" move the walk makes elsewhere, so it needs no `IfState`.
- **Alternatives considered**: **an `IfState` holding the chosen branch** — rejected: it would be a state case that
  carries nothing the value does not, and the branch can simply *be* the value. **Keeping the `If` and advancing a
  child in place** — rejected: the untaken branch would linger in the run and show in the panel as if it might still
  run, which is false.

## Decision 5 — The panel's chooser is a closed list in the control; `If` children default to `Fin`

- **Decision**: `editor/CueTree.fs` gains the four kinds and, in `drawEditor`, a milestone chooser over `Advent.all`
  committed through `withAt`. An `If` is drawn with its two branches as indented child rows, each headed by a
  `then`/`else` label row (part of the pure `rows` derivation) so the two are told apart — the label carries no node
  and is not selectable. The kind defaults to `If (head Advent.all) Fin Fin`, edited by selecting and re-kinding a child
  — as a `Parallel`'s or `Fork`'s children already are. `textOf`/`withText` are unchanged: none of the four carries
  text.
- **Rationale**: a milestone is a closed list, so the control can offer it without reading anything (no zone, no
  registry) — unlike a zone or waypoint name, which is why *those* are typed or panel-supplied (`specs/047` FR-006).
  Defaulting the branches to `Fin` and re-kinding the child row reuses the existing child-editing model, so no new
  "add child" affordance is invented.
- **Alternatives considered**: **the panel supplies the chooser** (as it does the waypoint combo) — rejected: the
  control needs no game state for a closed union, so the chooser belongs with the vocabulary. **A free-text milestone
  field** — rejected by spec 055 and SC-006.
