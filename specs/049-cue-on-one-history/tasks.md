---

description: "Task list for The cue's edits ride the one undo history"
---

# Tasks: The cue's edits ride the one undo history

**Input**: Design documents from `/specs/049-cue-on-one-history/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/cue-history.md, quickstart.md

**Tests**: **Required.** The spec makes them part of the deliverable — SC-005 (`dotnet build` clean and
`dotnet test` green) and SC-001…SC-004 are asserted in `test/CuePanelTests.fs` and `test/GameplayStateTests.fs`,
both of which already build a headless state (`Unchecked.defaultof` for the GPU pieces, a device-free asset
manager). Every story phase therefore has test tasks.

**Organization**: Tasks are grouped by user story so each story's *behaviour* is independently assertable. This is
a small, tightly-coupled feature: US1 and US3 both change `state/History.fs` and both touch the cue, so they are
**sequential**, not parallel. `[P]` appears only where the files are disjoint (the documentation tasks).

**One ordering fact**: a cue step must not exist until its payload and its runners do. The `Command` case, the
`Command.apply`/`undo` refusal and the two `History` arms are therefore **Foundational** (T002–T004): they are
inert until something pushes a `SetCue`, so Phase 2 changes no behaviour and the existing suite stays green.

**One structural fact**: `CueEditor.commit` cannot push onto the one history while `CueHistory` still exists and
its Undo/Redo buttons still call the four functions that walk it — so US1 deletes the history, changes `commit`
and removes the buttons **as one atomic change** (T006 + T007). US2 is the interface half of that same change and
has **no production task of its own**; it is delivered as the assertions that pin it (SC-002, and that the
existing keys and menu now reach a cue edit), which is recorded here rather than dressed up.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Single project at the repository root (this project has no `src/`): game sources under `world/`, `state/`,
`screen/`; editor under `editor/`; tests under `test/`; documentation under `docs/`. F# compiles in `.fsproj`
list order; **no `Compile` entry changes** in this feature — `SetCue` names an existing type (`world/Cue.fs` is
compiled before `state/Command.fs`), `History.pruneCueSteps` an existing module, and `CueEditor` an existing file.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: A known-good starting point. This feature adds no file, no module and no dependency, so there is no
scaffolding to build.

- [X] T001 Take the baseline: `dotnet build` clean (warnings are errors) and `dotnet test` green with the tree at
  `specs/048`'s completion, so every later phase starts from a known-good point

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The step's payload and its runners. **Behaviour-preserving — nothing pushes a `SetCue` at the end of
this phase, so nothing observable changes.**

**⚠️ CRITICAL**: US1 and US3 both need the `SetCue` case and the `History` arms, so neither can start until this
phase builds.

- [X] T002 Add `SetCue of Previous : Cue.Cue * Next : Cue.Cue` to the `Command` union in `state/Command.fs`
  (data-model.md), with the research D2 comment: it is not a zone's, the payload is the two cue values because a
  cue is immutable, and it is `Cue.Cue` module-qualified (no new `open`)
- [X] T003 In `state/Command.fs`, add `SetCue _` to the group `apply` and `undo` already raise on (the three zone
  steps) and reword the `invalidOp` to "a state-level step is on the gameplay state's history, not a zone edit" —
  a cue is not a zone's, so the zone-level runner refuses it exactly as it refuses a zone step
- [X] T004 In `state/History.fs`, add the one `SetCue` arm to each runner, before the fall-through:
  `apply` writes `state.Cue <- next`, `undo` writes `state.Cue <- previous`. Nothing else — no zone, no camera, no
  selection (research D5)
- [X] T005 Confirm `dotnet build` clean and `dotnet test` green — this phase adds only an inert case and two arms,
  so the existing suite must pass untouched

**Checkpoint**: the step's shape and its runners exist, and the editor behaves exactly as before.

---

## Phase 3: User Story 1 - One undo covers the cue and the scene (Priority: P1) 🎯 MVP

**Goal**: A cue edit is a step on the one history, so `Ctrl+Z`/`Ctrl+Y` and the Edit menu take it back and re-apply
it alongside scene edits, in the order they were made.

**Independent Test**: edit the cue, transform an actor, undo twice — the transform reverts first and the cue
second; redo twice re-applies both in the order made.

### Implementation for User Story 1

- [X] T006 [US1] In `state/EditorState.fs`, delete `CueHistory` — the type, its `create`/`push`/`undo`/`redo`
  module, the `CueHistory : CueHistory` field and its `CueHistory.create ()` seed — and trim the field-group
  comment so it describes the selection and the text buffer without a history. `CueSelection`, `CueTextBuffer`,
  `CueTextPath` and `CueTextEditing` stay (FR-008)
- [X] T007 [US1] In `editor/CueEditor.fs`, change `commit` to write `state.Cue` and push `SetCue (before, next)`
  onto `state.Editor.Undo` itself — **not** through `History.commit`, so the panel keeps taking only the gameplay
  state (add `open Command`, or qualify `Command.SetCue`/`Command.UndoStack.push`). Delete `canUndo`, `canRedo`,
  `undo`, `redo`, and the block in `draw` that drew their two `smallButton`s (and its comment). Rewrite the header
  comment: the panel edits the cue through the one history, not one of its own. **T006 and T007 land together** —
  deleting `CueHistory` breaks the four functions, so the build is only green once both are done
- [X] T008 [US1] Tests in `test/CuePanelTests.fs`, replacing **only** the `CueHistory` tests and the
  `CueEditor.commit`/`undo`/`redo`/`canUndo`/`canRedo` round trip; the spec-047 row, lock-rule and addition-rule
  tests stay untouched, which is where FR-007's "the editing rules are unchanged" is still asserted. Add a small
  headless `shared` fixture (mirroring `test/GameplayStateTests.fs`'s, with `Physics.create ()` and a device-free
  asset manager — no entity is spawned, so no prop asset is needed). Then assert: the panel's write path —
  `CueEditor.commit` changes `state.Cue` once and leaves exactly **one** `SetCue` on `state.Editor.Undo`; `History.undoHistory` restores the earlier cue and
  `History.redoHistory` re-applies the later one (`describe`, never a value comparison); an undo with nothing
  applied is a no-op that leaves the cue alone. Then **SC-001**: a cue edit followed by a `Transform` on an actor
  placed directly in the zone (`zone.Actors.Add`, its renderer `Unchecked.defaultof`) undone in the reverse of the
  order made and redone in the order made. Then **SC-004**: undo through the whole history returns the cue to its
  starting value, and a new edit after an undo clears the redo side. Then **FR-006**: undoing a cue step leaves the
  active zone, the camera and `state.Editor.CueSelection` exactly as they were

**Checkpoint**: User Story 1 is fully functional and testable on its own — this is the MVP. One undo covers the cue
and the scene.

---

## Phase 4: User Story 2 - The cue panel has no undo of its own (Priority: P2)

**Goal**: The Cue panel offers no Undo or Redo control; the only entry points are the keys and the Edit menu, and
both reach a cue edit.

**Independent Test**: the panel's compiled surface exposes no `undo`/`redo`/`canUndo`/`canRedo`; after a cue edit
the one history is non-empty, which is what the Edit menu's gate reads.

**Implementation note**: the controls' removal is not a task of its own — deleting `CueHistory` (T006) forces the
four functions and the two buttons to go (T007), so US2 is the interface half of US1's change. What remains is to
pin it.

### Implementation for User Story 2

- [X] T009 [US2] Tests in `test/CuePanelTests.fs`: **SC-002** — assert the compiled `CueEditor` surface
  (`typeof<GameplayState.GameplayState>.Assembly.GetType("CueEditor")`) exposes **no** `undo`, `redo`, `canUndo` or
  `canRedo` method, with `commit`/`draw` present as the anchor that the lookup found the type. And US2/AC2–AC3 — a
  cue edit leaves `state.Editor.Undo.Applied` non-empty (so the Edit menu's `idle && not (List.isEmpty …)` gate and
  the `Ctrl+Z` binding are satisfied), asserted as the history's shape rather than by drawing ImGui, which needs a
  device. **FR-009** (undo must not fire while a text field wants input or a drag is in progress) needs no new
  task or assertion here: it is preserved by the unchanged gate — `Editor.handleShortcuts` early-returns on
  `ImGui.wantTextInput()`, and the Edit menu's `idle` reads `state.Editor.Drag = None &&
  not (ImGui.wantTextInput())` — and cannot be exercised headlessly

**Checkpoint**: User Stories 1 and 2 both hold — the cue's undo is the one undo, and the panel no longer invites a
second.

---

## Phase 5: User Story 3 - Play drops only what went stale (Priority: P3)

**Goal**: Entering Play removes the cue's steps from both sides of the history and leaves every zone step
undoable.

**Independent Test**: put a cue step and a zone step on the history, prune, and find 0 cue steps and the zone step
still undoable.

### Implementation for User Story 3

- [X] T010 [US3] In `state/History.fs`, add `pruneCueSteps : byref<GameplayState.GameplayState> -> unit`
  (data-model.md): filter every `SetCue` out of **both** `Applied` and `Redo` and leave every other step exactly as
  it was. It takes no `SharedState`, because a cue step touches no zone and no resource
- [X] T011 [US3] In `editor/Editor.fs`, replace the play toggle's
  `state.Editor <- { state.Editor with CueHistory = CueHistory.create () }` with `History.pruneCueSteps &state`,
  and update its comment: the cue's *steps* go on the way into play because the cue advances while playing, while
  every zone step stays
- [X] T012 [US3] Tests in `test/GameplayStateTests.fs`: with a `SetCue` and a zone step (a `SetLight` or a
  `SetPlayer`, as that file already commits) on the history, `History.pruneCueSteps` leaves **0** cue steps and the
  zone step still undoable (**SC-003, FR-010**); a cue step that had been undone is dropped from the **redo** side
  too; pruning an empty history is a no-op

**Checkpoint**: All three user stories are independently functional — the cue's edits ride the one history, the
panel has no undo of its own, and a play drops only the cue's steps.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: The record of what is superseded, the documents the feature moves with it, and end-to-end validation.

- [X] T013 [P] Confirm the superseded statements are recorded in `specs/049-cue-on-one-history/research.md`
  (Decision 8) — the table of *where it was written → what this feature does instead*, with what is **not**
  superseded. **Do not edit the prior specs** — `specs/047-cue-panel` and `specs/048-unified-undo-stack` are
  historical artefacts, and the record lives here
- [X] T014 [P] Correct `docs/EDITOR_DESIGN.md`: the workspace table's Cue row (no own history); D-010 (the cue's
  undo — it is a step on the one history now); D-011 (the cue joins the command family, and the "non-entity
  surfaces" clause's cue example is corrected); D-013 (the panel keeps its tree list, not its own history); the
  module map's `CueEditor`/`CueTree` row
- [X] T015 [P] Correct `docs/PROJECT_STRUCTURE.md`: the `CueEditor.fs` line (`spec 047; direct write, its own
  history` → the cue panel, whose edits are steps on the one history)
- [X] T016 [P] Correct `docs/ROADMAP.md`: phase 9's cue-editor deliverable and the "Next step" summary, both of
  which call the panel's history its own. `docs/CUE_MODEL_DECISION.md` is deliberately **not** moved — it records
  the earlier model and is marked superseded
- [X] T017 Run `dotnet build` (warnings-clean) and `dotnet test` (green, including the adapted cue-panel cases and
  the new SC-001/cross-edit/prune cases), then validate `quickstart.md` by hand: one `Ctrl+Z` takes back a cue edit
  and then a scene edit in the order made, the Cue panel draws no Undo/Redo control, and a play returns with every
  zone edit still undoable and no cue step left

---

## Outcomes

Recorded, not relitigated — where the implementation differed from the plan.

- **The atomic change had a third file.** The plan's note said T006 + T007 land together, on the reasoning that
  deleting `CueHistory` breaks the four functions in `CueEditor`. It missed that `editor/Editor.fs` also named the
  deleted type (the play toggle's `CueHistory.create ()`), so it had to change in the same step. Rather than ship
  an intermediate with the prune removed and re-added, **T010 (`History.pruneCueSteps`) and T011 (its wiring) landed
  with it**, in Phase 3. The build was green at every step and no phase regressed spec 047's property; US3 is still
  separately asserted by T012, so the story split holds. `contracts/cue-history.md` and `data-model.md` already
  described the `Editor.fs` change, so only the ordering note above needed the correction.
- **T014's module-map claim was optimistic.** `docs/EDITOR_DESIGN.md`'s `CueEditor`/`CueTree` module-map row never
  mentioned a history, so it needed no change; the row was checked and left as it is. The D-011 bullet needed more
  than the plan said — not just the cue example corrected, but the "only zone-scoped state is undoable" sentence,
  which the cue now contradicts.
- **One test needed a two-element cue.** `seqOf [ x ]` collapses to a bare leaf, so the first cut of the SC-001
  and FR-006 cases edited a path that named nothing and raised from `CueTree.withAt`. Both now use
  `seqOf [ Exposit "a"; Wait 1.0 ]` and edit path `[ 0 ]`.
- **The reflection check for SC-002 resolved.** `Assembly.GetType "CueEditor"` finds the module's compiled type;
  the test anchors on `commit` being present (the lookup found the type) and asserts the absence of `undo`, `redo`,
  `canUndo` and `canRedo`.
- **Result**: build **0 warnings / 0 errors**; `dotnet test` **399 passed / 0 failed** (394 before the feature: −5
  replaced cases, +10 new — 7 in `CuePanelTests` and 3 in `GameplayStateTests`).
- **The interactive walkthrough in `quickstart.md` was not run** (it needs a display). The headless half is green;
  the four editor steps need a hand.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies — can start immediately
- **Foundational (Phase 2)**: depends on Setup — **blocks US1 and US3** (both need the `SetCue` case and the
  `History` arms)
- **User Stories (Phase 3–5)**: US1 and US3 depend on Foundational; US2 depends on US1 (its assertions are about
  what US1 removes). US1 and US3 both edit `state/History.fs`, so they are sequential
- **Polish (Phase 6)**: depends on all three stories

### User Story Dependencies

- **US1 (P1)**: after Foundational. No dependency on US2/US3 — it is the MVP
- **US2 (P2)**: after **US1**. It asserts the interface US1's structural change produces; it has no production
  task of its own
- **US3 (P3)**: after Foundational, and independent of US2. It adds `pruneCueSteps` to `History.fs` (which US1
  also edits) and wires it in `Editor.fs`; it does not depend on how US1 makes the panel commit

### ⚠️ Why US1 and US3 Are Sequential Here

Both add to `state/History.fs`, and both are about the same step. They are separate stories because they are
separately *assertable* (one undo across the panel and the scene; a play dropping only the cue's steps), not
because they can be built in parallel. `[P]` is therefore used only where files are disjoint — the documentation
tasks in Phase 6.

### Within Each Story

- Foundational first: the `Command` case (T002) before the runner handles it (T004), and the refusal (T003) with
  it
- US1 is atomic: `EditorState` (T006) and `CueEditor` (T007) land together, then the tests (T008). Do not run
  `dotnet build` between T006 and T007 and read a failure as a regression — the four deleted functions are what
  breaks, and that is the point
- US3: `pruneCueSteps` (T010) before the `Editor.fs` call site (T011) before the test (T012)

### Parallel Opportunities

- T001 and T005 are sequential with their phases (a build check after the work)
- Phase 6's documentation tasks **T014–T016** are three disjoint files and can run in parallel
- Nothing else is parallel: every other pair of tasks shares a file

---

## Parallel Example: Documentation (Phase 6)

```text
# Three different files — launch together:
Task: "docs/EDITOR_DESIGN.md — the Cue row, D-010, D-011, D-013, the module map row (T014)"
Task: "docs/PROJECT_STRUCTURE.md — the CueEditor.fs line (T015)"
Task: "docs/ROADMAP.md — phase 9 and the Next step summary (T016)"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (behaviour-preserving — the suite must still be green)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: edit the cue, transform an actor, undo twice — both come back off one history, in order
5. This is already the point of the feature: one undo, wherever the edit was made

