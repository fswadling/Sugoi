module DialoguePanel

// The Dialogue panel: the authoring surface for the dialogue state (FR-009). It writes
// `state.Dialogue` directly — no command, nothing persisted — so it is a direct-write surface
// beside the zone registry and the actor animation/morph sections
// (`docs/EDITOR_DESIGN.md`, D-011).
//
// It is a panel of its own rather than a section of the Inspector, because the Inspector's whole
// body is inside `match state.Editor.Selection` and the dialogue state is not a selection
// (research Decision 13).
//
// The text field's byte buffer in `EditorState` is transient UI scaffolding for the widget, not
// dialogue state — which is why writing it does not violate FR-009's "stores nothing beyond the
// state itself".

open Dialogue

/// The authoring-side explanation of a draw-time miss, in the author's terms (FR-005). Empty for
/// `NotMissed`.
let private advisory (reason: DialogueRender.MissReason) : string =
    match reason with
    | DialogueRender.NoAsset -> "no actor asset named for this speaker — name her asset after her and declare its portraits"
    | DialogueRender.NoPortraits -> "the actor asset for this speaker declares no portraits"
    | DialogueRender.TooTallForViewport -> "the line is taller than the viewport and will not be shown"
    | DialogueRender.NotMissed -> ""

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    let visible = ImGui.beginWindow("Dialogue", System.IntPtr.Zero, 0)
    if visible then

        // Field by field, on a local copy written back once: the state is a struct, and the authoring
        // surface is what mutates it.
        let mutable dialogue = state.Dialogue

        let mutable shown = dialogue.Shown
        if ImGui.checkbox("Shown", &shown) then
            dialogue.Shown <- shown
            if shown then dialogue.ShownAt <- state.PlayTime

        // The same ending control the cue tree uses: the player and timeout conditions plus the timeout
        // seconds. A cue can always close or replace the line, so that capability is not a toggle (spec 097).
        let endings, timeout = CueTree.endingControls "dialogue" dialogue.Endings dialogue.Timeout
        dialogue.Endings <- endings
        dialogue.Timeout <- timeout

        // The one dialogue kind/speaker/expression control, shared with the cue editor (spec 098).
        dialogue.Kind <- CueTree.dialogueControl state.Editor.ComboFilter (Assets.actorNames shared.Assets) "dialogue" dialogue.Kind

        let viewport = ImGui.displaySize()
        match DialogueRender.missReason shared.Assets &state.Ui viewport.x viewport.y dialogue with
        | DialogueRender.NotMissed -> ()
        | reason -> do ImGui.textUnformatted (advisory reason)

        if not state.Editor.DialogueTextEditing then
            do ImGui.seedText state.Editor.DialogueTextBuffer (EditorState.dialogueTextBufferSize - 1) dialogue.Text
        do ImGui.inputText("Text", state.Editor.DialogueTextBuffer, unativeint state.Editor.DialogueTextBuffer.Length, ImGui.enterReturnsTrue ||| ImGui.autoSelectAll, System.IntPtr.Zero, System.IntPtr.Zero) |> ignore
        let isEditing = ImGui.isItemActive()
        dialogue.Text <- ImGui.bufferText state.Editor.DialogueTextBuffer

        state.Dialogue <- dialogue
        state.Editor <- { state.Editor with DialogueTextEditing = isEditing }

    do ImGui.endWindow()
