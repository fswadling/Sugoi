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

## Input (`screen/Screen.fs`)

Adds three fields to `Input`:

```fsharp
{ ... existing ...
  MouseX : float32
  MouseY : float32
  LeftDown : bool }
```

## Gameplay state (`screen/GameplayState.fs`)

```fsharp
{ ... existing ...
  PrevLeftDown : bool
  WindowW : float32
  WindowH : float32 }
```

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
ray           : GameplayState -> float32 -> float32 -> Ray
pick          : GameplayState -> float32 -> float32 -> EntityRef option
gizmoAxisAt   : GameplayState -> Ray -> Vector3 -> GizmoAxis option
axisDir       : GizmoAxis -> Vector3
basis         : GizmoAxis -> Vector3 * Vector3
```

## Editor interaction (`editor/Editor.fs`)

```fsharp
handleShortcuts : GameplayState -> Input -> struct (GameplayState * ScreenTransition)
interact        : GameplayState -> Input -> GameplayState   // gizmo drag / selection
applyDrag       : GameplayState -> float32 -> float32 -> GameplayState
finishDrag      : GameplayState -> GameplayState
```

`interact` on left-press (not ImGui-captured): try `gizmoAxisAt` on the selection
→ begin a `Drag`; else `pick` → set selection. `applyDrag` derives the transform
each frame; `finishDrag` clears the drag (and, in 2d, pushes an undo using
`Drag.Start`).
