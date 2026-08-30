# Contract: Entity Foundations

Public surfaces added by this feature. Signatures are illustrative F#, matching
the implementation.

## Slot map index access (`core/SlotMap.fs`)

```fsharp
// Read alive-only; None for dead/out-of-range.
tryGetByIndex : SlotMap<'T> -> uint32 -> 'T option

// Overwrite alive, or re-occupy a freed slot. Raises on out-of-range.
setByIndex : SlotMap<'T> -> uint32 -> 'T -> SlotMap<'T>

// Free + bump generation. Raises on dead/out-of-range.
removeByIndex : SlotMap<'T> -> uint32 -> SlotMap<'T>
```

## Zone (`core/Zone.fs`)

```fsharp
type Zone = { Name : string; Actors : SlotMap<Actor>; Environment : Environment }
create : unit -> Zone
```

## Editor state (`editor/EditorState.fs`)

```fsharp
type Tool = Move | Rotate | Scale
type GizmoAxis = XAxis | YAxis | ZAxis
type Drag = { Axis : GizmoAxis; Start : Transform; Grab : Vector3 }

type EditorState =
    { Selection : EntityRef option
      PendingFocus : string option
      Tool : Tool
      Snap : bool
      Drag : Drag option }
```

## Debug draw (`render/DebugDraw.fs`)

```fsharp
wireframeBox : DebugDraw -> Vector3 -> Vector3 -> Color -> DebugDraw
sphere       : DebugDraw -> Vector3 -> float32 -> Color -> DebugDraw
```

## Rules

- Index access is by `uint32` index only; generation checks live in the
  generational `get`/`free`.
- Reads of absent entities return `None`; writes/removes to invalid indices raise.
- The debug-draw shapes append to the preallocated accumulator (no allocation).
