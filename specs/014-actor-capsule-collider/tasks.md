# Tasks: Actor Capsule Collider

**Input**: Design documents from `/specs/014-actor-capsule-collider/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, quickstart.md

**Tests**: Included where the feature touches pure data/derivation logic (`EntityTests`, `PhysicsTests`). The UI overlay itself is validated via the quickstart scenarios.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)
- Include exact file paths in descriptions

## Path Conventions

- **Single project**: `world/`, `render/`, `editor/`, `test/` at repository root (see `fsharp-game.fsproj` compile order).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the single shared UI primitive the feature needs.

- [ ] T001 Add an `igCheckbox` P/Invoke binding (`checkbox : string -> bool& -> bool`) to `render/ImGui.fs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Data-model + render-primitive changes that user stories build on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T002 [P] Add `ShowCollider : bool` to the `Actor` record in `world/Entity.fs`; set `ShowCollider = false` at both construction sites in `world/Zone.fs` (`setActorWithAsset`, `spawnActor`) and update `test/EntityTests.fs`
- [ ] T003 [P] Add a `DebugDraw.capsule` wireframe primitive (equator circles + cylinder silhouette + hemisphere arcs) in `render/DebugDraw.fs`

**Checkpoint**: Foundation ready — user story implementation can now begin in parallel.

---

## Phase 3: User Story 1 - Toggle capsule collider visualization (Priority: P1) 🎯 MVP

**Goal**: A developer can show/hide an actor's capsule collider in the editor.

**Independent Test**: Spawn an actor, select it, enable "Show Collider" in the Inspector, and confirm a capsule wireframe appears and disappears with the toggle.

### Implementation for User Story 1

- [ ] T004 [US1] Render a "Show Collider" checkbox for the selected actor in `editor/Inspector.fs`, writing the flag back via `SlotMap.setByIndex` (depends T001, T002)
- [ ] T005 [US1] Draw the capsule overlay in `editor/Viewport.fs` when an actor's `ShowCollider` is set, using its `Physics` attachment collider (depends T002, T003)

**Checkpoint**: User Story 1 is functional and testable independently (the capsule may not yet be aligned — that is User Story 2).

---

## Phase 4: User Story 2 - Collider matches the rendered actor (Priority: P2)

**Goal**: The capsule uses a fixed radius, spans the actor's full height, and sits on the actor's feet.

**Independent Test**: With the overlay on, confirm the capsule spans feet→head and uses a consistent radius across actor assets.

### Implementation & Tests for User Story 2

- [ ] T006 [US2] Change `Zone.capsuleCollider` in `world/Zone.fs` to use `defaultActorColliderRadius = 0.35f` and the full Z height; add `Zone.capsuleCenter` and use it in `Zone.capsuleBody` in `world/Zone.fs`
- [ ] T007 [US2] Update `test/PhysicsTests.fs`: fix the `capsuleCollider` assertion for the fixed radius/full height and add a `capsuleCenter` regression test (depends T006)
- [ ] T008 [US2] Update the `editor/Viewport.fs` overlay to use `Zone.capsuleCenter` so it matches the physics body (depends T005, T006)

**Checkpoint**: Both user stories are now independently functional and the overlay matches the physics body.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Verification and consistency across both stories.

- [ ] T009 Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj`; walk the `quickstart.md` scenarios to confirm toggle, alignment, and actor-only behavior

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS both user stories.
- **User Stories (Phase 3/4)**: Depend on Foundational completion; US1 and US2 can then proceed, with US2's overlay task (`T008`) depending on US1's overlay task (`T005`).
- **Polish (Phase 5)**: Depends on both user stories.

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational; no dependency on US2.
- **User Story 2 (P2)**: `T006`/`T007` (collider derivation + tests) have no dependency on US1; `T008` (overlay update) depends on US1's `T005`.

### Within Each User Story

- Core data/derivation changes before the overlay integration.
- Story complete before moving to the next priority.

### Parallel Opportunities

- Setup/foundational tasks `T001`, `T002`, `T003` touch different files and can run in parallel.
- US2's `T006`/`T007` can run in parallel with US1's `T004`/`T005`.

---

## Parallel Example

```text
# Foundational, in parallel:
Task: T002 — Actor.ShowCollider field (world/Entity.fs, world/Zone.fs, test/EntityTests.fs)
Task: T003 — DebugDraw.capsule primitive (render/DebugDraw.fs)

# User Story 2 derivation, in parallel with User Story 1 UI:
Task: T006 — Zone.capsuleCollider + Zone.capsuleCenter (world/Zone.fs)
Task: T004 — Inspector checkbox (editor/Inspector.fs)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 + 2.
2. Complete Phase 3 (US1).
3. **STOP and VALIDATE**: toggle the overlay on/off for a selected actor.
4. Then complete Phase 4 (US2) to align the capsule.

### Incremental Delivery

1. Setup + Foundational → shared primitive + field + wireframe ready.
2. US1 → show/hide a capsule (MVP).
3. US2 → correct size/position; overlay matches the body.
4. Polish → build/test/quickstart verification.

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps a task to US1/US2 for traceability.
- `Zone.capsuleCenter` is the single source of truth used by both the physics body and the overlay.
- Commit after each task or logical group; stop at any checkpoint to validate independently.
