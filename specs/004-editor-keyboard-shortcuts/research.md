# Research: Editor Keyboard Shortcuts

Port of `ocaml-game`'s shortcut handling (`lib/screen/screen.ml`,
`lib/engine.ml`, `lib/editor/editor.ml`) to fsharp-game's per-frame input poll.

## Decision 1 — Key representation: a `Key` DU surfaced as a `uint` bitmask

- **Decision**: add a `Key` discriminated union to `screen/Screen.fs` (`Escape`,
  `Tab`, `Backtick`, `Delete`, `W/A/S/D/Q/E/F/Z`, `Digit of int`, `LeftCtrl`,
  `RightCtrl`, `LeftShift`, `RightShift`) plus a `Key.mask : Key -> uint32` that
  assigns each case a unique bit (`1u <<< case-index`). Gathered state is a
  `uint32` bitmask (`held`, `pressed`), not a `Set<Key>`.
- **Rationale**: the current `GameplayScreen.Input` has one bool field per key
  (`WDown`, `BacktickPressed`, …), which doesn't scale to shortcuts and
  duplicates edge-detection logic per key. A `uint32` bitmask of the `Key` DU
  keeps the readable surface while making the hot gather path allocation-free,
  satisfying SC-004 and Constitution V.
- **Alternatives considered**: per-key bools (rejected — verbose, no clean
  modifier support); `Set<Key>` (rejected — allocates once per frame and would
  leave SC-004 unmet). The bitmask was chosen as the final approach.

## Decision 2 — SDL scancode → Key mapping lives in `GameplayScreen`

- **Decision**: a fixed `(int * Key) array` in `screen/GameplayScreen.fs`, one
  entry per scancode of interest, iterated each frame to build `held`.
- **Rationale**: matches `ocaml-game`'s `engine.ml` `key_map`; keeps `Screen.fs`
  SDL-free. An array is chosen over `readOnlyDict` because the gather path scans
  the *whole* map each frame (no random access), so a dictionary adds indirection
  without benefit.
- **Scancode names (verified against SDL3-CS 3.4.14.1)**: `Escape`, `Tab`,
  `Grave`, `Delete`, `W/A/S/D/Q/E/F/Z`, `Alpha1`/`Alpha2`/`Alpha3` (the top-row
  digits), `LCtrl`/`RCtrl`/`LShift`/`RShift`.

## Decision 3 — The "am I typing" guard is `io.WantTextInput`

- **Decision**: add a context-safe `cimgui_want_text_input()` shim
  (`native/cimgui_sdl3.cpp`) returning `false` when
  `ImGui::GetCurrentContext() == nullptr`, and bind it as
  `ImGui.wantTextInput()`.
- **Rationale**: `ImGuiConfigFlags_NavEnableKeyboard` (already set in
  `cimgui_enable_docking`) makes `io.WantCaptureKeyboard` true whenever nav is
  active — it even auto-focuses the first widget — so it cannot guard app
  shortcuts. `WantTextInput` is true only while typing. Documented in
  `ocaml-game/docs/IMGUI_NOTES.md` ("capture-flag gotcha").
- **Alternatives considered**: `WantCaptureKeyboard` (rejected — the gotcha).

## Decision 4 — Edge detection via `PrevHeld` in `GameplayState`

- **Decision**: replace `GameplayState.PrevBacktick : bool` with
  `PrevHeld : Set<Screen.Key>`; compute `pressed = held - PrevHeld`.
- **Rationale**: generalizes the existing backtick edge detection to every
  mapped key, and keeps the functional "return updated state" style. `activate`
  re-syncs `PrevHeld` so a key held across a screen switch doesn't register as a
  fresh press (the existing `PrevBacktick` re-sync is folded in).
- **Timing note**: `WantTextInput` is updated during `ImGui::NewFrame()`; since
  `update` runs before `newFrame()` it reflects the previous frame — a one-frame
  lag that is fine for gating, and the same ordering `ocaml-game` uses.

## Decision 5 — New shortcuts are the only new actions; the rest are deferred

- **Decision**: implement only `Escape` (menu) and `Ctrl+Tab`/`Ctrl+Shift+Tab`
  (panel cycle) as new editor actions, plus migrate the backtick toggle onto the
  shared mechanism. Tool-switch/snap/focus/delete/duplicate/nudge/undo are
  recorded but deferred to 2c/2d.
- **Rationale**: those deferred shortcuts act on selection/tool/undo state that
  doesn't exist yet; wiring them now would be dead code (violates Minimalism).
- **Panel cycling** reuses `ocaml-game`'s pattern: a `pending_focus` request set
  in update and applied at the start of draw (where `SetWindowFocus` is valid),
  using `Set<Key>`-derived modifier checks. Requires `igSetWindowFocus` +
  `igIsWindowActive` bindings (already exposed by cimgui; no new C++).
