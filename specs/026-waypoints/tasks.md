---

description: "Task list for the Waypoints feature"
---

# Tasks: Waypoints

**Input**: Design documents from `/specs/026-waypoints/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — the plan (`Testing`) and quickstart specify xUnit coverage for the headless behaviour, with `test/ZoneTests.fs`, `test/SpawningTests.fs`, and `test/EntityTests.fs` extended.

**Organization**: Tasks are grouped by user story. This feature changes an existing type and extends existing modules, so Phase 2 carries the waypoint entity and its zone storage that every story reads; the story phases add the authoring surface, the camera placement, and the viewport marker.

**Retrospective**: written after the behaviour was built and demonstrated (per the spec's status), so the implemented tasks are marked `[X]` and the interactive walk is left open.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- F# single project at the repository root; modules live in domain clusters (`core/`, `physics/`, `render/`, `world/`, `state/`, `editor/`, `screen/`).
- Compile order is defined by `fsharp-game.fsproj`; no new source file is added, so nothing is inserted into the compile list.
- Tests live in `test/` (one `<Module>Tests.fs` per module).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the feature needs no build change.

- [X] T001 Verify that waypoints extend existing modules only — `world/Entity.fs`, `world/Zone.fs`, `editor/Spawning.fs`, `editor/Outliner.fs`, `editor/Viewport.fs` — so `fsharp-game.fsproj` and `test/fsharp-game.Tests.fsproj` need no new `<Compile>` entry and the existing order (`world/Entity.fs` → `world/Zone.fs` → `editor/`) already permits the additions.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Turn `Waypoint` into a stored entity kind and give the zone a waypoint slot map with the operations that address it. Every user story reads this.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Change `Waypoint` from `{ Position : Vector3 }` to `{ Base : Entity }` in `world/Entity.fs`, with the comment stating that it composes the shared base (name, transform, editor flags) and carries no renderer, collider, or asset binding.
- [X] T003 [P] Add `waypointRef (index) (generation)` (kind `EntityKind.Waypoint`, epoch `0u`) to `world/Entity.fs`, beside `actorRef`/`propRef`/`partRef`.
- [X] T004 Add `Waypoints : SlotMap<Waypoint>` (capacity 128) to the `Zone` record and initialise it in `Zone.create` in `world/Zone.fs`, and mention waypoints in the module's header comment.
- [X] T005 Add the waypoint case to `Zone.isAlive` (`zone.Waypoints.IsAlive(handleOf ref)`; `Environment`/`Trigger` stay never alive) and to `Zone.tryEntityAt` (`Some waypoint.Base`) in `world/Zone.fs` (depends on T002, T004).
- [X] T006 Add the waypoint case to `Zone.setTransform` in `world/Zone.fs`: write `Base.Transform` into the live slot in place, no-op when the ref is stale or absent, and add no body handling (depends on T005).
- [X] T007 Add the waypoint case to `Zone.removeEntity` in `world/Zone.fs`: free the slot when it is alive, no-op otherwise (depends on T005).
- [X] T008 Add `bindingNone : AssetBinding` (`AssetId = None`) and the waypoint case to `Zone.bindingAt` in `world/Zone.fs`, returning it for a waypoint and leaving the catch-all `failwithf` for every other kind without a binding (depends on T002).
- [X] T009 Add the waypoint case to `Zone.setActorWithAsset` in `world/Zone.fs`: `ensureSlotFree zone.Waypoints ref`, then `zone.Waypoints.Occupy(handleOf ref, { Base = entity })`, so undoing a waypoint deletion re-occupies the recorded slot with the recorded entity (depends on T008).
- [X] T010 Add `Zone.spawnWaypoint` in `world/Zone.fs` (under a `// ── Waypoints ──` banner): add `{ Base = entity }` to the waypoint map and return `waypointRef handle.Index handle.Generation`. It takes no world and no assets (depends on T004).
- [X] T011 Add waypoint cases to `test/ZoneTests.fs` under a `// ── Waypoint refs ──` banner: a spawned waypoint resolves to its base entity's transform; `setTransform` moves and rotates it in place and keeps the ref valid; `removeEntity` makes the ref stale; a waypoint that reuses a freed slot does not resolve a stale ref; waypoints live in `zone.Waypoints`, not `zone.Props` (depends on T005–T010).
- [X] T012 [P] Update `test/EntityTests.fs`: `Waypoint` composes the shared `Entity` base (name and flags), with `Trigger` still standalone (depends on T002).

**Checkpoint**: The suite compiles and passes with waypoints addressable as zone entities; nothing is visible in the editor yet.

---

## Phase 3: User Story 1 - Author waypoints in a zone (Priority: P1) 🎯 MVP

**Goal**: A **Waypoints** collapsing section in the Outliner lists the active zone's waypoints, adds one at the origin, and gives each row the shared select/*Del*/*Dup* actions, all recorded in the zone's undo history.

**Independent Test**: add a waypoint; move and rotate it with the gizmo and confirm the marker follows; duplicate it and confirm an independent copy; delete it and undo to confirm it returns with the same transform; switch zones and back and confirm it is still there.

### Implementation for User Story 1

