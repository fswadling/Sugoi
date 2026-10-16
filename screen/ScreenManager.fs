module ScreenManager

// Owns the active screen and its state; the single source of truth for "which
// screen is active" (FR-007). Dispatches update/draw to the active screen and
// applies a pending transition at the end of the frame. The manager is mutated
// in place each frame through a byref.

open System
open Screen
open SDL3

[<Struct>]
type ScreenManager =
    { mutable Active : ScreenKind
      mutable Transition : ScreenTransition
      Shared : SharedState.SharedState
      mutable ShouldQuit : bool
      mutable Gameplay : GameplayState.GameplayState }

let init (gpu: Gpu.Gpu) (debugDraw: DebugDraw.DebugDraw) (gizmoDraw: DebugDraw.DebugDraw) (ui: Ui.Ui) (skydome: Skydome.Skydome) : ScreenManager =
    let shared = SharedState.create gpu
    // Load the project file once, at startup (spec 095 FR-007), so the author's scenarios are present from the
    // first frame. Not in `GameplayState.create`, which the headless fixtures call — a disk read there would make
    // the whole suite touch the filesystem.
    let mutable gameplay = GameplayState.create debugDraw gizmoDraw ui skydome
    gameplay.Scenarios <- Project.load ()
    { Active = ScreenKind.MainMenu
      Transition = ScreenTransition.None
      Shared = shared
      ShouldQuit = false
      Gameplay = gameplay }

// Advance the active screen; may set a transition. The frame carries the frame
// time, window size, and the engine-gathered input.
let update (frame: Frame) (m: byref<ScreenManager>) : unit =
    match m.Active with
    | ScreenKind.MainMenu ->
        m.Transition <- MainMenuScreen.update m.Shared
    | ScreenKind.Gameplay ->
        m.Transition <- GameplayScreen.update m.Shared frame &m.Gameplay

// Draw the active screen; may set a transition. A draw request (e.g. a clicked
// button) overrides an update request from earlier in the same frame.
let draw (m: byref<ScreenManager>) : unit =
    match m.Active with
    | ScreenKind.MainMenu ->
        let transition = MainMenuScreen.draw m.Shared
        m.Transition <- Transition.orElse m.Transition transition
    | ScreenKind.Gameplay ->
        let transition = GameplayScreen.draw m.Shared &m.Gameplay
        m.Transition <- Transition.orElse m.Transition transition

// A clear-only render pass for screens that draw nothing in the 3D scene.
// Begins the render pass (GPULoadOp.Clear) and ends it without drawing, so the
// swapchain is reset before ImGui draws on top of it.
let private clearOnly (pass: ScenePass) : unit =
    let mutable depthTarget = pass.DepthTarget
    let scenePass = SDL.BeginGPURenderPass(pass.Cmd, ReadOnlySpan<SDL.GPUColorTargetInfo>(pass.ColorTargets), 1u, &depthTarget)
    do SDL.EndGPURenderPass(scenePass)

// Render the active screen's 3D scene. UI-only screens have no scene, but the
// swapchain must still be cleared every frame — otherwise the previous frame's
// pixels linger and ImGui layers over them (window trails, stale screens).
let renderScene (m: inref<ScreenManager>) (pass: ScenePass) : unit =
    match m.Active with
    | ScreenKind.MainMenu -> clearOnly pass
    | ScreenKind.Gameplay -> GameplayScreen.renderScene m.Shared &m.Gameplay pass

// Apply a pending transition at end of frame. Switching activates the new
// screen (re-syncing its per-screen state); Quit sets ShouldQuit, which the
// engine's loop condition checks.
let applyTransition (m: byref<ScreenManager>) : unit =
    match m.Transition with
    | ScreenTransition.None -> ()
    | ScreenTransition.Quit ->
        m.Transition <- ScreenTransition.None
        m.ShouldQuit <- true
    | ScreenTransition.Next next ->
        m.Active <- next
        m.Transition <- ScreenTransition.None
        match next with
        | ScreenKind.MainMenu -> ()
        | ScreenKind.Gameplay ->
            GameplayScreen.activate m.Shared &m.Gameplay
