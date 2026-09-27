module DialoguePanel

// The Dialogue panel: the authoring surface for the dialogue state (FR-009). It writes
// `state.Dialogue` directly — no command, nothing persisted — so it is a direct-write surface
// beside the cast binding, the zone registry and the actor animation/morph sections
// (`docs/EDITOR_DESIGN.md`, D-011).
//
// It is a panel of its own rather than a section of the Inspector, because the Inspector's whole
// body is inside `match state.Editor.Selection` and the dialogue state is not a selection
// (research Decision 13).
//
// The text field's byte buffer in `EditorState` is transient UI scaffolding for the widget, not
// dialogue state — which is why writing it does not violate FR-009's "stores nothing beyond the
// state itself".

open System.Text
open Dialogue

// ── the text field's NUL-terminated buffer ──

let private seedText (buf: byte[]) (text: string) : unit =
    let charCount = min text.Length (EditorState.dialogueTextBufferSize - 1)
    let byteCount = Encoding.UTF8.GetBytes(text, 0, charCount, buf, 0)
    buf.[byteCount] <- 0uy

let private bufferText (buf: byte[]) : string =
    let zero = System.Array.IndexOf(buf, 0uy)
    let length = if zero < 0 then buf.Length else zero
    if length = 0 then "" else Encoding.UTF8.GetString(buf, 0, length)

// ── the panel ──

let private kindName (kind: Kind) : string =
    match kind with
    | Kind.PortraitDialogue _ -> "Portrait"
    | Kind.Dialogue -> "Dialogue"
    | Kind.Thought -> "Thought"

/// The authoring-side explanation of a draw-time miss, in the author's terms (FR-005). Empty for
/// `NotMissed`.
let private advisory (reason: DialogueRender.MissReason) : string =
    match reason with
    | DialogueRender.Unbound -> "no actor asset bound for this speaker — assign one in the Cast panel"
    | DialogueRender.AssetNotLoaded -> "the actor asset bound to this speaker is not loaded"
    | DialogueRender.NoPortraits -> "the actor asset bound to this speaker declares no portraits"
    | DialogueRender.TooTallForViewport -> "the line is taller than the viewport and will not be shown"
    | DialogueRender.NotMissed -> ""

let private selectable (label: string) (selected: bool) : bool =
    ImGui.selectableBool(label, selected, 0, ImGui.ImVec2(0.0f, 0.0f))

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Dialogue", System.IntPtr.Zero, 0) |> ignore

    // Field by field, on a local copy written back once: the state is a struct, and the authoring
    // surface is what mutates it.
    let mutable dialogue = state.Dialogue

    let mutable shown = dialogue.Shown
    if ImGui.checkbox("Shown", &shown) then
        dialogue.Shown <- shown

    if ImGui.beginCombo("Kind", kindName dialogue.Kind, 0) then
        if selectable "Dialogue" (dialogue.Kind = Kind.Dialogue) then
            dialogue.Kind <- Kind.Dialogue
        if selectable "Thought" (dialogue.Kind = Kind.Thought) then
            dialogue.Kind <- Kind.Thought
        // Re-selecting the current entry must not clobber a chosen speaker and expression, so the
        // pair is written only when the kind actually changes into portrait.
        let alreadyPortrait = match dialogue.Kind with Kind.PortraitDialogue _ -> true | _ -> false
        if selectable "Portrait" alreadyPortrait && not alreadyPortrait then
            dialogue.Kind <- Kind.PortraitDialogue (List.head Roster.characters, Neutral)
        do ImGui.endCombo()

    // The speaker and the expression are meaningful only for a portrait line, so they are shown
    // only for that kind: a plain line or a thought has neither.
    match dialogue.Kind with
    | Kind.PortraitDialogue (character, emotion) ->
        if ImGui.beginCombo("Speaker", Roster.characterName character, 0) then
            for c in Roster.characters do
                if selectable (Roster.characterName c) (c = character) then
                    dialogue.Kind <- Kind.PortraitDialogue (c, emotion)
            do ImGui.endCombo()

        if ImGui.beginCombo("Expression", emotionName emotion, 0) then
            for e in emotions do
                if selectable (emotionName e) (e = emotion) then
                    dialogue.Kind <- Kind.PortraitDialogue (character, e)
            do ImGui.endCombo()
    | _ -> ()

    // Why the configured line would not appear, in the same terms the view left it undrawn: the
    // authoring-side counterpart of the draw-time miss (FR-005). Asking `DialogueRender` keeps one
    // definition of "a miss"; measuring goes through `state.Ui`'s cached text slots, so it costs
    // nothing while the line is unchanged.
    let viewport = ImGui.displaySize()
    match DialogueRender.missReason shared.Assets shared.Cast &state.Ui viewport.x viewport.y dialogue with
    | DialogueRender.NotMissed -> ()
    | reason -> do ImGui.text(advisory reason)

    if not state.Editor.DialogueTextEditing then
        do seedText state.Editor.DialogueTextBuffer dialogue.Text
    do ImGui.inputText("Text", state.Editor.DialogueTextBuffer, unativeint state.Editor.DialogueTextBuffer.Length, ImGui.enterReturnsTrue ||| ImGui.autoSelectAll, System.IntPtr.Zero, System.IntPtr.Zero) |> ignore
    let isEditing = ImGui.isItemActive()
    dialogue.Text <- bufferText state.Editor.DialogueTextBuffer

    state.Dialogue <- dialogue
    state.Editor <- { state.Editor with DialogueTextEditing = isEditing }

    do ImGui.endWindow()
