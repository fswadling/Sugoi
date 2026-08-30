module ScreenManager

// Owns the active screen and its state; the single source of truth for "which
// screen is active" (FR-007). Dispatches update/draw to the active screen and
// applies a pending transition at the end of the frame. Immutable — update/draw
// return a new value (copy-and-update), so no mutable fields are needed.

open System
open Screen
open SDL3

[<Struct>]
type ScreenManager =
    { Active : ScreenKind
      Transition : ScreenTransition
      Shared : SharedState.SharedState
      ShouldQuit : bool
      Gameplay : GameplayState.GameplayState }

let init (debugDraw: DebugDraw.DebugDraw) : ScreenManager =
    { Active = ScreenKind.MainMenu
      Transition = ScreenTransition.None
      Shared = SharedState.create ()
      ShouldQuit = false
      Gameplay = GameplayState.create debugDraw }

// Advance the active screen; may set a transition. The frame carries dt plus
// event-only input (wheel); pollable input is read from SDL state by screens.
let update (frame: Frame) (m: ScreenManager) : ScreenManager =
    match m.Active with
    | ScreenKind.MainMenu ->
        { m with Transition = MainMenuScreen.update m.Shared }
    | ScreenKind.Gameplay ->
        let struct (nextState, transition) = GameplayScreen.update m.Shared m.Gameplay frame
        { m with Gameplay = nextState; Transition = transition }

// Draw the active screen; may set a transition. A draw request (e.g. a clicked
// button) overrides an update request from earlier in the same frame.
let draw (m: ScreenManager) : ScreenManager =
    match m.Active with
    | ScreenKind.MainMenu ->
        let transition = MainMenuScreen.draw m.Shared
        { m with Transition = Transition.orElse m.Transition transition }
    | ScreenKind.Gameplay ->
        let struct (nextState, transition) = GameplayScreen.draw m.Shared m.Gameplay
        { m with Gameplay = nextState; Transition = Transition.orElse m.Transition transition }

// A clear-only render pass for screens that draw nothing in the 3D scene.
// Begins the render pass (GPULoadOp.Clear) and ends it without drawing, so the
// swapchain is reset before ImGui draws on top of it.
let private clearOnly (pass: ScenePass) : unit =
    let scenePass = SDL.BeginGPURenderPass(pass.Cmd, ReadOnlySpan<SDL.GPUColorTargetInfo>(pass.ColorTargets), 1u, IntPtr.Zero)
    do SDL.EndGPURenderPass(scenePass)

// Render the active screen's 3D scene. UI-only screens have no scene, but the
// swapchain must still be cleared every frame — otherwise the previous frame's
// pixels linger and ImGui layers over them (window trails, stale screens).
let renderScene (m: ScreenManager) (pass: ScenePass) : unit =
    match m.Active with
    | ScreenKind.MainMenu -> clearOnly pass
    | ScreenKind.Gameplay -> GameplayScreen.renderScene m.Gameplay pass

// Apply a pending transition at end of frame. Switching activates the new
// screen (re-syncing its per-screen state); Quit sets ShouldQuit, which the
// engine's loop condition checks.
let applyTransition (m: ScreenManager) : ScreenManager =
    match m.Transition with
    | ScreenTransition.None -> m
    | ScreenTransition.Quit ->
        { m with Transition = ScreenTransition.None; ShouldQuit = true }
    | ScreenTransition.Next next ->
        let m = { m with Active = next; Transition = ScreenTransition.None }
        match next with
        | ScreenKind.MainMenu -> m
        | ScreenKind.Gameplay -> { m with Gameplay = GameplayScreen.activate m.Shared m.Gameplay }
