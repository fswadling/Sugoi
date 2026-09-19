---

description: "Task list for Narrative Core — History to Derived Facts"
---

# Tasks: Narrative Core — History to Derived Facts

**Input**: Design documents from `/specs/025-narrative-core/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/engine-api.md](./contracts/engine-api.md), [quickstart.md](./quickstart.md)

**Tests**: **required and not optional here.** The feature specification makes the
headless harness a deliverable (FR-025–FR-029, US3) and constitution principle N-V says a
rule change that cannot be observed in a scenario run is not finished. Test tasks are
therefore part of every story.

**A note on ordering under F#.** F# compiles top-down and will not accept a test that
names a function which does not exist, so "write the test first, watch it fail" means
writing it against a *declared surface* whose behaviour is stubbed with
`failwith "not implemented"`. Each story therefore orders as: declare the surface →
write the failing tests → fill in the behaviour. The stubs are removed as each behaviour
lands.
**Withdrawn 2026-09-19:** T041 (save and load) and the save/load halves of T015 and T036 are gone with persistence itself: FR-004 was withdrawn and the decoder removed (spec Amendments, research D32). The rows below are left as written, so the record of what was asked for is intact.

**Adjusted 2026-09-19 (research D34):** the construct-name check asked for by T011 is withdrawn.
It defended the unambiguity of a *rendering*, which only mattered while a fact was keyed and
identified by its own canonical text — and the one clash F# permits, an event case name equal to a
fact case name, reaches neither the index nor the pass restriction. T012's `FactEntry` (withdrawn by
D33) and its `nameof`-derived source names, and T015's refusal case, go the same way: the participant
index and a rule's reads are keyed by `FactKind`, a reason is identified by structural comparison, and
only `Harness` sorts for a reader. T050's audit stands, with `Facts.fs`'s only remaining string
comparison the explicit ordinal one in `withdraw` and its keys no longer text. The rows are left as
written, for the same reason as above.

**Organization**: tasks are grouped by user story so each can be implemented, tested and
delivered on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel — a different file, and no dependency on an incomplete task
- **[Story]**: which user story the task belongs to
- Exact file paths are given for every task

## Path Conventions

The engine is a new library at `narrative/` and its tests extend the existing project at
`test/`, both at the repository root. See [plan.md](./plan.md) § Project Structure.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: a library, wired into the build, that compiles and runs green with nothing
in it. The first task in the plan's Structure Decision is the project reference, because
nothing else can be built until the projects know about each other.

- [x] T001 Create the module stub files listed in plan.md § Project Structure, each with its namespace and a module declaration: `narrative/Vocabulary.fs`, `narrative/Canonical.fs`, `narrative/Facts.fs`, `narrative/Rules.fs`, `narrative/Derive.fs`, `narrative/History.fs`, `narrative/Engine.fs`, `narrative/Harness.fs`, `narrative/Units/Cast.fs`, `narrative/Units/Friendship.fs`, `narrative/Units/Romance.fs`
- [x] T002 Create the narrative `Compile` block in `fsharp-game.fsproj` — the eleven files in the dependency order given in T001, listed **before the game's own files** so that a reference from the engine into the game is a compile error rather than a convention (research D12, FR-025)
- [x] T003 [P] Confirm in `fsharp-game.fsproj` that every `narrative/` entry precedes `core/SlotMap.fs`, since that ordering is the whole of the one-way dependency (research D12)
- [x] T004 [P] Create the eight test stub files under `test/` — `NarrativeTestSupport.fs` (the first compile item, since later test files open it), `NarrativeVocabularyTests.fs`, `NarrativeCommitTests.fs`, `NarrativeProjectionTests.fs`, `NarrativeDerivationTests.fs`, `NarrativeRuleSetTests.fs`, `NarrativeHarnessTests.fs`, `NarrativePerformanceTests.fs` — and add them plus `PackageReference` entries for `FsCheck` and `FsCheck.Xunit` to `test/fsharp-game.Tests.fsproj` (research D15)
- [x] T005 Verify `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj --filter "FullyQualifiedName~Narrative"` both succeed on the empty skeleton, so every later failure is a real one

**Checkpoint**: an empty library builds, its tests run, and the game still builds.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the shared substrate every story reads — the closed vocabulary, canonical
text, the fact set, history, and a commit pipeline with its stages empty.

**⚠️ CRITICAL**: no user story work can begin until this phase is complete.

- [x] T006 [P] Declare the closed vocabulary in `narrative/Vocabulary.fs` — `[<Struct>] Character = Character of string`, `Trait = Shy | HotHeaded`, the eight-case `Event` and the seven-case `Fact` exactly as tabulated in data-model.md § 1, plus the declaration marking `FriendshipStarted`/`FriendshipEnded`/`RelationshipStarted`/`RelationshipEnded`/`Friends`/`InRelationship` as having interchangeable participants (research D20, D22)
- [x] T007 [P] Declare `GameTime`, `CommittedEvent` and the append-only history in `narrative/History.fs`, with the non-decreasing game-time check (research D8)
- [x] T008 Derive construct metadata by reflection in `narrative/Canonical.fs` — name, arity, field types, and participant positions as every field whose type is `Character` (research D21). No per-construct table in source
- [x] T009 Add the canonical text encoder to `narrative/Canonical.fs` — `Construct(arg, arg)`, with arguments sorted ordinally for the key where the construct declares interchangeable participants (research D22)
- [x] T010 Add the decoder to `narrative/Canonical.fs`, returning a failure when the construct name is unknown or the arguments do not fit it (research D23)
- [x] T011 Add the one-time uniqueness check over construct names in `narrative/Canonical.fs`, failing loudly on a duplicate, so canonical text is unambiguous across events and facts. *Withdrawn (research D34)*: with the index and reason identity typed, the check protected only what a report prints, and the reachable clash was the one with no engine-level consequence
- [x] T012 [P] Add `FactEntry`, the ordered fact set as a `Map` compared with `StringComparer.Ordinal`, and the `FactSource` definitions in `narrative/Facts.fs` — one line per fact construct, the name taken from `nameof` (research D5, D13, D24)
- [x] T013 Declare `Engine`, `create`, `facts` and `history` in `narrative/Engine.fs` (data-model.md § 5). ~~including the `Cast` set that `CharacterDeclared` maintains~~ (withdrawn 2026-09-19: the engine holds no cast set, and `CharacterDeclared` is now `Arrived` — spec Amendments)
- [x] T014 Add `commit` to `narrative/Engine.fs`: validate the event's time, append to history, and leave the projection and derivation stages empty (filled in by US1). A refused commit changes nothing (contracts/engine-api.md § Invariants). *Adjusted 2026-09-19*: no cast to maintain, since arrival gates nothing (spec Amendments); and a commit is one event, so a run is a caller-side fold (spec Amendments)
- [x] T015 [P] Write `test/NarrativeVocabularyTests.fs` — every construct round-trips through canonical text; ~~a duplicate construct name is refused~~ (withdrawn, research D34); `Friends(Aiko, Akane)` and `Friends(Akane, Aiko)` produce one key; an unknown construct name and an argument count that does not fit both fail (FR-004, FR-009)
- [x] T016 [P] Write `test/NarrativeCommitTests.fs` — ~~an event naming an undeclared character and~~ a game time preceding the previous commit raises `TimeWentBackwards` naming what it is about (FR-006, FR-028), while an event naming a character who has not arrived is accepted (2026-09-19); a refused event leaves the events before it committed (FR-013); committing nothing leaves the state alone (FR-007). *Adjusted during implementation*: "an unknown construct" moved to T015, because a closed type cannot produce one in code — only text can, so it is the decoder's test. "So does replacing the rule set" moved to T030 and "the facts of a character" to T029, where there are rules and facts for them to be about rather than a vacuously passing assertion.

**Checkpoint**: the vocabulary, canonical text, history and commit pipeline are done and
asserted. User stories can now begin.

---

## Phase 3: User Story 1 — See what a situation currently implies (Priority: P1) 🎯 MVP

**Goal**: the engine folds committed events into the facts they currently imply, recursing
through conclusions of conclusions, with every fact carrying the rules and premises behind
it.

**Independent Test**: load the quickstart § 1 history, request the facts, and assert
exactly five projected facts plus exactly three conclusions, each naming its rule and the
premises that rule applied to (SC-001).

### Declare the surface for User Story 1

- [x] T017 [US1] Declare the `project`/`derive` builder surface in `narrative/Rules.fs` — a rule is exactly one of the two categories and nothing else (FR-017). *Adjusted during implementation*: the rule *types* moved to Foundational, because `Engine` holds a rule set and so cannot be declared without them.
- [x] T018 [US1] Declare the evaluation entry point in `narrative/Derive.fs`, stubbed

### Tests for User Story 1 ⚠️

> Write these first, against the stubs from T017/T018, and confirm they fail.
>
> These tests carry their **own small ad-hoc rule sets**. They are written before the MVP
> units exist (T024–T026), and a story's tests should not depend on that story's content —
> otherwise a failure cannot tell you which of the two is wrong.

- [x] T019 [P] [US1] Write `test/NarrativeProjectionTests.fs` — an establishing event projects its fact; the withdrawing event removes it; a withdrawal with nothing to withdraw is a no-op; friendship then ended then friendship again reports only the current state (FR-011, Edge Cases, research D19)
- [x] T020 [P] [US1] Write `test/NarrativeDerivationTests.fs` — the three-step headline chain; recursion to a fixed point; a fact with two independent reasons is reported once carrying both; one surviving reason keeps the fact; a reason whose premises stopped holding stops being offered; concluding an already-current fact causes no further derivation; an event matching no rule is accepted and concludes nothing (FR-006, FR-008–FR-012, SC-002, SC-003); and the bound is reachable deliberately, exhaustion naming the rules still concluding and reported in under a second (FR-014, SC-006)

### Implementation for User Story 1

- [x] T021 [US1] Implement the `project` and `derive` builders in `narrative/Rules.fs`, capturing each `let!` binding as a premise so attribution is a property of the notation rather than something the author restates (FR-010, research D24)
- [x] T022 [US1] Implement `narrative/Derive.fs` — recompute inferred facts from the projected ones to a fixed point, taking one snapshot per pass and writing to a separate accumulator, and restricting passes after the first to rules mentioning a construct the previous pass gained (FR-008, research D7, D14)
- [x] T023 [US1] Implement `reasonsFor` and `factsOf` in `narrative/Engine.fs` — the facts true of a character are every fact naming them in any position, unfiltered (FR-010, FR-015)
- [x] T024 [P] [US1] Implement `narrative/Units/Romance.fs` — the `CrushStarted`/`CrushEnded` projection to `AttractedTo` as rule `"crush-started"`, the `RelationshipStarted`/`RelationshipEnded` projection to `InRelationship` as `"together"`, and the jealousy inference as `"jealousy"` (research D26; names are asserted verbatim by T043, so they are pinned here)
- [x] T025 [P] [US1] Implement `narrative/Units/Friendship.fs` — the `FriendshipStarted`/`FriendshipEnded` projection to `Friends` as `"befriended"`, the threatened-friendship inference as `"threatened"`, and bottled-up-feelings as `"bottled-up"` (research D26)
- [x] T026 [P] [US1] Implement `narrative/Units/Cast.fs` — the `TraitAssigned` projection to `HasTrait` as `"trait-assigned"` (research D26). Arrival is not here either: an `Arrived` event gates nothing, so there is no cast to maintain and the module holds the trait projection only (plan.md § Structure Decision)
- [x] T027 [US1] Add the MVP rule set assembly to `test/NarrativeTestSupport.fs` — the three units combined into one rule set, which the tests and the harness both supply as data rather than the library embedding content (contracts/engine-api.md § Harness)
- [x] T028 [US1] Make the derivation bound injectable in `narrative/Derive.fs` and report exhaustion by raising with the names of the rules still concluding (FR-014)
- [x] T029 [US1] Verify SC-001 end to end in `test/NarrativeDerivationTests.fs`: five projected facts reported as the events state them, exactly three conclusions added, none of the three present in history, each conclusion naming its rule and premises

**Checkpoint**: User Story 1 works on its own. This is the MVP — the engine can be shown
a situation and explain what it implies.

---

## Phase 4: User Story 2 — Keep a narrative unit's rules in one place (Priority: P2)

**Goal**: a unit is a module of rules reading one shared fact set, and where those rules
sit relative to other units' changes nothing about the result.

**Independent Test**: assemble the units in the opposite order and assert byte-identical
output; read a fact one unit concluded from a rule in another; remove a unit's rules and
assert the engine and the remaining units are unaffected.

### Tests for User Story 2 ⚠️

- [x] T030 [P] [US2] Write `test/NarrativeRuleSetTests.fs` — every permutation of unit order produces identical output; a rule in the friendship unit reads `JealousOf`, which the romance unit concluded, with nothing scoping it; a rule set missing one unit still runs and simply concludes less; and a rule set replaced wholesale rebuilds all current state from unchanged history, revisiting no committed event (FR-002, FR-018, FR-020, SC-007)

### Implementation for User Story 2

- [x] T031 [US2] Add rule-set assembly to `narrative/Rules.fs` — combine units into one rule set, and reject a rule set containing two rules of the same name, since a name is a reason's identity and a duplicate cannot be attributed (FR-010)
- [x] T032 [US2] Order the reasons attached to a fact canonically in `narrative/Facts.fs`, so that permuting the rules cannot permute what a fact is attributed to — the output ordering half of FR-019 that a monotone fixpoint alone does not give. *Landed with T031*: it is one line in `Facts.add`, and writing a deliberately unsorted version first to justify it as a separate step would have been writing known-wrong code.
- [x] T033 [US2] Confirm no unit module names another unit's rules and no unit declares vocabulary (FR-018, research D16): the three `narrative/Units/*.fs` files reference only the shared vocabulary and the shared fact set
- [x] T034 [US2] Pin SC-007 in `test/NarrativeRuleSetTests.fs` — the report is byte-identical across every permutation of the unit order

**Checkpoint**: User Stories 1 and 2 both work. Rules can be added, reordered and removed
per unit without disturbing anything else.

---

## Phase 5: User Story 3 — Develop rules by running scenarios headlessly (Priority: P3)

**Goal**: the author writes a scenario, runs it with no game, and gets back the history and
the facts in a form that can be diffed and asserted, each fact carrying its rule and
premises.

**Independent Test**: run the scenario suite from the command line with no graphical
interface, assert every expected fact and that each carries its rule, and assert a
generated cast replays identically from its seed.

### Declare the surface for User Story 3

- [x] T035 [US3] Declare `Scenario`, `RunResult`, `run`, `report` and `shouldDerive` in `narrative/Harness.fs`, stubbed

### Tests for User Story 3 ⚠️

- [x] T036 [P] [US3] Write `test/NarrativeHarnessTests.fs` — the report is stable and diffable across repeated runs; a run's history written out and loaded back continues as if never interrupted; a history saved before a construct was added still loads; a failed assertion attaches the whole report. *Adjusted 2026-09-19*: ~~an uneven burst compared batch delivery with single-event delivery~~, so with one event per commit there is no second delivery mode; that test and SC-008 were withdrawn, and a count of the events before a refused one replaced them (FR-004, FR-026, FR-028, FR-029)
- [x] T037 [P] [US3] Write `test/NarrativePerformanceTests.fs` — SC-005 (rebuild 10,000 events under 1 s, one further event under 10 ms, cast of 20) and SC-009 (≥1,000 committed events/s with all 20 active), measured in the test and not in a benchmark project (research D15). **SC-005 and SC-009 both pass** as of 2026-09-19 — see the note below.

### Implementation for User Story 3

- [x] T038 [US3] Implement `Scenario` and `run` in `narrative/Harness.fs` — commit the initial events (a scenario names an arrival as an `Arrived` event), then the steps, all through the ordinary commit path with no privileged route (FR-005, FR-025, FR-027). *Adjusted 2026-09-19*: no cast field and no declare step (spec Amendments)
- [x] T039 [US3] Implement `report` in `narrative/Harness.fs` — history and facts in canonical order, each fact followed by the rules and premises supporting it (FR-026)
- [x] T040 [US3] Implement `shouldDerive` in `narrative/Harness.fs`, attaching the whole report rather than a bare comparison (FR-028)
- [x] T041 [US3] Implement `save` and `load` in `narrative/Engine.fs` — canonical text, one committed event per line (FR-004)
- [x] T042 [US3] Add FsCheck generators in `test/NarrativeHarnessTests.fs` for a cast, its traits and an event order, so one situation is exercised across many casts in a single run (FR-027)
- [x] T043 [US3] Assert the quickstart § 1 report text verbatim in `test/NarrativeHarnessTests.fs`, since a test that only counts facts passes on an implementation that cannot explain itself
- [x] T044 [US3] Assert SC-004 in `test/NarrativeHarnessTests.fs`: the same scenario run one hundred times yields one hundred identical reports
- [x] T045 [US3] Assert the generated invariants across many scenarios in `test/NarrativeHarnessTests.fs` — no fact without a reason, every reason's premises currently hold, nothing reported that no rule concluded, no run exceeds the bound, and a generated scenario replays identically from its seed

**Checkpoint**: all three stories are functional. Rules are developed by running
scenarios, not by reproducing cases by hand in the game.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: the properties that only make sense once everything is built.

- [x] T046 [P] Verify no file under `narrative/` names SDL3, Jolt or the renderer, and that the narrative test files load no graphics native, so "usable with no graphical interface" holds by construction and by observation (FR-025, research D12)
- [x] T047 [P] Record in research.md D17 the line budget the finished design actually implies, and give a stated reason for any module over roughly 200 lines
- [x] T048 [P] Run `quickstart.md` end to end against the built engine and correct the document wherever it was wrong — including the § 1 report text, which was written before the code existed
- [x] T049 [P] Note the subsystem in `docs/ARCHITECTURE.md`. **Flag rather than settle** whether spec 025 becomes its own roadmap phase or extends phase 10; that is the user's decision and the spec explicitly declines to settle it
- [x] T050 Audit the string comparison in `narrative/Canonical.fs`, `narrative/Facts.fs` and `narrative/History.fs` — every comparison, ordering and map key must use `StringComparer.Ordinal`/`CompareOrdinal`, since the default is culture-sensitive and would make FR-021 false in a locale nobody tested in. Confirm also that nothing in the engine reads a clock or a global RNG, since observable output must not depend on ambient state (FR-022)
- [x] T051 Verify the public surface of `narrative/Engine.fs` and `narrative/Harness.fs` offers no way to assert a fact, no way to alter a committed event, no way to schedule, pace or advise on what happens next, and no mutable field on `Engine` (FR-031, FR-032). These must be absent rather than discouraged, and this is the check that they are

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies; T002 depends on T001, T003 on T002, T004 is independent, T005 on all four
- **Foundational (Phase 2)**: depends on T005 and **blocks every user story**
- **User Story 1 (Phase 3)**: depends on Foundational
- **User Story 2 (Phase 4)**: depends on Foundational and on US1, since it pins a property of US1's rule set
- **User Story 3 (Phase 5)**: depends on Foundational and on US1; independent of US2
- **Polish (Phase 6)**: depends on all three stories

### Within Foundational

T008 needs T006. T009, T010 and T011 each build on T008 and are sequential, being the same
file. T012 needs T006. T013 needs T007/T008/T012. T014 needs T013. T015 needs T008–T011;
T016 needs T014.

### Within User Story 1

T017 → T018 → T019/T020 (in parallel, different files) → T021 → T022 → T023 → T024/T025/T026
(in parallel, different files) → T027 → T028 → T029.

T019 and T020 are expected to fail until T021 and T022 land. That is the point of them.

### Within User Story 2

T030 is written against the surface US1 already provides, so it can be written immediately
and will fail until T031 and T032 land. T033 and T034 come last.

### Within User Story 3

T035 → T036/T037 (in parallel) → T038 → T039 → T040 → T041 → T042 → T043 → T044 → T045.

### Parallel Opportunities

- **Setup**: T004 alongside T001–T003
- **Foundational**: T006, T007 and T012 touch different files and run together
- **US1**: T019 and T020 together; then T024, T025 and T026 together
- **US3**: T036 and T037 together
- **Polish**: T047, T048 and T049 together

### Parallel Example: Foundational

```text
T006  Declare the closed vocabulary in narrative/Vocabulary.fs
T007  Declare GameTime, CommittedEvent and history in narrative/History.fs
T012  Add FactEntry, the fact set and the FactSources in narrative/Facts.fs
```

### Parallel Example: User Story 1

```text
T024  Implement narrative/Units/Romance.fs
T025  Implement narrative/Units/Friendship.fs
T026  Implement narrative/Units/Cast.fs
```

---

## Implementation Strategy

### MVP first — User Story 1 only

1. Phase 1 (Setup) and Phase 2 (Foundational)
2. Phase 3 (User Story 1)
3. **Stop and validate**: the headline scenario, asserted with its reasons
4. That is a demonstrable engine: it can be shown a social situation and explain what it
   implies, with no game, no window and no proposals.

The MVP is genuinely useful on its own — it is the whole of the engine's reason to exist,
and every later capability consumes it.

### Incremental delivery

1. Setup + Foundational → a library that compiles and commits events
2. US1 → the derived facts and their attribution (MVP)
3. US2 → units compose, order is not observable
4. US3 → the scenario harness, the report, persistence and generation
5. Polish → budget recorded, quickstart verified, absences confirmed

Each step leaves the previous ones working, and each is independently testable.

---

## Notes

### Known shortfall: SC-009 sustained throughput

*(Resolved 2026-09-19: SC-009 passes — the suite measures ~6,410 committed events/s against the
1,000/s criterion. The record below is the earlier diagnosis and the fixes it led to. Note that a
commit is now one event, since the batch entry point was removed (spec Amendments, 2026-09-19).)*

`NarrativePerformanceTests` asserts SC-005 and SC-009. **SC-005 passes**, and SC-009 was failing
when this was written:

| Scenario | Measured | Required |
|---|---|---|
| 20 characters each with a few ties, commits that change a fact every time | ~146 events/s | 1,000 |
| the same on a saturated world built from 10,000 events | ~690 events/s | 1,000 |

Two real inefficiencies were found and fixed on the way — a join that re-scanned a whole
construct instead of using an argument it had already bound (research D7's recorded
mistake), and a derivation that ran when no projected fact had changed. Both are in the
code and both are measured improvements.

**What is not explained**: the saturation figure moved 123 → 394 → 694 as those two fixes
landed, but the *plausible* cast measures ~146/s — worse, with a far smaller fact set. So
the cost is not proportional to fact-set volume, and the remaining bottleneck is something
per-commit that has not been identified. At ~68 ms for a commit over ~1,100 events, the
first thing to measure is `History.append`, which copies the whole array on every commit.
This was recorded as an open item rather than papered over; the resolution note above records
where it now stands.

- **The headline test is the deliverable.** SC-001 asserts not only the fact set but the
  rules and premises behind it; a test that counts facts passes on an implementation that
  cannot explain itself, which would fail the point of the feature.
- **[P] means a different file**, not merely a different concern.
- **`commit` is the only way in.** No test may set a fact or edit history; if a test needs
  a state, it commits events that produce it (FR-032). A test that cannot do so has found
  a missing rule or a missing construct, which is a design finding, not a fixture problem.
- **A failing performance figure** is investigated as a data structure before a backend —
  the prototype's two fixes were both joins re-scanning a bucket they could have indexed
  (research D7).
- **Adding a construct** is one case in `narrative/Vocabulary.fs`, after which the
  compiler reports every rule that must account for it. If a task seems to need a
  construct the vocabulary lacks, that is the deferred open-vocabulary trigger firing, and
  it should be raised rather than worked around.
