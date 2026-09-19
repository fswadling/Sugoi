# Feature Specification: Narrative Core — History to Derived Facts

**Feature Branch**: `025-narrative-core`

**Created**: 2026-09-16

**Status**: Draft

**Input**: User description: "Introduce the core narrative engine: interpret the
game's committed events into the social facts they currently imply, attribute
every conclusion to the rule that produced it, and keep each narrative unit's
rules together while the events and facts they reason about stay visible to
every other unit. This is the *emergent* half of the story model; the roadmap's
cues, triggers and adventure flags remain the scripted half."

## Clarifications

### Session 2026-09-16

- Q: When a relationship is socially mutual — two characters are friends, or two
  characters are dating — is the order of the two names in the fact significant,
  or does the engine treat them as interchangeable? → A: Mutual relations are
  declared once in the vocabulary, and the engine treats their participants as
  interchangeable for counting, for matching, and for cancellation.
- Q: Should the running game be able to ask why one particular fact is currently
  true — receiving the rule and the premises behind it — or is that reasoning
  only needed from a headless scenario run? → A: Per fact, from the live engine,
  through the same retrieval that serves facts generally; a run's report is one
  caller of it, not the only one.
- Q: When a batch of committed events arrives between two frames, must the engine
  interpret the whole batch before the next frame is drawn, or may it spread that
  work across several frames and have its facts trail history? → A: Neither is
  settled here. How the game loop drives the engine is integration, so the spec
  states engine-level throughput and latency at the cast size the game uses, and
  leaves frame budgets, batching policy and staleness to the integration work.
  *(The engine has since lost its batch entry point: a commit is one event and a
  caller folds — see Amendments, 2026-09-19.)*
- Q: Should the narrative principles this engine is built around be ratified in
  this repo's constitution, or should the spec carry them itself? → A: Ratified
  in the game's constitution as a section scoped to the narrative subsystem, with
  two principles corrected in transit — modular authorship no longer claims a
  unit adds vocabulary without editing the engine, and attribution now records
  that a fact's reasons are retrievable while the game is running.
- Q: When the vocabulary changes and an event in an already-saved history no
  longer fits any construct, should the engine keep that event as one it does not
  understand, or refuse to load the history? → A: Neither — unrecognised events
  crash. There is no accepted-but-uninterpreted form. Saved events are serialised,
  so a history must still deserialise after constructs are added, because adding
  one changes none of the others; a construct that is removed or reshaped fails
  loudly instead. *(The persistence half of this answer is withdrawn — see
  Amendments, 2026-09-19. What survives is the stronger claim: an event the
  vocabulary does not have cannot be built at all.)*

## User Scenarios & Testing *(mandatory)*

Three parties use this: the **author**, who writes rules and runs scenarios to
develop them; the **simulation**, which supplies committed events and reads back
what is currently true; and the **director**, the player, who inserts events of
their own. When the game is absent the **harness** takes the simulation's place
and the scenario script takes the director's.

In this game, the simulation is the systems that make things happen — cue
sequences executing, triggers firing, schedules being followed — and the
director is the player acting through the editor and in play mode. Both of those
reach the engine the same way: as committed events.

### User Story 1 - See what a situation currently implies (Priority: P1)

The author describes a starting situation as a history of committed events — who
is friends with whom, who likes whom, who started dating — and the engine reports
what is currently true as a consequence. Conclusions that follow from other
conclusions are reported too, so a long chain of reasoning is written as several
small rules rather than one unwieldy rule. Each conclusion says which rule
produced it and which premises that rule applied to.

**Why this priority**: it is the engine's reason to exist, and every later
capability consumes it. It is independently valuable — the author can inspect a
social situation with nothing else built — and it is independently testable,
because a hand-written history and an expected set of facts is the whole test.

**Independent Test**: load a hand-written scenario of committed events, request
the derived facts, and assert the exact set that comes back including the
indirect conclusions, each carrying its rule and premises.

**Acceptance Scenarios**:

1. **Given** Aiko likes Shinji and Shinji is in a relationship with Akane,
   **When** the derived facts are requested, **Then** Aiko is reported as
   jealous of Akane.
2. **Given** Aiko is jealous of Akane and Aiko and Akane are friends, **When**
   the derived facts are requested, **Then** Aiko's friendship with Akane is
   reported as threatened.
3. **Given** Aiko's friendship with Akane is threatened and Aiko is shy, **When**
   the derived facts are requested, **Then** Aiko bottling up her feelings
   towards Akane is reported.
4. **Given** a fact that is already current, **When** it would be derived again,
   **Then** it is reported exactly once and causes no further derivation.
