---
description: "Task list for Dialogue Endings"
---

# Tasks: Dialogue Endings

**Input**: Design documents from `specs/097-dialogue-endings/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/dialogue-endings.md`

**Tests**: Included — the feature is headless-testable and the project's constitution requires the
cue/state/panel logic to be asserted without a device (N-V).

**Organization**: Tasks are grouped by user story so each can be implemented and validated
independently. US1 settles the ending rules; US2 adds the cue display/wait/close shapes; US3 exposes
the controls in the editor.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: `[US1]`, `[US2]`, `[US3]`
- Every task names the exact file path.

## Path Conventions

Single F# project: `world/`, `state/`, `screen/`, `editor/`, `test/` at the repository root (see
`docs/PROJECT_STRUCTURE.md`). No `.fsproj` compile-order change is expected; `world/Dialogue.fs`
stays before `world/Cue.fs`, and `state/CueEngine.fs` stays after `state/GameplayState.fs`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: establish the baseline and confirm the compile-order assumption.

- [X] T001 Run `dotnet build` and `dotnet test` to record the clean baseline; confirm `world/Dialogue.fs` is before `world/Cue.fs` and `state/CueEngine.fs` is after `state/GameplayState.fs` in `Sugoi.fsproj`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the shared dialogue model every story reads.

**⚠️ CRITICAL**: US1, US2 and US3 all depend on this phase.

- [X] T002 Add `Dialogue.Ending` (`ByCue`, `ByPlayer`, `ByTimeout` — data-free), the `Endings : Set<Ending>` and `Timeout : float voption` fields, `ShownAt`, the `{ ByPlayer }` default on `Dialogue.empty`, and the pure helpers `cueCloses`, `playerCloses`, `timed`, `withCue`, `withPlayer`, `withTimeout` and `summary` in `world/Dialogue.fs`.
- [X] T003 Update the existing `Dialogue` record literals in `test/DialogueTests.fs` (at minimum the `shown` fixture) with `Endings = Set.singleton Dialogue.ByPlayer`, `Timeout = ValueNone` and `ShownAt = 0.0`, so the suite compiles against the new fields.

**Checkpoint**: the dialogue value carries a set of three ending kinds and a separate timeout voption; the suite compiles.

---

## Phase 3: User Story 1 — A line ends the way its author chose (Priority: P1) 🎯

**Goal**: the runtime tick ends a shown line only under its authored runtime conditions: `ByPlayer`
on the dismiss edge, `ByTimeout` after the line's duration, the first of the two when both are present,
and never for `{ ByCue }` or the empty set.

**Independent Test**: set a `Dialogue` value directly with each ending and combination, call
`Systems.updateDialogue` with chosen `now`/`pressed`/`textFocused` values, and assert `Shown`; no cue,
window, GPU or device is needed.

### Tests for User Story 1

- [X] T004 [US1] Replace the `dismissDialogue` cases in `test/DialogueTests.fs` with `updateDialogue` tests: `ByPlayer` press closes; `{ ByCue }` and the empty set ignore press/time; `ByTimeout` closes at/after its duration; `ByPlayer`+`ByTimeout` closes on either; `ByCue`+`ByPlayer` accepts either the press or a cue hide; `ByCue`+`ByTimeout` times out and a cue hide also clears it; all three accept the press, the timeout or a cue hide; the focus gate suppresses only `ByPlayer`; a non-positive timeout closes on the next play tick; retiming uses the original `ShownAt`; a `ByTimeout` with `Timeout = ValueNone` never times out; a portrait-miss line still closes under its ending.
- [X] T005 [US1] Add helper tests to `test/DialogueTests.fs` for `cueCloses`, `playerCloses`, `timed`, `withCue`, `withPlayer`, `withTimeout`, `summary` (including `no closer` and every combination label) and the `Dialogue.empty` default.

### Implementation for User Story 1

- [X] T006 [US1] Replace `Systems.dismissDialogue` with `Systems.updateDialogue` in `world/Systems.fs`: the `pressed`/`textFocused`/`now` signature, `Set.contains ByPlayer` and the `ByTimeout`/`Timeout` voption check, and no per-call allocation.
- [X] T007 [US1] Update `screen/GameplayScreen.fs`: inside the play block, compute `playTime = state.PlayTime + float frame.Dt`, pass it to `Systems.updateDialogue` with `Space` and `ImGui.wantTextInput()`, and assign it to `state.PlayTime` before `CueEngine.tick` so the dialogue and the cue share one clock.
- [X] T008 [US1] Update the comments in `world/Dialogue.fs`, `world/Systems.fs` and `screen/GameplayScreen.fs` to state the ending rules (including `ByCue` as the cue's action and the timeout's play-time base).
- [X] T009 [US1] Run `dotnet build` and `dotnet test --filter "FullyQualifiedName~DialogueTests"` against `test/DialogueTests.fs`; validate US1 independently.

