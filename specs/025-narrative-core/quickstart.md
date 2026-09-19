# Quickstart: Narrative Core

**Feature**: `025-narrative-core` | **Date**: 2026-09-16 | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

How to prove this feature works, without the game. Everything here runs headless.

---

## Prerequisites

- .NET 10 SDK (`dotnet --version` reports `10.x`).
- Nothing else. The engine's files open `System` and `Microsoft.FSharp.Reflection` —
  nothing from the game or its packages — and no test in this feature initialises SDL3,
  Jolt or the renderer.
  There is no `narrative/Narrative.fsproj` to build: an engine-and-units library was tried
  and dropped (research D12), so `narrative/` is a source folder inside
  `fsharp-game.fsproj`, listed first. What keeps it free of the game is that ordering
  rather than an assembly boundary — F# compiles in list order, so the engine's files
  cannot name anything the game defines, graphics or otherwise.

## Build and run

```sh
dotnet build fsharp-game.fsproj                  # the engine, in the game's assembly
dotnet test  test/fsharp-game.Tests.fsproj       # everything, including the engine
dotnet test  test/fsharp-game.Tests.fsproj --filter "FullyQualifiedName~Narrative"
```

There is no build that produces the engine on its own — it is compiled into the game's
assembly, not into one of its own. Building the test project builds it either way.

---

## 1. The headline situation

This is SC-001, and one test carries it verbatim.

**Initial history** — the starting situation as pre-existing events (FR-003), each
character's arrival recorded by an `Arrived` event. These are the events a scenario commits
as its starting history, held in memory and not read from a saved file:

```
-1000 Arrived(Aiko)
-1000 Arrived(Akane)
-1000 Arrived(Shinji)
-900 TraitAssigned(Aiko, Shy)
-800 FriendshipStarted(Aiko, Akane)
-300 CrushStarted(Aiko, Shinji)
-200 CrushStarted(Shinji, Akane)
-100 RelationshipStarted(Shinji, Akane)
```

**Expected `report`** — history in commit order, facts in canonical order:

```
history
    1  -1000  Arrived(Aiko)
    2  -1000  Arrived(Akane)
    3  -1000  Arrived(Shinji)
    4  -900  TraitAssigned(Aiko, Shy)
    5  -800  FriendshipStarted(Aiko, Akane)
    6  -300  CrushStarted(Aiko, Shinji)
    7  -200  CrushStarted(Shinji, Akane)
    8  -100  RelationshipStarted(Shinji, Akane)

facts
    AttractedTo(Aiko, Shinji)
        projected by "crush-started" from CrushStarted(Aiko, Shinji)
    AttractedTo(Shinji, Akane)
        projected by "crush-started" from CrushStarted(Shinji, Akane)
    BottledUpFeelings(Aiko, Akane)
        inferred by "bottled-up" from FriendshipThreatened(Aiko, Akane), HasTrait(Aiko, Shy)
    Friends(Aiko, Akane)
        projected by "befriended" from FriendshipStarted(Aiko, Akane)
    FriendshipThreatened(Aiko, Akane)
        inferred by "threatened" from JealousOf(Aiko, Akane), Friends(Aiko, Akane)
    HasTrait(Aiko, Shy)
        projected by "trait-assigned" from TraitAssigned(Aiko, Shy)
    InRelationship(Akane, Shinji)
        projected by "together" from RelationshipStarted(Shinji, Akane)
    JealousOf(Aiko, Akane)
        inferred by "jealousy" from AttractedTo(Aiko, Shinji), InRelationship(Akane, Shinji)
```

Two things about that block are worth reading twice. The *event* line keeps
`RelationshipStarted(Shinji, Akane)`, because history records what happened including who
acted. The *fact* line reads `InRelationship(Akane, Shinji)`, because a fact's
participants may be declared interchangeable (research D22) and its canonical text is then
its participants in the roster's order, not the order the event named them in. The declaration
is on the facts, never on the events that project them — which is why the two lines here
disagree, and why which rule happened to conclude the fact first cannot change the report.

