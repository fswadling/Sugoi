# Specification Quality Checklist: Narrative Core

**Purpose**: confirm the specification is complete, testable, and — the point of
this round — **minimal and satisfiable within its own scope**.

**Created**: 2026-09-16 | **Feature**: `specs/025-narrative-core/spec.md`

## Scope discipline

- [x] **CHK-001** Every acceptance criterion is satisfiable with what the spec
      admits. Checked clause by clause against the deferred list in Assumptions:
      no criterion reasons from something being absent, reads a proposal, or
      depends on a scene, a temporal pattern, or a wild card. *(Governance rule,
      and the failure this round exists to avoid: a prior draft required a
      proposed event to stop being offered once chosen, which silently demanded
      negation.)*
- [x] **CHK-002** Nothing is built ahead of the spec. The deferred list names
      proposals, scenes, temporal rules, negation, wild cards, explanations and
      the open vocabulary, each with the reason it is deferred rather than
      omitted.
- [x] **CHK-003** No capability is left implied. Proposals are named as deferred
      **with the reasoning**, rather than absent and unexplained, so a reader
      cannot mistake their absence for an oversight.
- [x] **CHK-004** The scope boundary is stated positively: the engine's own state
      is history and its interpretation, and nothing above that.

## Requirement quality

- [x] **CHK-005** Every requirement is testable by observation — a history in,
      a set of facts out, or a reported failure.
- [x] **CHK-006** No requirement names a technology, language, module, file, or
      function. The stack lives in the constitution, not the spec.
- [x] **CHK-007** Every requirement either stands on its own or was checked
      again once proposals were removed, rather than carried over on the
      assumption that it still held.
- [x] **CHK-008** FR-014's bound is required to be *reachable deliberately*.
      Without that, SC-006 would assert a failure that a well-formed rule set
      cannot produce, which is the vacuous-criterion failure mode.
- [x] **CHK-009** Nothing survives that names a trigger which cannot occur, and
      every removal is recorded in the Amendments section rather than made
      silently. Two criteria have been through this. FR-019 as first drafted
      required a rule naming an undeclared predicate to be refused, which was
      unrepresentable while a declaration built the pattern a rule used; it was
      restated to the reachable form, and then that form was withdrawn too, once
      a closed vocabulary made a misplaced rule unrepresentable as well. The
      guarantee is stronger for having left the list: Assumptions now states
      that the mistake *cannot be expressed*, which is more than a check the
      engine performs would say. SC-007 as first written — *no engine file
      changed* — went with it, for the same reason.
- [x] **CHK-022** The closure is a decision, not a gap. A unit cannot add a
      construct the engine does not declare; that limit is recorded under
      *Deferred* with the trigger that would reverse it and with what the
      closure buys, so a reader cannot mistake it for an omission.
- [x] **CHK-023** Every acceptance scenario traces to a requirement still in the
      list. US2's third scenario was removed rather than left standing on a
      withdrawn requirement — the traceability half of CHK-009.
- [x] **CHK-024** No requirement is a restatement of another. Checked clause by
      clause: three clauses were found and cut (`FR-016`'s *validated before a
      run*, `FR-020`'s immutability clause, `FR-031`'s authority clause), and
      the remaining near-overlaps each carry content the other does not —
      FR-021 states the observable guarantee where FR-022 states the
      construction that delivers it, and FR-011 governs whether a fact exists
      where FR-012 governs what it is attributed to.

## Success criteria

- [x] **CHK-010** Every criterion is measurable and states its threshold: 100
      runs out of 100, under one second, under ten milliseconds, byte-identical,
      at least 1,000 events per second.
- [x] **CHK-011** SC-007 as first written — *no engine file changed* — is
      withdrawn along with the requirement that a unit could add constructs the
      engine does not declare, because a closed vocabulary makes it unachievable
      by construction rather than by discipline. What those two criteria were
      protecting is now asserted by FR-018 and the renumbered SC-007: a unit's
      conclusions are shared with every other unit, and the concatenation order
      of units is not observable.
- [x] **CHK-012** SC-007 is checkable by permuting the concatenation order of
      units and diffing the report, so modular independence is asserted rather
      than assumed.
- [x] **CHK-013** No criterion depends on wall-clock time except the two
      throughput figures, and those are named as such.

## Consistency with the constitution

- [x] **CHK-014** N-I (history immutable, facts interpretation): FR-002, FR-007,
      FR-011, FR-032.
- [x] **CHK-015** N-II (deterministic by construction): FR-021..FR-024, SC-004.
      *(SC-008 was withdrawn 2026-09-19: with one event per commit there is no
      second delivery mode to compare — see Amendments.)*
- [x] **CHK-016** N-III (modular authorship on a shared substrate): FR-018..FR-020,
      SC-007. The principle's closed-vocabulary clause is carried by the Deferred
      entry and the matching Assumption rather than by a requirement, since it
      states a limit rather than a behaviour.
- [x] **CHK-017** N-IV (attribute every conclusion): FR-010, FR-012, FR-026, and
      the two multi-reason criteria SC-002 and SC-003.
- [x] **CHK-018** N-V (testing is the authoring tool): FR-025..FR-029.
- [x] **CHK-019** I (minimalism): no size ceiling is asserted. A budget is to be
      recorded once a design exists, which is the corrected principle.

- [x] **CHK-020** The spec is consistent with the game's roadmap rather than
      silently contradicting it. The roadmap's story model is *scripted* — phase
      9's cue engine, phase 10's waypoints, triggers, adventure flags and cast —
      and this engine is the *emergent* half. The spec says so in Assumptions,
      states that neither replaces the other, and does not require phase 9 or 10
      to land first. It also declines to settle whether an adventure-flag authoring
      surface survives, leaving that to whichever spec first needs it.
- [x] **CHK-021** The game's constitution is respected where it applies and
      explicitly not invoked where it does not. The engine is a cold path — it
      runs on committed events, not per frame — so the struct and
      allocation-avoidance rules for hot paths do not govern it; Principle V says
      exactly that of cold paths. Its performance is stated as engine-level
      throughput and latency at the game's cast size rather than in terms of
      frame budgets, and how the loop drives it is left to the integration work.

## Open questions carried to `/speckit-clarify`

Disambiguation to resolve before design, not defects in the spec:

- **~~Q1~~ and ~~Q2~~ now moot**: both were about how a construct's declaration
  would carry its name, arity, conclusion family, and which argument positions
  are participants, and how a rule would carry the declarations it was built
  from. There are no declarations to carry. A construct *is* a type, so there is
  nothing to keep in step, no registry, no declaration list, and no
  initialisation order.
- **~~Q3~~ resolved by research D14**: unit-against-unit permutation, which is
  what SC-007 asserts. The one thing order does affect is the work counter —
  counters are not observable output, and no criterion reads one. If that is not
  accepted as stated, it is because the counter's exclusion is asserted rather
  than shown, which is a wording decision, not a design one.
- **~~Q4~~ resolved by research D9**: one evaluation is one completed match of a
  rule's premises; the default is 1,000,000.
- **~~Q5~~ dissolved by closing the vocabulary**: it described a declared
  predicate and an event the engine did not understand sharing a name over
  different participant sets, so the same canonical text was checked or not
  depending on which constructor built it. Canonical text is now derived from a
  construct, so two constructs of different kinds cannot produce it — they are
  different types — and there is no uninterpreted form through which text could
  be supplied in place of a construct. The asymmetry Q5 named has nowhere left to
  arise.
