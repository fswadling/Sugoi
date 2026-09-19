# Phase 0 Research: Narrative Core

**Feature**: `025-narrative-core` | **Date**: 2026-09-16 | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

Decisions, each with the alternatives rejected and why.

**Provenance.** D1–D19 were decided while designing this engine in a prototype
(`narrative-core`, spec `001-interpretation-core`), where it was written in OCaml with
an *open* vocabulary. Two things changed on the way here — the language is now F# and
in-process, and the vocabulary is closed — so the decisions that rested on either are
marked **superseded** and replaced by D20–D26. The rest are carried, because their
reasoning depended on neither. Where "the earlier attempt" is cited, the reason we know
the answer is that it cost something.

---

## D1. Canonical text, hand-rolled, zero dependencies — carried

**Decision**: events, facts and their reasons are encoded as canonical text by hand.
Two structurally equal values always produce byte-identical text, and that text is the order a
collection is *shown* in, and the identity of a reason (research D33 keys the fact set by the
fact's own value).

**Rationale**: one choice satisfies the requirements that remain. FR-022 needs deterministic
output and ordering, so an ordering key must exist. FR-010 needs reasons compared for
identity, which is comparison on the key. FR-026 needs a stable report, which is the key
rendered. A hand-rolled encoder is small; a serialisation library is a dependency bought for
the same small amount, and the earlier attempt's experience is that the first dependency is
what makes the second one look reasonable.

*(A fourth requirement, FR-004's persistence, was part of this rationale until 2026-09-19 and is
withdrawn — see D32. The three above are unaffected: the key is still what facts are stored,
compared and reported by.)*

**Alternatives considered**:

- *`System.Text.Json`, Thoth, or another serialisation library.* Rejected: the text form
  exists to be stable and readable, not interoperable. Nothing else reads it.
- *A hash of the value as the ordering key.* Rejected by FR-022 outright — the key must
  be an ordering, not a bucket.
- *`compare` on the value.* Rejected: structural comparison is not stable across builds
  for a value with a string inside it in the way this needs, and it orders by declaration
  rather than by a form a reader can predict.

---

## D2. The engine's currency is a uniform predicate shape — superseded by D20

**Was**: the engine stored, compared, ordered and matched one uniform shape — a
predicate name plus a positional list of string arguments — with typed constructors at
the author's call site producing that shape.

**Why it is gone**: it existed to make the open vocabulary possible. A uniform shape has
no central list of constructs, so a unit could add one without editing the engine. With
the vocabulary closed the central list is the point, and F# reflection derives from the
type exactly what the shape would have had to carry (D21). Keeping both would be two
representations of the same thing.

**What survives from it**: the pattern stays uniform in a different sense — a rule
premise is always one construct plus its bound terms, whether it was written against an
event or a fact. That is what makes the two rule categories and the two closures alike.

---

## D3. A predicate is declared once, by the value that also provides its constructor and its pattern — superseded by D20

**Was**: one expression per predicate yielding both a constructor and a pattern
constructor, carrying name, arity, family and participant positions.

**Why it is gone**: there is nothing left to declare. A construct *is* a union case, so
the name, arity and field types come from the type (D21) and cannot drift from a rule
that matches on it. The prototype needed the declaration because the engine could not
see the type; here it can.

---

## D4. Nothing committed is ever decoded — reinstated by D32 after being superseded by D21

**Was**: persistence and matching worked on text and on the uniform shape, with no
per-family decoder, arity table or character-extraction function.

**Why it is gone**: FR-004 now requires that saved events deserialise, and
Clarification 5 requires that a history saved before a construct was added still
restores — so decoding exists. What does *not* come back is the earlier attempt's four
parallel tables, because the decoder is one generic function over the union driven by
reflection (D21). The prototype could avoid decoding only because its committed value
never had to become a typed value again; here a rule matches on the typed value, so it
does.

---

## D5. Facts live in an ordered map keyed by canonical text — superseded by D33

**Decision**: the fact set is a map from canonical text to a fact entry holding the
fact, its key and every reason supporting it.

**Rationale**: FR-009 (reported once however many reasons support it), FR-022
(identical ordering) and FR-010 (reason identity) all follow from the key. The key's
first component is the construct name, so a query for one construct — or for one
construct with a settled first argument — is a contiguous range, which is what makes a
join between two constructs a lookup rather than a nested scan. F# `Map` iterates in
key order, so determinism is the map's property rather than a discipline (D13).

**Alternatives considered**:

- *A `Dictionary` plus a sort at output time.* Rejected: two mechanisms where one will
  do, and it leaves ordering to be remembered rather than guaranteed.
- *A list with a linear duplicate scan.* Rejected: quadratic in the fact set, which is
  visited on every commit.

---

## D6. No event index, because the two rule categories keep history-reading and fact-reading apart — carried

**Decision**: derivation reads facts. A projection reads the events in the batch being
committed. No rule searches the log.

*(Superseded in part, 2026-09-19: a commit carries one event, so a projection reads the event
being committed rather than a batch. Nothing else here changes — no rule searches the log either
way.)*

**Rationale**: the earlier attempt carried an event index as its single recorded
complexity, justified by rules being allowed to read events *and* facts in any
category. That permission is what forces a history search, because a derivation joining
a fact against an event has to find the event. Separating the categories removes the
need: a projection already has the new events in hand, so it never searches, and a
derivation reads only the fact set. The index, its maintenance and its justification all
disappear.

The cost is expressiveness: a rule cannot say "this fact, *and* that event once
happened". That shape is what temporal rules are for (deferred), and until they arrive a
projection expresses "this event implies this fact", which covers what an author needs
at this scope.

**Alternatives considered**:

- *Allow mixed premises and carry the index.* Rejected for now: it is a mechanism with a
  requirement behind it only once temporal rules exist, and adding it then is additive.
- *Allow mixed premises and scan the log.* Rejected: the cost grows with history, which
  is what SC-005 and SC-009 measure against.

---

## D7. Incremental projections, recomputed derivation, and passes restricted to what can change — carried

**Decision**: on each commit, the new events are projected into **projected facts**
incrementally; **inferred facts** are then recomputed from those to a fixed point. A
pass after the first evaluates only the rules mentioning a construct the previous pass
gained, and derivation is skipped entirely when no projection matched.

**Terminology**: the spec calls every fact a *derived fact*, since all of them are
interpretations of history. This decision needs to distinguish the two ways one
arrives, so it says *projected fact* for one a projection concluded from an event and
*inferred fact* for one a derivation concluded from other facts. The distinction is
about how a fact was arrived at, not about two kinds of fact.

**Two additions, both from the same lesson.** A join is narrowed by an *index of facts by
construct and participant*, built once for the projected set and extended as each pass
concludes, because a premise that a bound argument can settle must not re-scan the whole
construct. And derivation is skipped entirely when no projected *fact* changed, since the
fixpoint is a function of the projected set — a commit that only adds reasons for facts
already there cannot conclude anything new. Both were measured rather than assumed, and
both were specified here before they were implemented.

*(Superseded in part, 2026-09-19: a commit is now one event, so the fixpoint runs per event
rather than once per burst. That is the measured cost — the SC-009 sustained figure fell from
~59,000–62,500 to ~6,410 events/s, still above its threshold — while the skip here when no
projected fact changed is what keeps a rebuild cheap, since most of its events re-establish
facts already present. See spec Amendments.)*

**Rationale**: FR-011 and FR-013 are both true by construction when derivation is
recomputed rather than patched, which is the earlier attempt's finding. The two
restrictions are not optimisations in the usual sense — they were the difference between
passing and failing SC-005 and SC-009 when measured:

| | before | after |
|---|---|---|
| one further event, full fact set | 29.6 ms | 4.0 ms |
| sustained commits | below 1,000/s | ~2,300/s |

The figures are the prototype's, at a cast of 20 and a history of 10,000 events, and
are carried as the reason the restrictions exist rather than as a target for this
implementation — F# numbers will differ, so SC-005 and SC-009 are what assert.

The first fix was a join whose second premise re-scanned its whole construct bucket
instead of using the argument the first premise had already settled; the second was
evaluating every rule on a final pass that could only conclude nothing. Both are
recorded because the lesson is that a performance problem is more often a data-structure
mistake than a missing backend.

**Alternatives considered**:

- *Recompute everything including projections.* Simple, and fine for a rebuild, but
  quadratic over a session.
- *Patch inferred facts incrementally.* Truth maintenance: support sets, invalidation
  propagation and a great deal of care. Rejected as the opposite of a solid base.
- *Semi-naive evaluation seeded only with new facts.* Strictly faster and materially
  more machinery; the simpler restriction already clears the targets with an order of
  magnitude to spare.

---

## D8. Ordering comes from the caller, never from arrival — carried, narrowed

**Decision**: each event carries a game time. The caller supplies events in order and
the engine commits them in that order, assigning each a dense history index. Game times
must be non-decreasing; a batch that would violate this is reported.

*(Narrowed 2026-09-19: a violating commit is **raised** as `TimeWentBackwards`, not reported as
a returned value, and since the batch entry point was removed a commit is one event — a run is a
fold. With only this case and the bound left, the returned-error type went and `Result` left the
commit path; see spec Amendments.)*

**Rationale**: FR-024 forbids taking effect order from arrival, and FR-023 needs
same-game-time ordering defined and repeatable. The caller's order plus a dense index
satisfies both, and the index breaks ties. Requiring non-decreasing times keeps history
sorted, which keeps replay and reporting simple; a violation is a caller bug and fails
loudly.

**Alternatives considered**:

- *The engine assigns times, or sorts by arrival.* Rejected by FR-024, and it makes a
  burst diverge from a smooth delivery, which SC-008 forbids.
- *Out-of-order insertion, sorted by game time.* Rejected: the tie-break becomes
  arbitrary, and it lets history be reshaped.

---

## D9. The work bound is a parameter, so that the reporting path is reachable — carried

**Decision**: derivation is bounded per commit. The ceiling is a parameter with a
generous default; exhausting it is reported naming the rules that were still
concluding.

*(Narrowed 2026-09-19: exhaustion is **raised** as `DerivationBoundExceeded`, which was already
this decision's posture; what changed is that it is now the only sibling of `TimeWentBackwards`,
and no engine failure is returned. See spec Amendments.)*

**Rationale**: FR-014 requires the bound be *reachable deliberately*, and that
requirement exists because of a finding in the earlier attempt: with a finite vocabulary
and a bounded cast, the fact set and the set of reasons are both finite, so a
well-formed rule set cannot fail to settle. A criterion asserting that "a rule set that
would never settle is reported" would therefore assert something unreachable. Making the
bound injectable turns a vacuous criterion into a tested one: the test drives it and
asserts the report. SC-006 is that test.

**The unit of accounting** (checklist Q4): one evaluation is one completed match of a
rule's premises — one conclusion that rule considered. That is the unit which grows with
the number of facts and rules, so it is the one an author can reason about. The default
is **1,000,000**, which at a cast of 20 sits one to two orders of magnitude above what a
legitimate fixed point costs, so it detects a runaway rather than reporting a merely
large rule set.

**Alternatives considered**:

- *A fixed, non-injectable bound.* Rejected: the failure path would be unreachable and
  therefore untested.
- *No bound, relying on rules terminating.* Rejected: the argument that they do is
  subtle enough that failing loudly is cheaper than relying on it.
- *A wall-clock timeout.* Rejected by FR-022: observable behaviour must not depend on
  elapsed real time, which would make exhaustion irreproducible.

---

## D10. A wrong rule set stops the run from starting — superseded

**Was**: rules validated before a run, a structurally wrong rule raising rather than
being returned from every call.

**Why it is gone**: there is no longer a runtime validation step to sequence. A rule is
an expression in F# over the event and fact types, so a derivation premised on an event,
or a rule concluding an event, does not type-check, and a rule cannot name a construct
the vocabulary does not have. The refusal therefore happens earlier than D10 could
arrange — at authoring time rather than before a run — and the spec records it as a
property of the approach in Assumptions rather than as a requirement the engine
satisfies. The distinction D10 drew still holds for what remains: a malformed *event* is
a caller's mistake in a running system and is reported, where a malformed *rule* is an
authoring mistake that cannot arise.

---

## D11. Failures redirect — carried, narrowed

**Decision**: failure messages are part of the interface rather than debugging output.

- A failed scenario assertion attaches the whole report — history, every fact with its
  rule and premises — so a failure reads as a narrative diff rather than "expected true,
  got false".
- A runtime failure names what it was about: the rules still concluding when the bound was
  exhausted (FR-014), the game time that went backwards (D8). *(Narrowed again 2026-09-19: the
  character-not-in-cast failure went with the declaration check, and both remaining failures are
  raised exceptions rather than returned values — see spec Amendments.)*

**Rationale**: FR-028, and the author is the one who has to act on the message. The
earlier attempt found both forms valuable for the same reason: this is an authoring
tool, and identifying what is wrong is only half of being useful. FR-026 already
requires a stable diffable report, so attaching it costs nothing further.

**Narrowed by D10**: the "a rejected rule names the rule, the construct, and where the
construct belongs" case is no longer the engine's to produce. The F# compiler's type
error is that message now, and it is a better one because it is produced at the point of
the mistake rather than at startup.

---

## D12. The engine is a source folder compiled before the game — carried, narrowed twice

**Decision**: the engine is `narrative/`, a source folder in `fsharp-game.fsproj`, listed
**before** the game's own files. Units live in it too, as one module per unit.

**Rationale, narrowed twice**: the prototype separated engine from units so that "a unit
cannot edit the engine" was structural, and put both in a separate library so that "adding
a unit changed no engine file" was a fact about a diff. The first reason went with the
closed vocabulary. The second went with the modularity requirement itself.

What FR-025 still needs — "usable with no graphical interface" — survives without a
separate assembly, because **F# compiles in list order**: listing `narrative/` first makes
a reference from the engine into the game's own files a compile error. That is the same
one-way dependency a project boundary would give, without a second project to build and
keep in step. It was briefly a separate `Narrative.fsproj`; the assembly boundary was
rejected because it bought no property the file order does not already give.

Units are not put in a third directory, because with a shared vocabulary "engine" and
"unit" are not different kinds of code — both are rules, and separating them would be
directory ceremony without a property behind it.

**Alternatives considered**:

- *A separate library project.* Rejected: it buys nothing the compile order does not, and
  it costs a second project, a second build, and a reference to keep correct.
- *The engine in the game project but listed last.* Rejected: the engine could then reach
  into the renderer, which is the one thing the separation existed to prevent.
- *A `narrative/` folder with units in a subfolder of their own.* Rejected as above.

---

## D13. Determinism is a property of the data structures — carried

**Decision**: canonical text is the ordering for every collection whose order is *shown*. No
collection is iterated in insertion order, and no ordering is taken from a hash. Storage order is a
separate matter and is internal (research D33).

**Rationale**: FR-021 and FR-022. Making it structural rather than a discipline means a
future contributor cannot break it by forgetting to sort: the map iterates in key order
because it is a map, not because someone remembered. In F#, that means `Map` rather than
`Dictionary` wherever output is derived from iteration, and `StringComparer.Ordinal`
wherever strings are compared — the default comparer is culture-sensitive, which would
make FR-021 false in a locale nobody tested in. **Narrowed by D33**: the fact set is now
keyed by the fact's own value, which is ordered structurally and internal, so the rule is that
every read path a reader sees sorts by text — one place per read path rather than one place in
the type.

**Alternatives considered**:

- *Sort at output time.* Rejected: it leaves the requirement as something a caller or a
  future module can forget.

**As built (T050)**: audited `Canonical.fs`, `Facts.fs` and `History.fs` for comparisons
that could pick up a culture. Every ordering is `Canonical.ordinal`, which is
`String.CompareOrdinal`, and it is the only ordering function the engine has; rule and
reason identity use `String.Equals(_, _, StringComparison.Ordinal)`; saved game times parse
with `CultureInfo.InvariantCulture`. Where no comparer is named (the participant index), the engine is
relying on F#'s structural comparison, which is ordinal for `string`, and that reliance is
what the locale assertion in `test/NarrativeVocabularyTests.fs` pins rather than assumes:
the famous case is Turkish, where a culture-sensitive comparison would order `I` and `i`
differently and change a report.

Nothing under `narrative/` reads a clock, an RNG, `Guid`, `Environment`, or a stopwatch, so
no observable output depends on ambient state (FR-022). The one place time enters is
`GameTime`, which the caller supplies — the engine never asks what time it is.

---

## D14. Rule order is not observable, and here is why it holds — carried

**Decision**: a pass reads one snapshot of the fact set and writes into a separate
accumulator, and the result is taken when a pass adds nothing. Facts and reasons are
canonically ordered. Consequently a rule set assembled by concatenating units in any
order produces byte-identical output.

**Rationale**: FR-019 and SC-007. This is the property that makes units independent
rather than accidentally coupled by the order they were listed in, which is what the
earlier attempt could only claim and not assert. The reason it holds is worth stating
precisely, because it is a property of the evaluation strategy and not of the rules: a
monotone fixpoint over a snapshot converges to the same set whatever order the rules are
tried in, and everything observable is then sorted canonically. The one thing order
*does* affect is the work counter, so a rule set sitting exactly on its budget could
report differently under permutation — recorded so that SC-007's assertion is understood
to be about outputs, not about the budget.

**Alternatives considered**:

- *Prescribe an order units must be listed in.* Rejected: it makes a unit's behaviour
  depend on its neighbours, which is the coupling FR-019 rules out.

---

## D15. Tests assert in plain xUnit facts, and performance claims are measured in a test — carried, adapted

**Decision**: scenario tests are xUnit facts whose failure message is the engine's own
report. FsCheck generates the parameterised and randomised scenarios FR-027 asks for.
SC-005 and SC-009 are measured in a test rather than in a benchmark harness (research
D15), and SC-006 is asserted alongside the bound's behaviour in the derivation tests,
where the exhaustion is driven rather than measured.

**Rationale**: FR-026's diffable report is what makes failures legible, and it is built
anyway — so a test framework is assertion plumbing the repo already has. The performance
figures belong in the suite because a claim that is not measured is not a claim, and a
regression should fail the build.

**Question stated before measuring**, per the constitution: *at a cast of 20, does
rebuilding state and processing the next event meet the throughput and latency the game
needs, and do both hold under sustained load?* The earlier attempt took a dependency on
the strength of benchmarks run at seven to thirty times the cast size this engine
operates at, and its own notes record that the headline win was a data-structure fix
rather than the dependency. So: no benchmark harness, no runtime dependency, and the
figures are asserted where they matter.

**Alternatives considered**:

- *Expecto, or a bespoke harness.* Rejected: the repo tests with xUnit, and a second
  test framework in one repository is a cost with no benefit.
- *A `bench/` project.* Rejected: it is what made a scale with no bearing on the design
  feel like the problem.
- *No generator — hand-written parameterised cases.* Rejected: FR-027 asks for
  generation, and FsCheck is the smallest thing that provides it.

---

## D16. Content is units, and adding one is adding a file — superseded

**Was**: a unit declares its predicates, writes its rules and exports them; nothing
registers it, and a unit that is not linked is not present.

**Why it is gone**: the vocabulary is closed, so a unit adds *rules* and not
*vocabulary*, and a construct it needs is added in the shared vocabulary. The
consequence is recorded in the spec as a deferral with a trigger — *a second narrative
unit wants a construct the vocabulary lacks* — and the reason it can wait is that
opening the vocabulary later is expected to change how a rule's pattern is written, not
how patterns are matched, attributed or derived.

---

## D17. No size ceiling is fixed in advance — carried

**Decision**: no line budget is asserted by this plan. Once the design is implemented,
the budget it actually implies is recorded here, and any module over roughly 200 lines
carries a stated reason.

**Rationale**: N-I's cast-size clause, which is also a correction the earlier attempt
paid for. It ratified 1,200 lines before designing and finished 45% over, which left a
choice between compressing working code and abandoning a number with no basis. Bounding
*responsibility* — one module, one job — is a rule that can be applied while writing;
bounding a total is a rule that can only be checked afterwards.

**As built (T047)**: the design implies **1,226 lines across 11 modules**, largest
`Facts.fs` at 220. The earlier attempt's 1,200-line ratification would have been within
2% of the truth and still been the wrong instrument: it was decided before the design
existed, so passing it would have been luck, and the modules that needed a reason to be
their size are not the ones a total would have flagged.

*(The figures below are the T047 snapshot. D34 changed `Canonical.fs`, `Vocabulary.fs`,
`Facts.fs` and `Engine.fs`, and the table was not re-measured; `Vocabulary.fs` in particular was
recorded before the twenty-case roster and the hand-written name matches landed, so it is well under
what it holds.)*

| module | lines | | module | lines |
|---|---|---|---|---|
| `Units/Cast.fs` | 24 | | `History.fs` | 121 |
| `Units/Friendship.fs` | 55 | | `Rules.fs` | 138 |
| `Units/Romance.fs` | 58 | | `Canonical.fs` | 163 |
| `Vocabulary.fs` | 70 | | `Engine.fs` | 172 |
| `Derive.fs` | 95 | | `Facts.fs` | 220 |
| `Harness.fs` | 110 | | | |

One module is over the roughly-200 line mark, and its reason is structural rather than
incidental:

- **`Facts.fs` (220)** holds three jobs that meet in one place, and two of them grow with
the vocabulary. It is the ordered fact map and its reason attribution (D5, N-IV); the
participant index, which is what keeps a join from re-scanning a construct (D7); and the
fact sources (D24), which are one line per construct a rule starts from — three plain and
three keyed, retyped from construct names to `FactKind` by D34 — and are the deliberate cost
of rules whose premises the compiler checks rather than a table the author maintains by hand. Splitting the sources out would move the
largest table in the design into a module named after it, while leaving the index and the
sources' shared representation behind, so the split would add a module without separating
anything. The size is the vocabulary's, which D20 accepts.

The test suite is a further ~1,300 lines across nine files, of which
`NarrativeHarnessTests.fs` and `NarrativePerformanceTests.fs` are the two over 200 and are
scenario and measurement files rather than modules.

**What is bounded instead**: the module tree in [plan.md](./plan.md) is closed — a new
module is a decision, not a convenience — and the mechanisms are bounded by the spec: no
event index (D6), no authored decoder (D21), no registry, no scenes, no temporal layer,
no negation, no proposals.

---

## D18. Sized for a cast under 20 — carried

**Decision**: every mechanism is sized for at most 20 characters and tens of rules.
Anything that only pays off at a larger scale is out of scope.

**Rationale**: FR-030. The earlier attempt's benchmark mistake is the reason this is
stated as a decision rather than assumed: a mechanism justified by a scale we do not
operate at is a mechanism that cannot be justified at all.

**Alternatives considered**:

- *Indexing, caching and incremental truth maintenance "for later".* Rejected: D6 and D7
  show the measurements do not ask for them, and the earlier attempt's notes are explicit
  that they are what made the design feel slow at a scale it never operated at.

---

## D19. Retraction is a projection step; an inferred fact is retracted by recomputation — carried

**Decision**: a projection may name an event that withdraws the conclusions it would
otherwise record. When an event matching that pattern arrives, the rule's yielded facts
are instantiated through that event's own bindings and removed. The withdrawing pattern
must bind every variable its conclusions use, so that the withdrawal is fully determined.
Withdrawing a fact that is not currently present is a no-op rather than an error.

**Rationale**: FR-011 says a fact ceases to be reported when none of its reasons still
hold, but a committed event never ceases to hold — history is immutable and there is no
decay — so a *projected* fact's reason can never be invalidated by itself. Retraction
therefore has to be an explicit step driven by a second event kind, and it is the only
reading consistent with D6: the decision is made entirely from the batch being committed,
so no rule has to search the log to discover that something has ended. Inferred facts
need no such step, because they are recomputed from the projected ones on every commit
(D7) — which is why "a crush, then it ends, then a crush again" reports only the current
state without any reason ever being invalidated.

*(Since 2026-09-19 a commit is one event; the point stands unchanged — a projection decides from
what is being committed, not by searching the log.)*

The no-op rule is deliberate and follows the spec's edge case that semantic oddity is
accepted where structure is not: an ending for something that never began is history
recording what happened, and the rules decide what it means.

**Alternatives considered**:

- *Negation in projections* — "this fact holds unless that event occurred". Rejected: it
  is negation, deferred as a whole, and it would make a projection's cost grow with the
  log.
- *Superseding: keep the ending event's reason and treat the newest event as
  authoritative.* Rejected: it leaves a fact reported whose support has gone, which is
  what FR-011 forbids.
- *Truth maintenance over reasons.* Rejected in D7, for the same reasons.

---

## D20. One closed discriminated union each for events and facts — supersedes D2, D3

**Decision**: the vocabulary is two F# discriminated unions and a small enum, declared
in one file, plus a newtype for a character so that reflection can tell a participant
from a plain string.

```fsharp
[<Struct>]
type Character = Character of string

type Trait = Shy | HotHeaded

type Event =
    | CharacterDeclared   of Character
    | TraitAssigned       of Character * Trait
    | FriendshipStarted   of Character * Character
    | FriendshipEnded     of Character * Character
    | CrushStarted        of Character * Character
    | CrushEnded          of Character * Character
    | RelationshipStarted of Character * Character
    | RelationshipEnded   of Character * Character

type Fact =
    | HasTrait             of Character * Trait
    | Friends              of Character * Character
    | AttractedTo          of Character * Character
    | InRelationship       of Character * Character
    | JealousOf            of Character * Character
    | FriendshipThreatened of Character * Character
    | BottledUpFeelings    of Character * Character
```

*(Superseded in part, 2026-09-19: the event is now `Arrived` — arrival gates nothing, see spec
Amendments — and D28 replaced the `Character` newtype with a closed roster of cases.)*

**Rationale**: it is the smallest representation that gives the spec what it asks for.
A rule is an expression over these types, so FR-017's two categories and the refusal of
a misplaced construct are the compiler's job rather than a validation pass (D10). Every
construct's name, arity and field types are derivable from the type, so the four parallel
tables the earlier attempt maintained have no successor (D21). And pattern matching on a
union is what an F# author already reaches for, so a rule reads as the reasoning it
describes rather than as a query against a schema.

**Alternatives considered**:

- *The prototype's uniform predicate shape.* Superseded — see D2.
- *One open polymorphic-variant family per narrative unit*, as the original design
  document proposed. Rejected twice over. A rule module can name only the family it cares
  about, but a *history* holds values from every family at once, which forces the union
  back into one place; and the engine must hold and dispatch over all tags, which is
  exactly what row polymorphism does not give. The design document's own argument for
  OCaml was this feature, and it does not survive contact with "the log holds them all".
- *A closed union plus an `Uninterpreted` catch-all.* Rejected — see D23.
- *Records with a kind tag and a field bag.* Rejected: it is the uniform shape with worse
  errors, and every rule would pattern-match on `obj`.

---

## D21. Reflection derives the construct's name, arity and field types — supersedes D4, its encoding half superseded by D30, its decoder removed by D32

**Decision**: one generic encoder and one generic decoder work over any union case, via
`FSharpType.GetUnionCases`, `FSharpReflectionValue.GetUnionFields` and `MakeUnion`. The
construct's name for canonical text comes from `nameof`, which is checked by the
compiler. Nothing per construct is written by hand.

**Rationale**: this is what pays for the closed vocabulary. FR-004 needs saved events to
deserialise, FR-010 needs reasons compared for identity, FR-026 needs a stable report,
and all three are served by one encoder and one decoder rather than a pair per construct
family. Verified against .NET before this plan was written:

- `GetUnionCases` gives every case's name, field count, field names and field types.
- A `Character` field is distinguishable from a plain `string` field by `PropertyType`,
  so participant positions are derived rather than declared.
- One generic encode/decode round-trips every case, including the nested list field that
  an unusual construct would have.
- The encoder needs **one branch per field *type*, not per case** — four branches cover
  the whole vocabulary — which is the cost that does not grow as the vocabulary does.
- `nameof` works on a union case, so canonical text carries no string literal that can
  drift from the type.

**Alternatives considered**:

- *Hand-written encode/decode per construct.* Rejected: it is the parallel-table problem
  the closed vocabulary exists to remove, and the drift it causes is silent.
- *A source generator (`Myriad`) emitting the per-construct code at build time.* Rejected
  for now: it is a build-time dependency and a code path an author has to read, for
  something reflection already does in a handful of lines. Revisit only if profiling
  shows reflection at the commit path matters, which at 1,000 events/s and a closed
  vocabulary it should not.
- *Derive participant positions from a convention on argument order.* Rejected: a
  convention is a silent failure, and the newtype costs one line.

---

## D22. Interchangeable participants are declared on the construct

**Decision**: a fact may declare that its participants are interchangeable — that the
order of names carries no meaning. Where it does, the engine puts the participants in
canonical order *on the value* as the fact is stored, so two orders are one fact, either order
satisfies a rule premise, and an event that withdraws pairs with the one that established
it whichever way each lists the names. The normalisation was in the text until D33 moved it
onto the data, which is the same declaration honoured in one place instead of at every render.

Applies to `Friends` and `InRelationship`, and to nothing else — **not** to the events
that project to them. The declaration is part of the closed vocabulary, so making a
further relation mutual is an edit of the same kind as adding a construct.

**Events are deliberately excluded, and this was got wrong first.** Normalising
`RelationshipStarted(Shinji, Akane)` to `RelationshipStarted(Akane, Shinji)` made history
read as something other than what happened, and made a saved history fail to load back
identically — history records who acted. The withdrawal pairing does not need it either:
`FriendshipEnded(Akane, Aiko)` cancels `FriendshipStarted(Aiko, Akane)` because both
instantiate to the same *fact*, which is where the normalisation belongs.

**A consequence worth stating**: a fact's canonical text is its normalised argument
order, not the order it was first concluded in. Otherwise the text of a fact could depend
on which rule happened to conclude it first, which would make SC-007's byte-identity
across rule permutations false. *(Since D33 the normalised order is a property of the stored value
rather than of its text, so the same thing holds one step earlier: the value cannot depend on which
rule concluded it first, and the text of that value follows.)*

**Rationale**: clarification 1. Two of the seven facts are mutual in meaning, and without
the declaration each is a silent trap: `Friends(Aiko, Akane)` would not satisfy a premise
written `Friends(Akane, Aiko)`, and `FriendshipEnded(Akane, Aiko)` would fail to withdraw
a friendship begun as `FriendshipStarted(Aiko, Akane)`, leaving the fact true forever.
Both failure modes are silent and both are the kind of thing a generated scenario would
have to be written to catch, which is a poor use of the harness when one declaration
removes the class.

The normalisation is on the key, not on the stored fact, so which name came first stays
recoverable from the events for a rule that needs to know who acted.

**Alternatives considered**:

- *Every fact positional and directional, with symmetry left to the author.* Rejected: it
  makes a social fact's identity depend on the incidental order two names appeared in an
  event, and it fails silently in the withdrawal case.
- *Avoid mutual relations; express friendship as two directed facts.* Rejected: it
  changes the fact count SC-001 asserts and makes every mutual relation two facts that
  must be kept in step by hand.
- *Infer mutuality from the construct's name.* Rejected: nothing decides semantics from
  spelling, and a rename would change behaviour.

---

## D23. There is no uninterpreted event; an unrecognised one fails — supersedes the prototype's escape hatch, superseded by D32 (the case is now unrepresentable)

**Decision**: every event the engine holds is a construct the vocabulary has. An event
the engine cannot recognise — an unknown construct name, or a name whose arguments no
longer fit — is reported as a failure. There is no accepted-but-carried-along form.

**Rationale**: clarification 5. The prototype carried an `Uninterpreted` event so a
newer or older world could supply something the engine did not know. That made an
unrecognised event indistinguishable from one no rule happens to match, which is a
different thing and the one FR-006 is about. With a closed type there is nowhere for such
a value to come from except a hand-written escape hatch, and the cost of having one is
that a typo in a construct name becomes a silently inert event instead of a loud error.

The consequence for persistence is worth stating because it is asymmetric: adding a
construct is safe for histories already saved, because it changes none of the constructs
already there. Removing or reshaping one is a breaking change to saved history, and is
meant to fail loudly rather than leave events nothing can interpret. FR-004 asserts the
first half.

**Alternatives considered**:

- *The prototype's `Uninterpreted` catch-all.* Rejected as above.
- *Ignore an unrecognised event and carry on.* Rejected: it is the same silent-failure
  shape, and it would make a mis-deserialised history look like an unusual one.
- *Version the saved format and migrate.* Rejected for now: it is machinery for a
  migration story nobody has needed, and the closure's trigger is the right place to
  revisit it.

---

## D24. Rules are computation expressions, and the builder captures the premises

**Decision**: each category is built by its own builder.

A **derivation** is a computation expression: `let!` binds a source's facts and the
builder records each binding as a premise.

A **projection** is a record of three named parts — the event that establishes, an
optional event that withdraws, and the fact to yield. Deliberately *not* a computation
expression: there is no sequencing to express, and with a single premise there is nothing
for an author to restate wrongly, so a CE would be machinery for a benefit that does not
apply. The parts are named rather than positional because the establishing and
withdrawing patterns have the same type, so positional arguments of that shape could be
swapped silently.

```fsharp
// history -> facts. Establishes on one event, withdraws on another (D19).
let befriended =
    project "befriended" {
        on     (fun (FriendshipStarted (a, b)) -> struct (a, b))
        ending (fun (FriendshipEnded   (a, b)) -> struct (a, b))
        yield' (fun (struct (a, b)) -> Fact.Friends (a, b))
    }

// facts -> facts. `let!` binds a construct's fields; the builder records the premise.
let jealousy =
    derive "jealousy" {
        let! struct (a, b)  = FactSource.attractedTo
        let! struct (b', c) = FactSource.inRelationship
        where (b = b')
        where (a <> c)
        yield' (Fact.JealousOf (a, c))
    }
```

`FactSource` values are one line per fact construct, in the vocabulary module, with the
construct's name taken from `nameof` so it cannot drift.

**Rationale**: FR-010 requires every conclusion to record the rule and the specific
premises that rule applied to, and FR-012 requires a reason to stop being offered when
its premises stop holding. A notation where the author writes the premises twice — once
to match on, once to record — has one place to get that wrong per rule, and the wrongness
is invisible because the conclusion is still correct. Having the builder capture each
`let!` binding as a premise makes attribution a property of the notation, which is N-IV
expressed as a design decision rather than a discipline.

It is also honest about F#: `let!` over fact sources is how an F# author already writes a
comprehension, so the notation needs no parser, no quotation reflection and no schema,
and the patterns are checked by the compiler for exhaustiveness against the closed type.

**Alternatives considered**:

- *Quotations (`<@ ... @>`) with symbolic variables.* Rejected: the DSL would be parsed
  at runtime from an expression tree, which reintroduces exactly the compile-time
  checking the closed vocabulary was bought for, and it makes a rule's shape invisible to
  the IDE.
- *The prototype's term-based DSL with logic variables.* Rejected: it requires terms to
  be one untyped union, which is D2 again.
- *Plain list comprehensions, premises written by hand.* Rejected as above: it is the
  notation with one place per rule to get attribution wrong.
- *One source definition per construct, hand-written.* Accepted, and stated as the cost:
  it is one line per construct with the name `nameof`-checked, and it is what keeps rules
  typed. Deriving the sources by reflection instead would push `obj` into every rule.

---

## D25. Reasons are retrievable while the game is running

**Decision**: the engine exposes, for any current fact, the rules and premises currently
supporting it — the same retrieval a headless run's report is built from. Prose
explanations remain out of scope.

**Rationale**: clarification 2. FR-010 already requires every distinct reason be
retained, and `has_trait`-style reason lookup is a map access on data the engine is
holding; a run's report then becomes one caller of the retrieval rather than a second
mechanism. The spec's Assumptions previously framed the rule-and-premises report as a
harness capability, which would have quietly dropped the in-game half of the
explainability requirement — a player seeing that Aiko is jealous and asking why.

**Alternatives considered**:

- *Reasons as a report-only concern.* Rejected: it makes the live game the one caller
  that cannot explain anything, which is the opposite of the requirement's intent.
- *Explanations as prose, expanded lazily.* Deferred, and N-IV says a future feature
  adding it must not write an explanation into history.

---

## D26. The vocabulary is the minimum the criteria need, and the omissions are recorded

**Decision**: 8 event constructs, 7 fact constructs and 2 traits, as in D20.

**Rationale**: N-I, and FR-030. Each construct is there because a criterion or an
Assumption needs it:

| Construct | Required by |
|---|---|
| `Arrived` | the starting situation names who has entered the story (FR-003); it gates nothing, and a unit may project it like any other event (spec Amendments, 2026-09-19) |
| `TraitAssigned` | SC-001's "Aiko is shy"; traits arrive as events, never as fields |
| `FriendshipStarted` / `FriendshipEnded` | SC-001; the "friendship, then ended, then friendship again" edge case; FR-011's withdrawal |
| `CrushStarted` / `CrushEnded` | SC-001; US1 scenario 7, "a crush that ends" |
| `RelationshipStarted` / `RelationshipEnded` | SC-001's "dating her"; the withdrawal half of `InRelationship` |
| `HasTrait`, `Friends`, `AttractedTo`, `InRelationship`, `JealousOf`, `FriendshipThreatened`, `BottledUpFeelings` | SC-001's five stated facts and exactly three conclusions; US1's seven scenarios |
| `Shy` | the one trait a rule reads — `FriendshipThreatened` + `Shy` concludes `BottledUpFeelings` |
| `HotHeaded` | present so FR-027's "scenarios parameterised over traits" is not vacuous: it is the value at which the shyness rule must *not* fire |

**Deliberately not present, with why**:

- `Witnessed`, `SatNextTo`, `TalkedTo`, `FlirtedWith` — world events that only earn their
  place once temporal rules can pattern across the stream, which is deferred. With no
  temporal layer they would be constructs no rule premises on, so they would be
  vocabulary nothing reads.
- `Avoided`, `Confronted`, `ConfessedTo`, `AskedWhatsWrong` — the committed forms of
  candidate events, deferred with candidates themselves.
- `Loyal`, `Outsider` — traits the deferred reaction rules are built around; nothing in
  this feature reads them.
- `Uninterpreted` — rejected outright, see D23.

Adding any of these later is one case in the union, and the type system then reports
every rule that must account for it. That is the deferred open vocabulary's trigger
working as intended rather than a cost being hidden.

**Alternatives considered**:

- *The design document's fuller MVP list.* Rejected: it includes candidate-side and
  temporal-side constructs whose capabilities are deferred, so they would be dead code
  with a requirement behind nothing.
- *`Shy` alone.* Rejected: FR-027 requires parameterising a scenario over traits, which
  one trait cannot exercise — there would be no value at which the rule must not fire.

---

## D27. The surface has no way to assert a fact, and the check that says so

**Decision**: FR-031 and FR-032 are met by absence. There is no function that writes a fact,
none that alters or removes a committed event, and none that schedules, paces, orders or
advises. The engine interprets.

**Rationale**: N-I's second half. The check matters more than the claim, because "we did not
write such a function" is the kind of assertion that rots quietly once someone needs a fact
early.

**What the audit (T051) found**, reading `Engine.fs` and `Harness.fs` for anything that reads
as deciding rather than interpreting:

| Surface | Why it cannot decide |
|---|---|
| `Engine.create`, `createBounded`, `commit` | the only way in is a committed event, in the caller's order |
| `Engine.facts`, `factsOf`, `reasonsFor`, `history` | read-only; `history` returns committed events, not a handle to them |
| `Harness.run`, `runBounded`, `report`, `shouldDerive` | drives commits and renders them; `Scenario.Steps` is the *caller* scripting a sequence, which is the harness standing in for the simulation rather than the engine proposing anything |
| `Harness.RunResult` | history, facts and the report — no ranking, weighting, or candidates |

No field of `Engine` is `mutable`, and the `mutable` bindings and `ResizeArray`s in
`Engine.commit`, `Derive.infer`, `Rules.DerivationBuilder` and `Harness.report` are locals of
one call, so nothing is reachable through the value that carries the state. Facts enter only
as a projection of a committed event or as a rule's conclusion.

**One residual, flagged rather than settled**: `Engine.Engine` is a record, so its fields are
visible and every type it is built from is public, which means a caller can reach around the
interface and build a state by hand:

```fsharp
{ engine with Projected = fst (Facts.add inventedFact inventedReason engine.Projected) }
```

Nothing invites this and there is no API for it, but it is not *absence*, which is what FR-032
asks for. The remedy is an `Engine.fsi` that exposes `type Engine` without its representation,
which hides the fields from everything outside `Engine.fs`; it would be the repository's first
signature file, and a line per public function to keep in step. Left as the user's call: the
requirement is about the interface, and whether a same-assembly representation counts as part
of it depends on how the game is written rather than on the engine.

---

## D28. The roster is closed, and a character is a case rather than a string — superseded in part

**Decision**: `Character` is a union whose cases are the twenty people who can exist in the
story. It is no longer a newtype over a `string`. Which of them is *in play* is still decided
by `CharacterDeclared` events, exactly as it was.

*(Superseded in part, 2026-09-19: `CharacterDeclared` no longer decides who is in play. Once the
roster closed, the declaration check no longer caught a name that does not exist — the type does
that — and only made FR-006's cast-membership clause reachable, so it was removed as a gate the
model does not need. The event is now `Arrived`, and the engine holds no cast set; see spec
Amendments, 2026-09-19. Everything else here stands.)*

**Rationale**: the cast does not change, so the type that names it does not have to admit names
it does not have. Three things follow, and each is one the string form could only promise:

- A misspelled character does not compile. With names in strings, a rule that names
  `"Akane "` instead of `"Akane"`, or that reasons about somebody nothing ever declared,
  concludes a fact about a person who does not exist and reports nothing wrong — the class of
  mistake the closed vocabulary (D20) exists to remove, left in the one place a union had not
  reached.
- A match over a character is exhaustive, so a rule with something to say about one character
  and nothing about the rest is the compiler's business rather than a convention.
- Nothing about a character's identity depends on comparing text: the roster's order is its
  declaration order, which is culture-free rather than usually equivalent.

**What it costs, recorded rather than discovered**:

- The engine now holds one piece of content, the roster. It is content of the same kind as
  `Trait = Shy | HotHeaded` — the *types* are the engine's and the *rules* are the caller's,
  which is the line D16 draws. `world/Cast.fs` does not exist yet; when it does it maps a
  roster case to visual identity, and the engine still never sees an asset id.
- A character's name has to be an identifier, so a name that is not one needs a backticked
  case: that is the price of the compiler being able to spell it.
- Renaming a case is not a breaking change, because nothing reads a name back: there is no
  decoder and no saved history for a rename to invalidate (research D32, which withdrew the caveat
  this bullet used to carry). The name is still what canonical text writes, so a rename renames the
  fact in every report and test that asserts one — visibly, at compile time or the first run.
- Twenty is the cast size SC-005 and SC-009 measure at, so the roster is twenty, and
  `Vocabulary.characters` enumerates the cases by reflection rather than repeating them as a
  list, so the number cannot drift from the type without something noticing.

**What it looked like when it went wrong, because the next reader may meet it again**: the
first implementation of `characterName` was `string character`, which for a union goes through
F#'s formatting machinery. Rendering a fact is what comparing two reasons does — the innermost
loop of a commit — so the engine lost a factor of four and SC-009 measured 606 events per
second against a criterion of 1,000. `ToString()` was no better, which is how we know it is the
generated formatter and not the `string` function. A `match` returning `nameof` per case is
free, and is what the module does now. What caught it was the suite's own measurement: nothing
about the output was wrong.

**Alternatives considered**:

- *A string, with a module of named constants* (`let aiko = Character "Aiko"`). Rejected: the
  names are then compile-checked where they are used, but the type still admits any string, so
  the mistake that motivated this is still representable and a misspelling in a report is still
  somebody who exists.
- *A case per character carrying its name as a field.* Rejected: the case name is already the
  name, and a field would be a second place to state it.
- *Generating the roster from the game's cast data once `world/Cast.fs` exists.* Deferred, not
  rejected: the narrative engine cannot reference the game (D12), so this would be a build-time
  source generator rather than a reference. Worth revisiting then, because there would be one
  source of truth instead of two that have to agree.

---

## D30. Encoding matches on the construct; only decoding is derived — decoding removed by D32

**Decision**: canonical text is written by matching on the construct — one arm per case, naming its
arguments with `nameof` and with the character and trait name functions. `FSharpValue` is still what
turns saved text back into a value, and still what reads a construct's shape at start-up.

**Rationale**, measured, because the guess about which layer costs what was wrong both times it was
made. Asking the type what a value is — `FSharpValue.GetUnionFields` — costs **~17 µs** a call and
re-derives the union's cases every time rather than caching them. The same text written out as a
match costs **~0.3 µs**. And that call was nearly all of a commit's time:

| | before | after |
|---|---|---|
| one further event on a 10,000-event history (SC-005) | 3.1–4.6 ms | 0.1 ms |
| rebuilding from 10,000 events (SC-005) | 455–478 ms | 16 ms |
| sustained throughput, twenty cast members (SC-009) | ~2,750/s | ~62,500/s |
| a commit that changes a projected fact | 3.4–4.7 ms | 162 µs |

The arithmetic that explains it: `Facts.insert` called two reflectors per fact when it built the
participant index (`Canonical.factName` and `Facts.participants`, each with its own
`GetUnionFields` — today `Vocabulary.factKind` and `Vocabulary.factParticipants`, both matches),
so a hundred-fact world paid ~3.4 ms a commit before a single rule ran. No output
changed, which is why only a measurement could find it — and why the suite now asserts that a
canonical text render stays off that path, at a bound of 5 µs a render, seventeen times what the
match costs.

**Why decoding keeps the derivation**: text is a string, so *some* dynamic mapping from a name to a
constructor is unavoidable. Deriving it keeps saved history and the vocabulary in step by
construction, and it runs once per saved line at load, where 17 µs a line is 17 µs per hundred
thousand events. Derivation is worth its cost exactly where it is cold.

**What it costs**: adding a construct now means editing the encoder as well as the type. The
compiler is what makes that safe — the encoder is an exhaustive match, so a case left out is a
warning, where the per-construct `FactSource` table that remains hand-written (D24) is silent. It
also means D21's "one branch per field type, not per construct" no longer describes the encoder: the
encoder now grows with the vocabulary, one arm a construct, and that is the price of not asking.

**Alternatives considered**:

- *Caching what reflection returns* — a per-type map of cases, or a precomputed reader — rather than
  writing the encoder out. Rejected: it keeps a per-value indirect call and a boxed argument array
  in the hot path to save fifteen lines, and the answer only needed caching because it was being
  asked for per value.
- *Hand-writing the decoder too.* Rejected: the name-to-constructor direction is genuinely dynamic,
  and a hand-written version is a table that a saved history can fall off the end of. Written out,
  decoding is the single place the vocabulary and the text are reconciled.
- *Leaving it alone, because 2,750 events a second already beats the criterion.* Rejected as the same
  mistake in another place: the criterion is a floor, not a target, and knowing the engine was doing
  ~25 times the work it needed to is a fact about the design that the game should not be built on
  top of unwittingly.

---

## D31. A construct is not described ahead of time

**Decision**: there is no `Construct` record. The encoder matches on the construct and names its own
arguments, and which constructs are interchangeable is stated once, in `Vocabulary.sortParticipants`,
which is what puts a fact in canonical order as it is stored (research D33).

**Rationale**: `Construct` was a cache, and once D30 had moved encoding onto the type it had one
reader left, which ran once at start-up. Every field was available from the `UnionCaseInfo` it
wrapped: `Arity` is that case's field count, `FieldTypes` their `PropertyType`s, `Participates` the
fields whose type is `Character`, `Interchangeable` the attribute, and `Case` is the case.

*(D32 then removed the decoder, so the cache's other justification — decoding once per saved line —
is gone too: nothing asks a construct for its arity or field types at all, and the only case metadata
read anywhere is the names, for the start-up uniqueness check.)*

It had also started to cost correctness rather than lines. `Participates`, reflected, was a second
statement of which arguments are people, alongside `Vocabulary.factParticipants`, which is what the
participant index reads. They agreed, and nothing kept them agreeing — the drift D20 exists to
remove, reintroduced by a change whose purpose was to remove work.

**What replaced the attribute's reader**: nothing at run time. The declaration stays on the case, and
`NarrativeVocabularyTests` fails if the encoder's arms disagree with it — a construct that declares
its participants interchangeable must render the same text with its arguments swapped, and one that
declares nothing must not. That is the same shape as the roster's round-trip test (D28): a
declaration the compiler cannot enforce, held true by a test rather than by a mechanism.

**What it costs**: decoding calls `GetFields()` once per saved line, which is the reflection the
record was caching — paid per line rather than per value. In exchange `Canonical` lost a type, six
fields, and a map lookup from every rendered value.

**Alternatives considered**:

- *Keeping `Construct`, shrunk to what decoding reads.* Rejected: a smaller record of things the case
  can be asked for, kept in step by hand for a reader that does not repeat.
- *Stating interchangeability only in the encoder, with no attribute on the case.* Rejected (D22):
  declaring it on the construct is what lets a reader of the vocabulary see which facts are mutual,
  and the test is what keeps the declaration honest.

*(D32 removed the decoder, so what this decision leaves behind is smaller than it reads: the union's
cases are read once at start-up for the uniqueness check, and nothing asks a construct for its arity
or field types at all. The record stays deleted, and the encoder is unaffected.)*

---

## D29. History is a list, newest first

**Decision**: committed history is a list in reverse commit order, and nothing else — `History.State`
is an alias for the list rather than a record around one. Appending conses each event onto the head,
giving it the index after the newest one's; reading reverses into commit order.

**Rationale**: an append must not copy what is already committed, and history may not be mutated
in place, because an earlier engine value has to keep describing the history it described (FR-001,
FR-002). A cons per event gives exactly that. The first implementation was an immutable array whose
capacity doubled per commit: it copied the whole history every time and the cost grew as the game
ran, which is what failed SC-009. Chunks of arrays replaced it and *bounded* that copy rather than
removing it.

**Measured before and after**, on the suite's own figures at the cast of twenty:

| | chunks of arrays | a list |
|---|---|---|
| `declaration-only`, which isolates the append | 1 µs per commit | 1 µs per commit |
| `fact-changing` commits | 4.3–4.7 ms | 3.4–3.8 ms |
| SC-005 rebuild of 10,000 events | 466 ms | 455 ms |
| SC-009 sustained | 2,857/s | 2,874/s |

Those differences are inside run-to-run noise, and the honest reason is that **both** designs append
in constant time: the chunked one copied at most 256 elements per append whatever the history's
length, and the list copies none. So this is a simplification rather than a speed-up. What it removes
is ~15 lines and a per-chunk invariant — every chunk non-empty, newest first, part-filled at the head
— and that is the kind of bookkeeping whose absence is a bug of its own, as the array version before
it demonstrated.

*(Superseded in part, 2026-09-19: there is no batch entry point now — a commit appends one event
and a run is a fold — so the large-batch commit an earlier performance note described no longer
exists. The append itself is unaffected: it was always one event. See spec Amendments.)*

**What the list costs**, recorded rather than discovered:

- A cons cell per event on top of the event object: 24 bytes against the 32 that the event and its
  array slot took, so about half again the memory for a long history.
- Contiguity, and it is worth being precise about how much: `CommittedEvent` is a reference type, so
  an array held contiguous *references* to scattered objects, not contiguous events. The locality
  given up is real but smaller than "a contiguous array of events" suggests.
- An indexed lookup would become a walk. Nothing performs one: `CommittedEvent.Index` is rendered in
  reports and asserted in tests, and the deferred `Witnessed(Aiko, Event42)` shape that would want it
  arrives with the temporal layer, if it arrives.

**Why the read path does not decide it**: nothing in the engine's reasoning reads history at all. A
commit touches `lastTime` and `append`; everything else — a save, a report, a test — reverses the
list, and would have allocated an equivalent list out of the chunks.

**Why the count is derived rather than stored or asked for**, verified rather than assumed because
the claim invites exactly the question "surely a list knows how long it is?": it does not.
`FSharpList<'T>` exposes a `Length` property and no `Count`, and every way of asking traverses it.
Measured at 400,000 elements against 1,600,000 — four times the length:

| asking | 400,000 | 1,600,000 | ratio |
|---|---|---|---|
| `List.length` | 0.85 ms | 4.98 ms | 5.9× |
| `xs.Length` | 0.82 ms | 4.48 ms | 5.5× |
| `Enumerable.Count` | 0.96 ms | 4.45 ms | 4.6× |

About 3 ns an element, so a history of a million events reads its own count in milliseconds. That is
why the index for a new event comes from the newest event's own index — which *is* the committed
count, since indices are dense and 1-based — rather than from `List.length` or from a field kept
beside the list. Asking would be a walk per commit, which is the shape of the array bug this
decision replaced; keeping a field would be a second place for one number to live, and the only way
that number can be wrong. `Enumerable.Count` is the row worth noting — the type does implement
`IReadOnlyCollection<'T>`, so .NET's usual fast path for that interface either does not apply here
or is itself the walk; either way it is linear.

**Alternatives considered**:

- *A record wrapping the list* (`{ Rev : CommittedEvent list }`, which is what it was first).
  Rejected: it is a name and a constructor around one field, and the only thing it bought was that a
  newest-first list could not be passed where a commit-ordered one is expected. That is real but
  small, and the alias does not lose it entirely — the name `State` still says which of the two a
  reader is holding — while the field it required was one more thing to keep in step with the list.
- *A mutable array or `ResizeArray`, grown in place.* Rejected: it makes history observable through a
  value that no longer owns it, so an engine held before a commit would show events committed after
  it. That is what "history is immutable" rules out, and several tests assert it.
- *A `Map<int, CommittedEvent>` keyed by the dense index.* Rejected: a tree per event for a lookup
  nothing performs, and it loses the order that is the log's whole purpose.

*(Numbered with the history decisions rather than placed among them: D29 was written after D30 and
D31, which is why the sections are not in numeric order here.)*

---

## D32. The engine does not persist history

**Decision**: `Engine.save`, `Engine.load` and `Engine.loadBounded` are removed, and with them the
whole decoding direction of `Canonical`. History lives in memory for the life of an engine value;
canonical text is written for identity, ordering, attribution and the report, and never read back.

**Rationale**: at the user's direction, and it withdraws a requirement (FR-004) rather than a
mechanism. Persisting a game is the game's business — the game is what holds the events it supplies,
so it is also what can hold them again when it saves — while the engine's job is interpreting the
values it is given.

Three things went with it, and each is a simplification rather than a loss:

- **The decoder, and with it almost all of the reflection.** `decodeEvent`, `decodeFact`, the
  argument reader and the map of cases existed only to turn saved text back into a constructor. What
  reflection is left in the engine is one start-up call, `caseNames`, which the uniqueness check
  needs. `Canonical` went from 179 lines to 100, and D31's decision stands with less behind it.
- **An unrecognised event became unrepresentable rather than reported.** D23 spent a decision on
  failing loudly when an event arrives that the vocabulary does not have, and its answer was "that is
  reachable only from text". With no text there is no such path: the event type is a closed union and
  events arrive as values, so the mistake cannot be built. This is the stronger form of the same
  property, and it **reinstates D4**, which said nothing committed is ever decoded and which D21
  superseded *because* of loading.
- **Renaming a character or a construct became safe.** D28 carried the caveat that a rename breaks
  histories saved under the old name. Nothing reads a name back now, so the caveat is withdrawn: a
  rename renames the fact in every report and test that asserts one, which is visible immediately
  rather than latent.

**What it costs**: the engine cannot reconstruct a session from a file, and the tests that proved a
round trip are gone — `saved history is one canonical event per line`, `a history saved and loaded
back continues as if never interrupted`, `a line that does not decode fails the whole load and names
the line`, and `an empty history saves as empty text and loads as an empty run`. Nothing else changed
behaviour: replay within a session is re-committing the in-memory log, which is what SC-005 and the
rule-set tests do and always did. The vocabulary tests that only existed to decode (round trips, and
refusals of an unknown construct, a wrong arity, an unknown trait, an unknown name and non-construct

text) are replaced by two tests asserting the literal canonical text of every event and every fact,
which is stronger coverage of the encoder than the round trips were.

**Alternatives considered**:

- *Keeping `Engine.save` as a rendering and dropping only the load.* Rejected as half a feature, and
  by the argument that removed the whole one: history is already rendered with indices and times by
  `Harness.report`, which is what a person actually reads.
- *Keeping the decoder for a game that might want to hand the engine text.* Rejected: an API with no
  caller is the thing to delete, and the game holds typed events — which is precisely why its save
  format is its own business.
- *Renumbering the requirements to close FR-004's gap.* Deferred, and recorded in the spec's
  amendment entry: the gap is documented in the requirements list, and renumbering touches five other
  artefacts. Mechanical whenever it is wanted.

---

## D33. Facts are their own identity, and reasons are carried with them — its read-path sorts superseded by D34

**Decision**: the fact set is `Map<Fact, Reason list>`. A fact is held under its own value, and the
interchangeable order of a mutual fact is normalised *onto the value* as it is stored
(`Vocabulary.sortParticipants`) rather than into a rendering of it. Canonical text is the order a fact
list is *shown* in, not the order it is stored in.

**Rationale**: a fact had two representations — the value, and the text it rendered to — and nothing
kept them in agreement. `Engine.facts` handed back the wording the projection happened to have
recorded (`InRelationship(Shinji, Akane)`) while the key said `InRelationship(Akane, Shinji)`, and a
premise written either way matched only because everything went through the same rendering. Comparing
text to find a fact is also the more expensive of the two.

Three things follow, and each removes machinery rather than adding it:

- **Normalisation happens once, on the data.** `sortParticipants` is applied where a fact enters or
  leaves the set, so a stored fact *is* canonical and the doublet cannot exist. The encoder no longer
  sorts: it renders a fact as it finds it, and `Canonical` lost its `mutual` helper and its last piece
  of knowledge about which constructs are interchangeable.
- **`FactEntry` is gone.** It held the fact (now the key), the text key (now the fact's own identity),
  and its reasons — three fields, two of which were the same fact twice. The set is a fact and why it
  holds.
- **The declaration on the case is still what the behaviour is tested against**, retargeted from the
  encoder to `sortParticipants`: swapping a fact's participants produces the same value exactly when
  its case declares them interchangeable (D22, D31).

**What it costs**: storage order is the value's structural order, which follows the *declaration*
order of the vocabulary's cases. So anything a reader sees has to sort explicitly — `Facts.toList`,
`Facts.ofCharacter`, and `Harness` where it compares or renders — one place per read path, on the cold
side. That is the price of the rule D13 originally got from the key: determinism is now a property of
the sort at each read path rather than of the container, and the locale test keeps it honest.

*(This is the one paragraph of D33 that no longer describes the code. D34 withdrew the sorts on
`Facts.toList` and `Facts.ofCharacter`, so a read path now reports the container's own order and only
the harness sorts.)*

**Measured**: canonical text render 0.21 → 0.13 µs, SC-009 55,500–62,500 → 66,667 events/s, SC-005
rebuild 16–19 → 15 ms. No string comparison per lookup, and no sort per render.

**Alternatives considered**:

- *A `Set<FactEntry>` whose identity ignores the reasons* — the literal reading of "a set of facts
  with their reasons". Rejected: F# records compare on every field, so an entry carrying one reason and
  an entry carrying another are different elements and the same fact appears twice, which is what
  FR-009 forbids. Making it work needs equality that ignores a field, and then `=` lies and `Set` stops
  meaning what it looks like — the escape from the type system this decision closes, dressed as one.
- *Keeping the set keyed by text and normalising inside `factText`.* Rejected: it is what the engine
  did, and the two representations could disagree.
- *Keying by a hash of the value.* Rejected in D1, and still: an ordering, not a bucket.
- *Computing reasons on demand instead of storing them* — the fact set a plain `Set<Fact>`, with "why"
  a query over the facts, the rules and the history. **Deferred, not rejected**: it would simplify
  `Derive` to a pure fixpoint and make "one reason survives" true by construction, at the cost of
  recomputation on every read of attribution and a batch query for the report. Recorded because the
  shape was considered deliberately rather than passed over.

---

## D34. Nothing the engine stores, keys or identifies is text — supersedes D33's read-path sorts, retires the construct-name check

**Decision**: the three jobs canonical text was still doing are moved off it, leaving rendering as the
only one.

- **The participant index and the pass restriction are keyed by `FactKind`**, a closed union of the fact
  constructs — `Vocabulary.factKind`, `Facts.Source.Kind`, `Derivation.Mentions : FactKind list`. The
  index was `Map<string * Character, Fact list>` keyed by `Canonical.factName`, and a rule's reads were
  a `string list`.
- **A reason is identified structurally.** `Reason` is `{ Rule : string; Premises : Premise list }` over
  unions, so F# structural comparison is the identity; the premises are put in a canonical order *for
  that comparison* (`Facts.reasonKey`), because a source's iteration order is an internal detail and
  cannot be what distinguishes two reasons. `Facts.reasonText`, and the text dedup in `Facts.add` and
  `Engine.reasonsFor`, are gone.
- **A fact list is ordered by its values.** `Facts.toList` and `Facts.ofCharacter` no longer sort: the
  set is already in the values' order, so each is a comprehension.
- **The harness sorts for a reader.** `Harness.report` orders facts by canonical text, ordinally, which
  is where "stable and diffable" (FR-026) belongs; `Harness.shouldDerive` compares facts as a *set*
  rather than as sorted text.

**Rationale**: the construct-name check was the tell, and it is deleted with the machinery that needed
it. It existed to keep a *rendering* unambiguous, which mattered only because a rendering had been made
the identity of a fact — and it was aimed at the one clash F# permits, an event case name equal to a
fact case name, which is the one with no engine-level consequence at all. Event names never reach the
index or the pass restriction; a reason cannot collide across the two unions (rule names are unique
across projections and derivations, and one rule's premises are all events or all facts); and
`Harness.shouldDerive` only ever compared facts. A duplicate *within* the fact union, which would have
merged index buckets, was already unrepresentable. So the invariant defended was a display nicety, and
needing a start-up check to defend it was the symptom of a fact being keyed by its own rendering -
which is the shape N-I's question in this session was pointing at.

**What it costs**:

- A source declares its `Kind` while the index derives one from the fact (`Vocabulary.factKind`), so the
  two have to agree. A source naming the wrong construct finds nothing, which the rule using it reports
  rather than hides — and each keyed source is exercised by a shipped rule (`partnersOf` by jealousy,
  `friendsOf` by threatened, `traitsOf` by bottled-up), so a mismatch fails the suite rather than
  quietly concluding less.
- `Harness.shouldDerive` now compares sets, so a duplicate in an expectation is not a mismatch. The
  assertion was always about which facts hold.
- Premises are identified as a set but *stored* in binding order, so the report still reads against the
  rule that produced it.

**Measured**: no regression and no windfall — rebuild 10,000 events 15 → 13 ms, one further event
0.2 ms, SC-009 62,500 events/s, canonical text render 0.19 µs, all inside the ranges D33 recorded.
Removing a string build per reason comparison was expected to show and did not, which is the third time
in this design that an expected cost turned out not to be where the time was (D7, D30).

**Alternatives considered**:

- *Keeping the name-clash check as a test rather than as a start-up guard.* Rejected: with the index and
  reason identity typed, a test asserting that two sets of case names are disjoint asserts a property of
  a rendering and protects nothing. `Canonical` now contains no reflection at all — only the ordinal
  comparer and the encoder.
- *Keeping `Facts.toList`'s text sort and having the engine report text order.* Rejected: that is the
  engine ordered by a rendering. The report's order is preserved anyway, because the sort moved to the
  one place that has a reader.
- *Deriving a source's `Kind` from its deconstructor rather than declaring it.* Not possible: `namedIn`
  needs the key before it has a fact — that is the lookup. A declared kind is one word per source, and a
  mismatch is a failing rule test rather than a silent one.
- *`FactKind` cases named after the fact cases, unqualified.* Rejected: they would shadow the fact cases
  they name, so the union is `[<RequireQualifiedAccess>]`.