**Checkpoint**: US1 works with a line set directly; the Dialogue panel's `Shown`/`ShownAt` wiring is
not required yet.

---

## Phase 4: User Story 2 — A cue waits for a line, or moves on (Priority: P1)

**Goal**: the cue can write a line and continue (`Exposit`, `ExpositWith`) or hold until the line is
gone (`ExpositWait`/`ExpositWaitState`), and can close it explicitly (`CloseExposition`). The timer remains
screen state, so a non-waiting timed line clears even after the cue is `Fin`.

**Independent Test**: build cues with the new cases in `test/CueTests.fs`, step them with
`CueEngine.tick`, clear the line with `Systems.updateDialogue` or `CloseExposition`, and assert the cue's
shape and `Dialogue.Shown`. No editor is needed.

### Tests for User Story 2

- [X] T010 [US2] Update `test/CueTests.fs`: extend `describe` for `ExpositWith`, `ExpositWait`, `ExpositWaitState` and `CloseExposition`; add tests for the non-waiting writes (`{ ByCue }`, player, timeout, combinations), the waiting leaf/state, hiding (including `CloseExposition` on a `{ ByPlayer }` line and a `{ ByTimeout }` line), `{ ByCue }` waiting, replacement-while-waiting, a parallel hide advanced before and after the waiting branch (same tick vs next tick), and timer independence after `Fin`.
- [X] T011 [US2] Add `test/CueTests.fs` allocation tests: an idle `ExpositWaitState` tick allocates 0 bytes, and `Systems.updateDialogue` allocates 0 bytes, including a call with `Shown = false` and one with `{ ByCue }`; follow the existing `GC.GetAllocatedBytesForCurrentThread` pattern.
- [X] T012 [P] [US2] Add `test/ProjectTests.fs` round-trip tests: the old two-argument `Exposit` still decodes as `{ ByCue }`, `ValueNone`; `ExpositWith`, `ExpositWait`, `ExpositWaitState` and `CloseExposition` encode/decode with the empty set, every single condition and every combination, including `Set<Ending>` and `ValueSome seconds`.

### Implementation for User Story 2

- [X] T013 [US2] Add the cue cases and comments in `world/Cue.fs`: keep `Exposit` two-argument (it writes `{ ByCue }`, `ValueNone`), add `ExpositWith`, `ExpositWait`, `ExpositWaitState` (each carrying `Set<Dialogue.Ending>` and `float voption`) and `CloseExposition`, and update the no-progress/state-shape list.
- [X] T014 [US2] Update `state/CueEngine.fs`: make `exposit` take `now`, the ending set and the timeout voption and write `Endings`, `Timeout` and `ShownAt`; add the `Exposit`, `ExpositWith`, `ExpositWait`, `ExpositWaitState` and `CloseExposition` arms to `advance`.
- [X] T015 [P] [US2] Add the bare `CloseExposition` atom after the final `Wait 4` in the intro cutscene in `project.manifest`, so its `{ ByCue }` final line clears.
- [X] T016 [US2] Update the `world/Cue.fs` and `state/CueEngine.fs` header comments to describe the display/wait/close shapes and that waiting is a cue run state, not a dialogue field.
- [X] T017 [US2] Run `dotnet build` and `dotnet test --filter "FullyQualifiedName~CueTests"` plus `--filter "FullyQualifiedName~ProjectTests"` against `test/CueTests.fs` and `test/ProjectTests.fs`.

**Checkpoint**: US1 and US2 work independently of the editor; the persisted file round-trips and the
intro cutscene clears its last line.

---

## Phase 5: User Story 3 — The editor authors the ending everywhere (Priority: P2)

**Goal**: the Cue panel, the trigger's inline cue editor and the Dialogue panel expose the cue, player
and timeout conditions; the cue tree labels the ending set; a running waiting op is read-only; the
Dialogue panel stays a direct-write surface.

**Independent Test**: assert the pure cue-tree functions (`executing`, `label`, `defaultOfKind`,
`textOf`/`withText`, `withEndings`, `children`) in `test/CuePanelTests.fs`, including that an ending
edit is one `SetCue` step; exercise the controls manually in the Cue panel, the trigger inline editor
and the Dialogue panel.

