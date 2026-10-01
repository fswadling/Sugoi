---

description: "Task list for Cue Core implementation"
---

# Tasks: Cue Core

**Input**: Design documents from `/specs/046-cue-core/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/cue-engine.md, quickstart.md

**Tests**: **Required.** The spec makes tests part of the deliverable — FR-016 (exercisable headlessly),
FR-018 (`dotnet test` passes with new tests covering the stories as pure logic) and SC-008/SC-009 make the headless
suite the authoring tool. Every story phase therefore has test tasks.

**This list supersedes the first one.** An earlier implementation of this feature (an authored cue beside a run of
frames with an `Outcome`, and a `splice`) was landed and then retired in favour of the model where the cue *is* its
run; these tasks are the model the spec, plan, research and contract now describe, and they replace that code
rather than adding to it.

**Organization**: Tasks are grouped by user story so each story's *behaviour* is independently assertable. This is
a deliberately small, tightly-coupled feature: the three stories are three behaviours of one type (`world/Cue.fs`)
and one engine (`world/CueEngine.fs`), so **US1–US3 all add arms to the same two source files and the same test
file** and are therefore sequential, not parallel, despite being separate stories. That is the feature's shape, not
a shortcut — it is stated under *Dependencies* so it is not mistaken for a missing independence. `[P]` appears only
where the files are disjoint.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Single project at the repository root (this project has no `src/`): game sources under `world/`, `state/`,
`screen/`; tests under `test/`; documentation under `docs/`. F# compiles in `.fsproj` list order, which this
feature's correctness depends on (see T004).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Scaffolding so the three new files exist and the build stays green before any behaviour lands.

- [X] T001 [P] Create `world/Cue.fs` as `module Cue`, opening `System.Numerics` and `Prime` (empty body)
- [X] T002 [P] Create `world/CueEngine.fs` as `module CueEngine`, opening `Prime`, `Cue`, `GameplayState`, `SharedState` (empty body)
- [X] T003 [P] Create `test/CueTests.fs` as `module CueTests`, opening `Xunit`, `System`, `System.Collections.Generic`, `System.Numerics`, `Prime`, `Cue`, `CueEngine` (empty body)
- [X] T004 Add `Compile` entries to `fsharp-game.fsproj`: `world/Cue.fs` after `world/ZoneRegistry.fs`, and `world/CueEngine.fs` **after `state/GameplayState.fs`** and before `editor/Spawning.fs` — with the ordering-inversion comment the contract asks for
- [X] T005 [P] Add `Compile` entry for `test/CueTests.fs` to `test/fsharp-game.Tests.fsproj` (after `NarrativePerformanceTests.fs`)
- [X] T006 Confirm `dotnet build` is clean (warnings are errors) with the empty scaffolding in `fsharp-game.fsproj` and `test/fsharp-game.Tests.fsproj`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The one type and the fields every story reads. **No story can be implemented until this is done.**

**⚠️ CRITICAL**: `Cue` is what US1–US3 all name and what the state holds; nothing else can compile without it.

- [X] T007 Define `Cue` in `world/Cue.fs` (data-model.md): `Fin`; `Sequence of current: Cue * rest: Cue FDeque`; `Parallel of left * right`; `Fork of inner`; `Wait of seconds`; `WaitState of seconds * endsAt`; `Exposit of text`; `SwitchZone of zone`; `MoveCamera of target: CameraPose * duration`; `MoveCameraState of from * target * duration * endsAt`; and `[<Struct>] CameraPose = { Position: Vector3; Target: Vector3 }`
- [X] T008 Add `mutable Cue : Cue.Cue` (seeded `Fin`) and `mutable PlayTime : float` (seeded `0.0`) to the `GameplayState` record and to `GameplayState.create` in `state/GameplayState.fs` — and **remove** the retired `setCue`/`Run`/`CueStarted` if present, since there is no separate run (FR-013)
- [X] T009 Shape the two hand-built `GameplayState` record literals in `test/SpawningTests.fs` and `test/SelectionTests.fs` for the new fields (`Cue = Fin`; `PlayTime = 0.0`)

**Checkpoint**: `dotnet build` is clean; a `Cue` can be named and constructed, and a state holds one.

---

## Phase 3: User Story 1 - A cue runs as written, and shrinks to Fin (Priority: P1) 🎯 MVP

**Goal**: A cue written as a sequence (with branches) performs each op at the time it asks for, no-time ops cost
none, and the cue is `Fin` when it is done — and `Fin` rolls.

**Independent Test**: Build a cue in code, put it on a state's `Cue`, step it against a scripted clock, and assert
the cue after each step — no editor, no trigger, no GPU.

### Implementation for User Story 1

- [X] T010 [US1] Implement `advance` in `world/CueEngine.fs`: the walk over `Fin` (returns itself), `Exposit` (performed where reached, then `Fin`), `Wait`/`WaitState` (begun, then the deadline), and `Sequence` via `stepSequence` — which drops a finished front, pulls the next from `rest` and carries on **in the same tick** (tail-recursively, so a long run of instant ops does not grow the stack), and returns **the value it was given** when nothing moved. Include `stepParallel` for `Parallel` (a `Fin` side drops out; the point is over when both are). Thread `shared` and `state` (T017 needs `shared`). A `Fork` reached here raises for now (T015 handles it)
- [X] T011 [US1] Implement `tick` in `world/CueEngine.fs`: `state.Cue <- advance shared &state now state.Cue`, and nothing else. There is no start — setting the cue is the beginning
- [X] T012 [US1] Wire the play chain in `screen/GameplayScreen.fs`: inside the existing `if not state.Editing` block, after `Systems.animate`, advance `state.PlayTime` by `float frame.Dt` then call `CueEngine.tick shared &state state.PlayTime` (two lines; research D5)
- [X] T013 [US1] Tests in `test/CueTests.fs` (build a headless `GameplayState`/`SharedState` as `test/GameplayStateTests.fs` does): "wait two, an instant op, wait one" performs the instant op at t=2 and is `Fin` at t=3; two branches reach `Fin` together; a cue of no-progress ops is `Fin` within one step; `Fin` stepped again stays `Fin` (SC-001, SC-002, SC-010); the same authored value run twice reaches the same state (FR-001); two runs against the same clock agree after every step (SC-009)
- [X] T014 [US1] Tests in `test/CueTests.fs`: a `MoveCamera` really moves `state.Camera` and interpolates, and a non-positive duration is over before it begins (SC-010's fourth shape)

**Checkpoint**: User Story 1 is fully functional and testable on its own — this is the MVP.

---

## Phase 4: User Story 2 - A cue can be forked (Priority: P2)

**Goal**: A cue runs beside another without disturbing it — a `Fork` written in a sequence, and a cue arriving from
outside.

**Independent Test**: From a running cue, fork a second in; assert the forked cue runs and the running cue's
remaining time is unchanged.

### Implementation for User Story 2

- [X] T015 [US2] In `world/CueEngine.fs`: handle `Fork` in `stepSequence`'s front — detach to `Parallel(Sequence(next, rest'), inner)` (the remainder on the left, as the reference has it) and advance both in the same tick — and add `forkInto joined current = match current with Fin -> joined | _ -> Parallel(current, joined)`. Replace the US1 placeholder raise with the malformed-`Fork` raise (a `Fork` reached *outside* a sequence's front still raises, FR-014)
- [X] T016 [US2] Tests in `test/CueTests.fs`: a `Fork` in a sequence detaches and both sides run, the cue not ending until both have (SC-005); `forkInto` runs an arriving cue beside the running one and the running op's remaining time is **unchanged** (0 ms); forking into `Fin` makes the arriving cue the cue; the forked value carries no knowledge of its host and can be forked in again elsewhere; and **past forks do not accumulate** — a thousand instant forks, each ticked out, leave the cue exactly as it was (FR-011)

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - The cue is the screen's, and it is frozen while editing (Priority: P3)

**Goal**: A cue switches zone and keeps running; and it advances only while playing.

**Independent Test**: Run a cue that switches zone mid-sequence, assert every later op still happens and nothing
before the switch is lost; open and close the editor and assert the cue resumed.

### Implementation for User Story 3

- [X] T017 [US3] Add the `SwitchZone` arm to the walk in `world/CueEngine.fs`: where the node is reached it calls `GameplayState.switchZone shared &state name`, reusing that function's existing raise for an unknown zone (FR-008, FR-014). Because the cue lives on the screen (T008), the switch does not disturb it
- [X] T018 [US3] Tests in `test/CueTests.fs`: a `SwitchZone` between two waits switches at its place and everything after still runs (0 dropped, 0 duplicated, SC-006); a detached branch's switch while the sequence waits leaves both running; a switch really changes `state.ActiveZone` against zones built as `test/GameplayStateTests.fs` builds them, and an unknown zone raises; the editor being open does not advance the cue and Play resumes it rather than restarting (FR-002, SC-007)

**Checkpoint**: All three user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: The two properties that belong to no single story, the documentation the spec moves with the feature,
and end-to-end validation.

- [X] T019 Allocation properties (FR-012, SC-004) in `test/CueTests.fs`, measured with `GC.GetAllocatedBytesForCurrentThread` the way the numbers below were first measured: 2000 ticks with an op in flight allocate **0 bytes** (flat and several sequences deep) and 2000 ticks on `Fin` allocate **0 bytes**; 2000 ticks each completing an op allocate a bounded amount per op that does not grow with the cue's length. (The first measurement's 109–111 B flat is the *measured* value; the test asserts the bound rather than pinning the figure.)
- [X] T020 Bounded-shape properties (FR-011, SC-003) in `test/CueTests.fs`: peak live nodes are the same for a 4-step and a 40-step cue, **and unchanged after ten thousand steps**; a cue of 200,000 no-progress ops is consumed by one `tick` without a stack overflow
- [X] T021 [P] Amend `docs/ARCHITECTURE.md`: move the cue from `Shared_State` to the gameplay screen in the state diagram, and replace the "Scenario & Cue" section's retired `{ template_root, state_root, state_map }` / `CueState` description with the cue that *is* its run, shrinking to `Fin` (research D8)
- [X] T022 [P] Amend `docs/EDITOR_DESIGN.md` D-010: there is no template/runtime split and no separate run — the cue is an immutable value replaced as it runs, and its history is its own, not a zone's (research D8)
- [X] T023 [P] Amend `docs/ROADMAP.md` phase 9: its deliverables and its "done when" (the engine here, the editor next), and the next-step line that points at this feature (research D8)
- [X] T024 [P] Amend `docs/PROJECT_STRUCTURE.md`: list `world/Cue.fs` (the one type) and `world/CueEngine.fs` (the engine) for what they are (research D8)
- [X] T025 [P] Correct the Status of `docs/CUE_MODEL_DECISION.md`: it records the *first* model (an authored cue beside a run of frames) and is kept for its measurements and the route, not as the design (research D8)
- [X] T026 Run `dotnet build` (warnings-clean) and `dotnet test` (green, including the allocation assertions), then validate `quickstart.md` — the scratch tick prints the cue shrinking to `Fin`, with `Exposit "hello"` written to `Dialogue` once at t=2

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately. T004 depends on T001/T002 existing; T005 on T003.
- **Foundational (Phase 2)**: Depends on Setup — **BLOCKS all user stories**.
- **User Stories (Phases 3–5)**: All depend on Foundational. In a single-threaded effort they run in priority order (US1 → US2 → US3).
- **Polish (Phase 6)**: Depends on all desired stories being complete.

