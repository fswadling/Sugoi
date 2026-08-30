# Data Model: Selection + Transform Foundations

## Slot map (index access)

The generational slot map gains an index-based (non-generational) surface for the
editor:

- `tryGetByIndex : SlotMap<'T> -> uint32 -> 'T option` — read alive-only; `None`
  for dead/out-of-range.
- `setByIndex : SlotMap<'T> -> uint32 -> 'T -> SlotMap<'T>` — overwrite alive, or
  re-occupy a freed slot (rebuild free list); raises on out-of-range.
- `removeByIndex : SlotMap<'T> -> uint32 -> SlotMap<'T>` — free + bump generation;
  raises on dead/out-of-range.

## Entity (`core/Entity.fs`)

```fsharp
type Transform = { Position : Vector3; Rotation : Quaternion; Scale : Vector3 }
let transformIdentity : Transform

type EntityKind = Actor | Environment | Prop | Waypoint | Trigger

// absence is `EntityRef option`; no sentinel value
type EntityRef = { Kind : EntityKind; Index : uint32 }

type Entity =
    { Name : string; Transform : Transform; Enabled : bool
      Visible : bool; Selectable : bool; Draggable : bool }

type Actor = { Base : Entity }
type Prop  = { Base : Entity }
type Environment = { Base : Entity; Props : SlotMap<Prop> }
type Waypoint = { Position : Vector3 }
type Trigger  = { Position : Vector3 }
```

## Zone (`core/Zone.fs`)

```fsharp
type Zone =
    { Name : string
      Actors : SlotMap<Actor>
      Environment : Environment }

let create () : Zone   // actors cap 64, props cap 512, default environment entity
```

`GameplayState` gains a `Zone : Zone.Zone` field (single active zone; a zone
registry is a later phase).

## Editor state (`editor/EditorState.fs`)

```fsharp
type Tool = Move | Rotate | Scale
type GizmoAxis = XAxis | YAxis | ZAxis

type Drag =
    { Axis : GizmoAxis
      Start : Transform
      Grab : Vector3 }

type EditorState =
    { Selection : EntityRef option
      PendingFocus : string option
      Tool : Tool
      Snap : bool
      Drag : Drag option }
```

`Drag.Start` records the entity's transform before a drag begins (the future undo
"before" value); `Drag.Grab` is the mouse hit on the drag plane.

## Debug draw (`render/DebugDraw.fs`)

```fsharp
let wireframeBox : DebugDraw -> Vector3 -> Vector3 -> Color -> DebugDraw  // center, half, color
let sphere       : DebugDraw -> Vector3 -> float32 -> Color -> DebugDraw  // center, radius, color
```
