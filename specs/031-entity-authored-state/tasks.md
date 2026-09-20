---

description: "Task list for the Entity Authored State feature"
---

# Tasks: Entity Authored State

**Input**: Design documents from `/specs/031-entity-authored-state/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — the plan and `quickstart.md` specify xUnit coverage: the reshaped constructions and the two classification experiments extend `test/EntityTests.fs`, `test/ZoneTests.fs`, `test/SpawningTests.fs`, and `test/SelectionTests.fs`. Everything is headless except the editor walk.

**Organization**: Tasks are grouped by user story. As in `specs/030-entity-command-families`, Phase 2 is deliberately large: reshaping an entity's payload type and rewiring its readers is one compilation unit, so it lands as a whole and keeps the app green. Phase 3 then **proves** US1's guarantee rather than adding it, Phase 4 runs US2's classification experiments, and Phase 5 verifies US3's claim that nothing maintains the boundary by convention.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- F# single project at the repository root; modules live in domain clusters (`world/`, `state/`, `editor/`, `render/`, `screen/`).
- Compile order is defined by `fsharp-game.fsproj`; no new source file is added, so both compile lists are unchanged.
- Tests live in `test/` (one `<Module>Tests.fs` per module).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the feature needs no build change.

- [X] T001 Verify that the feature extends existing modules only — `world/{Entity,Zone,Systems,EnvironmentBatch}.fs`, `state/{Command,GameplayState}.fs`, `editor/{Inspector,Selection,Spawning,Outliner,Viewport}.fs`, `screen/GameplayScreen.fs`, and the seven test modules — so `fsharp-game.fsproj` and `test/fsharp-game.Tests.fsproj` need no new `<Compile>` entry.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The authored records, the reshaped entities, and the readers and builders that must move with the type change. Every user story reads this.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 Add `ActorState`, `PropState`, and `TriggerState` to `world/Entity.fs` and reshape `Actor`, `Prop`, and `Trigger` to hold their authored value as `State`, keeping the derived fields (`Physics`, `Renderer`) and the runtime flag (`Triggered`) as siblings and exposing the authored properties as read-through accessors. Leave `Entity` and `Waypoint` unchanged (research Decisions 1 and 3). — The read-through accessors were dropped before the feature landed: every authored read is qualified with `State` instead, and no member was needed beyond the record (research Decision 2).
- [X] T003 In `world/Zone.fs`, make `captureActor`/`captureProp`/`capturePart`/`captureTrigger` resolve-and-read: the same signatures and the same raise for a stale or wrong-kind ref, but a body that returns the entity's authored value with no clearing step; update their docs and leave `captureWaypoint` alone (research Decision 4) (depends on T002).
- [X] T004 In `world/Zone.fs`, take the authored value through the builders and writers: `occupy*` accept a state and construct the entity field by field (a derived field still fails to build until produced), the `spawn*` builders keep their present parameters and assemble the authored value inside — which is where a new authored field's initial value is decided, and the site T014's experiment lands on — `setTrigger` takes a `TriggerState` and leaves `Triggered` alone, and `setName`/`setTransform`/`setShowCollider` write `State` (research Decision 5) (depends on T002).
- [X] T005 In `state/Command.fs`, change the entity payloads to the state types — `ActorCreate`/`ActorDelete`, `PropCreate`/`PropDelete`, `PartCreate`/`PartDelete`, `TriggerCreate`/`TriggerDelete`, `TriggerSet` — leaving `apply`/`undo` bodies, `Waypoint*`, `Link`, `LinkSpawn`, `Transform`, `Rename`, and `SetEnvironment` otherwise unchanged (depends on T002).
- [X] T006 Rewire `editor/Spawning.fs` and `editor/Selection.fs` to build their commands from the authored value, keeping `duplicate`'s write of that value into the live copy it spawned (`setShowCollider`, `setWaypointNext`, `setTrigger`) and changing nothing else about their behaviour (depends on T004, T005). — `duplicate` records the *copy's* authored value, captured after those writes settle it, not the source's: the `spawn*` builders apply kind defaults and a fresh transform, so a payload carrying the source's value would restore a second original on redo, which US1 scenario 4 forbids. This is the one deliberate departure from 030's payload-of-the-source shape, and it is guarded by `a duplicate records the copy's own authored value, so redo restores the copy` in `test/SpawningTests.fs`.
- [X] T007 In `editor/Inspector.fs`, take `updateTriggerRef`'s change function over `TriggerState` and edit the authored value in the radius, height, interactable, prompt, and enabled fields; the collider overlay keeps going through `Zone.setShowCollider` and stays off the undo stack (depends on T004).
- [X] T008 [P] Update the entity-shape construction helpers in `test/EntityTests.fs`, `test/ZoneTests.fs`, `test/SystemsTests.fs`, `test/GameplayStateTests.fs`, and `test/PlayerTests.fs` for the nested `State`, including the two in-place `.Base.Transform` writes the compiler reports (depends on T002). — Both writes became whole-record copy-and-updates on an authored path, `a <- { a with State.Base.Transform = … }`: an in-place write through two nested immutable struct hops is not addressable once the entity type comes from another assembly, which is how the compiler reports it in the test project.
- [X] T009 [P] Update the trigger payloads and actor constructions in `test/SpawningTests.fs` and `test/SelectionTests.fs` for the nested `State` and the state-typed `TriggerSet` (depends on T002).
- [X] T010 In `world/Systems.fs`, spell `syncActors`'s two in-place transform writes through `State` (`.State.Base.Transform.…`), which is where the compiler points once an in-place write through the immutable `State` field fails to compile (research Decision 2) (depends on T002).

