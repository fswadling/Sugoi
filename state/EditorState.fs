module EditorState

// Transient editor UI state. A struct so copy-and-update (the editor rebuilds it
// on every input event) is a stack copy rather than a heap allocation; it is
// still updated functionally. The camera and scene are owned by GameplayState.

open System.Numerics
open Entity
open Command

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
// prose a dialogue line is — so it shares the Dialogue panel's size.
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
      // The Cue panel (spec 047): which node is selected, and the reused buffer for the selected node's text.
      // The selection is the panel's and is never written into a cue; the text buffer belongs to the node
      // `CueTextPath` names — not always the selected one — and is re-seeded only while the field is not live,
      // so an edit in progress is never overwritten and its text can never be applied to a different node
      // (the Inspector's `NameBuffer` rule). The cue's edits are steps on `Undo` above (spec 049), so the panel
      // keeps no history of its own.
      CueSelection : int list option
      CueTextBuffer : byte[]
      CueTextPath : int list option
      CueTextEditing : bool
      // A second text field for the ops that carry two editable strings (a cross-fade's two clips, a move's two
      // clips); the buffer/path/editing above are the first field, and they follow the same re-seed rule.
      CueTextBuffer2 : byte[]
      CueTextPath2 : int list option
      CueTextEditing2 : bool
      // The open cue dropdown's filter text (spec 068): one reused buffer, since only one popup is open at a
      // time; it is cleared when a dropdown appears, so a newly opened combo starts unfiltered.
      ComboFilter : byte[]
      // A structural cue edit (Add Child / Remove) in the last frame asked the Cue panel to re-focus the newly
      // selected node's row on the next one, so ImGui's keyboard-nav position is restored (rather than sent back
      // to the top of the panel, which is what a vanished focused widget and a hidden nav cursor otherwise do).
      CueRefocus : bool
      // The Outliner's add-zone name field (spec 079): a zone is named rather than chosen from a closed vocabulary,
      // so the name is typed into a reused buffer.
      ZoneNameBuffer : byte[]
      // The Advent panel's name field (spec 082): a milestone is named rather than chosen from a closed vocabulary,
      // so it is typed into a reused buffer, as the add-zone field is.
      AdventNameBuffer : byte[]
      // The Scenario panel's name field (spec 083): a scenario is named rather than chosen from a closed vocabulary,
      // so it is typed into a reused buffer, as the advent field is.
      ScenarioNameBuffer : byte[]
      // The Inspector's inline trigger-cue editor (spec 058): which node of the selected trigger's cue is
      // selected, the trigger those fields belong to, and the reused buffer for the selected node's text. Kept
      // apart from the Cue panel's fields above so the two editors cannot share a selection or a buffer; the
      // fields reset when `TriggerCueRef` changes, so a path or text typed for one trigger is never applied to
      // another (the `NameEditRef` rule). The commit is a `TriggerSet` step on `Undo` above.
      TriggerCueSelection : int list option
      TriggerCueRef : EntityRef option
      TriggerCueTextBuffer : byte[]
      TriggerCueTextPath : int list option
      TriggerCueEditing : bool
      TriggerCueTextBuffer2 : byte[]
      TriggerCueTextPath2 : int list option
      TriggerCueTextEditing2 : bool }

let create () : EditorState =
    { Selection = None
      PendingFocus = None
      Tool = Move
      Snap = false
      Drag = None
      DockingInitialized = false
      Undo = UndoStack.create ()
      NudgeBefore = None
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
      CueSelection = None
      CueTextBuffer = Array.zeroCreate cueTextBufferSize
      CueTextPath = None
      CueTextEditing = false
      CueTextBuffer2 = Array.zeroCreate cueTextBufferSize
      CueTextPath2 = None
      CueTextEditing2 = false
      ComboFilter = Array.zeroCreate cueTextBufferSize
      CueRefocus = false
      ZoneNameBuffer = Array.zeroCreate entityNameBufferSize
      AdventNameBuffer = Array.zeroCreate entityNameBufferSize
      ScenarioNameBuffer = Array.zeroCreate entityNameBufferSize
      TriggerCueSelection = None
      TriggerCueRef = None
      TriggerCueTextBuffer = Array.zeroCreate cueTextBufferSize
      TriggerCueTextPath = None
      TriggerCueEditing = false
      TriggerCueTextBuffer2 = Array.zeroCreate cueTextBufferSize
      TriggerCueTextPath2 = None
      TriggerCueTextEditing2 = false }