5. **Given** unchanged history and rules, **When** the derived facts are
   requested twice, **Then** the two results are identical, including their
   order.
6. **Given** a fact reached by two independent chains of reasoning, **When** the
   derived facts are requested, **Then** it is reported once and both rules are
   reported with it.
7. **Given** a crush that ends, **When** the derived facts are requested,
   **Then** the conclusions that depended on it are no longer reported, while
   history still shows both the beginning and the end.

---

### User Story 2 - Keep a narrative unit's rules in one place (Priority: P2)

A narrative unit is a body of rules — one thread of story: romance, rivalry,
clubs, transfers. It owns those rules and nothing else. The vocabulary of events
and facts is single and shared, as is the set of facts the engine holds, so any
rule may read anything another unit concluded and what this unit concludes is
available to them in turn. Where a unit's rules sit relative to the others'
changes nothing about the result.

**Why this priority**: the engine accumulates rules over the life of the project.
If a unit's reasoning were scattered through the engine, or if the result
depended on where its rules happened to sit, units could not be developed,
reviewed, or removed independently. It is second rather than first only because
it needs something for there to be a unit *of*.

**Independent Test**: add a unit whose rule concludes a fact from a fact another
unit concluded, run a scenario that triggers it, and assert the conclusion
appears; then assemble the rules in the opposite order and assert identical
output.

**Acceptance Scenarios**:

1. **Given** two units, **When** a rule in one reads a fact the other concluded,
   **Then** it sees it, because nothing scopes facts to a unit.
2. **Given** a rule set assembled by concatenating units, **When** the same units
   are concatenated in a different order, **Then** every observable output is
   identical.

---

### User Story 3 - Develop rules by running scenarios headlessly (Priority: P3)

The author writes a scenario — an initial history and a sequence of events — and
runs it with no game at all, getting back the history and the derived facts in a
form that can be diffed and asserted, each fact carrying the rule that produced
it and the premises that rule applied to. Scenarios can be parameterised over
traits and generated from a seed, so one situation is checked across many casts
in a single run.

**Why this priority**: it is the author's development loop and what makes the
earlier stories safe to change. It is last only because it needs something to
run.

**Independent Test**: run the scenario suite from the command line with no
graphical interface, assert every expected fact and that each carries its rule,
and assert a generated cast replays identically from its seed.

**Acceptance Scenarios**:

1. **Given** a scenario, **When** it runs, **Then** the history and the facts are
   reported in a stable, diffable form, each fact with the rule that produced
   it.
2. **Given** a scenario parameterised over a trait, **When** it runs with each
   value, **Then** each outcome is reported and assertable separately.
3. **Given** an unexpected fact in a run, **When** the run is inspected, **Then**
   the rule that produced it and the premises it applied to are reported, so it
   can be traced without re-running anything.
4. **Given** the history a run produced, **When** the current rule set is run over
   it again, **Then** the rebuilt state is a function of that history and those
   rules, and no committed event is revisited or changed.
5. **Given** the same scenario run one hundred times, **Then** every run is
   identical.
6. **Given** a failure in a scenario assertion, **Then** the failure reports the
   whole state rather than a bare comparison.

### Edge Cases

- **A fact stops being true**: a crush ends or a friendship ends; conclusions
  that depended on it stop being reported, while history keeps both the
  beginning and the end.
- **One reason survives**: a conclusion has two independent reasons and one
  reason's premises stop holding; the conclusion stays reported and the vanished
  reason stops being offered.
- **Repeated on and off**: friendship, then ended, then friendship again; only
  the current state is reported, not a history of the states in between. Where
  the construct declares its participants interchangeable, the ending is paired
  with the beginning however the two names happened to be ordered.
- **An event no rule mentions**: the world supplies an event the rules do not
  match. It enters history and concludes nothing; this is not a failure.
- **An event the vocabulary does not have**: it cannot arise. Events are values of a
  closed union, so one the engine does not have cannot be built, and nothing arrives as
  text that could carry one.
- **An event naming a character who has not arrived**: accepted. Naming a
  character is always admissible, so it concludes whatever the rules project from
  it rather than being reported; the engine keeps no record of who has arrived
  (Amendments, 2026-09-19).
- **Same timestamp**: several events share a game time; their relative order
  must be defined and repeatable.
- **Events arriving close together**: several events are committed one after
  another, from storylines that do not touch. Conclusions must reflect the order
  the caller committed them in and must not vary with how the caller's fold
  happened to group them.
- **Empty history**: no facts, no failure.
- **Rules changed**: history is untouched and the rebuilt state may differ; a
  previously committed event is never revisited.

