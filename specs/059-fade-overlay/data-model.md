# Data Model: Screen Fade

## `state/GameplayState.fs` — changed

```fsharp
[<Struct>]
type GameplayState =
    { // ... existing fields ...
      mutable Fade : float32 }   // 0 = untouched game view, 1 = fully black; screen-scoped, runtime-only
```

- **One scalar.** `Fade` is the whole model: the game view is darkened by this much. It is not a zone's, not
  an entity's, and not authored; a zone switch leaves it alone.
- **Default `0.0f`** in `create` — the pre-feature frame.
- **Domain 0..1**: the slider (below) is the only writer, and it cannot leave the range. No clamping is
  modelled (research D1; spec Assumptions).

## `screen/GameplayScreen.fs` — changed

```fsharp
/// The fade overlay for a `width` × `height` view at `level`: no overlay at 0 (the pre-feature frame),
/// otherwise a full-view black quad at alpha `level`. Pure, so the no-op and the full coverage are
/// assertable headlessly.
let fadeOverlay (width: float32) (height: float32) (level: float32) : struct (Ui.Rect * Vector4) voption =
    if level <= 0.0f then ValueNone
    else ValueSome (struct ({ X = 0.0f; Y = 0.0f; W = width; H = height }, Vector4(0.0f, 0.0f, 0.0f, level)))

// in `draw`, after the dialogue and the prompt:
match fadeOverlay viewport.x viewport.y state.Fade with
| ValueSome (struct (overlay, colour)) -> Ui.rect &state.Ui overlay colour
| ValueNone -> ()
```

- **No new pass.** The quad joins the existing 2D-layer batch, so it is drawn last in the scene pass and
  needs no pipeline, shader or texture beyond the layer's white placeholder (research D2).
- **Allocation-free**: the result is a struct `voption` of a struct tuple; at 0 nothing is emitted
  (Constitution V).

## `render/ImGui.fs` — changed

```fsharp
// A bounded slider over a caller-owned float (cimgui's igSliderFloat). Returns true while it is being
// dragged; the write is in place, and this feature records no commit for it.
[<DllImport("cimgui", EntryPoint = "igSliderFloat", CallingConvention = CallingConvention.Cdecl)>]
extern bool sliderFloat(string label, float32& v, float32 min, float32 max, string format, int flags)
```

## `editor/MiscPanel.fs` — new

| Surface | Behaviour |
|---|---|
| `draw (state: byref<GameplayState>)` | `ImGui.beginWindow "Miscellaneous"`; a `sliderFloat "Fade"` over `[0, 1]`, format `"%.2f"`, bound by reference to `state.Fade`; `endWindow`. The write is in place — no `Command`, no `History` step (research D4). |

- **Only `GameplayState` is needed**: the value is the screen's, so the panel takes no `SharedState`, like
  `CueEditor`.

## `editor/Editor.fs` — changed

| Surface | Change |
|---|---|
| `panelNames` | `… "Advent"; "Miscellaneous"; "Zone Registry" …` — reachable by Ctrl+Tab / Ctrl+Shift+Tab |
| `draw` | `MiscPanel.draw &state` submitted after `AdventPanel`, so it is a right-column tab beside the Inspector/Cue/Advent and is the rightmost tab |

## `native/cimgui_sdl3.cpp` — changed (rebuilt into `libcimgui.so`)

`ImGui::DockBuilderDockWindow("Miscellaneous", right);` is added after `"Advent"`, so the window docks into
the right node on a fresh layout. (The tab order within the node still follows submission order —
`Editor.fs` — because `DockBuilderDockWindow` clears `DockOrder`.)

## Validation rules

| Rule | Where it is enforced |
|---|---|
| The level is in 0..1 | the control — a slider over `[0, 1]`; it cannot produce another value |
| At 0 the overlay is a no-op | `fadeOverlay` returns `ValueNone`; `draw` emits nothing |
| At 1 the view is fully black | `fadeOverlay` returns an opaque black quad spanning the viewport |
| The overlay covers the game view but not the editor panels | the quad is last in the 2D-layer batch, which the ImGui editor renders above |
| The level survives a zone switch | it lives on `GameplayState`, which a zone switch does not rebuild |
| The level is never persisted | nothing serializes it; it is not authored and not on the history |

## Deliberately not modelled

- **A timed tween / duration** — the cue ops `FadeOut`/`FadeIn` will drive this field later (research D7).
- **Undo / persistence of the level** — it is live screen tuning, not authored data (research D1).
- **A clamp for out-of-range values** — the control's domain is the contract (spec Assumptions).
- **A fader colour** — the overlay is black, as the reference's `rl.BLACK` is.
