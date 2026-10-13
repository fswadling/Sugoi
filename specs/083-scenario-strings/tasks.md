---

description: "Task list for feature implementation"
---

# Tasks: Scenarios as a String-Keyed Ordered Library

**Input**: Design documents from `/specs/083-scenario-strings/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/scenario-library.md

**Tests**: Included — the project mandates headless verification (constitution N-V; `specs/074`'s stance) and this
feature's success criteria are test-based (`test/ScenarioTests.fs` rewritten, `test/ScenarioForkTests.fs` migrated,
`test/CueTests.fs` and `test/CuePanelTests.fs` migrated).

**Organization**: As with `specs/082`, this is a single cohesive type change: the scenario becomes a string and the
library an ordered map everywhere at once, so the **Foundational** phase migrates the types and every call site (game
and tests) and keeps the solution compiling and green; the story phases then deliver the verifiable increments (the
library, the delete/clear path, the fork, the string cue op, the undo step).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US5)
- Paths are repository-relative.

## Path Conventions

Single project, F# — flat clusters at the repository root: `world/`, `state/`, `editor/`, `test/`. Compile order is
fixed by `fsharp-game.fsproj`; this feature **deletes** one file (`world/Scenario.fs`) and adds none, so exactly one
`<Compile>` entry is removed and nothing else reorders.

---

## Phase 1: Setup (Shared Infrastructure)

- [x] T001 Delete `world/Scenario.fs` and remove its `<Compile Include="world/Scenario.fs" />` line from `fsharp-game.fsproj`; correct the neighbouring `<Compile>` comments for `world/ScenarioRegistry.fs` and `world/Cue.fs` (they still say the vocabulary is a closed union).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Make a scenario a string, make the library `ScenarioRegistry.Registry` (Prime's `OMap`), migrate every
call site and test, and keep the solution compiling. **⚠️ No user-story work can begin until this is complete.**

- [x] T002 [P] Rewrite `world/ScenarioRegistry.fs`: `open Prime`; `type Registry = OMap<string, ScenarioContents option>`; `create`/`names`/`exists`/`tryContents`/`add`/`remove`/`setContents`/`normalise` (trim); **`add` is guarded (a name already present returns the library unchanged, so a duplicate add never clears a registered scenario's contents)**; keep `ScenarioContents` and `capture` unchanged; drop `open Scenario`; rewrite the header comment (a string-keyed ordered library, per `specs/083`)
- [x] T003 [P] `state/GameplayState.fs`: `mutable Scenarios : ScenarioRegistry.Registry`; reword the field comment
- [x] T004 [P] `state/Command.fs`: `SetScenario of Previous : ScenarioRegistry.Registry * Next : ScenarioRegistry.Registry` (whole-library pair); drop `open Scenario`; reword the case comment
- [x] T005 [P] `state/History.fs`: `SetScenario` assigns `state.Scenarios <- next` (apply) / `<- previous` (undo); reword the comment
- [x] T006 [P] `world/Cue.fs`: `ForkScenario of scenario: string`; drop `open Scenario`; correct the header comment
- [x] T007 [P] `state/EditorState.fs`: add `ScenarioNameBuffer : byte[]` beside `AdventNameBuffer` and initialise it in `create`
- [x] T008 `world/CueEngine.fs`: `ForkScenario scenario` reads a string — `ScenarioRegistry.tryContents scenario state.Scenarios`, `noOp "ForkScenario" scenario "it is not registered"`; drop the `Scenario.scenarioName` call (depends T002, T006)
- [x] T009 `editor/ScenarioPanel.fs`: rewrite the panel — a reused `inputText` over `state.Editor.ScenarioNameBuffer`, an **Add** control disabled for a blank-after-trim or already-present name (clearing the field on add), and a **list** of `ScenarioRegistry.names` each with **Register**/**Clear**/**Fork**/**Delete**; add a private `bufferText`; `commit` takes the next `Registry` and `fork` takes a name (depends T002, T007)
- [x] T010 `editor/CueTree.fs`: `withScenario` takes a `string`; `label` renders the raw name (suffix omitted when empty); `defaultOfKind "Fork scenario" -> ForkScenario ""`; drop `open Scenario` (depends T006)
- [x] T011 `editor/CueEditor.fs`: `drawNodeText` gains a `scenarios: string list` parameter and its `ForkScenario` case becomes `CueTree.filteredNameCombo` over those names (pick or type); `draw` and `drawTrigger` pass `ScenarioRegistry.names state.Scenarios` (depends T002, T010)
- [x] T012 [P] Migrate every reference in `test/` so it compiles: `test/ScenarioTests.fs` (rewrite to the string/`Registry` shape), `test/ScenarioForkTests.fs` (`Intro` → `"Intro"`, `ScenarioPanel.commit`/`fork` signatures), `test/CueTests.fs` and `test/CuePanelTests.fs` (`ForkScenario "Intro"`; drop `Scenario.scenarioName` from `describe`; the default op becomes `""`)
- [x] T013 `dotnet build fsharp-game.fsproj` is clean with no warnings (depends T002–T012)
- [x] T014 `dotnet test` — the migrated suite is green with no device (baseline) (depends T013)

**Checkpoint**: a scenario is a string, the library is ordered and string-keyed, the panel lists it, and the solution builds and passes.

---

## Phase 3: User Story 1 - The author names a scenario and files the current scene under it (Priority: P1) 🎯 MVP

**Goal**: The panel's text field + Add records a typed name; Register files the current scene under it; the library
reads in the order names were added.

**Independent Test**: with a headless state and an empty library, add a typed name and assert `ScenarioRegistry.names`
reads exactly it; register and assert the name now has contents; assert trim/blank/duplicate rules.

- [x] T015 [P] [US1] `test/ScenarioTests.fs`: a fresh library is empty; adding a name records it and `names` reads insertion order for several names; a repeated add is a no-op **and never clears the contents of a registered scenario** (`add` on a present name returns the library unchanged); a name is trimmed (`" x "` = `"x"`); a blank-after-trim add is refused; `"intro"` and `"Intro"` are distinct; `capture` files every present zone and the cue (kept from `specs/070`)
- [x] T016 [US1] `test/ScenarioTests.fs`: the panel's add path — `ScenarioPanel.commit` of one registered name is one `SetScenario` step from the empty library to the one-name library
- [x] T017 [US1] `dotnet test` — the add/register story is green with no device (depends T015, T016)

**Checkpoint**: the author can name a scenario and file the current scene under it, in order.

---

## Phase 4: User Story 2 - The author deletes a scenario, or clears its contents (Priority: P1)

**Goal**: A **Delete** control removes a name and its contents; a **Clear** control empties a scenario's contents but
keeps its name.

**Independent Test**: with a library holding two registered scenarios, delete one and clear the other; assert the
first is gone and the second remains named with no contents.

- [x] T018 [US2] `test/ScenarioTests.fs`: `remove` drops the name and its contents from the library; `setContents name None` keeps the name (it still appears in `names`) and `tryContents` is `None`; deleting an unregistered name is a no-op on the rest; the panel's delete and clear paths are each one `SetScenario` step
- [x] T019 [US2] `dotnet test` — the delete/clear story is green with no device (depends T018)

**Checkpoint**: the panel is a real editor, not an append-only log.

---

## Phase 5: User Story 3 - The author forks a registered scenario into the scene (Priority: P2)

**Goal**: The **Fork** control materialises a registered scenario into the live scene as one undoable step, keyed by
name.

**Independent Test**: register a scenario carrying an actor and a cue, fork it headlessly, and assert the actor is
appended and the running cue is the fork; undo and assert both are reversed.

- [x] T020 [US3] `test/ScenarioForkTests.fs`: migrate to string names and keep the full fork coverage — materialise appends entities and forks the cue; an absent place is created and staged without entering; a chained waypoint pair links; a fork is one step and undo/redo round-trips; forking twice adds two copies; a full target zone refuses the whole fork and creates no place; forking an **unregistered** name does nothing, and so does a fork naming a scenario that was **deleted** — the cue performs nothing, reports why, is not rewritten, and advances past the op (`specs/083` US2-AC4, FR-013); `pruneCueSteps` drops the fork's cue step but keeps its entity commands
- [x] T021 [US3] `dotnet test` — the fork story is green with no device (depends T020)

**Checkpoint**: a registered scenario forks into the scene exactly as before, now by name.

---

## Phase 6: User Story 4 - A cue names a scenario as a string (Priority: P2)

**Goal**: The fork cue op carries a string; the chooser offers the current names with a typed fallback; an op may name
a scenario before it is added.

**Independent Test**: build a cue whose only op is a fork naming a typed string that matches no scenario, and assert it
is authored without error and runs as a documented no-op.

- [x] T022 [P] [US4] `test/CuePanelTests.fs`: a new "Fork scenario" op defaults to `""`; its label reads the raw name (omitted when empty — `"Fork scenario: Intro"` vs `"Fork scenario"`); `withScenario` takes a string and refuses any other node; `children`/`placeOf` are unchanged
- [x] T023 [P] [US4] `test/CueTests.fs`: the fork op names a string — a cue naming a scenario not in the library is authored without error and performs nothing when run (the `describe` helper no longer calls `Scenario.scenarioName`)
- [x] T024 [US4] `dotnet test` — the cue-op story is green with no device (depends T022, T023)

**Checkpoint**: the fork op names scenarios over strings, with no vocabulary.

---

## Phase 7: User Story 5 - Each change is one undoable step on the one history (Priority: P3)

**Goal**: Every add, delete, clear and register is one `SetScenario` step; undo/redo restore the exact before/after
library; a scenario survives a zone switch.

**Independent Test**: add a name, undo, assert the prior library and a redo-side step; redo and assert it is back;
register a scenario, switch zone, assert it is still registered.

- [x] T025 [US5] `test/ScenarioTests.fs`: an add/delete/clear/register is exactly one `SetScenario` step; undo restores the exact before library and redo the after (including order); a registration survives a zone switch; a library edit followed by a zone move undoes the move first and the edit second (the interleaved-history case); the zone-level runner refuses a `SetScenario` step (`Command.apply`/`undo` raise)
- [x] T026 [US5] `dotnet test` — the undo story is green with no device (depends T025)

**Checkpoint**: the library is on the one history, exactly as the union's registrations were.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [x] T027 [P] Update `docs/ARCHITECTURE.md` — the Scenario & Cue section and the `Scenario_Library` mermaid node (a string-keyed library, not a closed union)
- [x] T028 [P] Update `docs/EDITOR_DESIGN.md` — the Scenario panel description (a text field + list editor, not a fixed set of rows)
- [x] T029 [P] Update `docs/PROJECT_STRUCTURE.md` — the `Scenario.fs` / `ScenarioRegistry.fs` / `ScenarioPanel.fs` lines and the Scenario cluster header
- [x] T030 [P] Update `docs/ROADMAP.md` — the phase-11 "scenario library" passage
- [x] T031 [P] Update `docs/CUE_MODEL_DECISION.md` — the scenario-library-off-the-one-history passages (the library is now a string-keyed ordered map)
- [x] T032 [P] Sweep the game for the retired union — grep `world/ state/ editor/ screen/` for `Scenario.all`, `Scenario.scenarioName`, `open Scenario`, and the former case name `Intro`; expect none (spec FR-012, SC-005)
- [x] T033 [P] Confirm the cue scenario chooser's wiring only rebuilds the cue and never calls `ScenarioRegistry.add` — a typed name does **not** grow the library (spec FR-009, US4-AC3); inspection, since the control is device-bound
- [x] T034 Run the `quickstart.md` validation: `dotnet build` warning-free and `dotnet test` green with no device

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup — **BLOCKS all stories** (the solution cannot compile until the types
  and every call site migrate).
- **User Stories (Phases 3–7)**: all depend on Foundational. US1 (library + add + register) is the base; US2, US3, US4
  and US5 refine and verify around it. They may proceed in priority order or in parallel by file (US4 touches
  `CueTests`/`CuePanelTests`; US1/US2/US5 touch `ScenarioTests`; US3 touches `ScenarioForkTests`).
- **Polish (Phase 8)**: after the desired stories.

### Within Each Story

- `world/` before `state/` before `editor/` (compile order).
- The test edits accompany the behaviour they cover; a story ends with `dotnet test` green.

### Parallel Opportunities

- T002, T003, T004, T005, T006, T007 (different files, no interdependencies).
- T012 (the mechanical test migration) runs beside the game-side edits, once the types are chosen (T002–T007).
- T015 alone in US1; T022, T023 (different test files) in US4.
- T027–T031 (different docs), and T032/T033 (a sweep and an inspection, beside them).

---

## Parallel Example: Foundational

```bash
# The independent leaves of the type change:
Task: "T002 rewrite world/ScenarioRegistry.fs as the ordered string-keyed library"
Task: "T003 state/GameplayState.fs — mutable Scenarios"
Task: "T004 state/Command.fs — SetScenario whole-library payload"
Task: "T006 world/Cue.fs — ForkScenario carries a string"
Task: "T007 state/EditorState.fs — ScenarioNameBuffer"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → Phase 2 Foundational (critical — blocks everything).
2. Phase 3 US1 → **STOP and VALIDATE** (`dotnet test`).
3. US1 alone delivers the core: the author names a scenario, files the scene under it, and the library reads in order.