What this asserts, and why each part matters:

| Assertion | Source |
|---|---|
| Five facts are reported as the events state them | SC-001 — the projected ones, unfiltered |
| Exactly three conclusions are added: `JealousOf`, `FriendshipThreatened`, `BottledUpFeelings` | SC-001 — the chain is in the report, not in the code |
| None of the three appears in `history` | SC-001 — facts are interpretation, never history |
| Three distinct rules produced the three conclusions | US1 scenario 1–3 |
| Each conclusion names the premises it applied to | FR-010, FR-028, N-IV |

**The reasoning is the deliverable.** A test that only asserts the fact set passes on an
implementation that cannot explain itself; this one asserts the reasons too, which is why
the expected block above is written out rather than summarised as a count.

## 2. What each capability test asserts

| Test file | Asserts | Source |
|---|---|---|
| `NarrativeVocabularyTests.fs` | canonical text is the literal expected string for every event and every fact construct; the roster enumerates every character once; `Friends(Aiko, Akane)` and `Friends(Akane, Aiko)` are one value, while an event keeps the order it was committed in and a directional relation keeps its argument order; and the ordering is the same under a Turkish culture as under the invariant one | FR-009, FR-021, clarification 1, research D13, D22, D28, D30, D34 |
| `NarrativeCommitTests.fs` | an event naming a character who has not arrived is accepted, while a game time that goes backwards raises `TimeWentBackwards` naming what it is about and an event no rule reads is accepted and concludes nothing; a refused event leaves the events before it committed; committing nothing leaves the state alone | FR-002, FR-006, FR-007, FR-013 |
| `NarrativeProjectionTests.fs` | `FriendshipEnded(Akane, Aiko)` withdraws a friendship begun as `FriendshipStarted(Aiko, Akane)`; an ending with nothing to end is a no-op; friendship, ended, friendship again reports only the current state | FR-011, Edge Cases, research D19, D22 |
| `NarrativeDerivationTests.fs` | recursion to a fixed point; a fact with two independent reasons is reported once with both; one reason surviving keeps the fact; a stale reason stops being offered; an event matching no rule concludes nothing; the bound is reachable deliberately and reports the rules still concluding, in under a second | FR-008–FR-012, FR-014, SC-002, SC-003, SC-006 |
| `NarrativeRuleSetTests.fs` | every permutation of unit order produces identical output; a rule in one unit reads a fact another concluded; a rule set missing a unit simply concludes less; a replaced rule set rebuilds from unchanged history — the same events with the same indices — and reaches different conclusions | FR-018, FR-019, FR-020, SC-007 |
| `NarrativeHarnessTests.fs` | the report is stable and diffable; a failed assertion attaches the whole report; the facts naming a character are every one of them, in either position; an assertion holds however a mutual fact's names are ordered; generated scenarios satisfy the invariants of §5 | FR-013, FR-015, FR-026–FR-029, SC-004 |
| `NarrativePerformanceTests.fs` | SC-005 and SC-009, plus the history-append and canonical-text regression guards | see §3 |

`NarrativeTestSupport.fs` is not a test file: it is the first of the narrative test files
and holds the three units — `Cast`, `Romance`, `Friendship` — assembled into one rule set,
plus the headline event list, which the other files and the harness supply as data.

One thing the table cannot say is worth having in one place: the ordering guarantees are
asserted against a hostile culture rather than assumed. `NarrativeVocabularyTests.fs` orders
the same facts under `tr-TR`, `en-US` and the invariant culture, because Turkish is where a
culture-sensitive comparison of `i` and `I` diverges and would quietly order a report
differently (FR-021, research D13).

## 3. Performance, measured where it matters

Five tests, deliberately not a benchmark harness (research D15):

- **SC-005** — rebuild from a history of 10,000 events, then commit one further event at
  a cast of 20. Rebuild under 1 s, one event under 10 ms. Two tests, because the two halves
  fail for different reasons.