## Requirements *(mandatory)*

### Functional Requirements

**Committed events and history**

- **FR-001**: Committed events MUST be recorded in an append-only, ordered
  history, each carrying a game time.
- **FR-002**: A committed event MUST never be modified, removed, or reordered,
  whether by later events or by a change of rules.
- **FR-003**: The initial social setup MUST be expressible as pre-existing
  committed events, so that no separate mutable store of starting facts exists.
- **FR-004**: *(Withdrawn 2026-09-19: the engine neither persists nor restores history; persisting a game is the game's business. See Amendments.)*
- **FR-005**: History MUST grow by admitting events supplied from outside the
  rules — seed events, events from the world, and events the director inserts.
  The engine MUST NOT behave differently according to who supplied an event or
  why. The one event it refuses is one whose game time goes backwards, which is a
  matter of order rather than of who supplied the event (FR-024).
- **FR-006**: An event that no rule matches MUST be accepted and MUST conclude
  nothing. An event the vocabulary does not have MUST be refused by construction:
  events are values of a closed union, so one the vocabulary lacks cannot be
  built, and nothing arrives as text that could carry one.

**Derived facts**

- **FR-007**: Every fact MUST be derived from committed history, and MUST NOT be
  writable by any other means.
- **FR-008**: Facts whose premises are themselves facts MUST be supported, and
  derivation MUST continue until no further fact and no further reason can be
  found.
- **FR-009**: A fact MUST be reported once however many reasons support it, and
  concluding a fact that is already current MUST NOT cause further derivation.
  Where a construct declares its participants interchangeable, the order they
  are given in MUST NOT distinguish anything about it — not when facts are
  counted, not when a rule's premise is matched, and not when one event undoes
  another.
- **FR-010**: Every distinct reason for a fact MUST be retained, where a reason
  is a rule together with the specific premises that rule applied to. Identical
  reasons MUST NOT be retained twice. A fact's reasons MUST be retrievable while
  the game is running, so that a run's report is one caller of that retrieval
  rather than the only route to it.
- **FR-011**: A fact MUST cease to be reported only when none of its reasons
  still hold, and this MUST NOT change or remove any committed event.
- **FR-012**: A reason whose premises no longer hold MUST NOT be held to support
  a conclusion, so that what a conclusion is attributed to always reflects the
  current situation.
- **FR-013**: Observations MUST be consistent with the committed history at the
  point they are made, whatever route grew that history. Each commit is one
  event and is whole or nothing: an event whose game time goes backwards MUST be
  raised as a failure naming it, having changed nothing. A run is a caller-side
  fold of commits, so a refused event leaves the events before it committed and
  nothing after it.
- **FR-014**: Derivation MUST be bounded, and exhausting that bound MUST be
  raised as a failure naming the rules involved rather than hanging or being
  silently tolerated. The bound MUST be reachable deliberately, so that the
  failure path can be exercised.
- **FR-015**: The derived facts MUST be retrievable as a whole, and the facts
  true of a given cast member MUST be retrievable together — every fact naming
  them, in any position. Retrieval MUST NOT filter: what is withheld from a
  player or a character is the caller's decision, not the engine's.

**Narrative units and rules**

- **FR-016**: Rules MUST be authored as source code rather than serialised data.
- **FR-017**: Rules MUST be authorable in two separate categories — history to
  facts, and facts to facts — and each rule MUST belong to exactly one.
- **FR-018**: Everything a unit concludes MUST be available to every other unit
  and to the game. Nothing MUST scope facts to a unit, a rule, an interaction, or
  a subset of the cast.
- **FR-019**: Rule order MUST NOT be observable: a rule set assembled by
  concatenating units MUST produce identical output in any concatenation order.
- **FR-020**: All current state MUST be rebuildable from unchanged history under
  a replaced rule set.

**Determinism**

- **FR-021**: Given identical history and identical rules, all observable output
  MUST be identical across runs, including ordering, including the set of
  reasons retained for each fact, and including the order in which those reasons
  are presented.
- **FR-022**: No observable output may depend on the iteration order of
  unordered collections, on hashing, on arrival timing, or on ambient state such
  as wall-clock time or frame cadence.
- **FR-023**: Where several events share a game time, their relative order MUST
  be defined and repeatable.
- **FR-024**: The order in which events take effect MUST be determined by data
  supplied with them by the caller, and never by the moment they reach the
  engine, so that a stream delivered in uneven bursts produces the same history
  as the same events delivered smoothly.

**Authoring harness**

- **FR-025**: The engine MUST be usable with no graphical interface, driven
  entirely by a scenario description.
- **FR-026**: A run MUST report history and facts, and for each fact the rule
  that produced it and the premises that rule applied to, in a stable form
  suitable for diffing and assertion.
- **FR-027**: Scenarios MUST support parameterisation and generation, so one
  situation can be exercised across varying traits and event orders in a single
  run.
- **FR-028**: A failure MUST identify the rule, event, or unit responsible, and
  a failed scenario assertion MUST report the whole state rather than a bare
  comparison.
- **FR-029**: The harness MUST drive the engine one event at a time, folding a
  scenario's events in order. *(Restated 2026-09-19: the engine commits one event
  per call, so there is no batch mode to drive. See Amendments.)*

