# Data Model: Selection + Transform

## Raycast (`editor/Raycast.fs`)

```fsharp
type Ray = { Origin : Vector3; Dir : Vector3 }

rayFromScreen       : Camera -> float32 -> float32 -> float32 -> float32 -> Ray
                      // camera, w, h, x, y
intersectAabb       : Ray -> Vector3 -> Vector3 -> float32 option   // center, half
planeIntersect      : Ray -> Vector3 -> Vector3 -> Vector3 option   // point, normal
raySegmentDistance  : Ray -> Vector3 -> Vector3 -> float32          // a, b
```

- `rayFromScreen`: `ndcX = 2x/w − 1`, `ndcY = 1 − 2y/h`,
  `dir = normalize(forward + ndcX·tan(Fov/2)·aspect·right + ndcY·tan(Fov/2)·up)`,
  `aspect = camera.Aspect` (kept in sync with `w/h` each frame).
- `intersectAabb`: slab test; returns the nearest entry `t ≥ 0`, or `None`.

## Input (`core/Input.fs`) + Frame (`screen/Screen.fs`)

Adds three fields to `Input` and, to `Frame`, the window size plus the gathered
`Input` itself:

```fsharp
type Input =
    { ... existing ...
      MouseX : float32
      MouseY : float32
      LeftDown : bool }

type Frame =
    { ... existing ...
      WindowW : float32
      WindowH : float32
      Input : Input }   // gathered once by the engine
```

Input is gathered once per frame by the engine (`Input.gather`), not by any
screen; the previous-frame state (`PrevInput`) lives beside `Input` in
`core/Input.fs` and is owned by the engine.

## Gameplay state (`screen/GameplayState.fs`)

No new fields: selection/transform state lives on `EditorState` (selection,
`Tool`/`Snap`/`Drag`), and input edge-detection state (`PrevLeftDown`, …) now
lives engine-side in `PrevInput` — not on `GameplayState`. The viewport reads
window size from `Frame` (see above), threaded to `ray`/`pick` as `(w, h)`.

## Zone helpers (`core/Zone.fs`, temporary)

```fsharp
// dispatch on EntityRef.Kind; migrate to editor/Command.fs in phase 2d
entityAt     : Zone -> EntityRef -> Entity option
setEntity    : Zone -> EntityRef -> Entity -> Zone
removeEntity : Zone -> EntityRef -> Zone
spawnActor   : Zone -> Entity -> Zone * EntityRef
spawnProp    : Zone -> Entity -> Zone * EntityRef
```

`entityAt`/`setEntity`/`removeEntity` resolve `Actor → zone.Actors` and
`Prop → zone.Environment.Props`; other kinds return `None`/no-op.

## Viewport (`editor/Viewport.fs`)

```fsharp
buildLines    : GameplayState -> DebugDraw -> DebugDraw   // markers + gizmo
drawGizmo     : GameplayState -> DebugDraw -> Vector3 -> DebugDraw
ray           : Camera -> float32 -> float32 -> float32 -> float32 -> Ray
                      // camera, w, h, x, y
pick          : GameplayState -> float32 -> float32 -> float32 -> float32 -> EntityRef option
                      // state, w, h, x, y
gizmoAxisAt   : GameplayState -> Ray -> Vector3 -> GizmoAxis option
axisDir       : GizmoAxis -> Vector3
basis         : GizmoAxis -> Vector3 * Vector3
```

## Editor interaction (`editor/Editor.fs`)

```fsharp
handleShortcuts : GameplayState -> Input -> struct (GameplayState * ScreenTransition)
interact        : GameplayState -> Input -> float32 -> float32 -> GameplayState   // + viewport w/h
applyDrag       : GameplayState -> float32 -> float32 -> float32 -> float32 -> GameplayState
finishDrag      : GameplayState -> GameplayState
```

`interact` on left-press (not ImGui-captured): try `gizmoAxisAt` on the selection
→ begin a `Drag`; else `pick` → set selection. `applyDrag` derives the transform
each frame; `finishDrag` clears the drag (and, in 2d, pushes an undo using
`Drag.Start`).
