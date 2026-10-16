module MiscPanel

// The Miscellaneous panel (spec 059): the editor's home for game-wide, non-entity runtime tuning — values
// that belong to no zone and to no selection, so they cannot live in the Inspector. It docks in the right
// column beside the Inspector/Cue/Advent, with the other editing surfaces.
//
// Its first control is the screen's fade level: a slider over 0..1, the opacity of the black overlay the
// gameplay screen draws over the rendered game view. The write is in place — no `Command`, no `Undo`
// step — because the fade is live screen runtime state, not authored project data (spec 059, research D1).

let draw (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Miscellaneous", System.IntPtr.Zero, 0) |> ignore

    // The slider writes `state.Fade` by reference; the overlay reads it next frame, so dragging it previews
    // the fade while the panels stay readable above it.
    if ImGui.sliderFloat("Fade", &state.Fade, 0.0f, 1.0f, "%.2f", 0) then ()

    // The project file's live reload (spec 096): when on, an external edit to `project.manifest` is picked up
    // while editing (an agent's or the author's, without a restart). Transient editor state, not authored data,
    // so the write is in place — no `Command`, no `Undo` step (the `Snap` pattern, not the `Fade` byref one).
    let mutable live = state.Editor.LiveReload
    if ImGui.checkbox("Live reload", &live) then
        state.Editor <- { state.Editor with LiveReload = live }

    do ImGui.endWindow()
