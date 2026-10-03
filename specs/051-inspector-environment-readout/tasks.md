# Tasks: Inspector Environment Readout

**Input**: Design documents from `/specs/051-inspector-environment-readout/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: none. The change is ImGui draw code in two panel functions; it cannot run without a
GPU device and a frame, and adds nothing pure to assert (research D6). Validation is the
quickstart walkthrough, with `dotnet build` / `dotnet test` to confirm nothing else regressed.

**Organization**: Tasks are grouped by user story. There is no setup or foundational work —
the readout reads existing zone state and the panels are already drawn, so nothing blocks the
stories. **US1** relocates the readout (the capability); **US2** confirms it follows the
active zone (existing behaviour of the value, relocated — no new code expected).

**Status note**: implemented and recorded together; all tasks below are marked `[X]`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2)
- Include exact file paths in descriptions

## Path Conventions

Single-project F# repository. Paths are relative to the repository root (`fsharp-game/`):

- `editor/` — editor panels (`Outliner.fs`, `Inspector.fs`); `docs/` — design notes;
  `specs/` — feature documents; `test/` — the xUnit project.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: project initialization and basic structure.

**No tasks.** The feature adds no dependency, native binding, module, project reference, or
scaffolding — it relocates a line between two panels that already draw (plan.md
`Technical Context`).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: core infrastructure needed before any user story.

**No tasks.** The readout reads `Zone.Environment` and `GameplayState.activeZone`, which
already exist; there is nothing to build first (data-model.md). US1 can start immediately.

---

## Phase 3: User Story 1 - A zone's settings are read in one panel (Priority: P1) 🎯 MVP

**Goal**: the active zone's environment name reads in the Inspector, above the Lighting
section, and no longer appears in the Outliner.

**Independent Test**: load an environment into a zone, select nothing, and confirm the
Inspector shows `Environment: <name>` directly above Lighting while the Outliner's Zones
section shows no such line.

### Implementation for User Story 1

- [X] T001 [P] [US1] In `editor/Outliner.fs`, delete the readout block between the Zones
  section and the Actors section — the `environmentName` match and the two `ImGui.text`
  calls (`Environment: %s`, then the blank line). Keep `let zone = GameplayState.activeZone
  state`, which the entity sections below still use. Nothing else in the panel changes.
  (FR-001; contracts/environment-readout.md.)
- [X] T002 [P] [US1] In `editor/Inspector.fs`, add `drawEnvironment (zone: Zone.Zone)` — read
  `zone.Environment`, drawing `ImGui.text (sprintf "Environment: %s" name)` with `(none)`
  when it is `None` — and call it in the `| None ->` branch immediately above
  `drawLighting shared &state zone`, so the readout sits above the "Lighting" header.
  `Zone` is already the type that branch uses, so no new `open`. (FR-002, FR-003, FR-004,
  FR-006; research D1, D3, D5.)

**Checkpoint**: US1 is functional on its own — the readout reads in the Inspector, above the
light settings, and is gone from the Outliner. This is the MVP.

---

## Phase 4: User Story 2 - The readout follows the active zone (Priority: P2)

**Goal**: the readout names the environment of the zone currently inspected — the zone
switched to, or the zone an environment was just instantiated into — and reads `(none)` when
that zone has no environment.

**Independent Test**: switch between a zone with an environment and one without and confirm
the readout changes to match each; instantiate a different environment into the active zone
and confirm the readout names the new one.

### Implementation for User Story 2

- [X] T003 [US2] Verify and record: the readout follows the active zone with no new code.
  `GameplayState.activeZone state` (already bound in `Inspector.draw`) yields the current
  zone each frame, so a zone switch or an environment instantiation is reflected in the same
  frame; `zone.Environment` is the same option the Outliner read, so the `(none)` case
  matches. No manual refresh and no state to keep in step. (FR-005; research D1.)

**Checkpoint**: the readout is the zone's own, not a stale label.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [X] T004 [P] Update `docs/EDITOR_DESIGN.md`: the `Inspector` module-map entry names the
  active zone's environment beside its lighting (the fallback subject, D-024).
- [X] T005 [P] Update `specs/011-environment-parts-props/quickstart.md` scenario 8 so it
  reads the environment from the Inspector above the Lighting section, not the Outliner's
  top line.
- [X] T006 Run the [quickstart.md](./quickstart.md) walkthrough (steps 1–7) and the full
  `dotnet build` + `dotnet test`; record the outcome. The panels need a GPU and a person, so
  the rendered result is the walkthrough (research D6).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: none — US1 can start immediately.
- **US1 (Phase 3)**: T001 and T002 are independent files.
- **US2 (Phase 4)**: T003 depends on T001 and T002 (verification of the relocated readout).
- **Polish (Phase 5)**: T004/T005 after the code is settled; T006 last.

### Same-file constraint

- `editor/Outliner.fs` is edited by **T001** only.
- `editor/Inspector.fs` is edited by **T002** only.
- `docs/EDITOR_DESIGN.md` (T004) and `specs/011-…/quickstart.md` (T005) are distinct files.

### Parallel Opportunities

- **T001 ∥ T002** — the deletion and the addition are in different panels.
- **T004 ∥ T005** — the two documentation updates are independent once the code is settled.

---

## Implementation Strategy

### MVP

Phases 3 (T001–T002) are the MVP: the readout reads in the Inspector above Lighting and is
gone from the Outliner.

### Incremental Delivery

1. **US1** (T001–T002) — the relocation; demonstrate the readout in one panel and its absence
   from the other.
2. **US2** (T003) — the follows-the-zone confirmation.
3. **Polish** (T004–T006) — docs and the walkthrough.

---

## Notes

- **A relocation, not a rewording** (T001, T002): the same `zone.Environment` match and the
  same `(none)` fallback move panels, so the value shown is unchanged (FR-004, SC-002).
- **Display-only** (T002): no command, no undo step, no new state, no new panel — a read-only
  `ImGui.text` (FR-006, FR-008, research D3).
- **Absent with an entity selected** (T002): the readout is drawn only in the `| None ->`
  branch, so an entity selection is unchanged and shows no readout — the spec's accepted
  consequence (FR-007, research D2).
- **Residency stays separate** (T002): the readout names the environment regardless of
  residency; spec 050's glyph remains the residency indicator in the Outliner (research D4).
- **No test is the deliberate choice** (T006): the panels cannot run headlessly and nothing
  pure is added (research D6); `dotnet test` guards against unrelated regressions only.
