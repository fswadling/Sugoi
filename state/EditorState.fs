module EditorState

// Transient editor UI state. A struct so copy-and-update (the editor rebuilds it
// on every input event) is a stack copy rather than a heap allocation; it is
// still updated functionally. The camera and scene are owned by GameplayState.

open System.Numerics
open Entity
open Command

// Length of the reusable zone-name text buffer (the pending name in the Outliner
// zones section). 64 bytes is ample for a zone name.
[<Literal>]
let zoneNameBufferSize = 64

// The Inspector's entity-name buffer and the longest name it accepts. Sized for
// UTF-8's worst case (3 bytes for a BMP character) so encoding a name of up to
// `entityNameMaxChars` characters always fits and never needs a clamp mid-character.
[<Literal>]
let entityNameBufferSize = 256

[<Literal>]
let entityNameMaxChars = 63

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

[<Struct>]
type EditorState =
    { Selection : EntityRef option
      PendingFocus : string option
      Tool : Tool
      Snap : bool
      Drag : Drag option
      DockingInitialized : bool
      Undo : UndoStack
      NudgeBefore : Transform option
      // A reused UTF-8 buffer for the pending zone name (Outliner zones section);
      // one buffer so typing allocates nothing per frame.
      NewZoneName : byte[]
      // The Inspector's entity-name field: the text, re-seeded from the selected
      // entity every frame, and the ref that text belongs to, so a pending edit is
      // never applied to a different entity than the one it was typed for.
      NameBuffer : byte[]
      NameEditRef : EntityRef option }

let create () : EditorState =
    { Selection = None
      PendingFocus = None
      Tool = Move
      Snap = false
      Drag = None
      DockingInitialized = false
      Undo = UndoStack.create ()
      NudgeBefore = None
      NewZoneName = Array.zeroCreate zoneNameBufferSize
      NameBuffer = Array.zeroCreate entityNameBufferSize
      NameEditRef = None }
