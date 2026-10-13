---

description: "Task list for feature implementation"
---

# Tasks: Advents as an Ordered String Set

**Input**: Design documents from `/specs/082-advent-strings/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/advent-set.md

**Tests**: Included — the project mandates headless verification (constitution N-V; `specs/074`'s stance) and this
feature's success criteria are test-based (`test/AdventTests.fs` rewritten, `test/CueTests.fs` and
`test/CuePanelTests.fs` migrated, the `Await` allocation guard retained).

**Organization**: As with `specs/072`/`specs/073`/`specs/074`, this is a single cohesive type change: the milestone
becomes a string everywhere at once, so the **Foundational** phase migrates the types and every call site (game and
tests) and keeps the solution compiling and green; the story phases then deliver the verifiable increments (the set,
the remove path, the string cue ops, the undo step).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)
- Paths are repository-relative.

## Path Conventions

Single project, F# — flat clusters at the repository root: `world/`, `state/`, `editor/`, `test/`. Compile order is
fixed by `fsharp-game.fsproj`; this feature adds and removes **no** file, so no `<Compile>` entry changes — only a
comment is corrected.

---

## Phase 1: Setup (Shared Infrastructure)

- [x] T001 Confirm no `.fsproj` change is needed — `world/Advent.fs` keeps its filename and compile slot and Prime is already referenced — and correct its `<Compile>` comment in `fsharp-game.fsproj` (it currently says "story milestones as a closed union").

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Make a milestone a string, make the flag set `Advent.Set`, migrate every call site and test, and keep
the solution compiling. **⚠️ No user-story work can begin until this is complete.**

- [x] T002 [P] Rewrite `world/Advent.fs`: remove the `Advent` union, `all` and `name`; `open Prime`; add `type Set = OSet<string>` and `empty`/`contains`/`add`/`remove`/`names`/`normalise` (trim); rewrite the header comment (the set is ordered and free-form, per `specs/082`)
- [x] T003 [P] `state/SharedState.fs`: `mutable Advents : Advent.Set`; `create` starts it `Advent.empty`; reword the field comment
- [x] T004 [P] `state/Command.fs`: `Advent of Previous : Advent.Set * Next : Advent.Set`; reword the case comment
- [x] T005 [P] `state/History.fs`: reword the `Advent` comment (the runner's assign is unchanged)
- [x] T006 [P] `world/Cue.fs`: the four ops carry `string` (`AddAdvent`/`RemoveAdvent`/`Await`/`AwaitState of advent: string`, `If of advent: string * thenCue: Cue * elseCue: Cue`); drop `open Advent`; correct the header comment
- [x] T007 [P] `state/EditorState.fs`: add `AdventNameBuffer : byte[]` beside `ZoneNameBuffer` and initialise it in `create`
- [x] T008 `world/CueEngine.fs`: use `Advent.add`/`Advent.remove`/`Advent.contains` in place of `Set.*` (curried); drop the now-unused opens (depends T002, T006)
- [x] T009 `editor/AdventPanel.fs`: rewrite the panel — a reused `inputText` over `state.Editor.AdventNameBuffer`, an **Add** control disabled for a blank-after-trim or already-present name (clearing the field on add), and a **list** of the current names each with a **Remove** control; add a private `bufferText`; `commit` keeps its shape (depends T002, T007)
- [x] T010 `editor/CueTree.fs`: `withAdvent` takes a `string`; `label` renders the raw name (suffix omitted when empty); `defaultOfKind` makes an advent op with `""`; `drawEditor` gains an `advents: string list` parameter and its milestone control becomes `filteredNameCombo` seeded with those names (pick or type) (depends T002, T006)
- [x] T011 `editor/CueEditor.fs`: pass `Advent.names shared.Advents` into both `CueTree.drawEditor` call sites (the Cue panel and the inline trigger editor) (depends T010)
- [x] T012 [P] `world/Scenario.fs`: correct the comment that names `Advent.name` (comment only)
- [x] T013 [P] Migrate every reference in `test/` so it compiles: `test/AdventTests.fs` (rewrite to the string/`Advent.Set` shape), `test/CueTests.fs` and `test/CuePanelTests.fs` (milestone strings; `Set.contains m s` → `Advent.contains m s`; `Advent.name m` → `m`), and every `SharedState` literal `Advents = Set.empty` → `Advent.empty` (`test/GameplayStateTests.fs`, `test/SpawningTests.fs`, `test/ScenarioTests.fs`, `test/SelectionTests.fs`, `test/ScenarioForkTests.fs`, `test/ActorNameTests.fs`)
- [x] T014 `dotnet build fsharp-game.fsproj` is clean with no warnings (depends T002–T013)
- [x] T015 `dotnet test` — the migrated suite is green with no device (baseline) (depends T014)

**Checkpoint**: a milestone is a string, the set is ordered, the panel lists it, and the solution builds and passes.

---

## Phase 3: User Story 1 - The author names a new advent and records it (Priority: P1) 🎯 MVP

**Goal**: The panel's text field + Add records a typed name; the set reads in the order names were added.

**Independent Test**: with a headless state and an empty set, add a typed name and assert the set contains exactly
it and `Advent.names` reads it; assert trim/blank/duplicate rules.

- [x] T016 [P] [US1] `test/AdventTests.fs`: a fresh set is empty; adding a name records it and `Advent.names` reads insertion order for several names; a repeated add is a no-op; a name is trimmed (`" x "` = `"x"`); a blank-after-trim add is refused; `"metAiko"` and `"MetAiko"` are distinct
- [x] T017 [US1] `test/AdventTests.fs`: the panel's add path — `AdventPanel.commit` of one name is one `Advent` step from the empty set to the one-name set
- [x] T018 [US1] `dotnet test` — the add story is green with no device (depends T016, T017)

**Checkpoint**: the author can record a named milestone and it reads back in order.

---

## Phase 4: User Story 2 - The author removes an advent (Priority: P1)

**Goal**: A **Remove** control clears a name from the set and the list.

**Independent Test**: with a set holding two names, remove one and assert only the other remains; removing an absent
name is a no-op.

- [x] T019 [US2] `test/AdventTests.fs`: removing a name drops it from the set and `Advent.names`; removing an absent name is a no-op; the panel's remove path is one `Advent` step
- [x] T020 [US2] `dotnet test` — the remove story is green with no device (depends T019)

**Checkpoint**: the panel is a real editor, not an append-only log.

---

## Phase 5: User Story 3 - A cue names an advent as a string (Priority: P2)

**Goal**: The four cue ops carry a string; the chooser offers the current names with a typed fallback; an op may name
an advent before it is added.

**Independent Test**: run a cue whose only op is an add naming a typed string and assert the set now contains it and
the cue is `Fin` in the same step; assert a cue naming an absent advent is authored and runs to its documented
behaviour.

- [x] T021 [P] [US3] `test/CuePanelTests.fs`: a new advent op defaults to `""`; the labels read the op's raw name (omitted when empty); the `If` children are unchanged; the advent kinds are unchanged
- [x] T022 [P] [US3] `test/CueTests.fs`: the four ops over string milestones — an add/remove is a no-op when redundant, an `Await` blocks until its name is set (and adds **0 bytes per step**, the retained guard), an `If` takes the branch its name selects, and removing a name an `Await` is waiting on leaves the cue still blocked (membership is re-checked each step; removal is not remembered); a cue naming an advent not in the set is authored without error and runs (await waits; if takes its else)
- [x] T023 [US3] `dotnet test` — the cue-ops story is green with no device (depends T021, T022)

**Checkpoint**: the milestones drive cues over strings, with no vocabulary.

---

## Phase 6: User Story 4 - Each change is one undoable step on the one history (Priority: P2)

**Goal**: Every add and remove is one `Advent` step; undo/redo restore the exact before/after set; a name survives a
zone switch.

**Independent Test**: add a name, undo, assert the prior set and a redo-side step; redo and assert it is back; set a
name, switch zone, assert it is still set.

- [x] T024 [US4] `test/AdventTests.fs`: an add/remove is exactly one `Advent` step; undo restores the exact before set and redo the after set (including order); a name survives a zone switch; an advent edit followed by a zone move undoes the move first and the edit second (the interleaved-history case); the zone-level runner refuses an `Advent` step (`Command.apply`/`undo` raise)
- [x] T025 [US4] `dotnet test` — the undo story is green with no device (depends T024)

**Checkpoint**: the set is on the one history, exactly as the union's flags were.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [x] T026 [P] Update `docs/ARCHITECTURE.md` — the `Advent_Set` node and the milestone passages (a string set, not a union)
- [x] T027 [P] Update `docs/EDITOR_DESIGN.md` — D-011/D-013 and the Advent panel description (a text field + list, not a checklist)
- [x] T028 [P] Update `docs/PROJECT_STRUCTURE.md` — the `Advent.fs` line and its header
- [x] T029 [P] Update `docs/ROADMAP.md` — the phase-10 "adventure flags" passage
- [x] T030 [P] Update `docs/CUE_MODEL_DECISION.md` — the advents-off-the-one-history passages (the set is now a string set)
- [x] T031 [P] Sweep the game for the retired union — grep `world/ state/ editor/ screen/` for `Advent.all`, `Advent.name`, and the former case names (`OpeningComplete`, `MetAiko`, `MetAkane`, `MetShinji`, `MetYuki`); expect none (FR-011, SC-005)
- [x] T032 [P] Confirm the cue milestone chooser's wiring only rebuilds the cue and never calls `Advent.add` — a typed name does **not** grow the set (FR-007, US3-AC3); inspection, since the control is device-bound
- [x] T033 Run the `quickstart.md` validation: `dotnet build` warning-free and `dotnet test` green with no device

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup — **BLOCKS all stories** (the solution cannot compile until the
  types and every call site migrate).
- **User Stories (Phases 3–6)**: all depend on Foundational. US1 (set + add) is the base; US2, US3 and US4 refine
  and verify around it. They may proceed in priority order or in parallel by file (US3 touches `CueTests`/
  `CuePanelTests`; US1/US2/US4 touch `AdventTests`).
- **Polish (Phase 7)**: after the desired stories.

### Within Each Story

- `world/` before `state/` before `editor/` (compile order).
- The test edits accompany the behaviour they cover; a story ends with `dotnet test` green.

### Parallel Opportunities

- T002, T003, T004, T005, T006, T007, T012 (different files, no interdependencies).
- T013 (the mechanical test migration) runs beside the game-side edits, once the types are chosen (T002–T006).
- T016 alone in US1; T021, T022 (different test files) in US3.
- T026–T030 (different docs), and T031/T032 (a sweep and an inspection, beside them).

---

## Parallel Example: Foundational

```bash
# The independent leaves of the type change:
Task: "T002 rewrite world/Advent.fs as the ordered string set"
Task: "T003 state/SharedState.fs — Advents : Advent.Set"
Task: "T004 state/Command.fs — Advent step payload"
Task: "T006 world/Cue.fs — the four ops carry a string"
Task: "T007 state/EditorState.fs — AdventNameBuffer"
Task: "T012 world/Scenario.fs — comment only"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → Phase 2 Foundational (critical — blocks everything).
2. Phase 3 US1 → **STOP and VALIDATE** (`dotnet test`).
3. US1 alone delivers the core: the author names a milestone and the set records it in order.

