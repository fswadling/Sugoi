# Tasks: Zone Light Settings

**Input**: Design documents from `/specs/043-zone-light-settings/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: The spec asks for the pure part to be covered headlessly (plan.md `Testing`), so the
`toLight`/default tests (T005), the `SetLight` reversal (T012) and the instantiate/undo case (T012)
are included. The rendered result, the Inspector's committed edit and the picker need a GPU and are
the quickstart walkthrough.

**Organization**: Tasks are grouped by user story. The authored record, the live `Zone` field, the
`SetLight` command and the editor bindings are shared foundation (Phase 1–2); **US1** adds the
render and the Inspector section (the capability); **US2** is the per-zone and independence
confirmation (no extra code — being per-zone is what a field on `Zone` already means); **US3**
threads the settings through the registered-zone record and the one-step instantiate.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Single-project F# repository. Paths are relative to the repository root (`fsharp-game/`):

- `render/` — GPU layer and ImGui bindings; `world/` — game-domain modules; `state/` — shared/editor
  state and commands; `editor/` — editor panels; `screen/` — the screens; `test/` — the xUnit project.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The authored record and its derivation, and the one new widget every later task needs.

- [X] T001 [P] In `render/Texture.fs`, add `[<Struct>] LightSettings { Pitch : float32; Yaw : float32; Color : Vector3; Ambient : float32 }`, `defaultLight` (pitch `54.7356`, yaw `45`, white, ambient `0.15`) and `toLight : LightSettings -> Light` deriving the direction as `normalize(sin yaw·cos pitch, sin pitch, cos yaw·cos pitch)` and the ambient as `(a, a, a)`, beside `Light` (data-model.md; research D1–D5).
- [X] T002 [P] In `render/ImGui.fs`, bind `igColorEdit3` (contracts/zone-light.md): `extern bool colorEdit3(string label, float32[] col, int flags)`. The only new widget.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: The zone's live light field, the command that changes it and the test/buffer scaffolding must exist before US1 can render under it or commit an edit.

- [X] T003 In `world/Zone.fs`, add `mutable Light : LightSettings` to `Zone`, with a doc comment noting it is a zone-level setting (not an entity), and seed it with `Texture.defaultLight` in `create`. The zone literal is built only there, so nothing else moves. (Depends on T001.)
- [X] T004 [P] In `state/EditorState.fs`, add `LightColorBuffer : float32 array` (length 3) to `EditorState` and initialise it once in `create`; it is the reused triple the colour picker draws over (research D9).
- [X] T005 [P] Create `test/LightTests.fs` (new) and add it to the compile list in `test/fsharp-game.Tests.fsproj`: cover `Texture.toLight` — `(0,0) → (0,0,1)`, `(90,0) → (0,1,0)`, `(0,90) → (1,0,0)`, unit length over a spread of angles; the default reproduces `{ Direction = normalize(0.4, 0.8, 0.4); Color = (1,1,1); Ambient = (0.15,0.15,0.15) }`; the scalar ambient expands to `(a, a, a)`. (Depends on T001.)
- [X] T006 In `state/Command.fs`, add `SetLight of Previous : LightSettings * Next : LightSettings`; `apply` sets `zone.Light <- next` and `undo` sets `zone.Light <- previous` (contracts/zone-light.md; research D7, D8). It is needed by both the Inspector's committed edits (T008) and the instantiate batch (T011). (Depends on T003.)

**Checkpoint**: `dotnet build` is green with the authored record, the zone's light field and the command; nothing yet reads or changes them from the UI.

---

## Phase 3: User Story 1 - A zone's light is authored, not compiled in (Priority: P1) 🎯 MVP

**Goal**: the scene lights under the active zone's settings, and the Inspector authors them, one undo step per committed change.

**Independent Test**: select the zone in the Outliner's Zones list, drag each control in the Inspector's "Lighting" section, and confirm the environment, props and actors re-light; an untouched scene looks exactly as before; releasing a control after a change gives one Ctrl+Z step.

### Implementation for User Story 1

- [X] T007 [US1] In `screen/GameplayScreen.fs`, replace the hard-coded `Light` in `renderScene` with `let light = Texture.toLight zone.Light` (`zone` is already bound). Both consumers (`EnvironmentBatch.render`, `ActorRender.draw`) are unchanged. (Depends on T001, T003.)
- [X] T008 [US1] In `editor/Inspector.fs`, draw a "Lighting" collapsible section for the active zone when `state.Editor.Selection` is `None` (the zone is the Inspector's fallback subject; an entity selection shows the entity as today); it replaces the Inspector's existing `(no selection)` text in that branch: Sun Pitch (`ImGui.dragFloat`, 10–90), Sun Yaw (`dragFloat`, 0–360), Light Colour (`ImGui.colorEdit3` over `state.Editor.LightColorBuffer`, seeded from `zone.Light.Color` while the picker is not active), Ambient (`dragFloat`, 0.1–1.5). Each control reads the zone's light before the widget and pushes `SetLight (before, after)` through `UndoStack.push` when `ImGui.isItemDeactivatedAfterEdit()` — one step per released/left control, no coalescing (contracts/zone-light.md; research D6, D7, D9). (Depends on T002, T003, T004, T006.)
- [X] T015 [US1] In `state/GameplayState.fs`, add `selectZone`: switch when the zone is another one, and drop the entity selection (`Selection = None; Drag = None`) when it is the zone **already active**, which a plain `switchZone` no-ops on. In `editor/Outliner.fs`, the zone row calls it, so clicking a zone always brings its details into the Inspector (FR-003; a gap found while running US1). Cover it in `test/GameplayStateTests.fs`. (Depends on T008.)

**Checkpoint**: User Story 1 is functional on its own — a zone's light is authored from the Inspector and the scene follows it, undoably. This is the MVP.

---

## Phase 4: User Story 2 - The light belongs to the zone and follows it (Priority: P2)

**Goal**: zones carry independent lights, and switching zones switches the light in effect and shown.

**Independent Test**: give two zones different lights, switch between them, and confirm the Inspector and the scene both follow the active zone; changing a light in one zone leaves the other untouched.

### Implementation for User Story 2

- [X] T009 [US2] Verify and record: a light changed in one zone leaves another zone's light untouched (the setting is a field of each `Zone`, T003); switching zones brings each zone's own light into the Inspector and into the render — `GameplayState.switchZone` selects the new zone and `enterZone` clears the entity selection, so the Inspector's zone subject takes over (T008) and `GameplayState.activeZone` drives the light (T007); a zone with no environment loaded still shows and applies its light. No new code is expected. (Depends on T003, T007, T008.)

**Checkpoint**: the light reads as the zone's own, not a global.

---

## Phase 5: User Story 3 - A registered place keeps the light it was authored under (Priority: P3)

**Goal**: registering captures the light and instantiating restores it, in the same single undo step as the environment and props.

**Independent Test**: author a distinctive light, Register the zone, change the light, Instantiate the place, and confirm the light returns; Ctrl+Z reverts environment, props and light together.

### Implementation for User Story 3

- [X] T010 [US3] In `world/ZoneTemplate.fs`, add `Light : LightSettings` to `ZoneTemplate` (doc comment: the zone's, not the environment's) and a `light` parameter to `ofEnvironment`; update its doc comment. In `world/Zone.fs`, `captureTemplate` passes `zone.Light`. Update the six `ZoneTemplate.ofEnvironment` call sites in `test/ZoneRegistryTests.fs` to pass a light. (Depends on T001, T003.)
- [X] T011 [US3] In `editor/Spawning.fs`, `instantiateZone` captures `previousLight = zone.Light` and includes `SetLight (previousLight, template.Light)` in its existing `Batch`, beside the `SetEnvironment` swap — still one undo entry. (Depends on T006, T010.)
- [X] T012 [US3] In `test/ZoneTests.fs`, add a case that `captureTemplate` records the zone's light and a case that `Command.apply` then `Command.undo` of `SetLight` set and restore it. In `test/SpawningTests.fs`, add a case that `instantiateZone` sets the zone's light to the recorded one and that undoing the step restores the previous light. (Depends on T006, T010, T011.)

**Checkpoint**: a place is the room *and* the light it was lit under, restored in one step.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T013 [P] Update `docs/EDITOR_DESIGN.md` with a note on the Lighting section: the Inspector location (the active zone as the fallback subject when no entity is selected), the one-undo-step-per-commit decision (a departure from `odin-game`'s direct write), and the recorded light on `ZoneTemplate` with the one-step instantiate.
- [X] T014 Run the [quickstart.md](./quickstart.md) walkthrough (steps 1–6) and the full `dotnet build` + `dotnet test`; record the outcome — including FR-010 (dragging the sun leaves the `Face` mesh's cel band direction unchanged, step 4) (the rendered result, the committed edit and the picker need a GPU and a person).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies. T001 and T002 are independent files.
- **Foundational (Phase 2)**: T003 needs T001; T004 is independent; T005 needs T001; T006 needs T003.
- **US1 (Phase 3)**: T007 needs T001/T003; T008 needs T002/T003/T004/T006. This is the MVP.
- **US2 (Phase 4)**: T009 needs T003/T007/T008 (verification only).
- **US3 (Phase 5)**: T010 needs T001/T003; T011 needs T006/T010; T012 needs T006/T010/T011.
- **Polish (Phase 6)**: T013 after the code is settled; T014 last.

### Same-file constraint

- `world/Zone.fs` is edited by **T003 → T010** (`captureTemplate`), sequential.
- `state/GameplayState.fs` and `editor/Outliner.fs` are each edited by **T015** only.
- `state/EditorState.fs` (T004), `state/Command.fs` (T006) and `editor/Spawning.fs` (T011) each get
  one task.
- `render/Texture.fs` is edited by **T001** only; `render/ImGui.fs` by **T002** only.
- `test/ZoneRegistryTests.fs` (T010) and `test/ZoneTests.fs` / `test/SpawningTests.fs` (T012) are
  distinct files.

### Parallel Opportunities

- **T001 ∥ T002** — the lighting record and the ImGui binding are independent.
- **T004 ∥ T005** — the editor buffer and the test file are independent once T001 lands.
- **T013** (docs) is independent of the code once the shapes are settled.

---

## Implementation Strategy

### MVP

Phases 1–3 (T001–T008, T015) are the MVP: a zone owns a light and the Inspector authors it, the
scene lighting under it, undoably, with an unedited zone rendering exactly as before.

### Incremental Delivery

1. **Setup + Foundational** (T001–T006) — the authored record, the derivation, the zone's field and
   the command; build green, nothing lit or edited by them yet.
2. **US1** (T007–T008, T015) — the render, the Inspector section and the zone-row selection; demonstrate a re-lit scene.
3. **US2** (T009) — the per-zone independence confirmation.
4. **US3** (T010–T012) — the recorded light and the one-step instantiate.
5. **Polish** (T013–T014) — docs and the walkthrough.

---

## Notes

- **The defaults are the contract.** `defaultLight` must reproduce the replaced constant
  (FR-004, SC-002); T005 asserts it, because a wrong pitch/yaw would silently relight every scene.
- **The derivation is the one subtle part.** A transposed axis in `toLight` (T001) tilts every
  scene; T005 is what catches it, and it is the only pure thing the feature adds.
- **`SetLight` has two callers** (T006): the Inspector's committed edits (T008, FR-006) and the
  instantiate batch (T011, FR-007). It is one command, not two mechanisms.
- **Commit on release, do not coalesce** (T008): `isItemDeactivatedAfterEdit` gives one step per
  drag; two deliberate changes are two steps (SC-007).
- **The Inspector's zone subject is the fallback** (T008): it is drawn when `Selection` is `None`,
  so an entity and a zone never appear together and no second selection state exists (FR-003,
  research D6). Bringing a zone up is T015: a switch already clears the selection, and the zone
  already active — where a switch is a no-op — is the case `selectZone` adds.
- **The face light is untouched** (T007): `toLight` feeds only the scene light; `ActorRender` derives
  the `Face` mesh's band direction from the actor's orientation (FR-010). No shader changes.
- **No per-frame allocation (FR-013).** The frame builds one `Light` from the zone; the picker
  reuses `LightColorBuffer` (T004).
- **Shadows stay out of scope** (FR-012): no shadow state, uniform or pass is touched.
