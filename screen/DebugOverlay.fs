module DebugOverlay

// On-screen debug overlay, drawn every frame on top of the active screen.
// Grows as more readouts are needed (frame time, draw calls, …).

let draw (fps: float) =
    do ImGui.beginWindow("Debug", System.IntPtr.Zero, 0) |> ignore
    do ImGui.text(sprintf "%.0f fps" fps)
    do ImGui.endWindow()
