# Feature Specification: Screens + Main Menu

**Feature Branch**: `001-screens-main-menu`

**Created**: 2026-08-27

**Status**: Draft

**Input**: User description: "Set up the app shell: a screen manager, a main menu, and a UI-only gameplay screen, navigable menu ↔ gameplay."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Launch into a main menu (Priority: P1)

A player launches the game and is presented with a main menu rather than being dropped directly into gameplay.

**Why this priority**: This is the entry point of the app shell; every other screen and later feature hangs off it.

**Independent Test**: Launch the game and confirm the main menu is the first thing shown, with a Play and a Quit option.

**Acceptance Scenarios**:

1. **Given** the game is not running, **When** the player launches it, **Then** a main menu is displayed.
2. **Given** the main menu is displayed, **When** the player looks at it, **Then** it offers at least "Play" and "Quit" options.

---

### User Story 2 - Start the game from the menu (Priority: P2)

A player chooses "Play" from the main menu and transitions to the gameplay screen.

**Why this priority**: This is the primary navigation path and proves screen transitions work.

**Independent Test**: From the main menu, select Play and confirm the view changes to a gameplay screen (currently a placeholder).

**Acceptance Scenarios**:

1. **Given** the main menu is displayed, **When** the player selects "Play", **Then** the gameplay screen is shown.
2. **Given** the gameplay screen is shown, **When** the player interacts with it, **Then** the screen responds (placeholder UI) without restarting the app.

---

### User Story 3 - Quit from menu or gameplay (Priority: P3)

A player can quit the game cleanly from either the menu or gameplay.

**Why this priority**: A clean exit path is required for a usable shell and for automated validation.

**Independent Test**: Select Quit from the menu, and separately close the window while in gameplay, and confirm the process exits without crashing.

**Acceptance Scenarios**:

1. **Given** the main menu is displayed, **When** the player selects "Quit", **Then** the game exits.
2. **Given** the gameplay screen is shown, **When** the player closes the window, **Then** the game exits.

---

### Edge Cases

- Selecting Play repeatedly must not stack or leak screens.
- If no screen is active (initial/invalid state), the app must fall back to the main menu rather than crash.
- A quit request during an active frame must be honored on the next loop iteration.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST present a main menu on launch.
- **FR-002**: The system MUST provide at least two main-menu actions: "Play" and "Quit".
- **FR-003**: The system MUST support navigating between screens (menu → gameplay) without restarting.
- **FR-004**: The system MUST dispatch per-frame update and draw to exactly one active screen.
- **FR-005**: The system MUST provide a clean quit path that shuts down and releases resources from both the menu and gameplay screens.
- **FR-006**: The gameplay screen MUST be present as a UI-only placeholder for this milestone (no 3D world).
- **FR-007**: The system MUST maintain a single source of truth for which screen is currently active.

### Key Entities *(include if feature involves data)*

- **Screen**: a distinct interactive state (Main Menu, Gameplay) with its own update and draw behaviour.
- **Screen Manager**: owns the active screen and performs transitions between screens.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Launching the game always lands on the main menu (100% of launches).
- **SC-002**: Selecting Play transitions to gameplay, and navigation is instant (no perceptible delay).
- **SC-003**: Selecting Quit (or closing the window) exits the process cleanly with no crash.
- **SC-004**: A new screen can be added without restructuring the application shell.

## Assumptions

- The gameplay screen is intentionally a UI-only placeholder in this milestone; 3D content is a later milestone.
- Navigation is driven by the existing immediate-mode UI layer, which is already integrated.
- No save/load, settings persistence, or in-game state is required for this milestone.
- The existing window/event/render loop is reused unchanged.
