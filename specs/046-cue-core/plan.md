# Implementation Plan: Cue Core

**Branch**: `046-cue-core` | **Date**: 2026-09-30 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/046-cue-core/spec.md`

## Summary

The engine half of phase 9, in the model where **the cue *is* its run**. One new type — `Cue` — is both the
authored vocabulary and the execution state: `Fin` (the terminal), `Sequence(current, rest)`, `Parallel`, `Fork`,
the leaves, and a `…State` case per timed leaf. Two new modules — `world/Cue.fs` (the type) and
`world/CueEngine.fs` (`advance` / `tick` / `forkInto`) — and nothing else. The ops perform their effects where the
walk reaches them, so `advance` takes `(shared, &state, now)` and returns the next cue; `tick` is
`state.Cue <- advance shared &state now state.Cue`, and that — plus advancing `PlayTime` — is the whole of the play
chain's cue work.

There is no start: setting the cue's state is the beginning, and `Fin` is both the third state ("finished") and the
fixed point ("advancing it is itself"), which is what removes the need for a started flag. There is no join and no
splice — an arriving cue is **forked** in, running beside what is already running. The run lives on the gameplay
screen beside the cue: `mutable Cue` and a `mutable PlayTime` that only advances while playing, which is what makes
pressing Play resume a cue rather than leap it forward. Two lines enter the play chain — advance play time, tick the
cue — after `Systems.animate`, following the reference's ordering (`… clock → cue`). `CueEngine` is compiled *after*
`state/GameplayState.fs`, because the ops switch zone and write the screen's dialogue and camera.

No state array, no `Frame`, no `Outcome`, no handles, no capacity bound, no request type, no new dependency (Prime
is already referenced), no editor, no undo, no persistence, and no change to any existing system, op or asset. An
idle tick allocates nothing (measured, flat and nested, and on `Fin`), and a completed op costs a small, bounded
amount that does not grow with the cue's length. The suite asserts the 0 bytes exactly and the per-op cost as a
*bound* (it does not pin the byte figure, which would be brittle).

## Technical Context

**Language/Version**: F# on .NET 10 (`net10.0`), `TreatWarningsAsErrors`-clean build

**Primary Dependencies**: Prime (for `FDeque` — **already** referenced by `world/Assets.fs`, `world/ActorAsset.fs`
and `test/DialogueTests.fs`, so this adds no package); SDL3, Dear ImGui and Jolt Physics are untouched

**Storage**: none — a cue is not persisted in this feature (phase 15), and a run never is

**Testing**: xUnit via `dotnet test` (headless). The suite builds a `GameplayState` the way
`test/GameplayStateTests.fs` already does — `Unchecked.defaultof` for the GPU pieces, a `SharedState` from an asset
manager — so every acceptance scenario, including the ops' effects, is assertable without a device; a cue's *walk*
is asserted through the value it is (`advance`)

**Target Platform**: Linux desktop (SDL_gpu/Vulkan)

**Project Type**: desktop game with an in-engine editor (single project; one `.fsproj` per the app and its tests)

**Performance Goals**: 60 fps. A cue that is waiting costs **0 bytes per frame** — the game loop must not pay for a
cue in flight — and a completed op costs a small, bounded amount that does not grow with the cue's length.
*Measured*: idle tick 0 B (flat and three sequences deep) and 0 B on
`Fin`; 109–111 B per completed op flat — the scratch sketch that took these numbers has since been removed, and
`test/CueTests.fs` now asserts them. **The counter-example is recorded too**: drawing the running op *inside* the
sequence's deque cost **72 B per tick per level**, because `FDeque.uncons` allocates the tail on the idle path —
which is why the op in flight is held out of the deque.

**Constraints**: the constitution's five principles; the cue is a value, and a step returns the next value without
mutating it; the engine is compiled **after** `state/GameplayState.fs` (its ops switch zone and write the screen's
dialogue and camera); the cue advances **last** in the play chain; the cue must survive a zone switch; the clock is
play time (a float64 summing the frame's float32 `Dt`, exact for a given `Dt` sequence — research D4); the docs the
spec lists must move with the feature

**Scale/Scope**: two new modules (~200 lines between them), two fields on `GameplayState`, two lines in the play
chain, one new test file, and five amendments to existing docs. No new module in `state/`, `editor/`, `screen/` or
`world/Systems.fs`

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — **one** type where the earlier arrangements had two, and two modules the `docs/PROJECT_STRUCTURE.md` already anticipates for the cue. There is no new mechanism at all: the ops perform their effects where the walk reaches them, as the port's play systems do, instead of asking for them; a step that changes nothing reports `ValueNone` — a struct, so it costs the idle path nothing — rather than a bespoke wrapper type; and `Fin` removes the started flag the previous design needed. The state array the roadmap sketched (`state_map`, `CueState`) is **not** built — it was measured and retired (0 bytes per frame either way; 72 per completed op against this design's cost, taken against a fixed 64-frame capacity that fails loudly in a shipped game). The editor, the cue's undo history, persistence, joining and the rest of the op family are deferred, each with a stated trigger. |
| II. Fail Loudly | Pass — a malformed cue (a `Fork` outside a sequence's current op) raises where it is reached, an unknown zone already raises in `GameplayState.switchZone`, and nothing here adds a fallback or a silent default. A finished cue is `Fin`, not a silently dropped absence. |
| III. Spec-First | Pass — `specs/046-cue-core/spec.md` is written and drives this plan. |
| IV. Data-Oriented Architecture | Pass — one discriminated union, no inheritance, no object graph, no component registry. The run is never mutated — a step returns the next value — and the mutation this feature does add is each op's effect on the state it was handed, written where the walk reaches it: exactly the "explicit and localized" form the principle asks for, and the form `playerInput` and `cameraFollow` already use. **One divergence, measured rather than preferred**: this feature does not keep the run in a contiguous array, because the constitution's guidance is about the hot path and the array was *slower* there than a cue value of a few nodes (research D2). |
| V. Minimise GC Pressure | Pass — measured: 0 bytes per idle tick at any nesting depth and on `Fin`, and a bounded amount per completed op. Two disciplines keep it, and both are in the plan rather than left to chance: a step that changes nothing reports nothing to change (`ValueNone`, a struct — no rebuilt value, no `Outcome`), and the op in flight is held **out** of the sequence's deque (the 72 B/tick counter-example). Neither is a caller's responsibility: `tick` owns the state's cue. |
| N-V. Testing is the Authoring Tool | Not engaged as written (the narrative subsystem is untouched), but its requirement is adopted rather than dodged: the engine is exercisable with no graphical interface and its behaviour is asserted headlessly (spec FR-016 / SC-008), which is the only way this project can demonstrate a cue at all. |

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

None. The design is simpler than each arrangement it replaces — no state map, no handles, no capacity bound, no
second type, no started flag — and the one place it diverges from the constitution's letter (contiguity,
Principle IV) is a divergence *towards* less machinery, justified by measurement: the contiguous form was built,
cost more per op, and bought nothing on the tick path where it was supposed to pay (`docs/CUE_MODEL_DECISION.md`).

## Project Structure

### Documentation (this feature)

```text
specs/046-cue-core/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── cue-engine.md    # Phase 1 output — the surfaces that change
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
world/
├── Cue.fs               # NEW: the whole model in one type — `Fin`, `Sequence(current, rest)`, `Parallel`,
│                        #   `Fork`, the leaves (`Wait`/`WaitState`, `Exposit`, `SwitchZone`,
│                        #   `MoveCamera`/`MoveCameraState`) and `CameraPose`. Needs `Prime` and
│                        #   `System.Numerics` only, so it names no zone, screen, asset or device.
├── CueEngine.fs         # NEW — and listed *after* `state/GameplayState.fs`, because the ops perform their
│                        #   effects on the gameplay state where the walk reaches them: `advance` (one cue in,
│                        #   the next out), `tick` (the state's cue, in place) and `forkInto` (an arriving cue
│                        #   beside the running one). Names `SharedState` only for the zone switch it reuses.
└── Systems.fs           # UNCHANGED: the play systems stay as they are; the cue runs last, beside them.
state/
└── GameplayState.fs     # CHANGED: `mutable Cue : Cue.Cue` (the run itself; `Fin` when nothing runs) and
                         #   `mutable PlayTime : float`. No `setCue`: setting the cue *is* the beginning.
