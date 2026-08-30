# Data Model: Editor Keyboard Shortcuts

## Key (`screen/Screen.fs`)

A leaf discriminated union — the editor's key abstraction, independent of SDL.

```fsharp
type Key =
    | Escape
    | Tab
    | Backtick
    | Delete
    | W | A | S | D | Q | E | F | Z
    | Digit of int          // Digit 1/2/3 (tools) — deferred to 2c
    | LeftCtrl | RightCtrl
    | LeftShift | RightShift

// mask : Key -> uint32 — each case maps to a unique bit (1u <<< case-index).
```

`Screen.fs` already owns `ScreenKind`/`ScreenTransition`/`Frame`; adding `Key`
here keeps the type where every screen and the editor can see it without a cycle.

## Keyboard input state (`screen/GameplayScreen.fs`)

Derived once per frame from `SDL.GetKeyboardState`; stored transiently.

- **Key map** — `(int * uint32) array`, one entry per mapped scancode holding the
  key's bit mask, iterated each frame (no random access; hence an array, not a
  dictionary).
- **`Key.mask : Key -> uint32`** (in `screen/Screen.fs`) — maps each `Key` case
  to a unique bit (`1u <<< case-index`), so the `Key` DU stays the readable
  surface and masks stay an implementation detail.
- **`held : uint32`** — a bitmask of currently-down keys (OR of the mapped
  scancodes' masks).
- **`pressed : uint32`** — `held &&& ~~~PrevHeld` (edge-triggered).
- **modifiers** — derived checks: `ctrl = held &&& (mask LeftCtrl ||| mask RightCtrl) <> 0u`,
  `shift = held &&& (mask LeftShift ||| mask RightShift) <> 0u`.

## GameplayState (`screen/GameplayState.fs`)

- `PrevHeld : uint32` replaces `PrevBacktick : bool` — the previous frame's held
  bitmask, re-synced by `activate` on switch-in. This generalizes the existing
  backtick edge detection. A `uint32` (not a `Set`) keeps the gather path
  allocation-free, satisfying SC-004.

## ImGui capture guard (`render/ImGui.fs` + native shim)

- `wantTextInput : unit -> bool` — `io.WantTextInput`, context-safe (false when
  no ImGui context). The gate every app shortcut checks before firing.
- `setWindowFocus : string -> unit` / `isWindowActive : string -> bool` — used by
  panel cycling.

## Shortcut table (in scope)

| Key(s) | Modifier | Action |
|--------|----------|--------|
| `` ` `` | — | toggle edit/play |
| `Escape` | — | return to main menu |
| `Tab` | Ctrl | focus next docked panel |
| `Tab` | Ctrl+Shift | focus previous docked panel |

## Deferred (recorded, not implemented this feature)

`1/2/3` (tool), `Q` (snap), `F` (focus), `Del` (delete), `Ctrl+D` (duplicate),
`Shift+WASD/QE` (nudge), `Ctrl+Z`/`Ctrl+Shift+Z` (undo/redo) — all require the
selection/transform model (2c) or the command/undo stack (2d).
