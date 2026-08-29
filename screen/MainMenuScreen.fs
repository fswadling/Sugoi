module MainMenuScreen

// Main menu: the launch screen. Offers "Play" (→ gameplay) and "Quit".

open Screen

// Advance non-render logic. The menu holds no per-frame state this milestone,
// so nothing to do; navigation comes from button clicks in `draw`.
let update (_shared: SharedState.SharedState) : ScreenTransition =
    ScreenTransition.None

// Draw the menu and return the navigation requested by a clicked button.
let draw (_shared: SharedState.SharedState) : ScreenTransition =
    do ImGui.beginWindow("Main Menu", System.IntPtr.Zero, 0) |> ignore
    do ImGui.text("fsharp-game")
    let playDown = ImGui.button("Play", ImGui.ImVec2(160.0f, 0.0f))
    let quitDown = ImGui.button("Quit", ImGui.ImVec2(160.0f, 0.0f))
    do ImGui.endWindow()

    let transition =
        if playDown then ScreenTransition.Next ScreenKind.Gameplay
        else if quitDown then ScreenTransition.Quit
        else ScreenTransition.None

    transition