**Scope**

- **FR-030**: The engine MUST be correct at a cast of at most 20 characters, and
  MUST NOT depend on mechanisms that only pay off at a much larger scale.
- **FR-031**: The engine MUST interpret and never decide. It is told what
  happened; it MUST NOT schedule, pace, order, or advise on what should happen
  next.
- **FR-032**: The interface MUST offer no way to assert a fact and no way to
  modify, remove, or reorder a committed event. These are absent rather than
  discouraged.

### Key Entities

- **Committed event**: an immutable historical occurrence carrying a game time
  and its participants; the only source of historical truth.
- **Derived fact**: something currently true as interpreted from history; may
  appear and disappear; carries every reason that currently supports it.
- **Interchangeable participants**: a property a construct may declare, making
  the order of its names insignificant — so two orders are one fact, either
  order satisfies a premise, and an event that undoes another pairs with it
  whichever way each lists the names. It is declared with the vocabulary, so
  making a relation mutual is an edit of the same kind as adding a construct.
- **Reason**: one rule together with the specific premises that rule applied to.
  A fact may have several, an identical one is never retained twice, and the set
  is retrievable for the fact it supports while the game is running. Expanding
  reasons into a readable explanation is not part of this feature.
- **Narrative unit**: a thread of story — romance, rivalry, a club — whose rules
  live together and are authored together. It reads the whole shared substrate
  and contributes its conclusions to it.
- **Rule**: one authored step of reasoning, in exactly one of two categories
  (history to facts, facts to facts).
- **Cast member**: a member of the engine's closed roster of twenty `Character`
  cases (research D28). Entering the story is recorded by an `Arrived` event, which
  gates nothing: any event may name any roster member.
- **Scenario**: an initial history and a sequence of events; the harness's unit of
  work. A scenario that wants a character present from the start names her
  `Arrived` event in that initial history.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The headline situation — Aiko is shy, Aiko and Akane are friends,
  Aiko likes Shinji, Shinji likes Akane and is dating her — reports those five
  facts as the events state them, and adds exactly three conclusions to them:
  jealousy of Akane, a threatened friendship with Akane, and bottled-up feelings
  towards Akane. None of the three appears in history.
- **SC-002**: A conclusion supported by two independent chains is reported once,
  and both rules that support it are reported with it.
- **SC-003**: When one of two reasons for a conclusion ceases to hold, the
  conclusion is still reported, with exactly the surviving reason.
- **SC-004**: Repeating any scenario one hundred times yields identical
  observable results in one hundred runs out of one hundred.
- **SC-005**: Rebuilding state from a history of 10,000 events completes in
  under one second, and processing one further event takes under ten
  milliseconds, at a cast of 20.
- **SC-006**: Exhausting the derivation bound is raised as a failure naming
  the rules involved, in under a second, rather than hanging — and the bound can
  be reached deliberately, so this is asserted rather than assumed.
- **SC-007**: A rule set assembled from narrative units produces byte-identical
  output in every concatenation order of those units.
- **SC-008**: *(Withdrawn 2026-09-19: a commit is one event, so there is no second
  delivery mode to compare and the criterion compares a run against itself. FR-023
  and FR-024 still state the ordering it was built on. See Amendments.)*
- **SC-009**: With all 20 cast members active at once, the engine sustains at
  least 1,000 committed events per second, measured as the rate at which it
  accepts committed events and its facts stay current with them.

## Assumptions

### Deferred, and why

- **Candidate events — what could happen next — are not in this feature.** They
  are deferred to a following spec. The engine's own state is therefore history
  and its interpretation, and nothing above that. This is deliberate: proposals
  are additive, because a proposal is a rule whose conclusion is a possibility
  rather than a fact and so reuses the whole evaluator; and deferring them means
  the vocabulary and modularity questions are settled first, without the
  pressure of an acceptance criterion that needs a capability this spec does not
  have.
