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

// The channels in the Inspector's light-colour buffer (spec 043): one reused `float[3]` the colour
// picker draws over, so editing the light allocates nothing per frame.
[<Literal>]
let lightColorChannels = 3

// The Inspector's text-field buffers and the longest value each accepts. Sized for
// UTF-8's worst case (3 bytes for a BMP character) so encoding a value of up to
// `entityNameMaxChars` characters always fits and never needs a clamp mid-character. The
// prompt shares the name field's size, which is what the reference projects use for it.
[<Literal>]
let entityNameBufferSize = 256

[<Literal>]
let entityNameMaxChars = 63

// The Dialogue panel's line buffer (spec 038). Dialogue lines are prose, so this is
// generous; UTF-8's worst case is 3 bytes per BMP character.
[<Literal>]
let dialogueTextBufferSize = 1023

// The Cue panel's text field (spec 047): the selected node's text — an `Exposit`'s line, which is the same
// prose a dialogue line is, or a `SwitchZone`'s zone name — so it shares the Dialogue panel's size.
[<Literal>]
let cueTextBufferSize = 1023

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

// The cue's own undo history (spec 047, `docs/EDITOR_DESIGN.md` D-010). Snapshots of the cue value *before*
// each accepted edit — a cue is immutable, so a snapshot is a reference — dropped when play begins, because
// the cue a step refers to is about to advance. The editor's, not a zone's: a cue spans zones.
type CueHistory =
    { Applied : Cue.Cue list
      Redo : Cue.Cue list }

module CueHistory =

    let create () : CueHistory =
        { Applied = []
          Redo = [] }

    /// One accepted edit: the cue as it was before it, and the redo side cleared — `UndoStack.push`'s shape,
    /// for the same reason.
    let push (before: Cue.Cue) (history: CueHistory) : CueHistory =
        { Applied = before :: history.Applied
          Redo = [] }

    /// Take an edit back: the value to restore and the history that results. Nothing applied is a no-op.
    let undo (current: Cue.Cue) (history: CueHistory) : Cue.Cue * CueHistory =
        match history.Applied with
        | [] -> current, history
        | before :: rest -> before, { Applied = rest; Redo = current :: history.Redo }

    /// Put an edit back: `undo`'s shape on the other side.
    let redo (current: Cue.Cue) (history: CueHistory) : Cue.Cue * CueHistory =
        match history.Redo with
        | [] -> current, history
        | after :: rest -> after, { Applied = current :: history.Applied; Redo = rest }

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
      // The Inspector's entity-name field: the text, the ref that text belongs to, and
      // whether the field was live as of the last draw. The buffer is re-seeded — and the ref
      // re-pointed — only while the field is not live, so an edit in progress is never
      // overwritten and its text can never be applied to a different entity.
      NameBuffer : byte[]
      NameEditRef : EntityRef option
      NameEditing : bool
      // The Inspector's trigger-prompt field. The same shape as the name field, in its
      // own buffer because a selected trigger draws both.
      PromptBuffer : byte[]
      PromptEditRef : EntityRef option
      PromptEditing : bool
      // The Dialogue panel's line field (spec 038): transient UI scaffolding for a text
      // field, not dialogue state, which is why FR-009's "stores nothing beyond the state
      // itself" holds — no command is recorded and nothing is persisted.
      DialogueTextBuffer : byte[]
      DialogueTextEditing : bool
      // The Inspector's light-colour picker (spec 043): the reused `float[3]` a colour edit draws
      // over, seeded from the zone's colour while the picker is not live.
      LightColorBuffer : float32 array
      LightColorEditing : bool
      // The Cue panel (spec 047): the cue's own undo history, which node is selected, and the reused buffer
      // for the selected node's text. The selection is the panel's and is never written into a cue; the text
      // buffer belongs to the node `CueTextPath` names — not always the selected one — and is re-seeded only
      // while the field is not live, so an edit in progress is never overwritten and its text can never be
      // applied to a different node (the Inspector's `NameBuffer` rule).
      CueHistory : CueHistory
      CueSelection : int list option
      CueTextBuffer : byte[]
      CueTextPath : int list option
      CueTextEditing : bool }

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
      NameEditRef = None
      NameEditing = false
      PromptBuffer = Array.zeroCreate entityNameBufferSize
      PromptEditRef = None
      PromptEditing = false
      DialogueTextBuffer = Array.zeroCreate dialogueTextBufferSize
      DialogueTextEditing = false
      LightColorBuffer = Array.zeroCreate lightColorChannels
      LightColorEditing = false
      CueHistory = CueHistory.create ()
      CueSelection = None
      CueTextBuffer = Array.zeroCreate cueTextBufferSize
      CueTextPath = None
      CueTextEditing = false }
