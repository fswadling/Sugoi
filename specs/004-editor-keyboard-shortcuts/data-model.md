# Data Model: Editor Keyboard Shortcuts

## Key (`core/Input.fs`)

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

`Input` lives in `core/Input.fs`, a leaf module every screen and the editor can
see without a cycle.

## Keyboard input state (`core/Input.fs`)

Derived once per frame by the engine (`Input.gather`) from
`SDL.GetKeyboardState`; the per-frame `Input` and the engine-owned `PrevInput`
(previous frame's raw poll) both live here.

- **Key map** — `(int * uint32) array`, one entry per mapped scancode holding the
  key's bit mask, iterated each frame (no random access; hence an array, not a
  dictionary).
- **`Key.mask : Key -> uint32`** (in `core/Input.fs`) — maps each `Key` case
  to a unique bit (`1u <<< case-index`), so the `Key` DU stays the readable
  surface and masks stay an implementation detail.
- **`held : uint32`** — a bitmask of currently-down keys (OR of the mapped
  scancodes' masks).
- **`pressed : uint32`** — `held &&& ~~~PrevInput.Held` (edge-triggered).
- **modifiers** — derived checks: `ctrl = held &&& (mask LeftCtrl ||| mask RightCtrl) <> 0u`,
  `shift = held &&& (mask LeftShift ||| mask RightShift) <> 0u`.

## GameplayState (`screen/GameplayState.fs`)

No input edge-detection state here: the previous frame's held bitmask
(`PrevInput.Held`, a `uint32`) is engine-owned in `core/Input.fs` and
advances every frame, so no `activate` re-sync is needed.

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
