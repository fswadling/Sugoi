---

description: "Task list for the Walk Around feature"
---

# Tasks: Walk Around

**Input**: Design documents from `/specs/016-walk-around/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — `plan.md` ("Testing") and `quickstart.md` specify xUnit coverage in `test/PlayerTests.fs` and an extension to `test/PhysicsTests.fs`.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- F# single project at the repository root; modules live in domain clusters (`core/`, `physics/`, `render/`, `world/`, `state/`, `editor/`, `screen/`).
- Compile order is defined by `fsharp-game.fsproj`; a file may only reference symbols defined in earlier files.
- Tests live in `test/` (one `<Module>Tests.fs` per module).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Register the new module and test file in the build.

- [X] T001 Add `<Compile Include="world/Systems.fs" />` to `fsharp-game.fsproj`, immediately after `world/Zone.fs` (before `state/Screen.fs`).
- [X] T002 Add `<Compile Include="PlayerTests.fs" />` to `test/fsharp-game.Tests.fsproj`, in the existing ordering.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared state, binding, and adapters that every user story needs.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 [P] Make actor capsules dynamic in `physics/Physics.fs`: `createCapsuleBody` uses `MotionType.Dynamic` with `AllowedDOFs` restricted to translations and sleeping disabled (stays upright), and add `getBodyVelocity`/`setBodyVelocity`/`setBodyRotation`/`getBodyTransform`. Keep the module a leaf (Jolt + `System.Numerics` only) per `contracts/player-systems.md`.
- [X] T004 [P] No `Zone` transform helper: `syncActors` writes each actor's `Base.Transform` in place through `Actors.Get` (byref), never by value and never touching the body. `world/Zone.fs` is unchanged by this feature.
- [X] T005 [P] Create `world/Systems.fs` with `[<Struct>]` `CameraFollow` (`Yaw`, `Pitch`, `Distance`, `Height`), `createFollow`, the tuning constants, and the pure `playerHandleOf` (raises for a non-actor), per `data-model.md`.
- [X] T006 Extend `state/GameplayState.fs` with `Player : SlotMap.Handle option` and `Follow : Systems.CameraFollow`; initialise `Follow` in `create`. Do not add a character or locomotion copy.
- [X] T007 Add *Set as Player* to `editor/Inspector.fs` for actor selections: `state.Player <- Some (Systems.playerHandleOf ref)` directly (not a `Command`), replacing any existing binding and rejecting non-actors loudly. It must not touch physics.

**Checkpoint**: State, binding, and adapter exist; user stories can begin.

---

## Phase 3: User Story 1 - Bind an actor as the player and walk around (Priority: P1) 🎯 MVP

**Goal**: A deliberately bound actor is driven in play mode with tank controls, physical collision, and gravity, and is left where it walked.

**Independent Test**: Set an actor as the player, enter play, drive it with `W`/`A`/`S`/`D`; confirm it moves, turns, collides with the environment, stays upright and on the ground, and that exiting play shows it at its walked-to position.

### Tests for User Story 1

- [X] T008 [US1] Write tests in `test/PlayerTests.fs`: `playerHandleOf` returns the actor's index/generation and raises for a prop; `isPlayerLive` is true only for a bound live actor; the three player-gated systems are no-ops with `Player = None` and with a stale handle (use `Zone.create ()`, which needs no GPU); `syncActors` writes every actor's body transform back, player or not.
- [X] T009 [P] [US1] Write a headless test in `test/PhysicsTests.fs`: create a dynamic capsule above the room's floor, step the world, and assert it falls, comes to rest on the floor, and stays upright (rotation remains identity).

### Implementation for User Story 1

- [X] T010 [US1] Implement `playerInput` in `world/Systems.fs` (resolve the bound player itself; read facing from the actor transform; A/D add `turnRate * dt` to the facing and write the body rotation; W/S write the body's horizontal velocity along the facing, preserving its vertical velocity).
- [X] T011 [US1] Implement `syncActors` in `world/Systems.fs` (walk the actor slots; read each body's position/rotation and write that actor's entity transform in place, with the feet = centre − capsule offset). Not player-gated: all actors are dynamic, so syncing only the player leaves the others rendering where they were placed.
- [X] T012 [US1] Wire the play chain into `screen/GameplayScreen.fs`: while `not state.Editing`, run `playerInput → Physics.step → syncActors` (then camera follow and animation). The world is stepped for all of play mode; the player systems no-op without a live player, while `syncActors` runs regardless. The editor does not simulate. No enter/leave callbacks.

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - A following camera (Priority: P2)

**Goal**: A separate camera-follow system keeps the view behind and above the player, tracking position and facing.

**Independent Test**: Bind a player, enter play, and confirm the camera remains behind and above throughout movement and turning, with no manual camera input.

### Tests for User Story 2

- [X] T013 [US2] Write a test in `test/PlayerTests.fs` for the pure `cameraPose`: for a forward facing it places the camera behind and above the player, and the target is the pivot above the player's feet.

### Implementation for User Story 2

- [X] T014 [US2] Implement `cameraPose` (pure) and `cameraFollow` (resolve the bound player itself; derive yaw from the actor's facing, place the camera from pitch/distance/height) in `world/Systems.fs`.
- [X] T015 [US2] Wire `cameraFollow` into the play chain in `screen/GameplayScreen.fs`, after `syncActors`.

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Idle/jog animation from held keys (Priority: P3)

**Goal**: The player shows idle when no movement key is held and jog while one is held.

**Independent Test**: Bind a player, enter play, hold and release a movement key, and confirm the idle ↔ jog switching follows the held keys.

### Implementation for User Story 3

- [X] T016 [P] [US3] Add a `playClip` helper to `world/ActorRender.fs` that switches the instance's animation slot to a named clip (no-op when already playing it), failing loudly if the asset does not define it.
- [X] T017 [US3] Implement the key-driven Idle/Jog switch in `world/Systems.fs`: the pure `locomotionClip` (returns `"Jog"` while any of `W`/`A`/`S`/`D` is held, else `"Idle"`) and its application via `animate` after `syncActors`.
- [X] T018 [US3] Write a test in `test/PlayerTests.fs` for `locomotionClip`: a held movement key selects the jog clip; no movement key held selects idle.

**Checkpoint**: All three user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and end-to-end validation.

- [X] T019 [P] Update `docs/ROADMAP.md` (mark phase 7 complete, advance "Next step") and drop the `(planned)` marker on `world/Systems.fs` in `docs/PROJECT_STRUCTURE.md`.
- [X] T020 Run the `quickstart.md` validation scenarios: `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj`, then walk through the manual scenarios — same-frame key response (SC-006) and leaving play at the walked-to position (SC-004); fix any failures.
- [X] T021 [P] Fix the Jolt collision-filter lifetime in `physics/Physics.fs`: `World` retains `Settings`/`PairFilter`/`BroadPhase`/`ObjectVsBroadPhase`, because `PhysicsSystem` holds raw pointers to them — once the GC finalises a wrapper, collision silently stops and dynamic actors fall through all geometry. Guard it with `collision still works after a garbage collection` in `test/PhysicsTests.fs`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Stories (Phase 3+)**: Depend on Foundational; in priority order (P1 → P2 → P3).
- **Polish (Phase 6)**: Depends on all targeted stories.

### User Story Dependencies

- **US1 (P1)**: Only the foundational phase. No dependency on other stories.
- **US2 (P2)**: Foundational only; independently testable from `cameraPose`, integrates with US1's chain wiring (T012).
- **US3 (P3)**: Foundational only; applies the clip to the bound actor after the move.

### Within Each User Story

- `playerInput` → `syncActors` → chain wiring (US1).
- `cameraPose`/`cameraFollow` before wiring them (US2).
- `playClip` before applying it (US3).

### Parallel Opportunities

- T003 and T005 (different files) can run in parallel.
- T008 and T009 (different test files) can run in parallel.
- T016 (ActorRender) is independent of the other US3 work.
- T019 (docs) is independent of implementation.

---

## Parallel Example: Foundational Phase

```bash
# Independent files — run together:
Task: "Make actor capsules dynamic and add body accessors in physics/Physics.fs"
Task: "Create world/Systems.fs with CameraFollow + playerHandleOf"
```

## Parallel Example: User Story 1

```bash
Task: "Write playerHandleOf tests in test/PlayerTests.fs"
Task: "Write dynamic capsule fall/rest test in test/PhysicsTests.fs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories).
3. Complete Phase 3: US1.
4. **STOP and VALIDATE**: walk the character around the environment.
5. Demo if ready.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → walk the character (MVP).
3. US2 → camera follows → validate.
4. US3 → idle/jog switching → validate.
5. Polish → docs + end-to-end quickstart.

---

## Notes

- [P] tasks = different files, no dependencies.
- The player binding is a `SlotMap.Handle` (index + generation), not an `EntityRef`; a deleted actor or recycled slot naturally fails resolution, so there is no clear-on-delete step.
- The player binding is deliberately not an undoable command (spec FR-003).
- Actor capsules are dynamic with rotation locked: gravity and collision come from the step, and the character stays upright. There is no separate gravity system.
- `Set as Player` and the play/edit toggle create, reset, and destroy nothing.
- Step the world for all of play mode (and never while editing); the editor is a passive view.
- `syncActors` writes the entity transform only; never write a body from it.
- Keep `physics/Physics.fs` a leaf and `world/Systems.fs` free of `state/` dependencies.
- Commit after each logical group.
