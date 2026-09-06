# Tasks: PBR + Lighting

**Input**: Design documents from `/specs/012-pbr-lighting/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md,
contracts/pbr-lighting.md, quickstart.md

**Tests**: Included — `test/GltfTests.fs` (material/texture-reference parsing +
defaults + `data:` image rejection); `test/IqmTests.fs` (IQM material string);
GPU/visual results validated manually via `quickstart.md`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1/US2/US3)

## Phase 1: Setup

**Purpose**: Confirm the baseline.

- [ ] T001 Verify baseline: `dotnet build` and `dotnet test` succeed on the phase-11
  commit before changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The parser emits PBR materials, textures decode/upload, both shaders
shade with GGX, and the material/light plumbing exists.

- [X] T002 [P] Extend `core/Gltf.fs`: parse per material `pbrMetallicRoughness`
  (`baseColorFactor`/`metallicFactor`/`roughnessFactor` + `baseColorTexture`/
  `metallicRoughnessTexture`), `emissiveFactor`/`emissiveTexture`, and
  `normalTexture`; add `textures`/`images` parsing and a `TextureRef`
  + `Material` type; add `Materials : Material array` to `Model`. Samplers are
  not parsed (a fixed linear/repeat sampler is used). Fail loudly on embedded
  images (`bufferView` and `data:` URI).
- [X] T003 [P] Extend `test/GltfTests.fs`: assert factor defaults, texture
  reference resolution (`textures[].source` → `images[].uri`), and that a material
  with no texture ref produces `None`.
- [X] T004 Add `render/Texture.fs`: `Texture` (`Handle` + `Sampler`) and
  `load : device -> path -> Texture` via `SDL3.Image.LoadGPUTexture` +
  `SDL.CreateGPUSampler`; insert it in `fsharp-game.fsproj` after `render/Gpu.fs`.
- [X] T005 Extend `world/EnvironmentAsset.fs`: replace `StaticSubmesh.BaseColor`
  with a runtime `Material` (factors + loaded `Texture` handles + `has*` flags).
- [X] T006 Extend `world/Assets.fs`: after `Gltf.load`, resolve each submesh's
  material texture refs to `Texture.load` (URI-keyed cache) and build runtime
  materials; keep a default material for `Material = -1`.
- [X] T007 Add the texture image files under `assets/{environments,props}` using
  odin's per-asset subdirectory layout (`environments/<Name>/textures/`,
  `props/<Name>/textures/`), so glTF `images[].uri` resolves; update manifests if
  the layout changes.
- [X] T008 Rewrite `shaders/static.frag` (+ extend `static.vert`): GGX PBR with
  albedo/normal/metallic-roughness/emissive samplers + `has*` flags (metallic-
  roughness sampled `.g` for roughness, `.b` for metallic), sRGB decode/encode,
  sun/ambient/camera uniforms, and a per-draw normal matrix; compile SPIR-V and
  update `CopyShaders`.
- [X] T009 Rewrite `shaders/mesh.frag` (+ extend `mesh.vert`): same GGX PBR with
  skinning (bone palette) intact; compile SPIR-V and update `CopyShaders`.
- [X] T010 Add a `Light` record (sun direction/color + ambient) in
  `render/Texture.fs`, construct the value in `screen/GameplayScreen.fs`, and pass
  it + camera position into both renderers.
- [X] T011 Extend `world/EnvironmentRender.fs` and `world/ActorRender.fs`: push the
  material factors + light uniforms and bind the material's texture samplers;
  compute/upload the per-draw normal matrix.
- [X] T012 Give the actor a dielectric material: change `core/Iqm.fs`
  `Mesh.Material` from `int` to `string` (the mesh names an albedo texture), and
  in `world/Assets.fs` build per-submesh actor materials (albedo texture where
  named, else a default dielectric material via `Texture.defaultMaterial`).
- [X] T013 Update `test/IqmTests.fs` for the IQM `Mesh.Material` int→string change.

**Checkpoint**: compiles; materials + textures load; both shaders shade with GGX.

---

## Phase 3: User Story 1 - Static geometry renders with PBR materials (Priority: P1) 🎯 MVP

**Goal**: environments and props shade with metallic-roughness PBR from their glTF
materials.

**Independent Test**: import `artroom`; walls/floor/furniture show albedo + normal
+ roughness shading under the sun.

### Implementation for User Story 1

- [X] T014 [US1] In `screen/GameplayScreen.fs`/`EnvironmentRender.fs`, confirm each
  static submesh binds its material samplers and factors; the environment and props
  draw through the PBR static shader.
- [X] T015 [US1] Confirm a factor-only material (e.g. `island`) still shades from
  its factors, and a texture-less submesh doesn't bind/require a sampler.

**Checkpoint**: static env/props render PBR with textures + factor fallback.

---

## Phase 4: User Story 2 - The animated actor renders under the same PBR light (Priority: P2)

**Goal**: the skinned actor shades with the same BRDF/light using a default
dielectric material.

**Independent Test**: spawn Akane alongside a PBR environment; it is lit by the
same sun/ambient and keeps skinning.

### Implementation for User Story 2

- [X] T016 [US2] In `world/ActorRender.fs`, draw the actor through the skinned PBR
  shader with its default material and the shared `Light`.

**Checkpoint**: actor shades PBR and still skins/animes.

---

## Phase 5: User Story 3 - Emissive and factor-only materials behave correctly (Priority: P3)

**Goal**: emissive glows; non-uniform scale lights correctly via the normal matrix.

**Independent Test**: `artroom`'s lamp is emissive; a non-uniformly scaled prop
lights correctly.

### Implementation for User Story 3

- [X] T017 [US3] Verify emissive factor/texture adds light independent of the sun
  in the static shader.
- [X] T018 [US3] Verify the per-draw normal matrix (inverse-transpose) is used
  instead of `mat3(model)`, and a non-uniformly scaled prop shades correctly.

**Checkpoint**: emissive + normal-matrix correctness confirmed.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T019 Run `quickstart.md` end-to-end (`dotnet run`): import `artroom` +
  `island`, spawn a `beech` prop + Akane, check emissive/normal-matrix, and
  fail-loudly on a missing texture; fix any render bug found.
- [X] T020 Confirm `dotnet build` is clean and `dotnet test` passes (existing suite
  + material tests).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: `Gltf.fs` (T002) → `GltfTests` (T003);
  `Texture.fs` (T004) → `Assets` (T006); `EnvironmentAsset` (T005) →
  `EnvironmentRender`/`ActorRender` (T011); shaders (T008/T009) are independent of
  F#; light (T010) → renderers (T011); actor material (T012); test updates (T013).
- **US1 (Phase 3)**: depends on Foundational (T002/T004/T005/T006/T008/T010/T011).
- **US2 (Phase 4)**: depends on Foundational + US1 (shared light/BRDF).
- **US3 (Phase 5)**: depends on US1.
- **Polish (Phase 6)**: depends on US1 + US2 + US3.

### Parallel Opportunities

- T002/T003 and T008/T009 are parallel (different files). T004 → T006; T005 →
  T011; T010 → T011. T007 (assets) is independent of the F# modules.
- T013 (test updates) is parallel once the refactor compiles.

## Implementation Strategy

### MVP First (User Story 1)

1. Foundational (T002–T013) → parser emits materials, textures load, both shaders
   shade GGX, and the material/light plumbing compiles and tests.
2. US1 (T014–T015) → static env/props render PBR (first visible result).

Then US2 (T016) → actor PBR, US3 (T017–T018) → emissive + normal matrix, then
Polish (T019–T020) → end-to-end verification.
