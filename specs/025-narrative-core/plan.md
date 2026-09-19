# Implementation Plan: Narrative Core — History to Derived Facts

**Branch**: `025-narrative-core` | **Date**: 2026-09-16 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/025-narrative-core/spec.md`

## Summary

Build the narrative subsystem: an event-sourced interpreter that folds a committed
event history into the social facts currently implied, with every fact carrying the
rules and premises behind it. Two rule categories — projections (history to facts)
and derivations (facts to facts) — are authored as F# source in computation-expression
DSLs over one closed event type and one closed fact type. Facts live in an
ordered map keyed by the fact itself, so derivation is a monotone fixpoint that converges to
the same answer whatever order rules were written in. A narrative unit is a module of
rules: the vocabulary is closed and shared, so adding a construct is one edit in one
place rather than something a unit does for itself. The engine has no runtime
dependencies and is driven entirely by scenario descriptions, so the whole feature is
developed and asserted by headless test runs.

The closed vocabulary is the load-bearing decision, and it is a deliberate trade
rather than an oversight: it buys compiler-checked rule categories and construct
kinds, and it makes an event the vocabulary does not recognise unrepresentable rather
than something the engine has to report. What it costs is that a unit cannot introduce
a construct the engine does not declare. That cost is recorded
in the spec as a deferral with a trigger.

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`)

**Primary Dependencies**: none at runtime. Test-only: xUnit 2.9.2 (already in the
repo) plus `FsCheck` and `FsCheck.Xunit` for generated scenarios. Rejected with
reasons in [research.md](./research.md): a Datalog engine, a serialisation library, an
immutable-collections package, an event-sourcing framework, and a benchmark harness.

**Storage**: none. History is held in memory; canonical text is output only — the key
the fact set is stored and ordered by, and what the report renders. Nothing reads it back.

**Testing**: xUnit via `dotnet test test/fsharp-game.Tests.fsproj`. SC-005 and SC-009
are measured in a test rather than a benchmark harness, and the derivation bound is
driven deliberately so the failure path is exercised rather than assumed.

**Target Platform**: Linux desktop. The engine itself is platform-neutral and names
no graphics or physics API.

**Project Type**: a library-shaped subsystem — `narrative/`, a source folder compiled
first inside the existing `fsharp-game.fsproj`, with no project of its own. See
Structure Decision.

**Performance Goals**: rebuilding state from 10,000 events in under 1 s; one further
event in under 10 ms; at least 1,000 committed events per second sustained with all 20
cast members active (SC-005, SC-009).

**Constraints**: no runtime dependencies; ordinal string comparison wherever strings
are compared or ordered, so behaviour is culture-independent; no ambient clock and no
global RNG — game time and any randomness arrive as caller-supplied data; the
vocabulary is closed and shared; adding a construct is an engine edit.

**Scale/Scope**: a cast of at most 20 characters, tens of rules, and — for this
feature — 8 event constructs, 7 fact constructs and 2 traits. No scenes, no temporal
layer, no proposals, no negation.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — one event type, one fact type, one trait type, two rule builders. The vocabulary is 8 events, 7 facts and 2 traits: what the spec's criteria require and no more, with the omissions recorded. No index (D6), no decoder, no registry, no scenes, no temporal layer. |
| II. Fail Loudly | Pass — a rule set cannot be written that is misplaced (the type system refuses it), an event the vocabulary does not have cannot be built, a backwards game time is raised rather than returned, and exhausting the derivation bound is raised naming the rules still concluding rather than tolerated. |
| III. Spec-First | Pass — `specs/025-narrative-core/spec.md` is complete, clarified and approved, and drives this plan. |
| IV. Data-Oriented Architecture | Pass, and this is a cold path so the hot-path parts do not bind. Facts are one discriminated union in a sorted map; history is a list of committed events, appended; no inheritance or object graph. |
| V. Minimise GC Pressure | Pass — the engine runs on committed events, not per frame, so Principle V's own carve-out for cold paths applies: clarity first, then measure. It must still not make the frame wait, which SC-005 and SC-009 assert as throughput and latency rather than as a frame budget (the spec leaves the frame arrangement to the integration work). |
| N-I. History is Immutable, Facts are Interpretation | Pass — history is append-only and never rewritten, including when the rule set changes; facts are a projection recomputed from that history; the interface offers no way to assert a fact and no way to alter a committed event. |
| N-II. Deterministic by Construction | Pass — every collection whose order a reader sees is ordered by canonical text, in one place per read path rather than by a discipline spread through the engine; storage order is the value's own and is never shown (D1, D13, D33). |
| N-III. Modular Authorship on a Shared Substrate | Pass — a unit is a module of rules reading one shared fact set; rule order is not observable (D14); the vocabulary is closed, which this principle now states as the accepted cost. |
| N-IV. Attribute Every Conclusion | Pass — reasons are recorded per fact, deduplicated by canonical text, dropped when their premises stop holding, and retrievable while the game is running (spec FR-010, Clarification 2). Prose explanations remain out of scope. |
| N-V. Testing is the Authoring Tool | Pass — the engine is exercised by scenario runs with no graphical interface, over parameterised and generated scenarios, reported in a form a failing assertion can attach in full. |