### Incremental Delivery

1. Setup + Foundational → the step exists and does nothing
2. US1 → the cue's edits ride the one history (MVP)
3. US2 → the panel no longer offers a second undo, pinned by SC-002
4. US3 → a play drops the cue's steps and keeps the zone edits
5. Polish → the documents and the superseded-decisions record move

### Notes

- **[P]** = different files, no dependencies. Most of this feature is one file deep, so expect few.
- **Do not route `CueEditor.commit` through `History.commit`.** That would make the panel take `SharedState`,
  which it does not need; it writes and pushes itself, like every other editor call site (research D3).
- **Do not clear the whole history on Play.** Only the `SetCue` steps go, from both sides; clearing would forfeit
  spec 048's property, which US3 exists to protect (FR-005, research D4).
- **Do not delete `CueSelection`/`CueTextBuffer`/`CueTextPath`/`CueTextEditing`.** Only `CueHistory` goes; the
  selection and the text buffer are interface scaffolding (FR-008).
- **Do not compare two `Cue` values in tests.** A cue holds an `FDeque`, and the engine answers "same value" by
  reference identity; read cues through `describe`, as the suite already does.
- **Do not change the cue, its engine, `world/CueEngine.fs`, the `.fsproj`, the panel's editing rules, the rest of
  the command family, the render path or the physics world.**
