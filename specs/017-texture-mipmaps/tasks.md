---

description: "Task list for the Texture Mipmaps feature"
---

# Tasks: Texture Mipmaps

**Input**: Design documents from `/specs/017-texture-mipmaps/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — plan.md ("Testing") calls out the pure level-count arithmetic as the only headless-testable part.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

**Retrospective**: this list records work that already exists in `render/Texture.fs`. Tasks T002–T004 are marked complete to match the working tree; the remaining items (helper extraction, tests, validation, doc update) are still open.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)
- Include exact file paths in descriptions

## Path Conventions

- F# single project at the repository root; modules live in domain clusters (`core/`, `physics/`, `render/`, `world/`, `state/`, `editor/`, `screen/`).
- Compile order is defined by `fsharp-game.fsproj`; a file may only reference symbols defined in earlier files.
- Tests live in `test/` (one `<Module>Tests.fs` per module).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Register the new test file in the build.

- [ ] T001 [P] Add `<Compile Include="TextureTests.fs" />` to `test/fsharp-game.Tests.fsproj`, in the existing ordering.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The single decode/upload path both user stories depend on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 Rewrite `load` in `render/Texture.fs` to decode through a software surface (SDL_image `Load`) instead of the mip-less convenience upload, normalise to a 32-bit RGBA byte layout via surface conversion, and raise with the source path on any decode/conversion failure.
- [X] T003 Compact the surface rows into a tightly packed staging buffer in `render/Texture.fs` (`load`), so a row stride wider than `width * 4` does not skew the uploaded image.
- [X] T004 Create the GPU texture in `render/Texture.fs` (`load`) with a full level chain (`NumLevels = floor(log2(max(w, h))) + 1`, usage `Sampler | ColorTarget`), upload level 0 in a copy pass, then generate the remaining levels outside any pass on the same command buffer.
- [ ] T005 Extract the pure `mipLevels` helper (chain length for a width×height) in `render/Texture.fs` and confirm the 1×1 `solid` placeholder stays on a single level.

**Checkpoint**: Every loaded texture has a complete chain; user stories can be validated.

---

## Phase 3: User Story 1 - Minified surfaces render smoothly (Priority: P1) 🎯 MVP

**Goal**: Textured surfaces are stable under minification — no shimmer at distance, no aliasing at grazing angles — with close-up detail unchanged.

**Independent Test**: load a textured scene, move the camera so surfaces pass into the distance and across grazing angles, and confirm their appearance is stable; view a surface close up and confirm its base-level detail is unchanged.

### Tests for User Story 1

- [ ] T006 [P] [US1] Test `mipLevels` in `test/TextureTests.fs`: 1×1 → 1 level; a square image; a non-square image; a non-power-of-two image (sized to its largest dimension).

### Validation for User Story 1

- [ ] T007 [US1] Run `quickstart.md` scenarios 1–3 (no shimmer at distance, stable grazing angles, close-up unchanged) against the textured environment; fix any failures.

**Checkpoint**: User Story 1 is functional and independently testable.

---

## Phase 4: User Story 2 - Every loaded texture is covered automatically (Priority: P2)

**Goal**: Every map type and source format goes through the same path with no per-asset authoring, and shared textures are prepared once.

**Independent Test**: load a scene whose materials use the full range of map types and source formats (with and without alpha, non-square, non-power-of-two) and confirm each renders correctly with no manual preparation.

### Validation for User Story 2

- [ ] T008 [US2] Run `quickstart.md` scenarios 4–8 (non-RGBA source correct, non-square/NPOT undistorted, shared map prepared once, all map types + placeholder covered, corrupt/missing image fails loudly); fix any failures.

**Checkpoint**: Both user stories work; all textures are covered.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and end-to-end validation.

- [ ] T009 [P] Update `docs/RENDER_PERFORMANCE.md` §8 to mark the missing-mipmaps issue fixed and point at this spec.
- [ ] T010 Run `dotnet build` and `dotnet test test/fsharp-game.Tests.fsproj`; fix any failures, then confirm no per-frame regression via the per-phase frame log (SC-004).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: T002–T004 already done; T005 is needed before T006.
- **User Stories (Phase 3+)**: Depend on Foundational. US1 must be validated before US2 (US2 additionally covers format/sharing edge cases).
- **Polish (Phase 5)**: Depends on both stories.

### Within Each User Story

- `mipLevels` helper before its test.
- Implementation before its quickstart validation.

### Parallel Opportunities

- T001 and T009 (different files) are independent.
- T006 (test file) is independent of the quickstart validation.

---

## Parallel Example: Setup

```bash
# Independent files — run together:
Task: "Register test/TextureTests.fs in test/fsharp-game.Tests.fsproj"
Task: "Update docs/RENDER_PERFORMANCE.md §8"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Setup + Foundational (T001–T005).
2. US1 (T006–T007): chain creation, generation, and the visual minification check.
3. **STOP and VALIDATE**: minification is stable; close-up unchanged.
4. US2 then extends coverage to formats and sharing (T008).

---

## Notes

- [P] tasks = different files, no dependencies.
- This feature is confined to `render/Texture.fs`'s `load`; no pipeline, shader, or sampler change is implied.
- Preparation is load-time only; the per-frame path gains no work (SC-004).
- The shared sampler was already configured to sample levels — only the levels were missing.
- Commit after each logical group.