### User Story Dependencies

- **US1 (P1)**: after Foundational. No dependency on other stories. **MVP.**
- **US2 (P2)**: after Foundational; T015 fills in the `Fork` arm US1 left raising, and its tests exercise a running cue, so in practice after US1's walk (T010–T012).
- **US3 (P3)**: after Foundational; adds the `SwitchZone` arm to US1's walk (T017 depends on T010).

### ⚠️ Why US1–US3 Are Sequential Here

Each story is *independently testable* (that is what the story phases buy), but they are not *independently
parallelizable*: every story's implementation lands in `world/CueEngine.fs` and every story's tests land in
`test/CueTests.fs`. Two tasks editing those files cannot run at once, so `[P]` appears only on tasks that touch
distinct files (the Setup scaffolding, and the five docs). Do not read the absence of `[P]` across stories as a
missing decomposition.

### Within Each User Story

- Implementation before its tests; the tests assert the behaviour the same phase just added.
- The story's own checkpoint is a validation stop.

### Parallel Opportunities

- **Phase 1**: T001, T002, T003 create three different files and can go together; T005 (test `.fsproj`) can go with any of them.
- **Phase 6**: T021, T022, T023, T024 and T025 amend five different documents and can go together.
- Nothing else: the engine and the suite are each one file.

