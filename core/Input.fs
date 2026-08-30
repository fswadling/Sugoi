module Input

// The app's input model, shared by the engine, screens, and editor: the `Key`
// abstraction, the per-frame `Input` gathered once by the engine, and the
// engine-owned `PrevInput` used for edge detection. Lives here (not in a
// screen) so everyone can read it without a cycle.

open System
open SDL3

// The editor's key abstraction, independent of SDL. Each case maps to a unique
// bit via `Key.mask`, so gathered keyboard state is a `uint32` bitmask (no
// per-frame allocation). New keys are added as cases here.
type Key =
    | Escape
    | Tab
    | Backtick
    | Delete
    | W | A | S | D | Q | E | F | Z
    | Digit of int
    | LeftCtrl | RightCtrl
    | LeftShift | RightShift

// A set of keys, represented as a `uint32` bitmask (no per-frame allocation).
// The alias is erased at compile time; `KeyMask.zero` avoids ambiguous `0u`
// literals at call sites.
type KeyMask = uint32

module KeyMask =

    let zero : KeyMask = 0u

    let add (a: KeyMask) (b: KeyMask) : KeyMask = a ||| b

module Key =

    // Map a key to its unique bit (1u <<< case index). The `Key` DU stays the
    // readable surface at call sites; masks are an implementation detail.
    let mask (key: Key) : KeyMask =
        match key with
        | Escape -> 1u <<< 0
        | Tab -> 1u <<< 1
        | Backtick -> 1u <<< 2
        | Delete -> 1u <<< 3
        | W -> 1u <<< 4
        | A -> 1u <<< 5
        | S -> 1u <<< 6
        | D -> 1u <<< 7
        | Q -> 1u <<< 8
        | E -> 1u <<< 9
        | F -> 1u <<< 10
        | Z -> 1u <<< 11
        | Digit n -> 1u <<< (12 + (n - 1))
        | LeftCtrl -> 1u <<< 15
        | RightCtrl -> 1u <<< 16
        | LeftShift -> 1u <<< 17
        | RightShift -> 1u <<< 18

// Per-frame input, gathered once by the engine (not by any screen) from SDL
// state. Held/Pressed are `Key` bitmasks (`Key.mask`), so gathering allocates
// nothing per frame.
[<Struct>]
type Input =
    { MouseX : float32
      MouseY : float32
      MouseDX : float32
      MouseDY : float32
      Wheel : float32
      LeftDown : bool
      LeftPressed : bool
      LeftReleased : bool
      RightDown : bool
      Held : KeyMask
      Pressed : KeyMask }

// The previous frame's raw polled input, held by the engine across frames so
// edge-detection (deltas, pressed/released) can be derived from the current
// poll. Engine-owned, not per-screen: one instance advances every frame.
[<Struct>]
type PrevInput =
    { MouseX : float32
      MouseY : float32
      Held : KeyMask
      LeftDown : bool }

module PrevInput =

    let zero : PrevInput = { MouseX = 0.0f; MouseY = 0.0f; Held = KeyMask.zero; LeftDown = false }

// Helpers on the per-frame input.
let isHeld (key: Key) (input: Input) : bool = input.Held &&& Key.mask key <> 0u
let isPressed (key: Key) (input: Input) : bool = input.Pressed &&& Key.mask key <> 0u
let isCtrl (input: Input) : bool = input.Held &&& (Key.mask Key.LeftCtrl ||| Key.mask Key.RightCtrl) <> 0u
let isShift (input: Input) : bool = input.Held &&& (Key.mask Key.LeftShift ||| Key.mask Key.RightShift) <> 0u

// SDL scancode -> `Key` mask. A fixed array (not a dict): the gather path
// scans the whole map each frame, so random access buys nothing.
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

// Gather this frame's input from SDL polled state plus the accumulated wheel,
// and return the advanced previous-frame state alongside the current input.
let gather (prev: PrevInput) (wheel: float32) : struct (PrevInput * Input) =
    let mutable mx = 0.0f
    let mutable my = 0.0f
    let buttons = SDL.GetMouseState(&mx, &my)

    let mutable numKeys = 0
    let keys = SDL.GetKeyboardState(&numKeys)

    let held = computeHeld keys
    let pressed = held &&& ~~~prev.Held
    let leftDown = (int buttons &&& int SDL.MouseButtonFlags.Left) <> 0
    let leftPressed = leftDown && not prev.LeftDown
    let leftReleased = (not leftDown) && prev.LeftDown

    let input =
        { MouseX = mx
          MouseY = my
          MouseDX = mx - prev.MouseX
          MouseDY = my - prev.MouseY
          Wheel = wheel
          LeftDown = leftDown
          LeftPressed = leftPressed
          LeftReleased = leftReleased
          RightDown = (int buttons &&& int SDL.MouseButtonFlags.Right) <> 0
          Held = held
          Pressed = pressed }

    let next = { MouseX = mx; MouseY = my; Held = held; LeftDown = leftDown }
    struct (next, input)