screen/
└── GameplayScreen.fs    # CHANGED: in the existing `not state.Editing` block — after `Systems.animate`, two
                         #   lines: advance `PlayTime` by the frame's `Dt`, then `CueEngine.tick`, following the
                         #   reference's `… clock → cue` order.
test/
└── CueTests.fs          # NEW: the headless suite — the three stories, the edge cases, and the allocation and
                         #   bounded-shape properties.
docs/
├── ARCHITECTURE.md      # CHANGED: move the cue from `Shared_State` to the gameplay screen in the state diagram,
│                        #   and replace the "Scenario & Cue" section's retired `Cue`/`CueState`/`state_map`
│                        #   description with the cue that *is* its run, shrinking to `Fin`.
├── EDITOR_DESIGN.md     # CHANGED: D-010 — no template/runtime split and no separate run; the cue is an
│                        #   immutable value replaced as it runs, and its history is its own, not a zone's.
├── ROADMAP.md           # CHANGED: phase 9's deliverables and its "done when" (the engine here, the editor
│                        #   next), and the next-step line that points at this feature.
├── PROJECT_STRUCTURE.md # CHANGED: `world/Cue.fs` and `world/CueEngine.fs` listed for what they are.
└── CUE_MODEL_DECISION.md # CHANGED: Status corrected — it now records the *first* model (an authored cue beside a
                         #   run of frames), kept for its measurements and the route to this one.