- [X] T013 [US1] Add `Spawning.spawnWaypoint` to `editor/Spawning.fs`: call `Zone.spawnWaypoint zone entity`, then push the reversing `Spawn (ref, entity, Zone.bindingNone)` command and select the new waypoint (depends on T008, T010).
- [X] T014 [US1] Add the `EntityKind.Waypoint` case to `Spawning.duplicate` in `editor/Spawning.fs` (spawn plus a `Duplicate` command with `Zone.bindingNone`), leaving the `failwithf` for kinds with no entity (depends on T013).
- [X] T015 [US1] Add the `newWaypoint (name) (transform)` base-entity builder and the **Waypoints** `collapsingHeader` after the **Triggers** header (the end of the entity sections) in `editor/Outliner.fs`: one row per live waypoint via the shared `entityRow` (`Del`/`Dup`), `(none)` when empty, and an *Add Waypoint* button spawning at the origin with `transformIdentity` (depends on T013).
- [X] T016 [US1] Add waypoint cases to `test/SpawningTests.fs`: duplicating a waypoint leaves the source live, selects a distinct live copy, and pushes the reversing `Duplicate`; deleting a waypoint then undoing restores its transform at the recorded slot; duplicating `Environment`/`Trigger` still raises (depends on T014).

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - Place a waypoint where the camera is looking (Priority: P2)

**Goal**: A second add action seeds the new waypoint's position and facing from the editor camera.

**Independent Test**: frame the camera at a distinctive position and direction, press *Add at Camera*, and confirm the new waypoint's position matches the camera and its facing arrow points along the view direction.

### Implementation for User Story 2

- [X] T017 [US2] Add the *Add at Camera* button beside *Add Waypoint* in the Waypoints section of `editor/Outliner.fs`, spawning `newWaypoint "Waypoint" { transformIdentity with Position = camera.Position; Rotation = Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateWorld(camera.Position, Camera.forward camera, camera.Up)) }`, and add `open System.Numerics` for the rotation (depends on T015).

**Checkpoint**: Both placements are available; US1 is unaffected.

---

## Phase 5: User Story 3 - See which way a waypoint faces (Priority: P3)

**Goal**: Every waypoint in the active zone is drawn as a marker with a facing shaft and arrow head, highlighted when selected.

**Independent Test**: place two waypoints with different rotations and confirm the arrows point different ways; select one and confirm it is drawn in the selection colour; rotate one to face straight up and confirm the head stays readable.

### Implementation for User Story 3

- [X] T018 [US3] Add waypoint markers to `Viewport.buildLines` in `editor/Viewport.fs`: a `wireframeBox` at the waypoint's position, a shaft to `position + forward * gizmoLen` with `forward = Vector3.Transform(-Vector3.UnitZ, rot)`, and four head barbs from the tip to `basePoint ± right` and `basePoint ± up`, where `basePoint = tip - forward * headLen` and `right`/`up` are `Vector3.Transform(Vector3.UnitX|UnitY, rot) * headRadius`; add `headLen` (0.25) and `headRadius` (0.12) beside `gizmoLen`, and use the selection colour when selected, white otherwise (depends on T005).

**Checkpoint**: All three stories work together; the marker reflects the transform edited in US1.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Whole-feature validation.

- [X] T019 Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj` and confirm the whole suite passes with the waypoint changes.
- [ ] T020 Walk the interactive scenarios 7–14 in `specs/026-waypoints/quickstart.md` in the editor (with a display) and confirm the origin and camera placements, the gizmo and undo behaviour, the marker and its arrow head at extreme orientations, selection from the list, inertness, zone-scoped persistence, and no accumulation. **Not run here: the agent environment has no display.**

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks all user stories**.
- **User Stories (Phase 3–5)**: all depend on Phase 2.
  - US1 has no dependency on US2/US3.
  - US2 depends on US1's section (T015).
  - US3 depends only on Phase 2's `tryEntityAt` (T005).
- **Polish (Phase 6)**: depends on the desired stories being complete.

### Within the Foundational phase

- T002 ∥ T003 (both `world/Entity.fs`, but disjoint edits).
- T004 depends on T002; T005 depends on T002 and T004.
- T006, T007, T008 depend on T005 (T008 on T002); T009 depends on T008; T010 depends on T004.
- T011 depends on T005–T010; T012 depends on T002.

### Within the User Story phases

- T013 → T014 → T016 (the same module, then its tests).
- T013 → T015 → T017 (the UI builds on the spawn helper).

### Parallel Opportunities

- T002 ∥ T003.
- T012 ∥ (T006–T010): `test/EntityTests.fs` is a different file from `world/Zone.fs`.
- T018 ∥ (T013–T017): `editor/Viewport.fs` is a different file from `editor/Spawning.fs` and `editor/Outliner.fs`.

```bash
# After T005, the zone operations and the entity test are on different files:
Task: "Add the waypoint case to Zone.setTransform in world/Zone.fs"
Task: "Add the waypoint case to Zone.removeEntity in world/Zone.fs"
Task: "Update test/EntityTests.fs for the composed Waypoint base"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational) — the waypoint entity and its zone storage.
2. Complete Phase 3 (US1) — the Waypoints section with add/select/duplicate/delete and undo.
3. **Stop and validate**: run `test/ZoneTests.fs` and `test/SpawningTests.fs`, then place and edit a waypoint from the Outliner.

### Incremental Delivery

1. Foundational → waypoints are zone entities with stable refs; nothing visible.
2. US1 → author waypoints from the Outliner (MVP).
3. US2 → stamp a waypoint where the camera is looking.
4. US3 → every waypoint shows its facing in the viewport.

---

## Notes

- [P] tasks touch different files and have no incomplete dependencies.
- No new source module is added: the waypoint record is a variant of an existing type, its storage is a slot map on `Zone`, and the UI is a section in the existing Outliner.
- A waypoint has no body, renderer, or asset binding, so nothing in `physics/` or `render/` changes, and delete/undo need no release step.
- Verify `dotnet test` passes after each phase; commit per task or logical group.