- **Scenes**, interaction containment and scene-local facts: deferred. One
  global set of events and facts is sufficient at this stage.
- **Temporal rules** — patterns across the event stream, such as "witnessed
  flirting, then avoided within thirty minutes": deferred.
- **Negation** — reasoning from something *not* being true: deferred. No
  acceptance criterion in this spec requires it, which is a deliberate
  consequence of deferring proposals: "and this has not happened yet" is the
  shape that needs it.
- **Wild cards** — authored inserts such as a new transfer student: deferred.
  The director's route into history is supplying events, which this spec
  covers.
- **Explanations** as opposed to attribution: deferred. Reasons are recorded,
  never expanded into prose.
- **An open vocabulary** — a unit adding an event, a fact, or a trait the engine
  does not declare — is deferred, with a recorded trigger: *a second narrative
  unit wants a construct the vocabulary lacks*. The vocabulary is therefore
  closed in this feature, and adding a construct means adding it in one shared
  place.
  That is the price of what the closure buys: the host language checks a rule's
  category and construct kind, and the engine derives each construct's name, its
  argument count, and its argument types rather than maintaining those in
  parallel by hand. Opening it later is expected to change how a rule's pattern
  is written; it should not change how patterns are matched, attributed, or
  derived, because the engine already works in terms of a name and its terms.

### Given

- **The roster is fixed; the story is not.** A participant is any member of the
  engine's closed roster of twenty (research D28). Naming a character is always
  admissible: an `Arrived` event records that one entered the story and gates
  nothing, so a character can be in the roster long before she walks into it, and
  an event about somebody who has not arrived is accepted rather than reported
  (Amendments, 2026-09-19).
- **Traits arrive as events**, never as fields on a cast member, so there is no
  second source of truth for something history already records.
- **Game time only.** The engine has no clock of its own; time is data supplied
  by the caller with each event.
- **No world model.** Locations, schedules, and movement are the game's, so no
  rule can depend on two characters being in the same place.
- **No numeric meters.** Social state is discrete events, facts, and rules, not
  scores. No weights, thresholds, or decay.
- **No decay in time.** A derived fact holds until its reasons cease to hold,
  however much game time passes.
- **No belief or knowledge model.** Perception enters as events; who saw what,
  who knows what, and concealment are not derived.
- **The vocabulary is closed and shared.** One event type, one fact type, and
  one trait type, declared with the engine; every unit reasons over those, and
  nothing is scoped to a unit. What that costs, and the trigger for revisiting
  it, are recorded under *Deferred* above.
- **Mutual relations are declared, never inferred.** A construct says whether its
  participants are interchangeable, and that declaration belongs to the closed
  vocabulary, so making a relation mutual is an edit of the same kind as adding
  a construct and is covered by the same deferred trigger. Nothing decides
  mutuality from a name, and no construct is mutual by default.
- **Boundary of this subsystem.** It is a library consumed in-process by the
  game. Canonical text is an output — the key facts are stored and ordered by, and
  what the report renders — and never a save format or a transport between
  processes. Persisting a game belongs to the game, which holds the events it
  supplied.
- **How the game drives the engine is not settled here.** Frame budgets, how much
  work a caller does between frames, and what the player is shown are integration
  decisions. This spec fixes what the engine does — one event per commit — and how
  fast it must be at the cast size the game uses; wiring it into the loop belongs
  to whichever spec does that.
- **The derivation bound cannot be reached by a well-formed rule set** at this
  cast size, because a finite vocabulary and a bounded cast make both the fact
  set and the set of reasons finite. It is kept as a raised failure because
  failing loudly is cheaper than reasoning about whether it is needed, and it is
  required to be reachable deliberately so that the failure path is tested
  rather than assumed.
- **A misplaced rule is refused before it can run, by construction.** A rule is
  an expression in the host language over the event and fact types, so a
  derivation premised on an event, or a rule concluding an event, does not
  type-check, and a rule cannot name a construct the vocabulary does not have.
  No requirement is stated for this, because it is a property of the approach
  rather than a check the engine performs: the guarantee is that the mistake
  cannot be expressed, not that something detects it afterwards.
- **There is no uninterpreted event, and none is needed.** Every event is a case of
  the closed event type, so an event the vocabulary does not have cannot be built: the
  mistake is unrepresentable rather than reported, which is stronger than the refusal it
  replaces. That is also why there is no decoder — nothing arrives as text, so there is
  nothing to fail on, and nothing that could not be read back.

### Relationship to the roadmap's scripted story model

