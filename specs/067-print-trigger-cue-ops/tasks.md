---
description: "Task list for Print & Trigger Cue Ops"
---

# Tasks: Print & Trigger Cue Ops

**Input**: Design documents from `/specs/067-print-trigger-cue-ops/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/cue-print-trigger-ops.md, quickstart.md

**Tests**: Included — the project's convention (and SC-005) require the suite green, and each op is a behaviour
the suite can assert headlessly with no device (spawn a trigger with `Zone.spawnTrigger` and read it back with
`Zone.tryTriggerAt`).

**Organization**: Tasks are grouped by user story (US1 P1 the trigger toggles, US2 P2 `Print`). Adding a `Cue`
case forces every exhaustive `match` on `Cue` to account for it, so each story bundles its union cases with the
match updates that keep the build warning-free.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 (foundational and polish carry none)

## Path Conventions

- Single F# project at the repository root; sources in `world/`, `editor/`, tests in `test/`.

---

## Phase 1: Foundational (Blocking Prerequisites)

**Purpose**: The trigger name lookup the toggles resolve through.

**⚠️ CRITICAL**: No user story can be implemented until this phase is complete.

- [X] T001 [P] `world/Zone.fs`: add `tryTriggerByName (zone: Zone) (name: string) : EntityRef option` — the
  first live trigger whose `Base.Name` matches, in slot order, mirroring `tryWaypointByName`.
- [X] T002 [P] `test/ZoneTests.fs`: assert `tryTriggerByName` finds a spawned trigger by name, returns `None`
  for an absent name, and returns the first match deterministically when two share a name.
- [X] T003 Build checkpoint: `dotnet build fsharp-game.fsproj` and `dotnet build test/fsharp-game.Tests.fsproj`
  compile with the lookup.

**Checkpoint**: the lookup exists; the stories can proceed.

---

## Phase 2: User Story 1 - A cue arms or disarms a trigger (Priority: P1) 🎯 MVP

**Goal**: a cue flips a named trigger's enabled flag in a named place; the next play tick honours it.

**Independent Test**: spawn a trigger in a placed zone, run a cue carrying `DisableTrigger (place, name)`, and
assert `Zone.tryTriggerAt` reports `Enabled = false` and a stepped `updateTriggers` no longer fires/tracks;
run `EnableTrigger` and assert it resumes; a redundant toggle, a missing name, and an absent place perform
nothing.

- [X] T004 `world/Cue.fs`: add `EnableTrigger` / `DisableTrigger` (`place: Place * trigger: string`); add them
  to the module's no-progress note.
- [X] T005 `world/CueEngine.fs`: add the arms and a `noOpTrigger` diagnostic (the actor `noOp` is
  character-shaped) — resolve the place (absent ⇒ no-op + log), `Zone.tryTriggerByName` (miss ⇒ no-op + log),
  `Zone.tryTriggerAt` (flag already holds ⇒ no-op + log), else `Zone.setTrigger` with `Base.Enabled` flipped.
  (depends on T001, T004)
- [X] T006 [P] `editor/CueTree.fs`: add the toggles to `kinds` (a new **Triggers** group), `kindName`, `label`,
  `executing` (false), `children` (none), `defaultOfKind`, `withPlace` (the place), `withTrigger` (the name),
  and the `drawEditor` place-combo arm. (depends on T004)
- [X] T007 [P] `editor/CueEditor.fs`: in `drawNodeText`, add a branch for the toggles — a combo over
  `zoneOfPlace place`'s trigger names, committed through `CueTree.withTrigger`, with a typed fallback when the
  place is not present. (depends on T004)
- [X] T008 [P] `test/CueTests.fs` and `test/CuePanelTests.fs`: extend the local `describe` helpers for the new
  cases so the test project compiles. (depends on T004)
- [X] T009 [US1] `test/CueTests.fs`: through `CueEngine.tick` — `DisableTrigger` flips the flag and a stepped
  `Systems.updateTriggers` stops firing/tracking, `EnableTrigger` restores it; a redundant toggle, a missing
  name, and a place that is not present perform nothing; the op records **no** step on `state.Editor.Undo`
  (FR-005).
- [X] T010 [P] [US1] `test/CuePanelTests.fs`: the toggle kinds and labels render, and `withPlace` / `withTrigger`
  write the place and the name.

**Checkpoint**: US1 works and is testable independently.

---

## Phase 3: User Story 2 - A cue prints a line (Priority: P2)

**Goal**: `Print` writes its text to the console where the walk reaches it, then is done.

**Independent Test**: run a cue carrying `Print` and assert it ends the tick it is reached and leaves no state;
assert it records no undo step; assert its label/kind render and `withText` replaces the text.

- [X] T011 `world/Cue.fs`: add `Print of text: string`; add it to the module's no-progress note.
- [X] T012 `world/CueEngine.fs`: add the arm — `printfn "cue: %s" text`, then `Fin`. (depends on T011)
- [X] T013 [P] `editor/CueTree.fs`: add `Print` to `kinds` (a new **Debug** group), `kindName`, `label`,
  `executing` (false), `children` (none), `defaultOfKind`, `textOf` / `withText`. (depends on T011)
- [X] T014 [US2] `test/CueTests.fs`: through `CueEngine.tick`, `Print` is a no-progress leaf (done the tick it
  is reached, no `…State`), and records **no** step on `state.Editor.Undo` (FR-005).
- [X] T015 [P] [US2] `test/CuePanelTests.fs`: the `Print` kind and label render, and `withText` replaces the text.

**Checkpoint**: both stories function.

---

## Phase 4: Polish & Cross-Cutting Concerns

- [X] T016 [P] Docs: note the trigger toggles and `Print` in the cue section of `docs/ARCHITECTURE.md` and under
  `docs/ROADMAP.md`; record that only the scenario ops and the camera-follow/interactions toggles remain.
- [X] T017 `dotnet build` warning-free and `dotnet test` green (SC-005).
- [X] T018 Run the automated section of `quickstart.md` and confirm the headless outcomes.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Foundational (Phase 1)**: no dependencies; **blocks** the user stories.
- **US1–US2 (Phases 2–3)**: depend on Foundational; run in priority order.
- **Polish (Phase 4)**: depends on the stories.

### Within and between stories

- T001 → T005; T004 → T005/T006/T007/T008; T011 → T012/T013.
- Test tasks follow the code they cover; the `describe` updates and the story tests share files, so they are not
  all parallelisable.

### Parallel Opportunities

- T001, T002 (distinct files) then T003.
- Within US1: T006 (CueTree), T007 (CueEditor), T008 (tests) touch distinct files once the `Cue` cases land.
- T016 is independent of the code.

---

## Implementation Strategy

### MVP First (User Story 1)

1. Foundational (the lookup).
2. US1 (the toggles).
3. **STOP and VALIDATE**: a cue can arm and disarm a trigger.
4. US2 (`Print`), then polish.

### Incremental notes

- Each story lands its `Cue` cases together with the matches that must account for them, so the build stays
  warning-free at every checkpoint.
- The bulk is US1 (the place/trigger resolution and its no-ops); US2 is thin.

## Notes

- `[P]` = different files, no dependencies.
- Ops are runtime effects, never steps on the undo history (FR-005).
- A cue op is forgiving: an absent place, a missing trigger, or a redundant toggle performs nothing and reports
  why on stderr (FR-003).
- No `.fsproj` change: no module is added or moved; no `EditorState` field is added.
