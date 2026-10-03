# Contract: Screen Fade

The surfaces this feature adds or changes, and what explicitly does not change.

## `state/GameplayState.fs` — CHANGED

```fsharp
mutable Fade : float32   // 0 = untouched, 1 = fully black; screen-scoped, runtime-only
```

**Contract**: one scalar on the struct state, defaulting to `0.0f`. It is the screen's, not a zone's, so a
zone switch leaves it unchanged; it is not authored and not persisted (FR-001, SC-005).

## `screen/GameplayScreen.fs` — CHANGED

```fsharp
let fadeOverlay (width: float32) (height: float32) (level: float32) : struct (Ui.Rect * Vector4) voption =
    if level <= 0.0f then ValueNone
    else ValueSome (struct ({ X = 0.0f; Y = 0.0f; W = width; H = height }, Vector4(0.0f, 0.0f, 0.0f, level)))
```

and, in `draw`, one commit after the dialogue and prompt:

```fsharp
match fadeOverlay viewport.x viewport.y state.Fade with
| ValueSome (struct (overlay, colour)) -> Ui.rect &state.Ui overlay colour
| ValueNone -> ()
```

**Contract**: `fadeOverlay` is pure and total over its inputs, returning a struct value (no allocation). At
level 0 it is `ValueNone`, so the frame is the pre-feature frame; otherwise it is a full-view black quad at
alpha `level`. The quad is the last thing in the 2D-layer batch, so it sits above the dialogue and prompt
and below the editor's ImGui panels (FR-002, FR-003, FR-008).

## `render/ImGui.fs` — CHANGED

One binding is added, `sliderFloat`, over the native `igSliderFloat` already shipped in `libcimgui.so`. The
write is in place; no commit is derived from it.

## `editor/MiscPanel.fs` — NEW

```fsharp
let draw (state: byref<GameplayState.GameplayState>) : unit =
    do ImGui.beginWindow("Miscellaneous", System.IntPtr.Zero, 0) |> ignore
    if ImGui.sliderFloat("Fade", &state.Fade, 0.0f, 1.0f, "%.2f", 0) then ()
    do ImGui.endWindow()
```

**Contract**: the panel takes only the gameplay state and writes `state.Fade` in place — no `SharedState`,
no `Command`, no `History` step. It is the home for game-wide, non-entity runtime tuning; the fade is its
first control (FR-005, FR-006).

## `editor/Editor.fs` — CHANGED

`"Miscellaneous"` joins `panelNames`; `MiscPanel.draw` is submitted after `AdventPanel`, so the panel docks
in the right column beside Inspector/Cue/Advent and is reachable by the panel-cycle shortcut (FR-005,
FR-007).

## `native/cimgui_sdl3.cpp`, `native/libcimgui.so` — CHANGED

`DockBuilderDockWindow("Miscellaneous", right)` is added so the window appears in the right node; the
library is rebuilt with `native/build.sh` (FR-005).

## `test/FadeTests.fs` — NEW

**Contract**: asserts the default (`GameplayState.create`'s `Fade` is `0.0f`) and the pure overlay rule —
`fadeOverlay w h 0.0f` is `ValueNone`; at a positive level it is a black quad at that alpha spanning
`(0, 0, w, h)`. No device is opened (FR-003, SC-003).

## Explicitly unchanged

- **`render/Ui.fs`** — the batch, the pipeline and `rect` are reused as they are.
- **`state/Command.fs`, `state/History.fs`** — the fade is not an authored edit, so no command is added.
- **`world/Cue.fs`, `world/CueEngine.fs`** — the cue ops `FadeOut`/`FadeIn` are a later feature; this
  feature adds the field they will drive, not the ops.
- **`screen/ScreenManager.fs`, `Engine.fs`** — drawing the screen's overlay needs no manager change.
- **The main menu** — untouched; the fade is a gameplay-screen effect (FR-004).
- **The zone model** — the fade is not zone data.
