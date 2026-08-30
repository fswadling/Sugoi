module Screen

// Screen kinds, navigation requests, and the per-frame values shared by all
// screens. New screens are added as `ScreenKind` cases here and handled in
// `ScreenManager`.

open SDL3

type ScreenKind =
    | MainMenu
    | Gameplay

// A navigation request produced by a screen and applied by the manager at the
// end of the frame (so the current frame still draws fully).
type ScreenTransition =
    | None
    | Next of ScreenKind
    | Quit

module Transition =

    // Merge the frame's update and draw requests: the later one (draw — e.g.
    // a clicked button) wins; `None` yields to the earlier request.
    let orElse (fallback: ScreenTransition) (t: ScreenTransition) : ScreenTransition =
        match t with
        | ScreenTransition.None -> fallback
        | t -> t

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

// Per-frame input, built by the active screen from SDL state. Held/Pressed are
// `Key` bitmasks (`Key.mask`), so gathering allocates nothing per frame.
// Lives here (not in a screen) so the editor can read it without a cycle.
[<Struct>]
type Input =
    { MouseDX : float32
      MouseDY : float32
      Wheel : float32
      RightDown : bool
      Held : KeyMask
      Pressed : KeyMask }

module Input =

    let isHeld (key: Key) (input: Input) : bool = input.Held &&& Key.mask key <> 0u
    let isPressed (key: Key) (input: Input) : bool = input.Pressed &&& Key.mask key <> 0u
    let isCtrl (input: Input) : bool = input.Held &&& (Key.mask Key.LeftCtrl ||| Key.mask Key.RightCtrl) <> 0u
    let isShift (input: Input) : bool = input.Held &&& (Key.mask Key.LeftShift ||| Key.mask Key.RightShift) <> 0u

// Everything the engine surfaces to screens each frame, built once in Engine.
// Event-only input (the wheel — not pollable as state) is accumulated here
// during the event loop; pollable input is read from SDL state by the screens
// themselves. New inputs extend this record, never a signature.
[<Struct>]
type Frame =
    { Dt : float32
      Wheel : float32 }

// What a screen needs to record its 3D scene pass. Bundles the GPU details so
// they cross one explicit seam instead of riding loose through the manager.
[<Struct>]
type ScenePass =
    { Cmd : nativeint
      ColorTargets : SDL.GPUColorTargetInfo array }
