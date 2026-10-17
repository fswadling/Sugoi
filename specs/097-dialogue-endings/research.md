# Phase 0 Research: Dialogue Endings

This feature answers a simple authoring question — "what may end this line?" — without turning the
cue vocabulary into a cross-product of "waiting" and "ending" modes. The decisions below settle the
representation, the cue shapes, the clock, the editor surfaces and the persistence boundary.

## Decision 1 — Two data-free runtime ending kinds in a `Set`, with the timeout as a `voption`

**Decision**: the line carries:

```fsharp
type Ending =
    | ByPlayer
    | ByTimeout

mutable Endings : Set<Ending>
mutable Timeout : float voption
```

The timeout's duration is **not** part of `ByTimeout`; it lives in `Dialogue.Timeout` and is
meaningful only when `ByTimeout` is in the set. The editor keeps the two in step
(`ByTimeout ∈ Endings ⇔ Timeout.IsSome`). Cue closing is not a member: a close or replacement always
works, and the empty set means the cue owns the line's end.

**Rationale**: the conditions are data-free, so F# structural equality/comparison is exactly right
and a real `Set<Ending>` gives every combination with no duplicates. The timeout stays real data
without making the set lose the value or lie about equality. The runtime check is `Set.contains` over
at most two elements plus a `voption` match, which allocates nothing.

**Alternatives considered**:

- `ByTimeout of seconds: float` in a `Set<Ending>` with custom equality ignoring the seconds:
  rejected. `Set.add` keeps the existing element, so retiming would not replace the duration, and
  `ByTimeout 2 = ByTimeout 4` would leak into dialogue and cue equality.
- `Ending list` with a data-carrying timeout: rejected. A real set is wanted now that the conditions
  are data-free, and the list would still hide the duration in the condition.
- A record `{ Player : bool; Timeout : float voption }`: rejected because the set of data-free kinds
  is the requested shape, is extensible without a new field, and reads the same in the editor and the
  label.

## Decision 2 — The cue closes implicitly; the set holds only runtime conditions

**Decision**: the cue's ability to close or replace a line is universal and is not recorded in the
ending set. The set holds `ByPlayer` and `ByTimeout` only. The empty set means the cue owns the line's
end: the line stays up until a close or replacement.

**Rationale**: a `ByCue` member would not gate anything — the engine already permits a close or
replacement regardless of the set (FR-002) — so it would be an authoring annotation, not a condition.
It would also create pairs of behaviourally identical sets (`{}`/`{ ByCue }`, `{ ByPlayer }`/
`{ ByCue; ByPlayer }`, and so on): eight states for four behaviours. And once cue closing is
universal, "no closer" is impossible, so the empty set genuinely means cue-managed rather than
"unclosable". The editor then offers two condition controls instead of three, and the label states
the runtime closers (`cue` for the empty set).

**Alternatives considered**: keep `ByCue` as a first-class member and use it to distinguish "intent"
from "the cue may still close this". Rejected: the distinction has no runtime consequence and a
non-waiting `{ ByCue }` line can still remain at `Fin`, so the annotation does not describe behaviour
either.

## Decision 3 — Keep `Exposit` two-argument; add `ExpositWith`, `ExpositWait` and `CloseExposition`

**Decision**: the cue vocabulary becomes:

```fsharp
| Exposit of text: string * kind: Dialogue.Kind
| ExpositWith of text: string * kind: Dialogue.Kind * endings: Set<Dialogue.Ending> * timeout: float voption
| ExpositWait of text: string * kind: Dialogue.Kind * endings: Set<Dialogue.Ending> * timeout: float voption
| ExpositWaitState of text: string * kind: Dialogue.Kind * endings: Set<Dialogue.Ending> * timeout: float voption
| CloseExposition
```

- `Exposit` is the existing non-waiting form and writes the empty set with `Timeout = ValueNone`.
- `ExpositWith` is the general non-waiting form; the editor normalises the empty set with
  `ValueNone` back to `Exposit` so the persisted text stays compact.