- **Cues, triggers and adventure flags are the scripted half, and this spec
  leaves them unchanged.** Phase 9's cue engine runs authored sequences; phase
  10's waypoints and triggers decide when one fires. The engine specified here is
  the *emergent* half: it reads what happened and derives what is now socially
  true, which a scripted sequence cannot express. Neither replaces the other,
  and nothing here requires phase 9 or 10 to be finished first.
- **The same occurrences feed both.** A cue step executing, a trigger firing, a
  character following a schedule, and the player acting all arrive as committed
  events. The engine must not know or care which produced one — that is what
  FR-005 requires.
- **Adventure flags are subsumed rather than duplicated.** A flag recording
  *that something happened* is a committed event; a flag recording *that
  something is now true* is a derived fact. Whether any flag surface remains for
  authoring, and how a cue reads a derived fact as a condition, is deliberately
  left to whichever spec first needs it. This spec must not be blocked on that
  question, and must not quietly settle it either.
- **The engine owns no visual identity.** The people who can exist at all are the engine's,
  as a closed roster of twenty (research D28): a misspelled character does not compile, and a
  rule that matches on a character matches all of them. An `Arrived` event records that one
  entered the story, but it gates nothing: any event may name any roster member, and one about
  somebody who has not arrived is accepted rather than reported (Amendments, 2026-09-19). The
  game's `world/Cast.fs` maps a roster case to an asset id for rendering. That mapping is the
  game's business and the engine never sees it.
- **This is a cold path.** The engine runs on committed events, not per frame, so
  the constitution's hot-path rules — structs, contiguous arrays, avoiding
  per-frame allocation — do not govern it: clarity first, then measure, exactly
  as Principle V says of cold paths. It must nevertheless keep up with the events
  the game produces, which is why SC-005 and SC-009 state throughput and latency
  at the cast size the game uses, rather than tying any of it to a frame
  arrangement.

## Amendments

FR and SC numbers in the entries below are as they stood when the entry was
written, not as they stand now. The list has been renumbered twice since, once
within a single day, so a number in an earlier entry can name a different
requirement today. The list above is current.

- **2026-09-19**: **The batch entry point is removed; a commit is one event.** `Engine.commit`
  and `History.append` now take a single `(GameTime * Event)`, and committing a run is a
  caller-side fold — `List.fold Engine.commit` — which is what the harness does over a
  scenario's initial events and steps. The batch existed to run the derivation fixpoint once
  per burst rather than once per event, and it is withdrawn as a premature optimisation: the
  usage is one event at a time, and bursty delivery was speculative. The cost is measured and
  stated rather than hidden: the fixpoint now runs per event, and the SC-009 sustained figure
  fell from ~59,000–62,500 events/s to ~6,410/s, still far above its 1,000/s threshold; SC-005's
  rebuild of 10,000 events is unchanged at ~9 ms, because most of those events re-establish
  facts already present, so derivation is skipped for them. Atomicity changed with it: a commit
  is whole or nothing *per event*, not per run, so a refused event leaves the events before it
  committed and nothing after it. FR-013 is restated in that form, FR-029 is restated to
  single-event driving, and SC-008 is withdrawn — with no second delivery mode it compared a
  run against itself.

- **2026-09-19**: **Commit failures are raised, not returned.** `CommitError` and its
  `CommitError.message` module are gone. A game time that goes backwards is now an F#
  exception, `TimeWentBackwards`, carrying the same message text; `History.append` returns
  `State` and `Engine.commit` returns `Engine`, with `Harness.run` and `runBounded` returning
  `RunResult` directly. Once the declaration check went (see the entry below) only one failure
  case remained, so the returned `Result` was ceremony: a backwards time is not data the engine
  can act on, because the caller supplies the order (FR-024), which is the same posture
  `DerivationBoundExceeded` already had. Only those two failures are raised now, and the
  distinction between them stands — a backwards time is a caller defect, an exhausted bound an
  authoring one. Raising is safe for a caller because `commit` does not mutate the value passed
  in: a commit that raises has changed nothing, so the caller still holds a usable engine
  (FR-013). FR-028's *every failure names what it was about* survives as the exception's
  `Message`. FR-013 and FR-005 now say a backwards-time event is raised rather than returned.
  *(Superseded in part by the entry above: there is no batch entry point now.)*

