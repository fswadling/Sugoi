module ScreenManager

// Owns the active screen and its state; the single source of truth for "which
// screen is active" (FR-007). Dispatches update/draw to the active screen and
// applies a pending transition at the end of the frame. Immutable — update/draw
// return a new value (copy-and-update), so no mutable fields are needed.

open Screen

[<Struct>]
type ScreenManager =
    { Active : ScreenKind
      Transition : ScreenTransition
      Shared : SharedState.SharedState
      Gameplay : GameplayState.GameplayState }

let init () : ScreenManager =
    { Active = ScreenKind.MainMenu
      Transition = ScreenTransition.None
      Shared = SharedState.create ()
      Gameplay = GameplayState.create () }

// Advance the active screen; may set a transition.
let update (m: ScreenManager) : ScreenManager =
    match m.Active with
    | ScreenKind.MainMenu ->
        { m with Transition = MainMenuScreen.update m.Shared }
    | ScreenKind.Gameplay ->
        let nextState, transition = GameplayScreen.update m.Gameplay m.Shared
        { m with Gameplay = nextState; Transition = transition }

// Draw the active screen; may set a transition.
let draw (m: ScreenManager) : ScreenManager =
    match m.Active with
    | ScreenKind.MainMenu ->
        { m with Transition = MainMenuScreen.draw m.Shared }
    | ScreenKind.Gameplay ->
        let nextState, transition = GameplayScreen.draw m.Gameplay m.Shared
        { m with Gameplay = nextState; Transition = transition }

// Apply a pending transition at end of frame. Returns the updated manager and
// true when the app should quit.
let applyTransition (m: ScreenManager) : ScreenManager * bool =
    match m.Transition with
    | ScreenTransition.Quit -> { m with Transition = ScreenTransition.None }, true
    | ScreenTransition.Next next -> { m with Active = next; Transition = ScreenTransition.None }, false
    | ScreenTransition.None -> m, false