- `ExpositWait` writes the line and returns `ExpositWaitState`, the cue's run form.
- `CloseExposition` clears the line and costs no time.

**Rationale**: `Exposit` is carried in persisted cues, and its two-argument form is what
`specs/095-scenario-persistence` writes. Changing its arity would require a symbolic migration of
every persisted cue or a project-format version bump; keeping it means old projects keep decoding,
which is the spec's FR-016. The editor still presents one "Exposit" kind: `withEndings` promotes an
`Exposit` to `ExpositWith` when the set or timeout changes and demotes it when they return to the
empty set, `ValueNone`.

**Alternatives considered**:

- Change `Exposit` to carry the set and timeout and migrate `project.manifest`: fewer constructors,
  but it needs a symbolic tree migration and breaks any project file written before this feature.
- A single op with a `Waiting : bool` field: the op's control-flow shape would depend on data, and the
  cue tree's `executing`/label logic would have to inspect it. The project already models span-time
  leaves as their own ops with `…State` forms (`Wait`/`WaitState`, `Await`/`AwaitState`).
- Drop the general non-waiting form: loses timed or player-clearable ambient lines. The ending set is
  meant to be orthogonal to the waiting choice.

## Decision 4 — Waiting is a run state on the cue, not a field on the dialogue

**Decision**: `ExpositWait` writes the line and returns `ExpositWaitState`. Each tick of that state
asks only whether `state.Dialogue.Shown` is false; when it is, the state returns `Fin` and the
sequence continues in the same tick. The dialogue value has no "waiting" field. `ExpositWaitState`
carries the original text, kind, endings and timeout only so the cue tree can label the running op.

**Rationale**: waiting is a property of the cue's position, not of the line. The same line value can
be shown by a waiting or a non-waiting op. The shape matches `Wait`/`WaitState` and `Await`/`AwaitState`,
so the cue tree's `executing` predicate and the panel's lock rule need only a new case in the true/false
groups, not a data-dependent rule.

**Parallel observation**: the waiting state resumes when its own step observes `Shown = false`. If a
`Parallel` branch advanced after it produces the clear, the wait resumes on the next tick. That is still
within one play frame of the clear and avoids a second pass over the parallel; the spec's FR-009 and
SC-002 were written to this bound.

## Decision 5 — One dialogue tick: player edge plus timeout on `PlayTime`

**Decision**: replace `Systems.dismissDialogue` with:

```fsharp
val updateDialogue :
    pressed: bool -> textFocused: bool -> now: float ->
    byref<Dialogue.Dialogue> -> unit
```

It runs only while playing, exactly where the dismiss runs today. `pressed` is the `Space` edge;
`textFocused` is `ImGui.wantTextInput()`; `now` is play time. It computes `elapsed = now - ShownAt`
and closes when:

```fsharp
(ByPlayer ∈ Endings && pressed && not textFocused) ||
(ByTimeout ∈ Endings && Timeout = ValueSome seconds && elapsed >= seconds)
```

The cue's close or replacement is outside this tick; the empty set has no runtime closer.

**Rationale**: one clock for the line and the cue. `PlayTime` already freezes while the editor is
open, so a timeout pauses with play and resumes without a second timer. The timeout is measured from
`ShownAt`, which the cue and the panel both set, so a retime during display uses the original show
time (the clarified rule). `Set.contains` over two elements and a `voption` match allocate nothing.

**Alternatives considered**:

- A wall-clock timer: wrong when the editor is open or play is paused, and inconsistent with
  `WaitState`'s `endsAt`.
- Let the cue own the timeout in an `ExpositWaitState` only: a non-waiting timed line would not
  expire, and a waiting timed line would duplicate the `Wait` deadline.
- Tick the dialogue after the cue: a timeout and a dismiss would be observed one frame later. The
  chosen order gives the cue the same frame's close.

## Decision 6 — The editor shares one ending control and one summary rule

