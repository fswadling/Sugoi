---

description: "Task list for Cue Panel implementation"
---

# Tasks: Cue Panel

**Input**: Design documents from `/specs/047-cue-panel/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/cue-panel.md, quickstart.md

**Tests**: **Required.** The spec makes tests part of the deliverable — FR-007 requires the control's labels,
child structure and lock state to be a *pure* function "asserted headlessly", FR-010 names `dotnet build` and
`dotnet test`, and SC-004/SC-006/SC-007/SC-008 are stated as measurable outcomes. Every story phase therefore
has test tasks.

**Organization**: Tasks are grouped by user story so each story's *behaviour* is independently assertable. The
three stories are three behaviours of one small subsystem, and **US1–US3 all add to the same two source files
(`editor/CueTree.fs`, `editor/CueEditor.fs`) and the same test file (`test/CuePanelTests.fs`)**, so they are
sequential, not parallel, despite being separate stories. That is the feature's shape rather than a shortcut —
it is stated under *Dependencies* so it is not mistaken for missing independence. `[P]` appears only where the
files are disjoint.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Single project at the repository root (this project has no `src/`): game sources under `world/`, `state/`,
`screen/`, `editor/`; tests under `test/`; the ImGui shim under `native/`; documentation under `docs/`. F#
compiles in `.fsproj` list order, which this feature's correctness depends on (see T004).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Scaffolding so the two new modules and the test file exist, the panel is docked, and the build
stays green before any behaviour lands.

- [X] T001 [P] Create `editor/CueTree.fs` as `module CueTree`, opening `Cue` and `Prime` (empty body)
- [X] T002 [P] Create `editor/CueEditor.fs` as `module CueEditor`, opening `Cue` and `GameplayState` (empty body)
- [X] T003 [P] Create `test/CuePanelTests.fs` as `module CuePanelTests`, opening `Xunit`, `Prime`, `Cue`, `CueTree` (empty body)
- [X] T004 Add `Compile` entries to `fsharp-game.fsproj`: `editor/CueTree.fs` then `editor/CueEditor.fs`, after `editor/DialoguePanel.fs` and before `editor/Editor.fs`, with a short comment saying why the order is what it is (the panel calls the control; the workspace calls the panel)
- [X] T005 [P] Add the `Compile` entry for `test/CuePanelTests.fs` to `test/fsharp-game.Tests.fsproj` (after `CueTests.fs`)
- [X] T006 Add `ImGui::DockBuilderDockWindow("Cue", right);` to `native/cimgui_sdl3.cpp`'s `cimgui_setup_dock_layout`, a tab beside the Inspector and the one sub-editor not in the left column, with the comment the others carry (FR-001), then rebuild with `bash native/build.sh` and confirm `libcimgui.so` is produced
- [X] T007 Confirm `dotnet build` is clean (warnings are errors) with the empty scaffolding in both `.fsproj` files

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The editor's cue state, in one file, so that no story phase has to open `state/EditorState.fs`.
Every story reads it — US1 the selection, US3 the text buffer and the history.

**⚠️ CRITICAL**: `CueHistory` must exist before `EditorState` can hold it, and the selection must exist before
the panel can record one.

- [X] T008 Add `CueHistory = { Applied : Cue.Cue list; Redo : Cue.Cue list }` and its `create` / `push` / `undo` / `redo` to `state/EditorState.fs` (data-model.md) — the value **before** each accepted edit, snapshots rather than commands, because a cue is immutable and shared so a snapshot is one reference
- [X] T009 Add the five fields to the `EditorState` record and to `EditorState.create` in `state/EditorState.fs`: `CueHistory` (seeded `CueHistory.create ()`), `CueSelection : int list option` (`None`), `CueTextBuffer : byte[]` (a `[<Literal>]` size, as the file's other buffers have), `CueTextPath : int list option` (`None`) and `CueTextEditing` (`false`)

**Checkpoint**: `dotnet build` is clean; the editor's state has somewhere to put the cue's history, a selection and a text buffer.

---

## Phase 3: User Story 1 - The running cue is visible (Priority: P1) 🎯 MVP

**Goal**: The Cue panel sits in the right column beside the Inspector and shows the screen's cue as it stands — a row per container,
children indented, the running op first among its sequence's children, the ops already run absent, and `Fin`
when there is nothing left to run.

**Independent Test**: Put a cue whose first op waits on a screen's state, read `CueTree.rows` and assert the
tree it derives; then open the editor and read the panel. No device needed for the first half, and no editing
involved in either.

### Implementation for User Story 1

- [X] T010 [US1] In `editor/CueTree.fs`, define `[<Struct>] Row = { Path : int list; Depth : int; Label : string; Locked : bool }` and implement `children`, `executing` and `rows` (data-model.md): a row per container with children indented one deeper, the queue behind a running op in its own order, `Fin` its own row, and the label vocabulary — `Fin`, `Sequence`, `Parallel`, `Fork`, `Wait 2`, `Exposit: hello`, `Zone: classroom`, `Move camera` — so a `WaitState` reads as the `Wait` it came from (FR-002, FR-004). `executing` is defined here with the rows because FR-007 puts the lock in the same pure function; US3 is what *uses* it
- [X] T011 [US1] Implement `rowId` and `drawTree` in `editor/CueTree.fs`: one `ImGui.selectableBool` per row, indented by `Depth * 2` spaces in the label (the reference's own idiom — the binding has no `indent`), the row's id built by `rowId` from the caller's prefix and the path, and the row clicked this frame returned as `int list option`. No `beginDisabled`: a locked row is selectable and readable (FR-005). The rows are drawn straight into the window, which scrolls, so a cue of any size is scrolled rather than truncated (FR-009) — no `beginChild` and no cap
- [X] T012 [US1] Implement `draw` in `editor/CueEditor.fs`: open the `"Cue"` window, read `state.Cue`, call `CueTree.drawTree "cue" state.Editor.CueSelection state.Cue`, and write the clicked path back to `state.Editor.CueSelection`. Read-only: an edit is US3's
- [X] T013 [US1] Wire `editor/Editor.fs`: add `"Cue"` to `panelNames` (so Ctrl+Tab reaches the panel, FR-001) and `do CueEditor.draw &state` to `draw` beside the other panels (FR-008)
- [X] T014 [US1] Tests in `test/CuePanelTests.fs`: a bare leaf is one row at depth 0; a `Sequence` is a row whose children are indented one deeper and listed front-first; `Fin` is a row with no children; `WaitState (2, deadline)` is labelled `Wait 2` while `Wait 2.0` is labelled the same, so the view is what is left to run and never an internal state's name (FR-002, FR-004, SC-001); the running leaf's row is marked `Locked` and a fresh authored cue marks none

**Checkpoint**: User Story 1 is fully functional and testable on its own — the panel shows the running cue. This is the MVP.

---

## Phase 4: User Story 2 - One cue control, reused (Priority: P2)

**Goal**: The cue the panel shows is rendered by a single control that knows nothing about where the cue is
stored, and two cues drawn in one window do not collide.

**Independent Test**: Search `editor/` for the recursive cue rendering — it exists once, in `editor/CueTree.fs`,
and `CueEditor` is one of its callers; then assert headlessly that a row's id changes with the caller's prefix
and is stable under it.

### Implementation for User Story 2

- [X] T015 [US2] In `editor/CueTree.fs`, make the caller's id prefix reach **every** id the control creates for the rows — each row's widget id is `rowId` of the caller's prefix and the node's path — so two cues drawn in one window cannot collide, and the same node keeps its identity across frames (FR-006)
- [X] T016 [US2] Hold the control's dependency boundary in `editor/CueTree.fs`: it names `Cue`, `Prime` and `ImGui` and nothing else — no `GameplayState`, `EditorState`, `Command`, zone or asset — and say so in the module's header comment, so a trigger's cue (phase 10) and a scenario's cue (phase 11) can draw it (FR-006, SC-003)
- [X] T017 [US2] Tests in `test/CuePanelTests.fs`: `rowId` differs for two different prefixes at the same path and is identical for the same prefix and path; and the derivation takes nothing but a cue — two identical cues derive identical rows, so a caller cannot change what a cue says (SC-003)

**Checkpoint**: User Stories 1 and 2 both work independently — the panel is a caller of a control that could serve another.

---

## Phase 5: User Story 3 - The author edits what is still to run (Priority: P3)

**Goal**: With the cue frozen in the editor, the author changes what has not happened yet — a queued op's kind
and numbers, an op appended to the sequence that is running, one removed — while what is executing is never
retuned, replaced or removed;
every accepted change is one step the cue's own undo can take back, and the history goes when Play begins.

**Independent Test**: With a cue paused part-way through a wait, edit a queued op, undo it and read the cue
back — the change and its reversal are exact; append one to the running sequence and see it land on the queue;
then confirm the running op offers no way to be changed and the sequence holding it offers exactly its addition.

### Implementation for User Story 3

- [X] T018 [US3] Implement the path helpers in `editor/CueTree.fs`: `tryAt` (the harmless question, `None` when a path names nothing), `withAt` and `withoutAt` (both of which **raise** on a path that names nothing, and `withoutAt []` raises because the root has no parent), and `withAppendedChild` (the rebuilt node **and** the added child's path, so the two cannot disagree about where it landed) (research D3, Principle II)
- [X] T019 [US3] Implement `drawEditor` in `editor/CueTree.fs` (data-model.md): the Kind dropdown over the ten cases replacing the node with that kind's default; per-kind fields — `Wait` seconds, `Exposit` text, `SwitchZone` zone, `MoveCamera` target (x, y, z) and seconds; **Add Child** on any `Sequence`, running or not, because an addition lands on a queue whose tail is ahead of the running op; **Remove** on any node but the root and never on one on the running path; and, for a node on the running path, no kind and no numbers, with a running node that is not a `Sequence` saying in one line why it offers nothing (FR-011, FR-012). Every field widget carries the caller's prefix in its id too (`rowId` of the prefix and the node's path, plus the field's own name), so a second caller's cue cannot collide with the panel's (FR-006)
- [X] T020 [US3] Implement `commit`, `undo`, `redo`, `canUndo` and `canRedo` in `editor/CueEditor.fs`: one write path (`state.Cue <- CueTree.withAt path replacement state.Cue`) and one step on the cue's own history, pushed **only when the replacement differs from the node** (research D4); Undo/Redo buttons in the panel, disabled when their side is empty, because Ctrl+Z is the zone stack's (research D5)
- [X] T021 [US3] Implement the text field's discipline in `editor/CueEditor.fs`: seed `CueTextBuffer` from the selected node and re-point `CueTextPath` **only while** `CueTextEditing` is false, commit on submit or `isItemDeactivatedAfterEdit`, and track `CueTextEditing` from `isItemActive` — the Inspector's `NameBuffer` rule, so an edit in progress is never overwritten and its text is never applied to a different node
- [X] T022 [US3] Implement the selection rules in `editor/CueEditor.fs`: an accepted **Add** selects the child just added (`withAppendedChild`), an accepted **Remove** of the selected node clears the selection, and the selection is validated with `tryAt` each frame so a path that names nothing is cleared rather than silently pointing at a different node (the spec's edge case, FR-005). Selecting writes `state.Editor` only: opening the panel, reading it and selecting a node never touch `state.Cue`, so looking at the cue changes nothing (SC-005)
- [X] T023 [US3] In `editor/Editor.fs`, drop the cue's history on the way **into** play: in the backtick toggle, when `state.Editing` goes false, `state.Editor <- { state.Editor with CueHistory = CueHistory.create () }` (FR-013)
- [X] T024 [US3] Tests in `test/CuePanelTests.fs`: the lock rule — a fresh authored cue locks 0 rows; a cue part-way through a wait locks the leaf **and the spine to the root** and locks 0 of the queue behind it; a running `Parallel` locks both branches; a `Fork`'s inner branch is locked while the remainder behind it is not; and the **addition** rule beside it — a running `Sequence` takes a child, while the op in flight, a leaf, a `Parallel` and a path that names nothing do not (FR-011, SC-006)
- [X] T025 [US3] Tests in `test/CuePanelTests.fs`: `withAt` replaces exactly the node a path names and leaves every other node the same value; `withoutAt` removes exactly it; `tryAt` reports a path that names nothing while `withAt`/`withoutAt`/`withAppendedChild` raise; a round trip through `withAt` gives back what was put in (FR-005, SC-007)
- [X] T026 [US3] Tests in `test/CuePanelTests.fs`: a push then an undo restores the cue exactly and a redo puts the edit back; a push clears the redo side; an undo with nothing applied is a no-op; a `create`d history has nothing to take back (FR-013, SC-008)

**Checkpoint**: All three user stories are independently functional — the panel shows, shares and edits.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: The documentation the spec moves with the feature, the one property that is asserted by
inspection rather than by the suite, and end-to-end validation.

- [X] T027 [P] Amend `docs/ROADMAP.md`: phase 9's editor deliverable (`editor/CueEditor.fs` + `editor/CueTree.fs`, recursive tree list, with the cue's own undo history) is complete, its "done when" is met, and the next-step line advances to phase 10 (research D9)
- [X] T028 [P] Amend `docs/PROJECT_STRUCTURE.md`: `editor/CueEditor.fs` and `editor/CueTree.fs` lose their "(planned)" and say what they are (research D9)
- [X] T029 [P] Amend `docs/EDITOR_DESIGN.md`: D-010 gains that the cue's history is per editing session and dropped when play begins; D-013 gains the row-per-container shape, the running path that takes an addition but not a replacement, and that reorder is out of this cut; D-011's "cue root" note is brought up to date, as the cue root has now landed with its own history (research D9)
- [X] T030 The SC-003 inspection: exactly one implementation of the recursive cue rendering exists (in `editor/CueTree.fs`), `editor/CueEditor.fs` is one of its callers, and no other module repeats it
- [ ] T031 Run `dotnet build` (warnings-clean) and `dotnet test` (green, including the lock, path and history assertions), then validate `quickstart.md`: with a scratch cue set in `screen/GameplayScreen.fs`'s `activate`, the Cue tab is docked in the right column beside the Inspector, the panel shows the tree with the running op first, a running op offers no control and says why while the sequence holding it offers its one addition, a queued node edits and undoes, and playing and returning shows what is left with an empty history. **Remove the scratch line afterwards.** *(The build and the suite ran green on 2026-09-30 — 378 passed, 0 failed; the walkthrough half needs a display and a hand, so it is left to the author.)*

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately. T004 depends on T001/T002 existing; T005 on T003; T007 on T004/T005; T006 (native) is independent of all of them.
- **Foundational (Phase 2)**: Depends on Setup — **BLOCKS all user stories**.
- **User Stories (Phases 3–5)**: All depend on Foundational. In a single-threaded effort they run in priority order (US1 → US2 → US3).
- **Polish (Phase 6)**: Depends on all desired stories being complete.

### User Story Dependencies

- **US1 (P1)**: after Foundational. No dependency on other stories. **MVP.**
- **US2 (P2)**: after Foundational; it hardens what US1 built (the ids and the boundary), so in practice after US1's control (T010–T011).
- **US3 (P3)**: after Foundational; it uses US1's `rows`/`executing` (T010) and adds the write path, so in practice after US1.

### ⚠️ Why US1–US3 Are Sequential Here

Each story is *independently testable* (that is what the story phases buy), but they are not *independently
parallelizable*: every story's implementation lands in `editor/CueTree.fs` / `editor/CueEditor.fs` and every
story's tests land in `test/CuePanelTests.fs`. Two tasks editing those files cannot run at once, so `[P]`
appears only on tasks that touch distinct files (the Setup scaffolding, the test `.fsproj`, the native shim,
and the three docs). Do not read the absence of `[P]` across stories as a missing decomposition.

### Within Each User Story

- Implementation before its tests; the tests assert the behaviour the same phase just added.
- The story's own checkpoint is a validation stop.

### Parallel Opportunities

- **Phase 1**: T001, T002, T003 create three different files and T006 touches the native shim, so they can go
  together; T005 (test `.fsproj`) can go with any of them. T004 waits on T001/T002, and T007 waits on
  T004/T005.
- **Phase 6**: T027, T028 and T029 amend three different documents and can go together.
- Nothing else: the control, the panel and the suite are each one file.

---

## Parallel Example: Setup

```bash
# T001, T002, T003 and T006 touch four different files:
Task: "Create editor/CueTree.fs as module CueTree"
Task: "Create editor/CueEditor.fs as module CueEditor"
Task: "Create test/CuePanelTests.fs as module CuePanelTests"
Task: "Dock the Cue window in native/cimgui_sdl3.cpp and rebuild libcimgui.so"
```

## Parallel Example: Documentation

```bash
# T027–T029 touch three different documents:
Task: "Amend docs/ROADMAP.md — phase 9's editor deliverable is complete"
Task: "Amend docs/PROJECT_STRUCTURE.md — CueEditor/CueTree are no longer planned"
Task: "Amend docs/EDITOR_DESIGN.md — D-010, D-013 and D-011's cue-root note"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup — including the **native rebuild**, which is the feature's one environmental step.
2. Complete Phase 2: Foundational (CRITICAL — blocks everything).
3. Complete Phase 3: US1 — the rows, the tree, the panel, the wiring, and their tests.
4. **STOP and VALIDATE**: `dotnet test` green for the rows; with a scratch cue in `GameplayScreen.activate`
   the panel shows the running cue, and playing and returning shows what is left.
