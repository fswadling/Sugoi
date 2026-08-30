# Contract: Editor Keyboard Shortcuts

The public surfaces added by this feature. Signatures are illustrative F#, not
the implementation.

## Key (`screen/Screen.fs`)

```fsharp
type Key =
    | Escape | Tab | Backtick | Delete
    | W | A | S | D | Q | E | F | Z
    | Digit of int
    | LeftCtrl | RightCtrl | LeftShift | RightShift

// Each case maps to a unique bit (1u <<< case-index); masks are an
// implementation detail kept out of call sites.
mask : Key -> uint32
```

## ImGui capture guard (`render/ImGui.fs`)

```fsharp
// True only while a text field has keyboard focus (io.WantTextInput).
// Context-safe: returns false when ImGui has no current context.
wantTextInput : unit -> bool

// Panel focus (used by Ctrl+Tab cycling).
setWindowFocus : string -> unit
isWindowActive : string -> bool
```

## Input gather (`screen/GameplayScreen.fs`)

```fsharp
// Per-frame input: mouse deltas + wheel + button state, plus the derived
// key bitmasks. `Pressed` is edge-triggered (true only on the first frame a
// key is down).
type Input =
    { MouseDX : float32; MouseDY : float32; Wheel : float32; RightDown : bool
      Held : uint32
      Pressed : uint32 }

// helpers
isHeld      : Key -> Input -> bool
isPressed   : Key -> Input -> bool
isCtrl      : Input -> bool   // LeftCtrl or RightCtrl held
isShift     : Input -> bool   // LeftShift or RightShift held
```

## Editor shortcut handlers (`editor/Editor.fs`)

```fsharp
// Apply one frame of app-level shortcuts. No-ops when a text field has focus
// (ImGui.wantTextInput()). Returns the updated state and any navigation request.
handleShortcuts : GameplayState.GameplayState -> Input -> struct (GameplayState.GameplayState * ScreenTransition)
```

## Rules

- `Screen.fs` stays SDL-free; the SDL scancode→`Key` mapping lives in
  `GameplayScreen`.
- `pressed = held &&& ~~~PrevHeld`; `PrevHeld` is re-synced by `GameplayScreen.activate`
  on screen switch-in.
- App shortcuts check `ImGui.wantTextInput()` before firing; `WantCaptureKeyboard`
  is not used (nav-enable makes it unreliable).
- `SetWindowFocus` is applied at the start of `draw` (after `ImGui.NewFrame`),
  not during `update`.
- The gather path is allocation-free: `held`/`pressed` are `uint32` bitmasks
  over the `Key` DU; no `Set`/list is built per frame.
