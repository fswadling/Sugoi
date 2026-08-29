# Quickstart: Editor Workspace Shell

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK (installed).
- Native ImGui library built (`./native/build.sh`, already done).

## Build & run

```sh
dotnet build
dotnet run
```

## Validation scenarios

1. **Open the workspace** — launch, select Play; expect a dockable editor
   workspace with a menu bar and the Viewport, Outliner, Inspector, and Asset
   Browser panels.
2. **Navigate the viewport** — drag/scroll in the viewport; expect the grid to
   orbit (pitch/yaw) and pan/zoom.
3. **Browse assets** — the Asset Browser lists assets under `assets/` (empty list
   if none).
4. **Quit** — close the window; expect a clean exit.

## Expected outcome

- Play opens the dockable editor workspace (US1).
- The viewport shows a grid and responds to camera input (US2).
- The asset browser lists discovered assets (US3).
