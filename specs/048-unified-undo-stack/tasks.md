---

description: "Task list for One undo history on the gameplay state"
---

# Tasks: One undo history on the gameplay state

**Input**: Design documents from `/specs/048-unified-undo-stack/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/undo-history.md, quickstart.md

**Tests**: **Required.** The spec makes them part of the deliverable — SC-005 (`dotnet build` clean and
`dotnet test` green) and the four cross-zone/add/delete/whole-history outcomes (SC-001…SC-004) are asserted in
`test/GameplayStateTests.fs`, which already builds a headless state (`Unchecked.defaultof` for the GPU pieces,
a device-free asset manager). Every story phase therefore has test tasks.

**Organization**: Tasks are grouped by user story so each story's *behaviour* is independently assertable. This is
a small, tightly-coupled feature: US1–US3 all add cases to the same `Command` union (`state/Command.fs`), the same
runner (`state/History.fs`) and the same test file, so they are **sequential**, not parallel, despite being
separate stories. `[P]` appears only where the files are disjoint (mostly the documentation tasks).

**One ordering fact decides the whole list**: `GameplayState.UndoByZone` may not be removed until `ZoneMove`
exists. With the side map gone and no move step, the first undo after a zone switch applies the last edit to the
*wrong* zone — the exact bug the feature fixes. So T010 (remove the side map) sits inside US1, after T008/T009
(the move step), and US2/US3 depend on US1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Single project at the repository root (this project has no `src/`): game sources under `world/`, `state/`,
`screen/`; editor under `editor/`; tests under `test/`; documentation under `docs/`. F# compiles in `.fsproj` list
order, on which `state/History.fs`'s placement depends (T002).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Scaffolding so the new module exists and the build stays green before the move of any behaviour.

- [X] T001 Create `state/History.fs` as `module History`, opening `Prime`, `Entity`, `Zone`, `Command`,
  `SharedState`, `GameplayState` (empty body)
- [X] T002 Add the `Compile` entry for `state/History.fs` to `fsharp-game.fsproj`, **after
  `state/GameplayState.fs`** (it adds, removes and switches zones) and before `editor/Spawning.fs`, with the
  comment that says why — the same shape as the `world/CueEngine.fs` entry (contract `undo-history.md`)
- [X] T003 Confirm `dotnet build` is clean (warnings are errors) with the empty module in place

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The state-level runner and the steppers every story adds to. **Behaviour-preserving — the per-zone
side map is still in place at the end of this phase, and nothing observable changes.**

**⚠️ CRITICAL**: US1–US3 all add a case to `state/History.fs`, so none can start until it exists and builds.

- [X] T004 In `state/Command.fs`, keep `UndoStack` as it is — `create`, `push` (with the transform merge) and the
  zone-level `undo`/`redo`, which step a stack against one zone and need no gameplay state. The *state-level*
  steppers are new and live in `History.fs` (T005)
- [X] T005 In `state/History.fs`, implement, per data-model.md: `apply` and `undo` (state-level dispatch — for an
  entity or zone-value command call `Command.apply`/`Command.undo` with `GameplayState.activeZone state`,
  `shared.Physics` and `shared.Assets`); `commit` (apply, then `UndoStack.push` the command onto
  `state.Editor.Undo`); and `undoHistory`/`redoHistory` (the logic `UndoStack.undo`/`redo` had, lifted to take the
  state and reach the command through `apply`/`undo`). Undo/redo on an empty side is a no-op
- [X] T006 Point `editor/Editor.fs`'s `undoStep` and `redoStep` at `History.undoHistory`/`redoHistory`; leave the
  `Ctrl+Z`/`Ctrl+Y` bindings, the Edit menu (whose enabled state reads `state.Editor.Undo.Applied`/`.Redo`) and the
  `idle` gate unchanged
- [X] T007 Confirm `dotnet build` clean and `dotnet test` green — this phase changes no behaviour, so the existing
  suite must pass untouched

**Checkpoint**: the runner exists and the editor still undoes exactly as before.

---

## Phase 3: User Story 1 - One history, and a zone move is a step (Priority: P1) 🎯 MVP

**Goal**: An edit made in one zone can be undone after the developer has moved to another zone — the edit is
still on the history, and undo steps back through the moves to reach it.

**Independent Test**: edit in A; move to B; edit in B; undo twice — B's edit reverts with B active, then the move
reverts with A active again — and then A's edit reverts.

### Implementation for User Story 1

- [X] T008 [US1] Add `ZoneMove of Previous : string * Next : string` to the `Command` union in
  `state/Command.fs` (data-model.md), with the comment that a command carries no zone and why
- [X] T009 [US1] Handle `ZoneMove` in `state/History.fs`: `apply` is `GameplayState.switchZone shared &state next`;
  `undo` is `switchZone shared &state previous`. It re-frames the camera and drops the zone-scoped selection by
  construction, because that *is* `switchZone` (research D6) — carry no camera pose and no previous selection
- [X] T010 [US1] Remove `GameplayState.UndoByZone` from the record and `create`, and the park/load lines from
  `enterZone` in `state/GameplayState.fs`; keep `enterZone`'s selection/drag/nudge drop and camera re-frame. Drop
  `UndoByZone = Dictionary()` from the hand-built states in `test/SelectionTests.fs` and `test/SpawningTests.fs`,
  and replace the `UndoByZone.ContainsKey` assertion in the delete test in `test/GameplayStateTests.fs` with one
  that the zone is simply gone
- [X] T011 [US1] In `editor/Outliner.fs`'s Zones section, make a row click on a zone that is **not** active commit
  a `ZoneMove (previous, name)`; a click on the zone already active keeps its current behaviour (clears the
  Inspector's subject via `selectZone`, records nothing)
- [X] T012 [US1] Tests in `test/GameplayStateTests.fs` (build a state as that file already does): an edit in A, a
  move to B, an edit in B are undone in reverse order — B's edit with B active, the move (A active again), then
  A's edit (SC-001); and the same is redone in the order the edits were made, with the active zone following
- [X] T013 [US1] Tests in `test/GameplayStateTests.fs`: a move with no edit after it undoes on its own and changes
  nothing else; an undo of an edit reaches the zone it was recorded in and touches no other; and a zone switch no
  longer parks or loads a history

**Checkpoint**: User Story 1 is fully functional and testable on its own — this is the MVP.

---

## Phase 4: User Story 2 - Adding a zone is undoable (Priority: P2)

**Goal**: Adding a zone is a step; undoing it removes the zone and returns to the zone that was active before.

**Independent Test**: add a zone (`atrium` exists and is active); undo — it is gone and the previous zone is
active again; redo — it is back and active.

### Implementation for User Story 2

- [X] T014 [US2] Add `ZoneAdd of Previous : string * Name : string` to the `Command` union in `state/Command.fs`
- [X] T015 [US2] **No new removal function was needed** — see the Outcomes note. `deleteZone` keeps its guards
  (unknown name, last zone) and is what a `ZoneDelete` applies and what a `ZoneAdd`'s undo calls: the last-zone
  guard tests the count *before* the removal, and removing a zone that a later step added leaves at least one
  behind, so it cannot re-fire during an undo. One removal path, not two
- [X] T016 [US2] Handle `ZoneAdd` in `state/History.fs`: `apply` is `GameplayState.addZone shared &state name`
  (which makes the new zone active); `undo` is `switchZone shared &state previous`, then `deleteZone name`. The
  removed zone is empty — every edit made to it came later on the history and has been undone first
- [X] T017 [US2] In `editor/Outliner.fs`, make *Add Zone* commit a `ZoneAdd (previous, pending)` (capturing the
  active zone before the add) and clear the name buffer on success, exactly as it does today. The disabled gate
  (empty or duplicate name) is unchanged
- [X] T018 [US2] Tests in `test/GameplayStateTests.fs`: adding a zone and undoing leaves the same set of zones and
  the same active zone (SC-002); redoing adds it back and makes it active; **one** undo covers the add and the
  zone change it caused (not two)

---

## Phase 5: User Story 3 - Deleting a zone is undoable (Priority: P3)

**Goal**: Deleting a zone is a step; undoing it brings the zone back with everything that was in it, at the same
identities.

**Independent Test**: build a zone with actors, props, waypoints, triggers, a loaded environment, a changed light
and a designated player; delete it; undo; assert the zone is back with the same entities at the same refs and the
same environment, light and player; redo deletes it again.

### Implementation for User Story 3

- [X] T019 [US3] In `world/Zone.fs`, add the `ZoneContents` record (data-model.md) and the two routines that
  read and write it: `captureZone` (the live maps through the existing `capture*` readers, plus `zone.Environment`,
  `zone.Light` and `zone.Player`) and `occupyZone` (`setEnvironment`, then
  `occupyActor`/`occupyProp`/`occupyWaypoint`/`occupyTrigger` per recorded ref, then the light and the player).
  They live with the zone rather than beside the command, because a command's payload is defined where the thing it
  describes is (`ActorState`/`PropState`/`Environment` in Entity.fs, `LightSettings` in Texture.fs) and
  `ZoneTemplate` sets the same precedent; `world/Zone.fs` is compiled before `state/Command.fs`, so `ZoneDelete`
  can name the record (see the Outcomes note on the move)
- [X] T020 [US3] Add `ZoneDelete of Previous : string * Name : string * Contents : ZoneContents` to the `Command`
  union in `state/Command.fs`
- [X] T021 [US3] Handle `ZoneDelete` in `state/History.fs`: `apply` is `deleteZone name` (the contents were
  captured by the caller before the step); `undo` is create a zone named `Name`, `occupyZone` its contents, then
  `switchZone shared &state previous` (a no-op switch when the deleted zone was not active)
- [X] T022 [US3] In `editor/Outliner.fs`, make *Del* commit a `ZoneDelete` whose contents are captured **before**
  the delete, from the zone being deleted; the `previous` is the zone active before the delete — which, when the
  deleted zone is the active one, is the deleted zone itself. The "not the last zone" gate is unchanged
- [X] T023 [US3] Tests in `test/GameplayStateTests.fs`: a zone holding actors, props, waypoints, triggers, a
  loaded environment, a non-default light and a designated player round-trips through a delete and an undo with
  the same entity count **and the same `EntityRef`s** (hold a ref taken before the delete and assert it still
  resolves after the undo) and the same environment, light and player (SC-003); redo deletes it again; deleting
  the **active** zone and undoing restores it **and** makes it active; deleting an **inactive** zone and undoing
  leaves the active zone and camera unchanged

**Checkpoint**: All three user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: The property that belongs to no single story, the documents the feature moves with it, and
end-to-end validation.

- [X] T024 Whole-history round trip (SC-004) in `test/GameplayStateTests.fs`: after a session of edits across two
  zones plus an add and a delete, undo through the entire history returns the project to its starting zones and
  entities with none lost or corrupted; a new edit after an undo clears the redo side
- [X] T025 [P] Record every statement this feature supersedes in `specs/048-unified-undo-stack/research.md`
  (Decision 10), in a table of *where it was written → what this feature does instead*, with what is **not**
  superseded. **Do not edit the prior specs** — `specs/024`, `specs/030`, `specs/032`, `specs/043` and `specs/045`
  are historical artefacts, and the record lives here
- [X] T026 [P] Correct `docs/EDITOR_DESIGN.md` principle 4 (the exceptions list) and D-011 (the command/undo
  model, including the module cycle and where the history now lives)
- [X] T027 [P] Correct `docs/EDITOR_DESIGN.md` D-010 (the cue's history) and D-022/D-023 (the cast binding and the
  zone registry: still direct writes, but no longer for the per-zone reason)
- [X] T028 [P] Correct `docs/ARCHITECTURE.md`: "the undo history [is] held per zone"
- [X] T029 [P] Correct `docs/PROJECT_STRUCTURE.md`: list `state/History.fs` for what it is
- [X] T030 [P] Correct the header of `editor/CastPanel.fs`: the binding is a direct write because it is not a scene
  edit, not because the stacks were per zone
- [X] T031 [P] Correct the header of `editor/ZoneRegistryPanel.fs`: registering is a direct write for the same
  reason. Neither panel's behaviour changes — whether they *should* become steps is a separate decision (spec
  Assumptions)
- [X] T032 Run `dotnet build` (warnings-clean) and `dotnet test` (green, including the new cross-zone, add,
  delete and whole-history cases), then validate `quickstart.md` — one `Ctrl+Z` undoes the last thing done
  wherever it was done, and the view follows it

---

## Outcomes

Recorded, not relitigated — the four places the implementation differs from the plan.

- **T015 was delivered by a deletion, not an addition.** The plan drew a `removeZone` (a `deleteZone` without its
  guards) so that an undo could remove a zone without a guard re-firing. It cannot re-fire: the last-zone guard
  tests the count *before* the removal, and removing a zone that a later step added leaves at least one behind.
  `deleteZone` is therefore reused unchanged, and there is one removal path rather than two. `data-model.md` and
  `contracts/undo-history.md` were corrected to match.
- **`UndoStack.undo`/`redo` stayed in `state/Command.fs`.** They step a stack against *one zone* and need no
  gameplay state, which is what the zone-level suite (`ZoneTests`, `SpawningTests`, `SelectionTests`) drives them
  with; removing them would have churned ~24 call sites and dropped that coverage. `History` adds the
  **state-level** `undoHistory`/`redoHistory` beside them, because only they can carry out a zone step. The plan's
  "the steppers move" is half-right: the state-level ones are new, the zone-level ones stay.
- **Two tests were replaced, not adapted.** `switching zones swaps the per-zone undo history` asserted the
  behaviour this feature removes; it became `switching zones leaves the one undo history alone`. The per-zone
  designation test's premise — an undo that cannot reach across zones — is now false, and its place is taken by
  `an edit in one zone is undone after a move, in the order the edits were made`.
- **Actors cannot be exercised headlessly, so SC-003 is partial.** `Zone.occupyActor` needs a real `ActorAsset`:
  `Assets.createActorRender` indexes the manager and `ActorRender.create` calls `SDL.CreateGPUBuffer`. No test in
  this repo builds one, so `deleting a zone and undoing restores its entities, environment and light` covers a
  placed prop, a waypoint, a trigger, the environment's part and the light — and **not** an actor or the player
  designation. `ZoneContents.Player` is put back by the same `occupyZone` call that restores every other field, but
  it is not asserted; the interactive quickstart step 3 is where it is checked.
- **The interactive walkthrough was not run** (it needs a display). The headless half is green; the four editor
  steps in `quickstart.md` need a hand.
- **`ZoneContents` and its reader/writer moved to `world/Zone.fs` (after the feature was complete).** They were
  first written in `state/Command.fs`, justified as "they take a `Zone` and nothing else from the state" — true of
  `Zone.fs` too, so it was never the real reason. Every other command payload is defined where the thing it
  describes is (`ActorState`/`PropState`/`Environment` in `world/Entity.fs`, `LightSettings` in
  `render/Texture.fs`), and `ZoneTemplate` — the same kind of composite — is in `world/` as well; `ZoneContents` was
  the one exception. `world/Zone.fs` is compiled before `state/Command.fs`, so the record can live there and the
  `ZoneDelete` case still names it. `captureZone`/`occupyZone` became `Zone.captureZone`/`Zone.occupyZone`; the
  record is a plain reference record, not a `[<Struct>]` — it is allocated once per delete and stored inside a
  reference-type DU, so a struct would copy eight field slots where a pointer is copied and save no allocation.
- **Result**: build 0 warnings / 0 errors; `dotnet test` **394 passed / 0 failed** (387 before the feature: +6
  cases and one replaced in this phase, and the two convergence cases below).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies — can start immediately
- **Foundational (Phase 2)**: depends on Setup — **blocks all three stories** (they all add a case to
  `state/History.fs`)
- **User Stories (Phase 3–5)**: all depend on Foundational; here they are also sequential between themselves (see
  below)
- **Polish (Phase 6)**: depends on all three stories

### User Story Dependencies

- **US1 (P1)**: after Foundational. No dependency on US2/US3 — it is the MVP
- **US2 (P2)**: after **US1**. `ZoneAdd` cannot work while `UndoByZone` is still parked on a switch: the add makes
  the new zone active, so the stack swap would park the very step just recorded. US1 removes the side map
- **US3 (P3)**: after **US1**, and independent of US2. `ZoneDelete` applies `deleteZone` and `ZoneDelete`'s undo
  recreates the zone through `Zone.occupyZone`; neither depends on US2's work

### ⚠️ Why US1–US3 Are Sequential Here

Each story adds a case to the same `Command` union, the same `apply`/`undo` dispatch and the same test file. They
are separate stories because they are separately *assertable* (three independent user journeys), not because they
can be built in parallel. `[P]` is therefore used only where files are disjoint — the documentation tasks in
Phase 6.

### Within Each Story

- The `Command` case (T008, T014, T019–T020) before the runner handles it (T009, T016, T021)
- The runner before the editor call site that commits it (T011, T017, T022)
- The editor call site before the test that exercises the journey end to end (T012, T018, T023)
- **Never remove `UndoByZone` (T010) before `ZoneMove` exists (T008/T009)** — see the note at the top

### Parallel Opportunities

- T001 and T003 are sequential (the file before the build check)
- Phase 6's documentation tasks **T025–T031** are seven disjoint files and can run in parallel
- Nothing else is parallel: every other pair of tasks shares a file

---

## Parallel Example: Documentation (Phase 6)

```text
# Seven different files — launch together:
Task: "Record the superseded decisions in research.md D10; prior specs untouched (T025)"
Task: "docs/EDITOR_DESIGN.md — principle 4 and D-011 (T026)"
Task: "docs/EDITOR_DESIGN.md — D-010, D-022, D-023 (T027)"
Task: "docs/ARCHITECTURE.md — the per-zone undo line (T028)"
Task: "docs/PROJECT_STRUCTURE.md — state/History.fs (T029)"
Task: "editor/CastPanel.fs header (T030)"
Task: "editor/ZoneRegistryPanel.fs header (T031)"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (behaviour-preserving — the suite must still be green)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: an edit in one zone undoes after moving to another, with the view following
5. This is already a usable improvement — the history no longer dies at a zone switch