**Checkpoint**: `dotnet build` is clean and the full suite passes with **every behaviour test unmodified** — the reshape is invisible in behaviour, which is the whole point of the feature.

---

## Phase 3: User Story 1 - An entity's authored state is a value of its own (Priority: P1) 🎯 MVP

**Goal**: Prove that what a command records is the authored value, and that nothing derived or runtime can be part of it.

**Independent Test**: for each of the three kinds, record it for a deletion and confirm the value carries every authored property; delete and undo and confirm the entity returns with every authored property unchanged and its resources newly built.

**Note**: Phase 2 reshapes the payloads, so this phase is where the guarantee is *proved* and encoded as a regression guard.

### Tests for User Story 1 ⚠️

- [X] T011 [US1] Add the structural purity test to `test/ZoneTests.fs`: reflect over `ActorState`, `PropState`, and `TriggerState` and assert that no member is named for a physics attachment, a rendering resource, or a runtime flag, so SC-001 is machine-checked rather than eyeballed (FR-002, SC-001).
- [X] T012 [US1] Add the recording-and-restore test to `test/ZoneTests.fs`: for a prop, a part, and a trigger, the value a deletion carries equals the authored value the live entity had field by field, and undoing the deletion rebuilds the resources with the authored value identical (FR-003, FR-004, FR-005).
- [X] T013 [P] [US1] Add the create-side test to `test/SpawningTests.fs`: a create carries the authored value it was given, and redoing it produces an entity with that value and freshly built resources (FR-003, SC-001).

**Checkpoint**: US1 is proven — the payload type cannot express a resource, and recording is a read.

---

## Phase 4: User Story 2 - A new field is classified by the compiler (Priority: P2)

**Goal**: Show that a new field cannot be added without its classification being decided.

**Independent Test**: add an authored property and confirm nothing in the recording or restoring path needs an edit; add a derived resource and confirm the build fails until it is produced.

- [X] T014 [US2] Run the authored-property experiment: add a throwaway field to `ActorState` in `world/Entity.fs`, confirm the build only asks for the sites that decide a new entity's initial value (and that `world/Zone.fs`'s recording and restoring paths needed no edit), record the outcome in this task, then remove the field (FR-006, SC-004). **Outcome**: one app site failed — `Zone.spawnActor`'s initial-state literal — plus the six test fixtures that construct an actor state. `Zone.captureActor` and `Zone.occupyActor` needed no edit at all.
- [X] T015 [US2] Run the derived-resource experiment: add a throwaway derived field to `Actor` in `world/Entity.fs`, confirm the build fails in `Zone.occupyActor` and `Zone.spawnActor` until it is produced, record the outcome, then remove the field (FR-006, SC-004) (depends on T014). **Outcome**: exactly two errors, `world/Zone.fs`'s `occupyActor` and `spawnActor`, as claimed.

**Checkpoint**: Both directions are demonstrated — an authored field flows, a derived one stops the build.

---

## Phase 5: User Story 3 - No code maintains the boundary by convention (Priority: P3)

**Goal**: Confirm the clearing step is gone and the one remaining teardown is a lifecycle behaviour.

**Independent Test**: read the recording path and find no step that copies a live entity and clears derived fields; swap an environment out and back and confirm its parts behave exactly as before.

- [X] T016 [US3] Confirm the recording path records and never writes: `world/Zone.fs`'s `capture*` return the authored value with no clearing, and the only remaining write of `Physics = None`/`Renderer = …` is `deactivateEnvironment`'s teardown; update any comment still describing the old behaviour (FR-005). Confirm in the same pass that the recording path leaves runtime state alone: a trigger's flag is still recomputed in place by the trigger system and nothing records or restores it, with the containment tests passing unmodified (FR-010).
- [X] T017 [P] [US3] Confirm the environment lifecycle is unchanged: `test/ZoneTests.fs`'s environment load/swap tests pass unmodified, including that an inactive zone's parts hold no bodies and no GPU resources and are rebuilt on return (FR-011).

**Checkpoint**: All three user stories hold.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Whole-feature validation.

