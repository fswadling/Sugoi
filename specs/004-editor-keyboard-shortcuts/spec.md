# Feature Specification: Editor Keyboard Shortcuts

**Feature Branch**: `004-editor-keyboard-shortcuts`

**Created**: 2026-08-29

**Status**: Draft

**Input**: User description: "get the editor keyboard shortcuts working (port ocaml-game's shortcut handling)"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - App shortcuts yield while typing (Priority: P1)

A developer is editing a text field in the editor (e.g. an Inspector property).
Keyboard shortcuts must not fire while they are typing, so the letters they type
are not stolen by an app-level shortcut.

**Why this priority**: It is the guard every other shortcut depends on. Without
it, any letter-key shortcut (or the backtick toggle) would misfire whenever the
user types in a text field.

**Independent Test**: focus a text field, type a shortcut key (e.g. the backtick
or a letter), and confirm the text field receives the character and no shortcut
fires.

**Acceptance Scenarios**:

1. **Given** a text field has keyboard focus, **When** the user types a key that
   is also bound to a shortcut, **Then** the character is entered into the field
   and no shortcut fires.
2. **Given** no text field has keyboard focus, **When** the user presses a bound
   shortcut key, **Then** the shortcut fires normally.

---

### User Story 2 - Toggle edit and play mode (Priority: P1)

A developer switches between editing the scene and viewing it by pressing the
backtick (`) key. In edit mode the editor workspace is shown; in play mode it is
hidden and only the 3D viewport remains visible.

**Why this priority**: It is the live-edit seam the whole editor is built around
and already partially works; this story brings it under the shared shortcut
mechanism and the text-input guard.

**Independent Test**: press backtick and observe the editor workspace hide/show
while the viewport grid stays visible.

**Acceptance Scenarios**:

1. **Given** the editor is in edit mode, **When** the user presses backtick,
   **Then** the editor workspace hides and the viewport grid stays visible.
2. **Given** the editor is in play mode, **When** the user presses backtick,
   **Then** the editor workspace reappears.
3. **Given** a text field has focus, **When** the user presses backtick, **Then**
   the character is typed and the mode does not toggle.

---

### User Story 3 - Cycle docked panel focus (Priority: P2)

A developer moves keyboard focus between the docked editor panels (Outliner,
Inspector, Asset Browser) without reaching for the mouse.

**Why this priority**: panel focus is a prerequisite for keyboard-driven editor
navigation and follows naturally once the shortcut mechanism exists.

**Independent Test**: press Ctrl+Tab and Ctrl+Shift+Tab and observe focus move
forward and backward through the docked panels.

**Acceptance Scenarios**:

1. **Given** the editor workspace is open, **When** the user presses Ctrl+Tab,
   **Then** focus moves to the next docked panel.
2. **Given** the editor workspace is open, **When** the user presses
   Ctrl+Shift+Tab, **Then** focus moves to the previous docked panel.
3. **Given** focus is on the last panel, **When** the user presses Ctrl+Tab,
   **Then** focus wraps to the first panel.

---

### User Story 4 - Return to the main menu (Priority: P3)

A developer leaves the editor and returns to the main menu with a single key
press.

**Why this priority**: it is a small quality-of-life shortcut that rounds out the
navigation surface; quitting already works through the File menu.

**Independent Test**: press Escape in the editor and observe the main menu appear.

**Acceptance Scenarios**:

1. **Given** the editor workspace is open, **When** the user presses Escape,
   **Then** the application returns to the main menu.
2. **Given** a text field has focus, **When** the user presses Escape, **Then**
   the text field is dismissed first (or the character is otherwise handled by
   the field) and the editor does not navigate away.

---

### Edge Cases

- Holding a shortcut key across a screen transition must not register as a fresh
  press on the next screen (no "stuck" key).
- Shortcuts must not fire while a text field has focus (the P1 guard).
- Ctrl+Tab must work with either the left or right Ctrl key.
- Ctrl+Shift+Tab must work with either Shift key.
- Escape while editing a text field must not leave the editor unexpectedly.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The editor MUST maintain a set of currently-held keys and a set of
  newly-pressed keys for each frame.
- **FR-002**: App-level shortcuts MUST yield while a text field is being edited
  (i.e. while text input is captured).
- **FR-003**: The backtick (`) key MUST toggle between edit and play mode, and
  MUST NOT fire while a text field is being edited.
- **FR-004**: Ctrl+Tab MUST move focus to the next docked panel; Ctrl+Shift+Tab
  MUST move focus to the previous docked panel, wrapping at the ends.
- **FR-005**: Escape MUST return to the main menu, except while a text field is
  being edited.
- **FR-006**: The same held/pressed key state MUST be reusable by the camera
  controls and later editor shortcuts (selection/transform/undo) without a
  per-key bespoke input field.

### Key Entities

- **Shortcut**: a key combination (optionally with Ctrl/Shift modifiers) mapped
  to a single editor action.
- **Keyboard input state**: the set of keys held this frame and the set of keys
  newly pressed this frame (edge-triggered), derived once per frame.
- **Modifier state**: whether Ctrl or Shift is held, used to disambiguate
  shortcuts (e.g. Tab vs Ctrl+Tab, Ctrl+Tab vs Ctrl+Shift+Tab).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every shortcut in scope (backtick, Ctrl+Tab, Ctrl+Shift+Tab,
  Escape) fires reliably on a single key press without requiring a mouse.
- **SC-002**: Typing in a text field never triggers an app-level shortcut.
- **SC-003**: Panel focus wraps cleanly at both ends without getting stuck or
  skipping a panel.
- **SC-004**: The editor remains at 60 fps with the added key-state bookkeeping
  (no per-frame allocation in the input gather path).

## Assumptions

- Selection, transform, tool-switch (`1`/`2`/`3`), snap (`Q`), focus (`F`),
  delete (`Del`), duplicate (`Ctrl+D`), keyboard nudge (`Shift+WASD/QE`), and
  undo/redo (`Ctrl+Z`/`Ctrl+Shift+Z`) are out of scope here: they require the
  selection/transform model (phase 2c) and the command/undo stack (phase 2d).
  This feature builds the shared shortcut mechanism they will reuse.
- The Escape-to-menu and Ctrl+Tab panel-cycle shortcuts are the only *new*
  editor actions in scope; the backtick toggle already exists and is migrated
  onto the shared mechanism.
- The existing polling input model (SDL keyboard state read each frame) is
  retained rather than switching to event-driven key delivery.
