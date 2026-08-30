# Quickstart: Editor Keyboard Shortcuts

Validation guide for the feature.

## Prerequisites

- `dotnet` SDK (installed).
- Native ImGui library built (`./native/build.sh`, already done; rebuild it if
  the `cimgui_want_text_input` shim is added).

## Build & run

```sh
dotnet build
dotnet run
```

## Validation scenarios

1. **Toggle edit/play** — from the editor, press backtick (`` ` ``); expect the
   editor workspace to hide, leaving the viewport grid visible. Press again to
   restore it.
2. **Return to menu** — press `Escape`; expect the main menu. (If a text field
   has focus, `Escape` should dismiss the field first, not leave the editor.)
3. **Cycle panels** — press `Ctrl+Tab`; expect focus to move to the next docked
   panel (Outliner → Inspector → Asset Browser → wrap). `Ctrl+Shift+Tab` moves
   backward.
4. **No fire while typing** — focus a text field and type a shortcut key; expect
   the character to appear with no shortcut triggered.

## Expected outcome

- Backtick, `Escape`, `Ctrl+Tab`, and `Ctrl+Shift+Tab` all fire reliably.
- Shortcuts never fire while typing in a text field.
- Held keys across a screen transition don't register as a fresh press.
