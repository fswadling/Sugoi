module DebugOverlay

// On-screen debug overlay, drawn every frame on top of the active screen.
// Grows as more readouts are needed (frame time, draw calls, …).

let draw (fps: float) =
    ImGui.beginWindow("Debug", System.IntPtr.Zero, 0) |> ignore
    ImGui.text(sprintf "%.0f fps" fps)
    ImGui.endWindow()
