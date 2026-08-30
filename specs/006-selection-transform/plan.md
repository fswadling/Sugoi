# Implementation Plan: Selection + Transform

**Branch**: `006-selection-transform` | **Date**: 2026-08-30 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/006-selection-transform/spec.md`

## Summary

Port ocaml-game's viewport interaction: a minimal screen-ray (`editor/Raycast.fs`)
for click-to-select, `F`/`Del`/`Ctrl+D` edits, translate/rotate/scale gizmos with
axis picking, and asset-browser spawning. Builds on the 005 foundations (zone,
slot-map index access, editor transform state, debug-draw shapes).

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`)

**Primary Dependencies**: `System.Numerics` (ray/transform math), SDL3-CS (input),
the cimgui binding (one new shim: `io.DisplaySize`). No new packages.

**Storage**: in-memory only (the `Zone` on `GameplayState`).

**Testing**: xUnit for the pure `Raycast` math (`test/RaycastTests.fs`); the UI
interaction is validated manually via `quickstart.md`.

**Target Platform**: Linux x64 desktop (native).

**Project Type**: native desktop game.

**Performance Goals**: 60 fps while drawing markers + gizmos; no per-frame
allocation in the marker/gizmo draw path.

**Constraints**: `GameplayState` is a `[<Struct>]`; the editor stays a layer that
reads/mutates gameplay state without owning it; viewport interaction yields to
ImGui mouse capture.

**Scale/Scope**: one active zone, up to 64 actors + 512 props, one selection, one
gizmo.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: one ray module, three `Zone` helpers (marked
  temporary), three gizmo draw/pick functions, and the shortcuts the roadmap asks
  for. No speculative physics/BVH picking.
- **II. Fail Loudly** — PASS: ray math returns `Option` (miss = `None`); removing
  an absent entity is a guarded no-op; writing to an invalid index raises.
- **III. Spec-First** — PASS: derives from `spec.md`.
- **IV. Data-Oriented** — PASS: markers/gizmos append to the preallocated
  `DebugDraw` accumulator; selection/tool/drag are a small record, not objects.
- **V. Minimise GC Pressure** — PASS: the ray + gizmo math uses `System.Numerics`
  structs; the draw path allocates nothing.

## Project Structure

### Documentation (this feature)

```text
specs/006-selection-transform/
├── plan.md              # This file
├── research.md          # Phase 0 decisions
├── data-model.md        # Phase 1 data/behaviour model
├── quickstart.md        # Phase 1 validation guide
├── contracts/
│   └── selection-transform.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
render/ImGui.fs          # + displaySize + wantCaptureMouse bindings
native/cimgui_sdl3.cpp   # + cimgui_display_size() + cimgui_want_capture_mouse() shims (rebuild libcimgui.so)
core/Input.fs           # + Input.MouseX / MouseY / LeftDown / PrevInput
screen/Screen.fs         # + Frame.WindowW / WindowH / Input
screen/GameplayScreen.fs # consume frame.Input; set Camera.Aspect from frame; call Editor.update
core/Zone.fs             # + entityAt / setEntity / removeEntity / spawn* (temporary)
editor/Raycast.fs        # NEW: ray + intersect_aabb + plane_intersect + ray_segment_distance
editor/Viewport.fs       # + buildLines / drawGizmo / ray / pick / gizmoAxisAt
editor/Editor.fs         # + tool/snap/focus/delete/duplicate + interact/applyDrag/finishDrag
editor/Outliner.fs       # + list/select/delete/duplicate
editor/AssetBrowser.fs   # + click-to-spawn
editor/Inspector.fs      # + read-only name/transform for the selection
test/RaycastTests.fs     # NEW: pure ray-math tests
```

**Structure Decision**: `editor/Raycast.fs` sits before `editor/Viewport.fs` (the
viewport consumes it) and before `editor/Editor.fs` (the editor consumes both).
`core/Zone.fs` gains the entity kind-dispatch helpers that 2d's `Command.fs` will
adopt; they are marked temporary.

## Complexity Tracking

No constitution violations to justify.
