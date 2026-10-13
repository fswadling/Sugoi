---

description: "Task list for feature implementation"
---

# Tasks: Clear the World Back to a Fresh Start

**Input**: Design documents from `/specs/084-clear-zones/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/clear-world.md

**Tests**: Included — the project mandates headless verification (constitution N-V) and this feature's success criteria
are test-based (`test/GameplayStateTests.fs`, `test/InputTests.fs`). The shortcut's device-bound gating is verified by
inspection, as a control that reads `ImGui` cannot run headless.

**Organization**: This is a small, cohesive change, so the **Foundational** phase lands the whole command — the
`ZonesClear` type, its state-level runner, the world-swap helper and the write path — and keeps the solution compiling
and green (a command's match arms are atomic: `Command.apply`/`undo`/`placeOf` must gain the case together or the
warning-free build breaks). The story phases then deliver and verify the increments (the menu surface, the exact
reversal, the shortcut).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US3)
- Paths are repository-relative.

## Path Conventions

Single project, F# — flat clusters at the repository root: `core/`, `world/`, `state/`, `editor/`, `test/`. Compile
order is fixed by `fsharp-game.fsproj`; this feature **adds no file and removes none**, so **no `<Compile>` line
changes** and nothing reorders.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the baseline the feature lands on.

- [x] T001 Confirm the baseline: `dotnet build` is warning-free and `dotnet test` is green; note that this feature adds no project scaffolding and no `.fsproj` change (every edit extends an existing module).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Land the `ZonesClear` command end-to-end — the type, its classification, the world-swap helper and the
write path — so the solution compiles and a clear can be performed and reversed headlessly. **⚠️ No user-story work
can begin until this is complete.**

- [x] T002 [P] `world/Zone.fs`: add `emptyContents : ZoneContents` (empty lists, `Environment = None`, `Light = Texture.defaultLight`, `Player = ValueNone`) beside `captureZone`/`occupyZone`, with a doc comment (research D4)
- [x] T003 [P] `state/Command.fs`: add the case `ZonesClear of Previous : (string * ZoneContents) list * PreviousActive : string`; add it to `placeOf`'s `None` arm and to **both** the `apply` and `undo` `invalidOp` arms (research D2, D5)
- [x] T004 [P] `state/GameplayState.fs`: add `replaceZones shared state zones active` — retire every present zone (`Zone.release`), unload each distinct environment (`Assets.releaseEnvironment`), clear the map, rebuild `zones` via `Zone.create`/`Zone.occupyZone` deactivating all but `active`, then set `ActiveZone`, reset `InteractableTrigger`, and drop the selection/drag/nudge; `invalidOp` if `active` is not among `zones` (research D3)
- [x] T005 `state/History.fs`: handle `ZonesClear` — `apply` installs `[ (GameplayState.startZone, Zone.emptyContents) ]`/`startZone`, `undo` installs the payload — through `GameplayState.replaceZones`; add the public `clearZones` (bind `let zones = state.Zones`; capture per zone; `cueSteps = if state.Cue.IsFin then [] else [ SetCue (state.Cue, Cue.Fin) ]`; `commit … (Batch (ZonesClear (previous, state.ActiveZone) :: cueSteps))`) (depends T002, T003, T004; research D6, D6a)
- [x] T006 `dotnet build` is clean with no warnings (depends T002–T005)

**Checkpoint**: the command is carried by the one history; a clear and its undo run headlessly (no surface yet).

---

## Phase 3: User Story 1 - The author clears the world to a fresh start (Priority: P1) 🎯 MVP

**Goal**: The Edit menu offers **Clear World**; invoking it leaves a single empty, active start zone.

**Independent Test**: build a headless authored multi-zone world, invoke the clear, and assert exactly one zone —
the start place, empty, active — with the cue and the scenario library unchanged and a loaded environment released.

- [x] T007 [US1] `editor/Editor.fs`: add the **Clear World** item to the Edit menu (after Redo), labelled `"Ctrl+Shift+C"`, gated by the existing `idle` predicate, calling `History.clearZones` (depends T005; research D8, D9)
- [x] T008 [US1] `test/GameplayStateTests.fs`: headless clear test — an authored multi-zone world (actor/prop/waypoint/trigger, a loaded environment, a light, a player) and a non-empty cue clear to exactly one empty active start zone **with an empty cue (`Fin`)**; a clear of an already-fresh world loses nothing; the scenario library and the **camera** are unchanged; a selected entity is dropped; the removed zone's environment is released/unloaded (depends T005, T007; FR-002, FR-003, FR-007, FR-008, FR-010)
- [x] T009 [US1] `dotnet test` — the clear story is green with no device (depends T008)

**Checkpoint**: the author can reduce any world to a fresh start in one action, and it is reversible (the reversal is
Foundational code).

---

## Phase 4: User Story 2 - The clear is one undoable step, and undo restores the world exactly (Priority: P2)

**Goal**: Verify the clear is exactly one step and that a single undo restores the whole previous world (the
reversal is shared Foundational code, since the command's match arms are atomic).

**Independent Test**: from an authored multi-zone world, clear, undo, and assert every zone's contents and the active
place match pre-clear; redo and assert the fresh start returns.

- [x] T010 [US2] `test/GameplayStateTests.fs`: the clear is exactly one step (a `Batch` of `ZonesClear` and, when the cue was not already `Fin`, `SetCue`); undo restores every zone at its original refs with its contents, the previously active place, **and the previous running cue**; redo re-clears; a clear interleaved with other steps reverses LIFO; the zone-level runner (`Command.apply`/`undo`) refuses a `ZonesClear` (raises) (depends T005; FR-004, FR-010)
- [x] T011 [US2] `dotnet test` — the undo story is green with no device (depends T010)

**Checkpoint**: the clear is a single, exactly-reversible step on the one history.

---

## Phase 5: User Story 3 - The clear is reachable by keyboard as well as by menu (Priority: P3)

**Goal**: `Ctrl+Shift+C` invokes the same clear, without firing while typing or mid-drag.

**Independent Test**: assert the new `C` key maps to a distinct bit; inspect the binding's placement to confirm it is
suppressed while a text field has focus and mid-gesture.

- [x] T012 [P] [US3] `core/Input.fs`: add `| C` to the `Key` union, `| C -> 1u <<< 22` to `Key.mask`, and `int SDL.Scancode.C, Key.mask Key.C` to `keyMap` (research D7)
- [x] T013 [US3] `editor/Editor.fs`: add the binding to `handleShortcuts` — `Input.isCtrl input && Input.isShift input && Input.isPressed Input.Key.C input` → `History.clearZones` — inside the `if state.Editing` block **and** the `Drag = None` guard (depends T005, T012; FR-005, FR-006; research D8)
- [x] T014 [P] [US3] `test/InputTests.fs`: add `Key.C` to the "every key maps to a distinct bit" list
- [x] T015 [US3] Inspection (device-bound): confirm the binding sits after the `ImGui.wantTextInput()` early-return, inside the edit-mode block and the `Drag = None` guard, so it never fires while typing, mid-drag, or outside edit mode (FR-006)
- [x] T016 [US3] `dotnet test` — the keyboard story is green with no device (depends T014)

**Checkpoint**: the clear is reachable by keyboard with the same effect and the same gating.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and end-to-end validation.

- [x] T017 [P] `docs/EDITOR_DESIGN.md`: the Edit menu/shortcut description, the D-011 command list (add the `ZonesClear` state-level step), and the module map (research D11)
- [x] T018 Sweep `editor/ state/` (inspection) for a per-frame `Zone.captureZone`/snapshot: expect the clear's snapshot only in `History.clearZones` (invoked on click/undo), never in `Editor.draw` (constitution V; plan Performance Goals)
- [x] T019 Run the `quickstart.md` validation: `dotnet build` warning-free and `dotnet test` green with no device

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup — **BLOCKS all stories** (the command's match arms are atomic; the
  solution must stay warning-free).
- **User Stories (Phases 3–5)**: all depend on Foundational. US1 delivers the surface; US2 verifies the reversal; US3
  adds the shortcut. US2 and US3 do not depend on US1's task, only on Foundational (US3's `Editor.fs` edit shares a
  file with US1's, so the two are sequenced by file, not by logic).
- **Polish (Phase 6)**: after the desired stories.

### Within Each Story

- `world/` before `state/` before `editor/` (compile order).
- A story ends with `dotnet test` green.

### Parallel Opportunities

- T002, T003, T004 (three different files, no interdependencies).
- T012 and T014 (different files) within US3.
- T017 (docs) beside the code tasks.

---

## Parallel Example: Foundational

```bash
# The independent leaves of the command:
Task: "T002 world/Zone.fs — Zone.emptyContents"
Task: "T003 state/Command.fs — the ZonesClear case + placeOf/invalidOp arms"
Task: "T004 state/GameplayState.fs — replaceZones"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 Setup → Phase 2 Foundational (critical — the command must land whole).
2. Phase 3 US1 → **STOP and VALIDATE** (`dotnet test`).
3. US1 alone delivers the feature: an author clears the world to a fresh start, reversibly.