No violations, so Complexity Tracking is empty.

### Post-design re-check

Re-evaluated after Phase 1. Nothing in the design added a principle it does not already
satisfy, and two choices were weighed against Minimalism explicitly rather than passed
over:

- **A project boundary, weighed and then not taken.** A `narrative/Narrative.fsproj` with
  no `PackageReference` was the first reading of FR-025, and it was built and then removed:
  the compile order gives the same one-way dependency without a second project, a second
  build step, and a `ProjectReference` to keep facing the right way. The subsystem is a
  source folder inside `fsharp-game.fsproj`, which is the smaller answer, so this is
  Minimalism satisfied rather than traded against. See Structure Decision for what replaced
  it.
- **One `Source` line per construct a rule starts from** (research D24). Three plain and
  three keyed as built, each declaring the `FactKind` it enumerates, in exchange for rules
  whose premises the compiler checks and whose attribution the builder captures. The
  alternative — deriving the sources by reflection — removes six lines and pushes `obj`
  into every rule, which would cost the closed vocabulary's main benefit. This is the one
  place the design accepts a small per-construct table, and it is a table of one column,
  not the four the earlier attempt maintained.

## Project Structure

### Documentation (this feature)

```text
specs/025-narrative-core/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── engine-api.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
narrative/                         # a source folder in fsharp-game.fsproj, listed first
├── Vocabulary.fs                  # the roster, Trait, Event, Fact — the closed vocabulary
├── Canonical.fs                   # canonical text: construct name and argument text (rendering only)
├── Facts.fs                       # fact sources, the ordered fact map, the participant index
├── Rules.fs                       # rule types, the project / derive builders
├── Derive.fs                      # the fixpoint, its pass restriction and its bound
├── History.fs                     # committed events, game time
├── Engine.fs                      # the public surface: commit, facts, reasons
├── Harness.fs                     # scenario, run, report, assertions
└── Units/
    ├── Cast.fs                    # the trait projection only; arrival is an event a unit may project
    ├── Friendship.fs              # friendship projection, threatened, bottled-up
    └── Romance.fs                 # crush and relationship projections, jealousy

test/
├── NarrativeTestSupport.fs        # first compile item; the MVP rule set, shared by the tests
├── NarrativeVocabularyTests.fs
├── NarrativeCommitTests.fs
├── NarrativeProjectionTests.fs
├── NarrativeDerivationTests.fs
├── NarrativeRuleSetTests.fs
├── NarrativeHarnessTests.fs
├── NarrativePerformanceTests.fs   # SC-005, SC-009
└── fsharp-game.Tests.fsproj       # += the files above, FsCheck
```

**Structure Decision**: the engine is a **source folder inside `fsharp-game.fsproj`**,
listed **first**, before the game's own files. F# compiles in list order, so putting it
first makes "the engine cannot reference the game" a compile error rather than a
convention — which is the property FR-025 needs, without a second project to build and
keep in step. Nothing in `narrative/` names SDL3, Jolt or the renderer, and the narrative
tests run with no window, so "usable with no graphical interface" holds both by
construction and by observation.

It was briefly a separate `Narrative.fsproj`. That was rejected because it bought nothing
the compile order does not already give — the one-way dependency is a property of the file
list, not of the assembly boundary — while costing a second project, a second build step,
and a `ProjectReference` to keep pointing the right way.

**Arrival is an event a unit may project, not engine bookkeeping.** An `Arrived` event
records that a character entered the story, but it gates nothing: any event naming any roster
member is admissible, so there is no cast set to maintain during commit and no
`CharacterNotDeclared` failure (spec Amendments, 2026-09-19). A unit that wants to react to an
arrival projects `Arrived` the way it projects any other event — projections read events and
derivations read facts. `Units/Cast.fs` therefore holds the trait projection only,
`TraitAssigned` to `HasTrait`, because that is a conclusion a rule draws rather than validation
the engine performs.

## Complexity Tracking

None. No principle is violated, and the one deliberate limitation — the closed
vocabulary — is a spec-level deferral with a recorded trigger rather than an
unjustified mechanism.
