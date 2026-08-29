module GameplayScreen

// Gameplay screen: hosts the editor workspace and the 3D scene (grid). Owns its
// input model and gathering — each screen defines its own input system.

open System
open System.Numerics
open SDL3
open Screen

// Gameplay-specific per-frame input, gathered from SDL state.
[<Struct>]
type Input =
    { MouseDX : float32
      MouseDY : float32
      Wheel : float32
      RightDown : bool
      WDown : bool
      ADown : bool
      SDown : bool
      DDown : bool
      QDown : bool
      EDown : bool
      BacktickPressed : bool }

// Re-sync edge-detection state on switch-in so input held across the
// transition doesn't register as a fresh press or a mouse jump.
let activate (_shared: SharedState.SharedState) (state: GameplayState.GameplayState) : GameplayState.GameplayState =
    let mutable mx = 0.0f
    let mutable my = 0.0f
    do SDL.GetMouseState(&mx, &my) |> ignore

    let mutable numKeys = 0
    let keys = SDL.GetKeyboardState(&numKeys)

    { state with
        PrevMouseX = mx
        PrevMouseY = my
        PrevBacktick = keys.[int SDL.Scancode.Grave] }

// Gather this frame's input from SDL state plus the frame's event data, and
// advance the edge-detection fields (Prev*) in the returned state.
let private gatherInput (state: GameplayState.GameplayState) (frame: Frame) : struct (GameplayState.GameplayState * Input) =
    let mutable mx = 0.0f
    let mutable my = 0.0f
    let buttons = SDL.GetMouseState(&mx, &my)

    let mutable numKeys = 0
    let keys = SDL.GetKeyboardState(&numKeys)

    let backtickDown = keys.[int SDL.Scancode.Grave]

    let input =
        { MouseDX = mx - state.PrevMouseX
          MouseDY = my - state.PrevMouseY
          Wheel = frame.Wheel
          RightDown = (int buttons &&& int SDL.MouseButtonFlags.Right) <> 0
          WDown = keys.[int SDL.Scancode.W]
          ADown = keys.[int SDL.Scancode.A]
          SDown = keys.[int SDL.Scancode.S]
          DDown = keys.[int SDL.Scancode.D]
          QDown = keys.[int SDL.Scancode.Q]
          EDown = keys.[int SDL.Scancode.E]
          BacktickPressed = backtickDown && not state.PrevBacktick }

    let state =
        { state with PrevMouseX = mx; PrevMouseY = my; PrevBacktick = backtickDown }

    struct (state, input)

// Apply orbit/dolly/fly to the camera from this frame's input. Fly speed is
// per second (dt-scaled); orbit and dolly are event-driven and unscaled.
let private applyCamera (state: GameplayState.GameplayState) (input: Input) (dt: float32) : GameplayState.GameplayState =
    let camera =
        if not input.RightDown then state.Camera else
        let c = Camera.orbit state.Camera (-input.MouseDX * 0.01f) (-input.MouseDY * 0.01f)
        let moving =
            input.WDown || input.ADown || input.SDown || input.DDown || input.QDown || input.EDown
        if not moving then c else
        let f = Camera.forward c
        let r = Camera.right c
        let up = Vector3.UnitY
        let speed = 6.0f * dt
        let mutable delta = Vector3.Zero
        if input.WDown then delta <- delta + f * speed
        if input.SDown then delta <- delta - f * speed
        if input.DDown then delta <- delta + r * speed
        if input.ADown then delta <- delta - r * speed
        if input.EDown then delta <- delta + up * speed
        if input.QDown then delta <- delta - up * speed
        Camera.pan c delta

    let camera =
        if input.Wheel = 0.0f then camera else
        Camera.dolly camera (MathF.Pow(0.9f, input.Wheel))

    { state with Camera = camera }

// Advance non-render logic: gather input, toggle the editor on backtick, then
// apply camera navigation when editing.
let update (_shared: SharedState.SharedState) (state: GameplayState.GameplayState) (frame: Frame) : struct (GameplayState.GameplayState * ScreenTransition) =
    let struct (state, input) = gatherInput state frame

    let state =
        if input.BacktickPressed then { state with Editing = not state.Editing }
        else state

    let state =
        if state.Editing then applyCamera state input frame.Dt
        else state

    struct (state, ScreenTransition.None)

// Draw the editor workspace (when editing) and always emit the reference grid
// so the scene stays visible in both edit and play modes.
let draw (shared: SharedState.SharedState) (state: GameplayState.GameplayState) : struct (GameplayState.GameplayState * ScreenTransition) =
    let struct (state, transition) =
        if state.Editing
        then Editor.draw state shared
        else struct (state, ScreenTransition.None)

    let state = Viewport.drawGrid state

    struct (state, transition)

// Render the 3D scene: upload the grid (copy pass), then draw it in a scene
// render pass that clears the swapchain (the engine's ImGui pass follows).
let renderScene (state: GameplayState.GameplayState) (pass: ScenePass) : unit =
    do DebugDraw.upload state.DebugDraw pass.Cmd
    let scenePass = SDL.BeginGPURenderPass(pass.Cmd, ReadOnlySpan<SDL.GPUColorTargetInfo>(pass.ColorTargets), 1u, IntPtr.Zero)
    do DebugDraw.draw state.DebugDraw (Camera.viewProjection state.Camera) pass.Cmd scenePass
    do SDL.EndGPURenderPass(scenePass)
