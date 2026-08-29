# Implementation Plan: Editor Workspace Shell

**Branch**: `003-editor-workspace` | **Date**: 2026-08-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-editor-workspace/spec.md`

## Summary

Build the editor workspace shell: a dockable ImGui layout (menu bar + Viewport,
Outliner, Inspector, Asset Browser) launched from the main menu, with a 3D
viewport that renders a reference grid through an orbit camera, and an asset
browser backed by a disk-scanning asset manager. No entity editing yet (2c).

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`)

**Primary Dependencies**: SDL3-CS (GPU), custom Dear ImGui binding (docking),
`System.Numerics` (camera math); no new runtime dependencies

**Storage**: assets read from `assets/` (JSON manifests); no writes this phase

**Testing**: manual via `dotnet run` (UI is visual); no unit tests this phase

**Target Platform**: Linux x64 desktop (native)

**Project Type**: native desktop game

**Performance Goals**: 60 fps while rendering the grid + ImGui

**Constraints**: no per-frame allocation on the render/draw hot path

**Scale/Scope**: one docking workspace, one orbit camera, one grid, one asset list

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: only the four panels the editor needs; no speculative
  panels or tools.
- **II. Fail Loudly** — PASS: malformed asset manifests raise rather than being
  silently dropped.
- **III. Spec-First** — PASS: derives from `spec.md`.
- **IV. Data-Oriented** — PASS: `GameplayState` holds the camera + editor state as
  data; `Editor` is behaviour; no class hierarchy.
- **V. Minimise GC Pressure** — PASS: camera math uses `System.Numerics` structs;
  no per-frame allocation.

## Project Structure

### Documentation (this feature)

```text
specs/003-editor-workspace/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── editor-workspace.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
core/Assets.fs            # asset scan + JSON manifests
render/Camera.fs          # orbit camera -> view-projection
render/DebugDraw.fs       # line primitive + grid
editor/EditorState.fs     # transient editor UI state
editor/Viewport.fs        # 3D viewport (grid + orbit)
editor/Outliner.fs        # left tree panel (empty)
editor/Inspector.fs       # right panel (no selection)
editor/AssetBrowser.fs    # bottom panel (asset list)
editor/Editor.fs          # docking + menu bar + orchestration

screen/GameplayState.fs   # grows: camera + editor state + editing flag
screen/GameplayScreen.fs  # grows: calls Editor.draw instead of placeholder
```

**Structure Decision**: `core/`, `render/`, and `editor/` follow
`docs/PROJECT_STRUCTURE.md`. The one wrinkle is the screen↔editor dependency:
`GameplayScreen` (behaviour) calls `Editor` (behaviour), so the `.fsproj` order
must interleave the clusters as `core → render → screen/data → editor →
screen/behaviour → Engine` (see `research.md`, Decision 3).

## Complexity Tracking

No constitution violations to justify.