**Decision**: add `CueTree.endingControls : string -> Set<Dialogue.Ending> -> float voption ->
Set<Dialogue.Ending> * float voption`. It draws two checkboxes — Player can close, Closes after —
plus a seconds field, and returns the next set and timeout. `CueTree.drawEditor` commits it through
`withEndings`; `DialoguePanel.draw` assigns it directly. Both use the `Dialogue` helpers:

```fsharp
playerCloses, timed, withPlayer, withTimeout, summary
```

The editor defaults are:

- a new `Exposit` / `ExpositWith` node → the empty set, `ValueNone`;
- a new `ExpositWait` node → `{ ByPlayer }`, `ValueNone`;
- a hand-shown Dialogue panel line → `{ ByPlayer }`, `ValueNone`;
- a newly enabled timeout → `ByTimeout` in the set and `ValueSome 3.0`.

**Rationale**: the two authoring surfaces must agree on what "all unchecked" means (the empty set,
i.e. cue-managed) and on how the duration is preserved while toggling. A shared control in `CueTree`
follows the existing `filteredCombo`/`filteredNameCombo` precedent: `DialoguePanel` already calls into
`CueTree` for its speaker combo, so the module is the natural shared home and no new module is needed.

**Alternatives considered**: duplicate the checkboxes in `DialoguePanel` and `CueTree`. Rejected —
the rule would drift, and the shared control is the same size as the duplication.

## Decision 7 — Persist the new case forms, keep the file version unchanged

**Decision**: add a `ProjectTests` round-trip for a cue containing `Exposit` (old two-argument form),
`ExpositWith`, `ExpositWait`, `ExpositWaitState` and `CloseExposition`, with every set state and a
timeout. Change `project.manifest` only to add the final `CloseExposition` to the intro cutscene. Do not
bump `projectVersion` and do not write a migration.

**Rationale**: Prime's symbolic converter already maps unions, sets, options/voptions and lists
directly; the new cases are ordinary union cases, and the existing form is untouched. A version bump
would force a migration for no benefit, and FR-016 is satisfied by keeping the old form valid. The
round-trip test is the gate because `Set<Ending>` and `float voption` are new to the persisted cue.

**Alternatives considered**: a schema version bump with a symbolic tree rewriter. Rejected as
unnecessary machinery for a format whose old form remains valid.

## Decision 8 — The Scenario panel is not a cue editor

**Decision**: the shared cue control's surfaces are the Cue panel and the trigger's inline cue editor.
The Scenario panel only captures the screen's cue (`capture state.Zones state.Cue`) and forks a
stored cue; it has no cue tree of its own.

**Rationale**: this is what the code does (`editor/ScenarioPanel.fs` and `editor/CueEditor.fs`);
adding a second cue editor to the Scenario panel would be a new feature, not a panel update for this
one.

## Decision 9 — Clarified behaviours carried into the design

1. `ByPlayer` is `Space` only, edge-triggered and focus-gated.
2. Defaults: non-waiting empty set, waiting `{ ByPlayer }`, hand-shown `{ ByPlayer }`.
3. A timeout edited while shown retimes from the original show time.
4. A waiting cue keeps waiting when its line is replaced; it resumes when the screen is clear.
5. A waiting line with no runtime condition is allowed silently; no warning and no prevention.

## Decision 10 — Allocation discipline

**Decision**: the per-frame path is `Set.contains` over at most two elements plus a `voption`
match; it creates no closure, array, list or set. The ending set and timeout are allocated when a line
is written (a cue op or a panel edit), not per frame. `ExpositWaitState` is allocated once when the
op begins, like every other `…State`.

**Rationale**: the project measures 0 bytes/frame on idle cue paths (`specs/046` FR-012); this feature
must not regress that. `Set.contains` on a small persistent set does not allocate.

**Alternatives considered**: a LINQ/`Seq` check or a closure over `pressed`/`elapsed`; both are
shorter but can allocate per call. Use the direct `Set.contains` and `voption` tests.