### Incremental Delivery

1. Setup + Foundational → the runner exists, behaviour unchanged
2. US1 → cross-zone undo (MVP)
3. US2 → adding a zone is undoable
4. US3 → deleting a zone is undoable; the history now accounts for the whole session
5. Polish → the whole-history round trip, and the documents and superseded decisions move

### Notes

- **[P]** = different files, no dependencies. Most of this feature is one file deep, so expect few.
- **Do not move the `Command` type.** Only the runners move. Moving the type is cyclic —
  `EditorState` names `UndoStack`, which names `Command`, and `GameplayState` names `EditorState`.
- **Do not put a zone on a command.** Recording the moves is what keeps the active zone the one the step belongs
  to; adding a zone field to the commands would be the alternative `specs/024` D-5 rejected and this feature
  deliberately did not take.
- **Do not change the per-kind entity commands, `Batch`, `world/Zone.fs`, the render path or the physics world.**
- **Do not migrate the cue.** It keeps `CueHistory` and its own Undo/Redo buttons (spec FR-014); moving it onto
  this history is the next feature.
- **Do not add coalescing for moves** — every move is its own step (research D7).
- Commit after each task or logical group; validate each story at its checkpoint.

---

## Phase 7: Convergence

Appended by `/speckit-converge` (2026-10-01). The implementation satisfies the spec, plan and tasks; two success
criteria are **implemented but not verified**. Append-only — nothing above was changed.

