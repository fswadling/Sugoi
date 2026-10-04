# Data Model: Remove Editor Scroll Zoom

A removal: no new entity, and the only data change is one field leaving a struct.

## `core/Input.fs` — the per-frame input

```fsharp
[<Struct>]
type Input =
    { MouseX : float32
      MouseY : float32
      MouseDX : float32
      MouseDY : float32
      // `Wheel : float32` — REMOVED (spec 065): its only reader was the editor camera's zoom.
      LeftDown : bool
      LeftPressed : bool
      LeftReleased : bool
      RightDown : bool
      Held : KeyMask
      Pressed : KeyMask }

// The wheel parameter is removed with the field.
val gather : PrevInput -> struct (PrevInput * Input)
```

## `Engine.fs` — the event poll

- The `let mutable wheel = 0.0f` accumulator and the `MouseWheel` branch that fed it are removed; every event
  is still forwarded to `ImGuiSdl3.processEvent`.
- `Input.gather prevInput wheel` becomes `Input.gather prevInput`.

## `editor/Editor.fs` — `updateCamera`

- The wheel dolly block (`if input.Wheel <> 0.0f then Camera.dolly …`) is removed. Orbit (right-drag) and fly
  (right-drag + `WASD`/`Q`/`E`) are unchanged.

## `render/Camera.fs` — camera ops

- `orbit` and `pan` are unchanged. `dolly` is removed:

```fsharp
// let dolly (c: byref<Camera>) (factor: float32) : unit = …   // REMOVED (spec 065)
```

## Tests and docs

- `test/InputTests.fs` (3 literals), `test/PlayerTests.fs` (1 literal): the `Wheel = 0.0f` field assignment is
  removed.
- `test/CameraTests.fs`: the two `dolly` tests (`dolly scales the distance`, `dolly clamps to a minimum
  distance`) are removed; the `orbit` and `pan` tests remain.
- `docs/EDITOR_DESIGN.md`: the interaction model drops "scroll zooms".

## Validation rules

- **No zoom input remains**: no code path moves the camera from the wheel (spec FR-001).
- **Panel scrolling unaffected**: wheel events still reach ImGui (FR-002).
- **Navigation unchanged**: orbit and fly behave as before (FR-003).
- **No dead path**: `Input` carries no wheel value and `Camera` offers no `dolly` (FR-004, FR-005).
