---

description: "Task list for Commands Carry the Zone They Act On"
---

# Tasks: Commands Carry the Zone They Act On

**Input**: Design documents from `/specs/077-commands-carry-zone/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/command-resolution.md, quickstart.md

**Tests**: Included — the command and history tests are already headless (`GameplayStateTests`, `SelectionTests`,
`SpawningTests`, `CuePanelTests`, `AdventTests`), so the new guarantees are asserted with no device. FR-013 makes the
test work in scope.

**Organization**: Tasks are grouped by user story. The **model change** — the command carries a place and the
state-level runner resolves it — is foundational: every story builds on it, and the compiler drives it (adding a
field breaks every construction site and pattern match at once). US1 (P1) is a step acting on its recorded place;
US2 (P2) is a gesture spanning places; US3 (P3) is the docs and the single-place guarantee.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 (setup, foundational and polish carry none)
- Exact file paths are in every description.

## Path Conventions

- Single F# project at the repository root; sources in `world/`, `state/`, `editor/`, tests in `test/`, docs in `docs/`.

---

## Phase 1: Setup

**Purpose**: Establish a green baseline.

- [X] T001 Run `dotnet build` (warning-free) and `dotnet test` (green) at the repository root and record the
  baseline. Confirm `fsharp-game.fsproj` needs **no** edit: this feature adds no file and removes none, and no
  compile-order changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The command carries its place and the runner resolves it — the shape every story builds on.

**⚠️ CRITICAL**: No user story can be validated until this phase is complete.

- [X] T002 `state/Command.fs`: add `Place` as the first field of every zone-content case — `Transform`, `Rename`,
  `ActorCreate/Delete`, `PropCreate/Delete`, `PartCreate/Delete`, `WaypointCreate/Delete`, `TriggerCreate/Delete`,
  `Link`, `TriggerSet`, `LinkSpawn`, `SetEnvironment`, `SetLight`, `SetPlayer` — leaving `Batch`, `ZoneMove`,
  `ZoneAdd`, `ZoneDelete`, `SetCue`, `Advent` and `SetScenario` unchanged (data-model.md has the exact shapes). Add
  `let placeOf (cmd: Command) : Place option` (`Some` for a content command, `None` for a state-level step or a
  `Batch`). The state-level `invalidOp` group in `apply`/`undo` is unchanged.
- [X] T003 `state/History.fs`: resolve each content command's place to its zone in `apply` and `undo` — for the
  default arm, `match Command.placeOf cmd with Some place -> (match state.Zones.TryGetValue place with true, zone ->
  Command.apply zone … | false, _ -> failwithf "…absent place…") | None -> failwith "…"`. Keep the state-level cases
  and the per-sub-command `Batch` recursion as they are. (depends on T002)
- [X] T004 `state/Command.fs`: in `UndoStack.push`, coalesce consecutive `Transform`s only when they share **both**
  place and ref (`Transform (pa, a, before, _)` + `Transform (pb, b, _, after)` → `Transform (pa, a, before, after)`
  only when `a = b && pa = pb`). (depends on T002)
- [X] T005 `state/History.fs`: `ZoneDelete`'s **undo** rebuilds without switching — create the zone, `Zone.occupyZone`
  the contents into it, then `Zone.deactivatePhysics` to detach the bodies it built (the zone is not active). No
  `switchZone` call; `state.ActiveZone` is untouched (FR-014).
- [X] T006 [P] `editor/Selection.fs`: name `state.ActiveZone` in every command it builds — `ActorDelete`/`PropDelete`/
  `PartDelete`/`WaypointDelete`/`TriggerDelete` in `deleteRef`, and `Rename`, `Link`, `TriggerSet`, `SetLight`,
  `SetPlayer`. The direct `apply zone …` calls keep passing the active zone (the one the command names).
  (depends on T002)
- [X] T007 [P] `editor/Spawning.fs`: name the active place in every command — the `*Create` in `spawnActor`/`spawnProp`/
  `spawnWaypoint`/`spawnTrigger`/`duplicate`, `LinkSpawn` in `spawnLinkedWaypoint`, and every member of
  `instantiateZone`'s `Batch`. (depends on T002)
- [X] T008 [P] `editor/Editor.fs`: name `state.ActiveZone` in the `Transform` commands built by `finishDrag` and
  `updateNudge`. (depends on T002)
- [X] T009 [P] `editor/AssetBrowser.fs`: name the active place in the `SetEnvironment` step it pushes.
  (depends on T002)
- [X] T010 `test/`: update every `Command` construction and pattern match to the new arity — supply the active place
  in constructions (`Command.Transform (place, ref, …)`, `SetLight (place, …)`, `SetPlayer (place, …)`, `ZoneAdd`/
  `ZoneDelete`/`ZoneMove`/`SetCue` unchanged) and add the place to matches (`[ Rename (_, r, before, after) ]`,
  `[ TriggerSet (_, r, before, after) ]`, …). Cover `test/GameplayStateTests.fs`, `test/SelectionTests.fs`,
  `test/SpawningTests.fs`, `test/CuePanelTests.fs`, `test/AdventTests.fs` and any other that names a command.
  (depends on T002)
- [X] T011 Checkpoint: `dotnet build fsharp-game.fsproj` warning-free and `dotnet test` green — behaviour is unchanged
  by the model change alone. (depends on T002–T010)

**Checkpoint**: the command layer is place-carrying and the tree is green; the stories can be validated.

---

## Phase 3: User Story 1 - A step acts on the place it was recorded in (Priority: P1) 🎯 MVP

**Goal**: an edit recorded in one place undoes in that place, whatever place is active, without disturbing the view.

**Independent Test**: build a two-place state; record an edit in place A; switch to B; undo once and assert A's
entity is restored, B is still active, and B's camera and selection are unchanged; redo and assert the edit is back.

- [X] T012 [US1] `test/GameplayStateTests.fs`: add the cross-place test — two present places; record an entity edit
  naming place A (a `Transform` or a `SetLight`); make B active; `History.undoHistory`; assert A's entity is
  reversed, `state.ActiveZone` is still B, and B's camera and `state.Editor.Selection` are unchanged; `redoHistory`
  and assert the edit returns with B still active. (depends on T003, T006)
- [X] T013 [US1] `test/GameplayStateTests.fs`: assert the delete-restore does not switch — delete a **non-active**
  place, `undoHistory`, and assert the place and its entities are present again and the active place, camera and
  selection are exactly as before (FR-014, SC-006). This tightens the existing deletes tests, which already assert
  the active place is unchanged; here the camera and selection are asserted too. (depends on T005)
- [X] T014 [US1] Checkpoint: `dotnet test` green. (depends on T012, T013)

**Checkpoint**: US1 is independently testable — a step acts on its own place.

---

## Phase 4: User Story 2 - One gesture may span several places (Priority: P2)

**Goal**: a single batch of mutations naming several places applies and reverses as one step.

**Independent Test**: build a step whose sub-commands name two different places; apply it, undo once, and assert both
places are back exactly as they were with the active place unchanged.

- [X] T015 [US2] `test/GameplayStateTests.fs`: add the multi-place batch test — two present places, a `Batch` whose
  sub-commands name A and B (e.g. a `Transform` in A and a `SetLight` in B); `History.commit`; assert both places
  changed and one step is on the history; `undoHistory` once and assert both are back and `state.ActiveZone` never
  changed; `redoHistory` and assert the whole gesture returns. (depends on T003)
- [X] T016 [US2] Checkpoint: `dotnet test` green. (depends on T015)

**Checkpoint**: a gesture may span places as one step — the capability the fork needs.

---

## Phase 5: User Story 3 - The rule is recorded and single-place use is unchanged (Priority: P3)

**Goal**: the docs state the new rule, and a single-place session behaves exactly as before.

**Independent Test**: build and run the suite; grep the docs/headers for the old rule; assert transforms coalesce
per (place, ref) and the step count for a sequence of single-place edits is unchanged.

- [X] T017 [US3] `test/SelectionTests.fs`: assert coalescing is per **(place, ref)** — two `Transform`s to the same
  ref index in two different places do **not** merge (two steps), while two in the same place do (one step); and the
  single-place merge behaviour is otherwise unchanged. (depends on T004)
- [X] T018 [US3] Docs and headers — update `docs/EDITOR_DESIGN.md` (D-011: a command carries the place it acts on;
  a `ZoneMove` is no longer load-bearing), `docs/ARCHITECTURE.md` (the command/history description), and the module
  headers of `state/Command.fs` and `state/History.fs` (the "no command carries a zone" comment). Leave
  `specs/048-unified-undo-stack` as the historical record, superseded.
- [X] T019 [US3] Checkpoint: `dotnet build` warning-free and `dotnet test` green; grep `docs/` and the `Command`/
  `History` headers and confirm none still states that a command carries no zone or that a step relies on the active
  place being the recorded one. (depends on T017, T018)

**Checkpoint**: the rule is recorded and single-place use is provably unchanged.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T020 Run the headless section of `quickstart.md` (the five scenarios) and confirm the outcomes; confirm
  `fsharp-game.fsproj` is untouched and no file was added or removed. (depends on T014, T016, T019)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: depends on Setup; **blocks** all stories (the command shape).
- **US1 (Phase 3)**: depends on Foundational.
- **US2 (Phase 4)**: depends on Foundational (its batch is a `Batch` of place-carrying commands).
- **US3 (Phase 5)**: depends on Foundational; the doc edits are independent but the grep checkpoint wants the code
  and tests in place, so run after US1/US2.
- **Polish (Phase 6)**: depends on all stories.

### Within and between phases

- T002 → { T003, T004, T006–T010 }; T003 → T005; T004 → T017; T005 → T013; T003 → T012, T015.
- T006–T009 touch different files and can be written together; T010 (tests) depends on T002 only.
- The model change (T002–T005) is one compile-forced change: adding a field breaks every site at once, so it lands as
  a unit before the tree compiles again.

### Parallel Opportunities

- T006 (`Selection.fs`), T007 (`Spawning.fs`), T008 (`Editor.fs`), T009 (`AssetBrowser.fs`) are independent files.
- Within T010, the test files are independent and can be updated in any order.
- Within T018, the two docs are independent files.

---

## Parallel Example: Foundational phase

```bash
# Different files, no interdependency — all depend only on T002's new command shape:
Task: "editor/Selection.fs — name state.ActiveZone in every command"
Task: "editor/Spawning.fs — name the active place in every command"
Task: "editor/Editor.fs — Transform in finishDrag/updateNudge"
Task: "editor/AssetBrowser.fs — SetEnvironment step"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Setup (T001) and Foundational (T002–T011): the command layer is place-carrying and green.
2. US1 (T012–T014).
3. **STOP and VALIDATE**: an edit in one place undoes in that place with another place active.

### Incremental notes

- This is a **refactor**: the compiler drives most of it (adding the field breaks every construction site and match).
  T010 is the mechanical test sweep; the story tests then assert the new guarantees.
- Behaviour is unchanged except the two stated changes: coalescing is per (place, ref) (T004), and a delete-restore
  no longer switches (T005, FR-014).
- `world/Zone.fs` is expected untouched — the delete-restore reuses `Zone.deactivatePhysics` (the same
  build-then-detach `GameplayState.populateZone` uses for a non-active zone). If it turns out a small helper is
  needed, it belongs in T005.
- No persistence change; no cue vocabulary; the scenario fork remains the next increment and is not built here.

## Notes

- `[P]` = different files, no dependencies.
- `Command` stays compiled before `GameplayState`; resolution lives in the state-level runner (`placeOf` + a lookup),
  so the command type never names the state container (FR-007).
- The zone-level `UndoStack.undo`/`redo` steppers remain valid only for stacks whose commands all name one place;
  a multi-place batch is driven through `History` (research note).
