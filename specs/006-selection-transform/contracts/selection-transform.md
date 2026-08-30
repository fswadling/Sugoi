# Contract: Selection + Transform

Public surfaces added by this feature. Signatures are illustrative F#, matching
the data model.

## Raycast (`editor/Raycast.fs`)

```fsharp
type Ray = { Origin : Vector3; Dir : Vector3 }
rayFromScreen      : Camera -> float32 -> float32 -> float32 -> float32 -> Ray
intersectAabb      : Ray -> Vector3 -> Vector3 -> float32 option
planeIntersect     : Ray -> Vector3 -> Vector3 -> Vector3 option
raySegmentDistance : Ray -> Vector3 -> Vector3 -> float32
```

## Zone entity access (`core/Zone.fs`, temporary)

```fsharp
entityAt     : Zone -> EntityRef -> Entity option
setEntity    : Zone -> EntityRef -> Entity -> Zone
removeEntity : Zone -> EntityRef -> Zone
spawnActor   : Zone -> Entity -> Zone * EntityRef
spawnProp    : Zone -> Entity -> Zone * EntityRef
```

## Viewport picking (`editor/Viewport.fs`)

```fsharp
pick        : GameplayState -> float32 -> float32 -> EntityRef option
gizmoAxisAt : GameplayState -> Ray -> Vector3 -> GizmoAxis option
```

## Editor interaction (`editor/Editor.fs`)

```fsharp
interact   : GameplayState -> Input -> GameplayState
applyDrag  : GameplayState -> float32 -> float32 -> GameplayState
finishDrag : GameplayState -> GameplayState
```

## ImGui (`render/ImGui.fs`)

```fsharp
// Main viewport size (io.DisplaySize); context-safe.
displaySize : unit -> ImVec2

// io.WantCaptureMouse; context-safe (false with no ImGui context).
wantCaptureMouse : unit -> bool
```

## Rules

- `pick`/`interact` must not run while `ImGui.wantCaptureMouse()` is true.
- `rayFromScreen` uses `camera.Aspect` (kept in sync with `w/h` each frame).
- `entityAt`/`setEntity`/`removeEntity` only handle `Actor` and `Prop`; other
  kinds are no-ops (`None`/unchanged).
