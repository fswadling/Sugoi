# Tasks: Cel-Shaded Actors

**Input**: Design documents from `/specs/040-actor-cel-shading/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: The spec asks for the pure parts to be covered headlessly (plan.md `Testing`), so one test task is included (US3). The shading, the cutouts, the live sun response and the face constant-banding need a GPU, a window and a person, and are the quickstart walkthrough.

**Organization**: Tasks are grouped by user story. The feature is small and all four stories share **one shader pipeline**, so the phases are ordered by priority and the same file (`shaders/cel.frag`) is extended by later phases — each phase is a demonstrable increment, and the note under each says which acceptance it delivers. The actors-only structural fact (plan.md `Summary`) means the environment/prop path is never touched.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- Include exact file paths in descriptions

## Path Conventions

Single-project F# repository. All paths are relative to the repository root (`fsharp-game/`):

- `shaders/` — GLSL + committed SPIR-V; `render/` — GPU layer; `world/` — game-domain modules;
  `screen/` — the screens; `test/` — the xUnit project; `docs/` — reference docs.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Stand up the cel shader and its build entry so every later step builds green.

- [X] T001 [P] In `fsharp-game.fsproj`, change the `CopyShaders` target: replace `shaders/mesh.frag.spv` with `shaders/cel.frag.spv`.
- [X] T002 [P] Create `shaders/cel.frag` — `#version 450`, the `CelUniform` block (contracts/cel-actor.md: `uBaseColor`, `uLightDir`, `uLightColor`, `uAmbient`, `uFace`; 80 bytes), the three constants (`RAMP_THRESHOLD = 0.5`, `RAMP_SOFTNESS = 0.05`, `ALPHA_CUTOFF = 0.5`), the single `uAlbedoMap` sampler (`set = 2, binding = 0`), and a **pass-through body** (`outColor = vec4(srgbToLinear(texture(...).rgb) * uBaseColor.rgb, 1.0)`) so it compiles and links before the shading lands (unused std140 members keep their layout, so the 80-byte block stays valid); compile to `shaders/cel.frag.spv` with `glslc`.
- [X] T003 [P] Create `test/CelTests.fs` (module header + `open Xunit`) and add `<Compile Include="test/CelTests.fs" />` to `test/fsharp-game.Tests.fsproj`.

**Checkpoint**: `cel.frag` compiles to `cel.frag.spv`, the `.fsproj` copies it, and the test project has a home for the new tests. Nothing draws through it yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: The actor pipeline and its draw plumbing must exist before any story's shading can be seen.

- [X] T004 In `render/Gpu.fs`, retarget and rename the actor pipeline: `createMeshPipeline` → `createCelPipeline` (load `shaders/mesh.vert.spv` with `2, 2, 0` and `shaders/cel.frag.spv` with **`1, 0, 1`**), rename the `Gpu` field `MeshPipeline` → `CelPipeline`, and update `create`/`destroy`. The vertex layout, cull mode and depth state are unchanged. (Depends on T002.)
- [X] T005 In `render/Mesh.fs`: add `isFaceMeshName : string -> bool` (`= "Face"`, exact); add `IsFace : bool` to `Submesh`; in `Assets.loadActors` (`world/Assets.fs`) set each submesh's `IsFace = Mesh.isFaceMeshName mesh.Name` (the no-meshes fallback sets `false`); and change `draw` to **drop `cameraPos`** and **gain `faceDir : Vector3`**, write the 80-byte cel uniform per submesh (base colour + `light.Direction`/`Color`/`Ambient` + `(faceDir, isFace)`), push **80** bytes at fragment slot 0, and bind **one** sampler (`sub.Material.Bindings.[0]`). The vertex `PerDraw` push (192 bytes) and the index draw are unchanged.
- [X] T006 [P] In `world/ActorRender.fs`, add the pure `faceLightDir : Matrix4x4 -> Vector3` (data-model.md: local `+Z` under the model matrix, Y-flattened and normalized, then `+0.25` elevation) and pass `faceLightDir model` from `draw` (using the raw entity matrix, **before** the `upAxisFix` multiply). (Depends on T005.)
- [X] T007 [P] In `screen/GameplayScreen.fs`, pass `shared.Gpu.CelPipeline` to `ActorRender.draw` (was `shared.Gpu.MeshPipeline`) — one identifier. (Depends on T004.)

