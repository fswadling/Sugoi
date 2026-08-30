module Editor

// Editor orchestration: the docking workspace (menu bar + panels) and keyboard
// shortcuts. The editor reads the gameplay state's camera/editor data without
// owning it; transient editor state lives on `EditorState`, not in module-level
// singletons.

open System

// Docked panels, in the order Ctrl+Tab cycles through them.
let private panelNames = [| "Outliner"; "Inspector"; "Asset Browser" |]

// Compute the index the focus should move to (forward/backward, wrapping).
let private nextPanelIndex (forward: bool) : int =
    let n = panelNames.Length
    let current = Array.tryFindIndex (fun (p: string) -> ImGui.isWindowActive p) panelNames
    match current with
    | Option.None -> if forward then 0 else n - 1
    | Option.Some i -> if forward then (i + 1) % n else (i - 1 + n) % n

// Apply a queued panel-focus request (valid only after ImGui.NewFrame), then
// clear it. Returns the updated state.
let private applyPendingFocus (state: GameplayState.GameplayState) : GameplayState.GameplayState =
    match state.Editor.PendingFocus with
    | None -> state
    | Some name ->
    do ImGui.setWindowFocus(name)
    { state with Editor.PendingFocus = None }


// Apply app-level shortcuts for this frame. No-ops while a text field has
// focus so typing doesn't trigger shortcuts.
let handleShortcuts (state: GameplayState.GameplayState) (input: Screen.Input) : struct (GameplayState.GameplayState * Screen.ScreenTransition) =
    if ImGui.wantTextInput() then
        struct (state, Screen.ScreenTransition.None)
    else

    let transition =
        if Screen.Input.isPressed Screen.Key.Escape input
        then Screen.ScreenTransition.Next Screen.ScreenKind.MainMenu
        else Screen.ScreenTransition.None

    let state =
        if Screen.Input.isCtrl input && Screen.Input.isPressed Screen.Key.Tab input then
            let next = nextPanelIndex (not (Screen.Input.isShift input))
            { state with Editor.PendingFocus = Some panelNames.[next] }
        else state

    struct (state, transition)

// Draw the workspace. Returns the updated state (the dock layout is built once
// and remembered in the state) and any navigation request (File > Quit).
let draw (state: GameplayState.GameplayState) (shared: SharedState.SharedState) : struct (GameplayState.GameplayState * Screen.ScreenTransition) =
    // Apply any panel-focus request queued by update (valid only after NewFrame).
    let state = applyPendingFocus state

    // Dockspace + (once) the default dock layout. The viewport is the central
    // passthru node; Outliner/Inspector/Asset Browser are docked around it.
    let dockspaceId = ImGui.dockSpaceOverViewport(0u, IntPtr.Zero, ImGui.passthruCentralNode, IntPtr.Zero)

    let state =
        if state.DockingInitialized then state else
        do ImGui.setupDockLayout(dockspaceId)
        { state with DockingInitialized = true }

    let mutable transition = Screen.ScreenTransition.None

    if ImGui.beginMainMenuBar() then
        if ImGui.beginMenu("File", true) then
            if ImGui.menuItem("Quit", null, false, true) then
                transition <- Screen.ScreenTransition.Quit
            do ImGui.endMenu()
        do ImGui.endMainMenuBar()

    do Outliner.draw state
    do Inspector.draw state
    do AssetBrowser.draw shared

    struct (state, transition)
