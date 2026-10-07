---
description: "Task list for Scenario Library"
---

# Tasks: Scenario Library

**Input**: Design documents from `/specs/070-scenario-library/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/scenario-registry.md, quickstart.md

**Tests**: Included — the capture is pure and device-free (spec FR-009, SC-004), so it is asserted headlessly, and
the registration step is exercised through `History` with the existing headless fixtures (`GameplayStateTests`,
`TestSupport`). The panel's drawing needs a device and is covered by the walkthrough, as the other panels are.

**Organization**: Tasks are grouped by user story. US1 (P1) is the panel that registers a scene; US2 (P2) is the
undo/redo correctness of that registration. The **step itself** (`Command`/`History`) is foundational, because
US1's Register control is already committed through it — it is not a throwaway for US2 to replace.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 (setup, foundational and polish carry none)
- Exact file paths are in every description.

## Path Conventions

- Single F# project at the repository root; sources in `world/`, `state/`, `editor/`, native in `native/`, tests in `test/`.

---

## Phase 1: Setup

**Purpose**: Fix the compile order the rest of the change depends on.

- [X] T001 `fsharp-game.fsproj`: add the three `<Compile Include>` entries at their compile points —
  `world/Scenario.fs` **before** `world/Cue.fs`; `world/ScenarioRegistry.fs` **after** `world/Zone.fs`;
  `editor/ScenarioPanel.fs` **before** `editor/Editor.fs` — each with the intentional-order comment the other
  entries carry.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The vocabulary, the store, the step, and where the library lives.

**⚠️ CRITICAL**: No user story can be implemented until this phase is complete.

- [X] T002 [P] `world/Scenario.fs`: create the vocabulary — the `Scenario` union with the single case `Intro`,
  `scenarioName` (a `match` over the cases, the shape of `Place.placeName`), and `all` (every case via
  `Microsoft.FSharp.Reflection`, the shape of `Place.all`). Pure; consumes nothing.
- [X] T003 `world/ScenarioRegistry.fs`: create the store — `ScenarioContents`
  (`{ Zones : Map<Place, ZoneContents>; Active : Place; Cue : Cue.Cue }`), `Registry`
  (`{ Entries : Dictionary<Scenario, ScenarioContents> }`), `create`, `tryContents` (the `option` rule, as
  `ZoneRegistry.tryTemplate`), and `setContents` (the one write: `Some` files, `None` removes). **Superseded
  by T020**: the zone type is `ScenarioZone`, not `ZoneContents`. (depends on T001, T002)
- [X] T004 `state/Command.fs`: add the case
  `SetScenario of Name : Scenario * Previous : ScenarioContents option * Next : ScenarioContents option`, and add
  it to the **state-level** `invalidOp` group of both `apply` and `undo` (beside `SetCue`/`Advent`). `open Scenario`
  for the `Scenario` type; name `ScenarioContents` from `ScenarioRegistry`. (depends on T003)
- [X] T005 `state/History.fs`: handle `SetScenario` in `apply` (`ScenarioRegistry.setContents Name Next state.Scenarios`)
  and `undo` (`… Previous …`). No zone, entity, camera or selection is touched. (depends on T004)
- [X] T006 `state/GameplayState.fs`: add `Scenarios : ScenarioRegistry.Registry` beside `ZoneRegistry`, seeded in
  `create` with `ScenarioRegistry.create ()`. (depends on T003)
- [X] T007 Build checkpoint: `dotnet build fsharp-game.fsproj` and `dotnet build test/fsharp-game.Tests.fsproj`
  compile (warning-free). (depends on T002–T006)

**Checkpoint**: the vocabulary, store, step and library field exist; the stories can proceed.

---

## Phase 3: User Story 1 - Register the current game state as a scenario (Priority: P1) 🎯 MVP

**Goal**: the Scenario panel lists the scenarios and files the current scene (every present zone, the active
place, and the cue) under one with **Register**, and empties it with **Clear**.

**Independent Test**: build a multi-zone headless state, call `capture`, and assert its contents; then drive
Register and Clear through `History.commit` and assert the library.

- [X] T008 [US1] `world/ScenarioRegistry.fs`: add `capture (zones: Dictionary<Place, Zone.Zone>) (active: Place)
  (cue: Cue.Cue) : ScenarioContents` — map each live zone through `Zone.captureZone` into the immutable `Map`, and
  pair in `active` and `cue`. Pure and device-free. **Superseded by T020** (maps through `ScenarioZone.capture`).
  (depends on T003)
- [X] T009 [US1] `editor/ScenarioPanel.fs`: create the panel (`draw : SharedState.SharedState ->
  byref<GameplayState.GameplayState> -> unit`) — `ImGui.beginWindow "Scenario"`; enumerate `Scenario.all`; one row
  each showing whether it holds contents (`tryContents`); **Register** always, committing `SetScenario (s,
  tryContents s state.Scenarios, Some (capture state.Zones state.ActiveZone state.Cue))`; **Clear** only when it
  holds contents, committing the same with `None`. (depends on T005, T008)
- [X] T010 [US1] `editor/Editor.fs`: add `"Scenario"` to `panelNames`, and submit `ScenarioPanel.draw shared &state`
  with the left-docked panels (beside `ZoneRegistryPanel.draw`). (depends on T009)
- [X] T011 [US1] `native/cimgui_sdl3.cpp`: add `ImGui::DockBuilderDockWindow("Scenario", left);` beside the
  `"Cast"` / `"Zone Registry"` calls.
- [X] T012 [P] [US1] `test/ScenarioTests.fs`: create the suite (and list it in `test/fsharp-game.Tests.fsproj`) —
  assert `capture` records one entry per present zone keyed by place (US1 #2), that each entry equals
  `Zone.captureZone` of its source and that `Active` and `Cue` are carried (SC-003), that a deleted/duplicated
  part or prop is reflected exactly (US1 #4), and that Clear empties a registered scenario (US1 #5). **Its
  capture assertions are superseded by T022** (the `ScenarioZone` shape).
  (depends on T008)
- [X] T013 [US1] Checkpoint: `dotnet build` warning-free and `dotnet test` green. (depends on T009–T012)

**Checkpoint**: US1 works and is testable independently — a scene can be filed under a scenario and cleared.

---

## Phase 4: User Story 2 - A registration is one undoable step (Priority: P2)

**Goal**: register, overwrite and clear are each one step whose undo restores the *previous* state and whose redo
re-applies it, without disturbing the scene.

**Independent Test**: through `History`, register an unregistered scenario, undo, redo; repeat for an overwrite
and for a clear; assert the active place, zone count, cue, camera and selection never change.

- [X] T014 [P] [US2] `test/ScenarioTests.fs`: add the step tests over a headless `GameplayState` — register →
  undo (unregistered) → redo (contents back) (US2 #1, #3); re-register after an edit → undo restores the
  *previous* contents (US2 #2, FR-005); clear → undo restores the contents (FR-011); and, across every one,
  assert the active place, the zone count, the cue, the camera and the selection are unchanged (US2 #4, FR-008,
  SC-005). (depends on T005, T009)
- [X] T015 [US2] Checkpoint: `dotnet test` green. (depends on T014)

**Checkpoint**: both stories function; a registration is fully reversible.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [X] T016 [P] Docs: add the **Scenario** panel to `docs/EDITOR_DESIGN.md` (the workspace table and the
  sub-editor list — the left dock); drop the `(planned)` markers in `docs/PROJECT_STRUCTURE.md` and name
  `world/ScenarioRegistry.fs` + `editor/ScenarioPanel.fs`; move `Scenario_Library` off `Shared_State` in
  `docs/ARCHITECTURE.md` and update the "Scenario & Cue" section; and reshape `docs/ROADMAP.md` phase 11 (this
  increment: register + panel; fork deferred) and refresh the stale "Next step".
- [X] T017 `dotnet build` warning-free and `dotnet test` green across the solution (SC-004).
- [X] T018 Run the automated section of `quickstart.md` and confirm the headless outcomes.

---

## Phase 6: Scenario zone — a ref-free type (change)

**Purpose**: A scenario must not store `ZoneContents`. That type exists to reverse a zone *delete* (it keeps each
entity's `EntityRef` and the live `Environment`), and reusing it pushed zone-scoped identity and its list shape
onto scenarios. Introduce a ref-free authored zone instead (FR-012, `docs/ZONE_MODEL_DECISION.md`).

- [X] T019 `world/ScenarioZone.fs`: create the ref-free type — `ScenarioEnvironment` (`AssetId`/`Base`/`Orientation`/`Parts array`), `ScenarioWaypoint` (`Base` + `Next : int option`), and `ScenarioZone` (`Environment option`, `Light`, and `Actors`/`Props`/`Waypoints`/`Triggers` arrays with `Player : int option`), plus the pure `capture : Zone -> ScenarioZone` (de-refs; two passes to resolve waypoint `Next` to an index and the player handle to an actor index).
- [X] T020 `world/ScenarioRegistry.fs`: `ScenarioContents.Zones` becomes `Map<Place, ScenarioZone>`; `capture` maps zones through `ScenarioZone.capture`; update the header/`ScenarioContents` comments (drop the `ZoneContents` reuse note).
- [X] T021 `fsharp-game.fsproj`: add `world/ScenarioZone.fs` after `world/Zone.fs`, before `world/ScenarioRegistry.fs`.
- [X] T022 `test/ScenarioTests.fs`: update the capture tests to `ScenarioZone` (arrays, not `(ref, state)` lists) and add the de-ref assertions — a waypoint chain captured as indices, the player as an actor index, and no `EntityRef`/slot surviving the capture.
- [X] T023 Docs: record the decision as taken in `docs/ZONE_MODEL_DECISION.md`; update `docs/ARCHITECTURE.md` ("Scenario & Cue") and `docs/PROJECT_STRUCTURE.md` (name `world/ScenarioZone.fs`).
- [X] T024 Checkpoint: `dotnet build` warning-free and `dotnet test` green.

**Checkpoint**: a scenario stores a ref-free authored zone; nothing ties it to the zone it was captured from.

---

## Phase 7: Scenario actor tags (addition)

**Purpose**: show who a registered scenario contains, at a glance, the way a zone row in the Outliner shows its
cast — derived from the stored contents, not the live scene (FR-013).

- [X] T025 `editor/ScenarioPanel.fs`: add a pure, public `actorTags : ScenarioContents -> string` — the cast
  characters (`ActorState.Cast`) across the scenario's zones' actors, distinct and sorted, `[A, B]` or `""` — and
  draw it on the row when the scenario holds contents.
- [X] T026 `test/ScenarioTests.fs`: assert `actorTags` — cast actors across two zones aggregate and sort, an
  uncast actor contributes nothing, and a scenario with no actors is `""`.
- [X] T027 Checkpoint: `dotnet build` warning-free and `dotnet test` green.

**Checkpoint**: a registered scenario's cast actors are visible on its own row.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none; blocks everything (the compile order).
- **Foundational (Phase 2)**: depends on Setup; **blocks** the user stories.
- **US1 (Phase 3)**: depends on Foundational; no dependency on US2.
- **US2 (Phase 4)**: depends on Foundational and on US1's panel (its commit path).
- **Polish (Phase 5)**: depends on the stories.
- **Scenario zone (Phase 6)**: a change to the capture value; depends on Phase 2–4 (it replaces `ScenarioContents`'
  zone type).

### Within and between stories

- T002 → T003 → { T004 → T005, T006 }; T003 → T008 → T009 → T010; T008 → T012; T005 → T014.
- The step (T004/T005) is foundational, so US1's Register is committed from the start — US2 adds no throwaway code,
  only the verification that undo is total.

### Parallel Opportunities

- T002 (new file) is independent of the rest of Foundational and can be written first.
- T011 (native) and T012 (tests) run beside T009/T010.
- T016 (docs, distinct files) runs beside any remaining work.

---

## Parallel Example: User Story 1

```bash
# Once T008's capture exists, these touch different files and can go together:
Task: "editor/ScenarioPanel.fs — the panel (Register/Clear via History.commit)"
Task: "test/ScenarioTests.fs — the capture assertions"
Task: "native/cimgui_sdl3.cpp — dock the Scenario window in the left node"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Setup (T001) and Foundational (T002–T007).
2. US1 (T008–T013).
3. **STOP and VALIDATE**: register a scene by hand and assert the capture headlessly.
4. US2 (T014–T015), then Polish (T016–T018).

### Incremental notes

- The step is built once in Foundational and used by both stories — there is no direct-write version to replace.
- **Phase 6 changes Phase 3's capture**: `ScenarioContents.Zones` moves from `ZoneContents` to `ScenarioZone`. It is
a distinct change with its own rationale, recorded as a new phase rather than rewriting the delivered tasks.

## Notes

- `[P]` = different files, no dependencies.
- No cue vocabulary and no run/fork: registering only *captures* (FR-010). Forking is the next increment.
- No module moves; the three new files are inserted into the existing clusters.
- The scenario library is in-memory only until persistence lands (phase 15); nothing is serialized here.
