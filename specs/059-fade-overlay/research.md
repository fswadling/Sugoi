# Phase 0 Research: Screen Fade

The reference already answers most of this: `odin-game`'s gameplay screen carries a `fade: f32` and draws it
as a full-window black rectangle. The decisions here are about where the value lives in *this* port, how the
overlay composes with the existing 2D layer and the ImGui editor, and how much of it is assertable without a
device.

## Decision 1 — A live, screen-scoped scalar on `GameplayState`

- **Decision**: the fade level is `mutable Fade : float32` on `GameplayState`, defaulting to `0.0f`. It is
  screen-scoped and runtime-only: a zone switch does not touch it, and it is not authored, persisted, or a
  step on the undo history.
- **Rationale**: it is exactly where the reference keeps it (`screen.fade`) and where this port already
  keeps screen-level runtime values (`Editing`, `Follow`, `PlayTime`). A zone switch swaps `Zone` data, not
  the screen's own fields, so screen-scoping is free. It is not authored project data, so making it undoable
  would put a debug/tuning value on the same history as scene edits — the opposite of what
  `docs/EDITOR_DESIGN.md` D-011 scopes the history to.
- **Alternatives considered**: **a value on `Zone`** — rejected: a fade is a screen/cinematic effect, not a
  property of a place, and the reference keeps it off the zone. **A `float`** — rejected: `float32` is the
  scalar the rendering path consumes and matches the reference; there is no precision need. **Undoable /
  persisted** — rejected: neither the reference nor this port's other runtime screen values are, and a cue
  will drive the value at run time (below).

## Decision 2 — The overlay is a quad in the existing 2D layer, not a new pass

- **Decision**: the fade is drawn as one full-viewport black quad appended to the in-game 2D layer's batch
  (`render/Ui.fs`) in `GameplayScreen.draw`, after the dialogue and prompt. No new pipeline, shader, or
  render pass.
- **Rationale**: the 2D layer is already a blended, depth-less batch drawn last inside the scene pass, on
  top of the 3D scene (`specs/038` FR-011). A screen-space black overlay is the same kind of thing, so it
  reuses the pipeline, the white placeholder texture and the batch. Being last in that batch is also what
  places it above the dialogue/prompt (the fade darkens them, as a cinematic fade should) while leaving it
  below the editor's ImGui panels, which ImGui renders after the whole scene pass — which is what keeps the
  editor usable while tuning (D6).
- **Alternatives considered**: **a dedicated overlay pass / fullscreen quad pipeline** — rejected: a whole
  new pipeline for one blended rectangle the existing layer already draws. **Drawing it in the ImGui
  foreground draw list** — rejected: that would put it above the editor panels and tie a game effect to the
  UI toolkit; the reference draws it in the game's own frame, not its GUI layer.

## Decision 3 — A pure level→overlay rule, so the no-op is assertable headlessly

- **Decision**: the rule "is there an overlay, and what is it" is one pure function,
  `GameplayScreen.fadeOverlay (width) (height) (level) : struct (Ui.Rect * Vector4) voption` — `ValueNone`
  at 0 (and below), otherwise a full-view black quad at alpha `level`. `draw` matches it and emits the quad.
- **Rationale**: the project's rule is that a feature's decision logic is testable with no device (`specs/039`
  does exactly this for the prompt layout). "At 0 the frame is unchanged" and "at 1 the view is fully black"
  are the two failure modes that matter, and both are properties of this pure rule, so they are asserted
  headlessly. It also keeps the per-frame path allocation-free: the result is a struct `voption` of a struct
  tuple (Constitution V).
- **Alternatives considered**: **testing only the default** — rejected: it would leave the overlay's two
  important edges (no-op at 0, full coverage at 1) unasserted. **Asserting the emitted batch** — rejected:
  that needs a `Ui` built against a GPU device.

## Decision 4 — A new right-docked Miscellaneous panel, writing in place

- **Decision**: a new module `editor/MiscPanel.fs` draws the `"Miscellaneous"` window, docked into the
  right node beside Inspector/Cue/Advent through the native layout; it writes `state.Fade` in place and
  records no command. It is added to `Editor.panelNames` and submitted after `AdventPanel`.
- **Rationale**: a panel is not a selection, so it cannot live in the Inspector — the same reason
  `AdventPanel`/`DialoguePanel` are their own modules. It belongs in the right column because that is where
  the non-entity editing surfaces are (`docs/EDITOR_DESIGN.md`, Workspace), and it is the home for game-wide
  runtime tuning rather than a one-off field. Writing in place matches the value's nature (D1) and the
  reference, whose editor writes live state directly.
- **Alternatives considered**: **a field in the Inspector** — rejected: nothing is selected, and the fade
  belongs to no entity or zone. **An "Advent"-style module with a `History.commit` write** — rejected: the
  value is not authored project data (D1, research).

## Decision 5 — Add the `sliderFloat` binding and use a slider

- **Decision**: add an `igSliderFloat` binding to `render/ImGui.fs` and present the level as a slider over
  0..1.
- **Rationale**: the request names a slider and a slider is the fitting control for a bounded 0..1 tuning
  value (a bounded drag reads as one too, but the slider is the intent). `render/ImGui.fs`'s header says the
  binding is grown as widgets are required, and the native library already exports `igSliderFloat`, so this
  is one line with no native rebuild.
- **Alternatives considered**: **reuse `dragFloat`** — viable (its min..max form is slider-like) but it is a
  drag, not a slider, and the control the author asked for is a slider. **A 0–100 integer** — rejected: the
  value's natural domain is the 0..1 the overlay consumes.

## Decision 6 — The editor panels stay above the overlay (a deliberate divergence)

- **Decision**: the overlay covers the rendered game view (scene + in-game 2D layer) but not the editor's
  ImGui panels, which remain fully readable at every level.
- **Rationale**: the reference draws its rectangle over the whole window only because its raylib GUI and its
  game frame share one immediate-mode pass; there is no separate editor layer. In this port the editor is
  ImGui, rendered after the scene pass, so the natural composition is the fade under the panels. That is
  also the behaviour the feature needs: the author tunes the slider *while watching the fade*, which is
  impossible if the fade blacks out the panel. When playing there are no panels, so the fade covers the
  whole view as the reference does.
- **Alternatives considered**: **drawing the overlay in the ImGui foreground list to match the reference's
  full-window coverage** — rejected: it would hide the very control that tunes it and tie a game effect to
  the UI toolkit.

## Decision 7 — Tweening is explicitly a later feature

- **Decision**: this feature sets the level; it does not animate it over a duration.
- **Rationale**: the request is a value that applies a fade and a control to tune it. The reference's
  `FadeOut`/`FadeIn` are cue ops that tween this field over their own duration and are part of the cue op
  family this port has not ported; they will drive this field when they arrive. Adding a timer now would be
  a mechanism with no caller (Constitution I).
- **Alternatives considered**: **an automatic timed fade with a duration** — rejected: no spec asks for it,
  and it would need run state the value does not otherwise have.
