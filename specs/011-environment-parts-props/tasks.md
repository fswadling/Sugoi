# Tasks: Environment Parts as Props

**Input**: Design documents from `/specs/011-environment-parts-props/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md,
contracts/environment-parts-props.md, quickstart.md

**Tests**: Included — `test/GltfTests.fs` (parts/local-space) and updated
`test/{AssetsTests,EntityTests,CommandTests}.fs` cover the pure logic; GPU/visual
results are validated manually via `quickstart.md`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1/US2/US3)

## Phase 1: Setup

**Purpose**: Confirm the baseline.

- [X] T001 Verify baseline: `dotnet build` and `dotnet test` succeed on the phase-4
  commit before changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The parser emits local-space submeshes + parts, and the asset/entity/
renderer plumbing can represent and draw a submesh range.

- [X] T002 [P] Extend `core/Gltf.fs`: add `Part` (`Name`/`HierarchyPath`/`World :
  Matrix4x4`/`SubmeshIndex`/`SubmeshCount`) and a `Parts` field on `Model`; stop
  baking node transforms into positions/normals (keep submeshes local-space);
  record one part per mesh-bearing node during the scene walk with its accumulated
  world matrix and appended submesh range.
- [X] T003 [P] Extend `test/GltfTests.fs`: assert `Island`/`Beech` still parse, and
  add assertions for local-space positions (un-scaled `Island` plane) and part
  emission (one part per mesh node, correct `World` transform and submesh range,
  transform-only nodes contribute no part).
- [X] T004 Extend `world/EnvironmentAsset.fs`: add `EnvironmentPart`
  (`Name`/`HierarchyPath`/`World`/`SubmeshIndex`/`SubmeshCount`) and a `Parts` field
  on `EnvironmentAsset`.
- [X] T005 Extend `world/EnvironmentRender.fs`: add `SubmeshIndex`/`SubmeshCount`
  to `EnvironmentRender`; change `create` to take an orientation quaternion plus a
  submesh range; make `draw` iterate only that range (composing
  `model * OrientationMatrix`).
- [X] T006 Extend `world/Entity.fs`: `Prop` gains `SubmeshIndex`/`SubmeshCount`;
  `Environment` drops `Renderer`, gains `Orientation : Quaternion`.
- [X] T007 Rework `world/Assets.fs`: `loadStaticModel` copies `Gltf.Model.Parts`
  into `EnvironmentAsset.Parts` for environments (empty for props); replace
  `createEnvironmentRender` with `createPropRender` (full range, identity
  orientation) and `createPartRender` (part range, environment orientation).
- [X] T008 Rework `world/Zone.fs`: `loadEnvironment` decomposes each part's `World`
  via `Matrix4x4.Decompose` and spawns one prop per part (`AssetId = None`, part's
  submesh range, decomposed transform); add `spawnPropBinding`/`bindingAt` and an
  `AssetBinding` type; `setActorWithAsset`/`removeEntity` preserve the submesh
  range alongside the asset id.
- [X] T009 Update `screen/GameplayScreen.fs` `renderScene`: remove the direct
  environment draw (the environment draws via its parts in the props slot map);
  the props loop already covers placed props and imported parts.
- [X] T010 Update `test/AssetsTests.fs`, `test/EntityTests.fs`, and
  `test/CommandTests.fs` for the `Prop` submesh-range + environment-part refactor.

**Checkpoint**: compiles; the parser emits parts; no environment is drawn yet via
the new path.

---

## Phase 3: User Story 1 - Importing an environment spawns its parts as props (Priority: P1) 🎯 MVP

**Goal**: spawning a multi-node environment creates one prop per mesh-bearing node
at its authored transform.

**Independent Test**: import `ArtRoom`; the outliner shows many props, each at its
authored position, and the environment entity draws no geometry.

### Implementation for User Story 1

- [X] T011 [US1] Copy `ArtRoom.gltf` + `ArtRoom.bin` from odin-game's
  `assets/environments/ArtRoom/` into `assets/environments/ArtRoom/`, and add
  `assets/environments/artroom.manifest` (`[[Id "artroom"] [Name "ArtRoom"]
  [Model "environments/ArtRoom/ArtRoom.gltf"]]`).
- [X] T012 [US1] In `editor/AssetBrowser.fs`, confirm environments spawn via
  `Zone.loadEnvironment` (already wired in phase 4); verify the new part-spawn path
  is exercised on click.
- [X] T013 [US1] In `world/Zone.fs`/`world/Entity.fs`, ensure imported parts carry
  `AssetId = None` while placed props keep `AssetId = Some id`.

**Checkpoint**: `artroom` imports as many props as it has mesh-bearing nodes.

---

## Phase 4: User Story 2 - Imported parts are independently editable (Priority: P2)

**Goal**: an imported part selects, transforms, and undoes like any prop; moving
one part doesn't move others.

**Independent Test**: import `ArtRoom`, select one desk, move it, undo; only that
desk moves and undo restores it.

### Implementation for User Story 2

- [X] T014 [US2] Confirm the existing selection/gizmo flow picks imported parts
  (they are ordinary props in `Zone.Props`); fix any
  `editor/Viewport.fs`/`editor/Selection.fs` issue where a `None` `AssetId` is
  mishandled.
- [X] T015 [US2] Confirm `state/Command.fs` spawn/delete/duplicate/transform undo
  preserves the prop's `SubmeshIndex`/`SubmeshCount` and `AssetId = None` (so an
  imported part's range and part-ness survive undo/redo).
- [X] T016 [US2] In `screen/GameplayScreen.fs`, confirm each part draws with its
  own entity transform and depth-sorts correctly against siblings/actors.

**Checkpoint**: imported parts are selectable/transformable/undoable.

---

## Phase 5: User Story 3 - Composition with placed props and actors (Priority: P3)

**Goal**: imported parts, placed prop assets, and the actor compose in one scene
pass without regression.

**Independent Test**: import an environment, spawn a `beech` prop, spawn Akane; all
render with correct depth, the `beech` prop is still one entity, and grid/gizmos
remain visible.

### Implementation for User Story 3

- [X] T017 [US3] In `screen/GameplayScreen.fs`, verify draw order (grid → props →
  actors) and that placed props (full-range renderers) and imported parts
  (range renderers) coexist in the same props loop.
- [X] T018 [US3] Verify re-spawn/replace: `Zone.loadEnvironment` drops the old
  environment's parts and keeps placed props before spawning the new parts (no
  stale parts, placed props survive).

**Checkpoint**: the full result — imported furniture + placed props + actor visible
together.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T019 Run `quickstart.md` end-to-end (`dotnet run`): import `artroom` +
  `island`, spawn a `beech` prop and Akane, move/undo a desk, undo/re-spawn an
  environment; fix any render/depth/undo bug found.
- [X] T020 Confirm `dotnet build` is clean and `dotnet test` passes (existing suite
  + new part/local-space assertions).

## Phase 7: Post-implementation fixes (added after first pass)

- [X] T021 Fix node-transform composition: `Gltf.bake` must accumulate
  `local * parentWorld` (row-vector order), not `parentWorld * local` — the old
  order scattered `ArtRoom`'s furniture. Regression test in `GltfTests.fs`.
- [X] T022 Deduplicate instanced meshes: emit submeshes per unique mesh (mesh-index
  order) and have parts reference ranges, instead of re-reading geometry per node.
- [X] T023 Make environment spawn undoable: add `Command.SetEnvironment`
  (`PreviousAssetId * NewAssetId`); `Zone.loadEnvironment` preserves placed props
  and `Zone.clearEnvironment` clears. Push the command from `editor/AssetBrowser.fs`;
  tests in `CommandTests.fs`.
- [X] T024 Divide `editor/AssetBrowser.fs` into Actors / Environments / Props
  sections.
- [X] T025 Add the loaded-environment indicator to `editor/Outliner.fs`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: `core/Gltf.fs` (T002) → `GltfTests` (T003);
  `EnvironmentAsset` (T004) → `EnvironmentRender` (T005) → `Entity` (T006) →
  `Assets` (T007) → `Zone` (T008) → `GameplayScreen` (T009) → test updates (T010).
- **US1 (Phase 3)**: depends on Foundational + the `ArtRoom` asset (T011).
- **US2 (Phase 4)**: depends on US1 (props exist to select).
- **US3 (Phase 5)**: depends on US1 + US2.
- **Polish (Phase 6)**: depends on US1 + US2 + US3.

### Parallel Opportunities

- T002/T003 are parallel (different files). T004→T005→T006→T007→T008→T009 are
  sequential (each depends on the prior); T010 (test updates) is parallel once the
  refactor compiles.
- T011 (asset) is parallel with T014–T016 once US1's spawn path exists.

## Implementation Strategy

### MVP First (User Story 1)

1. Foundational (T002–T010) → parser emits parts, renderer draws ranges, and the
   asset/entity/zone plumbing compiles and tests.
2. US1 (T011–T013) → `artroom` imports as props (first visible result).

Then US2 (T014–T016) → parts are editable, US3 (T017–T018) → composition, then
Polish (T019–T020) → end-to-end verification.

## Phase 8: Convergence

- [X] T026 Remove the now-dead `Environment.releaseRenderer` in `world/Entity.fs`
  (Environment no longer owns a renderer, so the no-op has no callers), or
  justify keeping it as a seam per Constitution I (unrequested)