fsharp-game.fsproj        # CHANGED: `world/Cue.fs` before `state/GameplayState.fs` (which names `Cue`), and
                          #   `world/CueEngine.fs` after it (it performs the ops' effects on the state) and
                          #   before `screen/GameplayScreen.fs` (which calls it).
```

**Structure Decision**: the type and the engine go in **`world/`** because a cue is authored world data like a zone,
an actor or a waypoint, and `docs/PROJECT_STRUCTURE.md` already reserves `world/Cue.fs` for it. `Cue.fs` names no
screen, zone, asset or device, so it is compiled before `state/GameplayState.fs` (which names `Cue`) and stays
loadable on its own. **The engine performs its ops' effects on the gameplay state where the walk reaches them** —
an exposition writes `Dialogue`, a zone switch reuses `switchZone`, a camera move writes `Camera` — so
`CueEngine.fs` is compiled *after* `state/GameplayState.fs` and names it. This is the project's one deliberate
ordering inversion (a `world/` file below `state/`), and the `.fsproj` says why. Its consequence is stated rather
than hidden: the engine is not loadable without the game, so the headless suite builds a `GameplayState` the way
`test/GameplayStateTests.fs` already does and asserts the **state an op changed** rather than a request for it —
a stronger assertion, at the cost of a standalone FSI walkthrough. There is no separate authored cue to keep beside
the run, so there is no `live`/`current` derivation and no `setCue`: the screen's cue *is* the run. Nothing is added
to `world/Systems.fs`, `world/Zone.fs`, `world/Actor.fs`, the editor, the render path or the physics world.

## Phase 0: Research

See [research.md](./research.md). It settles the first cut of the vocabulary (D1), the cue that *is* its run and why
`Fin` is the third state (D2), the step that performs its ops' effects where it reaches them (D3), the clock and why
it must be *play* time and absolute (D4), where the step sits in the chain and why it needs no start (D5), the two
allocation disciplines the loop depends on (D6), what the headless suite can assert (D7), and the documentation that
moves with the feature (D8).

## Phase 1: Design

- [data-model.md](./data-model.md) — the one type (with the `…State` rule and why `current` is out of the deque),
  the engine's signatures, the two fields on `GameplayState`, the cue's state transitions with `Fin`, and its
  validation rules.
- [contracts/cue-engine.md](./contracts/cue-engine.md) — every surface that changes (`world/Cue.fs`,
  `world/CueEngine.fs`, `state/GameplayState.fs`, `screen/GameplayScreen.fs`, the `.fsproj`, the test file, the
  five docs) and what explicitly does not (`world/Systems.fs`, the zone and actor modules, the editor, the render
  path, physics, and the rest of the op family).
- [quickstart.md](./quickstart.md) — runnable validation: the headless suite, and a scratch-test walkthrough that
  shows the state's cue shrinking to `Fin`.

## Post-Design Constitution Re-Check

| Principle | Assessment |
|---|---|
| I. Minimalism | Pass — two modules, two fields, one engine function the chain calls, two chain lines. One type, no request type, no applier, no started flag, no separate run. |
| II. Fail Loudly | Pass — malformed cue and unknown zone both raise, in the op that is reached and in `switchZone` respectively; no default, no silent drop. |
| III. Spec-First | Pass. |
| IV. Data-Oriented Architecture | Pass — one DU, `[<Struct>]` `CameraPose`, `FDeque` for the queue; the array divergence is measured and documented (D2). |
| V. Minimise GC Pressure | Pass, and it is a *tested* property rather than an intention: FR-012's assertions are in the suite (0 B idle and on `Fin`, bounded per op), and both disciplines that hold it (D6) are recorded in the code and in the note. |
| N-V. Testing is the Authoring Tool | Adopted in substance — D3 and D7 exist so that a cue's behaviour, including the state it changed, is assertable with no device. |
