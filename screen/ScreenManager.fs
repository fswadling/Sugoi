module ScreenManager

// Owns the active screen and its state; the single source of truth for "which
// screen is active" (FR-007). Dispatches update/draw to the active screen and
// applies a pending transition at the end of the frame.

open Screen

type ScreenManager =
    { mutable Active : ScreenKind
      mutable Transition : ScreenTransition
      Shared : SharedState.SharedState
      mutable Gameplay : GameplayState.GameplayState }

let init () : ScreenManager =
    { Active = ScreenKind.MainMenu
      Transition = ScreenTransition.None
      Shared = SharedState.SharedState()
      Gameplay = GameplayState.GameplayState() }

// Advance the active screen; may set a transition.
let update (m: ScreenManager) =
    match m.Active with
    | ScreenKind.MainMenu ->
        m.Transition <- MainMenuScreen.update m.Shared
    | ScreenKind.Gameplay ->
        let nextState, transition = GameplayScreen.update m.Gameplay m.Shared
        m.Gameplay <- nextState
        m.Transition <- transition

// Draw the active screen; may set a transition.
let draw (m: ScreenManager) =
    match m.Active with
    | ScreenKind.MainMenu ->
        m.Transition <- MainMenuScreen.draw m.Shared
    | ScreenKind.Gameplay ->
        m.Transition <- GameplayScreen.draw m.Gameplay m.Shared

// Apply a pending transition at end of frame. Returns true when the app should
// quit; otherwise the requested screen (if any) becomes active.
let applyTransition (m: ScreenManager) : bool =
    let quit =
        match m.Transition with
        | ScreenTransition.Quit -> true
        | ScreenTransition.Next next -> m.Active <- next; false
        | ScreenTransition.None -> false

    m.Transition <- ScreenTransition.None
    quit