5. This is the MVP: phase 9's cue becomes *visible*, which is the whole reason the panel was asked for.

### Incremental Delivery

1. Setup + Foundational → the modules exist, the panel is docked, the build is clean.
2. US1 → the running cue is visible → **MVP / demo**.
3. US2 → the control is parameterised and its ids cannot collide, so the later callers are free.
4. US3 → the author edits what is still to run, with the cue's own history.
5. Polish → the three docs match the code, the SC-003 inspection is done, `dotnet build`/`dotnet test` green,
   and `quickstart.md` walked.

### Notes

- Every task names the file it changes; tasks on `editor/CueTree.fs`, `editor/CueEditor.fs` or
  `test/CuePanelTests.fs` are sequential by construction.
- `editor/CueTree.fs` is compiled before `editor/CueEditor.fs` and both before `editor/Editor.fs` (T004). If the
  control starts naming `GameplayState`, an `EditorState` or a zone, that is the FR-006 boundary drifting, not a
  convenience — a trigger's cue needs it in phase 10.
- **Do not put the running op back into the sequence's deque.** The panel reads `state.Cue` as spec 046 holds
  it; `Sequence(current, rest)` is that model's decision, and the panel neither changes it nor keeps a view of
  its own beside it.
- **Do not let the lock become a guard.** `drawEditor` draws nothing for a locked node rather than drawing
  controls and refusing their writes: FR-011 says the panel "MUST NOT offer a way to change it", and a withheld
  control is the difference between a design and a bug waiting to be filed.
