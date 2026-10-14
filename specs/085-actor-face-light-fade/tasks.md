---

description: "Task list for the actor face light fade"

---

# Tasks: Face Light Fades as the Actor Turns Away from the Sun

**Input**: Design documents from `/specs/085-actor-face-light-fade/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/cel-face-fade.md](./contracts/cel-face-fade.md), [quickstart.md](./quickstart.md)

**Tests**: No test tasks. The spec requests none: the fade is in the shader body, needs a GPU, and cannot run headless (`spec.md` FR-008 / SC-004 only require the existing suite to pass unmodified). No pure F# logic is added.

**Organization**: Tasks are grouped by user story so each is independently implementable and testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)
- Every task names its exact file path.

## Path Conventions

Single project at the repository root. The surfaces this feature touches are `shaders/` (the actor
fragment shader and its compiled SPIR-V) and `docs/` (the rendering notes). No F# source changes.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the baseline and the shader toolchain before editing.

- [X] T001 Verify the baseline in `fsharp-game/`: `dotnet build` is clean, `dotnet test test/fsharp-game.Tests.fsproj` passes (578), and `glslc shaders/cel.frag -o /tmp/cel.spv && cmp /tmp/cel.spv shaders/cel.frag.spv` reproduces the committed `.spv` byte-for-byte (proves the exact compile invocation before it is relied on).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core prerequisites that must exist before the user story can be implemented.

**None.** The fade reads only data the shader already receives (`cel.uFace.xyz`, `cel.uLightDir.xyz`)
and reuses the existing cel uniform, pipeline and F# draw path (`plan.md` Summary; `data-model.md`).
There is nothing to build first, so this phase is empty by design.

**Checkpoint**: Proceed directly to User Story 1.

---

## Phase 3: User Story 1 - The face dims as the actor turns away from the sun (Priority: P1) 🎯 MVP

**Goal**: The `Face` mesh keeps its fixed band *direction*, but its lit band switches to the shadow
band as the actor turns its face away from the scene sun, and returns to the full front light as it
faces the sun (`spec.md` FR-001, FR-002).

**Independent Test**: Place an actor under a fixed, off-vertical sun and turn it through a full
circle: the face reads at full `specs/040` brightness when it points at the sun, at the switch's
intermediate tone a quarter turn off, and at the ambient-only shadow band when it points directly away
(`quickstart.md` §2 steps 1–2).

### Implementation for User Story 1

- [X] T002 [US1] Edit the body of `shaders/cel.frag`: bind `bool isFace = cel.uFace.w > 0.5;`, and after `band` is computed add the face-only switch — `faceH = vec3(cel.uFace.x, 0, cel.uFace.z)`, `sunH = vec3(cel.uLightDir.x, 0, cel.uLightDir.z)`, the guarded normalized `toward = dot(faceH, sunH) / (length(faceH) * length(sunH))` (or `1.0` when either length `<= 1e-4`), a new `const float FACE_FADE_SOFTNESS = 0.05;`, and `band *= smoothstep(-FACE_FADE_SOFTNESS, FACE_FADE_SOFTNESS, toward);` — exactly per [contracts/cel-face-fade.md](./contracts/cel-face-fade.md). Leave the uniforms, the existing ramp constants, the albedo/alpha-test and the non-face path unchanged; update the file's header comment so it states that the face's *amount* of light now follows the sun while its band *direction* stays fixed.
- [X] T003 [US1] Recompile the SPIR-V from the edited source: `glslc shaders/cel.frag -o shaders/cel.frag.spv` (depends on T002), and confirm the file is regenerated (its size changes).

**Checkpoint**: User Story 1 is complete and independently testable via the walkthrough; this is the MVP.

---

## Phase 4: User Story 2 - The split stays clean and nothing else changes (Priority: P2)

**Goal**: Turning the actor does not move the face's band boundary, does not change the band colours,
and leaves every non-face mesh and every other render path exactly as it was (`spec.md` FR-004,
FR-005; `specs/040` FR-004, FR-007, FR-008, FR-009).

**Independent Test**: Sweep the scene sun around a stationary actor (the face's band boundary stays
fixed while only its brightness changes) and compare a face-less actor, a co-located environment/prop,
a portrait, the UI and the editor against before (unchanged) (`quickstart.md` §2 steps 3–5).

### Implementation for User Story 2

- [X] T004 [US2] Verify the guard confines the change: confirm the new logic sits **only** inside the `if (isFace)` block in `shaders/cel.frag` and that the non-face path (`L = normalize(cel.uLightDir.xyz)` and the plain `band`) is unmodified; confirm the cel uniform block is unchanged (still 80 bytes with no member added — `render/Mesh.fs` untouched) and no CPU-side per-frame allocation was introduced (FR-007); then confirm `git diff --stat` touches only `shaders/cel.frag`, `shaders/cel.frag.spv` and `docs/RENDERING_NOTES.md` (no `render/Mesh.fs`, `world/ActorRender.fs`, `shaders/mesh.vert`, `render/Gpu.fs`, `screen/GameplayScreen.fs` or `.fsproj` change) (depends on T003).

**Checkpoint**: Both stories hold — the face switches, and nothing else moved.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and end-to-end validation.

- [X] T005 [P] Update `docs/RENDERING_NOTES.md` §13 with the switch bullet (it is the source of truth for the actor render path): the face's lit band switches to the shadow band as the heading turns away from the sun, the small `smoothstep` on the azimuthal `toward` factor, and the degenerate-input fallback. Cite spec 085.
- [X] T006 Run `dotnet build` (clean) and `dotnet test test/fsharp-game.Tests.fsproj` (578 pass) to prove `spec.md` SC-004.
- [X] T007 Run the interactive walkthrough in [quickstart.md](./quickstart.md) §2 to validate SC-001 (the switch saturates and does not strobe), SC-002 (never below ambient) and SC-003 (band boundary, colours and all other paths unchanged).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start here.
- **Foundational (Phase 2)**: Empty — nothing blocks the story.
- **User Story 1 (Phase 3)**: After Setup. T003 depends on T002.
- **User Story 2 (Phase 4)**: T004 depends on T003 (the compiled shader must exist to inspect the change).
- **Polish (Phase 5)**: After User Story 1 (T005 can start once T002 lands; T006/T007 after T003).

### Task Graph

```text
T001 → T002 → T003 → T004
                       T005 (docs, parallel with T004)
                       T006 → T007