- **SC-009** — with all 20 cast members active, sustained commits at ≥1,000/s.
- **No criterion behind it** — a commit's cost must not grow with the length of the
  history: 1,000 commits no projection reads, then two runs of 500 that each flip a
  projected fact, asserting the second run is not materially slower than the first. This is
  the regression guard for an append that copied more as history grew, which is a cost
  SC-009 is the first to notice.
- **No criterion behind it** — rendering a canonical text must not go back through the
  type: a bound of 5 µs a render, against ~0.3 µs written out and ~17 µs asked for. This
  guards the second cost found by measuring, which was nearly all of a commit's time
  (research D30), and it is the test that would have failed on the day that was introduced.

The question being answered, stated before measuring, per the constitution: *at a cast of
20, does the engine meet the throughput and latency the game needs, and does it hold
under sustained load?* The prototype's figures — 29.6 ms falling to 4.0 ms for one
further event, and a sustained rate below 1,000/s rising to ~2,300/s at this cast — are
the reason research D7's two restrictions exist (the facts indexed by construct and
participant, and derivation skipped when no projected fact changed), not a target this
implementation is aiming at. F# numbers will differ. If a figure fails, the order of
investigation is a data structure before a backend: one of the prototype's two fixes was
a join re-scanning a bucket it could have indexed, and the other was evaluating every rule
on a pass that could only conclude nothing.

The suite as last run, on 2026-09-19, printed ~9 ms for the rebuild of 10,000 events and ~6,410
events per second sustained — inside every bound above, and past every figure the prototype
recorded. The sustained figure is lower than the batched version it replaced, because a commit is
now one event and the derivation fixpoint runs per event; the rebuild is unaffected, because most
of its events re-establish facts already present, so derivation is skipped for them. Most of the
earlier gain arrived in one change: encoding a value by asking the type what it is, which cost
~17 µs a call and made the engine do about 25 times the work its own reasoning required (research
D30).

## 4. Determinism, which is checked by behaving the same way twice

| Check | How | Source |
|---|---|---|
| 100 runs identical | run the headline scenario 100 times, diff the reports | SC-004, FR-021 |
| Rule order not observable | concatenate the units' rules in every permutation, diff each run's canonical rendering | SC-007, FR-019 |

Byte-identical is the assertion, not "equivalent", because the report is the author's
diff tool and a report that merely means the same thing is not diffable.

## 5. Generated scenarios

`FsCheck` drives FR-027: a generator produces a cast, its traits, and a set of friendships,
crushes and relationships, from which the event order follows — each character's arrival named
first as an `Arrived` event, though nothing requires it, because naming any roster member is
admissible. The suite asserts invariants across many of them rather than one hand-built case:

- a fact is never reported without a reason
- every reason is attributed to a rule in the rule set
- every premise that is a fact is itself currently reported
- the same generated scenario replays identically

Two of the invariants in the spec's terms are asserted elsewhere, because neither varies
with a generated scenario. That no conclusion is reported that no rule concluded is §1's
exact fact set, held to rather than sampled. That a scenario never exceeds the derivation
bound is driven deliberately with a bound of zero — `Engine.createBounded`, since an F#
module function cannot take an optional argument — because a well-formed rule set cannot
reach the default.

One property is asserted repeatedly rather than once: over 200 runs of the same cast with
the trait alternating, `bottled-up` fires exactly when the cast is shy. A rule that fired
always, or never, would pass a test that only looked for it once.

## 6. What passing looks like

```sh
$ dotnet test test/fsharp-game.Tests.fsproj --filter "FullyQualifiedName~Narrative"
Passed!  - Failed: 0, Passed: N, Skipped: 0, Total: N
```

with the headline test in §1 among them. A failure should print the whole report rather
than a comparison (FR-028) — if it prints `expected true, got false`, the report
machinery is not attached and that is itself the bug to fix first.