**Checkpoint**: actors draw through the cel pipeline (flat albedo, no banding yet); `dotnet build` and the suite are green. `ActorRender.draw` receives `light` unchanged and `Mesh.draw` carries it into the uniform.

---

## Phase 3: User Story 1 - The stylised two-tone cel look (Priority: P1) 🎯 MVP

**Goal**: an actor shades in two flat bands with a soft edge, coloured additively from the scene light — while the environment beside it keeps PBR.

**Independent Test**: spawn Akane next to ArtRoom: the actor shows a clean two-tone split, the environment shows PBR.

### Implementation for User Story 1

- [X] T008 [US1] In `shaders/cel.frag`, replace the pass-through body with the two-band shading (contracts/cel-actor.md): `L = normalize(uLightDir.xyz)`; `band = smoothstep(RAMP_THRESHOLD - RAMP_SOFTNESS, RAMP_THRESHOLD + RAMP_SOFTNESS, dot(normalize(vWorldNormal), L) * 0.5 + 0.5)`; `color = albedo * (uAmbient.rgb + uLightColor.rgb * band)`; output `pow(color, vec3(1.0/2.2))` (FR-002, FR-003, FR-005). Recompile `shaders/cel.frag.spv`.

**Checkpoint**: User Story 1 is fully functional and testable on its own — the actor reads as cel-shaded, the environment does not. This is the MVP.

---

## Phase 4: User Story 2 - The bands follow the scene light (Priority: P2)

**Goal**: the terminator sits where the sun does, and ambient lifts the shadow band.

**Independent Test**: change the sun's direction/colour and the ambient term; the actor's body band boundary and tones follow.

### Implementation for User Story 2

- [X] T009 [US2] **Verified, not built** (like 039's nearest-trigger resolution): FR-004 is delivered by the plumbing already in place — `ActorRender.draw` passes the scene `Light` and T005 writes `light.Direction`/`Color`/`Ambient` into the uniform, so the band is the scene sun and the shadow band is ambient-floored (FR-003). Confirm in the walkthrough that turning the sun moves the terminator and raising the ambient lightens the shade (`screen/GameplayScreen.fs`'s `light` is the single source). No code change beyond T005/T008 unless the walkthrough shows otherwise.

**Checkpoint**: the actor's body responds to the scene light.

---

## Phase 5: User Story 3 - The face keeps a constant, front-lit banding (Priority: P3)

**Goal**: the `Face` mesh is lit by an idealized front light, so its band boundary is constant as the actor turns and the sun moves; only the band *direction* is idealized (colours still from the scene).

**Independent Test**: turn the actor through a full circle and the sun around: the face's boundary does not move, the body's does; the face still takes the scene's colour/ambient.

### Tests for User Story 3

- [X] T010 [P] [US3] In `test/CelTests.fs`, cover the two pure parts (quickstart.md): `Mesh.isFaceMeshName` — `true` for `"Face"`, `false` for `"Body"`, `"Hair"`, `"face"`, `""` (exact match); `ActorRender.faceLightDir` — identity model → `(0, 0.25, 1)` after normalization (level `+Z` + elevation), a quarter turn about `Y` → level `+X` + elevation, and a non-degenerate unit-horizontal direction.

### Implementation for User Story 3

- [X] T011 [US3] In `shaders/cel.frag`, choose the band direction per mesh: `L = normalize(uFace.w > 0.5 ? uFace.xyz : uLightDir.xyz)`, so `Face` submeshes take the idealized light and every other mesh the scene sun (FR-007, FR-008, FR-009). The `uFace` value is already written per submesh by T005 and derived per actor by T006. Recompile `shaders/cel.frag.spv`.

**Checkpoint**: the face keeps constant banding while the body follows the sun.

---

## Phase 6: User Story 4 - Cutouts stay clean and nothing else changes (Priority: P4)

**Goal**: hair/eye cutout texels are discarded (no blend pass, no fringe), and the environment/prop/UI/editor paths are unchanged.

**Independent Test**: the actor's transparent hair/eye texels are absent, not fringed or blended; the environment, props, portraits, UI and editor render as before.

### Implementation for User Story 4

- [X] T012 [US4] In `shaders/cel.frag`, add the alpha-test discard before shading: `if (texture(uAlbedoMap, vUV).a * uBaseColor.a < ALPHA_CUTOFF) discard;` (FR-006). The shared opaque-white placeholder means a factor-only material never discards. Recompile `shaders/cel.frag.spv`.
- [X] T013 [US4] Delete `shaders/mesh.frag` and `shaders/mesh.frag.spv` (the actor-only PBR shader the cel one replaces, now unused — research D1); confirm `dotnet build` is green and grep that nothing references `mesh.frag` (FR-013).

