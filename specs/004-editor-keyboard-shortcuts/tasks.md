# Tasks: Editor Keyboard Shortcuts

**Input**: Design documents from `/specs/004-editor-keyboard-shortcuts/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/editor-keyboard-shortcuts.md, quickstart.md

**Tests**: Not requested — validation is manual (visual UI) via `quickstart.md`. Pure input helpers may get unit tests if they aid confidence, but they are optional.

## Phase 1: Setup

**Purpose**: Confirm the baseline.

- [X] T001 Verify baseline: `dotnet build` succeeds and `dotnet run` launches into the editor workspace.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The key abstraction + input gather + the text-input guard every story builds on.

- [X] T002 Add the `Key` discriminated union to `core/Input.fs` (`Escape`, `Tab`, `Backtick`, `Delete`, `W/A/S/D/Q/E/F/Z`, `Digit of int`, `LeftCtrl/RightCtrl`, `LeftShift/RightShift`) plus `Key.mask : Key -> uint32` (`1u <<< case-index`).
- [X] T003 Add the context-safe `cimgui_want_text_input()` getter to `native/cimgui_sdl3.cpp` (return `false` when `ImGui::GetCurrentContext() == nullptr`, else `io.WantTextInput`) and rebuild `libcimgui.so` via `native/build.sh`.
- [X] T004 Bind `ImGui.wantTextInput()` in `render/ImGui.fs` (P/Invoke against `cimgui_want_text_input`).
- [X] T005 Add `PrevInput` (with `Held : uint32`) to `core/Input.fs`, owned by the engine (replaces `GameplayState.PrevBacktick : bool`).
- [X] T006 Add the SDL scancode→mask mapping and `Input.gather` in `core/Input.fs` (called once per frame by `Engine.fs`): `Held : uint32` and `Pressed : uint32` (`pressed = held &&& ~~~PrevInput.Held`), plus `isHeld`/`isPressed`/`isCtrl`/`isShift` helpers.
- [X] T007 Confirm no per-screen `activate` re-sync is needed: `PrevInput` advances every frame, so a key held across a screen switch doesn't register as a fresh press.

**Checkpoint**: the shell compiles with the new `Key` + input-state model; existing camera controls still work off the same `Held` set.

---

## Phase 3: User Story 1 - App shortcuts yield while typing (Priority: P1) 🎯 MVP

**Goal**: shortcuts never fire while a text field is being edited.

**Independent Test**: focus a text field and press a shortcut key; the field receives the character and no shortcut fires.

### Implementation for User Story 1

- [X] T008 [US1] Add a `shortcuts` entry point in `editor/Editor.fs` that returns state unchanged when `ImGui.wantTextInput()` is true, and otherwise dispatches the in-scope shortcuts.
- [X] T009 [US1] Call the shortcut handler from `GameplayScreen.update` before camera input, threading the `ScreenTransition` it may produce.

**Checkpoint**: shortcut dispatch is gated on text input; no shortcut fires while typing.

---

## Phase 4: User Story 2 - Toggle edit and play mode (Priority: P1)

**Goal**: backtick toggles edit/play, migrated onto the shared mechanism.

**Independent Test**: press backtick; the workspace hides/shows while the grid stays visible.

### Implementation for User Story 2

- [X] T010 [US2] Implement the backtick toggle in `editor/Editor.fs` using `Pressed` (not the old `BacktickPressed` field), gated by `ImGui.wantTextInput()`.

**Checkpoint**: backtick still toggles, now through the shared shortcut path and no longer firing while typing.

---

## Phase 5: User Story 3 - Cycle docked panel focus (Priority: P2)

**Goal**: `Ctrl+Tab` / `Ctrl+Shift+Tab` move focus through the docked panels, wrapping.

**Independent Test**: press Ctrl+Tab and Ctrl+Shift+Tab; focus moves forward/backward and wraps.

### Implementation for User Story 3

- [X] T011 [US3] Bind `ImGui.setWindowFocus(string)` and `ImGui.isWindowActive(string)` in `render/ImGui.fs` (cimgui `igSetWindowFocus`/`igIsWindowActive`; no new C++).
- [X] T012 [US3] Implement panel cycling in `editor/Editor.fs`: a module-level panel list (`Outliner`, `Inspector`, `Asset Browser`), a `pending_focus` request set on `Ctrl+Tab`/`Ctrl+Shift+Tab` in update, and an `applyPendingFocus` called at the start of `Editor.draw` (where `SetWindowFocus` is valid).

**Checkpoint**: focus cycles forward and backward through the panels and wraps at the ends.

---

## Phase 6: User Story 4 - Return to the main menu (Priority: P3)

**Goal**: `Escape` returns to the main menu.

**Independent Test**: press Escape in the editor; the main menu appears.

### Implementation for User Story 4

- [X] T013 [US4] In `editor/Editor.fs`, return a `ScreenTransition.Next MainMenu` when `Escape` is pressed and not gated by text input.

**Checkpoint**: Escape leaves the editor for the menu.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T014 Run `quickstart.md` validation end-to-end (launch → toggle → cycle → escape → typing guard → quit).
- [X] T015 Confirm `dotnet build` is clean (the 2 expected native-pointer warnings), `dotnet test` still passes, and `Input.gather` performs no per-frame allocation (the `uint32` bitmask path).
- [X] T016 Add optional unit tests for the pure helpers (`Key.mask` uniqueness, `isCtrl`/`isShift`, `pressed = held &&& ~~~PrevInput.Held`) in `test/InputTests.fs` and register it in `test/fsharp-game.Tests.fsproj`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; blocks all user stories.
- **US1 (Phase 3)**: depends on Foundational (the gate is the dispatch seam).
- **US2 (Phase 4)**: depends on US1 + Foundational.
- **US3 (Phase 5)**: depends on US1 + Foundational (needs T011 bindings first).
- **US4 (Phase 6)**: depends on US1 + Foundational.
- **Polish (Phase 7)**: depends on all stories.

### Parallel Opportunities

- US2, US3, and US4 are independent once US1 and Foundational land; they touch
  different parts of `editor/Editor.fs` but can be implemented sequentially
  within the same file without conflicts.
- T002 (Key DU), T003/T004 (ImGui guard) touch different files and can proceed
  in parallel.

## Implementation Strategy

### MVP First (User Story 1)

1. Foundational (T002–T007) → the shell compiles with the new input model.
2. US1 (T008–T009) → the gate is in place.
3. US2 (T010) → backtick works through the shared path.

Then US3 and US4 incrementally, each independently testable.

---

## Phase 8: Convergence

- [ ] T017 Run the interactive `quickstart.md` validation end-to-end (launch → toggle edit/play → cycle panels → escape to menu → typing guard → quit) and confirm each scenario passes per SC-001/SC-003 (partial)
- [ ] T018 Remove the unused `KeyMask.add` helper in `core/Input.fs` (or add a test exercising it) per Constitution I (unrequested)
