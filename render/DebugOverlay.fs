module DebugOverlay

// On-screen debug overlay, drawn every frame on top of the active screen. Grows as more readouts are
// needed (frame time, draw calls, the error tally, …).

// IM_COL32 packed style colour for the error read-out (a soft red). `ImGuiCol_Text` is 0.
let private errorColour = (255u <<< 24) ||| (80u <<< 16) ||| (80u <<< 8) ||| 255u

// As the tally first crosses the threshold, draw attention to the panel once (escalation).
let mutable private escalated = false

// ── cached render strings ──
// The overlay is rebuilt every frame, but its text is not: the fps line changes only when the
// displayed whole number changes, and the tally/rows only when the report list changes (F-19).
let mutable private cachedFps : int = System.Int32.MinValue
let mutable private fpsLine = ""
let mutable private cachedErrors : int = System.Int32.MinValue
let mutable private errorLine = ""
let mutable private cachedReports : Diagnostics.Report list = []
let mutable private reportRows : string array = [||]

let draw (fps: float) =
    let errors = Diagnostics.errorCount()
    if Diagnostics.isLoud() && not escalated then
        do ImGui.setWindowFocus "Debug"
        escalated <- true
    if not (Diagnostics.isLoud()) then escalated <- false

    // ── refresh the cached strings only when their source changed ──
    let shownFps = int (fps + 0.5)
    if shownFps <> cachedFps then
        cachedFps <- shownFps
        fpsLine <- sprintf "%.0f fps" fps

    if errors <> cachedErrors then
        cachedErrors <- errors
        errorLine <- sprintf "%d error(s): %s" errors (Diagnostics.lastMessage())

    let reports = Diagnostics.recent()
    if not (obj.ReferenceEquals(reports, cachedReports)) then
        cachedReports <- reports
        reportRows <- [| for report in reports -> sprintf "  x%d  %s" report.Count report.Message |]

    let visible = ImGui.beginWindow("Debug", System.IntPtr.Zero, 0)
    if visible then
        do ImGui.textUnformatted fpsLine

        // The error tally: shown only when something has been reported, so a clean run is unchanged. The count
        // stays in the body, never the title, so the window keeps a stable ImGui identity.
        if errors > 0 then
            do ImGui.separator()
            do ImGui.pushStyleColorU32(0, errorColour)
            do ImGui.textUnformatted errorLine
            do ImGui.popStyleColor(1)
            // De-duplicated: a repeat reads as one entry with its count, not N lines.
            for row in reportRows do
                do ImGui.textUnformatted row

    do ImGui.endWindow()
