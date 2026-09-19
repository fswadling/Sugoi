# fsharp-game Constitution

## Core Principles

### I. Minimalism
Write only what is needed. Prefer small, focused modules over large,
speculative abstractions. No just-in-case fallbacks unless they are part of an
expected use case.

### II. Fail Loudly
Unexpected state is a bug. Prefer exceptions/asserts over silently returning
defaults. Use `Result`/`Option` for *expected* failure paths and
`failwith`/`invalidOp`/`assert` where an error is unrecoverable.

### III. Spec-First
Every feature starts as a spec under `specs/<NNN>-<name>/spec.md`, then moves
through plan, tasks, implement, and converge. Code is written to satisfy a
spec, not before one exists.

### IV. Data-Oriented Architecture
Prefer records and discriminated unions over inheritance and heavy object
graphs. F# supports structs, which enables the same generational-slot-map and
struct-of-arrays patterns used in `odin-game`. Use `[<Struct>]` records and
struct tuples for hot-path data created and consumed every frame, keep hot-path
data contiguous (`array`, `Span`, `Memory`), and make mutation explicit and
localized.

### V. Minimise GC Pressure
The game loop runs every frame. Prefer structs, value options, and reused
buffers in hot paths to avoid per-frame allocations and GC pauses. Do not
contort code to avoid allocation in cold paths — clarity first, then measure.

## Narrative Subsystem Principles

These govern the narrative engine — `specs/025-narrative-core` and whatever
succeeds it. They add to the Core Principles above for that subsystem only; the
game as a whole remains governed by the five above. Where a Core Principle
already covers the ground, these say what it means here: Minimalism I carries
its cast-size constraint (at most 20 characters, and no mechanism that only pays
off at a larger scale), and Fail Loudly II means a bound, once reached, is a
failure to report rather than a condition to tolerate.

### N-I. History is Immutable, Facts are Interpretation

Committed events are the only source of historical truth and are never
rewritten, deleted, or reordered — not by later events, and not when rules
change. Derived facts are the current rule set's interpretation of that history;
they may appear, change, and disappear. Nothing may write an interpretation into
history. Replay of the same history under the same rules must reconstruct the
same state, and replay under different rules must still accept the existing
history as given.

The interface must make this structural rather than conventional: no way to
assert a fact, and no way to alter a committed event, is offered at all.

### N-II. Deterministic by Construction

Given identical history and identical rules, every observable output — derived
facts, their reasons, and their ordering — is identical across runs. No
observable output may depend on unordered iteration, hashing, timing, or ambient
state. Canonical text is the ordering key wherever output is produced, so
determinism is a property of the data structures rather than a discipline the
code must remember to observe.

### N-III. Modular Authorship on a Shared Substrate

A narrative unit — one thread of story — keeps its rules together, and
everything it concludes is available to every other unit and to the game:
nothing scopes events or facts to a unit, an interaction, or a subset of the
cast.

The vocabulary is closed. Adding a construct a unit needs is an edit in one
shared place rather than something a unit does for itself; that cost is accepted
in exchange for the host language checking a rule's category and construct kind,
and for the engine deriving each construct's name, arity, and argument types
rather than maintaining them alongside. Opening the vocabulary is a recorded
deferral with a stated trigger, not a gap.

Rule order must not be observable. A rule set assembled by concatenating units
produces identical output in any concatenation order, so units are genuinely
independent rather than accidentally coupled by the order they were listed in. A
change that makes unit order observable has broken the substrate, not the unit.

### N-IV. Attribute Every Conclusion

Every derived fact records every rule that produced it and the premises each
rule applied to, so a conclusion with several independent justifications keeps
all of them. A conclusion that cannot be attributed to a rule is not fit to be
shown to a player or acted on. Identical reasons are never retained twice, and a
reason whose premises have ceased to hold is no longer offered. A fact's reasons
are retrievable while the game is running, not only from a scenario run.

Turning those reasons into a readable explanation — terminating, citing only
premises that currently hold, expanding a conclusion once where many reasoning
paths rejoin — is not required by any ratified spec yet. A future feature that
adds it must not write an explanation into history.

### N-V. Testing is the Authoring Tool

Rules are developed by running headless scenario suites, not by reproducing
cases by hand in the game. The engine must be exercisable without any graphical
interface, over parameterised and generated scenarios, with results reported in
a stable, diffable form that a failing assertion can attach in full. A rule
change that cannot be observed and asserted in a scenario run is not finished.

## Technology Stack

- Language: F#
- Build: .NET (`dotnet`)
- Windowing/Graphics: SDL3 (via the `SDL3-CS` .NET bindings)
- Editor UI: Dear ImGui (via a custom cimgui binding + SDL_gpu backend, `ImGui.fs`/`ImGuiSdl3.fs` + native `libcimgui.so`)
- Physics: Jolt Physics (via the `JoltPhysicsSharp` .NET bindings)
- Spec workflow: Spec Kit ("speckit")

## Development Workflow

- Feature specs live in `specs/`; the active feature is recorded in
  `.specify/feature.json` (override with `SPECIFY_FEATURE_DIRECTORY`).
- Run `/speckit-specify` to create a spec, `/speckit-plan` for design,
  `/speckit-tasks` for the task list, `/speckit-implement` to build, and
  `/speckit-converge` to verify completeness.
- Build with `dotnet build`, run with `dotnet run`, test with `dotnet test`.

## Governance

- This constitution supersedes other ad-hoc practices.
- Amendments require a documented rationale and a migration plan where the
  change affects existing specs or code.
- Complexity must be justified against the Minimalism principle.

## Amendments

### 2026-09-06 — Physics backend: Box3D → Jolt Physics

- **Rationale**: `Box3D` was referenced but never used in code. The physics +
  picking phase (`specs/013-physics-picking`) and later locomotion need a
  full-featured rigid-body engine; Jolt Physics provides a BVH broadphase,
  capsule/box/hull/mesh/heightfield shapes, ray casts, and a character
  controller. The maintained .NET binding is `JoltPhysicsSharp`.
- **Migration plan**: Replace the unused `Box3D` NuGet reference with
  `JoltPhysicsSharp` (+ `JoltPhysics.Native`). No code depends on `Box3D`, so
  there is no source migration.

### 2026-09-16 — Narrative subsystem principles

- **Rationale**: The narrative engine's principles were ratified in a separate
  prototype project before that engine was brought into this repo as spec 025.
  The spec's quality checklist was validating against those principles while they
existed only there, so plan, tasks and converge were each validating it against
  nothing. They are carried here so they are stated once rather than restated in
  every narrative spec, and because the capabilities queued behind this one —
  proposals, scenes, temporal rules, decay — all owe the same invariants. Two
  were corrected in transit rather than copied: modular authorship no longer
  claims a unit adds vocabulary without editing the engine, because the
  vocabulary is now closed and that cost is accepted deliberately; and
  attribution now records that a fact's reasons are retrievable while the game
  runs, not only from a scenario run.
- **Migration plan**: None needed. The section is scoped to the narrative
  subsystem, so the five Core Principles and the 24 specs already satisfying them
  are unchanged.

**Version**: 1.2.0 | **Ratified**: 2026-08-27 | **Last Amended**: 2026-09-16
