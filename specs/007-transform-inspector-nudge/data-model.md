# Data Model: Transform Inspector + Keyboard Nudge

## Transform (`core/Entity.fs`)

```fsharp
[<Struct>]
type Transform =
    { Position : Vector3
      Rotation : Quaternion   // stored as a quaternion
      Scale : Vector3 }
```

The Inspector reads `Position` and `Scale` directly and `Rotation` through a new
converter:

```fsharp
// ZYX Euler angles in degrees, for display.
eulerDegrees : Quaternion -> Vector3
```

- `eulerDegrees Quaternion.Identity = Vector3.Zero`.
- Output order is `(x, y, z)`, matching the reference's `to_rot3_zyx`.

## Nudge direction (`editor/Editor.fs`)

A private value type mapping held keys to a camera-relative direction.

```fsharp
type NudgeDir = Forward | Back | Left | Right | Up | Down

nudgeDirOfKey : Key -> NudgeDir option
// W -> Forward, S -> Back, A -> Left, D -> Right, Q -> Down, E -> Up
```

## Nudge transform (`editor/Editor.fs`)

```fsharp
nudgeTransform : Camera -> Tool -> bool -> Transform -> NudgeDir list -> Transform
```

Camera-relative basis `f`/`r`/`u` from `Camera.forward/right/up`:

- **Move**: each dir adds `step * dirVec` to `Position` (Forward→`f`, Back→`-f`,
  Right→`r`, Left→`-r`, Up→`u`, Down→`-u`).
- **Rotate**: each dir pre-multiplies `Rotation` by `Quat(axis, sign*step)`:
  Forward/Back→axis `r`, Left/Right→axis `f`, Down/Up→axis `u`; sign +1 for
  Forward/Right/Up, -1 otherwise.
- **Scale**: only Forward/Back matter; Forward multiplies `Scale` by `(1+step)`,
  Back divides by `(1+step)`.

Steps (snap in parentheses): move 0.05 (0.5), rotate 0.02 rad (π/12), scale 0.01
(0.25).

## Nudge update (`editor/Editor.fs`)

```fsharp
updateNudge : GameplayState -> Input -> GameplayState
```

- Active only when `Input.isShift input`, not `ImGui.wantTextInput()`, and
  `Editor.Drag = None`.
- Maps held WASD/QE keys to directions and applies `nudgeTransform` to the
  selection via the existing `setSelectedTransform`.
- A no-op when there is no selection.
