module Editor

// Editor orchestration: the docking workspace (menu bar + panels). The editor
// reads the gameplay state's camera/editor data without owning it.

open System
open Screen

// Draw the workspace. Returns the updated state (the dock layout is built once
// and remembered in the state) and any navigation request (File > Quit).
let draw (state: GameplayState.GameplayState) (shared: SharedState.SharedState) : struct (GameplayState.GameplayState * ScreenTransition) =
    // Dockspace + (once) the default dock layout. The viewport is the central
    // passthru node; Outliner/Inspector/Asset Browser are docked around it.
    let dockspaceId = ImGui.dockSpaceOverViewport(0u, IntPtr.Zero, ImGui.passthruCentralNode, IntPtr.Zero)

    let state =
        if state.DockingInitialized then state else
        do ImGui.setupDockLayout(dockspaceId)
        { state with DockingInitialized = true }

    let mutable transition = ScreenTransition.None

    if ImGui.beginMainMenuBar() then
        if ImGui.beginMenu("File", true) then
            if ImGui.menuItem("Quit", null, false, true) then
                transition <- ScreenTransition.Quit
            do ImGui.endMenu()
        do ImGui.endMainMenuBar()

    do Outliner.draw state
    do Inspector.draw state
    do AssetBrowser.draw shared

    struct (state, transition)