- **2026-09-19**: **Arrival is decoupled from admissibility.** `CharacterDeclared` was a
  gate: the engine held the set of characters declared so far and refused, as
  `CharacterNotDeclared`, any event naming one who was not in it. Once the roster closed
  (research D28) the check no longer caught a name that does not exist — the type does that —
  and its only remaining job was to make FR-006's cast-membership clause reachable, so it had
  begun to add a gate the model does not need. The gate is removed. The event is now `Arrived`:
  narrative content like any other event, recording that a character entered the story rather
  than permitting it, and a rule may project it the way it projects any other event. The engine
  holds no cast set and has no `CharacterNotDeclared`, and FR-006 is restated rather than
  withdrawn: an event no rule matches is still accepted and concludes nothing, and an event the
  vocabulary does not have is still refused by construction, because events are values of a
  closed union — the cast-membership clause is gone with the check it described. A scenario
  that wants a character present from the start names her `Arrived` event in its initial
  events; the harness has no cast field and no declare step. The trade-off is recorded plainly:
  the engine no longer reports an event about a character who has not arrived, so an event
  naming any roster member is admissible and concludes whatever the rules project from it.
  `Arrived` remains as the event a unit can project to react to an arrival — projections read
  events and derivations read facts — so an arrival is content a unit reads rather than
  bookkeeping the engine maintains.

- **2026-09-16**: FR-019 restated, and FR-018 widened, during Phase 1 design
  rather than after it. FR-019 as first written required a rule naming an
  undeclared predicate to be refused; design established that such a rule is
  unrepresentable, because a predicate's declaration is what builds the pattern
  it appears in. A criterion whose trigger cannot occur is the failure mode the
  governance rule exists to catch, so it was replaced with the reachable form —
  a construct of the wrong kind — which names the rule and where the construct
  belongs. FR-018 gained "of whatever shape" for the same reason: had
  declarations covered only fixed arities, a unit introducing an unusual one
  would have needed an engine edit, which is what FR-018 forbids. *Superseded by
  the entry below, which withdrew that FR-018 and SC-007 and renumbered the
  list.*
- **2026-09-16**: SC-001 restated. It read as claiming the situation reports
  three facts; the five the events state directly are reported too, unfiltered,
  by FR-015. It now says exactly three conclusions are added to those five.
- **2026-09-16**: **The vocabulary is closed.** FR-018 and SC-007, as they then
  stood, required a narrative unit to add events and facts the engine does not
  declare and to do so without an engine edit. Both are withdrawn. What that
  required was a declaration mechanism, a predicate identity that was not a
  type, and constructs whose names and arities no compiler could check — so an
  author's mistake was caught by a scenario, if at all, rather than at build
  time. With one event type and one fact type the host language checks a rule's
  category and construct kind, and the engine derives each construct's name,
  arity, and argument types instead of four parallel lists kept in step by hand.
  The guarantee that a misplaced rule is refused before a run was restated as
  FR-018, scoped to authoring time, and US2 was re-scoped from *without touching
  the engine* to *in one place*: it keeps what remains true — a unit's rules
  together, its conclusions shared, order not observable — and drops the part the
  closure makes false. *Superseded on both counts by the entry below.*
  FR-020..FR-034 and SC-008..SC-010 were renumbered to close the
  gap, and the open vocabulary moved to the deferred list with a recorded
  trigger. Q5 is dissolved by the same change: canonical text is derived from a
  construct, so two constructs of different kinds cannot produce it — they are
  different types — and nothing can supply text in place of a construct at all,
  since there is no uninterpreted form.
- **2026-09-16**: **Extraneous requirements removed, and one contradiction
  settled.** FR-018 as it then stood — the misplaced-rule requirement — is
  withdrawn: with the vocabulary closed, a rule that
  concludes an event or is premised on one does not compile, so the requirement
  named no behaviour the engine has — that the mistake cannot be *expressed* is
  now recorded in Assumptions as a property of the approach, which is a stronger
  statement than the one it replaces. US2's third acceptance scenario went with
  it, because it tested the same unrepresentable mistake and could only have
  been exercised by a deliberately failing build. Three clauses were cut as
  restatements of requirements already stated: the second clause of FR-016,
  *validated before a run*, which is compilation; the first clause of FR-020,
  *replacing the rule set MUST NOT alter committed history*, which FR-002
  already covers, including its "by a change of rules"; and the second clause of
  FR-031, *behave identically whichever authority supplies events*, which was
  FR-005 verbatim. FR-030 required correctness at a cast of *fewer than 20*
  while SC-005 and SC-009 measure at 20, so the criteria tested one above the
  stated range; it now says *at most 20*. The list is renumbered to close the
  gap and stands at 32 requirements.
