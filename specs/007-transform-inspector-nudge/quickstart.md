# Quickstart: Transform Inspector + Keyboard Nudge

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK (installed).
- Sample assets under `assets/` (see `specs/006-selection-transform/quickstart.md`).

## Build & test

```sh
dotnet build
dotnet test   # includes the eulerDegrees test in test/EntityTests.fs
```

## Validation scenarios

1. **Inspect** — spawn an actor/prop, select it, and confirm the Inspector shows
   name, position, rotation (degrees), and scale.
2. **Gizmo feedback** — press `2` (rotate) and drag a ring; the Inspector rotation
   changes. Press `3` (scale) and drag an axis; the Inspector scale changes.
3. **Nudge** — with a selection, hold Shift and tap W/A/S/D/Q/E in each tool
   (`1`/`2`/`3`); the Inspector values change by the step. Toggle `Q` (without
   Shift) to snap and re-nudge to see the larger increments.
4. **No conflict** — hold Shift and press Q: snap does not toggle. Right-drag with
   Shift held and press W: the camera does not fly.
5. **No interference** — focus a text field and type W/Q/Shift+W; no nudge or
   shortcut fires.

## Expected outcome

- Rotate/scale gizmos produce a visible change in the Inspector.
- Shift+WASD/QE nudges the selection without flying the camera or toggling snap.
- `dotnet test` passes.
