module EditorState

// Transient editor UI state. Not per-frame data, so it is an immutable record;
// the editor updates it functionally. The camera and scene are owned by
// GameplayState.

open System.Numerics
open Entity

// The active transform tool, selected with 1/2/3.
type Tool =
    | Move
    | Rotate
    | Scale

// Which gizmo axis a drag is operating on.
type GizmoAxis =
    | XAxis
    | YAxis
    | ZAxis

// State recorded when a gizmo drag begins: the grabbed axis, the pre-drag
// transform (the undo "before"), and the mouse hit on the drag plane.
type Drag =
    { Axis : GizmoAxis
      Start : Transform
      Grab : Vector3 }

type EditorState =
    { Selection : EntityRef option
      PendingFocus : string option
      Tool : Tool
      Snap : bool
      Drag : Drag option
      DockingInitialized : bool }

let create () : EditorState =
    { Selection = None
      PendingFocus = None
      Tool = Move
      Snap = false
      Drag = None
      DockingInitialized = false }
