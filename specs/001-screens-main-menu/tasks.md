# Tasks: Screens + Main Menu

**Input**: Design documents from `/specs/001-screens-main-menu/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/screen.md, quickstart.md

**Tests**: Not requested; validation is manual via `quickstart.md`.

## Phase 1: Setup

**Purpose**: Confirm the baseline.

- [X] T001 Verify baseline: `dotnet build` succeeds and `dotnet run` shows the ImGui demo window.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The app shell structure all screens depend on.

- [X] T002 Extract the run loop from `Program.fs` into `Engine.fs` (`Engine.run`), leaving `Program.fs` as `[<EntryPoint>] main → Engine.run`; update `fsharp-game.fsproj` compile order.
- [X] T003 Move `ImGui.fs` → `render/ImGui.fs` and `ImGuiSdl3.fs` → `render/ImGuiSdl3.fs` (module names unchanged); update `fsharp-game.fsproj` compile order.
- [X] T004 [P] Create `screen/Screen.fs` defining `ScreenKind` (`MainMenu` | `Gameplay`) and `ScreenTransition` (`None` | `Next of ScreenKind` | `Quit`); add to `fsharp-game.fsproj`.
- [X] T005 [P] Create `screen/SharedState.fs` (empty record, the seam for later phases); add to `fsharp-game.fsproj`.
- [X] T006 Create `screen/ScreenManager.fs` (owns the active `ScreenKind` + `SharedState` + `GameplayState`, dispatches `update`/`draw` to the active screen, applies `ScreenTransition` at end of frame); add to `fsharp-game.fsproj`.

**Checkpoint**: The shell compiles and the loop dispatches through `ScreenManager`.

---

## Phase 3: User Story 1 - Launch into a main menu (Priority: P1) 🎯 MVP

**Goal**: The game launches into a main menu with Play and Quit.

**Independent Test**: `dotnet run` shows the main menu with "Play" and "Quit".

### Implementation for User Story 1

- [X] T007 [US1] Create `screen/MainMenuScreen.fs` with `draw` rendering "Play" and "Quit" buttons via ImGui; add to `fsharp-game.fsproj`.
- [X] T008 [US1] Wire `Engine`/`ScreenManager` to start on `MainMenu` and dispatch its `draw`; verify launch lands on the menu.

**Checkpoint**: Launch → main menu (US1 complete).

---

## Phase 4: User Story 2 - Start the game from the menu (Priority: P2)

**Goal**: Selecting Play transitions to a UI-only gameplay screen.

**Independent Test**: From the menu, select Play and see the gameplay placeholder.

### Implementation for User Story 2

- [X] T009 [US2] Create `screen/GameplayState.fs` (empty record); add to `fsharp-game.fsproj`.
- [X] T010 [US2] Create `screen/GameplayScreen.fs` with a UI-only placeholder `draw`; add to `fsharp-game.fsproj`.
- [X] T011 [US2] In `screen/MainMenuScreen.fs`, return `Next Gameplay` when Play is pressed; `ScreenManager` switches to `Gameplay`.

**Checkpoint**: Menu → Play → gameplay placeholder (US2 complete).

---

## Phase 5: User Story 3 - Quit from menu or gameplay (Priority: P3)

**Goal**: Quit cleanly from either screen.

**Independent Test**: Select Quit from the menu, and separately close the window during gameplay; both exit cleanly.

### Implementation for User Story 3

- [X] T012 [US3] In `screen/MainMenuScreen.fs`, return `Quit` when Quit is pressed; `ScreenManager`/`Engine` exit the loop.
- [X] T013 [US3] In `Engine.fs`, honor the window-close event from any screen to exit the loop.

**Checkpoint**: Quit/close exits cleanly (US3 complete).

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T014 Run `quickstart.md` validation end-to-end (launch → Play → Quit).
- [X] T015 Confirm `dotnet build` is clean (2 expected native-pointer warnings).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; blocks all user stories.
- **US1 (Phase 3)**: depends on Foundational.
- **US2 (Phase 4)**: depends on Foundational and US1 (Play wiring lives in the menu).
- **US3 (Phase 5)**: depends on Foundational and US1/US2 (quit paths).
- **Polish (Phase 6)**: depends on all stories.

### Parallel Opportunities

- T004 and T005 (new `screen/` type files) are independent of each other once the
  `screen/` directory exists; T006 (manager) depends on both.
- T003 (moves) is independent of T004/T005/T006.

## Implementation Strategy

### MVP First (User Story 1)

1. T001 → T002 → T003–T006 (foundation).
2. T007–T008 (US1) → validate launch-to-menu.

Then US2 and US3 incrementally, each independently testable.
