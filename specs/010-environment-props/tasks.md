# Tasks: Environment + Props

**Status**: Complete — all phases implemented and validated (`dotnet build` clean,
58 tests pass, `dotnet run` reaches the 60 fps frame loop).

**Input**: Design documents from `/specs/010-environment-props/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md,
contracts/environment-props.md, quickstart.md

**Tests**: Included — `test/GltfTests.fs` (parser) and the updated
`test/AssetsTests.fs` (environment/prop asset loading) cover the pure logic;
GPU/visual results are validated manually via `quickstart.md`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1/US2/US3)

## Phase 1: Setup

**Purpose**: Confirm the baseline.

- [ ] T001 Verify baseline: `dotnet build` and `dotnet test` succeed before changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The parser, the static mesh path, the environment/prop asset + entity
plumbing, and the pipeline wiring every story builds on.

- [ ] T002 [P] Add `core/Gltf.fs` — the scoped glTF 2.0 parser (buffers/views/
  accessors, `float32` POSITION/NORMAL/TEXCOORD_0, u8/u16/u32 indices, per-submesh
  `baseColorFactor`, node transforms baked to world space, flat `float32[]`/`int[]`
  output, loud-fail on unsupported features) — and insert it in `fsharp-game.fsproj`
  after `core/Iqm.fs`.
- [ ] T003 [P] Add `test/GltfTests.fs` (accessor/buffer-view decode, index
  widening u8/u16→u32, node-transform baking, missing-POSITION/unsupported-attribute/
  non-triangle/sparse/skin failures) and register it in `test/fsharp-game.Tests.fsproj`.
- [ ] T004 Add `createStaticPipeline` to `render/Gpu.fs` (position/normal/uv,
  24-byte stride, depth-tested triangle pipeline, no blend attributes/storage
  buffer).
- [ ] T005 Add `render/StaticMesh.fs` (24-byte stride vertex interleave, static
  vertex+index buffers, `PerDraw` uniform push + indexed draw) and insert it after
  `render/Mesh.fs`.
- [ ] T006 [P] Add `shaders/static.vert` + `shaders/static.frag` (directional
  Lambert, position/normal/uv inputs, `PerDraw` uniform, no skinning), compile to
  `static.vert.spv`/`static.frag.spv`, and extend the `CopyShaders` target in
  `fsharp-game.fsproj`.
- [ ] T007 Add `world/EnvironmentAsset.fs` (`EnvironmentAsset` = `Id`/`Name`/
  `ModelPath`/`Orientation`/`Submeshes : StaticMesh array`, shared by environments
  and props) and insert it after `world/ActorRender.fs`.
- [ ] T008 Add `world/EnvironmentRender.fs` (per-instance `EnvironmentRender` with
  `create`/`draw`/`release`, `OrientationMatrix`, submesh iteration with per-submesh
  `BaseColor`) and insert it after `world/EnvironmentAsset.fs`.
- [ ] T009 Rework `world/Assets.fs`: extend the manifest with `Model` +
  `OrientationDegrees`; change `Props`/`Environments` to
  `Dictionary<string, EnvironmentAsset.EnvironmentAsset>`; eagerly load
  environment/prop glTF (parse + upload) in `scan`; add `createEnvironmentRender`.
- [ ] T010 Rework `world/Entity.fs`: `Prop` gains `AssetId` + `mutable Renderer`;
  `Environment` gains `AssetId` + `mutable Renderer`.
- [ ] T011 Rework `world/Zone.fs`: `spawnProp` carries `assetId` + creates the
  renderer; add `setEnvironment`; `setActorWithAsset` creates an actor *or* prop
  renderer; `removeEntity` releases a prop's renderer; `assetIdAt` returns the prop
  `AssetId` too.
- [ ] T012 Add `StaticPipeline` to the `render/Gpu.fs` `Gpu` struct (`Gpu.create`/
  `destroy`) and thread it through `Engine.fs` → `ScreenManager.init` →
  `GameplayState.create` → `GameplayState.StaticPipeline`.
- [ ] T013 Update `test/AssetsTests.fs`, `test/EntityTests.fs`, and
  `test/CommandTests.fs` for the environment/prop asset + entity refactor.

**Checkpoint**: compiles; the parser is unit-tested; no environment/prop is drawn
yet.

---

## Phase 3: User Story 1 - A static environment renders in the viewport (Priority: P1) 🎯 MVP

**Goal**: a spawned environment draws as static geometry via the new static path.

**Independent Test**: spawn the `island` environment; its static meshes (not a
wireframe box) are visible in the viewport at the manifest orientation.

### Implementation for User Story 1

- [ ] T014 [US1] Add the environment model files under `assets/environments/`
  (copy `Island.gltf` + `Island.bin` from odin-game) and update
  `assets/environments/island.manifest` to `[[Id "island"] [Name "Island"]
  [Model "environments/Island.gltf"] [OrientationDegrees (... )]]`.
- [ ] T015 [US1] In `editor/AssetBrowser.fs`, list environments and spawn them via
  `Zone.setEnvironment` on click.
- [ ] T016 [US1] In `screen/GameplayScreen.fs` `renderScene`, after the debug grid,
  draw the environment (if it has a renderer) via `EnvironmentRender.draw` with
  `state.StaticPipeline`.

**Checkpoint**: the environment is visible as a static, flat/Lambert-lit mesh.

---

## Phase 4: User Story 2 - Prop assets spawn and render (Priority: P2)

**Goal**: props spawn into the zone's props slot map and render at their entity
transforms, sharing one loaded model per asset.

**Independent Test**: spawn two `beech` props at different positions; both render,
share one model, and differ only in transform.

### Implementation for User Story 2

- [ ] T017 [US2] Add the prop model files under `assets/props/` (copy `Beech.gltf`
  + `Beech.bin` from odin-game) and update the `beech`/`oak`/`oak2` manifests with
  a `Model` field.
- [ ] T018 [US2] In `editor/AssetBrowser.fs`, spawn props via `Zone.spawnProp` with
  the selected prop asset id (and push a `Spawn` command carrying that id).
- [ ] T019 [US2] In `screen/GameplayScreen.fs` `renderScene`, draw each alive prop
  in `Zone.Environment.Props` via `EnvironmentRender.draw` with its entity
  transform and `state.StaticPipeline`.
- [ ] T020 [US2] Verify prop undo/redo preserves the model: `Zone.setActorWithAsset`
  re-creates the prop renderer and `Zone.assetIdAt` returns the prop id (so
  `Duplicate`/`Delete`/undo carry it through `Command`).

**Checkpoint**: props render and are selectable/transformable/undoable like actors.

---

## Phase 5: User Story 3 - Environment, props, and actors render together (Priority: P3)

**Goal**: environment + props + the animated actor compose in one scene pass with
correct depth ordering and no regression to the debug overlays.

**Independent Test**: spawn an environment + a prop + Akane, then select and move
the prop; all three render together, depth-sort correctly, and the grid/gizmos
remain visible.

### Implementation for User Story 3

- [ ] T021 [US3] In `screen/GameplayScreen.fs` `renderScene`, draw in order grid →
  environment → props → actors, all within the existing scene pass (depth tested).
- [ ] T022 [US3] Confirm the existing selection/gizmo/undo flow works on props (no
  changes to `editor/Viewport.fs`/`Selection.fs` expected — they already address
  props via the slot map); fix any regression found.

**Checkpoint**: the full phase result — static environment + props + animated actor
visible together.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T023 Run `quickstart.md` end-to-end (`dotnet run`): confirm the environment,
  props, and actor all render with correct depth; fix any render/depth bug found.
- [ ] T024 Confirm `dotnet build` is clean and `dotnet test` passes (existing suite
  + glTF parser + asset loading).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup. `core/Gltf.fs` (T002) → `GltfTests`
  (T003); `createStaticPipeline` (T004) → `StaticMesh` (T005) →
  `EnvironmentAsset` (T007) → `EnvironmentRender` (T008) → `Assets` (T009) →
  `Entity`/`Zone` (T010/T011); the static pipeline wiring (T012) depends on T004;
  shaders (T006) are independent of the F# modules.
- **US1 (Phase 3)**: depends on Foundational (T002/T004/T005/T007/T008/T009/T011/
  T012) plus the asset files (T014).
- **US2 (Phase 4)**: depends on Foundational + US1's scene-pass draw path.
- **US3 (Phase 5)**: depends on US1 + US2.
- **Polish (Phase 6)**: depends on US1 + US2 + US3.

### Parallel Opportunities

- T002/T003 and T006 are parallel (different files). T005 depends on T004; T007 on
  T005; T008 on T007; T009 on T008; T010/T011 on T009.
- T013 (test updates) is parallel with T014–T019 once the refactor compiles.
- T015/T016 (US1) and T018/T019 (US2) touch different files (`AssetBrowser.fs` vs
  `GameplayScreen.fs`) and can proceed once their foundational prerequisites exist.

## Implementation Strategy

### MVP First (User Story 1)

1. Foundational (T002–T013) → parser, static path, and asset/entity plumbing
   compile and test.
2. US1 (T014–T016) → the environment renders as a static mesh (first visible
   result).

Then US2 (T017–T020) → props render, US3 (T021–T022) → composition, then Polish
(T023–T024) → end-to-end verification.
