# Tasks: Undoable Player Designation

**Input**: Design documents from `/specs/045-undoable-player-designation/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: The spec asks for the reversible designation to be covered headlessly (plan.md `Testing`), so
the command's round trip (T004) and the cross-zone case (T005) are included. The Inspector's control, the
no-op and play mode need the editor and a device, and are the quickstart walkthrough.

**Organization**: There is no project setup — no new module, dependency, file or binding — so the shared
foundation is the command and the recording helper (Phase 1), which every story needs. **US1** is the
capability (the button records a step; the command round trip is asserted). **US2** needs no
implementation at all: that the step rides the zone's own stack is what `Zone.Player` and
`UndoByZone` already mean, so it is a test (T005). **US3** is the control reading its state, which is what
makes an undo visible.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Single-project F# repository. Paths are relative to the repository root (`fsharp-game/`):

- `state/` — shared/editor state and the command union; `editor/` — the editor panels and the
  selection-recording helpers; `world/` — the game-domain modules; `test/` — the xUnit project;
  `docs/` — the design notes.

---

## Phase 1: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: The command and the recording helper must exist before any user story can be built — the Inspector (US1, US3) has nothing to call until they do.

- [X] T001 In `state/Command.fs`, add `SetPlayer of Previous : Handle voption * Next : Handle voption` to the union beside its two sibling zone-level cases (`SetEnvironment`, `SetLight`), with `apply` writing `zone.Player <- next` and `undo` writing `zone.Player <- previous`. Introduce **no** payload type: the value is exactly what `Zone.Player` already holds (`SlotMap.Handle voption`), so the case is total and the union's other rules (redo clears on push, only consecutive `Transform`s coalesce) are untouched (contracts/player-designation.md; research D1, D3; data-model.md).
- [X] T002 In `editor/Selection.fs`, add `setPlayer (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (ref: EntityRef) : unit`, shaped exactly like its neighbour `setLight`: resolve the active zone once, take `before = zone.Player` and `after = ValueSome (Systems.playerHandleOf ref)`, **return without recording when `after = before`**, otherwise apply `SetPlayer (before, after)` and push the one step onto `state.Editor.Undo` (research D2, D3; FR-004). It becomes the editor's only writer of a designation. (Depends on T001.)

**Checkpoint**: `dotnet build` is green with the command and the helper in place; nothing calls them from the UI yet, so the editor behaves exactly as before.

---

## Phase 2: User Story 1 - A player designation can be taken back (Priority: P1) 🎯 MVP

**Goal**: setting an actor as the player is one reversible step — one Ctrl+Z takes it back, one Ctrl+Y puts it back.

**Independent Test**: select an actor in a zone with no player, press "Set as Player", press Ctrl+Z, and confirm the zone has no player again; press Ctrl+Y and confirm she is the player again.

### Implementation for User Story 1

- [X] T003 [US1] In `editor/Inspector.fs`, the actor section's "Set as Player" button stops writing `zone.Player` and calls `Selection.setPlayer shared &state ref` instead (contracts/player-designation.md). Nothing else in the section moves. (Depends on T002.)
- [X] T004 [P] [US1] In `test/ZoneTests.fs`, a command round-trip case beside the existing `SetLight` one: `apply` a first designation (`ValueNone → ValueSome handle`) and assert it is set, `UndoStack.undo` it and assert it is back to `ValueNone`, `redo` and assert it is set again; then re-designate from actor B to actor C, undo, and assert the designation is **B** — the previous actor, not "no player" (FR-001, FR-002, SC-001; quickstart steps 1–2; research D6).

**Checkpoint**: User Story 1 is functional on its own — the designation is an ordinary reversible edit. This is the MVP.

---

## Phase 3: User Story 2 - The step belongs to the zone it was made in (Priority: P2)

**Goal**: a designation is undone in the zone it was made in, never in the zone now on screen.

**Independent Test**: designate a player in zone A, switch to zone B, and undo — zone A is untouched.

### Implementation for User Story 2

- [X] T005 [P] [US2] In `test/GameplayStateTests.fs`, a cross-zone case: designate an actor in the first zone, `switchZone` to the other, run an undo there, and assert that the incoming zone's own history was what moved and that the first zone's designation is unchanged (FR-003; SC-003; quickstart step 5). **No implementation task**: being per zone is what `Zone.Player` and `GameplayState.UndoByZone` already mean, and `switchZone` already swaps the stacks — this task exists to make that observable in the command's presence.

**Checkpoint**: the step rides the zone's own stack; a switch cannot reach across.

---

## Phase 4: User Story 3 - The editor shows who the player is (Priority: P3)

**Goal**: the actor section reads the active zone's designation, so setting, undoing and redoing it are visible in the editor.

**Independent Test**: designate an actor, undo, and redo, and confirm the section's reading follows each.

### Implementation for User Story 3

- [X] T006 [US3] In `editor/Inspector.fs`, the actor section's control reads the active zone's designation: when the selected actor's handle is `zone.Player`, draw it through the existing `ImGui.beginDisabled`/`ImGui.endDisabled` bindings with a label that says so, and leave it enabled and labelled "Set as Player" otherwise. The bindings already exist in `render/ImGui.fs`, so no native surface is added (FR-005, Story 3; research D4; contracts/player-designation.md). (Depends on T003.)

**Checkpoint**: the editor says who the player is, and an undo or redo of the designation is visible in it.

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: The design doc that recorded the old decision, and the end-to-end walkthrough.

- [X] T007 [P] In `docs/EDITOR_DESIGN.md`, amend **principle 4** and **D-022** so the player designation leaves the direct-write exceptions (they keep the collider overlay's visualisation preference and the cast binding), and add **D-025** in the style of D-022/D-023/D-024: why the designation is a command (it is a zone's own setting, so its undo rides the zone's own stack — D-024's reasoning for the light applied to the other value of the same kind) and why the control reads its state (research D5; data-model.md).
- [ ] T008 Run the [quickstart.md](./quickstart.md) walkthrough (steps 1–7) and the full `dotnet build` + `dotnet test`; record the outcome. (The control's state, the no-op and play mode need the editor and a person.)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Foundational (Phase 1)**: no dependencies; **blocks every user story** (the Inspector has nothing to call).
- **User Story 1 (Phase 2)**: depends on Phase 1. T003 and T004 are independent of each other.
- **User Story 2 (Phase 3)**: depends on Phase 1 only — the test needs the command to exist, not the Inspector call.
- **User Story 3 (Phase 4)**: depends on T003 (it edits the same control), so it follows US1.
- **Polish (Phase 5)**: T007 is independent of the code; T008 depends on everything.

### Same-file constraint

- `editor/Inspector.fs` is edited by **T003** (US1) and **T006** (US3), sequentially — they touch the same
  control (`T006` is the state-aware half of the change `T003` makes). Everything else is one task per
  file: `state/Command.fs` (T001), `editor/Selection.fs` (T002), `test/ZoneTests.fs` (T004),
  `test/GameplayStateTests.fs` (T005), `docs/EDITOR_DESIGN.md` (T007).

### Parallel Opportunities

- **T004** and **T005** are independent tests in different files, and neither depends on the Inspector.
- **T007** (the design doc) can be written at any point.

## Implementation Strategy

### MVP

1. Phase 1: the command and the helper.
2. Phase 2 (US1): the button records a step, and the round trip is asserted.
3. **STOP and VALIDATE**: one Ctrl+Z takes the designation back and one Ctrl+Y puts it back.

That alone closes the gap the feature exists for; US2 needs no code, and US3 is what makes it visible.

### Incremental Delivery

1. Phase 1 + US1 → the designation is reversible (MVP).
2. US2 → the cross-zone behaviour is asserted rather than assumed.
3. US3 → the editor shows the designation, so the undo is visible where it is made.
4. Polish → the design doc stops contradicting the code; the walkthrough confirms the whole.

## Notes

- [P] tasks = different files, no dependencies.
- The feature adds **no** module, field, resource, shader or dependency; `world/Zone.fs` and
  `world/Systems.fs` are deliberately untouched (contracts/player-designation.md).
- T006 changes only how the control is *drawn* — the action and its recording are T003's, and the no-op
  rule is T002's, so the disabled state can never be the thing that prevents a step.
