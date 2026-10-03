# Tasks: Adventure Flags and the Advent Panel

**Input**: Design documents from `/specs/055-advent-flags/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md,
contracts/advent-flags.md, quickstart.md

**Tests**: Not TDD. The union, the set, the panel's write path and the history steppers are pure data,
so they are exercised **headlessly** — the acceptance cases are part of each story's implementation,
as every state-level spec's tests are (`specs/048`, `specs/049`). No device, window or GPU is opened.

**Organization**: Tasks are grouped by user story. The union, its `SharedState` home and the command
are shared foundation (Phases 1–2) and block every story. **US1** (P1) is ticking a milestone, **US2**
(P2) is clearing one, **US3** (P3) is the undo step that covers both.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US3)
- Include exact file paths in descriptions

## Path Conventions

Single-project F# repository. Paths are relative to the repository root (`fsharp-game/`):
`world/`, `state/`, `editor/`, `native/`, `test/`, `specs/`, `docs/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the union, its set and the field the panel and the command rest on.

- [X] T001 Create `world/Advent.fs`: the closed `type Advent` union and the two helpers `all`
  (enumerated from the type) and `name` (an explicit match), with the header stating the union and
  ordered-set rationale. The flags are a plain `Set<Advent>`, manipulated with the standard `Set`
  functions. (data-model.md; research D1; FR-001, FR-008.)
- [X] T002 Add `Compile Include="world/Advent.fs"` to `fsharp-game.fsproj` after
  `world/EnvironmentRender.fs`, so the pure data module is listed before the state that names it.
  (plan.md Project Structure.)
- [X] T003 Add `mutable Advents : Set<Advent>` beside `ZoneRegistry` in `state/SharedState.fs` and
  seed it `Set.empty` in `create`; update the five `SharedState` fixtures (`test/CueTests.fs`,
  `test/CuePanelTests.fs`, `test/GameplayStateTests.fs`, `test/SelectionTests.fs`,
  `test/SpawningTests.fs`) with `Advents = Set.empty`. (data-model.md; research D2; FR-002.)

**Checkpoint**: `dotnet build` is green; the union exists and the set is empty everywhere.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: the command and its history arms must exist before US3 can be built or verified.

- [X] T004 In `state/Command.fs`, add the state-level case `Advent of Previous : Set<Advent> *
  Next : Set<Advent>`, and add `Advent _` to the `invalidOp` arm of `Command.apply`/`undo` that
  already refuses `ZoneMove`/`ZoneAdd`/`ZoneDelete`/`SetCue`. (data-model.md; research D3/D7;
  FR-007.)
- [X] T005 In `state/History.fs`, add `| Advent (_, next) -> shared.Advents <- next` to `apply` and
  `| Advent (previous, _) -> shared.Advents <- previous` to `undo`, beside the `SetCue` arms.
  (data-model.md; research D3; FR-007, FR-009.)

**Checkpoint**: the command round-trips through `History` — US1–US3 can be built.

---

## Phase 3: User Story 1 - The author records a story milestone by ticking it (Priority: P1) 🎯 MVP

**Goal**: the Advent panel lists every milestone and ticking one records it; it outlives a zone switch.

**Independent Test**: commit a milestone through the write path against a real `SharedState` and assert
the set; switch zones and assert it is unchanged (quickstart §3).

### Implementation for User Story 1

- [X] T006 [US1] Create `editor/AdventPanel.fs`: `commit` records a `next` set as one `Advent` step via
  `History.commit`; `draw` opens the `"Advent"` window and draws one checkbox per `Advent.all`, seeded
  with `Set.contains advent shared.Advents`, calling `commit` with `Set.add` on a tick. (research D6;
  FR-003, FR-004, FR-005, FR-006.)
- [X] T007 [US1] Add `Compile Include="editor/AdventPanel.fs"` to `fsharp-game.fsproj` after
  `editor/CastPanel.fs`. (plan.md Project Structure.)
- [X] T008 [US1] In `editor/Editor.fs`, add `"Advent"` to `panelNames` and call `AdventPanel.draw`
  after `CastPanel.draw`. (contracts; FR-003.)
- [X] T009 [US1] Add `ImGui::DockBuilderDockWindow("Advent", right);` to `cimgui_setup_dock_layout`
  in `native/cimgui_sdl3.cpp` with the comment the others carry, and rebuild `libcimgui.so` with
  `bash native/build.sh`. (research D4; FR-003.)
- [X] T010 [US1] Create `test/AdventTests.fs` and add its `Compile` entry to
  `test/fsharp-game.Tests.fsproj`: `Advent.all` is every case in declaration order; the set helpers
  (membership, immutability); the write path setting a milestone and the set surviving a zone switch.
  (FR-001, FR-002, FR-004, FR-005, FR-006, FR-008; SC-001, SC-004.)

**Checkpoint**: every milestone is listed, tickable, and survives a zone switch.

---

## Phase 4: User Story 2 - The author clears a milestone (Priority: P2)

