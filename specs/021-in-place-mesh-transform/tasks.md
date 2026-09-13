---

description: "Task list for the In-Place Mesh Transform feature"
---

# Tasks: In-Place Mesh Transform

**Input**: Design documents from `/specs/021-in-place-mesh-transform/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Added — the mesh-transform path is headlessly testable (the `ZoneTests` fixture builds a fake `AssetManager` and a real physics world with no device), and the headless body-identity tests are the primary guard for FR-001/FR-002/FR-004.

**Organization**: The fix is a single coupled change (Foundational). The user stories validate its outcomes and are co-equal P1 (smooth drag and correct scale rebuild), plus a P2 correctness story.

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

**Purpose**: Record the baseline so the fix is measured against a known-good state.

- [X] T001 Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj` and confirm clean / 99 passing before any change.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The single code change — update a mesh body in place when the scale is unchanged, rebuild only when it changes.

**⚠️ CRITICAL**: All three user stories validate this one change; nothing else compiles differently.

- [X] T002 In `world/Zone.fs`, change the private `setMeshTransform` so that when `p.Base.Transform.Scale = transform.Scale` it updates the existing body in place via `Physics.setBodyTransform world ph.Body transform.Position transform.Rotation` (keeping the same `PhysicsAttachment`), and only when the scale changes does it `destroyBody` + `meshBody` (rebuild). Keep the single trailing `mapBody zone attachment ref` and the `p <- { p with … }` update. Do not touch `Zone.setTransform`'s actor branch, `meshBody`, or `meshCollider`. No new public API.

**Checkpoint**: The mesh path no longer rebuilds on a move/rotate; a scale change still rebuilds.

---

## Phase 3: User Story 1 - Dragging a prop stays smooth (Priority: P1) 🎯 MVP

**Goal**: A move/rotate of a prop updates its collider without destroying or rebuilding the body.

**Independent Test**: move a spawned prop headlessly and confirm its body id is unchanged with the transform applied; run the editor and drag a prop with no `update` stall.

- [X] T003 [US1] Extend the `ZoneTests` headless fixture so a prop asset can be supplied (`test/ZoneTests.fs`), then add a test: spawn a mesh-colliding prop via `Zone.spawnProp`, record `(zone.Props.Get handle).Physics.Value.Body`, call `Zone.setTransform` with the same scale and a new position (and separately a new rotation), and assert the body id is unchanged, the body's transform matches (`Physics.getBodyTransform`), and `zone.BodyToEntity` still maps it to the ref (FR-001, FR-004, FR-007). T005 and T007 reuse the extended fixture.
- [X] T004 [US1] Run `quickstart.md` scenario 1: place a prop and drag it; the per-frame `update` phase shows no ~200–280 ms spike (SC-001). Also nudge it repeatedly (scenario 4, FR-008).

**Checkpoint**: Moves and rotates are in-place and stall-free.

---

## Phase 4: User Story 2 - Scaling a prop still rebuilds its collider (Priority: P1)

**Goal**: A scale change rebuilds the collider so it matches the new geometry.

**Independent Test**: scale a spawned prop headlessly and confirm the body id changed and `BodyToEntity` holds the new body; scale a prop in the editor and confirm the collider matches.

- [X] T005 [US2] Add a headless test in `test/ZoneTests.fs`: spawn a mesh-colliding prop, call `Zone.setTransform` with a changed scale (uniform, then a non-uniform scale on one axis), and assert the body was rebuilt (new body id), the old body is absent from `zone.BodyToEntity`, and the new body is mapped to the ref (FR-002, FR-003).
- [X] T006 [US2] Run `quickstart.md` scenario 3: scale a prop (uniform and non-uniform) in the editor; the collider matches the scaled geometry when picked, and the one-off rebuild cost is expected (unlike a move) (SC-003).

**Checkpoint**: Scale changes still rebuild correctly; the optimisation did not silently ignore scale.

---

## Phase 5: User Story 3 - The collider never drifts from the geometry (Priority: P2)

**Goal**: Identity and collider fidelity hold for every transform change and for entities without a collider.

**Independent Test**: after move/rotate/scale, the entity still resolves and its body matches; a collider-less entity still moves; undo restores the prior transform.

- [X] T007 [US3] Add a headless test in `test/ZoneTests.fs`: after a move, `Zone.isAlive`/`Zone.tryEntityAt` return the entity with the new transform and `zone.BodyToEntity` resolves `ref`; and assert the `Physics = None` path still updates the transform (FR-004, FR-005). Keep the existing `setTransform` tests green.
- [X] T008 [US3] Run `quickstart.md` scenarios 5 and 6: after moving a prop, picking selects the same entity and undo restores the prior transform; a part with no collider still moves with no error (SC-005).

**Checkpoint**: Correctness preserved end to end.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and final validation.

- [X] T009 [P] Record the fix in `docs/RENDER_PERFORMANCE.md` §11: the per-drag hitch was the mesh-body rebuild, now in place for move/rotate; distinguish it from the spawn-time asset-load cost.
- [X] T010 Run `quickstart.md` end to end and confirm `dotnet build` clean + `dotnet test` green (SC-004). Confirm the full suite still covers command/undo and actor behaviour, verifying FR-006 (no semantic, actor, or undo change) against the unchanged tests.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on T001 (baseline). T002 blocks all stories.
- **User Stories (Phases 3–5)**: All depend on T002. US1 and US2 are co-equal P1; US3 is P2. They can be verified in any order.
- **Polish (Phase 6)**: Depends on the stories.

### Within Each User Story

- Headless tests (T003, T005, T007) all edit `test/ZoneTests.fs`, so they are **not** parallelisable with each other — write them together or sequentially.
- Runtime scenarios (T004, T006, T008) need the running editor (a GPU) and are user-run.

### Parallel Opportunities

- T009 (docs) is independent of the code and test tasks.
- Nothing else is safely parallel: the tests share one file and the runtime checks share the app.

---

## Parallel Example: Polish

```bash
# Independent of the code and tests:
Task: "Record the in-place mesh-transform fix in docs/RENDER_PERFORMANCE.md §11"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Setup + Foundational (T001–T002) — the whole code change.
2. US1 (T003–T004): moves/rotates are in-place and stall-free.
3. US2 (T005–T006): scale still rebuilds.
4. US3 (T007–T008): correctness preserved.

---

## Notes

- [P] tasks = different files, no dependencies.
- The code change is one private function (`setMeshTransform`); treat it as one commit.
- Scale equality is a heuristic in the safe direction: a needless rebuild is only slower, never incorrect — so no extra state is needed.
- Actors (capsule bodies) are deliberately out of scope (spec Assumptions).
- The run-based checks (T004, T006, T008) need a running app; T001, T003, T005, T007, T009, T010 are static.
