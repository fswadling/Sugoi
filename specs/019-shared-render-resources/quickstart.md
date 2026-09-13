# Quickstart: Shared Render Resources

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK and SDL3; the app runs a windowed SDL_gpu app.

## Build & test

```sh
dotnet build
dotnet test test/fsharp-game.Tests.fsproj
```

## Validation scenarios

1. **No hardware in screen state (SC-001)** — inspect `state/GameplayState.fs`:
   the type has no pipeline, format, or depth-target field. Its fields are the
   zone, camera, per-screen buffers, and editor/play state.
2. **Single owner (SC-002)** — search the source for `MeshPipeline` and
   `StaticPipeline`: each is declared once (on the GPU context) and only read
   elsewhere. No screen holds a copy.
3. **Identical rendering (SC-003)** — load the ArtRoom and play: the frame log
   reports the same `draws 191 instances 521` as before the change, and the scene
   looks the same. Add a prop: `draws` rises only by the prop asset's distinct
   submeshes, as before.
4. **Depth target still works (FR-005)** — resize the window while playing: the
   depth target is recreated through shared state and rendering continues with no
   error or artifacts.
5. **Menu unaffected** — the menu screen (no 3D scene) still shows and the app
   transitions between menu and gameplay as before.
6. **Per-screen buffers stay put (SC-005)** — confirm the debug lines, gizmos, and
   the static-scene batch are still fields of the gameplay screen's state and
   still work (grid visible, gizmos draw).
7. **Clean lifecycle (SC-004)** — start and quit the app: it shuts down cleanly
   with the same creation/release ordering relative to the device.
8. **No per-frame regression (FR-010)** — the per-phase log shows no added
   per-frame work (`record`/`update` unchanged).

## Expected outcome

- Device-level render resources live only in shared state; the gameplay screen's
  state describes the screen, not the hardware.
- Rendering, draw counts, resize behaviour, and shutdown are unchanged.
- `dotnet build` is clean; `dotnet test` passes.