- Commit after each task or logical group; validate each story at its checkpoint.

---

## Phase 7: Convergence

Appended by `/speckit-converge` (2026-10-01). The implementation satisfies the spec, plan and tasks; two
acceptance criteria are **implemented but not asserted**. Append-only — nothing above was changed.

- [X] T018 [US1] Assert in `test/CuePanelTests.fs` that a cue edit made before a **zone move** is still on the one
  history after the move and is reached by a second undo: commit a cue edit, add a zone and `History.commit` a
  `ZoneMove` (the way `test/GameplayStateTests.fs` does — `GameplayState.addZone shared &state "classroom"` then
  `ZoneMove ("main", "classroom")`), then `History.undoHistory` steps the move back (*main* active again) and a
  second `undoHistory` reverts the cue edit. A cue spans zones, so this is `specs/048`'s cross-zone case with a
  cue step standing in for the zone edit (US1/AC4, `partial`)
- [X] T019 [US3] Assert in `test/GameplayStateTests.fs` that a Play with **no** cue edits on the history leaves it
  exactly as it was: commit two zone steps (a `SetLight` and a `ZoneMove`, or two `SetLight`s), call
  `History.pruneCueSteps`, and assert both `Applied` and `Redo` are unchanged in count **and order** — the
  existing prune cases cover a cue step and an empty history, not a history that holds zone steps and no cue steps
  (US3/AC2, `partial`)

**Phase 7 expected outcome.** T018 and T019 are test-only; no production code changes. `dotnet build` stays at 0
warnings / 0 errors and `dotnet test` goes from **399 to 401**.

**Phase 7 outcome.** As expected: both landed as test-only. T018 is
`a cue edit made before a zone move is still reachable after it` — two cue edits with a `ZoneAdd` between them,
undone B → the move → A, with the active zone following — and T019 is
`a play with no cue edits leaves the history exactly as it was`, which undoes one step first so the redo side is
non-empty and checks both sides by pattern. `dotnet build` is clean and `dotnet test` is **401 passed / 0 failed**.
