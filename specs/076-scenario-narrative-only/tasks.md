---

description: "Task list for Trim Scenario Scope to Narrative Only"
---

# Tasks: Trim Scenario Scope to Narrative Only

**Input**: Design documents from `/specs/076-scenario-narrative-only/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/scenario-capture.md, quickstart.md

**Tests**: Included — the capture is pure and device-free (spec FR-007, SC-004), so the trimmed record is asserted
headlessly through the existing `test/ScenarioTests.fs`, as it already is. FR-014 makes the test update in scope.

**Organization**: Tasks are grouped by user story. US1 (P1) is the narrative-only record; US2 (P2) is the
independence of zone staging from a registration; US3 (P3) is coherence (docs + validation). The **record trim**
itself is foundational: it is the shape every story builds on, and the compiler forces it and its one call site
together (removing a field removes every reader of it).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 (setup, foundational and polish carry none)
- Exact file paths are in every description.

## Path Conventions

- Single F# project at the repository root; sources in `world/`, `state/`, `editor/`, tests in `test/`, docs in `docs/`.

---

## Phase 1: Setup

**Purpose**: Establish a green baseline and confirm no project-file change is needed.

- [X] T001 Run `dotnet build` (warning-free) and `dotnet test` (green) at the repository root and record the
  baseline. Confirm `fsharp-game.fsproj` needs **no** edit: this feature adds no file and removes none, and no
  compile-order changes (the trimmed `ScenarioZone` stays in `world/ScenarioZone.fs`, `ScenarioContents` in
  `world/ScenarioRegistry.fs`).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The trimmed record and its one call site — the shape every story builds on.

**⚠️ CRITICAL**: No user story can be validated until this phase is complete.

- [X] T002 [P] `world/ScenarioZone.fs`: trim the record. Delete the `ScenarioEnvironment` type; drop the
  `Environment`, `Light` and `Player` fields from `ScenarioZone` (leaving `Actors`, `Waypoints`, `Triggers`); delete
  the private `captureEnvironmentParts` helper; simplify `captureActors` to return just `ActorState array` (drop the
  player-index computation); update `capture` to build only the three arrays; update the module header comment to say
  the zone is the narrative entities only (no environment, light or player).
- [X] T003 [P] `world/ScenarioRegistry.fs`: drop the `Active : Place` field from `ScenarioContents`; change
  `capture` to `(zones: Dictionary<Place, Zone.Zone>) (cue: Cue.Cue) : ScenarioContents` (drop the `active`
  parameter and the `Active = active` file); update the header and `ScenarioContents` comments that mention the
  active place.
- [X] T004 `editor/ScenarioPanel.fs`: update the Register call to `capture state.Zones state.Cue` (the `active`
  argument is gone). Leave `commit`, the row, the controls and the preview unchanged. (depends on T003)
- [X] T005 Checkpoint: `dotnet build fsharp-game.fsproj` compiles warning-free. (depends on T002–T004)

**Checkpoint**: the scenario record is narrative-only and the tree compiles; the stories can be validated.

---

## Phase 3: User Story 1 - A scenario records only its narrative entities (Priority: P1) 🎯 MVP

**Goal**: a registration captures actors, waypoints (chains by index) and triggers — and the cue — with no
environment, light, player or active place.

**Independent Test**: build a headless scene with actors, a chained waypoint pair, triggers and a cue (a loaded
environment needs an asset, so its absence is type-enforced); register it and assert the stored zone equals a
hand-built `ScenarioZone` of exactly those arrays, and the contents carry no active place.

- [X] T006 [US1] `test/ScenarioTests.fs`: make the suite compile and assert the trimmed record. Fix the `register`
  helper's call to `capture state.Zones state.Cue`; rename and edit ``the capture carries every present zone, the
  active place and the cue`` to drop the `Active` assertion (keep zones-by-place and the cue); rename and edit
  ``a captured zone is ref-free: waypoint chains and the player become indices`` to assert the chain is by index and
  **drop** the player-index assertion; add assertions that a captured zone holds only `Actors`/`Waypoints`/`Triggers`
  and that `ScenarioContents` exposes no active place. (depends on T005)
- [X] T007 [US1] `test/ScenarioTests.fs`: assert the empty-zone case — a present place with no actors, waypoints or
  triggers is captured as an empty `ScenarioZone`, so `contents.Zones` still records every present place
  (spec FR-006, Clarifications). (depends on T006)
- [X] T008 [US1] Checkpoint: `dotnet test` green — including the surviving pre-existing tests `register records one
  step, and undo and redo round-trip it`, `undo of a re-registration restores the previous contents, not nothing`,
  `undo of a clear restores the contents it held`, and `registering disturbs nothing in the scene`, which cover
  FR-008/FR-011/SC-003 without change. (depends on T006, T007)

**Checkpoint**: US1 is independently testable — a registered scenario holds exactly the narrative entities and cue.

---

## Phase 4: User Story 2 - Zone staging is independent of a scenario (Priority: P2)

**Goal**: changing a zone's environment, light or player does not change what a registration records.

**Independent Test**: register a scene; change only the zone's light and player (a loaded environment can't be staged
headlessly, and the record no longer has one); register again and assert the two captures are equal.

- [X] T009 [US2] `test/ScenarioTests.fs`: add the independence test — register; then change the zone's **light**
  (`zone.Light <- …`) and **player** designation (add an actor and set `zone.Player`), which the headless fixtures can
  do; register again and assert the two `ScenarioContents` are equal (spec FR-009, US2 #1–#2). The environment is not
  varied here: loading one needs a registered asset, and the trimmed record has no environment field to compare
  (FR-002 is type-enforced). (depends on T005)
- [X] T010 [US2] Checkpoint: `dotnet test` green. (depends on T009)

**Checkpoint**: staging is provably outside the record.

---

## Phase 5: User Story 3 - The library's record, panel and docs are coherent (Priority: P3)

**Goal**: no guiding document, and no scenario test, still claims a scenario records its environment, light, player
or active place.

**Independent Test**: build and run the suite; grep the guiding docs and the scenario tests for the removed concepts
and confirm none remain as claims about the scenario record.

- [X] T011 [US3] Docs — update the five guiding docs to the trimmed model:
  `docs/ARCHITECTURE.md` (the "Scenario & Cue" section: the scenario zone is actors/waypoints/triggers by index, no
  player/environment/light; the contents carry zones + cue, no active place);
  `docs/EDITOR_DESIGN.md` (Workspace's Scenario row: Register files the scene's narrative entities and the cue, not
  the active place or the zone's staging);
  `docs/ZONE_MODEL_DECISION.md` (the `ScenarioZone` shape, the four-value table and the convergence prose drop the
  environment/light/player);
  `docs/ROADMAP.md` (phase 11 deliverables: the ref-free zone is entities + chains by index, no player);
  `docs/PROJECT_STRUCTURE.md` (the `ScenarioZone.fs` line). (depends on T005)
- [X] T012 [US3] Checkpoint: `dotnet build` warning-free and `dotnet test` green; confirm `state/Command.fs` and
  `state/History.fs` are unchanged (FR-012) and the pre-existing undo/redo and 'disturbs nothing' tests pass; grep
  the guiding docs and `test/ScenarioTests.fs` and confirm no remaining claim that a scenario records an environment,
  light, player or active place. (depends on T006–T009, T011)

**Checkpoint**: the record, the tests and the docs all describe the trimmed scenario.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T013 Run the headless section of `quickstart.md` (the four scenarios) and confirm the outcomes; then confirm
  `fsharp-game.fsproj` is untouched and no file was added or removed (SC-004). (depends on T008, T010, T012)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none; establishes the baseline.
- **Foundational (Phase 2)**: depends on Setup; **blocks** all stories (the record shape).
- **US1 (Phase 3)**: depends on Foundational.
- **US2 (Phase 4)**: depends on Foundational (and shares `test/ScenarioTests.fs` with US1, so run after US1).
- **US3 (Phase 5)**: depends on Foundational; its doc edits are independent of US1/US2 but its grep checkpoint wants
  the test edits in place, so run after US2.
- **Polish (Phase 6)**: depends on all stories.

### Within and between phases

- T002 ∥ T003 (different files) → T004 → T005.
- T006 → T007 → T008; T009 → T010; T011 → T012; T013 last.
- The trim (T002–T004) is one compile-forced change: the field removal and its readers cannot land separately.

### Parallel Opportunities

- T002 (`world/ScenarioZone.fs`) and T003 (`world/ScenarioRegistry.fs`) are independent files and can be written
  together.
- Within T011, the five docs are independent files and can be edited in any order.

---

## Parallel Example: Foundational phase

```bash
# Different files, no interdependency — write both, then fix the one call site:
Task: "world/ScenarioZone.fs — delete ScenarioEnvironment; drop Environment/Light/Player; trim capture"
Task: "world/ScenarioRegistry.fs — drop ScenarioContents.Active; capture (zones) (cue)"
# then T004: editor/ScenarioPanel.fs — capture state.Zones state.Cue
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Setup (T001) and Foundational (T002–T005): the record is trimmed and the tree compiles.
2. US1 (T006–T008).
3. **STOP and VALIDATE**: register a scene headlessly and assert the record is narrative-only.