```

### Within Each User Story

- Source before compiled artifact (T002 before T003).
- Implementation before the verification task (T004 after T003).

### Parallel Opportunities

- T005 (`docs/RENDERING_NOTES.md`) is a different file from the shader work and can run in parallel with T004.
- There is no meaningful further parallelism: the whole feature is one file plus its compile and docs.

---

## Parallel Example: after T003

```text
Task T004: "Verify the guard confines the change (git diff --stat + shader inspection)"
Task T005: "Update docs/RENDERING_NOTES.md §13 with the switch bullet"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (T001).
2. Skip Phase 2 (empty).
3. Complete Phase 3 (T002–T003).
4. **STOP and VALIDATE**: run `quickstart.md` §2 steps 1–2 — the face switches. This alone is the feature.

### Incremental Delivery

1. Setup → baseline confirmed.
2. User Story 1 → the switch works → demo (MVP).
3. User Story 2 → confirm the guard → demo that nothing else moved.
4. Polish → docs and the full walkthrough.

---

## Notes

- [P] tasks = different files, no dependencies.
- The switch is a fixed expression with no authoring surface (`spec.md` FR-006): retuning its softness
  (`FACE_FADE_SOFTNESS`) is a `shaders/cel.frag` edit plus a `glslc` recompile, never a UI change.
- Recompiling `cel.frag.spv` is required for the runtime to see the change — the `.spv` is what
  `render/Gpu.fs` loads, not the `.glsl`.
- `specs/040-actor-cel-shading/` is referenced, not edited.
