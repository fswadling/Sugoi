module CueEditor

// The Cue panel (spec 047): the screen's cue, drawn through the one shared control (`CueTree`), and the
// author's edits written back to `state.Cue` with the cue's own undo history.
//
// It takes only the gameplay state. Nothing it needs is in `SharedState`: the control is the cue's, and a
// zone name is typed rather than chosen, because a dropdown would have the control read the zone registry
// (`FR-006`).
//
// Editing is a *direct write* (`docs/EDITOR_DESIGN.md`, D-011) — a cue spans zones while the undo stacks are
// per zone, so it cannot ride a zone's stack, and it is immutable, so its history is the list of values it
// had before each accepted edit (D-010) rather than a family of commands. The history is dropped when play
// begins (`FR-013`), in `Editor`'s edit/play toggle.
//
// The panel reads `state.Cue` each frame rather than keeping a copy, which is what makes "the screen's own
// cue, not one kept in step with it" true by construction (`FR-003`), and why an op that has run needs no
// filtering: it is not in the value any more.

open System.Text
open Cue

// ── the text field's NUL-terminated buffer ──

let private seedText (buffer: byte[]) (text: string) : unit =
    let charCount = min text.Length (EditorState.cueTextBufferSize - 1)
    let byteCount = Encoding.UTF8.GetBytes(text, 0, charCount, buffer, 0)
    buffer.[byteCount] <- 0uy

let private bufferText (buffer: byte[]) : string =
    let zero = System.Array.IndexOf(buffer, 0uy)
    let length = if zero < 0 then buffer.Length else zero
    if length = 0 then "" else Encoding.UTF8.GetString(buffer, 0, length)

// ── the cue's own history ──

/// Apply an accepted edit: the next cue written in place, and the value it had before it pushed onto the
/// cue's own history. The control decides whether anything changed (research D4), so this always records a
/// step — there is no second opinion here about whether an edit "counted".
let commit (state: byref<GameplayState.GameplayState>) (next: Cue) : unit =
    let before = state.Cue
    state.Cue <- next
    state.Editor <- { state.Editor with CueHistory = EditorState.CueHistory.push before state.Editor.CueHistory }

let canUndo (state: GameplayState.GameplayState) : bool =
    not (List.isEmpty state.Editor.CueHistory.Applied)

let canRedo (state: GameplayState.GameplayState) : bool =
    not (List.isEmpty state.Editor.CueHistory.Redo)

/// Take the last accepted edit back.
let undo (state: byref<GameplayState.GameplayState>) : unit =
    let cue, history = EditorState.CueHistory.undo state.Cue state.Editor.CueHistory
    state.Cue <- cue
    state.Editor <- { state.Editor with CueHistory = history }

/// Put the last taken-back edit back.
let redo (state: byref<GameplayState.GameplayState>) : unit =
    let cue, history = EditorState.CueHistory.redo state.Cue state.Editor.CueHistory
    state.Cue <- cue
    state.Editor <- { state.Editor with CueHistory = history }

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

    // ── the cue's own history, before the tree ──
    // Its own controls rather than Ctrl+Z: that key is the zone stack's (`Editor.handleShortcuts`), and one
    // key meaning two things depending on which panel was last clicked is worse than a second control.
    do ImGui.beginDisabled (not (canUndo state))
    if ImGui.smallButton "Undo" then do undo &state
    do ImGui.endDisabled()
    do ImGui.sameLine(0.0f, 8.0f)
    do ImGui.beginDisabled (not (canRedo state))
    if ImGui.smallButton "Redo" then do redo &state
    do ImGui.endDisabled()

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