### Incremental notes

- This is a **removal**: the bulk of the work is deletion in T002, and the compiler drives the rest (every reader of
  a dropped field fails until it is removed).
- No `state/Command.fs` or `state/History.fs` change: `SetScenario` names `ScenarioContents`, and only that type
  shrinks; the case and its runner are unchanged (`contracts/scenario-capture.md`).
- No persistence migration: the library is not persisted (ROADMAP phase 15).
- Forking a scenario remains the next increment (ROADMAP phase 11's remainder); this feature adds no cue vocabulary.

## Notes

- `[P]` = different files, no dependencies.
- The only behavioural change is what is *not* captured; nothing in the scene, the cue, or the panel's behaviour
  changes (spec FR-010, FR-011).
- Keep the existing raise for a waypoint whose `Next` names no live waypoint (spec FR-007; `world/ScenarioZone.fs`).

---

## Phase 7: Convergence

- [X] T014 `docs/ZONE_MODEL_DECISION.md`: finish the trim. Reword the residual claims that a scenario's zone is a broad
  "recipe with breadth" — "What this decides" item 1 (`ScenarioZone`, a recipe with the breadth a scenario needs), the
  table's `Role` row (`scenario zone (a recipe, with breadth)`), and Option 0's cost (`the near-duplicate of
  ScenarioZone stays`) — and reword item 4 so scenarios no longer "follow onto whichever ref-free shape that
  produces" (a scenario now holds narrative entities only). Per FR-013, SC-005 (partial).
- [X] T015 `world/ScenarioZone.fs`: remove the now-unused `open SlotMap` (the file names no `SlotMap` symbol; the
  `IteriAlive`/`Count`/`Add` uses are members). Per Constitution I (Minimalism) (partial).
