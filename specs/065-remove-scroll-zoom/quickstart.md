# Quickstart: Remove Editor Scroll Zoom

## Prerequisites

- .NET 10 SDK; the vendored native libraries present (`native/`).

## Automated (headless)

```bash
dotnet build
dotnet test
```

Expected: a clean build with no warnings and the suite green, with no device opened. The change removes rather
than adds behaviour, so the assertions are mostly that nothing regressed:

- `test/CameraTests.fs` — `orbit` preserves the target distance and `pan` moves position and target together
  (the two `dolly` tests are gone with the function).
- `test/InputTests.fs` and `test/PlayerTests.fs` — the input literals compile and the key/edge helpers
  (`isCtrl`, `isShift`, `isPressed`) still behave.
- The whole build proves no code references `Input.Wheel` or `Camera.dolly` any more.

## Manual

1. **Scroll a panel**: open the editor; scroll the pointer over the Outliner, the Inspector, or a long Cue.
   The panel scrolls and the 3D view does **not** move.
2. **Scroll the viewport**: with the pointer over the 3D view, turn the wheel. Nothing happens (there is no
   zoom anywhere).
3. **Navigate as before**: right-drag to orbit; hold a key and right-drag to fly (`WASD`, `Q`/`E` up/down).

## References

- Data model: [data-model.md](./data-model.md)
- Research (the design decisions): [research.md](./research.md)
