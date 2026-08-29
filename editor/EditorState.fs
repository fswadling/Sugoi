module EditorState

// Transient editor UI state. Not per-frame data, so it is an immutable record;
// the editor updates it functionally. The camera and scene are owned by
// GameplayState.

open Entity

type EditorState =
    { Selection : EntityRef option }

let create () : EditorState =
    { Selection = None }
