module MainMenuScreen

// Main menu: the launch screen. Offers "Play" (→ gameplay) and "Quit".

open Screen

// Advance non-render logic. The menu holds no per-frame state this milestone,
// so nothing to do; navigation comes from button clicks in `draw`.
let update (_shared: SharedState.SharedState) : ScreenTransition =
    ScreenTransition.None

// Draw the menu and return the navigation requested by a clicked button.
let draw (_shared: SharedState.SharedState) : ScreenTransition =
    ImGui.beginWindow("Main Menu", System.IntPtr.Zero, 0) |> ignore
    ImGui.text("fsharp-game")

    let mutable transition = ScreenTransition.None

    if ImGui.button("Play", ImGui.ImVec2(160.0f, 0.0f)) then
        transition <- ScreenTransition.Next ScreenKind.Gameplay

    if ImGui.button("Quit", ImGui.ImVec2(160.0f, 0.0f)) then
        transition <- ScreenTransition.Quit

    ImGui.endWindow()
    transition
