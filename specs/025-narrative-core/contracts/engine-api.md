# Contract: Narrative Engine API

**Feature**: `025-narrative-core` | **Date**: 2026-09-16 | **Spec**: [spec.md](../spec.md) | **Data model**: [data-model.md](../data-model.md)

The surface the game and the headless harness both consume. This is an in-process
library contract: there is no wire format, no serialisation protocol between processes,
and no service boundary (spec Assumptions, "Boundary of this subsystem").

Two callers exercise it and neither is privileged: the game, which commits events as
they happen and reads back what is currently true, and the harness, which commits events
from a scenario with no window at all. The engine must not behave differently according
to which one it is, or why an event was supplied (spec FR-005, FR-032).

---

## 1. Vocabulary

```fsharp
namespace Narrative

/// The roster: the four characters who can exist, as cases (research D28). Since 2026-09-20 it is
/// `cast/Roster.fs`'s — hoisted out of this namespace so the engine and the game share one
/// declaration (specs/032-cast-binding) — and it was twenty cases before that.
type Character = Aiko | Akane | Shinji | Yuki

type Trait = Shy | HotHeaded

type Event = /* 8 constructs — see data-model.md §1 */

type Fact  = /* 7 constructs — see data-model.md §1 */

/// What a reason applied to: an event a projection read, or a fact a derivation read.
type Premise = FromEvent of Event | FromFact of Fact

/// One reason: the rule, and the premises it applied to. Two reasons are the same when the same
/// rule applied to the same premises — F#'s structural comparison, not their text (research
/// D34). `Premises` is held in the order the rule bound them.
type Reason = { Rule : string; Premises : Premise list }
```

Closed by design (research D20). A narrative unit needs a construct the vocabulary lacks
by editing this file — one case, in one place, with the type system then reporting every
rule that has to account for it.

---

## 2. Authoring rules

```fsharp
/// A projection: history to facts. Establishes on one event, optionally withdraws on another.
val project  : name:string -> ProjectionBuilder

/// A derivation: facts to facts. Premises are bound with `let!` and captured by the builder.
val derive   : name:string -> DerivationBuilder
```

Both builders are computation expressions (research D24). Neither can be used to write a
rule of the other's shape, and neither can name a construct the vocabulary lacks, because
both are ordinary F# expressions over `Event` and `Fact`. Nothing therefore validates
rules at runtime, because nothing invalid can be written — the guarantee the spec records
in Assumptions as a property of the approach rather than as a requirement the engine
satisfies (research D10).

A rule's name is the identity in its reasons, so two rules sharing a name is a rule set
that cannot be attributed — rejected when the rule set is built (spec FR-010).

---

## 3. Engine

```fsharp
type GameTime = GameTime of int   // minutes since the simulation's origin

type Engine

/// Build an engine over a rule set.
val create : rules:RuleSet -> Engine

/// The same, with an explicit derivation bound. F# permits optional arguments only on type
/// members, so the bound is a second entry point rather than an optional parameter; it
/// exists so the failure path below is reachable deliberately (research D9).
val createBounded : bound:int -> rules:RuleSet -> Engine

/// Commit one event at the game time the caller gives it. Raises `TimeWentBackwards` when that
/// time precedes the last committed one; a commit that raises has changed nothing, so the
/// caller still holds a usable engine. Committing a run is a fold of this.
val commit : Engine -> (GameTime * Event) -> Engine

/// Every derived fact, in the order the values have.
val facts   : Engine -> Fact list

/// Every fact naming this character, in any position, in the same order.
val factsOf : Engine -> Character -> Fact list

/// The reasons currently supporting this fact, each once, ordered by rule.
/// Empty when the fact is not current.
val reasonsFor : Engine -> Fact -> Reason list

/// Committed history, in order.
val history : Engine -> CommittedEvent list
```

Rendering a report is the harness's job, not the engine's — the spec groups it under
the authoring harness (FR-026) — so `report` is defined in § 4 below.

### Invariants the caller may rely on

