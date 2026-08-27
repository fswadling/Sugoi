module ScreenManager

// Owns the active screen and its state; the single source of truth for "which
// screen is active" (FR-007). Dispatches update/draw to the active screen and
// applies a pending transition at the end of the frame.

open Screen

type ScreenManager =
    { mutable active : ScreenKind
      mutable transition : ScreenTransition
      shared : SharedState.SharedState
      mutable gameplay : GameplayState.GameplayState }

let init () : ScreenManager =
    { active = ScreenKind.MainMenu
      transition = ScreenTransition.None
      shared = SharedState.SharedState()
      gameplay = GameplayState.GameplayState() }

// Advance the active screen; may set a transition.
let update (m: ScreenManager) =
    match m.active with
    | ScreenKind.MainMenu ->
        m.transition <- MainMenuScreen.update m.shared
    | ScreenKind.Gameplay ->
        let nextState, transition = GameplayScreen.update m.gameplay m.shared
        m.gameplay <- nextState
        m.transition <- transition

// Draw the active screen; may set a transition.
let draw (m: ScreenManager) =
    match m.active with
    | ScreenKind.MainMenu ->
        m.transition <- MainMenuScreen.draw m.shared
    | ScreenKind.Gameplay ->
        m.transition <- GameplayScreen.draw m.gameplay m.shared

// Apply a pending transition at end of frame. Returns true when the app should
// quit; otherwise the requested screen (if any) becomes active.
let applyTransition (m: ScreenManager) : bool =
    let quit =
        match m.transition with
        | ScreenTransition.Quit -> true
        | ScreenTransition.Next next -> m.active <- next; false
        | ScreenTransition.None -> false

    m.transition <- ScreenTransition.None
    quit
