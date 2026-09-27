module PromptRender

// The interact prompt's view (spec 039): it draws one line of text — a trigger's prompt while the
// player is inside it — centred near the bottom of the screen, with a shadow for contrast. It is the
// second consumer of the game's 2D layer (`render/Ui.fs`, spec 038), after the dialogue panel, and
// is like `DialogueRender` deliberately blind to where its text came from: it takes a string, not a
// trigger. The screen decides *which* prompt to show.
//
// The layout arithmetic is pure, so it is asserted headlessly (`test/PromptTests.fs`); the measuring
// and the drawing need the font and a device.

open System.Numerics

// ── layout (pure) ──

// Fractions of the viewport, so the prompt stays on screen at any size (FR-007).
let private bottomMarginRatio = 0.02f
let private maxWidthRatio = 0.80f

let private bottomMargin (viewportH: float32) : float32 = viewportH * bottomMarginRatio

/// The widest the prompt is laid out before it wraps, so a long one stays on screen rather than
/// running off an edge.
let private maxWidth (viewportW: float32) : float32 = viewportW * maxWidthRatio

/// The prompt's text block at this viewport: centred horizontally and sitting above the bottom
/// margin. Pure, so it is asserted headlessly; `textW`/`textH` are the measured size of the wrapped
/// string. Every figure is a fraction of the viewport, so the block stays on screen at any size
/// (FR-007).
let layout (viewportW: float32) (viewportH: float32) (textW: float32) (textH: float32) : Ui.Rect =
    { X = max 0.0f ((viewportW - textW) * 0.5f)
      Y = max 0.0f (viewportH - bottomMargin viewportH - textH)
      W = textW
      H = textH }

// ── presentation ──

// A glyph-atlas slot of its own, so the prompt is measured alongside (never instead of) a dialogue's
// name plate and line (spec 038's `textSlots`).
let private promptSlot = 2

// A pixel or two down-right: enough to read the text off any background without looking heavy.
let private shadowOffset = 2.0f

/// The prompt's palette: a near-white text over a semi-transparent near-black shadow, so the prompt
/// is legible against any scene content without a panel (FR-008).
[<Struct>]
type private PromptColours =
    { Text : Vector4
      Shadow : Vector4 }

let private colours : PromptColours =
    { Text = Vector4(0.96f, 0.96f, 0.96f, 1.0f)
      Shadow = Vector4(0.0f, 0.0f, 0.0f, 0.65f) }

/// Draw `prompt` as one centred line near the bottom of the viewport. A no-op when `prompt` is empty,
/// so nothing is measured or emitted (FR-005, FR-009). It measures through the layer's slot cache, so
/// a stable prompt is measured once and the draw path allocates nothing (FR-010); the shadow and the
/// text share the glyph atlas texture and sampler, so they coalesce into a single draw run.
let draw (prompt: string) (viewportW: float32) (viewportH: float32) (ui: byref<Ui.Ui>) : unit =
    if prompt = "" then () else

    let wrapWidth = int (max 1.0f (maxWidth viewportW))
    let (textW, textH) = Ui.setLine &ui promptSlot prompt wrapWidth
    let geom = ui.TextGeoms.[promptSlot]
    let rect = layout viewportW viewportH (float32 textW) (float32 textH)
    let shadow : Ui.Rect = { rect with X = rect.X + shadowOffset; Y = rect.Y + shadowOffset }
    do Ui.text &ui shadow geom colours.Shadow
    do Ui.text &ui rect geom colours.Text
