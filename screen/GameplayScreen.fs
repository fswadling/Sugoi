module GameplayScreen

// Gameplay screen: hosts the editor workspace and the 3D scene (grid). Owns its
// input model and gathering — each screen defines its own input system.

open System
open System.Numerics
open SDL3
open Screen

// SDL scancode -> `Key` mask. A fixed array (not a dict): the gather path scans
// the whole map each frame, so random access buys nothing.
let private keyMap : (int * KeyMask) array =
    [| int SDL.Scancode.Escape, Key.mask Key.Escape
       int SDL.Scancode.Tab, Key.mask Key.Tab
       int SDL.Scancode.Grave, Key.mask Key.Backtick
       int SDL.Scancode.Delete, Key.mask Key.Delete
       int SDL.Scancode.W, Key.mask Key.W
       int SDL.Scancode.A, Key.mask Key.A
       int SDL.Scancode.S, Key.mask Key.S
       int SDL.Scancode.D, Key.mask Key.D
       int SDL.Scancode.Q, Key.mask Key.Q
       int SDL.Scancode.E, Key.mask Key.E
       int SDL.Scancode.F, Key.mask Key.F
       int SDL.Scancode.Z, Key.mask Key.Z
       int SDL.Scancode.Alpha1, Key.mask (Key.Digit 1)
       int SDL.Scancode.Alpha2, Key.mask (Key.Digit 2)
       int SDL.Scancode.Alpha3, Key.mask (Key.Digit 3)
       int SDL.Scancode.LCtrl, Key.mask Key.LeftCtrl
       int SDL.Scancode.RCtrl, Key.mask Key.RightCtrl
       int SDL.Scancode.LShift, Key.mask Key.LeftShift
       int SDL.Scancode.RShift, Key.mask Key.RightShift |]

// Build the held-key bitmask from the current SDL keyboard state.
let private computeHeld (keys: ReadOnlySpan<bool>) : KeyMask =
    let mutable held = KeyMask.zero
    for (scancode, mask) in keyMap do
        if keys.[scancode] then held <- held ||| mask
    held

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
        PrevHeld = computeHeld keys }

// Gather this frame's input from SDL state plus the frame's event data, and
// advance the edge-detection fields (Prev*) in the returned state.
let private gatherInput (state: GameplayState.GameplayState) (frame: Frame) : struct (GameplayState.GameplayState * Input) =
    let mutable mx = 0.0f
    let mutable my = 0.0f
    let buttons = SDL.GetMouseState(&mx, &my)

    let mutable numKeys = 0
    let keys = SDL.GetKeyboardState(&numKeys)

    let held = computeHeld keys
    let pressed = held &&& ~~~state.PrevHeld

    let input =
        { MouseDX = mx - state.PrevMouseX
          MouseDY = my - state.PrevMouseY
          Wheel = frame.Wheel
          RightDown = (int buttons &&& int SDL.MouseButtonFlags.Right) <> 0
          Held = held
          Pressed = pressed }

    let state =
        { state with PrevMouseX = mx; PrevMouseY = my; PrevHeld = held }

    struct (state, input)

// Apply orbit/dolly/fly to the camera from this frame's input. Fly speed is
// per second (dt-scaled); orbit and dolly are event-driven and unscaled.
let private applyCamera (state: GameplayState.GameplayState) (input: Input) (dt: float32) : GameplayState.GameplayState =
    let camera =
        if not input.RightDown then state.Camera else
        let c = Camera.orbit state.Camera (-input.MouseDX * 0.01f) (-input.MouseDY * 0.01f)
        let moving =
            Input.isHeld Key.W input || Input.isHeld Key.A input || Input.isHeld Key.S input
            || Input.isHeld Key.D input || Input.isHeld Key.Q input || Input.isHeld Key.E input
        if not moving then c else
        let f = Camera.forward c
        let r = Camera.right c
        let up = Vector3.UnitY
        let speed = 6.0f * dt
        let mutable delta = Vector3.Zero
        if Input.isHeld Key.W input then delta <- delta + f * speed
        if Input.isHeld Key.S input then delta <- delta - f * speed
        if Input.isHeld Key.D input then delta <- delta + r * speed
        if Input.isHeld Key.A input then delta <- delta - r * speed
        if Input.isHeld Key.E input then delta <- delta + up * speed
        if Input.isHeld Key.Q input then delta <- delta - up * speed
        Camera.pan c delta

    let camera =
        if input.Wheel = 0.0f then camera else
        Camera.dolly camera (MathF.Pow(0.9f, input.Wheel))

    { state with Camera = camera }

// Advance non-render logic: gather input, toggle the editor on backtick, apply
// editor shortcuts (Escape -> menu, Ctrl+Tab panel cycling), then the camera.
let update (_shared: SharedState.SharedState) (state: GameplayState.GameplayState) (frame: Frame) : struct (GameplayState.GameplayState * ScreenTransition) =
    let struct (state, input) = gatherInput state frame

    // Backtick toggles edit/play, unless the user is typing in a text field.
    let state =
        if Input.isPressed Key.Backtick input && not (ImGui.wantTextInput()) then
            { state with Editing = not state.Editing }
        else state

    let struct (state, transition) = Editor.handleShortcuts state input

    let state =
        if state.Editing then applyCamera state input frame.Dt
        else state

    struct (state, transition)

// Draw the editor workspace (when editing) and always emit the reference grid
// so the scene stays visible in both edit and play modes.
let draw (shared: SharedState.SharedState) (state: GameplayState.GameplayState) : struct (GameplayState.GameplayState * ScreenTransition) =
    let struct (state, transition) =
        if state.Editing
        then Editor.draw state shared
        else struct (state, ScreenTransition.None)

    let state = Viewport.drawGrid state

    struct (state, transition)

// Render the 3D scene: upload the grid (copy pass), then begin the scene
// render pass (which clears the swapchain), draw the grid, and end the pass.
let renderScene (state: GameplayState.GameplayState) (pass: ScenePass) : unit =
    do DebugDraw.upload state.DebugDraw pass.Cmd
    let scenePass = SDL.BeginGPURenderPass(pass.Cmd, ReadOnlySpan<SDL.GPUColorTargetInfo>(pass.ColorTargets), 1u, IntPtr.Zero)
    do DebugDraw.draw state.DebugDraw (Camera.viewProjection state.Camera) pass.Cmd scenePass
    do SDL.EndGPURenderPass(scenePass)
