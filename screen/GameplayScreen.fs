module GameplayScreen

// Gameplay screen: hosts the editor workspace.

open Screen

// Advance non-render logic. No world yet; camera input arrives with the
// DebugDraw render step.
let update (state: GameplayState.GameplayState) (_shared: SharedState.SharedState) : GameplayState.GameplayState * ScreenTransition =
    state, ScreenTransition.None

// Draw the editor workspace, then honour a quit request from the menu.
let draw (state: GameplayState.GameplayState) (shared: SharedState.SharedState) : GameplayState.GameplayState * ScreenTransition =
    let quit = Editor.draw state shared

    let transition =
        if quit then ScreenTransition.Quit else ScreenTransition.None

    state, transition