### Incremental Delivery

1. Setup + Foundational → the command is carried by the one history, green.
2. US1 → the Edit-menu surface is verified → MVP.
3. US2 → the exact single-step reversal is verified.
4. US3 → the keyboard binding is verified.
5. Polish → docs and quickstart.

---

## Notes

- [P] = different files, no dependencies.
- Phase 2 is atomic in practice: the `Command`/`History`/`placeOf` match arms must extend together to keep the build
  warning-free.
- **No file is added or removed**; **no `<Compile>` line changes**.
- The step is **state-level** (like `ZoneDelete`): `placeOf` returns `None`, the zone-level runner refuses it, and
  `History` carries it.
- The teardown is **reused** (`Zone.release`, `Assets.releaseEnvironment`, `Zone.occupyZone`) — no new runtime teardown.
- The snapshot is taken **on invoke**, never per frame (T018 guards this).
- The shortcut is `Ctrl+Shift+C`, not `Ctrl+C` (spec Clarifications), so the conventional Copy binding stays free.
- The action is **Clear World**; the command is `ZonesClear` and the write path `clearZones`. The command is
  zones-only **by design** (the cue rides a `SetCue` in the same `Batch`), so the name drift is deliberate, not an
  oversight (research D6a).
- FR-008 needs no serialization task: the scenario library and the camera are simply not touched; the running cue is reset through the existing `SetCue` step inside the clear's `Batch`.
- The camera is left where it is (research D6b); only `Selection`/`Drag`/`NudgeBefore` are dropped.
- Commit after each task or logical group.

---

## Phase 7: Convergence

*(Populated by `/speckit-converge` if the built code falls short of the spec.)*