**Goal**: unticking a milestone clears it.

**Independent Test**: with two milestones set, clear one and assert only the other remains
(quickstart §3).

### Implementation for User Story 2

- [X] T011 [US2] Ensure `editor/AdventPanel.fs`'s checkbox routes an untick to `commit` with
  `Set.remove` (the same `if` handles both directions of the toggle). (research D6; FR-005.)
- [X] T012 [US2] In `test/AdventTests.fs`, add the clearing cases: clearing one of two milestones
  leaves the other; clearing the only one leaves the empty set. (FR-005; SC-002.)

**Checkpoint**: the panel is a real editor — milestones are set and cleared.

---

## Phase 5: User Story 3 - Each toggle is one undoable step on the one history (Priority: P3)

**Goal**: each toggle is one `Advent` step; Ctrl+Z restores the prior set, Ctrl+Y re-applies it, beside
a zone move.

**Independent Test**: set a milestone, undo, assert the set is back and the step is on the redo side;
redo and assert it is back (quickstart §3).

### Implementation for User Story 3

- [X] T013 [US3] In `test/AdventTests.fs`, add the history cases: one toggle is exactly one `Advent`
  step carrying the before/after sets; `History.undoHistory` restores the before set and
  `History.redoHistory` the after set; an `Advent` step beside a `ZoneMove` reverses in order; and
  the zone-level runner refuses an `Advent` step. (FR-007, FR-009; SC-003.)

**Checkpoint**: all three stories work; the flag edits ride the one history with the scene edits.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: the startup tab, the docs, and the green build.

- [X] T014 In `editor/Editor.fs`, submit `Inspector.draw` **first** among the right node's panels and queue
  `PendingFocus <- Some "Inspector"` on the docking-init frame, so the Inspector is both the leftmost and the
  selected tab when the editor opens (a node's tab order follows its windows' submission order; the selected tab
  defaults to the last submitted). (Revised 2026-10-03 from "submit it last"; research D5; user request.)
- [X] T015 [P] Update `docs/PROJECT_STRUCTURE.md`, `docs/EDITOR_DESIGN.md` (the Advent docks right and
  is a milestone checklist; D-011: the flags ride the one history) and `docs/ROADMAP.md`.
- [X] T016 Build `fsharp-game.fsproj` and `test/fsharp-game.Tests.fsproj` with **0 warnings** and run
  the full suite green; walk `quickstart.md` end to end (the tick, the clear, the zone switch, the
  undo/redo, the relaunch, the startup tab). (FR-001–FR-011; SC-001–SC-005.)

---

## Dependencies & Execution Order

- **Setup (Phase 1)**: T001 → T002; T003 depends on T001 and blocks everything.
- **Foundational (Phase 2)**: depends on T003; blocks US3.
- **US1 (Phase 3)**: depends on T003–T005; T006 is the MVP.
- **US2 (Phase 4)**: depends on T006 (same file).
- **US3 (Phase 5)**: depends on T004/T005 and T006.
- **Polish (Phase 6)**: depends on all of the above.

### Same-file constraint (sequential)

- `editor/AdventPanel.fs`: T006 → T011.
- `test/AdventTests.fs`: T010 → T012 → T013.
- `fsharp-game.fsproj`: T002 → T007.
- `editor/Editor.fs`: T008 → T014.

### Parallel Opportunities

- T004 (`state/Command.fs`) and T005 (`state/History.fs`) can run in parallel once T003 lands.
- T015 (docs) is independent of the code tasks once T001–T014 are done.

---

## Notes

### Revision (2026-10-03, after review)

The first cut used the references' string model (a `Set<string>` with a free-text add field, docked
left). On review it was revised to:

- a **closed union** for milestones (`OpeningComplete | MetAiko | …`) with the flags a plain
  `Set<Advent>`, so a milestone cannot be misspelt and the panel enumerates the cases (D1, D6;
  FR-001, FR-004, FR-008);
- a **checklist** with one checkbox per case, removing the add field, the `AdventNameBuffer` and the
  blank/duplicate rules (D6; FR-005);
- a **right** dock beside the Inspector, not the left (D4; FR-003);
- the **Inspector as the startup tab**, by submitting it first and focusing it (D5; T014; revised 2026-10-03).

### Implementation status (2026-10-03)

- **T001–T016: done.** Both projects build with **0 warnings** and `dotnet test` passes. The union,
  the `Set<Advent>` flags, the `SharedState.Advents` field, the `Command.Advent` step in
  `state/History.fs`, the checklist panel in `editor/AdventPanel.fs` (right-docked) and the native
  layout line are in; no cue case is added (FR-011); no `EditorState` field is added.
- Tests are part of each story, not a separate phase; the acceptance cases live in
  `test/AdventTests.fs`.
- The panel's drawing is device-bound (ImGui), so its rendering and the startup tab are the
  quickstart walkthrough's business; its **write path** is asserted headlessly.
