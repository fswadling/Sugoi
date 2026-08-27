module GameplayScreen

// Gameplay screen: a UI-only placeholder this milestone (no 3D world yet).

open Screen

// Advance non-render logic. No world to step yet, so state is unchanged and no
// navigation is requested.
let update (state: GameplayState.GameplayState) (_shared: SharedState.SharedState) : GameplayState.GameplayState * ScreenTransition =
    state, ScreenTransition.None

// Draw the gameplay placeholder UI.
let draw (_state: GameplayState.GameplayState) (_shared: SharedState.SharedState) : ScreenTransition =
    ImGui.beginWindow("Gameplay", System.IntPtr.Zero, 0) |> ignore
    ImGui.text("Gameplay (placeholder)")
    ImGui.endWindow()
    ScreenTransition.None
