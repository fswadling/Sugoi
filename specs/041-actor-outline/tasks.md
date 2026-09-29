# Tasks: Actor Outline

**Input**: Design documents from `/specs/041-actor-outline/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: The spec asks for the pure part to be covered headlessly (plan.md `Testing`), so the `smoothNormals` tests are included (T003). The outline itself needs a GPU and is the quickstart walkthrough.

**Organization**: Tasks are grouped by user story. This feature is **one extra draw** over the geometry spec 040 already poses, so its four stories share the same shader and pipeline: the code lands across Setup, Foundational and US1, and the US2/US3 phases are the acceptance-and-tuning passes (a line's evenness and a hull's bleed-through are GPU-observable only). The note under each phase says what it delivers.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Single-project F# repository. Paths are relative to the repository root (`fsharp-game/`):

- `shaders/` — GLSL + committed SPIR-V; `core/` — dependency-free parsers/helpers; `render/` — GPU
  layer; `world/` — game-domain modules; `screen/` — the screens; `test/` — the xUnit project.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Stand up the build entry for the new shader so later steps build green.

- [X] T001 [P] In `fsharp-game.fsproj`, add `shaders/outline.frag.spv` to the `CopyShaders` target's `SourceFiles`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: The vertex stride, the smoothed normal, the pipelines and the draw plumbing must exist before any story's pass can be seen.

- [X] T002 [P] In `core/Iqm.fs`, add the pure `smoothNormals : float32 array -> float32 array -> float32 array` (data-model.md): bucket vertices by position rounded to `1e-4`, average each bucket's normals, normalize, and assign the direction to every member; a single-member bucket keeps its own normal; a degenerate (near-zero) sum falls back to the vertex's own normal, normalized. Never emit a zero direction.
- [X] T003 [P] In `test/IqmTests.fs`, cover `smoothNormals`: a cube corner (three coincident vertices with the three face normals) yields the corner diagonal for all three; well-separated vertices keep their own normalized normal; a single vertex is returned as its own; output length matches the input and every output is a unit vector. (Depends on T002.)
- [X] T004 In `render/Mesh.fs`: widen `interleave` to a **52-byte** stride, writing the smoothed normal as `Float3` at offset 40; add a `smoothNormals` argument to `create`; in `draw` push vertex uniform slot 2 with `uOutline = (0, 0, 0, 0)`; and add `drawOutline` (contracts/outline.md) that pushes `PerDraw` + `uOutline` (slot 2), binds the vertex/index buffers and **one** sampler, and issues the indexed draws with no fragment uniform. (Depends on T002.)
- [X] T005 In `render/Gpu.fs`: update `createCelPipeline`'s vertex input to **6 attributes / 52-byte stride** and load `mesh.vert.spv` with **(3, 2, 0)**; add `createOutlinePipeline` — `mesh.vert.spv` **(3, 2, 0)** + `outline.frag.spv` **(0, 0, 1)**, same 6-attribute layout, `CullMode.Front`, depth `LessOrEqual` with write, **depth bias off for now** (T012 turns it on), opaque; add `OutlinePipeline : Pipeline` to the `Gpu` record; build it in `create` and release it in `destroy`. (Depends on T004.)
- [X] T006 [P] In `world/Assets.fs`, `loadActors` computes `Iqm.smoothNormals model.Positions normals` (reusing the `normals` it already defaults) and passes it to `Mesh.create`. (Depends on T002, T004.)

**Checkpoint**: `dotnet build` is green with the wider vertex buffer and both pipelines defined; nothing outlines yet (the shaders are next).

---

## Phase 3: User Story 1 - An actor carries a drawn outline (Priority: P1) 🎯 MVP

**Goal**: an actor is drawn with a world-space dark edge at its silhouette, following its pose, masked by its cutouts, while the environment stays PBR and un-outlined.

**Independent Test**: spawn Akane beside the ArtRoom; a dark continuous edge traces her, and nothing else is outlined.

### Implementation for User Story 1

- [X] T007 [US1] In `shaders/mesh.vert`, add `layout(location = 5) in vec3 aSmoothNormal` and the vertex uniform slot 2 `OutlineParams` (`set = 1, binding = 2`, `vec4 uOutline`); transform the smoothed normal like the surface normal (bone blend + `normalMatrix`, plus the same morph normal deltas); and offset the world position along that smoothed normal by `uOutline.x` when `uOutline.x > 0` (the world-space width), before projecting. Recompile `shaders/mesh.vert.spv` with `glslc`. (Depends on T004.)
- [X] T008 [US1] Create `shaders/outline.frag` (contracts/outline.md): one `uAlbedoMap` sampler, no uniform buffers, a constant `OUTLINE_COLOR`, and `discard` when `texture(uAlbedoMap, vUV).a < 0.5`; output the constant colour. Compile `shaders/outline.frag.spv` with `glslc`. (Depends on T001.)
- [X] T009 [US1] In `world/ActorRender.fs`, add the `outlineWidth` (world) constant and change `draw` to take the outline pipeline and issue **two** passes after the shared bone/morph bindings: `Mesh.drawOutline … outlinePipeline (upAxisFix * model) outlineWidth`, then the existing cel `Mesh.draw …`. (Depends on T004, T005, T007.)
- [X] T010 [US1] In `screen/GameplayScreen.fs`, pass `shared.Gpu.OutlinePipeline` to `ActorRender.draw`. (Depends on T005, T009.)

**Checkpoint**: User Story 1 is functional on its own — an actor is outlined, the environment is not. This is the MVP.

---

## Phase 4: User Story 2 - The outline follows the actor as a physical line (Priority: P2)

**Goal**: the outline deforms with the animation and shrinks with distance exactly as the actor does.

**Independent Test**: play the jog/idle clips and a morph and watch the outline track the limbs; zoom in and out and confirm the line shrinks with distance exactly as the actor does.

### Implementation for User Story 2

- [X] T011 [US2] Verify and tune: confirm the outline deforms with clips and morphs (it shares the posed mesh — T007/T009) and shrinks with distance exactly as the actor does; adjust `outlineWidth` (T009) until the thickness is right. Recompile `shaders/mesh.vert.spv` if it changes. (Depends on T009, T010.)

**Checkpoint**: the outline tracks the pose and reads as an even ink line.

---

## Phase 5: User Story 3 - The outline closes at creases and does not leak or ghost (Priority: P3)

**Goal**: unbroken at hard creases, following the hair/eye cutouts, and free of bleed-through on thin parts.

**Independent Test**: turn Akane fully; the hair and body creases show no gaps, no outline boxes the transparent texels, and no dark bleed crosses a thin part (an arm against the torso).

### Implementation for User Story 3

- [X] T012 [US3] In `render/Gpu.fs`, enable and tune the outline pipeline's depth bias (`EnableDepthBias = true` plus a small `DepthBiasConstantFactor`/`DepthBiasSlopeFactor`) until the hull no longer bleeds through thin or concave geometry, without thinning the silhouette. (Depends on T005.)
- [X] T013 [US3] Verify the gap-free result (T002's averaged normals + T007) at the hair and body creases, and the cutout masking (T008) on the hair/eye textures; record the outcome. (Depends on T007, T008, T012.)

**Checkpoint**: the outline is continuous, cutout-clean and leak-free.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T014 [P] Update `docs/RENDERING_NOTES.md` with a §14 for the actor outline pass (inverted hull, the averaged extrusion direction, the front-cull/depth-bias pipeline, world-space extrusion).
- [X] T015 Run the [quickstart.md](./quickstart.md) walkthrough (steps 1–6) and the full `dotnet build` + `dotnet test`; record the outcome (the outline, its width and its gap-freedom need a GPU and a person).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: T002/T003 are independent; T004 needs T002; T005 needs T004; T006 needs T002/T004.
- **US1 (Phase 3)**: T007 needs T004; T008 needs T001; T009 needs T004/T005/T007; T010 needs T005/T009. This is the MVP.
- **US2 (Phase 4)**: T011 needs T009/T010.
- **US3 (Phase 5)**: T012 needs T005; T013 needs T007/T008/T012.
- **Polish (Phase 6)**: T014 after the code is settled; T015 last.

### Same-file constraint

- `render/Gpu.fs` is edited by **T005 → T012** (bias off, then enabled) — sequential.
- `shaders/mesh.vert` is edited by **T007 → T011** (if the width scale is tuned) — sequential, recompiling the `.spv`.
- Each other task edits a distinct file.

### Parallel Opportunities

- T002 and T003 can be written together (a pure function and its tests); T006 is independent once T002/T004 land.
- T008 (`shaders/outline.frag`) is independent of T007 (`shaders/mesh.vert`) — different files.
- T014 (docs) is independent of the code once the shapes are settled.

---

## Implementation Strategy

### MVP

Phases 1–3 (T001–T010) are the MVP: an actor is drawn with a world-space silhouette outline that
follows its skinned pose and respects its cutouts, while the environment stays PBR.

### Incremental Delivery

1. **Setup + Foundational** (T001–T006) — the wider vertex buffer with the averaged normals and both pipelines; build green, nothing outlined yet.
2. **US1** (T007–T010) — the outline pass; demonstrate an outlined actor.
3. **US2** (T011) — the width/pose confirmation and tuning.
4. **US3** (T012–T013) — depth bias and the gap-free/cutout confirmation.
5. **Polish** (T014–T015) — docs and the walkthrough.

---

## Notes

- **The averaged extrusion direction is the feature.** Akane is ~79% hard-edged and her hair ~100% (spec Origin); `Iqm.smoothNormals` (T002) is what stops the hull tearing open, so its tests (T003) are not optional.
- **`mesh.vert` is shared** by the cel and outline pipelines (research D2), so the surface pass also pushes the (zero-width) outline uniform (T004) — the deliberate trade against duplicating the skinning.
- **The width is a fixed world-space shell** (T007): the hull offsets in world space by one constant distance, so the line shrinks with distance exactly as the actor does (FR-008).
- **Depth bias is the bleed-through guard** (T012) — it is enabled there, not in T005, so the ordering matches the story that needs it.
- **No per-frame allocation (FR-011).** The smoothed normals are load-time; the uniform byte arrays are reused.
- **FR-010 and FR-011 are structural** — the outline pass shares the posed mesh and pushes a zero-width uniform (T004/T007), and the smoothed normals are computed once at load, so the cel shading is unchanged and the draw allocates nothing; T015 confirms both.
- **Only actors are outlined** (FR-002); the env/prop static path is never touched.
- **The reference's later items are still out of scope**: interior crease lines (would need a G-buffer), shadows, rim, specular/matcap, per-actor parameters.
