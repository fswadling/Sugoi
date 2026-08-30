# Contract: Transform Inspector + Keyboard Nudge

Public surfaces added by this feature. Signatures are illustrative F#, matching
the data model.

## Transform display (`core/Entity.fs`)

```fsharp
// ZYX Euler angles in degrees, for display.
eulerDegrees : Quaternion -> Vector3
```

## Nudge (`editor/Editor.fs`)

```fsharp
updateNudge : GameplayState -> Input -> GameplayState
```

## Rules

- `eulerDegrees Quaternion.Identity = Vector3.Zero`.
- `updateNudge` is a no-op when: no selection, an ImGui text field has focus, or a
  gizmo drag is in progress.
- `updateNudge` fires only while `Input.isShift input` is true.
- The camera fly path (right-drag + WASD/QE) and the `Q` snap toggle must be gated
  on `not (Input.isShift input)`.