### Incremental Delivery

1. Setup + Foundational → a milestone is a string, the set is ordered, the panel lists it, the solution is green.
2. US1 → naming/recording is verified → MVP.
3. US2 → removal is verified.
4. US3 → the cue ops run over strings, with no vocabulary.
5. US4 → the undo step and zone survival are verified.
6. Polish → docs and quickstart.

---

## Notes

- [P] = different files, no dependencies.
- Phase 2 is atomic in practice: the solution cannot compile between the type change and the call-site migration.
- No file is added or removed; no `<Compile>` entry changes (only the `Advent.fs` comment in Setup).
- The milestone is a **string** and the set is Prime's `OSet<string>`; both are already available (no new package).
- The per-frame `Await` check is `OSet.contains`, measured allocation-free — the retained allocation guard proves it.
- Spelling and cue↔set cross-references are deliberately unchecked (spec FR-012); typing a name in a cue does not
  add it to the set (spec FR-007).
- Commit after each task or logical group.

---

## Phase 8: Convergence

- [x] T034 Reconcile the extra `Advent.ofList` helper in `world/Advent.fs` with the planned module surface — **resolved: kept**. It is already documented in the module ("how a caller builds one outright (a fixture, a test)") and used at seven test sites to build a set from a list; it is the construction companion to `Advent.names`. No code change needed (`plan:data-model`, unrequested).
