# Implementation Plan: Editor Keyboard Shortcuts

**Branch**: `004-editor-keyboard-shortcuts` | **Date**: 2026-08-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/004-editor-keyboard-shortcuts/spec.md`

## Summary

Add a shared keyboard-shortcut mechanism to the editor: gather held/pressed key
state once per frame from the existing SDL keyboard-state poll, gate app-level
shortcuts on "not typing in a text field", and wire the in-scope shortcuts
(backtick edit/play toggle, `Ctrl+Tab`/`Ctrl+Shift+Tab` panel cycling, `Escape`
to menu). Ports the approach from `ocaml-game` (`lib/editor/editor.ml`,
`lib/engine.ml`, `lib/screen/screen.ml`) onto fsharp-game's existing per-frame
poll in `screen/GameplayScreen.fs`.

## Technical Context

**Language/Version**: F# (.NET 10, `Microsoft.NET.Sdk`)

**Primary Dependencies**: SDL3-CS (`SDL.GetKeyboardState`), the custom cimgui
binding (`render/ImGui.fs` + `render/ImGuiSdl3.fs` + native `libcimgui.so`); no
new runtime dependencies.

**Storage**: N/A (transient per-frame input state only).

**Testing**: manual via `dotnet run` (UI is visual); pure input/state helpers are
unit-testable.

**Target Platform**: Linux x64 desktop (native).

**Project Type**: native desktop game.

**Performance Goals**: 60 fps; no per-frame allocation in the input gather path
(Constitution V).

**Constraints**: keep the gather path allocation-free; `GameplayState` is a
`[<Struct>]`; editor stays a layer that reads/mutates gameplay state without
owning it.

**Scale/Scope**: one key→action table, one input-state struct, three shortcuts.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Minimalism** — PASS: a single `Key` DU + one fixed key map + three
  shortcut handlers; no speculative shortcut framework.
- **II. Fail Loudly** — PASS: unknown SDL scancodes are simply unmapped (not
  errors); the ImGui guard getter is context-safe (returns false when no context)
  so headless tests don't crash.
- **III. Spec-First** — PASS: derives from `spec.md`.
- **IV. Data-Oriented** — PASS: key state is a small struct/record of sets; no
  class hierarchy.
- **V. Minimise GC Pressure** — PASS: the hot gather path builds a `uint32`
  bitmask (no per-frame allocation); `held`/`pressed` are value types.

## Project Structure

### Documentation (this feature)

```text
specs/004-editor-keyboard-shortcuts/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
screen/Screen.fs           # add `Key` DU (leaf module, no deps)
screen/GameplayState.fs    # replace `PrevBacktick` with `PrevHeld : Set<Key>`
screen/GameplayScreen.fs   # key map + gather held/pressed + dispatch shortcut intent
editor/Editor.fs           # shortcut handlers (cycle panel, escape) + want-text-input gate
render/ImGui.fs            # bind `wantTextInput` (and setWindowFocus/isWindowActive)
native/cimgui_sdl3.cpp     # add `cimgui_want_text_input` context-safe getter
```

**Structure Decision**: `Screen.fs` is already the leaf shared-type module (it
holds `ScreenKind`/`ScreenTransition`/`Frame`), so `Key` lives there to avoid a
screen↔editor cycle. The key map and gather stay in `GameplayScreen` (the
behaviour that owns input), matching `ocaml-game`'s `engine.ml` key map +
`editor.ml` shortcut dispatch.

## Complexity Tracking

No constitution violations to justify.

## Design decisions (Phase 0 → research.md)

- Use `io.WantTextInput` (not `WantCaptureKeyboard`) as the "am I typing" guard,
  because `NavEnableKeyboard` makes `WantCaptureKeyboard` true whenever nav is
  active (ocaml-game `docs/IMGUI_NOTES.md`). Add a context-safe `want_text_input`
  shim returning `false` when `ImGui::GetCurrentContext()` is null.
- Represent held/pressed as a `uint32` bitmask over a small `Key` DU
  (`Key.mask`), keeping the gather path allocation-free; derive `pressed` as
  `held &&& ~~~PrevHeld`. Re-sync `PrevHeld` on screen switch-in (`activate`) so
  a key held across a transition doesn't read as a fresh press.
- Reuse the existing `SDL.GetKeyboardState` poll rather than event-driven key
  delivery; edge detection mirrors the current `BacktickPressed` pattern,
  generalized to all mapped keys.