| Invariant | Source |
|---|---|
| A commit is one event and is whole or nothing | spec FR-013 — an observation must be consistent with the history at the point it is made, and a half-applied commit is not a point |
| `commit` never mutates the value passed in; failure leaves the caller's engine usable | spec FR-013, and it is what makes raising safe |
| Same history and same rules ⇒ identical output, including order and including the set and order of reasons | spec FR-021 |
| Output does not depend on unordered iteration, hashing, arrival timing, wall-clock time or frame cadence | spec FR-022 |
| A fact appears once however many reasons support it; concluding it again changes nothing | spec FR-009 |
| Every reason currently supporting a fact is retained, deduplicated by canonical text | spec FR-010 |
| Retrieval does not filter: withholding anything is the caller's decision | spec FR-015 |
| Replacing the rule set leaves history untouched and rebuilds all current state from it | spec FR-020 |

### Failure modes

Both failures are raised exceptions, not returned values. Raising is safe because `commit`
does not mutate the value passed in: a commit that raises has changed nothing, so the caller
still holds a usable engine (spec FR-013).

| Exception | Meaning | Source |
|---|---|---|
| `TimeWentBackwards` | a game time preceding the previous committed event — a caller defect, because the caller supplies the order (FR-024) | research D8 |
| `DerivationBoundExceeded` | the derivation bound was exhausted, carrying the names of the rules still concluding — reaching it means the rule set is malformed, an authoring defect | spec FR-014, SC-006 |

There is deliberately no exception for an event the vocabulary does not have. The vocabulary
is closed, so such a value cannot be built in code at all — and nothing reads text, so one
cannot arrive that way either.

`TimeWentBackwards` overrides `Message` to name the time and the previous committed time,
because the author has to act on it (spec FR-028); `DerivationBoundExceeded` carries the rules
still concluding. The bound is injectable so that path is tested rather than asserted (spec
FR-014, SC-006).

### Absent by construction

Spec FR-032 requires these be absent rather than discouraged, and they are:

- no way to assert, add, edit or remove a fact
- no way to modify, remove or reorder a committed event
- no way to reach a mutable field of the engine

There is no `assert`, no `addFact`, no `removeEvent`, and `Engine` exposes no mutable
field. A caller cannot construct a state the engine would not have derived.

---

## 4. Harness

```fsharp
type Scenario =
    { Name          : string
      InitialEvents : (GameTime * Event) list
      Steps         : (GameTime * Event) list }

/// Run a scenario with no graphical interface.
val run : rules:RuleSet -> Scenario -> RunResult
val runBounded : bound:int -> rules:RuleSet -> Scenario -> RunResult

/// History and facts with their reasons, in canonical text order and stable form.
/// This is the one route to a rendering: `RunResult.Report` is this applied to
/// the run's engine, so an assertion and a caller's display cannot diverge.
val report : Engine -> string

type RunResult =
    { History : CommittedEvent list
      Facts   : Fact list
      Report  : string }

/// Raise on mismatch, attaching the whole report rather than a bare comparison.
val shouldDerive : RunResult -> Fact list -> unit
```

`run` and `runBounded` drive the ordinary commit path, folding `Scenario.InitialEvents` and then
`Scenario.Steps` through `commit`, so they raise the same exceptions as `commit` —
`TimeWentBackwards` or `DerivationBoundExceeded` — rather than returning them.

FR-027 asks for parameterised and generated scenarios, which is `FsCheck`'s job: a
scenario generator is a function from traits and event orders to `Scenario`, so one
situation is exercised across many casts and orderings in a single run.

A failed assertion attaches `Report` whole, so a failure reads as a narrative diff
rather than "expected true, got false" (spec FR-028), and the same reasons are what the
game reads when a player asks why a fact holds (spec FR-010, clarification 2).

---

## 5. What this contract deliberately does not offer

| Not offered | Why |
|---|---|
| Proposals or candidate events | deferred; the engine interprets and never decides (spec FR-032) |
| Scheduling, pacing or advice | same |
| Scenes or containment | deferred |
| Temporal patterns | deferred |
| Prose explanations | reasons are recorded and retrievable; expanding them is deferred (constitution N-IV) |
| Anything a unit can add without editing the vocabulary | the closure, recorded as a deferral with a trigger (research D16, D26) |
