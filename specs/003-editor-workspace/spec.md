# Feature Specification: Editor Workspace Shell

**Feature Branch**: `003-editor-workspace`

**Created**: 2026-08-29

**Status**: Draft

**Input**: User description: "Editor workspace shell: dockable editor with 3D viewport, outliner, inspector, asset browser"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Open the editor workspace (Priority: P1)

A developer launches the game and selects Play, landing in a dockable editor
workspace (menu bar + panels around a central 3D area) rather than a placeholder
screen.

**Why this priority**: the workspace is the shell every later editor feature
(selection, gizmos, cue/zone editors) docks into.

**Independent Test**: launch → menu → Play shows the dockable workspace with a
menu bar and the Viewport, Outliner, Inspector, and Asset Browser panels.

**Acceptance Scenarios**:

1. **Given** the main menu, **When** Play is selected, **Then** the editor
   workspace is shown.
2. **Given** the editor workspace, **When** it is inspected, **Then** it presents
   a menu bar and four dockable panels (Viewport, Outliner, Inspector, Asset
   Browser).

---

### User Story 2 - Navigate the 3D viewport (Priority: P2)

The central viewport shows a reference grid that can be orbited (pitch/yaw) and
panned/zoomed.

**Why this priority**: the viewport is the primary editing surface; camera
navigation is required before any placing/selecting (2c).

**Independent Test**: from the workspace, drag/scroll in the viewport and see the
grid rotate/pan/zoom.

**Acceptance Scenarios**:

1. **Given** the viewport, **When** viewed, **Then** a 3D reference grid is
   visible.
2. **Given** the viewport, **When** the camera is dragged, **Then** the grid
   orbits (pitch/yaw).
3. **Given** the viewport, **When** the camera is panned/zoomed, **Then** the
   grid translates/scales accordingly.

---

### User Story 3 - Browse discovered assets (Priority: P3)

The Asset Browser lists assets discovered from the assets directory.

**Why this priority**: assets must be listed before they can be dragged/spawned
(2c), and the browser proves the asset-manager seam.

**Independent Test**: with assets present on disk, the browser lists them (by
name/kind); with none, it shows an empty list.

**Acceptance Scenarios**:

1. **Given** the workspace, **When** opened, **Then** the Asset Browser panel is
   visible.
2. **Given** assets under the assets directory, **When** the browser is shown,
   **Then** it lists each discovered asset.

---

### Edge Cases

- With no assets on disk, the browser shows an empty list (no crash).
- Closing the window while in the editor exits cleanly.
- Selecting Play repeatedly must not stack duplicate editor workspaces.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST open the editor workspace when Play is selected
  from the menu.
- **FR-002**: The editor MUST present a dockable workspace with a menu bar and
  four panels (Viewport, Outliner, Inspector, Asset Browser).
- **FR-003**: The Viewport MUST render a 3D reference grid.
- **FR-004**: The Viewport MUST support orbit navigation (pitch, yaw, pan, zoom).
- **FR-005**: The Asset Browser MUST list assets discovered from the assets
  directory.
- **FR-006**: The Outliner and Inspector MUST be present and empty (no selection)
  this milestone.
- **FR-007**: The editor MUST support toggling between edit and play modes with
  the backtick (`` ` ``) key. In edit mode the docking workspace (menu bar +
  panels) is shown; in play mode the workspace is hidden and only the 3D viewport
  (grid) is shown.

### Key Entities

- **Editor Workspace**: the dockable layout (menu bar + dock space + panels).
- **Viewport**: the central 3D scene area with a reference grid.
- **Camera**: a look-at orbit camera (`Position` + `Target` + `Up`) producing
  the view-projection; orbit/pan/dolly update `Position`/`Target`.
- **Outliner / Inspector / Asset Browser**: dockable panels (empty / no-selection
  / asset list).
- **Asset**: a discovered asset (id, name, kind) from disk.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Play from the menu opens the editor workspace on every launch.
- **SC-002**: The viewport grid renders and responds to orbit/pan/zoom input.
- **SC-003**: The asset browser lists every asset discovered under the assets
  directory (or an empty list when none).

## Assumptions

- No entity spawning/selection yet (2c); the Outliner/Inspector are empty.
- There is no gameplay yet; the backtick (`` ` ``) key toggles edit/play mode. In
  edit mode the editor workspace is shown; in play mode only the 3D viewport is
  shown (the editor workspace is hidden). The grid stays visible in both modes.
- The 3D scene renders directly to the window (not an offscreen texture).
- Assets are scanned from `actors/`, `environments/`, and `props/` subdirectories
  with JSON manifests keyed by id.
