# Tasks: Physics + Picking

**Input**: Design documents from `/specs/013-physics-picking/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/physics.md, quickstart.md

**Tests**: Included — `test/PhysicsTests.fs` for collider derivation (bounds → capsule,
submesh-range merge) and the collider raycast (hit/miss).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1/US2/US3)

## Phase 1: Setup

**Purpose**: Confirm the baseline.

- [X] T001 Verify baseline: `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj`
  succeed on the phase-12 commit before changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Jolt is wired in, the `Physics` adapter exists, and entities can carry
a physics attachment (collider + body).

- [X] T002 Replace the `Box3D` package reference with `JoltPhysicsSharp` in
  `fsharp-game.fsproj`, and add an `AfterTargets="Build"` target to copy the Jolt
  native library next to the apphost (mirror `CopySdl3ImageLibraries`).
- [X] T003 [P] Add `physics/Physics.fs`: a handle-based Jolt adapter (`World`,
  `BodyId`, `create`, `step`, `createMeshBody`, `createCapsuleBody`, `destroyBody`,
  `setBodyTransform`, `raycast`) with no `Entity`/GPU dependency; insert it in
  `fsharp-game.fsproj` after `core/Input.fs` and before `render/Gpu.fs`.
- [X] T004 [P] Extend `world/Entity.fs`: add `MeshCollider`/`CapsuleCollider` and
  a `Physics : PhysicsAttachment<'Collider> option` field to `Actor` (capsule) and
  `Prop` (mesh); update construction sites to supply it.
- [X] T005 Add the `Physics.World` to `state/SharedState.fs` (one per game) and a
  `BodyToEntity : Dictionary<BodyId, EntityRef>` map on `world/Zone.fs`; step the
  world each frame in `GameplayScreen.update`.

**Checkpoint**: compiles; the world + `Physics` adapter + entity body fields exist.

---

## Phase 3: User Story 1 - Props and the environment have colliders (Priority: P1) 🎯 MVP

**Goal**: props and environment parts own a static mesh collider derived from the
actual geometry, created on spawn and removed on delete.

**Independent Test**: import an environment and spawn a prop; each spawned entity
owns a body, and deleting an entity removes its body.

### Implementation for User Story 1

- [X] T006 [P] [US1] Keep collider geometry on `world/EnvironmentAsset.fs`: store
  per-submesh `Positions`/`Indices` (CPU-side) and `Bounds` on `EnvironmentPart`/
  `EnvironmentAsset`.
- [X] T007 [P] [US1] Add collider-derivation helpers in `world/Zone.fs`
  (`meshCollider` merging a submesh range; `capsuleCollider` from bounds), and add
  `test/PhysicsTests.fs` asserting the pure-math derivation.
- [X] T008 [US1] In `world/Zone.fs`, create a mesh body on prop/environment-part
  spawn (`spawnPropBinding` / `loadEnvironment`) and destroy it on
  `removeEntity`; record the body in the `BodyToEntity` map.
- [X] T009 [US1] Ensure spawn/remove in `state/Command.fs` create/destroy bodies
  through undo/redo so colliders never orphan or drift.

**Checkpoint**: props/environment parts have mesh colliders that live and die with
their entity.

---

## Phase 4: User Story 2 - Click-select hits physical shapes (Priority: P2)

**Goal**: viewport selection raycasts against colliders rather than the AABB slab
test.

**Independent Test**: rotate a prop and click its surface; it selects where the
old AABB would have missed, and a click on empty space selects nothing.

### Implementation for User Story 2

- [X] T010 [US2] Route click-select in `editor/Viewport.fs`/`editor/Selection.fs`
  through `Physics.raycast` (replacing `Raycast.intersectAabb` for entity
  selection), mapping the hit `BodyId` back to an `EntityRef` via the entity↔body
  map.
- [X] T011 [P] [US2] Add a raycast test to `test/PhysicsTests.fs`: cast against a
  mesh body (hit returns the body) and a ray missing the body (returns `None`).

**Checkpoint**: click-select matches the drawn collider shape.

---

## Phase 5: User Story 3 - Character capsule + colliders stay in sync (Priority: P3)

**Goal**: the actor gets a capsule collider, and editor transforms keep colliders
matched (including undo/redo).

**Independent Test**: spawn an actor (it has a capsule); move a prop and click the
moved shape; undo the move and confirm the collider returns with it.

### Implementation for User Story 3

- [X] T012 [US3] Compute actor bounds from `Iqm.Model.Positions` in `world/Assets.fs`
  (stored on `ActorAsset.Bounds`), derive a capsule (radius/height), create a
  capsule body on actor spawn, and destroy it on remove.
- [X] T013 [US3] In `state/Command.fs`/`world/Zone.fs`, rebuild the body (destroy +
  recreate from the cached collider) on every move/rotate/scale (and its undo/redo)
  so the collider follows the entity transform.

**Checkpoint**: actor has a capsule; colliders stay in sync with the editor.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T014 [P] Update `README.md`, `docs/ROADMAP.md`, and `docs/ARCHITECTURE.md` to
  replace the `Box3D` physics reference with Jolt Physics.
- [ ] T015 Run `quickstart.md` end-to-end (`dotnet run`): import `artroom`, spawn a
  prop + Akane, verify accurate picking, transform sync, and delete; fix any bug.
- [X] T016 Confirm `dotnet build` is clean and
  `dotnet test test/fsharp-game.Tests.fsproj` passes (existing suite + `PhysicsTests`).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: T002 (package) → T003 (`Physics.fs`); T004 (entity
  fields) and T005 (world) can run in parallel with T002/T003 once the package is
  in place.
- **US1 (Phase 3)**: depends on Foundational (T002/T003/T004/T005).
- **US2 (Phase 4)**: depends on US1 (bodies must exist to pick).
- **US3 (Phase 5)**: depends on Foundational + US1 (derivation helper + world).
- **Polish (Phase 6)**: depends on US1 + US2 + US3.

### Parallel Opportunities

- T003/T004/T006/T007 are parallel (different files). T008 depends on T003 + T006 +
  T007. T010 depends on T008; T011 is parallel to T010. T012/T013 build on T003 +
  T004.

## Implementation Strategy

### MVP First (User Story 1)

1. Foundational (T002–T005) → Jolt wired, `Physics.fs` exists, entities carry a
   physics attachment, the world steps.
2. US1 (T006–T009) → props/environment parts have mesh colliders (first physical
   result).

Then US2 (T010–T011) → accurate picking, US3 (T012–T013) → actor capsule + sync,
then Polish (T014–T016) → docs + end-to-end verification.
