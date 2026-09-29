# Tasks: Multisample Anti-Aliasing

**Input**: Design documents from `/specs/042-multisample-antialiasing/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: **None new.** MSAA is entirely GPU pipeline/target state and the test project cannot open a GPU device (research D5), so the existing suite is the regression check and the feature is verified by the quickstart walkthrough. The `render/Ui.fs` and `render/DebugDraw.fs` changes are one field each.

**Organization**: Tasks are grouped by user story. This feature is **one change to the scene pass** — two targets and the sample count on the pipelines that render into it — so the code lands together in Foundational (a pass is only valid when its targets and pipelines share a sample count; there is no green intermediate), and the US1/US2/US3 phases are the acceptance passes (smooth edges, correctness across the frame, lifecycle).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Single-project F# repository. Paths are relative to the repository root (`fsharp-game/`):

- `render/` — the GPU layer; `Engine.fs` — the frame loop; `docs/` — the rendering notes.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Fix the sample count and stand up the fail-loudly check, so later steps have one constant and one honest guard.

- [X] T001 In `render/Gpu.fs`: add the sample-count constant (`SampleCount = SDL.GPUSampleCount.SampleCount4`, FR-001/FR-010) and, in `create`, check `SDL.GPUTextureSupportsSampleCount` for the swapchain colour format and `D32Float` at that count — `failwithf` a clear message if either is unsupported (FR-009). (Depends on nothing.)

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: The targets and the pipelines MUST agree on the sample count, so this phase is one coherent change — it is only valid (and only renders) once T002–T006 have all landed. There is no intermediate frame.

- [X] T002 In `render/Gpu.fs`: add `MsaaTexture : Texture` to the `Gpu` record; add `createMsaaTexture` (swapchain format, `ColorTarget` usage, `SampleCount`, 1 layer/level); make `createDepthTexture` use `SampleCount`; rename `ensureDepthTexture` to `ensureSceneTargets` and have it recreate **both** textures when the extent changes (releasing the old handles first); release `MsaaTexture` in `destroy`; `failwithf` if either target's handle is null (FR-009). (Depends on T001.)
- [X] T003 In `render/Gpu.fs`: set `MultisampleState.SampleCount <- SampleCount` in `createActorPipeline` (cel **and** outline) and in `createStaticPipeline`; change nothing else — the outline's depth bias stays as it is (FR-006). (Depends on T002.)
- [X] T004 [P] In `render/DebugDraw.fs`: set the debug-line pipeline's `MultisampleState.SampleCount <- SampleCount` (it draws into the scene pass, so it must match — FR-003). Depth state and primitive type unchanged. (Depends on T001.)
- [X] T005 [P] In `render/Ui.fs`: set the 2D layer's pipeline `MultisampleState.SampleCount <- SampleCount` (same reason — FR-003); its blend state, depth state and target format are unchanged, so blending and ordering are preserved. (Depends on T001.)
- [X] T006 In `Engine.fs`: ensure both scene targets by extent (`Gpu.ensureSceneTargets &shared.Gpu w h`), then point the reused `sceneTargets.[0]` at `shared.Gpu.MsaaTexture` and set `ResolveTexture <- swapchainTexture`, `ResolveMipLevel <- 0u`, `ResolveLayer <- 0u`; the overlay pass is left exactly as it is (it loads the resolved swapchain). (Depends on T002, T003.)

**Checkpoint**: `dotnet build` is green and the frame renders; the scene draws into a 4× multisampled colour target, resolves into the swapchain, and every scene-pass pipeline shares the count.

---

## Phase 3: User Story 1 - The scene is drawn with smooth edges (Priority: P1) 🎯 MVP

**Goal**: the actor outline (spec 041) and every geometry silhouette render with smooth edges instead of a staircase.

**Independent Test**: view an actor's outline (and a hard-edged prop beside it) and confirm both boundaries are smooth gradients rather than single-pixel steps.

### Implementation for User Story 1

- [X] T007 [US1] Verify: look at Akane's outline with the environment/ArtRoom beside her — both its **inner** and **outer** boundaries are anti-aliased (no staircase), and other geometry silhouettes are smooth too. This is the artefact of the spec's Origin. Record the outcome in `tasks.md`'s notes. (Depends on T006.)

**Checkpoint**: the frame is anti-aliased — User Story 1 is met. This is the MVP.

---

## Phase 4: User Story 2 - The anti-aliasing is correct across the whole frame (Priority: P2)

**Goal**: multisampling covers the depth-tested scene as a whole (overlapping surfaces, cutouts) and composites cleanly (the overlays sit on the resolved image).

**Independent Test**: turn the actor so a limb crosses the torso and confirm the limb's edge against the body is smooth and the outline does not leak; confirm the hair/eye cutout edges are smooth and unchanged; confirm the editor overlay and the 2D UI are as crisp as before.

### Implementation for User Story 2

- [X] T008 [US2] Verify: (a) an overlapping-surface edge (a limb against the torso; hair over the body) is anti-aliased — the depth target is multisampled (FR-004); (b) the hair/eye alpha-cutout edges are anti-aliased **and** the visible cut shape is unchanged (FR-005); (c) the editor overlay and the 2D UI are crisp — no blur, no double-resolve (FR-007); (d) the outline does not bleed or thin — its depth bias is untouched (FR-006). Record the outcome. (Depends on T006.)

**Checkpoint**: the anti-aliasing is correct across the frame, not just on the outer silhouette.

---

## Phase 5: User Story 3 - Bounded cost, honest quality point, clean lifecycle (Priority: P3)

**Goal**: the targets follow the render extent (recreated only on change), the frame allocates nothing new, a 3D-less screen still resolves, and an unsupported sample count fails loudly.

**Independent Test**: resize the window (including a DPI/scale change) and confirm the frame stays correct and the targets are not recreated every frame; confirm the main menu still presents.

### Implementation for User Story 3

- [X] T009 [US3] Verify: (a) resizing the window (and a DPI/scale change) recreates both targets once and keeps the frame correct (FR-008); (b) the frame adds no per-frame allocation — the reused target arrays and byte buffers are unchanged (SC-005); (c) the main menu (the clear-only scene pass) still clears and presents as before (FR-013); (d) note that the fail-loud branch (FR-009) is unreachable on a normal 4×-capable desktop. Record the outcome. (Depends on T006.)

**Checkpoint**: the lifecycle is clean and honest.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T010 [P] Update `docs/RENDERING_NOTES.md` with a section on the multisampled scene pass: the 4× colour + depth targets, the resolve into the swapchain, why every scene-pass pipeline must share the count, and that the overlay is composited after the resolve.
- [X] T011 Run the [quickstart.md](./quickstart.md) walkthrough (steps 1–7) and the full `dotnet build` + `dotnet test`; record the outcome (the smoothness and the overlay are GPU-observable and need a person).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 can start immediately.
- **Foundational (Phase 2)**: T002 needs T001; T003 needs T002; T004/T005 need T001; T006 needs T002/T003. The phase is only complete (and the frame only valid) when T002–T006 have all landed.
- **US1 (Phase 3)**: T007 needs T006.
- **US2 (Phase 4)**: T008 needs T006.
- **US3 (Phase 5)**: T009 needs T006.
- **Polish (Phase 6)**: T010 after the shapes are settled; T011 last.

### Same-file constraint

- `render/Gpu.fs` is edited by **T001 → T002 → T003** — sequential.
- `Engine.fs` is edited by **T006** only.
- `render/DebugDraw.fs` (T004) and `render/Ui.fs` (T005) are distinct files.

### Parallel Opportunities

- T004 (`render/DebugDraw.fs`) and T005 (`render/Ui.fs`) are independent once T001 lands.
- T010 (docs) is independent of the code once the shapes are settled.

---

## Implementation Strategy

### MVP

Phases 1–3 (T001–T007) are the MVP: the scene is drawn anti-aliased, so the actor outline and the
frame's silhouettes are smooth. The code all lands in Foundational because the targets and pipelines
must share the sample count; US1 is the acceptance pass.

### Incremental Delivery

1. **Setup + Foundational** (T001–T006) — the constant, the guard, the two targets, the pipeline sample counts, the resolve.
2. **US1** (T007) — the smooth-edge confirmation.
3. **US2** (T008) — the overlapping-surface, cutout and overlay confirmation.
4. **US3** (T009) — the resize/allocation/menu confirmation.
5. **Polish** (T010–T011) — docs and the walkthrough.

---

## Notes

- **The change is atomic.** A graphics pass is only valid when its colour/depth targets and every pipeline drawing into it share a sample count, so T002–T006 must land together (there is no green intermediate to stop at).
- **No new tests** (research D5). The suite must stay green unmodified (FR-012) and the existing 319 tests are the regression check.
- **No shader, screen or world change.** MSAA is target and pipeline state; `state/Screen.fs`'s `ScenePass` is unchanged (the resolve rides inside the colour target).
- **The overlay is deliberately untouched** (FR-007): it draws in its own pass over the resolved swapchain, so its text stays crisp.
- **The outline's depth bias is untouched** (FR-006): the value from spec 041 is preserved; MSAA does not change it.
- **Deferred items stay deferred**: shadows, rim, specular/matcap, interior crease lines, FXAA/TAA (research D1, D2).
