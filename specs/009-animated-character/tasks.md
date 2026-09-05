# Tasks: Animated Character on Screen

**Input**: Design documents from `/specs/009-animated-character/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/animated-character.md, quickstart.md

**Tests**: Included — `test/IqmTests.fs` (parser) and `test/ActorTests.fs` (pose
sampling) cover the pure logic; GPU/visual results are validated manually via
`quickstart.md`. This is a retrospective task list: all tasks are already
complete in the working tree and marked `[X]`.

## Phase 1: Setup

**Purpose**: Confirm the baseline.

- [X] T001 Verify baseline: `dotnet build` and `dotnet test` succeed before changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The parser, the animation-sampling core, the GPU mesh path, and the
asset/manifest plumbing every story builds on.

- [X] T002 [P] Add `core/Iqm.fs` — the IQM v2 binary parser (header/meshes/vertex
  arrays/triangles/joints/poses/anims/frame channels, `float32` geometry + raw
  `byte` blend data, `PoseChannelOffsets`, loud-fail on unsupported kinds/formats)
  — and insert it in `fsharp-game.fsproj` after `core/SlotMap.fs`.
- [X] T003 [P] Add `test/IqmTests.fs` (header magic/version, vertex-array decode,
  raw blend index/weight decode, unsupported-kind/format failures) and register it
  in `test/fsharp-game.Tests.fsproj`.
- [X] T004 Add `render/Gpu.fs` (device/pass/buffer/pipeline wrappers, `BonesMax`,
  `createBuffer`/`createTransferBuffer`/`uploadBufferOnce`, `writeMatrix`/
  `writeVector4`, `createMeshPipeline`, depth texture) and insert it in the
  `render` cluster after `core/Input.fs`.
- [X] T005 Add `render/Mesh.fs` (interleave 40-byte stride vertex data, static
  vertex+index buffers, `PerDraw` uniform push + indexed draw) and insert it after
  `render/Gpu.fs`.
- [X] T006 [P] Add `shaders/mesh.vert` + `shaders/mesh.frag` (directional-Lambert,
  position/normal/uv/boneIds/weights inputs, a `PerDraw` uniform plus a `Bones`
  storage buffer, GPU skinning), compile to `mesh.vert.spv`/`mesh.frag.spv`, and
  extend the `CopyShaders` target in `fsharp-game.fsproj`.
- [X] T007 Add `world/Actor.fs` (`ActorModel`, `AnimationSlot`, `PoseFrame`,
  inverse-bind, `advance`/`advanceInPlace`, `samplePose`) and insert it in the
  `world` cluster after `render/DebugDraw.fs`.
- [X] T008 [P] Add `test/ActorTests.fs` (inverse bind, bind-pose local, skinning
  matrix = inverseBind · world, animation clock loop) and register it in
  `test/fsharp-game.Tests.fsproj`.
- [X] T009 Add `world/ActorAsset.fs` (shared per-asset `Id`/`Name`/`ModelPath`/
  `Mesh`/`Model`) and insert it after `world/Actor.fs`.
- [X] T010 Add `world/ActorRender.fs` (per-instance `ActorRender` with
  `create`/`advanceInPlace`/`upload`/`draw`/`release`, bone storage buffer + copy
  upload) and insert it after `world/ActorAsset.fs`.
- [X] T011 Rework `core/Assets.fs` into `world/Assets.fs` (`PropAsset`/
  `EnvironmentAsset`/`AssetManager`, `scan`, eager `loadActors`,
  `createActorRender`) and insert it after `world/ActorRender.fs`.
- [X] T012 Rework `core/Entity.fs` into `world/Entity.fs` (Actor gains `AssetId`
  + `mutable Renderer`, `Actor.releaseRenderer`) and `core/Zone.fs` into
  `world/Zone.fs` (`spawnActor`/`spawnProp`/`setActorWithAsset`/`removeEntity`/
  `entityAt`/`assetIdAt`/`setEntity`), inserting them after `world/Assets.fs`.
- [X] T013 Move `screen/Screen.fs`, `screen/SharedState.fs`, `screen/GameplayState.fs`
  and `editor/Command.fs`, `editor/EditorState.fs` into `state/`; put the
  `AssetManager` on `SharedState`, `Zone` + `MeshPipeline` on `GameplayState`, and
  rework `Command` so `Spawn`/`Delete`/`Duplicate` carry `AssetId`. Reorder the
  `.fsproj` to `core → render → world → state → editor → screen`.
- [X] T014 Update `test/AssetsTests.fs`, `test/CommandTests.fs`, and
  `test/EntityTests.fs` for the `AssetManager`/`Command`/`Actor` refactor.

**Checkpoint**: compiles; the parser and sampling are unit-tested; no mesh is
drawn yet.

---

## Phase 3: User Story 1 - A character mesh renders in the viewport (Priority: P1) 🎯 MVP

**Goal**: a spawned Akane actor draws as a lit mesh via the new GPU path.

**Independent Test**: spawn Akane from the asset browser; its geometry (not a
wireframe box) is visible and lit in the viewport.

### Implementation for User Story 1

- [X] T015 [US1] In `world/Assets.fs` `loadActors`, build the static GPU buffers
  via `Mesh.create` from the parsed IQM geometry and store them on the shared
  `ActorAsset`.
- [X] T016 [US1] In `editor/AssetBrowser.fs`, spawn actors via `Zone.spawnActor`
  with the selected asset's id (and push a `Spawn` command carrying `AssetId`).
- [X] T017 [US1] In `screen/GameplayScreen.fs` `renderScene`, after the debug grid
  draw, iterate the zone's alive actors and draw those with a renderer via
  `ActorRender.draw`, passing the entity transform.

**Checkpoint**: the character is visible as a static, lit 3D mesh.

---

## Phase 4: User Story 2 - The character is skinned by its skeleton (Priority: P2)

**Goal**: vertices are deformed by the bone hierarchy, not drawn in raw bind pose.

**Independent Test**: the rendered mesh is posed by a single sampled frame and
changes coherently when the sampled frame changes.

### Implementation for User Story 2

- [X] T018 [US2] In `world/ActorRender.fs` `upload`, sample the pose (frame 0),
  write `Pose.Skinning` into `BoneBytes`, and copy it to the bone storage buffer
  via a copy pass.
- [X] T019 [US2] In `shaders/mesh.vert`, blend `bones[boneId] * weight` so the
  skeleton deforms the mesh (visible as a non-degenerate pose vs. raw positions).

**Checkpoint**: the character is posed by its skeleton (single frame).

---

## Phase 5: User Story 3 - The character animates (Priority: P3)

**Goal**: the `Idle` clip advances and loops over time.

**Independent Test**: the character visibly animates and loops; multiple spawned
instances of Akane share the model but animate independently.

### Implementation for User Story 3

- [X] T020 [US3] In `world/ActorRender.fs` `create`, resolve the `Idle` clip by
  name (fall back to the first clip, then a disabled bind-pose slot) via
  `Actor.findAnimIndex`/`Actor.createSlot`.
- [X] T021 [US3] In `screen/GameplayScreen.fs` `update`, advance each actor's
  animation clock (`advanceActorAnimations` → `ActorRender.advanceInPlace`).
- [X] T022 [US3] In `screen/GameplayScreen.fs` `renderScene`, call
  `ActorRender.upload` each frame so the interpolated pose re-uploads the bone
  matrices.

**Checkpoint**: the `Idle` animation loops; the phase result is complete.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T023 Run `quickstart.md` end-to-end (`dotnet run`): confirm the mesh, the
  skinned pose, and the looping animation, plus the grid/gizmos still visible;
  fix any render/depth bug found.
- [X] T024 Confirm `dotnet build` is clean (2 expected native-pointer warnings)
  and `dotnet test` passes (54 tests).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup. T002 → T003 (tests need `Iqm`);
  T004 → T005 → T007 → T009 → T010 → T011 → T012 (dependency order in the
  `.fsproj`); T006 (shaders) is independent of the F# modules.
- **US1 (Phase 3)**: depends on Foundational (T002/T004/T005/T007 for parser +
  GPU path, T011/T012 for the asset/entity binding).
- **US2 (Phase 4)**: depends on US1 + T007 (`Actor.samplePose`).
- **US3 (Phase 5)**: depends on US2 + T007 (`advance`/`samplePose`).
- **Polish (Phase 6)**: depends on US1 + US2 + US3.

### Parallel Opportunities

- T002/T003, T006, and T008 are parallel (different files); T007 depends on T002,
  T008 on T007, T010 on T009.
- T011 (Assets) and T012 (Entity/Zone) touch different files but both depend on
  T010; they can run in parallel once `ActorRender`/`ActorAsset` exist.
- T014 (test updates) is parallel with T015–T017 once the refactor compiles.

## Implementation Strategy

### MVP First (User Story 1)

1. Foundational (T002–T014) → parser, animation core, GPU path, and asset/entity
   plumbing compile and test.
2. US1 (T015–T017) → the character renders as a static lit mesh (first visible
   result).

Then US2 (T018–T019) → skinning, US3 (T020–T022) → animation loop, then Polish
(T023–T024) → end-to-end verification.