### Tests for User Story 3

- [X] T018 [US3] Update `test/CuePanelTests.fs`: `describe` for the new cases; `executing` true only for `ExpositWaitState`; row labels distinguish `[no closer]`, `[cue]`, `[player]`, `[3.5s]`, `[cue or player]`, `[player or 3.5s]`, `[cue or player or 3.5s]`; `defaultOfKind` for `Exposit` (`{ ByCue }`, `ValueNone`), `Exposit (wait)` (`{ ByPlayer }`, `ValueNone`) and `Close exposition`; `children` empty for the new leaves.
- [X] T019 [US3] Add `test/CuePanelTests.fs` coverage for `withEndings` (including normalising `ExpositWith(..., { ByCue }, ValueNone)` to `Exposit`), `withText`, `withExpositKind`, the `Dialogue.summary` labels, and the one-step commit: `CueTree.withEndings` + `CueEditor.commit` records exactly one `SetCue`, and re-selecting the same set/timeout records none.

### Implementation for User Story 3

- [X] T020 [US3] Update `editor/CueTree.fs`: add `endingControls` (Cue closes (intended) / Player can close / Closes after + seconds, returning `Set<Ending> * float voption`) and `withEndings`; label the cue control/tooltip so it records intent and does not read as a permission gate; update `kinds`, `kindName`, `label`, `executing`, `children`, `textOf`, `withText`, `withExpositKind`, `defaultOfKind` and `drawEditor`; keep `ExpositWaitState` out of `textOf` and out of the editable path; default a newly enabled timeout to 3.0 seconds.
- [X] T021 [P] [US3] Verify the generic text field in `editor/CueEditor.fs` handles `ExpositWith`/`ExpositWait` through the widened `textOf`/`withText`; update the `drawNodeText` comment to state `ExpositWaitState` is excluded. No new control code is expected.
- [X] T022 [P] [US3] Update `editor/DialoguePanel.fs`: draw the shared ending controls via `CueTree.endingControls "dialogue"`, label the cue control as intent (or provide the shared tooltip), default a newly enabled timeout to 3.0 seconds, set `ShownAt <- state.PlayTime` when `Shown` turns on, write the set and timeout, and keep the panel direct-write (no command, no persistence).
- [X] T023 [US3] Update the `editor/CueTree.fs` top comment (it now names `Dialogue` and has the waiting run state) and the `DialoguePanel` comment for the new controls.
- [ ] T024 [US3] Run `dotnet build` and `dotnet test --filter "FullyQualifiedName~CuePanelTests"` against `test/CuePanelTests.fs`; then exercise the manual Cue/Dialogue-panel scenarios in `specs/097-dialogue-endings/quickstart.md`.

**Checkpoint**: all three user stories are independently functional; the author can set the ending on
every surface.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: full-suite, allocation and persistence validation.

- [X] T025 [P] Run `dotnet build` (warnings-clean) and the full `dotnet test`; fix any regression in the existing rendering/gameplay/editor assertions, and record the result against `specs/097-dialogue-endings/quickstart.md`.
- [ ] T026 [P] Run the quickstart's automated suites and manual walkthrough in `specs/097-dialogue-endings/quickstart.md`; confirm the intro cutscene's cue-managed (empty-set) lines ignore `Space` and the final `CloseExposition` clears the line.
- [X] T027 Confirm the allocation assertions (idle `ExpositWaitState` and `updateDialogue` at 0 bytes) in `test/CueTests.fs`, the `Set<Ending>`/`float voption` round-trip in `test/ProjectTests.fs`, and that `project.manifest` decodes with the new `CloseExposition` and the old `Exposit` forms.
- [X] T028 Re-read `specs/097-dialogue-endings/spec.md` against the implementation; update `specs/097-dialogue-endings/checklists/requirements.md` only if a checklist state actually changed.

---

## Phase 7: Remediation — remove `ByCue` (model correction)

**Purpose**: the ending set holds only runtime conditions (`ByPlayer`, `ByTimeout`). Closing from a cue
is a universal capability, so the empty set means cue-managed. This phase supersedes the `ByCue` parts
of Phases 2–6; those tasks remain as the record of the first pass.

