module DebugOverlay

// On-screen debug overlay, drawn every frame on top of the active screen. Grows as more readouts are
// needed (frame time, draw calls, the error tally, …).

// IM_COL32 packed style colour for the error read-out (a soft red). `ImGuiCol_Text` is 0.
let private errorColour = (255u <<< 24) ||| (80u <<< 16) ||| (80u <<< 8) ||| 255u

// As the tally first crosses the threshold, draw attention to the panel once (escalation).
let mutable private escalated = false

let draw (fps: float) =
    let errors = Diagnostics.errorCount()
    if Diagnostics.isLoud() && not escalated then
        do ImGui.setWindowFocus "Debug"
        escalated <- true
    if not (Diagnostics.isLoud()) then escalated <- false

    do ImGui.beginWindow("Debug", System.IntPtr.Zero, 0) |> ignore
    do ImGui.text(sprintf "%.0f fps" fps)

    // The error tally: shown only when something has been reported, so a clean run is unchanged. The count
    // stays in the body, never the title, so the window keeps a stable ImGui identity.
    if errors > 0 then
        do ImGui.separator()
        do ImGui.pushStyleColorU32(0, errorColour)
        do ImGui.text(sprintf "%d error(s): %s" errors (Diagnostics.lastMessage()))
        do ImGui.popStyleColor(1)
        // De-duplicated: a repeat reads as one entry with its count, not N lines.
        for r in Diagnostics.recent() do
            do ImGui.text(sprintf "  x%d  %s" r.Count r.Message)

    do ImGui.endWindow()