---

## Parallel Example: Setup

```bash
# T001, T002, T003 and T005 touch four different files:
Task: "Create world/Cue.fs as module Cue"
Task: "Create world/CueEngine.fs as module CueEngine"
Task: "Create test/CueTests.fs as module CueTests"
Task: "Add test/CueTests.fs Compile entry to test/fsharp-game.Tests.fsproj"
```

## Parallel Example: Documentation

```bash
# T021–T025 touch five different documents:
Task: "Amend docs/ARCHITECTURE.md (state diagram + Scenario & Cue)"
Task: "Amend docs/EDITOR_DESIGN.md D-010"
Task: "Amend docs/ROADMAP.md phase 9"
Task: "Amend docs/PROJECT_STRUCTURE.md"
Task: "Correct the Status of docs/CUE_MODEL_DECISION.md"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — blocks everything).
3. Complete Phase 3: US1 — the type, the walk, `tick`, the play-chain wiring, and their tests.
4. **STOP and VALIDATE**: `dotnet test` green for US1; a cue runs as written and shrinks to `Fin`, headlessly.
5. This is the MVP: the engine half of phase 9's capability, with no editor.

### Incremental Delivery

1. Setup + Foundational → a `Cue` exists and the state holds one; the build is clean.
2. US1 → a cue runs as written and shrinks to `Fin` → **MVP / demo**.
3. US2 → a cue can be forked beside another.
4. US3 → a cue survives a zone switch and is frozen while editing.
5. Polish → the two allocation/shape properties are *tested*, the five docs match the code, `dotnet test` green.

### Notes

- Every task names the file it changes; tasks on `world/CueEngine.fs` or `test/CueTests.fs` are sequential by construction.
- Determinism (FR-015) is per-program (same cue, same clock, same state) — see research D4; do not write a test that claims bitwise agreement across machines.
- `world/CueEngine.fs` is compiled **after** `state/GameplayState.fs` on purpose (T004). If a change there starts naming zones, screens or assets beyond `switchZone`, that is the design drifting, not a convenience.
- **Do not put the op in flight back into the deque.** `Sequence of Cue FDeque` is the tempting, literal reading and it costs 72 bytes per idle tick per level (research D2). `Sequence(current, rest)` is the decision.