- **Nothing edits a locked node, and nothing edits the cue the run has already passed**: the panel is a writer
  of *values* into `state.Cue`, and the ops that have run are not in the value any more.
- **Ctrl+Z is still the zone stack's.** The cue's Undo/Redo are the panel's own controls; do not add the cue to
  `Editor.handleShortcuts`.

---

## Phase 7: Convergence

**Purpose**: the two assertions the spec's success criteria promise that the suite does not yet make. Both were
found by `/speckit-converge` against `spec.md`; in each case the *code* satisfies the criterion and the *test*
does not, so these are test gaps rather than implementation ones — no application code changes.

- [X] T032 [US1] Extend the row assertions in `test/CuePanelTests.fs` to the whole label vocabulary and to a nested container: `Parallel`, `Fork`, `Zone: classroom` and `Move camera` are the four kinds whose row labels nothing asserts today, and a `Sequence` inside a `Sequence` (indented one deeper again) is unasserted too — so FR-004's "shown as the op it came from" and SC-004's "the control's labels and child structure are asserted headlessly" are met only in part (partial, per FR-004 and SC-004)
- [X] T033 [US3] Assert the panel's own write path end to end in `test/CuePanelTests.fs`: drive `CueEditor.commit` against a headless `GameplayState` (the fixture `test/CueTests.fs` builds), assert that one edit changes `state.Cue` exactly once and that `CueEditor.undo` restores it exactly, with `CueEditor.redo`, `CueEditor.canUndo` and `CueEditor.canRedo` tracking — the pieces (`withAt`, `CueHistory`) are asserted, but the glue that writes the screen's cue is not, which is what SC-007 and SC-008 are about (partial, per SC-007 and SC-008)
