module Editor

// Editor orchestration: the docking workspace (menu bar + panels). The editor
// reads the gameplay state's camera/editor data without owning it.

open System

// Draw the workspace; returns true when the user asked to quit (File > Quit).
let draw (state: GameplayState.GameplayState) (shared: SharedState.SharedState) : bool =
    let mutable quit = false

    do ImGui.dockSpaceOverViewport(0u, IntPtr.Zero, 0, IntPtr.Zero) |> ignore

    if ImGui.beginMainMenuBar() then
        if ImGui.beginMenu("File", true) then
            if ImGui.menuItem("Quit", null, false, true) then
                quit <- true
            do ImGui.endMenu()
        do ImGui.endMainMenuBar()

    do Viewport.draw state
    do Outliner.draw state
    do Inspector.draw state
    do AssetBrowser.draw shared

    quit
