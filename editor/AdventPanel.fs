module AdventPanel

// The Advent panel (spec 055; a string set since spec 082): the story milestones — the named adventure
// flags — as a small list editor the author changes by hand. It is docked beside the Inspector on the
// right, with the other editing surfaces, because what it edits is a set of story facts rather than a place
// in the scene.
//
// A milestone is a plain string (spec 082), so the panel offers a **text field** and an
// **Add** control to name a new one, and a **list** of the current advents, each with a **Remove** control —
// there are no fixed cases to check off. A name is trimmed and refused when blank or already present; a
// name is matched exactly (ordinal). Each add and remove is one step on the one undo history
// (`docs/EDITOR_DESIGN.md`, D-011) — the set is not a zone's, so `Command.Advent` carries the set before and
// after the edit and `state/Undo.fs` writes `SharedState.Advents`, the same shape the cue's `SetCue` step
// has (`specs/048`, `specs/049`). The panel keeps no history of its own; `Ctrl+Z`, `Ctrl+Y` and the Edit menu
// reach the step.

open Prime

// ── the write path ──

/// Record a new set as one step on the one history (FR-008). The caller computes `next` with `OSet`'s
/// operations; this applies `next` and pushes the before/after step in one place, so the panel keeps no
/// history of its own.
let commit (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) (next: OSet<string>) : unit =
    let before = shared.Advents
    do Undo.commit shared &state (Command.Advent (before, next))

// ── the text field's NUL-terminated buffer ──

let private bufferText (buffer: byte[]) : string =
    let zero = System.Array.IndexOf(buffer, 0uy)
    let length = if zero < 0 then buffer.Length else zero
    if length = 0 then "" else System.Text.Encoding.UTF8.GetString(buffer, 0, length)

// ── the panel ──

let draw (shared: SharedState.SharedState) (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Advent", System.IntPtr.Zero, 0) |> ignore

    // The field names a new advent. Add is offered only for a name that survives trimming and is not already
    // present, and clears the field on success so the next name can be typed straight away (the Outliner's
    // add-zone rule).
    let buffer = state.Editor.AdventNameBuffer
    do ImGui.inputText("Name##addadvent", buffer, unativeint buffer.Length, 0, System.IntPtr.Zero, System.IntPtr.Zero) |> ignore
    let typed = (bufferText buffer).Trim()
    do ImGui.sameLine(0.0f, -1.0f)
    let canAdd = typed <> "" && not (OSet.contains typed shared.Advents)
    do ImGui.beginDisabled(not canAdd)
    if ImGui.smallButton("Add##addadvent_btn") && canAdd then
        do commit shared &state (OSet.add typed shared.Advents)
        do System.Array.Clear(buffer, 0, buffer.Length)
    do ImGui.endDisabled()

    // The current advents, in the order they were added; each row clears its own name. A name is unique in
    // the set, so it is a stable widget id.
    let names = OSet.toSeq shared.Advents |> List.ofSeq
    if names.IsEmpty then
        do ImGui.text "(none)"
    else
        for name in names do
            do ImGui.text name
            do ImGui.sameLine(0.0f, -1.0f)
            if ImGui.smallButton(sprintf "Remove##adv_%s" name) then
                do commit shared &state (OSet.remove name shared.Advents)

    do ImGui.endWindow()