### Incremental Delivery

1. Setup + Foundational → a scenario is a string, the library is ordered, the panel lists it, the solution is green.
2. US1 → naming/registering is verified → MVP.
3. US2 → delete/clear is verified.
4. US3 → forking by name is verified.
5. US4 → the cue op runs over strings, with no vocabulary.
6. US5 → the undo step and zone survival are verified.
7. Polish → docs and quickstart.

---

## Notes

- [P] = different files, no dependencies.
- Phase 2 is atomic in practice: the solution cannot compile between the type change and the call-site migration.
- One file is **deleted** (`world/Scenario.fs`); no file is added; exactly one `<Compile>` entry is removed (Setup).
- The scenario is a **string** and the library is Prime's `OMap<string, ScenarioContents option>`; both are already
  available (no new package).
- Whole-library undo snapshots are cheap — `OMap` is persistent, so a before/after pair shares structure.
- Spelling and cue↔library cross-references are deliberately unchecked (spec FR-013); typing a name in a cue does not
  add it to the library (spec FR-009).
- FR-016 (no persistence) needs no task: it is a "must not", satisfied by the absence of any serialization.
- Commit after each task or logical group.

---

## Phase 9: Convergence

- [x] T035 [US1] `test/ScenarioTests.fs`: assert a registration survives a zone switch per FR-002/SC-001/US1-AC3 (partial) — register under `"Intro"`, make a second zone present and active (`GameplayState.addZone`), then assert `tryContents "Intro"` still holds the registration
- [x] T036 [US1] Make the blank-name rule assertable headlessly per FR-007/T015 (partial) — factor the panel's add guard into a pure predicate (e.g. `ScenarioRegistry.canAdd`) used by `editor/ScenarioPanel.fs` and asserted in `test/ScenarioTests.fs`, since the "blank after trimming is rejected" rule currently lives only in the device-bound panel