- [X] T029 Update `specs/097-dialogue-endings/spec.md`: remove `ByCue` from the vocabulary, semantics, user stories, edge cases, requirements, entities, success criteria and assumptions; define the empty set as cue-managed and add the universal cue-close guarantee.
- [X] T030 Update `specs/097-dialogue-endings/plan.md`, `research.md`, `data-model.md`, `contracts/dialogue-endings.md`, `quickstart.md` and `checklists/requirements.md` to the two-condition model.
- [X] T031 Remove `ByCue`, `cueCloses` and `withCue` from `world/Dialogue.fs`; make `summary` return `"cue"` for the empty set and drop the `cue` part from non-empty summaries; update the module comment.
- [X] T032 Update `state/CueEngine.fs`: `Exposit` writes `Set.empty` (not `{ ByCue }`), and the `exposit`/header comments state the universal cue close.
- [X] T033 Update the dialogue-op comment in `world/Cue.fs`: `Exposit` writes the empty set; describe the empty set as cue-managed.
- [X] T034 Update the dialogue comments in `world/Systems.fs` and `screen/GameplayScreen.fs` to remove `ByCue` and describe only the two runtime conditions.
- [X] T035 Update `editor/CueTree.fs`: remove the `Cue closes (intended)` checkbox and tooltip from `endingControls`; make `withEndings` normalise the empty set with `ValueNone` to `Exposit`; label `Exposit` via `Dialogue.summary Set.empty ValueNone`; read `Set.empty` for an `Exposit` in `drawEditor`; update comments.
- [X] T036 Update the comments in `editor/DialoguePanel.fs` and `editor/CueEditor.fs` for the two condition controls and unconditional cue closing.
- [X] T037 Update `test/DialogueTests.fs`: remove `ByCue`/`cueCloses`/`withCue` cases, test the empty set as never ending on input or time, and update the summary expectations (`cue`, `player`, `3.5s`, `player or 3.5s`).
- [X] T038 Update `test/CueTests.fs`: `Exposit` writes the empty set; empty-set waiting/close/replacement/parallel cases; remove `ByCue` from the describe labels and allocation fixtures.
- [X] T039 Update `test/CuePanelTests.fs`: labels `[cue]`, `[player]`, `[3.5s]`, `[player or 3.5s]`; `withEndings` normalises the empty set; the commit case uses `{ ByPlayer }`; remove `ByCue` expectations.
- [X] T040 Update `test/ProjectTests.fs`: round-trip every set state (empty, `ByPlayer`, `ByTimeout`, both) and the waiting/close cases; the old `Exposit` decodes as the empty set.
- [X] T041 Run `dotnet build` and the focused suites (`DialogueTests`, `CueTests`, `CuePanelTests`, `ProjectTests`).
- [X] T042 Run the full suite and decode `project.manifest`; confirm no `ByCue` atom remains and the intro cutscene still ends with `CloseExposition`.
- [X] T043 Grep the code, tests and normative feature artifacts for `ByCue`, `cueCloses`, `withCue` and `no closer`; only historical records (the spec's Input quote, the completed task log and the research rationale) retain the removed name.

---

## Phase 8: Rename the close op to `CloseExposition`

**Purpose**: the display op is called `Exposit`; its counterpart is named for the thing it closes.
`HideDialogue` is renamed `CloseExposition`, and the editor label becomes "Close exposition". The
feature is uncommitted, so no decode alias or migration is needed.

- [X] T044 [P] Rename the op and the prose in `spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/dialogue-endings.md`, `quickstart.md`, `checklists/requirements.md` and this task list: `HideDialogue` → `CloseExposition`, "hide op" → "close op", "Hide dialogue" → "Close exposition".
- [X] T045 [P] Rename the case in `world/Cue.fs`, the `advance` arm in `state/CueEngine.fs`, and the kind name/label/default/case groups in `editor/CueTree.fs` (`CloseExposition`, "Close exposition"); update the comments in `world/Systems.fs`, `editor/DialoguePanel.fs` and `world/Cue.fs` to "close".
- [X] T046 [P] Rename the case in `test/CueTests.fs`, `test/CuePanelTests.fs` and `test/ProjectTests.fs` (including `describe`, the label and `defaultOfKind`), and the atom in `project.manifest`.
- [X] T047 Run `dotnet build`, the focused dialogue/cue/panel/persistence suites, the full suite, and decode `project.manifest`; grep the code, tests, manifest and normative artifacts for `HideDialogue`/"Hide dialogue" and confirm none remain (this rename record excepted).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies; run first for the baseline.
- **Foundational (Phase 2)**: depends on Setup; blocks every user story.
- **US1 (Phase 3)**: depends on Foundational; the runtime timing/input rules.
- **US2 (Phase 4)**: depends on Foundational and on US1's `Systems.updateDialogue` for its player/timeout
  resume tests; the cue cases themselves are independent of US3.
- **US3 (Phase 5)**: depends on Foundational, US1 and US2 (it authors and labels the ending values).
- **Polish (Phase 6)**: depends on all desired stories.
- **Remediation (Phase 7)**: depends on Phases 2–6; supersedes their `ByCue` parts and must be the last
  code change before validation.
- **Close-op rename (Phase 8)**: depends on Phase 7; a mechanical rename that must precede the final
  validation.

### User Story Dependencies

- **US1**: can start after Phase 2; does not require any cue case.
- **US2**: can start after Phase 2 for `Cue.fs`/`CueEngine.fs`, but its end-to-end wait tests use
  `updateDialogue`; complete T006/T009 first.
- **US3**: uses the helpers and cue cases from US1/US2; complete Phase 3 and the `world/Cue.fs` part
  of Phase 4 first.

### Within Each User Story

- Tests first (T004/T005, T010/T011/T012, T018/T019), then implementation.
- The dialogue model (T002) precedes every runtime path.
- The cue cases (T013) precede the engine (T014); the engine precedes the editor's labels (T020).
- Run the story's focused suite at its checkpoint before moving on.

### Parallel Opportunities

- T012 (`test/ProjectTests.fs`) can run alongside T010/T011 (`test/CueTests.fs`).
- T015 (`project.manifest`) touches a different file from T013/T014 and can be done once `CloseExposition`
  is agreed.
- T021 (`editor/CueEditor.fs`) and T022 (`editor/DialoguePanel.fs`) touch different files and can run
  after T020 (`editor/CueTree.fs`).
- T025/T026/T027 are independent validation commands/files.

---

## Parallel Example: User Story 2

```text
Task: "T010 update test/CueTests.fs describe + cue behaviour"
Task: "T011 add test/CueTests.fs allocation tests"
Task: "T012 add test/ProjectTests.fs round-trip tests"
```

`T010` and `T011` share `test/CueTests.fs`, so do them in one editing pass; `T012` is a separate file
and can proceed in parallel.

## Parallel Example: User Story 3

```text
Task: "T020 update editor/CueTree.fs"
Task: "T021 verify editor/CueEditor.fs text handling"
Task: "T022 update editor/DialoguePanel.fs"
```

`T021`/`T022` depend on `T020`'s shared helpers and can then be done together.

---

## Implementation Strategy

### MVP First (US1)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1) and validate `DialogueTests`.
3. The ending rules are demonstrable by setting `Dialogue.Endings`/`Dialogue.Timeout` directly (the
   Dialogue panel wiring arrives in US3).

### Incremental Delivery

1. US1 → the runtime ending rules.
2. US2 → cue-managed, waiting, skippable and timed cutscenes; update the intro cutscene.
3. US3 → author the endings in the Cue panel, trigger inline editor and Dialogue panel.
4. Polish → full suite, allocation and persistence.

### Notes

- `Ending` is data-free and holds only the runtime conditions (`ByPlayer`, `ByTimeout`); the timeout is
  `Dialogue.Timeout`. Do not put the duration back into the union.
- The cue's ability to close or replace a line is universal. The empty set means cue-managed, not
  "unclosable"; do not reintroduce a `ByCue` member.
- No `.fsproj` change is expected; if a new module is added despite this plan, insert it at the correct
  compile point per `docs/PROJECT_STRUCTURE.md` and explain the deviation.
- `Exposit` stays two-argument and writes the empty set, `ValueNone`; do not add a project-format version
  bump or a symbolic migration.
- A waiting line with the empty set is allowed silently; do not add a warning or a disable.
- Keep the per-frame dialogue tick allocation-free; the ending set and timeout are authored data, not
  frame data.

---

## Phase 9: Convergence

- [X] T048 Add a short user-facing statement in `editor/CueTree.fs` `endingControls` that a cue can always close or replace the line, so the universal capability is presented rather than only described in a code comment per FR-012 (partial).
- [X] T049 Extend the `CloseExposition` test in `test/CueTests.fs` to include an empty-set line, so the close op is asserted to clear every set state per FR-002, FR-010 and SC-009 (partial).
- [X] T050 Add a two-line `ExpositWait` sequence test in `test/CueTests.fs` that asserts the second line appears on the tick the first ends for `{ ByPlayer }`, `{ ByTimeout }` and both conditions per US2/AC1–AC3 and FR-009 (partial).
