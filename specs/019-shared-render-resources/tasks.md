---

description: "Task list for the Shared Render Resources feature"
---

# Tasks: Shared Render Resources

**Input**: Design documents from `/specs/019-shared-render-resources/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: None added — this is an ownership/location refactor with no new pure logic (plan.md "Testing"). The existing suite must still pass, and `test/SpawningTests.fs` must be updated because it builds a `GameplayState` by hand.

**Organization**: Tasks are grouped by user story to enable independent validation. The state move itself is a single coupled change (Foundational); the user stories validate its outcomes.

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

**Purpose**: Confirm no build or compile-order change is required.

- [X] T001 Confirm the move needs no `fsharp-game.fsproj` change: `state/SharedState.fs` already precedes `state/GameplayState.fs`, which precedes `screen/`, so `GameplayState` and the screens can read shared state without reordering.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The coupled ownership move — shared state gains the GPU context, screen state loses the pipelines, and the render entry point reads through shared state.

**⚠️ CRITICAL**: The tasks below must land together to compile; no user story can be validated until they do.

- [X] T002 Add the GPU context to `state/SharedState.fs`: a mutable `Gpu : Gpu.Gpu` field (device, formats, pipelines, depth), and change `create` to take the `Gpu` (which carries the device) instead of a bare device.
- [X] T003 Remove `MeshPipeline` and `StaticPipeline` from `state/GameplayState.fs`, and change `create` to take the shared state instead of the two pipelines and the device — the resulting signature is `create shared debugDraw gizmoDraw` (see `contracts/state-ownership.md`). Per-screen buffers still get the device from shared state.
- [X] T004 Change `screen/GameplayScreen.fs` `renderScene` to take the shared state and read `shared.Gpu.StaticPipeline` / `shared.Gpu.MeshPipeline` instead of the removed fields.
- [X] T005 Change `screen/ScreenManager.fs`: `init` takes the `Gpu` context (plus the two debug draws), builds `SharedState` around it, constructs the gameplay screen from shared state, and passes shared state into `GameplayScreen.renderScene`.
- [X] T006 Change `Engine.fs`: create the GPU context after the device, hand it to `ScreenManager.init`, and thereafter reach it through `manager.Shared` for `Gpu.ensureDepthTexture`, `Gpu.depthTargetInfo`, and `Gpu.destroy`. The local `gpu` is a bootstrap value read for `DepthFormat` and consumed at init — it must not be used afterward (all later access goes through shared state), or a resized depth target would be written to a stale copy.
- [X] T007 Update `test/SpawningTests.fs`: the hand-built `GameplayState` no longer has the pipeline fields; supply shared state (or the device) as `create` now expects.

**Checkpoint**: The app compiles and the pipelines have one owner; user stories can be validated.

---

## Phase 3: User Story 1 - Screen state describes the screen (Priority: P1) 🎯 MVP

**Goal**: The gameplay screen's state type has no device-level render resources.

**Independent Test**: inspect the type and confirm every field describes the screen.

- [X] T008 [US1] Run `quickstart.md` scenario 1: confirm `state/GameplayState.fs` has no pipeline/format/depth field and its `create` receives no hardware handles.
- [X] T009 [US1] Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj`; fix any failures.

**Checkpoint**: Screen state is hardware-free and the project builds.

---

## Phase 4: User Story 3 - Nothing about rendering changes (Priority: P1)

**Goal**: Same scene, same draw counts, same lifecycle.

**Independent Test**: run the app and compare the frame log and appearance to the pre-change build.

- [ ] T010 [US3] Run `quickstart.md` scenarios 3, 5, 6, 8: the ArtRoom still reports `draws 191 instances 521`, adding a prop still adds only its distinct submeshes, the menu is unaffected, the grid/gizmos still work, and the per-phase log shows no added per-frame work. Separately (a static check, no run needed) confirm no changes were made under `world/`, `physics/`, or `state/Command.fs` (FR-009).
- [ ] T011 [US3] Run `quickstart.md` scenario 7: start and quit the app; it shuts down cleanly with unchanged creation/release ordering.

**Checkpoint**: The move is behaviour-preserving.

---

## Phase 5: User Story 2 - Device resources have a single home (Priority: P2)

**Goal**: Each device-level resource is owned once, in shared state, and reached from there.

**Independent Test**: search for the pipelines and confirm one declaration, read from shared state; resize recreates depth through shared state.

- [X] T012 [US2] Run `quickstart.md` scenario 2: confirm `MeshPipeline`/`StaticPipeline` are declared once (on the GPU context) and only read elsewhere — no screen holds a copy.
- [ ] T013 [US2] Run `quickstart.md` scenario 4: resize the window while playing; the depth target is recreated through shared state with no error or artifacts.

**Checkpoint**: Device resources have one owner and survive a resize.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and final validation.

- [X] T014 [P] Update `docs/PROJECT_STRUCTURE.md` and/or `docs/ARCHITECTURE.md` if they describe the shared/screen state split, so the device-resource ownership is recorded.
- [ ] T015 Run the full `quickstart.md` end to end and confirm `dotnet build` + `dotnet test` pass.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup. T002–T007 are one coupled move and must land together; T003 depends on T002 (the `SharedState` shape), T004 depends on T003, T005 depends on T002–T004, T006 depends on T005, T007 depends on T003.
- **User Stories (Phase 3+)**: Depend on Foundational. US1 (P1) and US3 (P1) both gate on the build; US2 follows.
- **Polish (Phase 6)**: Depends on all stories.

### Within Each User Story

- Build/verification after the move; the manual resize/render checks need a running app.

### Parallel Opportunities

- T014 (docs) is independent of the code tasks.
- Nothing else is safely parallel: the Foundational tasks touch interdependent modules.

---

## Parallel Example: Polish

```bash
# Independent of the code:
Task: "Update docs/PROJECT_STRUCTURE.md / ARCHITECTURE.md for the state ownership split"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Setup + Foundational (T001–T007) — the whole move, landed together.
2. US1 (T008–T009): screen state is hardware-free and the build passes.
3. US3 (T010–T011) confirms behaviour is unchanged; US2 (T012–T013) confirms single ownership and resize.

---

## Notes

- [P] tasks = different files, no dependencies.
- The Foundational tasks are interdependent by nature (the type changes ripple through the call sites) — treat them as one commit.
- "Per-screen render buffer" is deliberate: `DebugDraw`, `GizmoDraw`, and `EnvironmentBatch` stay on `GameplayState`.
- No `world/`, `render/`, physics, or command changes.
- The tasks that need a running app (T010, T011, T013) cannot be verified headless.
