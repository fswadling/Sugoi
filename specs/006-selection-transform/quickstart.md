# Quickstart: Selection + Transform

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK (installed).
- Native ImGui library rebuilt after adding the `cimgui_display_size` shim
  (`bash native/build.sh`).

## Build & test

```sh
dotnet build
dotnet test   # includes test/RaycastTests.fs
```

## Validation scenarios

1. **Spawn** — open the editor, click an actor/prop in the Asset Browser; a
   wireframe box appears in the viewport and is selected (orange vs. white).
2. **Select** — click a marker to select it; click empty space to clear the
   selection; clicking a panel (Outliner/Inspector/Browser) must not change it.
3. **Focus/delete/duplicate** — press `F` (camera re-centres), `Del` (entity
   removed, selection clears), `Ctrl+D` (copy added + selected).
4. **Gizmos** — press `1`/`2`/`3` to switch move/rotate/scale; drag the axis
   handles/rings to transform; press `Q` to toggle snapping.
5. **No interference** — focus a text field and type a shortcut key; no editor
   action fires.

## Expected outcome

- Spawn → select → transform all work in one session.
- Editor holds ~60 fps while dragging a gizmo.
- `dotnet test` passes the ray-math tests.
