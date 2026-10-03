---

description: "Task list for the Trigger Cue feature"
---

# Tasks: Trigger Cue

**Input**: Design documents from `specs/058-trigger-cue/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/trigger-cue.md, quickstart.md

**Tests**: Included — the spec (`FR-006`, `SC-006`) requires the fire and the control's write path to be asserted
headlessly, and the project's workflow runs `dotnet test`.

**Organization**: Tasks are grouped by user story. US1 and US2 are both P1 (the fire and the authoring surface); US3 is
P2. All tasks follow the checklist format `- [ ] [ID] [P?] [Story?] description with file path`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3

## Path Conventions

Single project at the repository root: `world/`, `state/`, `editor/`, `screen/`, `test/`, `fsharp-game.fsproj`.

---

## Phase 1: Setup

**Purpose**: Confirm a green baseline before any change.

- [x] T001 Run `dotnet build` and `dotnet test` at the repository root and confirm both are clean before starting

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The compile-order moves and the trigger's cue field. No user story can compile without these.

**⚠️ CRITICAL**: Nothing in Phases 3+ compiles until this phase is done.

- [x] T002 Move the `world/Cue.fs` compile entry in `fsharp-game.fsproj` to immediately before `world/Entity.fs` (it must stay after `world/Advent.fs`/`world/Dialogue.fs`, which it already follows)
- [x] T003 Move the `editor/CueTree.fs` and `editor/CueEditor.fs` compile entries in `fsharp-game.fsproj` to immediately after `editor/Selection.fs` and before `editor/Inspector.fs`
- [x] T004 Relocate `forkInto` from `world/CueEngine.fs` to `world/Cue.fs` unchanged, and move the header notes that name it in both files
- [x] T005 Add `Cue : Cue.Cue` to `TriggerState` in `world/Entity.fs`, extending the comment to say it is the authored cue forked when the trigger fires
- [x] T006 Add `Cue = Cue.Fin` to `defaultTrigger` in `world/Zone.fs`
- [x] T007 Run `dotnet build` and `dotnet test` and confirm the move and the field change are behaviour-neutral (existing tests pass)

**Checkpoint**: The tree compiles with a cue on every trigger; `forkInto` is callable from `world/Systems.fs`.

---

## Phase 3: User Story 1 - A trigger fires its cue when the player enters (Priority: P1) 🎯 MVP

**Goal**: A non-interactable trigger forks its authored cue into the screen's running cue on the rising edge of
"player inside".

**Independent Test**: Build a zone with a player and a trigger whose cue is `Wait 1`; step `updateTriggers` with the
player outside then inside and assert the screen cue became the trigger's cue, that remaining inside does not fork
again, and that leaving and re-entering forks again — no device.

### Implementation for User Story 1

- [x] T008 [US1] In `world/Systems.fs`, add `forkTriggerCue (zone) (ref : EntityRef voption) (cue : byref<Cue>)` (resolve, skip `Fin`, `forkInto`) and change `updateTriggers` to take `(cue : byref<Cue>)`, firing an enabled non-interactable trigger's cue on the rising edge only
- [x] T009 [P] [US1] In `screen/GameplayScreen.fs`, pass `&state.Cue` as the new third argument to `Systems.updateTriggers`

### Tests for User Story 1

- [x] T010 [US1] In `test/SystemsTests.fs`, update the `tracked`/`inside` helpers for the new arity and add cases: entry forks the trigger's cue into `Fin`; standing inside does not re-fork; leaving and re-entering forks again; a `Fin` cue, a disabled trigger, and no live player fork nothing; an interactable trigger is not forked on entry
- [x] T011 [US1] Run `dotnet build` and `dotnet test`; both clean

**Checkpoint**: User Story 1 is fully functional and independently testable, headlessly.

---

## Phase 4: User Story 2 - A trigger's cue is edited inline in the Inspector (Priority: P1)

**Goal**: The selected trigger's cue is drawn in the Inspector through the shared control, with its own selection and
text field, committing each accepted edit as one `TriggerSet` step.

**Independent Test**: Assert that an edit staged through the inline editor's commit yields one `TriggerSet` step whose
`After.Cue` is the edit and whose undo restores `Before.Cue`, and that the trigger editor's transient fields are
distinct from the Cue panel's and reset when the selected trigger changes — no device.

### Implementation for User Story 2

- [x] T012 [US2] Add the trigger inline-editor fields to `state/EditorState.fs` (`TriggerCueSelection`, `TriggerCueRef`, `TriggerCueTextBuffer`, `TriggerCueTextPath`, `TriggerCueEditing`) and initialise them in `create`
- [x] T013 [US2] In `editor/CueEditor.fs`, parameterise the per-node text and waypoint-combo helpers over their transient field state and a staged commit, and add `drawTrigger (shared) (state) (ref)` — reset the fields when `TriggerCueRef` changes, draw the tree and node editor with the `"triggercue"` id prefix, and commit through `Selection.updateTriggerRef`
- [x] T014 [P] [US2] In `editor/Inspector.fs`, call `CueEditor.drawTrigger shared &state ref` from the trigger section

### Tests for User Story 2

- [x] T015 [US2] In `test/CuePanelTests.fs`, add cases: a trigger cue edit committed through the trigger path becomes one `TriggerSet` step (undo restores the before-cue); the trigger inline-editor fields are distinct from the Cue panel's; `TriggerCueRef` differing resets the selection and text path
- [x] T016 [US2] Run `dotnet build` and `dotnet test`; both clean

**Checkpoint**: User Stories 1 and 2 both work independently: a cue can be authored inline and fires.

---

## Phase 5: User Story 3 - An interactable trigger fires on interaction (Priority: P2)

**Goal**: Pressing the interact key (`E`) while playing forks the tracked interactable trigger's cue; entry alone does
not.

**Independent Test**: Assert `Systems.forkTriggerCue` forks the tracked trigger's cue, and that `ValueNone`, a stale
ref and a `Fin` cue each leave the cue unchanged — no device.

### Implementation for User Story 3

- [x] T017 [US3] In `screen/GameplayScreen.fs`, after `updateTriggers` and before `CueEngine.tick`, fork the tracked trigger's cue on `Input.isPressed Input.Key.E`

### Tests for User Story 3

- [x] T018 [US3] In `test/SystemsTests.fs`, add `forkTriggerCue` cases: `ValueSome` of a trigger with a cue forks it; `ValueSome` of an empty-cue trigger, a stale ref, and `ValueNone` leave the cue unchanged
- [x] T019 [US3] Run `dotnet build` and `dotnet test`; both clean

**Checkpoint**: All three stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and end-to-end validation.

- [x] T020 [P] Update `docs/ROADMAP.md` phase 10 to mark trigger→cue wiring complete, `docs/ARCHITECTURE.md`'s scenario/cue section for the trigger's owned cue, `docs/EDITOR_DESIGN.md` (D-026, the inline trigger cue), and `docs/PROJECT_STRUCTURE.md`'s compile order and module map
- [ ] T021 Run the `specs/058-trigger-cue/quickstart.md` scenarios — the headless suite passed (461 tests, all green); the manual editor walkthrough is the user's to run

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (T001)**: No dependencies.
- **Foundational (T002–T007)**: Depends on Setup. BLOCKS all stories.
- **US1 (T008–T011)** and **US3 (T017–T019)** both touch `world/Systems.fs` and `screen/GameplayScreen.fs`: US3 must
  follow US1 (a helper US3 calls is added in T008).
- **US2 (T012–T016)**: Depends on Foundational (the `CueTree`/`CueEditor` move). Can run in parallel with US1/US3 once
  Foundational is done, except that T013 depends on T012.
- **Polish (T020–T021)**: Depends on all stories.

### Within Each User Story

- Implementation before its tests (F# will not compile the new test arity before the signature exists).
- `world/Systems.fs` (T008) before `screen/GameplayScreen.fs` (T009, T017).
- `state/EditorState.fs` (T012) before `editor/CueEditor.fs` (T013) before `editor/Inspector.fs` (T014).

### Parallel Opportunities

- **US1**: T009 [P] after T008; T010 after T008 (different files).
- **US2**: T014 [P] after T013; T015 after T013.
- **US3**: T018 after T017 (different files).
- **Polish**: T020 [P] can run while T021 is prepared.

---

## Parallel Example: User Story 1

```text
# After T008 lands, these touch different files:
Task: "Pass &state.Cue to Systems.updateTriggers in screen/GameplayScreen.fs"
Task: "Update test/SystemsTests.fs arity and add entry-fire cases"
```

## Parallel Example: User Story 2

```text
# After T013 lands:
Task: "Call CueEditor.drawTrigger from the trigger section in editor/Inspector.fs"
Task: "Add inline trigger-cue cases to test/CuePanelTests.fs"
```

---

## Implementation Strategy

### MVP First

1. Phase 1 Setup → Phase 2 Foundational (T001–T007).
2. Phase 3 US1 (T008–T011): **STOP and validate** the entry fire headlessly.
3. Phase 4 US2 (T012–T016): add the inline editor; both P1 stories are the MVP.

### Incremental Delivery

1. Foundation → skeleton with a cue field, no behaviour change.
2. US1 → the fire works (and is testable from code).
3. US2 → the cue is authorable inline.
4. US3 → interactable triggers fire on `E`.
5. Polish → docs and quickstart.

---

## Notes

- `[P]` tasks touch different files and depend only on already-complete tasks.
- No new command is added: a trigger's cue rides `TriggerSet` (research D7).
- The idle path of `updateTriggers` must stay allocation-free; a fork allocates only on a fire (Constitution V).
- Commit after each logical group; keep `dotnet build` clean at every checkpoint.