- [X] T033 [P] Extend `deleting a zone and undoing restores its entities, environment and light` in
  `test/GameplayStateTests.fs` so the round trip also covers an **actor** and the **player designation** (an actor
  asset in the fixture, `ZoneContents.Actors` restored and `.Player` asserted). If `ActorRender.create` cannot run
  against the device-free fixture — `test/ActorTests.fs` builds no `ActorAsset` either — record that an actor's
  restore is outside the headless envelope where SC-003 is read, so the criterion is not taken as verified in full
  (SC-003, partial)
- [X] T034 [P] Assert in `test/GameplayStateTests.fs` that undo with an empty history and redo with an empty redo
  side are no-ops: `History.undoHistory`/`redoHistory` leave the state and the history exactly as they were (spec
  Edge Cases, partial)

**Phase 7 outcomes.** T034 is done as written. T033 took the fallback: `ActorRender.create` calls
`SDL.CreateGPUBuffer`/`Gpu.createTransferBuffer` on the GPU device, and the fixture's device is `IntPtr.Zero` —
passing that to native SDL would dereference null, so a restored actor is genuinely outside the headless envelope
(and `test/ActorTests.fs` builds no `ActorAsset` to try with). The suite instead asserts the half it can: a zone's
contents **carry** an actor (ref and authored state) and the player designation, and `occupyZone` puts the
designation back — a field of its own, needing no actor. `quickstart.md` records that the restored actor is its
step 3, checked by hand.
