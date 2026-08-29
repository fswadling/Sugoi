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

## US2 implementation — grid + viewport input (T008/T009)

The viewport's 3D scene renders directly to the swapchain as the background,
with ImGui panels drawn on top. This is the first non-ImGui GPU path, so it
pulls forward a minimal line pipeline.

### DebugDraw line pipeline (`render/DebugDraw.fs`)

- **Shader** — `shaders/line.vert` + `shaders/line.frag` compiled to SPIR-V with
  `glslc`. Vertex layout: `position : Vector3` + `color : Vector4` (28 bytes/
  vertex). One uniform: the view-projection `Matrix4x4` (row-major/row-vector,
  consistent with `System.Numerics` and later SDL_gpu uploads). No transpose is
  needed: `Matrix4x4` row-major storage + row-vector math (`v * M`) is exactly the
  transpose of a column-major GLSL `mat4` doing `u * v`, so uploading the raw
  M11..M44 floats is already correct for `vp * pos`.
- **Pipeline** — `SDL.GPUPrimitiveType.LineList`; no depth test (lines are unlit
  and drawn in submission order).
- **Buffers** — a persistent vertex buffer + a persistent staging buffer
  (`GPUTransferBufferUsage.Upload`) sized to a fixed capacity (e.g. 65536
  vertices); re-uploaded each frame via a copy pass, matching ocaml-game's
  `render.ml`.
- **Accumulator** — a pre-allocated CPU array of line vertices + a count, cleared
  each frame (`clear`), appended via `line a b color`, and populated by
  `drawGrid`. No per-frame allocation.
- **Upload/draw split** — `upload` (copy pass, before the render pass) and `draw`
  (bind pipeline + push uniform + draw, inside the render pass), matching the
  SDL_gpu command-buffer model.
- **Ownership** — the `DebugDraw` instance lives on `GameplayState` (created once
  with the device + swapchain format, reused every frame); it is gameplay-screen
  specific, not cross-screen state.
- **Shader loading** — the SPIR-V is loaded from disk at startup (like
  `ocaml-game`'s `read_file`), not embedded; `fsharp-game.fsproj` copies
  `shaders/*.spv` to the output directory (mirroring the existing
  `native/libcimgui.so` copy target).

### Grid

- Minor lines every 1 unit, major lines every 10 units, on the XZ plane
  (`y = 0`), centred at the origin, extending ±100 units.
- Major lines brighter than minor lines; the X and Z axes tinted (X red, Z blue)
  for orientation.

### Render order (Engine.fs)

Currently Engine clears and renders ImGui in one render pass. Change it to:

1. acquire the swapchain texture,
2. `DebugDraw.upload` (copy pass on the command buffer),
3. begin the render pass (clear colour),
4. `DebugDraw.draw` — draw the grid,
5. `ImGuiSdl3.renderDrawData` — draw ImGui on top,
6. end the render pass + submit.

The grid is drawn first, so ImGui panels and the docked UI composite over it;
the central dock node is passthru, so the grid shows through the viewport area.

### Viewport input (`editor/Viewport.fs`)

Input is read at the SDL level (the viewport is a passthru node, not an ImGui
window, so ImGui does not capture input there). Engine builds a `Frame` each
frame (dt + the accumulated wheel delta — the only event-only input) and hands
it to `ScreenManager.update`; the gameplay screen reads pollable input (mouse
position/buttons, held keys) from SDL state itself and applies it to the camera
(matching `ocaml-game/lib/editor/editor.ml` `update_camera` and
`docs/EDITOR_DESIGN.md`):

- **orbit** — right-drag: yaw around the up axis, pitch around the right axis
  (scaled by mouse delta).
- **dolly (zoom)** — scroll wheel: `factor = 0.9^wheel`, clamped to a minimum
  distance.
- **fly** — right-drag + `W`/`A`/`S`/`D`/`Q`/`E`: translate `Position` + `Target`
  together along camera-relative axes.

## Complexity Tracking

No constitution violations to justify.