- [X] T018 Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj` and confirm the whole suite passes with every behaviour test unmodified — including the physics and rendering behaviour tests (FR-009) — and that neither experiment left a trace. — Result: build clean, 240/240 pass (236 before this feature).
- [ ] T019 Walk interactive scenario 10 in `specs/031-entity-authored-state/quickstart.md` in the editor (with a display): every entity operation, the trigger edits, the collider overlay, chain authoring, and zone switching behave as before, with the stats line's frame rate unchanged, and the adjacent name-attribution correction re-checked (add an actor, then add a waypoint — the actor keeps its name; likewise for a trigger's prompt). **Not run if the agent environment has no display.** — Not run: the agent environment has no display.
- [X] T020 [P] Check the design docs for claims this feature changes — `docs/ARCHITECTURE.md`'s entity model list and `docs/EDITOR_DESIGN.md` D-011 — and correct anything now inaccurate. — Corrected D-011's "with the derived body and renderer cleared" and added the authored-value split to the F# port decisions.
- [X] T021 [P] Confirm no `EntityKind` match was added outside `world/` and `state/`, and that the hot loops (`editor/Viewport.fs`, `screen/GameplayScreen.fs`, `world/Systems.fs`) read the authored properties they read before (FR-008, SC-005).

---

## Adjacent correction on the same working tree (not part of this feature)

The tree carries one change this feature does not own and no task above describes: the
Inspector's name-attribution defect, which has been present since
`specs/027-entity-rename` (`2078b19`) and is unrelated to the authored-state split.

- **Defect**: `Inspector.draw` re-seeded `NameBuffer` from the *currently selected* entity
  unconditionally every frame, and re-pointed `NameEditRef` in the same step, while the
  commit that follows reads the buffer and attributes it to that ref. The panels draw
  outliner-first (`editor/Editor.fs`), so one click on a spawn button changes the selection
  and deactivates the live field within the same frame: the buffer already holds the new
  entity's name when the commit fires, against the entity the text was typed for. Adding an
  actor and then a waypoint renamed the actor to "Waypoint".
- **Correction**: `render/ImGui.fs` binds `igIsItemActive` as `ImGui.isItemActive()`;
  `state/EditorState.fs` gains `NameEditing`/`PromptEditing`; `editor/Inspector.fs` seeds
  and re-points each field only while it is not live, and records liveness straight after
  the widget. The trigger prompt carried the same latent defect and took the same
  correction.
- **Consequence for this feature's artifacts**: this is the only change outside the
  feature's reader set, so the "untouched" claims in `plan.md`, `data-model.md`, and
  `contracts/authored-state.md` are scoped to the feature rather than to the tree. T019
  re-checks the defect, since the interactive walk is where it is observable.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks all user stories**. T002 is first, then T003, T004, T005, T010 (same or dependent files), with T008 and T009 free to run alongside T004–T007.
- **User Stories (Phase 3–5)**: all depend on Phase 2, and each is independently testable.
- **Polish (Phase 6)**: depends on all three stories.

### Within the Foundational phase

- T002 → T003, T004, T005, T010; T004 ∥ T005 (different files); T006 needs T004 and T005; T007 needs T004; T008 ∥ T009 need only T002.

### Within the User Story phases

- US1: T011 → T012 (same file); T013 is independent (another file).
- US2: T014 → T015 (both mutate `world/Entity.fs`).
- US3: T016 → T017.

### Parallel Opportunities

- T008 ∥ T009 (different test files), both ready as soon as T002 lands.
- T020 ∥ T021 in Polish.
- US3's T017 can run alongside US1 and US2, since it only reads existing environment tests.

---

## Parallel Example: after T002

```bash
# The reshape is in; these touch different files:
Task: "T003 capture* become resolve-and-read in world/Zone.fs"
Task: "T005 entity payloads become the state types in state/Command.fs"
Task: "T008 entity-shape helpers in test/{Entity,Zone,Systems,GameplayState,Player}Tests.fs"
Task: "T009 trigger payloads and actor constructions in test/{Spawning,Selection}Tests.fs"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational) — the authored records and the reshape.
2. Complete Phase 3 (US1) — the proof that a payload is the authored value.
3. **STOP and VALIDATE**: run `dotnet test`; confirm the behaviour tests are unmodified and the purity test passes.

### Incremental Delivery

1. Foundational → the app compiles and behaves exactly as before, with the payload types now unable to hold a resource.
2. US1 → the guarantee is proven and guarded.
3. US2 → both classifications are demonstrated.
4. US3 → the convention is confirmed gone and the lifecycle teardown is confirmed untouched.

---

## Notes

- [P] tasks touch different files and have no incomplete dependencies; tasks in the same file are ordered even when independent.
- **Why Phase 2 is large**: a payload's type and every reader of it are one compilation unit; splitting them would leave a checkpoint that does not build.
- **Why the story phases are mostly proofs**: the reshape *is* the feature, so US1's job is to prove it, US2's to demonstrate the forcing, and US3's to confirm what was deleted.
- **Reads are qualified with `State`** (research Decision 2): there are no accessors, so every authored read says which half of the entity it touches, and an authored read is a field access rather than a member call. Deleting the members made the compiler name all of them — 47 in the app across 8 files, 37 across 7 test modules. Writes are compiler-enumerated the same way, because `State` is an immutable field.
- Nothing this feature touches lives in `render/`, `physics/`, or `core/`; `screen/GameplayScreen.fs` changes only in that its three authored reads are spelled through `State`. (`render/ImGui.fs` does change on the same working tree, for the adjacent correction above.)
- Verify `dotnet test` passes after each phase; commit per task or logical group.
