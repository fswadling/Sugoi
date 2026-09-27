# Contract: Prompt Display

The surfaces that change, and what explicitly does not. There is no external interface; the
"contract" here is the internal surface between the screen, the prompt view and the 2D layer.

## `render/Ui.fs` (changed — one constant only)

```fsharp
// CHANGED: 2 -> 3. One slot per kind of string the layer may draw: a dialogue's name plate (0),
// its line (1) and the interact prompt (2). The prompt and a line are mutually exclusive today
// (FR-012), but the capacity must not depend on that. No signature changes.
let private textSlots = 3
```

Nothing else in the layer changes: no function is added or altered, no field, no shader.

## `world/PromptRender.fs` (new)

```fsharp
/// The glyph-atlas measurements and draw the prompt uses. It knows `Ui` and the viewport only —
/// nothing of triggers, dialogue or the editor; the screen decides the string.
module PromptRender

/// A fraction of the viewport, so the prompt stays on screen at any size (FR-007).
val private bottomMargin : viewportH:float32 -> float32
/// The widest the prompt is laid out before it wraps, so a long one stays on screen.
val private maxWidth : viewportW:float32 -> float32

/// The prompt's text block at this viewport: centred horizontally, above the bottom margin. Pure,
/// so it is asserted headlessly. `textW`/`textH` are the measured size of the wrapped string.
val layout : viewportW:float32 -> viewportH:float32 -> textW:float32 -> textH:float32 -> Ui.Rect

/// The prompt's palette: a near-white text over a semi-transparent near-black shadow, drawn
/// offset, so the prompt is legible against any scene content without a panel (FR-008).
[<Struct>]
type private PromptColours = { Text : Vector4; Shadow : Vector4 }

/// Draw `prompt` as one centred line near the bottom of the viewport. A no-op when `prompt` is
/// empty, so nothing is measured or emitted (FR-005, FR-009). Measures through the layer's text
/// slot cache (slot 2), so a stable prompt is measured once and nothing allocates per frame
/// (FR-010). The shadow and the text share the atlas texture and sampler, so they coalesce into a
/// single draw run.
val draw :
    prompt:string -> viewportW:float32 -> viewportH:float32 -> byref<Ui.Ui> -> unit
```

- **Pure until the `Ui` calls.** `layout` is a function of the viewport and the measured size; it is
  what the headless test asserts. `draw` measures (through `Ui.setLine` on slot 2), lays out, and
  emits two `Ui.text` calls (shadow then text).
- **One input, one output.** It takes a string, not a trigger: *which* prompt is the screen's job.

## `screen/GameplayScreen.fs` (changed)

```fsharp
// In `draw`, after `Ui.beginBatch` and `DialogueRender.draw`:

// Resolve the prompt from the nearest interactable trigger the player is inside (spec 029's
// `Systems.updateTriggers` already chose it), while playing and only when no line is up: a
// dialogue line takes the screen (FR-001, FR-012). Explicit match, not `ValueOption.bind`, so
// nothing closes over `zone` and nothing allocates.
let prompt =
    if state.Editing || state.Dialogue.Shown then ""
    else
        match state.InteractableTrigger with
        | ValueSome ref ->
            match Zone.tryTriggerAt zone ref with
            | ValueSome trigger -> trigger.State.Prompt
            | ValueNone -> ""
        | ValueNone -> ""

do PromptRender.draw prompt viewport.x viewport.y &state.Ui
```

- **Removed**: `drawTriggerOverlay` (the ImGui `Status` window) and its `anyInside` helper, and the
  `if state.Editing then … else …` branch that called them. The prompt is the play-mode affordance now,
  and no debug window appears over the game (FR-006; research Decision 7).
- **Unchanged**: everything else about `draw` and `renderScene`. The prompt is uploaded and drawn by
  the existing `Ui.upload`/`Ui.draw` in the scene pass, as the dialogue is, so it is over the scene and
  never occluded (FR-006).

## What does not change

- **`world/Zone.fs`** — the trigger's `Interactable` flag and `Prompt` text (spec 029) are unchanged,
  and so is `tryTriggerAt`.
- **`world/Systems.fs`** — `updateTriggers` already resolves the nearest interactable trigger (FR-004);
  no change.
- **`world/DialogueRender.fs`** — the dialogue's layout and draw are untouched: the prompt yields to a
  line (FR-012) rather than sharing the bottom strip with it.
- **`render/Ui.fs`'s functions, shaders, pipelines and passes** — no new shader, pipeline or pass; the
  prompt rides the existing 2D layer.
- **`Engine.fs`**, **`Gpu.fs`**, **`Mesh.fs`**, the static path — untouched.
- **Authoring** — no new editor surface; the trigger's prompt is edited exactly as before (spec 029).