- **2026-09-16**: **An unrecognised event is a failure, and there is no
  uninterpreted form.** FR-006 left a gap: it said an event no rule matches is
  accepted, and a malformed one is reported, but a construct the vocabulary does
  not have fell between the two. The design had carried an *uninterpreted* event
  for it, which entered history, matched nothing, and named no participants.
  That is withdrawn, so every event the engine holds is a construct the
  vocabulary has. FR-004 gained the other half: a history saved before a
  construct was added MUST still restore once it is, which holds because adding a
  construct changes none of the existing ones. Removing or reshaping a construct
  is therefore a breaking change to saved history, and is meant to fail loudly
  rather than leave events nothing can interpret.
- **2026-09-16**: **Terminology normalised.** The spec had used *derived fact*
  and *current fact* interchangeably, and *ruleset* alongside *rule set*.
  *Derived fact* is now the single term everywhere, including the title and the
  requirement group heading, because the name states a fact's provenance, which
  is what attribution depends on; FR-007 was reworded to *every fact* rather than
  carry a clause that would have read as *derived facts must be derived*. *Rule
  set* is two words throughout, so it stays prose rather than pre-empting a code
  identifier. *Module* became *unit* in US2 and in the input description, so the
  spec uses its own term and no acceptance criterion appears to name a source
  module. No requirement changed meaning.
- **2026-09-16**: **US2 restated, and FR-018's event clause dropped.** US2 said
  *the events and facts those rules reason about are visible to every other
  unit*, which described a world where a unit owned events and facts and they
  had to be shared — a leftover from the open vocabulary, and the last place in
  the spec still implying it. A unit now owns rules and nothing else: one
  vocabulary and one set of facts are shared by construction, and that is what
  US2 says. FR-018's clause forbidding events from being scoped to a unit went
  with it, on the same reasoning that removed the misplaced-rule requirement:
  with a single event type there is no mechanism that could scope them, so the
  clause named nothing the engine does. Its fact clause is unchanged, because
  facts could plainly be scoped per unit and that is what it forbids.
- **2026-09-19**: **The roster is closed.** `Character` was a newtype over a `string`; it is
  now a union whose cases are the twenty people who can exist. The cast does not change, so
  the type that names its members does not have to admit names it does not have: a
  misspelled character is a compile error rather than an event about somebody who does not
  exist, matching on a character is exhaustive, and no part of a character's identity is
  decided by a string comparison. Being in the roster is not the same as being in the story —
  a `CharacterDeclared` event still decides who is in play, so FR-006 and the cast that grows
  during a run are unchanged — and visual identity is still the game's, not the engine's. The
  two consequences a reader needs are recorded in research D28: a saved history names a
  character by its case name, so renaming a case invalidates histories saved under the old
  name, and adding one invalidates nothing. *Superseded in part by the entry above: arrival was
  decoupled from admissibility, so `CharacterDeclared` — now `Arrived` — no longer decides who
  is in play, and the cast that grows during a run is gone with it.*
- **2026-09-19**: **FR-004 withdrawn: the engine does not persist history.** The requirement asked
  that history be persistable and restorable, and that one saved before a construct was added
  still restore. It is withdrawn at the user's direction — persisting a game is the game's
  business, and the game is what holds the events it supplied. Removing it took three things with
  it, and each is a simplification rather than a loss:

  - **The decoder.** Text-to-constructor existed only to read a saved history back, so `Canonical`
    now only *writes* text and the engine contains no decoder at all. That removes the one path by
    which the engine could meet something it does not have.
  - **An unrecognised event is now unrepresentable, not reported.** Events are values of a closed
    union, so there is no way to build one the vocabulary lacks. The edge case above and the
    assumption below have been rewritten in that stronger form.
  - **Renaming a character is now safe.** Nothing reads a name back, so no saved history can be
    invalidated by a rename; the caveat recorded in research D28 no longer applies, and neither
    does the one about reshaping a construct breaking saved history.

  Three other artefacts changed to stay true: a user scenario that wrote history out and loaded it
  back now states that replaying the current rule set over that history rebuilds the state; an
  edge case about an event the vocabulary does not have is dissolved; and the boundary assumption
  now says canonical text is an output rather than the persistence format. The clarification above
  carries a pointer to this entry, because its answer described saved histories.

  **The list is deliberately not renumbered this time.** Withdrawing FR-018 renumbered it (see
  above), because a gap mid-list is a trap for a reader following cross-references. This gap is
  recorded in the list itself and in this entry, and renumbering would touch every reference in
  `plan.md`, `tasks.md`, `data-model.md`, `contracts/engine-api.md` and `quickstart.md` — more
  places to get wrong than the gap is worth. The judgement is reversible: renumbering is a
  mechanical pass whenever it is wanted.
