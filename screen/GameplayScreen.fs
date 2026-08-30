module GameplayScreen

// Gameplay screen: hosts the editor workspace and the 3D scene (grid). Owns its
// input model and gathering — each screen defines its own input system.

open System
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
    let buttons = SDL.GetMouseState(&mx, &my)

    let mutable numKeys = 0
    let keys = SDL.GetKeyboardState(&numKeys)

    let size = ImGui.displaySize()
    let aspect = if size.y > 0.0f then size.x / size.y else state.Camera.Aspect

    { state with
        PrevMouseX = mx
        PrevMouseY = my
        PrevHeld = computeHeld keys
        PrevLeftDown = (int buttons &&& int SDL.MouseButtonFlags.Left) <> 0
        WindowW = size.x
        WindowH = size.y
        Camera = { state.Camera with Aspect = aspect } }

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
    let leftDown = (int buttons &&& int SDL.MouseButtonFlags.Left) <> 0
    let leftPressed = leftDown && not state.PrevLeftDown
    let leftReleased = (not leftDown) && state.PrevLeftDown

    let input =
        { MouseX = mx
          MouseY = my
          MouseDX = mx - state.PrevMouseX
          MouseDY = my - state.PrevMouseY
          Wheel = frame.Wheel
          LeftDown = leftDown
          LeftPressed = leftPressed
          LeftReleased = leftReleased
          RightDown = (int buttons &&& int SDL.MouseButtonFlags.Right) <> 0
          Held = held
          Pressed = pressed }

    // window size from ImGui (drives the raycast + keeps the camera aspect in
    // sync so the grid and picking agree on resize).
    let size = ImGui.displaySize()
    let aspect = if size.y > 0.0f then size.x / size.y else state.Camera.Aspect

    let state =
        { state with
            PrevMouseX = mx
            PrevMouseY = my
            PrevHeld = held
            PrevLeftDown = leftDown
            WindowW = size.x
            WindowH = size.y
            Camera = { state.Camera with Aspect = aspect } }

    struct (state, input)

// Advance non-render logic: gather input, then hand the frame to the editor,
// which owns the edit/play toggle, shortcuts, nudge, interaction, and camera.
let update (_shared: SharedState.SharedState) (state: GameplayState.GameplayState) (frame: Frame) : struct (GameplayState.GameplayState * ScreenTransition) =
    let struct (state, input) = gatherInput state frame
    Editor.update state input frame.Dt

// Draw the editor workspace (when editing) and always emit the reference grid
// so the scene stays visible in both edit and play modes.
let draw (shared: SharedState.SharedState) (state: GameplayState.GameplayState) : struct (GameplayState.GameplayState * ScreenTransition) =
    let struct (state, transition) =
        if state.Editing
        then Editor.draw state shared
        else struct (state, ScreenTransition.None)

    let state = Viewport.drawGrid state
    let state = if state.Editing then Viewport.buildLines state else state

    struct (state, transition)

// Render the 3D scene: upload the grid (copy pass), then begin the scene
// render pass (which clears the swapchain), draw the grid, and end the pass.
let renderScene (state: GameplayState.GameplayState) (pass: ScenePass) : unit =
    do DebugDraw.upload state.DebugDraw pass.Cmd
    let scenePass = SDL.BeginGPURenderPass(pass.Cmd, ReadOnlySpan<SDL.GPUColorTargetInfo>(pass.ColorTargets), 1u, IntPtr.Zero)
    do DebugDraw.draw state.DebugDraw (Camera.viewProjection state.Camera) pass.Cmd scenePass
    do SDL.EndGPURenderPass(scenePass)
