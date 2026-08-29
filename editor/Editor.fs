module Editor

// Editor orchestration: the docking workspace (menu bar + panels). The editor
// reads the gameplay state's camera/editor data without owning it.

open System

// One-time flag: build the dock layout on the first frame (the dockspace id
// only exists once the first DockSpaceOverViewport call has been made).
let private dockingInitialized = ref false

// Draw the workspace; returns true when the user asked to quit (File > Quit).
let draw (state: GameplayState.GameplayState) (shared: SharedState.SharedState) : bool =
    let mutable quit = false

    // Dockspace + (once) the default dock layout. The viewport is the central
    // passthru node; Outliner/Inspector/Asset Browser are docked around it.
    let dockspaceId = ImGui.dockSpaceOverViewport(0u, IntPtr.Zero, ImGui.passthruCentralNode, IntPtr.Zero)
    if not dockingInitialized.Value then
        do ImGui.setupDockLayout(dockspaceId)
        dockingInitialized.Value <- true

    if ImGui.beginMainMenuBar() then
        if ImGui.beginMenu("File", true) then
            if ImGui.menuItem("Quit", null, false, true) then
                quit <- true
            do ImGui.endMenu()
        do ImGui.endMainMenuBar()

    do Outliner.draw state
    do Inspector.draw state
    do AssetBrowser.draw shared

    quit
