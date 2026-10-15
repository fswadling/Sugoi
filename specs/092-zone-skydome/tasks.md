---

description: "Task list for the Zone Skydome feature"
---

# Tasks: Zone Skydome

**Input**: Design documents from `/specs/092-zone-skydome/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/skydome-shader.md, quickstart.md

**Tests**: Included — the spec's FR-011 requires headless tests.

**Organization**: Tasks are grouped by user story (US1 render, US2 scenario staging, US3 editor) so each
is an independently testable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3
- Exact file paths are given in each task.

## Path Conventions

Single project. Sources at the repo root (`render/`, `world/`, `state/`, `editor/`, `screen/`), tests in
`test/`, shaders in `shaders/`, compile order in `fsharp-game.fsproj`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The shaders and their build wiring.

- [X] T001 [P] Write `shaders/skydome.vert` and `shaders/skydome.frag` per `contracts/skydome-shader.md`
  (position-only vertex with `set = 1` mat4 VP; fragment with `set = 3` zenith/horizon/sun uniforms;
  gradient + simple sun; gamma-encode at output).
- [X] T002 Compile the shaders with `glslc shaders/skydome.vert -o shaders/skydome.vert.spv` and
  `glslc shaders/skydome.frag -o shaders/skydome.frag.spv`, then add both `.spv` files to the `CopyShaders`
  target's `SourceFiles` in `fsharp-game.fsproj`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The authored sky value, on the zone. Every story reads it.

**⚠️ CRITICAL**: No user story can be implemented until this phase is complete.

- [X] T003 Add `[<Struct>] Skydome.Settings` (`Enabled : bool`, `Zenith : Vector3`, `Horizon : Vector3`)
  and `defaultSettings` (off; `odin-game`'s colours converted to linear) to a new `render/Skydome.fs`, and
  add `<Compile Include="render/Skydome.fs" />` to `fsharp-game.fsproj` after `render/Camera.fs`.
- [X] T004 [P] Add `mutable Skydome : Skydome.Settings` to the `Zone` record and set it to
  `Skydome.defaultSettings` in `Zone.create` (`world/Zone.fs`).

**Checkpoint**: `Skydome.Settings` exists and a zone carries one; user stories can proceed.

---

## Phase 3: User Story 1 — A zone draws an authored sky behind its scene (Priority: P1) 🎯 MVP

**Goal**: A zone with its sky enabled draws a full gradient sphere (with a simple sun) behind the scene;
a zone with it off is unchanged.

**Independent Test**: build a zone, enable its sky with distinct colours, and draw the scene — the sky is
behind the geometry; with the sky off nothing is drawn behind the scene.

### Tests for User Story 1

- [X] T005 [P] [US1] Add `test/SkydomeTests.fs` with: `defaultSettings` is off with the linear
  zenith/horizon colours; a freshly `Zone.create`d zone carries `defaultSettings`; and a zone left at the
  default draws nothing behind the scene (asserted through the value, `Enabled = false`). Register the file
  in `test/fsharp-game.Tests.fsproj`.

### Implementation for User Story 1

- [X] T006 [P] [US1] Add `Gpu.createSkydomePipeline` (position-only vertex layout, one vertex + one fragment
  uniform buffer, no cull, depth test/write disabled, scene sample count + depth format) in `render/Gpu.fs`,
  add `SkydomePipeline : Pipeline` to the `Gpu` record, build it in `Gpu.create`, and release it in
  `Gpu.destroy`.
- [X] T007 [US1] Add the sphere mesh generator (rings/segments like `odin-game`) and
  `Skydome.create`/`draw`/`release` to `render/Skydome.fs`: `create` builds the buffers once and holds the
  reused uniform bytes; `draw` takes the camera, the zone's `Settings` and the scene `Light`, builds the
  camera-centred view-projection, pushes the uniforms, binds, and draws. Depends on T006.
- [X] T008 [US1] Create the `Skydome` resource in `Engine.fs` (beside `Ui`/`DebugDraw`) and thread it to the
  gameplay scene through `screen/ScreenManager.fs` and `state/GameplayState.fs`; free it before the device is
  destroyed. Depends on T007.
- [X] T009 [US1] In `screen/GameplayScreen.renderScene`, draw the sky as the **first** draw of the scene pass
  (before the grid and geometry) when `zone.Skydome.Enabled`, using the resolved `Texture.toLight zone.Light`
  for the sun. Depends on T008.

**Checkpoint**: US1 is visible and independently testable.

---

## Phase 4: User Story 2 — The sky belongs to the zone and rides with a scenario (Priority: P2)

**Goal**: The sky is part of a zone's staging: a scenario captures it and a fork restores it.

**Independent Test**: author a zone's sky, register the scene as a scenario, fork into a world without the
zone, and assert the zone's sky is restored; restage a present zone and assert its sky becomes the
scenario's.

### Tests for User Story 2

- [X] T010 [P] [US2] Extend `test/ScenarioForkTests.fs`: a fork of a scenario whose zone carries a
  non-default sky creates the zone with that sky; a fork restaging a present zone replaces its sky; the
  round-trip works for an environment-less zone.

### Implementation for User Story 2

- [X] T011 [P] [US2] Add `Skydome : Skydome.Settings` to `ZoneTemplate` and `ZoneStaging`, give it a
  value in `empty`/`ofTemplate`, add a `skydome` parameter to `ofEnvironment`, and pass `zone.Skydome` from
  `Zone.captureTemplate` (`world/ZoneTemplate.fs`, `world/Zone.fs`).
- [X] T012 [P] [US2] Carry the zone's sky in `ScenarioZone.captureStaging` (`world/ScenarioZone.fs`).
- [X] T013 [US2] Set `zone.Skydome <- staging.Skydome` in `GameplayState.applyStaging`
  (`state/GameplayState.fs`), beside `zone.Light`.

**Checkpoint**: US2 round-trips the sky through register/fork.

---

## Phase 5: User Story 3 — Editing the sky is reversible from the Inspector (Priority: P3)

**Goal**: With nothing selected, the Inspector edits the active zone's sky, one undoable step per commit.

**Independent Test**: with nothing selected, toggle the sky and change each colour; each is one undo step
that round-trips, and a no-op commit records nothing.

### Tests for User Story 3

- [X] T014 [US3] Extend `test/SkydomeTests.fs`: `SetSkydome` applies and undoes; `Selection.setSkydome`
  pushes exactly one step for a real change and none for a no-op. Depends on T015–T017. Register nothing new
  (same file as T005).

### Implementation for User Story 3

- [X] T015 [P] [US3] Add `SetSkydome of ZoneId : string * Previous : Skydome.Settings * Next :
  Skydome.Settings` to `state/Command.fs`: `placeOf` returns `Some zone`; `apply` sets `zone.Skydome <- next`;
  `undo` sets it back.
- [X] T016 [P] [US3] Add the zenith/horizon colour buffers and their live-editing flags to
  `state/EditorState.fs`, mirroring `LightColorBuffer`/`LightColorEditing`.
- [X] T017 [P] [US3] Add `Selection.setSkydome` in `editor/Selection.fs`, mirroring `Selection.setLight`
  (reads the active zone's sky, applies the change as one `SetSkydome` step, records nothing on a no-op).
- [X] T018 [US3] Add `Inspector.drawSkydome` (an enabled checkbox + two colour pickers, committed on
  release) in `editor/Inspector.fs` and call it in `draw`'s no-selection branch beside `drawLighting`.
  Depends on T015, T016, T017.

**Checkpoint**: All three stories work and are independently testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T019 [P] Update the guiding docs: name the sky among the zone's settings in `docs/ARCHITECTURE.md`
  ("Zone = world container") and the persistence note; move `render/Skydome.fs` out of "(planned)" in
  `docs/PROJECT_STRUCTURE.md`; record phase 14's sky as landed (trimmed) in `docs/ROADMAP.md`.
- [X] T020 Run `dotnet build` (warning-free) and `dotnet test` (green); walk the `quickstart.md` manual
  scenarios against a running build. Build: 0 warnings, suite: 664 passing. The interactive walkthrough
  is left to the user, as it needs a window and a GPU.

---

## Dependencies & Execution Order

- **Setup (T001–T002)** → **Foundational (T003–T004)** → all user stories.
- **US1** (T005–T009) depends only on T003–T004.
- **US2** (T010–T013) depends only on T003–T004; independent of US1.
- **US3** (T014–T018) depends only on T003–T004; independent of US1 and US2.
- T007 depends on T006; T008 on T007; T009 on T008. T014 depends on T015–T017; T018 depends on T015–T017.
- Polish (T019–T020) after the stories.

**Parallel Opportunities**

- T001/T002 are setup; T003→T004 are sequential.
- Within US1, T005 (tests) and T006 (pipeline) are `[P]`.
- T011/T012 are `[P]`; T010 (`[P]`, its own file edits in `ScenarioForkTests.fs`).
- T015/T016/T017 are `[P]` (distinct files); T014 follows them, and T018 follows T015–T017.

## Implementation Strategy

**MVP = US1**: the sky value plus the render path — a zone that shows an authored sky. Then US2 (rides
with a scenario) and US3 (editor surface). Each phase is committable and independently testable; the
headless suite (US1/US2/US3 tests) runs with no window.