**Checkpoint**: cutouts are clean and no PBR actor fragment shader remains.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T014 [P] Update `docs/RENDERING_NOTES.md` to record that the actor mesh pipeline is now the cel pipeline (`mesh.vert` + `cel.frag`, `Gpu.CelPipeline`), the `Face` mesh's idealized light, and the additive cel colour. (No `docs/PROJECT_STRUCTURE.md` change: no new module.)
- [X] T015 Run the [quickstart.md](./quickstart.md) walkthrough (steps 1–6) and the full `dotnet build` + `dotnet test`; record the outcome (the shading, cutouts and face banding need a GPU and a person, so this is the manual pass).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies; T001, T002 and T003 touch different files.
- **Foundational (Phase 2)**: T004 needs T002 (the `.spv` it loads); T005/T006/T007 follow; T007 needs T004.
- **US1 (Phase 3)**: T008 needs T002/T004/T005. This is the MVP.
- **US2 (Phase 4)**: T009 needs T005/T008.
- **US3 (Phase 5)**: T010 needs T005 (the field it tests) and T006 (the helper it tests); T011 needs T008 (the body it branches).
- **US4 (Phase 6)**: T012 needs T008; T013 needs T004 (nothing loads `mesh.frag` any more).
- **Polish (Phase 7)**: T014 after the code is settled; T015 last.

### Same-file constraint

- `shaders/cel.frag` is edited by **T002 → T008 → T011 → T012** — sequential (the shader is built up across the phases; each recompiles the `.spv`).
- `render/Mesh.fs` and `world/Assets.fs` are edited by T005 — one task.
- `test/CelTests.fs` is edited by T003 → T010 — sequential.
- `fsharp-game.fsproj` (T001) and `test/fsharp-game.Tests.fsproj` (T003) are different files.

### Parallel Opportunities

- T001 and T003 (different `.fsproj` files); T002 depends on neither.
- T006 (`world/ActorRender.fs`) and T007 (`screen/GameplayScreen.fs`) touch different files and can follow T005/T004 independently.
- T010 (`test/CelTests.fs`) can be written once T005 lands, alongside T008.
- T014 (docs) is independent of the code once the shapes are settled.

---

## Implementation Strategy

### MVP

Phases 1–3 (T001–T008) are the MVP: an actor renders with the two-band additive cel look from the
scene light while the environment keeps PBR. That alone satisfies US1.

### Incremental Delivery

1. **Setup + Foundational** (T001–T007) — actors draw through the cel pipeline (flat albedo); the build and suite stay green.
2. **US1** (T008) — the two-tone look; demonstrate an actor beside the PBR environment.
3. **US2** (T009) — confirm the body follows the sun/ambient.
4. **US3** (T010–T011) — the `Face` idealization; demonstrate a turning head with a fixed band.
5. **US4** (T012–T013) — clean cutouts, and the PBR actor shader removed.
6. **Polish** (T014–T015) — docs and the walkthrough.

---

## Notes

- **Actors only; the environment/prop path is untouched.** The `StaticPipeline` and `static.*` shaders are never modified (plan.md `Summary`); this is why the feature can retarget the *existing* actor pipeline instead of adding one (research D1).
- **One sampler, one uniform block.** `Texture.Material` is unchanged; cel reads `Bindings.[0]` (albedo) and a lean 80-byte block (research D2, D3).
- **The `Face` flag is derived at load**, from the IQM mesh name the parser already reads — no re-read at draw time and no new authored field (research D5, FR-011).
- **The face light is derived from the transform**, matching the reference's `{sin yaw, 0.25, cos yaw}` and the project's own `Systems.forwardOf`/`yawOf` convention (research D6).
- **FR-004 (bands follow the sun) is verified, not built** — see T009; the light wiring is T005.
- **FR-010 and SC-005 are structural** — the cel path reuses `mesh.vert` (skinning/morphs) and a reused uniform byte array, so an actor's pose is unchanged and the draw allocates nothing; T015 confirms both.
- **No per-frame allocation (FR-011).** `IsFace` is a struct field; the uniform bytes array is reused; the face light is a `Vector3`.
- **Shadows, outlines, rim, specular/matcap, ramp textures and per-actor parameters are out of scope** (spec Assumptions; research D8).
