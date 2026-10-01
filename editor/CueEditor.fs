module CueEditor

// The Cue panel (spec 047): the screen's cue, drawn through the one shared control (`CueTree`), and the
// author's edits written back to `state.Cue` as steps on the one undo history (spec 049).
//
// It takes only the gameplay state. Nothing it needs is in `SharedState`: the control is the cue's, and a
// zone name is typed rather than chosen, because a dropdown would have the control read the zone registry
// (`FR-006`).
//
// An accepted edit is one `SetCue` step on `state.Editor.Undo` (`docs/EDITOR_DESIGN.md`, D-011). The cue is
// immutable, so the step carries two references — the value before the edit and after it — and the panel keeps
// no history of its own. `commit` writes the cue and pushes the step itself rather than routing through
// `History.commit`, which is how the panel keeps taking only the state. The Undo/Redo controls are gone: the
// one undo is the keyboard's and the Edit menu's, and both already reach a cue edit. On the way into play the
// cue's steps are pruned (`History.pruneCueSteps`), because the cue advances while playing, while every zone
// step stays.
//
// The panel reads `state.Cue` each frame rather than keeping a copy, which is what makes "the screen's own
// cue, not one kept in step with it" true by construction (`FR-003`), and why an op that has run needs no
// filtering: it is not in the value any more.

open System.Text
open Cue
open Command

// ── the text field's NUL-terminated buffer ──

let private seedText (buffer: byte[]) (text: string) : unit =
    let charCount = min text.Length (EditorState.cueTextBufferSize - 1)
    let byteCount = Encoding.UTF8.GetBytes(text, 0, charCount, buffer, 0)
    buffer.[byteCount] <- 0uy

let private bufferText (buffer: byte[]) : string =
    let zero = System.Array.IndexOf(buffer, 0uy)
    let length = if zero < 0 then buffer.Length else zero
    if length = 0 then "" else Encoding.UTF8.GetString(buffer, 0, length)

// ── the write path ──

/// Apply an accepted edit: the next cue written in place, and one `SetCue` step pushed onto the one history
/// (spec 049). The control decides whether anything changed, so this always records a step — there is no second
/// opinion here about whether an edit "counted". It takes only the state: routing through `History.commit` would
/// make the panel take `SharedState`, which it does not need.
let commit (state: byref<GameplayState.GameplayState>) (next: Cue) : unit =
    let before = state.Cue
    state.Cue <- next
    state.Editor <- { state.Editor with Undo = UndoStack.push state.Editor.Undo (SetCue (before, next)) }

// ── the selected node's text ──

/// The selected node's text field. The buffer is the panel's (a control that named `EditorState` could not be
/// reused where the cue is stored elsewhere), and it follows the Inspector's `NameBuffer` rule: re-seeded —
/// and re-pointed at the node it belongs to — only while the field is *not* live, and committed once, so an
/// edit in progress is never overwritten and its text is never applied to a different node.
let private drawText (state: byref<GameplayState.GameplayState>) (idPrefix: string) (path: int list) (node: Cue) (current: string) : unit =
    if not state.Editor.CueTextEditing then
        do seedText state.Editor.CueTextBuffer current
        state.Editor <- { state.Editor with CueTextPath = Some path }

    let submitted =
        ImGui.inputText("Text##" + CueTree.rowId idPrefix path, state.Editor.CueTextBuffer, unativeint state.Editor.CueTextBuffer.Length, ImGui.enterReturnsTrue ||| ImGui.autoSelectAll, System.IntPtr.Zero, System.IntPtr.Zero)
    let finished = submitted || ImGui.isItemDeactivatedAfterEdit()
    let text = bufferText state.Editor.CueTextBuffer

    // Committed only for the node the buffer belongs to. The selection can change while the field is live
    // (the author clicks another row), and a deactivation in that same frame must not write this text there.
    if finished && state.Editor.CueTextPath = Some path && text <> current then
        do commit &state (CueTree.withAt path (CueTree.withText node text) state.Cue)

    state.Editor <- { state.Editor with CueTextEditing = ImGui.isItemActive() }

// ── the panel ──

let draw (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Cue", System.IntPtr.Zero, 0) |> ignore

    // The cue is the screen's own value, read where it lives: what it shows shrinks as the cue runs, so an
    // op that has already run needs no row of its own (FR-002, FR-003).
    let cue = state.Cue
    let idPrefix = "cue"

    // ── the tree, then the selected node's editor ──
    // An edit accepted here shows in the tree next frame; the tree above the click does not move, so the lag
    // is invisible.
    let mutable selection = state.Editor.CueSelection
    match CueTree.drawTree idPrefix selection cue with
    | Some clicked -> selection <- Some clicked
    | None -> ()

    match selection with
    | Some path ->

        // The control owns the rebuild and hands back the next cue rather than writing it: a byref cannot
        // be captured by the callback it draws through, so the write is staged here and applied in one
        // place, as one step.
        let mutable pending : Cue option = None
        selection <- CueTree.drawEditor idPrefix cue path (fun next -> pending <- Some next)
        match pending with
        | Some next -> do commit &state next
        | None -> ()

        // the node's text, when it carries any. A node that carries text is never executing — only the two
        // in-flight leaves are — so this needs no lock check of its own (CueTree.textOf says so).
        match selection with
        | Some path' ->
            match CueTree.tryAt path' state.Cue with
            | Some node ->
                match CueTree.textOf node with
                | ValueSome current -> do drawText &state idPrefix path' node current
                | ValueNone -> ()
            | None -> ()
        | None -> ()

    | None -> ()

    state.Editor <- { state.Editor with CueSelection = selection }
    do ImGui.endWindow()
